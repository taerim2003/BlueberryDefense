#if UNITY_EDITOR || BOT_QA
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// "예외는 안 났는데 틀린 상태"를 잡는 검사. 어긋나면 `Debug.LogError("[QA-INV] …")` → QAErrorLog가 오류로 기록한다.
// 렉(한 프레임이 FrameSpikeSeconds를 넘음)은 오류가 아니라 `<세션>/perf.jsonl`로 따로 남긴다.
//
// 🔴 검사는 **사람이 볼 수 있는 증상**만 건다(일시정지가 안 풀림·체력 범위·적 누수). 내부 값의 "이래야 할 것 같다"를
//    걸면 설계상 정상인 상태를 오류로 쏟아내서, 진짜 오류가 그 틈에 묻힌다.
public class QAInvariants : MonoBehaviour
{
    private const float CheckInterval = 1f;
    private const float StuckPauseSeconds = 3f;
    private const float FrameSpikeSeconds = 0.25f;
    private const int EnemyLeakCount = 1500;

    private BotPilot pilot;
    private string perfPath;
    private float lastReal, nextCheck, ignoreSpikesUntil, lastSpikeLogged;
    private float timeScaleZeroSince = -1f, titlePausedSince = -1f;
    private readonly HashSet<string> firedThisRun = new HashSet<string>();
    private int enemyPeak, spikes;
    private float worstFrame;

    // 계측기 자신이 렉의 원인인지 가르는 스위치(perf 실험용). 켜면 1초 검사와 렉 상세 기록을 끄고
    // 느린 프레임 목록만 남긴다 — 그래도 렉이 그대로면 계측기는 범인이 아니다.
    private bool lightweight;

    public void Init(BotConfig cfg, BotPilot owner)
    {
        pilot = owner;
        lightweight = cfg.qaLightweight;
        perfPath = Path.Combine(cfg.sessionDir, "perf.jsonl");
        lastReal = Time.realtimeSinceStartup;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    // 씬 로드 프레임의 멈칫은 렉으로 치지 않는다.
    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        ignoreSpikesUntil = Time.realtimeSinceStartup + 1.5f;
        titlePausedSince = -1f;
        timeScaleZeroSince = -1f;
    }

    // 🔴 느린 프레임을 **전수로** 모아 판 끝에 한 번에 쓴다(프레임 중 디스크 I/O가 측정을 오염시키지 않게).
    //    렉이 내용물 때문인지(적·이펙트 수와 함께 커짐) 내용과 무관한 고정 지연인지(같은 길이가 주기적으로 반복)를
    //    가르는 건 이 목록의 **간격**이다. perf.jsonl의 표본만으로는 안 갈린다(2026-09-23).
    private readonly List<(float t, float ms)> slowFrames = new List<(float, float)>();
    private readonly List<(int index, string parts)> slowFrameMarkers = new List<(int, string)>();
    private const float SlowFrameMs = 100f;

    private void DumpSlowFrames()
    {
        if (slowFrames.Count == 0 || string.IsNullOrEmpty(perfPath)) return;
        var byIndex = new Dictionary<int, string>();
        foreach (var m in slowFrameMarkers) byIndex[m.index] = m.parts;
        var lines = new System.Text.StringBuilder();
        for (int i = 0; i < slowFrames.Count; i++)
        {
            var d = BotJson.Obj();
            d["t"] = slowFrames[i].t; d["ms"] = Mathf.RoundToInt(slowFrames[i].ms);
            d["markers"] = byIndex.TryGetValue(i, out string p) ? p : null;
            lines.Append(BotJson.Write(d)).Append('\n');
        }
        slowFrameMarkers.Clear();
        try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(perfPath), "frames.jsonl"), lines.ToString()); }
        catch (Exception) { }
        slowFrames.Clear();
    }

    // 판 하나의 요약(runs.jsonl의 qa). 부르면 비운다.
    public Dictionary<string, object> TakeRunSummary()
    {
        DumpSlowFrames();
        var d = BotJson.Obj();
        d["enemyPeak"] = enemyPeak;
        d["frameSpikes"] = spikes;
        d["worstFrameMs"] = Mathf.RoundToInt(worstFrame * 1000f);
        d["invariants"] = new List<object>(firedThisRun);
        enemyPeak = 0; spikes = 0; worstFrame = 0f;
        firedThisRun.Clear();
        return d;
    }

    private int gcPrev, gcNow;

    // 느린 프레임의 **어디에** 시간이 갔는지: Unity 프로파일러 마커를 프레임마다 읽는다(Development 빌드에서만 값이 찬다).
    // 이름은 Unity가 정한 것이라 없는 것도 있다 — isValid로 걸러서 쓴다.
    private static readonly string[] MarkerNames = {
        "BehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedBehaviourUpdate",
        "Physics2D.Simulate", "Camera.Render", "Gfx.WaitForPresentOnGfxThread", "Gfx.PresentFrame",
        "WaitForTargetFPS", "Semaphore.WaitForSignal", "ParticleSystem.Update", "Canvas.SendWillRenderCanvases",
        "GC.Alloc", "GC.Collect", "Loading.UpdatePreloading", "Application.Integrate Assets in Background",
    };
    private readonly List<(string name, UnityEngine.Profiling.Recorder rec)> markers = new List<(string, UnityEngine.Profiling.Recorder)>();

    private void InitMarkers()
    {
        foreach (string n in MarkerNames)
        {
            var r = UnityEngine.Profiling.Recorder.Get(n);
            if (!r.isValid) continue;
            r.enabled = true;
            markers.Add((n, r));
        }
    }

    private void Update()
    {
        if (markers.Count == 0) InitMarkers();
        float now = Time.realtimeSinceStartup;
        float dt = now - lastReal;
        lastReal = now;
        gcPrev = gcNow;
        gcNow = System.GC.CollectionCount(0);
        // 에디터의 프레임 시간은 에디터 자체 부하라서 게임의 렉이 아니다 — 빌드에서만 잰다.
        if (!Application.isEditor && dt * 1000f > SlowFrameMs && now > ignoreSpikesUntil && slowFrames.Count < 20000)
        {
            slowFrames.Add((now, dt * 1000f));
            // 마커는 **직전 프레임** 값이다 — 느린 프레임 바로 다음 Update에서 읽는 것이라 그 프레임의 분해가 맞다.
            var parts = new List<string>();
            foreach (var m in markers)
            {
                float ms = m.rec.elapsedNanoseconds / 1000000f;
                if (ms >= 5f) parts.Add(m.name + "=" + Mathf.RoundToInt(ms));
            }
            if (parts.Count > 0) slowFrameMarkers.Add((slowFrames.Count - 1, string.Join(" ", parts)));
        }
        if (!Application.isEditor && dt > FrameSpikeSeconds && now > ignoreSpikesUntil) Spike(dt);

        if (lightweight || now < nextCheck) return;
        nextCheck = now + CheckInterval;
        string scene = SceneManager.GetActiveScene().name;
        if (scene == "Battle") CheckBattle(now);
        else if (scene == "Title") CheckTitle(now);
    }

    private void CheckBattle(float now)
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        bool live = !gm.IsGameOver && !gm.IsGameClear && !gm.IsEnding;

        // 모달이 없는데 시간이 멈춰 있다 = 일시정지가 안 풀린 것(사람 눈엔 "게임이 얼었다").
        if (live && Time.timeScale == 0f && !ModalPause.IsPaused)
        {
            if (timeScaleZeroSince < 0f) timeScaleZeroSince = now;
            else if (now - timeScaleZeroSince > StuckPauseSeconds)
                Fire("timescale-stuck", "전투 중 모달이 하나도 없는데 Time.timeScale=0이 " + StuckPauseSeconds + "초 넘게 이어짐");
        }
        else timeScaleZeroSince = -1f;

        PlayerHealth ph = FindAnyObjectByType<PlayerHealth>();
        if (ph != null && (ph.CurrentHealth < 0 || ph.CurrentHealth > ph.MaxHealth))
            Fire("hp-range", "플레이어 체력이 범위를 벗어남: " + ph.CurrentHealth + "/" + ph.MaxHealth);

        int enemies = FindObjectsByType<Enemy>(FindObjectsInactive.Exclude).Length;
        if (enemies > enemyPeak) enemyPeak = enemies;
        if (enemies > EnemyLeakCount) Fire("enemy-leak", "살아 있는 적이 " + enemies + "마리(상한 " + EnemyLeakCount + ")");
    }

    private void CheckTitle(float now)
    {
        // 타이틀엔 게임을 멈추는 모달이 없다 — 여기서 카운트가 남아 있으면 전투에서 Pop이 빠진 것이다.
        if (ModalPause.IsPaused || Time.timeScale != 1f)
        {
            if (titlePausedSince < 0f) titlePausedSince = now;
            else if (now - titlePausedSince > StuckPauseSeconds)
                Fire("title-paused", "타이틀인데 ModalPause=" + ModalPause.IsPaused + " timeScale=" + Time.timeScale
#if BOT_QA
                    + "\n안 닫힌 Push: " + string.Join(" | ", ModalPause.Pushers)
#endif
                    );
        }
        else titlePausedSince = -1f;
    }

    // 같은 검사는 판마다 한 번만 쏜다.
    private void Fire(string key, string message)
    {
        if (!firedThisRun.Add(key)) return;
        Debug.LogError("[QA-INV] " + key + ": " + message);
    }

    private void Spike(float dt)
    {
        spikes++;
        if (dt > worstFrame) worstFrame = dt;
        float now = Time.realtimeSinceStartup;
        if (lightweight) return;
        if (now - lastSpikeLogged < 2f) return; // 연속 렉은 첫 프레임만 적는다
        lastSpikeLogged = now;
        // 🔴 "무엇이 많았나"를 같이 적는다 — 적 수만으로는 원인이 안 갈린다(광역기 타격마다 붙는 피해 숫자·파티클이 후보).
        //    세는 건 비싸지만 렉 프레임에만, 그것도 2초에 한 번만 한다.
        var d = BotJson.Obj();
        d["utc"] = DateTime.UtcNow.ToString("o");
        d["ms"] = Mathf.RoundToInt(dt * 1000f);
        d["scene"] = SceneManager.GetActiveScene().name;
        d["stage"] = GameManager.Instance != null ? GameManager.Instance.CurrentStage : 0;
        d["enemies"] = FindObjectsByType<Enemy>(FindObjectsInactive.Exclude).Length;
        d["damageNumbers"] = FindObjectsByType<DamageNumber>(FindObjectsInactive.Exclude).Length;
        d["hitParticles"] = FindObjectsByType<HitParticle>(FindObjectsInactive.Exclude).Length;
        d["projectiles"] = FindObjectsByType<Projectile>(FindObjectsInactive.Exclude).Length;
        d["whirlwinds"] = FindObjectsByType<Whirlwind>(FindObjectsInactive.Exclude).Length;
        d["particleSystems"] = FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude).Length;
        // 풀에서 나온 오브젝트를 **원본 프리팹별로** 센다 — 무엇이 쌓여서 프레임을 먹는지는 이 목록이 답한다.
        var byPrefab = new Dictionary<string, int>();
        foreach (PooledInstance pi in FindObjectsByType<PooledInstance>(FindObjectsInactive.Exclude))
        {
            string key = pi.SourcePrefab != null ? pi.SourcePrefab.name : "?";
            byPrefab.TryGetValue(key, out int n);
            byPrefab[key] = n + 1;
        }
        var top = new List<object>();
        foreach (var kv in byPrefab.OrderByDescending(x => x.Value).Take(8))
        {
            var e = BotJson.Obj();
            e["prefab"] = kv.Key; e["live"] = kv.Value;
            top.Add(e);
        }
        d["pooledTop"] = top;
        d["pooledTotal"] = byPrefab.Values.Sum();
        d["gcSinceLastFrame"] = gcNow - gcPrev;      // 0이 아니면 그 프레임에 GC가 돌았다
        d["monoHeapMB"] = (int)(System.GC.GetTotalMemory(false) / (1024 * 1024));
        Dictionary<string, object> h = pilot != null ? pilot.RunHeader : null;
        d["run"] = h != null && h.TryGetValue("run", out object run) ? run : null;
        d["map"] = h != null && h.TryGetValue("map", out object map) ? map : null;
        d["lastActions"] = pilot != null && pilot.Chaos != null ? pilot.Chaos.LastActions : null;
        try { File.AppendAllText(perfPath, BotJson.Write(d) + "\n"); } catch (Exception) { }
    }
}
#endif
