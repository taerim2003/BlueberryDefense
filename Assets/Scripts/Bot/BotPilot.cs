#if UNITY_EDITOR || BOT_QA
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 봇 플레이테스트 본체. `BotRuns/active/session.json`이 있을 때만 플레이모드 시작에 생성된다(BotLauncher가 만든다).
// QA 빌드(`BOT_QA`, `Assets/Editor/QABuild.cs`)에서는 `-botConfig <session.json>` 인자가 있을 때만 생성되고,
// 없으면 평범한 게임으로 뜬다. 러너는 `Tools/QA/qa-runner.js`, 절차는 `.claude/skills/qa-loop`.
//
// 정책(사용자 지정 — 바꾸면 이전 측정과 비교할 수 없게 되므로 `balance` 스킬부터 볼 것):
//   · QWER 꾹(BotInput.HoldSkills) · 레벨업 카드는 우선순위(2차 열쇠 → 1차 열쇠 → 보유 스킬 레벨업 → 아무거나,
//     `LevelUpPriorityPool`), 같은 단계 안에서는 무작위 · 리롤 안 씀
//   · 진화할 수 있으면 무조건 진화(보물 갈림길 → 진화). 대상·루트는 2차 열쇠로 필요한 것 우선(`NeededKeyRoutes`), 없으면 무작위
//   · 판이 끝나면 살 수 있는 노드를 싼 것부터 전부 구매 · 캐릭터는 해금된 것을 판마다 순환
//
// 🔴 판 진입은 **타이틀의 정상 흐름**(플레이 → 캐릭터 → 맵·난이도 → 시작)으로 한다. RunConfig를 손으로 채우고
//    Battle을 바로 로드하면 선택 화면이 하는 초기화가 빠져 게임 버그처럼 보이는 화면이 나온다(unity-mcp §11-1).
// 🔴 UI 필드는 private이라 리플렉션으로 찾는다. **이름이 하나라도 안 맞으면 즉시 세션을 멈추고 이름을 적는다** —
//    조용히 기다리게 두면 밤새 아무것도 안 한 채로 끝난다.
public class BotPilot : MonoBehaviour
{
    private static BotConfig cfg;

    private BotRecorder recorder;
    private System.Random rng;
    private float nextActionReal;
    private float lastMainTickReal;
    private float lastStatusReal;
    private string lastException;
    private bool finished;
    private readonly Dictionary<string, object> status = BotJson.Obj();

    private const string TitleScene = "Title";
    private const string BattleScene = "Battle";

    private QAErrorLog errors;
    private QAInvariants invariants;

    // QAErrorLog·QAInvariants가 오류 기록에 붙일 문맥.
    public BotChaos Chaos { get; private set; }
    public Dictionary<string, object> RunHeader { get; private set; }
    public float RunGameTime => recorder != null && recorder.Active ? recorder.GameTime : 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        BotInput.HoldSkills = false;
        BotInput.SkipEnding = false;
#if UNITY_EDITOR
        cfg = BotConfig.LoadActive();
        if (cfg == null)
        {
            SaveStore.UseProfile(null); // 도메인 리로드가 꺼져 있어도 지난 봇 세션의 프로필이 남지 않게
            return;
        }
        SaveStore.UseProfile("bot");
#else
        cfg = LoadFromCommandLine();
        if (cfg == null) return; // 인자가 없으면 평범한 게임 — QA 빌드를 손으로 해 봐도 된다
        BotConfig.RunsRootOverride = cfg.runsRoot;
        BotConfig.YieldPathOverride = Path.Combine(cfg.sessionDir, "stop");
        SaveStore.UseProfile("qa_" + (string.IsNullOrEmpty(cfg.instance) ? "0" : cfg.instance));
#endif
        BotInput.SkipEnding = cfg.skipEnding;
        BotInput.SuppressDamageNumbers = cfg.noDamageNumbers;
        BotInput.SuppressHitParticles = cfg.noHitParticles;
        BotInput.ImpactVfxPerFrameOverride = cfg.impactVfxPerFrame;
        var go = new GameObject("[BotPilot]");
        DontDestroyOnLoad(go);
        go.AddComponent<BotPilot>();
    }

#if !UNITY_EDITOR
    private static BotConfig LoadFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-botConfig");
        if (i < 0 || i + 1 >= args.Length) return null;
        BotConfig c = BotConfig.LoadFrom(args[i + 1]);
        if (c == null || string.IsNullOrEmpty(c.sessionDir)) { Debug.LogError("[Bot] -botConfig가 비었거나 sessionDir이 없다: " + args[i + 1]); return null; }
        Directory.CreateDirectory(c.sessionDir);
        return c;
    }
#endif

    private void Awake()
    {
        rng = new System.Random(cfg.seed);
        UnityEngine.Random.InitState(cfg.seed);
        recorder = new BotRecorder(cfg.sessionDir);
        Time.captureDeltaTime = Mathf.Max(0f, cfg.simStep);
        Application.runInBackground = true;
#if !UNITY_EDITOR
        // 고정 스텝(captureDeltaTime)이라 프레임이 빠를수록 게임 시간도 빨리 간다 — 수직 동기를 풀어 최대 속도로 돌린다.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        // 창 없이(-batchmode -nographics) 뜨면 키보드 장치가 없어 `Keyboard.current`가 null이다. 게임은 PC라 키보드를 전제하고
        // (PlayerSkills가 HoldSkills가 꺼진 프레임에 `Keyboard.current[...]`를 읽는다) 그 가정은 맞다 — 봇 쪽 환경을 맞춘다.
        if (UnityEngine.InputSystem.Keyboard.current == null) UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
#endif
        Application.logMessageReceived += OnLog;
        errors = gameObject.AddComponent<QAErrorLog>();
        errors.Init(cfg, this);
        invariants = gameObject.AddComponent<QAInvariants>();
        invariants.Init(cfg, this);
        gameObject.AddComponent<QAFrameStats>().Init(cfg, this);
        if (cfg.chaos)
        {
            Chaos = gameObject.AddComponent<BotChaos>();
            Chaos.Init(cfg);
        }
        status["label"] = cfg.label;
        status["mode"] = cfg.mode;
        if (!string.IsNullOrEmpty(cfg.kind)) { status["kind"] = cfg.kind; status["buildId"] = cfg.buildId; status["instance"] = cfg.instance; }
        status["startedUtc"] = DateTime.UtcNow.ToString("o");
        lastMainTickReal = Time.realtimeSinceStartup;
    }

    private void Start() => StartCoroutine(Main());

    private void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        recorder?.Unsubscribe();
        BotInput.HoldSkills = false;
        Time.captureDeltaTime = 0f;
    }

    // 세션 끝: 에디터는 플레이모드를 끄고(BotLauncher가 뒷정리), 빌드는 프로세스를 끝낸다(러너가 다음 세션을 띄운다).
    private static void StopPlay()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Exception) lastException = msg + "\n" + stack;
    }

    private void Update()
    {
        // 플레이 중 재컴파일(도메인 리로드)이 일어나면 static(cfg·SaveStore 프로필·BotInput 훅)과 코루틴이 전부 사라진 채
        // 이 컴포넌트만 남는다 — 판을 이어갈 수 없으니 기록하고 멈춘다. 런처가 세션 동안 재컴파일을 미루지만 이중 방어.
        if (cfg == null)
        {
            if (finished) return;
            finished = true;
            BotConfig active = BotConfig.LoadActive();
            if (active != null && !string.IsNullOrEmpty(active.sessionDir))
                File.WriteAllText(Path.Combine(active.sessionDir, "status.json"),
                    "{\"state\":\"error\",\"error\":\"플레이 중 스크립트 재컴파일로 봇 상태가 초기화됨 — 세션 도중 .cs를 고치지 말 것\",\"heartbeatUtc\":\""
                    + DateTime.UtcNow.ToString("o") + "\"}");
            StopPlay();
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (now - lastStatusReal > 5f) WriteStatus();
        // 메인 코루틴이 예외로 죽으면 Unity는 조용히 멈춘다 — 심장박동이 끊긴 것으로 잡는다.
        if (!finished && now - lastMainTickReal > Mathf.Max(90f, cfg.stuckRealSeconds * 1.5f))
        {
            errors.Record("BotDied", "[Bot] 메인 코루틴이 멈췄다" + (lastException != null ? ": " + lastException : ""));
            Finish("error", "메인 코루틴이 멈췄다" + (lastException != null ? ": " + lastException : ""));
        }
    }

    // ───────────────────────── 세션 ─────────────────────────
    private IEnumerator Main()
    {
        Set("state", "running");
        if (cfg.mode == "campaign") yield return Campaigns();
        else if (cfg.mode == "probe") yield return Probe();
        else if (cfg.mode != "audit") Fail("알 수 없는 mode: " + cfg.mode);
        Finish("done", null);
    }

    private IEnumerator Campaigns()
    {
        BotGoal[] goals = cfg.Goals;
        BotResumeState st = LoadResume("campaign");
        for (; st.campaign < cfg.campaigns; st.campaign++, st.campaignStarted = false)
        {
            if (!st.campaignStarted)
            {
                BotTree.ResetSave();
                st.attempts = new int[goals.Length];
                st.runs = 0; st.cumPicks = 0; st.cumGame = 0f; st.lastChar = null;
                st.firstClears.Clear();
                st.allNodesReached = false;
                st.campaignStarted = true;
                SaveResume(st);
            }
            string campaignKey = st.campaignKeyPrefix + "#" + st.campaign;
            string outcome;

            while (true)
            {
                int gi = Array.FindIndex(goals, g => !MapClearSave.HasCleared(g.map, g.ascension));
                bool allNodes = BotTree.AllMaxed();
                if (gi < 0 && allNodes) { outcome = "complete"; break; }
                if (gi >= 0 && st.attempts[gi] >= cfg.maxAttemptsPerGoal) { outcome = "stuckGoal"; break; }
                if (st.runs >= cfg.maxRunsPerCampaign) { outcome = "runCap"; break; }
                if (YieldRequested()) { Yield(st); yield break; } // 판 경계 — 여기서 비키면 잃는 게 없다

                // 이번 판 값은 지역 변수로만 들고 있다가 판이 **끝나야** st에 반영한다(판 중간 양보 시 되감기 불필요).
                int target = gi >= 0 ? gi : goals.Length - 1; // 전부 깼으면 남은 노드를 위해 마지막 목표 반복
                int attempt = gi >= 0 ? st.attempts[gi] + 1 : 0;
                string charName = st.lastChar;
                CharacterDefinition ch = NextCharacter(ref charName);

                var header = BotJson.Obj();
                header["mode"] = "campaign"; header["label"] = cfg.label; header["campaign"] = st.campaign; header["campaignKey"] = campaignKey;
                header["run"] = st.runs;
                header["goalIndex"] = target; header["map"] = goals[target].map; header["ascension"] = goals[target].ascension;
                header["character"] = ch.name; header["attempt"] = attempt; header["farming"] = gi < 0;
                header["nodesOwnedBefore"] = BotTree.OwnedLevels(); header["nodesTotal"] = BotTree.TotalLevels();
                header["spentBefore"] = BotTree.Spent(); header["treeTotalCost"] = BotTree.TotalCost();
                header["essenceEarnedBefore"] = SkillTreeSave.EssenceEarned;

                SetProgress(st.campaign, st.runs, goals[target], ch.name);
                yield return EnterRun(goals[target], ch);
                Dictionary<string, object> run = null;
                yield return PlayBattle(header, r => run = r);
                if ((string)run["result"] == "yielded") { Yield(st); yield break; } // 판을 버리고 마지막 저장 지점으로

                if (gi >= 0) st.attempts[gi] = attempt;
                st.lastChar = charName;
                run["purchases"] = BotTree.BuyCheapestFirst(rng);
                run["nodesOwnedAfter"] = BotTree.OwnedLevels();
                run["spentAfter"] = BotTree.Spent();
                st.cumGame += (float)run["gameTime"];
                st.cumPicks += ((List<object>)run["picks"]).Count;
                st.runs++;
                run["cumGameTime"] = st.cumGame;
                run["cumPicks"] = st.cumPicks;
                run["cumRuns"] = st.runs;

                if ((string)run["result"] == "clear" && gi >= 0 && !st.firstClears.Any(f => f.goal == gi))
                    st.firstClears.Add(new BotFirstClear { goal = gi, attempts = attempt, cumRuns = st.runs, cumGameTime = st.cumGame, cumPicks = st.cumPicks, nodesOwned = BotTree.OwnedLevels() });
                if (!st.allNodesReached && BotTree.AllMaxed())
                {
                    st.allNodesReached = true;
                    st.allNodesRuns = st.runs; st.allNodesGame = st.cumGame; st.allNodesPicks = st.cumPicks;
                }
                recorder.WriteRun(run);
                SaveResume(st);
                if ((string)run["result"] == "stuck") Fail("판이 멈춤 — stuck_*.png와 runs.jsonl 마지막 줄 참고");
            }

            var summary = BotJson.Obj();
            summary["label"] = cfg.label; summary["campaign"] = st.campaign; summary["campaignKey"] = campaignKey; summary["outcome"] = outcome;
            summary["runs"] = st.runs; summary["cumGameTime"] = st.cumGame; summary["cumPicks"] = st.cumPicks;
            if (st.allNodesReached)
            {
                var at = BotJson.Obj();
                at["cumRuns"] = st.allNodesRuns; at["cumGameTime"] = st.allNodesGame; at["cumPicks"] = st.allNodesPicks;
                summary["allNodesAt"] = at;
            }
            else summary["allNodesAt"] = null;
            var goalRows = new List<object>();
            for (int i = 0; i < goals.Length; i++)
            {
                var row = BotJson.Obj();
                row["map"] = goals[i].map; row["ascension"] = goals[i].ascension; row["attempts"] = st.attempts[i];
                BotFirstClear f = st.firstClears.FirstOrDefault(x => x.goal == i);
                if (f != null)
                {
                    var fc = BotJson.Obj();
                    fc["attempts"] = f.attempts; fc["cumRuns"] = f.cumRuns; fc["cumGameTime"] = f.cumGameTime; fc["cumPicks"] = f.cumPicks; fc["nodesOwned"] = f.nodesOwned;
                    row["firstClear"] = fc;
                }
                else row["firstClear"] = null;
                goalRows.Add(row);
            }
            summary["goals"] = goalRows;
            File.AppendAllText(Path.Combine(cfg.sessionDir, "campaigns.jsonl"), BotJson.Write(summary) + "\n");
            // 저장은 다음 캠페인 시작 시점에 한다 — 여기서 저장하면 재개가 끝난 캠페인을 다시 열어 요약이 두 번 적힌다.
        }
    }

    // ───────────────────────── 양보 · 재개 ─────────────────────────
    private string SessionId => Path.GetFileName(cfg.sessionDir);
    private float nextYieldCheckReal;
    private bool yieldNowCached;

    private static bool YieldRequested() => File.Exists(BotConfig.YieldPath);

    // 판 중간 즉시 양보: yield 파일 내용에 "now"가 있을 때만. 파일 읽기는 실시간 2초마다.
    private bool YieldNow()
    {
        float now = Time.realtimeSinceStartup;
        if (now < nextYieldCheckReal) return yieldNowCached;
        nextYieldCheckReal = now + 2f;
        try { yieldNowCached = File.Exists(BotConfig.YieldPath) && File.ReadAllText(BotConfig.YieldPath).Contains("now"); }
        catch (Exception) { yieldNowCached = false; }
        return yieldNowCached;
    }

    private BotResumeState LoadResume(string mode)
    {
        if (!string.IsNullOrEmpty(cfg.resumeFrom))
        {
            string path = Path.Combine(BotConfig.RunsRoot, cfg.resumeFrom, "resume.json");
            if (!File.Exists(path)) Fail("재개할 상태 파일이 없다: " + path);
            BotResumeState st = JsonUtility.FromJson<BotResumeState>(File.ReadAllText(path));
            if (st == null || st.mode != mode) Fail("재개 상태의 mode가 다르다: " + path);
            if (string.IsNullOrEmpty(st.campaignKeyPrefix)) st.campaignKeyPrefix = cfg.resumeFrom;
            Set("resumedFrom", cfg.resumeFrom);
            SaveResume(st);
            return st;
        }
        return new BotResumeState { mode = mode, campaignKeyPrefix = SessionId };
    }

    private void SaveResume(BotResumeState st) =>
        File.WriteAllText(Path.Combine(cfg.sessionDir, "resume.json"), JsonUtility.ToJson(st, true));

    private void Yield(BotResumeState st)
    {
        SaveResume(st);
        string who = "";
        try { who = File.ReadAllText(BotConfig.YieldPath).Trim(); } catch (Exception) { }
        Set("yieldedFor", who);
        Set("resumeWith", "{\"label\":\"" + cfg.label + "-r\",\"resumeFrom\":\"" + SessionId + "\"}");
        Finish("yielded", null);
    }

    private IEnumerator Probe()
    {
        string[] mapNames = BotConfig.DefaultGoals.Select(g => g.map).Distinct().ToArray();
        var cells = new List<(float ratio, BotGoal goal, string ch, int rep)>();
        foreach (float ratio in cfg.probeTreeRatios)
            foreach (BotGoal g in BotConfig.DefaultGoals)
            {
                if (cfg.probeGoalFilter != null && cfg.probeGoalFilter.Length > 0
                    && !cfg.probeGoalFilter.Contains(g.map + ":" + g.ascension)) continue;
                foreach (string ch in cfg.characters)
                    for (int rep = 0; rep < cfg.probeRuns; rep++) cells.Add((ratio, g, ch, rep));
            }
        BotGoal hardest = BotConfig.DefaultGoals.Last();
        foreach (string ch in cfg.characters)
            for (int rep = 0; rep < cfg.fullTreeRuns; rep++) cells.Add((1f, hardest, ch, rep));

        BotResumeState st = LoadResume("probe");
        for (int i = st.nextProbeIndex; i < cells.Count; i++)
        {
            if (YieldRequested()) { st.nextProbeIndex = i; Yield(st); yield break; }
            var cell = cells[i];
            Trace("probe cell " + i + " ratio=" + cell.ratio + " " + cell.goal.map + ":" + cell.goal.ascension + " " + cell.ch);
            Trace("buildReferenceSave 시작");
            BotTree.BuildReferenceSave(cell.ratio, mapNames);
            Trace("buildReferenceSave 끝");
            CharacterDefinition ch = BotTree.LoadByName<CharacterDefinition>(cell.ch);
            if (ch == null) Fail("캐릭터 에셋 없음: " + cell.ch);

            var header = BotJson.Obj();
            header["mode"] = "probe"; header["label"] = cfg.label; header["run"] = i; header["cells"] = cells.Count;
            header["treeRatio"] = cell.ratio; header["map"] = cell.goal.map; header["ascension"] = cell.goal.ascension;
            header["character"] = cell.ch; header["rep"] = cell.rep;
            header["nodesOwnedBefore"] = BotTree.OwnedLevels(); header["nodesTotal"] = BotTree.TotalLevels();
            header["spentBefore"] = BotTree.Spent(); header["treeTotalCost"] = BotTree.TotalCost();

            SetProgress(0, i, cell.goal, cell.ch);
            Trace("enterRun 시작");
            yield return EnterRun(cell.goal, ch);
            Dictionary<string, object> run = null;
            Trace("playBattle 시작");
            yield return PlayBattle(header, r => run = r);
            Trace("playBattle 끝 result=" + run["result"]);
            if ((string)run["result"] == "yielded") { st.nextProbeIndex = i; Yield(st); yield break; }
            recorder.WriteRun(run);
            Trace("writeRun 끝");
            st.nextProbeIndex = i + 1;
            SaveResume(st);
            if ((string)run["result"] == "stuck") Fail("판이 멈춤 — stuck_*.png 참고");
        }
    }

    private CharacterDefinition NextCharacter(ref string last)
    {
        string[] order = cfg.characters;
        int start = last == null ? 0 : Array.IndexOf(order, last) + 1;
        for (int k = 0; k < order.Length; k++)
        {
            string name = order[(start + k) % order.Length];
            CharacterDefinition ch = BotTree.LoadByName<CharacterDefinition>(name);
            if (ch != null && ch.IsUnlocked) { last = name; return ch; }
        }
        Fail("해금된 캐릭터가 없다");
        return null;
    }

    // ───────────────────────── 판 진입 (타이틀 정상 흐름) ─────────────────────────
    private IEnumerator EnterRun(BotGoal goal, CharacterDefinition ch)
    {
        // 🔴 2026-09-18 정지 3건이 전부 이 메서드 안에서 났다(trace.log 마지막 줄이 세 번 다 "enterRun 시작").
        //    정상 전환은 3.5초다. 어느 문장인지 좁히려고 단계마다 도장을 찍는다 — 다음 정지 때 trace.log가 답을 준다.
        // chaos: 결과 화면에서 스킬트리로 가기·다시 하기 같은 다른 출구를 먼저 탄다.
        if (Chaos != null && SceneManager.GetActiveScene().name == BattleScene && GameManager.Instance != null)
        {
            Trace("  chaos 결과 화면");
            yield return Chaos.StartCoroutine(Chaos.Guarded("result_phase", Chaos.ResultBody()));
        }
        // 판이 아직 진행 중이면(양보·chaos 다시 하기 직후) **사람과 같은 길로** 나간다: 일시정지 → 포기 → 결과 화면.
        // 진행 중에 ReturnToTitle을 직접 부르면 페이드아웃 동안 판이 계속 돌다 GameOver가 timeScale=0을 세운 채
        // 타이틀이 뜬다 — 사람은 도달할 수 없는 상태라 가짜 버그가 된다(2026-09-22 title-paused).
        GameManager live = GameManager.Instance;
        if (SceneManager.GetActiveScene().name == BattleScene && live != null && !live.IsGameOver && !live.IsGameClear && !live.IsEnding)
        {
            PauseMenu pm = FindAnyObjectByType<PauseMenu>();
            if (pm != null)
            {
                Trace("  진행 중인 판 포기(일시정지 → 포기)");
                if (!Get<bool>(pm, "paused")) Call(pm, "PauseAndShow");
                yield return null;
                Call(pm, "GiveUpToTitle");
                yield return WaitReal(() => GameManager.Instance == null || GameManager.Instance.IsGameOver, 10f, "포기 후 게임오버");
            }
        }
        if (SceneManager.GetActiveScene().name != TitleScene)
        {
            Trace("  타이틀 복귀 요청 전");
            if (GameManager.Instance != null) GameManager.Instance.ReturnToTitle();
            else SceneFade.LoadScene(TitleScene);
            Trace("  타이틀 복귀 요청 후");
        }
        // SceneFade는 페이드 도중의 로드 요청을 **무시**한다(두 번 로드 방지). 판이 페이드인(0.45초) 안에 끝나면
        // 위 요청이 먹혀서 영원히 기다리게 된다 — 3초마다 다시 요청한다.
        float nextRetry = Time.realtimeSinceStartup + 3f;
        yield return WaitReal(() =>
        {
            if (SceneManager.GetActiveScene().name == TitleScene && FindAnyObjectByType<TitleController>() != null) return true;
            if (Time.realtimeSinceStartup > nextRetry && SceneManager.GetActiveScene().name != TitleScene)
            {
                nextRetry = Time.realtimeSinceStartup + 3f;
                Trace("  타이틀 복귀 재요청");
                if (GameManager.Instance != null) GameManager.Instance.ReturnToTitle();
                else SceneFade.LoadScene(TitleScene);
            }
            return false;
        }, 60f, "타이틀 로드");
        Trace("  타이틀 로드됨");
        yield return new WaitForSecondsRealtime(1.2f);
        Tick();
        if (Chaos != null)
        {
            Trace("  chaos 타이틀");
            yield return Chaos.StartCoroutine(Chaos.Guarded("title_phase", Chaos.TitleBody()));
            Tick();
        }

        TitleController title = FindAnyObjectByType<TitleController>();
        Get<Button>(title, "playButton").onClick.Invoke();
        Trace("  play 버튼 누름");
        yield return new WaitForSecondsRealtime(0.4f);
        Tick();

        var cs = Get<CharacterSelectUI>(title, "characterSelect");
        var chars = Get<CharacterDefinition[]>(cs, "characters");
        int ci = Array.FindIndex(chars, c => c != null && c.name == ch.name);
        if (ci < 0) Fail("CharacterSelectUI.characters에 " + ch.name + " 없음");
        Call(cs, "Pick", ci);
        if (cs.Selected != chars[ci]) Fail("캐릭터 선택이 거부됨(잠김?): " + ch.name);
        if (cfg.captureSelectScreens) yield return CaptureSelect("char");
        Get<Button>(cs, "confirmButton").onClick.Invoke();
        Trace("  캐릭터 확정");
        yield return new WaitForSecondsRealtime(0.4f);
        Tick();

        var ms = Get<MapSelectUI>(title, "mapSelect");
        var maps = Get<MapDefinition[]>(ms, "maps");
        int mi = Array.FindIndex(maps, m => m != null && m.name == goal.map);
        if (mi < 0) Fail("MapSelectUI.maps에 " + goal.map + " 없음");
        Call(ms, "Select", mi);
        int maxAsc = (int)Prop(ms, "MaxSelectableAscension");
        if (maxAsc < goal.ascension) Fail($"{goal.map} 난이도 {goal.ascension}이 아직 잠김(최대 {maxAsc})");
        SetField(ms, "ascensionLevel", goal.ascension);
        Call(ms, "RefreshAscension");
        if (cfg.captureSelectScreens) yield return CaptureSelect("map");
        Button start = Get<Button>(ms, "startButton");
        if (!start.interactable) Fail("시작 버튼 비활성: " + goal.map);
        start.onClick.Invoke();
        Trace("  시작 버튼 누름");

        yield return WaitReal(() => SceneManager.GetActiveScene().name == BattleScene && GameManager.Instance != null
                                     && FindAnyObjectByType<PlayerSkills>() != null, 60f, "전투 로드");
        Trace("  전투 로드됨");
        // 🔴 여기서 **실시간**을 기다리면 안 된다. 게임 시간은 프레임당 simStep으로 고정이라, 프레임이 빠를수록
        //    실시간 0.3초 동안 게임 시간이 더 흐르고 그동안 봇은 스킬을 안 누른다(HoldSkills는 PlayBattle에서 켠다).
        //    창 없는 QA 빌드에선 그 틈이 게임 시간 수십 초가 되어 시작 체력 242 중 224를 공짜로 맞았다(2026-09-22).
        //    초기화(Start·첫 Update)만 끝나면 되므로 프레임 수로 기다린다 — 게임 시간 약 0.1초.
        for (int f = 0; f < 3; f++) yield return null;
        if (RunConfig.Map == null || RunConfig.Map.name != goal.map || RunConfig.AscensionLevel != goal.ascension
            || RunConfig.Character == null || RunConfig.Character.name != ch.name)
            Fail("RunConfig가 요청과 다름 — 선택 화면 흐름이 바뀌었는지 확인");
    }

    // 선택 연출(팝·페이드)이 가라앉은 뒤 찍는다. 파일 이름에 해상도를 넣어 해상도별로 나란히 비교할 수 있게.
    private int captureCount;

    private IEnumerator CaptureSelect(string screen)
    {
        yield return new WaitForSecondsRealtime(0.8f);
        Tick();
        string png = Path.Combine(cfg.sessionDir, "sel_" + screen + "_" + Screen.width + "x" + Screen.height + "_" + (captureCount++) + ".png");
        ScreenCapture.CaptureScreenshot(png);
        yield return null;
    }

    // ───────────────────────── 전투 ─────────────────────────
    private IEnumerator PlayBattle(Dictionary<string, object> header, Action<Dictionary<string, object>> done)
    {
        header["seed"] = cfg.seed;
        if (!string.IsNullOrEmpty(cfg.kind)) { header["kind"] = cfg.kind; header["buildId"] = cfg.buildId; header["instance"] = cfg.instance; }
        RunHeader = header;
        recorder.BeginRun(header);
        errors.TakeRunCounts();
        invariants.TakeRunSummary();
        // Chaos 기록은 여기서 비우지 않는다 — 판 직전 타이틀·결과 화면에서 한 행동도 이 판의 chaosActions에 들어가야 한다.
        float runStartReal = Time.realtimeSinceStartup;
        float lastProgressReal = runStartReal;
        float lastGameTime = 0f;
        GameManager startGm = GameManager.Instance;
        string result;

        while (true)
        {
            Tick();
            GameManager gm = GameManager.Instance;
            // 엔딩(skipEnding=false일 때만 탄다)은 끝까지 보고 타이틀로 돌아오면 클리어로 친다. 엔딩에서 멈추면 WaitReal이 실패로 잡는다.
            if (gm != null && gm.IsEnding)
            {
                // 🔴 스킬을 놓지 말 것 — 엔딩 한가운데 마지막 보스전(블루베리 군집체)이 있고, 그걸 잡아야 엔딩이 이어진다.
                //    연출 구간의 봉인은 게임이 PlayerSkills.Sealed로 한다. 놓았더니 군집체를 못 잡아 240초 대기로 끝났다(9/23 QA).
                BotInput.HoldSkills = true;
                yield return WaitReal(() => SceneManager.GetActiveScene().name == TitleScene, 240f, "엔딩 끝");
                result = "clear";
                break;
            }
            // chaos가 판 도중 타이틀로 나가거나 다시 하기를 눌렀다 — 오류가 아니라 버려진 판이다.
            if (Chaos != null && (gm == null || gm != startGm)) { result = "abandoned"; break; }
            if (gm == null) { result = "error"; lastException = "전투 중 GameManager가 사라짐"; break; }
            if (gm.IsGameClear) { result = "clear"; break; }
            if (gm.IsGameOver) { result = "dead"; break; }
            if (YieldNow()) { result = "yielded"; break; } // 다른 세션이 급히 Unity를 요청 — 이 판은 기록하지 않고 버린다

            BotInput.HoldSkills = true;
            recorder.Tick();
            if (Chaos != null) Chaos.BattleActive = true;
            // 일시정지·옵션 창이 떠 있으면 사람은 그 너머의 레벨업 카드를 못 누른다 — 봇도 기다린다.
            bool acted = Chaos != null && Chaos.OverlayOpen ? false : HandleModals();

            float now = Time.realtimeSinceStartup;
            if (acted || recorder.GameTime > lastGameTime + 0.0001f) { lastProgressReal = now; lastGameTime = recorder.GameTime; }
            if (now - lastProgressReal > cfg.stuckRealSeconds) { result = "stuck"; DumpStuck(); break; }
            if (now - runStartReal > cfg.maxRunRealSeconds) { result = "timeout"; break; }
            if (cfg.qaSelfTest && !selfTested && recorder.GameTime > 5f) SelfTest();
            if (GameManager.Instance != null) Set("stage", GameManager.Instance.CurrentStage);
            yield return null;
        }

        if (Chaos != null) Chaos.BattleActive = false;
        BotInput.HoldSkills = false;
        if (result == "stuck" || result == "timeout")
            errors.Record(result == "stuck" ? "Stuck" : "Timeout", "[QA-INV] " + result + ": 판이 " + (result == "stuck" ? cfg.stuckRealSeconds + "초 동안 진행 없음" : "실시간 상한 초과"));
        Dictionary<string, object> run = recorder.EndRun(result);
        if (!string.IsNullOrEmpty(cfg.kind))
        {
            Dictionary<string, object> qa = invariants.TakeRunSummary();
            qa["errors"] = errors.TakeRunCounts();
            run["qa"] = qa;
            if (Chaos != null) run["chaosActions"] = Chaos.TakeRunLog();
        }
        RunHeader = null;
        if (result == "error") Fail(lastException);
        if (result == "abandoned") yield return new WaitForSecondsRealtime(2f); // 진행 중인 씬 전환이 끝나게 둔다
        done(run);
    }

    // 오류 수집 대조군 — 수집이 실제로 잡는지 보려고 일부러 예외 1개·LogError 1개를 낸다(cfg.qaSelfTest, 세션당 1회).
    private bool selfTested;

    private void SelfTest()
    {
        selfTested = true;
        Debug.LogError("[QA-SELFTEST] 일부러 낸 LogError");
        try { throw new InvalidOperationException("[QA-SELFTEST] 일부러 낸 예외"); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // 떠 있는 모달 하나를 처리했으면 true. 연출이 한 프레임 늦게 붙는 걸 감안해 실시간 0.12초 간격으로만 누른다.
    private bool HandleModals()
    {
        float now = Time.realtimeSinceStartup;
        if (now < nextActionReal) return false;

        LevelUpUI lu = LevelUpUI.Instance;
        if (lu != null)
        {
            // ① 보물 갈림길: 진화할 수 있으면 무조건 진화
            if (Get<bool>(lu, "choiceOpen"))
            {
                Button evo = Get<Button>(lu, "choiceEvolveButton");
                Button box = Get<Button>(lu, "choiceTreasureButton");
                bool canEvo = Usable(evo);
                RecordPick("treasureFork", canEvo ? new object[] { "treasure", "evolve" } : new object[] { "treasure" },
                    canEvo ? "evolve" : "treasure", canEvo);
                (canEvo ? evo : box).onClick.Invoke();
                return Acted();
            }

            // ② 보물 공개: 아이콘이 다 뜨면 닫기
            if (Get<bool>(lu, "treasureMode"))
            {
                Button dismiss = Get<Button>(lu, "treasureDismissButton");
                if (Usable(dismiss)) { dismiss.onClick.Invoke(); return Acted(); }
                return false;
            }

            // ③ 레벨업 3택 / 진화 대상 선택
            if (Get<bool>(lu, "isOpen"))
            {
                Array opts = Get<Array>(lu, "currentOptions");
                if (opts == null || opts.Length == 0) return false;
                bool evoMode = Get<bool>(lu, "evolutionMode");

                var pool = Enumerable.Range(0, opts.Length).ToList();
                string rule = "random";
                if (evoMode)
                {
                    var evoIdx = pool.Where(i => OptField<bool>(opts.GetValue(i), "IsEvolution")).ToList();
                    if (evoIdx.Count > 0) { pool = evoIdx; rule = "evolution"; }
                    // 2차 열쇠로 필요한(아직 진화 전인) 스킬을 먼저 진화시킨다 — 사용자 지정 2026-09-18.
                    NeededKeyRoutes(out var keyA, out var keyP);
                    var keyIdx = pool.Where(i =>
                    {
                        object o = opts.GetValue(i);
                        var sid = OptField<ActiveSkillId?>(o, "SkillId");
                        var pid = OptField<PassiveSkillId?>(o, "PassiveId");
                        return OptField<bool>(o, "IsEvolution")
                            && ((sid.HasValue && keyA.ContainsKey(sid.Value)) || (pid.HasValue && keyP.ContainsKey(pid.Value)));
                    }).ToList();
                    if (keyIdx.Count > 0) { pool = keyIdx; rule = "stage2KeyEvolve"; }
                }
                else pool = LevelUpPriorityPool(opts, out rule);
                int oi = pool[rng.Next(pool.Count)];
                int slot = opts.Length == 1 ? 1 : oi; // LevelUpUI.SlotForOption과 같은 규칙
                Button b = Get<Button>(lu, slot == 0 ? "optionButtonA" : slot == 1 ? "optionButtonB" : "optionButtonC");
                if (!Usable(b)) return false;

                var offered = new List<object>();
                for (int i = 0; i < opts.Length; i++) offered.Add(DescribeOption(opts.GetValue(i)));
                RecordPick(evoMode ? "evolutionTarget" : "levelUp", offered, DescribeOption(opts.GetValue(oi)),
                    OptField<bool>(opts.GetValue(oi), "IsEvolution"), rule);
                b.onClick.Invoke();
                return Acted();
            }
        }

        // ④ 진화 루트 트리
        EvolutionTreeUI et = EvolutionTreeUI.Instance;
        if (et != null && Get<bool>(et, "isOpen"))
        {
            Array nodes = Get<Array>(et, "nodes");
            var usable = new List<(int route, Button button)>();
            for (int i = 0; i < nodes.Length; i++)
            {
                object node = nodes.GetValue(i);
                Button nb = node != null ? OptField<Button>(node, "button") : null;
                if (Usable(nb)) usable.Add((i / 2, nb)); // index = route*2 + (tier-1)
            }
            if (usable.Count == 0) return false;
            EquippedSkill s = Get<EquippedSkill>(et, "currentSkill");
            EquippedPassive p = Get<EquippedPassive>(et, "currentPassive");
            // 이 스킬이 다른 스킬의 2차 열쇠면 **열쇠가 요구하는 루트**로 진화시킨다(열쇠는 루트까지 맞아야 한다).
            NeededKeyRoutes(out var keyA, out var keyP);
            int wantRoute = s != null && keyA.TryGetValue(s.Id, out int ra) ? ra
                          : p != null && keyP.TryGetValue(p.Id, out int rp) ? rp : -1;
            var keyed = usable.Where(u => u.route == wantRoute).ToList();
            var pick = keyed.Count > 0 ? keyed[rng.Next(keyed.Count)] : usable[rng.Next(usable.Count)];
            RecordPick("evolutionRoute", usable.Select(u => (object)u.route).Distinct().ToList(),
                (s != null ? s.Id.ToString() : p != null ? "P:" + p.Id : "?") + ":R" + pick.route, true,
                keyed.Count > 0 ? "keyRoute" : "random");
            pick.button.onClick.Invoke();
            return Acted();
        }
        return false;
    }

    private bool Acted()
    {
        nextActionReal = Time.realtimeSinceStartup + 0.12f;
        return true;
    }

    private static bool Usable(Button b) => b != null && b.gameObject.activeInHierarchy && b.interactable;

    private object DescribeOption(object opt)
    {
        var d = BotJson.Obj();
        var skill = OptField<ActiveSkillId?>(opt, "SkillId");
        var passive = OptField<PassiveSkillId?>(opt, "PassiveId");
        d["kind"] = skill.HasValue ? "active" : passive.HasValue ? "passive" : "other";
        d["id"] = skill.HasValue ? skill.Value.ToString() : passive.HasValue ? passive.Value.ToString() : null;
        d["isNew"] = OptField<bool>(opt, "IsNew");
        d["isEvolution"] = OptField<bool>(opt, "IsEvolution");
        return d;
    }

    // 🔴 대공에 강한 스킬(2026-09-19 사용자 지정). 8회차 측정의 대공 축(비행 적에게 준 피해/시간)이 근거다:
    //    Sniping 5891 · Homing 5727 · Lightning 2297 · EagleDrop 827.
    //    나머지는 BasicAttack 752 · Swing 156 · GrapeToss 62 · Whirlwind 31 · Shotgun 3 · Orb 0 · Rewind 0.
    //    이제는 **사거리·유도 성능의 차이**다 — "때릴 수 있나"를 막던 requiresAntiAir 게이트는 폐지됐고,
    //    비행 적은 히트박스가 닿으면 무엇에든 맞는다. 회오리가 지상에 묶여 못 닿고, 저격·호밍은 쫓아가서 맞힌다.
    // ⚠️ 위 숫자는 **게이트가 살아 있던 때** 잰 것이다 — 게이트가 사라지면 오브·산탄 쪽이 특히 오를 수 있다.
    //    9회차 측정 뒤 보고서 스킬 표의 대공 열로 이 목록을 **다시 뽑을 것.**
    // 🔴 실측으로 추린 목록이다(219판, effByEnemy의 비행 적 몫). 전체 피해 중 비행 적 몫이 **24.4%**이고
    //    그게 기준선이다 — 스나이핑 63.6% · 호밍 54.5% · 독수리 34.3%만 그 위다.
    //    **피뢰침은 21.6%로 기준선 아래라 2026-09-21에 뺐다**(사용자 결정). 들어 있으면 피뢰침만 든 판에서
    //    봇이 "대공 있음"으로 판단해 ⓪단계(대공 구멍 메우기)를 안 연다.
    //    참고로 대공이 거의 안 되는 것: 오브 4.6% · 산탄 4.3% · 기본화살 4.2% · 휘두르기 6.6% · 회오리 7.9%.
    private static readonly HashSet<ActiveSkillId> AntiAirSkills = new HashSet<ActiveSkillId>
    {
        ActiveSkillId.Sniping, ActiveSkillId.Homing, ActiveSkillId.EagleDrop,
    };

    // 대공 스킬이 하나도 없을 때 대공 카드를 고를 확률. 1.0으로 두지 않는 이유 —
    // 항상 고르면 모든 판의 빌드가 같아져서 "대공 유무가 만드는 차이"를 측정할 수 없게 된다.
    private const float AntiAirPickChance = 0.75f;

    // 레벨업 3택 우선순위(사용자 지정 2026-09-18 — 무작위만으로는 2차 진화가 거의 안 만들어져서):
    //   ⓪ **대공에 강한 스킬이 하나도 없으면** 그 스킬의 획득 카드(확률 AntiAirPickChance) — 2026-09-19 사용자
    //      비행선은 높이 떠서 지상에 묶인 스킬로는 닿지 않는다. 사람은 하늘이 안 잡히는 걸 보고 대공을 고르는데
    //      무작위 봇은 그걸 못 봐서, 봇의 후반이 사람보다 구조적으로 약해지는 원인 중 하나였다.
    //   ① 보유한 **1차 진화 스킬**의 2차 열쇠(`EvolutionRoutes.Stage2Prereq`) 중 아직 없는 것의 획득 카드
    //   ② 보유한 **진화 전 스킬**(액티브·패시브)의 1차 루트 조건(`RoutePrereq`, 두 루트 모두) 중 아직 없는 것의 획득 카드
    //   ③ 보유한 스킬의 레벨업 카드   ④ 아무 카드
    // 같은 단계에 여러 장이면 그중 무작위. 반환 = 그 단계의 카드 인덱스들, rule = 기록용 단계 이름.
    private List<int> LevelUpPriorityPool(Array opts, out string rule)
    {
        PlayerSkills ps = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives pp = FindAnyObjectByType<PlayerPassives>();
        var keyActive1 = new HashSet<ActiveSkillId>(); var keyPassive1 = new HashSet<PassiveSkillId>();
        var keyActive2 = new HashSet<ActiveSkillId>(); var keyPassive2 = new HashSet<PassiveSkillId>();
        bool Owns(PassiveSkillId? p, ActiveSkillId? a) =>
            (p.HasValue && pp != null && pp.HasPassive(p.Value)) || (a.HasValue && ps != null && ps.HasSkill(a.Value));
        void AddMissing(PassiveSkillId? p, ActiveSkillId? a, HashSet<PassiveSkillId> ps_, HashSet<ActiveSkillId> as_)
        {
            if (p.HasValue && !Owns(p, null)) ps_.Add(p.Value);
            if (a.HasValue && !Owns(null, a)) as_.Add(a.Value);
        }

        if (ps != null)
            foreach (EquippedSkill s in ps.EquippedSkills)
            {
                if (s.EvolutionStage == 1 && s.EvolutionStage < EvolutionRoutes.MaxStageFor(s.Id))
                {
                    var k = EvolutionRoutes.Stage2Prereq(s.Id, s.Route);
                    AddMissing(k.Passive, k.Active, keyPassive1, keyActive1);
                }
                else if (s.EvolutionStage == 0)
                    for (int route = 0; route < 2; route++)
                    {
                        var k = EvolutionRoutes.RoutePrereq(s.Id, route);
                        AddMissing(k.Passive, k.Active, keyPassive2, keyActive2);
                    }
            }
        if (pp != null)
            foreach (EquippedPassive p in pp.EquippedPassives)
                if (p.EvolutionStage == 0)
                    for (int route = 0; route < 2; route++)
                    {
                        var k = EvolutionRoutes.RoutePrereq(p.Id, route);
                        AddMissing(k.Passive, k.Active, keyPassive2, keyActive2);
                    }

        var all = Enumerable.Range(0, opts.Length).ToList();
        List<int> Match(HashSet<ActiveSkillId> actives, HashSet<PassiveSkillId> passives) => all.Where(i =>
        {
            object o = opts.GetValue(i);
            if (!OptField<bool>(o, "IsNew")) return false;
            var sid = OptField<ActiveSkillId?>(o, "SkillId");
            var pid = OptField<PassiveSkillId?>(o, "PassiveId");
            return (sid.HasValue && actives.Contains(sid.Value)) || (pid.HasValue && passives.Contains(pid.Value));
        }).ToList();

        // ⓪ 대공 구멍 메우기 — 보유 스킬에 대공이 하나도 없을 때만 연다.
        bool hasAntiAir = ps != null && ps.EquippedSkills.Any(s => AntiAirSkills.Contains(s.Id));
        if (!hasAntiAir)
        {
            List<int> air = all.Where(i =>
            {
                object o = opts.GetValue(i);
                if (!OptField<bool>(o, "IsNew")) return false;
                var sid = OptField<ActiveSkillId?>(o, "SkillId");
                return sid.HasValue && AntiAirSkills.Contains(sid.Value);
            }).ToList();
            if (air.Count > 0 && rng.NextDouble() < AntiAirPickChance) { rule = "antiAir"; return air; }
        }

        List<int> pool = Match(keyActive1, keyPassive1);
        if (pool.Count > 0) { rule = "stage2Key"; return pool; }
        pool = Match(keyActive2, keyPassive2);
        if (pool.Count > 0) { rule = "stage1Key"; return pool; }
        // 보유한 것 레벨업 — 🔴 **액티브를 패시브보다 먼저 올린다**(사용자 결정 2026-09-21).
        //    종전엔 둘을 한 통에 넣고 무작위로 골라서, 패시브만 계속 올리고 액티브가 저레벨로 남는 판이 나왔다.
        //    실측에서 결과를 가르는 것은 스킬 쪽이다(스킬 격차 44%p vs 패시브 10.8%p, 219판).
        //    ⚠️ 액티브 레벨업 선택지가 없을 때만 패시브로 내려간다 — 패시브를 막는 게 아니다.
        List<int> levelUp(bool activeOnly) => all.Where(i =>
        {
            object o = opts.GetValue(i);
            if (OptField<bool>(o, "IsNew")) return false;
            bool isActive = OptField<ActiveSkillId?>(o, "SkillId").HasValue;
            bool isPassive = OptField<PassiveSkillId?>(o, "PassiveId").HasValue;
            return activeOnly ? isActive : (isActive || isPassive);
        }).ToList();

        pool = levelUp(true);
        if (pool.Count > 0) { rule = "levelUpActive"; return pool; }
        pool = levelUp(false);
        if (pool.Count > 0) { rule = "levelUpPassive"; return pool; }

        // 🔴 **되감기는 액티브 중 맨 뒤로 미룬다**(사용자 결정 2026-09-21).
        //    되감기는 피해가 0인 유틸이라 **사람이 쓰면 강한데 무작위 봇은 못 쓴다** — 슬롯만 먹는다.
        //    실측(219판, 5층까지 뽑은 스킬로 통제): 되감기를 초반에 뽑은 판이 **-9.3%p**, 같은 목표 안에서는
        //    해변 쉬움 **-25%p**로 전 스킬 최하위다. 이건 게임의 난이도가 아니라 **봇의 눈이 없는 것**이므로
        //    측정 편향으로 보고 정책을 고친다(balance §6 "봇이 사람보다 약한 축은 정책을 고쳐도 된다").
        //    ⚠️ **빼는 게 아니라 미루는 것**이다 — 되감기뿐인 선택지면 그대로 뽑아 빌드 다양성이 남는다.
        List<int> notRewind = all.Where(i =>
        {
            var sid = OptField<ActiveSkillId?>(opts.GetValue(i), "SkillId");
            return !(sid.HasValue && sid.Value == ActiveSkillId.Rewind);
        }).ToList();
        if (notRewind.Count > 0 && notRewind.Count < all.Count) { rule = "deferRewind"; return notRewind; }

        rule = "random";
        return all;
    }

    // 보유한 1차 진화 액티브의 2차 열쇠 중 **보유는 했지만 아직 진화 전**인 것 → 요구 루트. 진화 대상·루트 선택이 우선한다.
    // (이미 다른 루트로 진화했으면 되돌릴 수 없어 뺀다. 아예 없는 열쇠는 레벨업 우선순위 ①이 챙긴다.)
    private void NeededKeyRoutes(out Dictionary<ActiveSkillId, int> actives, out Dictionary<PassiveSkillId, int> passives)
    {
        actives = new Dictionary<ActiveSkillId, int>();
        passives = new Dictionary<PassiveSkillId, int>();
        PlayerSkills ps = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives pp = FindAnyObjectByType<PlayerPassives>();
        if (ps == null) return;
        foreach (EquippedSkill s in ps.EquippedSkills)
        {
            if (s.EvolutionStage != 1 || s.EvolutionStage >= EvolutionRoutes.MaxStageFor(s.Id)) continue;
            var k = EvolutionRoutes.Stage2Prereq(s.Id, s.Route);
            if (k.Active.HasValue)
            {
                EquippedSkill ks = ps.EquippedSkills.FirstOrDefault(x => x.Id == k.Active.Value);
                if (ks != null && ks.EvolutionStage == 0) actives[k.Active.Value] = k.Route;
            }
            if (k.Passive.HasValue && pp != null)
            {
                EquippedPassive kp = pp.GetPassive(k.Passive.Value);
                if (kp != null && kp.EvolutionStage == 0) passives[k.Passive.Value] = k.Route;
            }
        }
    }

    private void RecordPick(string screen, IEnumerable<object> offered, object picked, bool wasEvolution, string rule = null)
    {
        if (recorder.Picks == null) return;
        var d = BotJson.Obj();
        d["screen"] = screen;
        if (rule != null) d["rule"] = rule;
        d["offered"] = offered.ToList();
        d["picked"] = picked;
        d["wasEvolution"] = wasEvolution;
        d["stage"] = GameManager.Instance != null ? GameManager.Instance.CurrentStage : 0;
        d["gameTime"] = recorder.GameTime;
        recorder.Picks.Add(d);
    }

    private void DumpStuck()
    {
        string png = Path.Combine(cfg.sessionDir, "stuck_" + DateTime.Now.ToString("HHmmss") + ".png");
        ScreenCapture.CaptureScreenshot(png);
        var d = BotJson.Obj();
        d["scene"] = SceneManager.GetActiveScene().name;
        d["modalPaused"] = ModalPause.IsPaused;
        d["timeScale"] = Time.timeScale;
        LevelUpUI lu = LevelUpUI.Instance;
        if (lu != null)
            foreach (string f in new[] { "isOpen", "choiceOpen", "treasureMode", "evolutionMode" }) d["levelUp." + f] = Get<bool>(lu, f);
        if (EvolutionTreeUI.Instance != null) d["evolutionTree.isOpen"] = Get<bool>(EvolutionTreeUI.Instance, "isOpen");
        d["screenshot"] = png;
        Set("stuck", d);
    }

    // ───────────────────────── 상태 파일 ─────────────────────────
    private void Tick() => lastMainTickReal = Time.realtimeSinceStartup;

    private void SetProgress(int campaign, int run, BotGoal goal, string character)
    {
        Set("campaign", campaign); Set("run", run); Set("map", goal.map); Set("ascension", goal.ascension);
        Set("character", character);
        WriteStatus();
    }

    private void Set(string key, object value) => status[key] = value;

    // 메인 스레드가 동기 코드 안에서 멈추면 Update도 안 돌아 status.json·워치독·콘솔이 전부 멈춘다
    // (2026-09-18: 클리어 직후 7시간 멈춤. 마지막 로그가 "Game Clear"라 어느 단계인지 알 수 없었다).
    // 그래서 단계 이름만 그때그때 **동기로** 덧붙인다 — 다음에 멈추면 이 파일의 마지막 줄이 범인을 가리킨다.
    private void Trace(string step)
    {
        if (cfg == null || string.IsNullOrEmpty(cfg.sessionDir)) return;
        try
        {
            File.AppendAllText(Path.Combine(cfg.sessionDir, "trace.log"),
                DateTime.UtcNow.ToString("o") + " " + Time.realtimeSinceStartup.ToString("0.0") + " " + step + "\n");
        }
        catch (Exception) { }
    }

    private void WriteStatus()
    {
        lastStatusReal = Time.realtimeSinceStartup;
        if (cfg == null || string.IsNullOrEmpty(cfg.sessionDir)) return;
        status["heartbeatUtc"] = DateTime.UtcNow.ToString("o");
        status["realElapsed"] = Time.realtimeSinceStartup;
#if !UNITY_EDITOR
        status["audioVolume"] = AudioListener.volume; // QA 빌드 음소거 확인용(QAMute가 0으로 누른다)
#endif
        string path = Path.Combine(cfg.sessionDir, "status.json");
        try
        {
            // Replace는 원자적이다 — Delete→Move 사이 몇 ms 동안 파일이 없으면, 그 순간 읽은 감시자(QA 러너)가 무응답으로 오판한다.
            File.WriteAllText(path + ".tmp", BotJson.Write(status));
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
            else File.Move(path + ".tmp", path);
        }
        catch (Exception e) { Debug.LogWarning("[Bot] status 쓰기 실패: " + e.Message); }
    }

    private void Fail(string message)
    {
        Finish("error", message);
        throw new InvalidOperationException("[Bot] " + message);
    }

    private void Finish(string state, string error)
    {
        if (finished) return;
        finished = true;
        StopAllCoroutines();
        BotInput.HoldSkills = false;
        recorder?.Unsubscribe();
        Set("state", state);
        if (error != null) Set("error", error);
        Set("finishedUtc", DateTime.UtcNow.ToString("o"));
        WriteStatus();
        Time.captureDeltaTime = 0f;
        StopPlay();
    }

    private IEnumerator WaitReal(Func<bool> cond, float timeout, string what)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!cond())
        {
            if (Time.realtimeSinceStartup > end) Fail(what + " 대기 시간 초과(" + timeout + "초)");
            Tick();
            yield return null;
        }
    }

    // ───────────────────────── 리플렉션 ─────────────────────────
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private T Get<T>(object target, string field)
    {
        FieldInfo fi = target.GetType().GetField(field, Flags);
        if (fi == null) Fail(target.GetType().Name + "." + field + " 필드가 없다(이름 변경?)");
        return (T)fi.GetValue(target);
    }

    private void SetField(object target, string field, object value)
    {
        FieldInfo fi = target.GetType().GetField(field, Flags);
        if (fi == null) Fail(target.GetType().Name + "." + field + " 필드가 없다(이름 변경?)");
        fi.SetValue(target, value);
    }

    private object Prop(object target, string prop)
    {
        PropertyInfo pi = target.GetType().GetProperty(prop, Flags);
        if (pi == null) Fail(target.GetType().Name + "." + prop + " 속성이 없다(이름 변경?)");
        return pi.GetValue(target);
    }

    private void Call(object target, string method, params object[] args)
    {
        MethodInfo mi = target.GetType().GetMethod(method, Flags);
        if (mi == null) Fail(target.GetType().Name + "." + method + "() 가 없다(이름 변경?)");
        mi.Invoke(target, args);
    }

    // LevelUpUI.Option(private 중첩 클래스)·EvolutionTreeUI.NodeButton의 public 필드.
    private T OptField<T>(object obj, string field)
    {
        FieldInfo fi = obj.GetType().GetField(field, Flags);
        if (fi == null) Fail(obj.GetType().Name + "." + field + " 필드가 없다(이름 변경?)");
        object v = fi.GetValue(obj);
        return v == null ? default : (T)v;
    }
}
#endif
