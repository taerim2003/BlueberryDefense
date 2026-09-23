#if UNITY_EDITOR || BOT_QA
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// 프레임 비용을 **CPU · GPU · 화면 제출 대기**로 갈라 `<세션>/frametimes.jsonl`에 2초마다 한 줄 남긴다.
//
// 🔴 벽시계 프레임 시간(Time.realtimeSinceStartup 차이)으로는 렉을 판정할 수 없다 — 창이 가려지면 윈도우가
//    화면 제출을 초당 1회로 억제해서, 게임이 아무 일도 안 해도 0.5초짜리 프레임이 찍힌다(9/23 실측:
//    같은 판이 창 모드 153회 vs headless 0회). 그 대기는 `cpuMainThreadPresentWaitTime`으로 따로 나오므로
//    **빼고 본다** — 그게 이 파일이 있는 이유다.
// ⚠️ 값이 채워지려면 빌드가 Frame Timing Stats로 뽑혀야 한다(`Assets/Editor/QABuild.cs`가 QA 빌드에만 켠다).
//    안 켜진 빌드에서는 전부 0이 나온다 — 0만 나오면 "부하가 없다"가 아니라 "안 재졌다"이다.
public class QAFrameStats : MonoBehaviour
{
    private const float WindowSeconds = 2f;

    private string path;
    private BotPilot pilot;
    private readonly FrameTiming[] buffer = new FrameTiming[1];
    private readonly List<double> cpu = new List<double>();
    private readonly List<double> gpu = new List<double>();
    private readonly List<double> wait = new List<double>();
    private readonly List<double> renderThread = new List<double>();
    private float windowStart;
    private int frames;
    private bool everNonZero;

    public void Init(BotConfig cfg, BotPilot owner)
    {
        pilot = owner;
        path = Path.Combine(cfg.sessionDir, "frametimes.jsonl");
        windowStart = Time.realtimeSinceStartup;
    }

    private void Update()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, buffer) > 0)
        {
            FrameTiming t = buffer[0];
            cpu.Add(t.cpuFrameTime);
            gpu.Add(t.gpuFrameTime);
            wait.Add(t.cpuMainThreadPresentWaitTime);
            renderThread.Add(t.cpuRenderThreadFrameTime);
            if (t.cpuFrameTime > 0) everNonZero = true;
        }
        frames++;

        float now = Time.realtimeSinceStartup;
        if (now - windowStart < WindowSeconds) return;
        Flush(now);
    }

    private void OnDestroy() => Flush(Time.realtimeSinceStartup);

    // 연출이 가장 몰린 순간을 한 번 찍는다 — 상한을 바꿔 가며 **같은 조건의 그림**을 나란히 놓고 보려고.

    // 🔴 조건 한 방이 아니라 **후반부에서 여러 장**을 찍는다 — 조건부 한 장은 설정(상한)에 따라 아예 안 찍혀서
    //    비교할 그림이 안 나온다(9/23에 두 번 그랬다). 파일명에 적 수를 넣어 두면 나중에 비슷한 장면끼리 짝지을 수 있다.
    private int shots;

    private void CaptureHeavyMoment(int enemies, int stage)
    {
        if (shots >= 10 || stage < 16 || enemies < 15 || string.IsNullOrEmpty(path)) return;
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        shots++;
        ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(path),
            "shot_stage" + stage + "_enemies" + enemies + "_" + shots + ".png"));
    }

    private static double Pct(List<double> v, float p)
    {
        if (v.Count == 0) return 0;
        var s = new List<double>(v);
        s.Sort();
        return s[Mathf.Clamp(Mathf.RoundToInt((s.Count - 1) * p), 0, s.Count - 1)];
    }

    private void Flush(float now)
    {
        float span = now - windowStart;
        windowStart = now;
        if (cpu.Count == 0 || string.IsNullOrEmpty(path)) { Reset(); return; }

        var d = BotJson.Obj();
        d["utc"] = DateTime.UtcNow.ToString("o");
        d["scene"] = SceneManager.GetActiveScene().name;
        d["stage"] = GameManager.Instance != null ? GameManager.Instance.CurrentStage : 0;
        d["fps"] = frames / Mathf.Max(0.001f, span);
        d["screen"] = Screen.width + "x" + Screen.height;
        // 창이 가려져 있어도 이 셋은 그대로다 — 대기(wait)만 커진다.
        d["cpuMs50"] = Pct(cpu, 0.5f); d["cpuMs95"] = Pct(cpu, 0.95f);
        d["gpuMs50"] = Pct(gpu, 0.5f); d["gpuMs95"] = Pct(gpu, 0.95f);
        d["renderThreadMs50"] = Pct(renderThread, 0.5f);
        d["presentWaitMs50"] = Pct(wait, 0.5f); d["presentWaitMs95"] = Pct(wait, 0.95f);
        d["timingsValid"] = everNonZero;

        // 무엇이 화면에 떠 있었나(렌더링 부하의 원인 후보). 2초에 한 번이라 세도 된다.
        int enemyCount = FindObjectsByType<Enemy>(FindObjectsInactive.Exclude).Length;
        d["enemies"] = enemyCount;
        CaptureHeavyMoment(enemyCount, GameManager.Instance != null ? GameManager.Instance.CurrentStage : 0);
        d["damageNumbers"] = FindObjectsByType<DamageNumber>(FindObjectsInactive.Exclude).Length;
        d["hitParticles"] = FindObjectsByType<HitParticle>(FindObjectsInactive.Exclude).Length;
        int psCount = FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude).Length;
        d["particleSystems"] = psCount;

        d["pooled"] = FindObjectsByType<PooledInstance>(FindObjectsInactive.Exclude).Length;
        Dictionary<string, object> h = pilot != null ? pilot.RunHeader : null;
        d["run"] = h != null && h.TryGetValue("run", out object run) ? run : null;
        d["map"] = h != null && h.TryGetValue("map", out object map) ? map : null;
        d["ascension"] = h != null && h.TryGetValue("ascension", out object asc) ? asc : null;
        try { File.AppendAllText(path, BotJson.Write(d) + "\n"); } catch (Exception) { }
        Reset();
    }

    private void Reset()
    {
        cpu.Clear(); gpu.Clear(); wait.Clear(); renderThread.Clear();
        frames = 0;
    }
}
#endif
