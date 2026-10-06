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
    private ExpeditionLoadout expedition;   // 원정 모드에서 현재 판의 고정 로드아웃. 그 외엔 null — 픽 분기의 스위치다.

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
        // (게임 입력은 GameInput이 null을 견디지만 BotChaos의 ESC 주입이 키보드 장치에 이벤트를 넣는다) 그 가정은 맞다 — 봇 쪽 환경을 맞춘다.
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
        BotCastPolicy.Clear();
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
        else if (cfg.mode == "gym") yield return Gym();
        else if (cfg.mode == "expedition") yield return Expedition();
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

    // ───────────────────────── 2차 진화 원정 (expedition) ─────────────────────────
    // 한 판 = 로드아웃 항목 하나(목표 2차 진화 + 고정 픽). 픽·진화·리롤이 전부 expedition_loadouts.json을 따른다.
    // 판정 단위는 진화 차수별 구간이다 — BotRecorder의 effByEvoStage와 evolutions 이벤트(경계 시각)로 분석기가 가른다.
    private IEnumerator Expedition()
    {
        ExpeditionLoadout[] all = ExpeditionLoadoutFile.Load(Path.Combine(BotConfig.ProjectRoot, cfg.expeditionLoadouts));
        if (all == null || all.Length == 0) { Fail("원정 로드아웃이 없다: " + cfg.expeditionLoadouts); yield break; }
        ExpeditionLoadout[] items = cfg.expeditionItems != null && cfg.expeditionItems.Length > 0
            ? all.Where(l => cfg.expeditionItems.Contains(l.id)).ToArray() : all;
        if (items.Length == 0) { Fail("expeditionItems 필터에 맞는 항목이 없다"); yield break; }
        BotGoal goal = new BotGoal { map = cfg.expeditionMap, ascension = cfg.expeditionAscension };
        string[] mapNames = BotConfig.DefaultGoals.Select(g => g.map).Distinct().ToArray();

        var cells = new List<(ExpeditionLoadout item, int rep)>();
        foreach (ExpeditionLoadout it in items)
            for (int rep = 0; rep < cfg.expeditionRuns; rep++) cells.Add((it, rep));

        BotResumeState st = LoadResume("expedition");
        for (int i = st.nextExpeditionIndex; i < cells.Count; i++)
        {
            if (YieldRequested()) { st.nextExpeditionIndex = i; Yield(st); yield break; }
            var cell = cells[i];
            Trace("expedition cell " + i + " " + cell.item.id + " rep=" + cell.rep);
            BotTree.BuildReferenceSave(cfg.expeditionTreeRatio, mapNames);
            CharacterDefinition ch = BotTree.LoadByName<CharacterDefinition>(cell.item.character);
            if (ch == null) Fail("캐릭터 에셋 없음: " + cell.item.character);

            var header = BotJson.Obj();
            header["mode"] = "expedition"; header["label"] = cfg.label; header["run"] = i; header["cells"] = cells.Count;
            header["loadoutId"] = cell.item.id;
            header["targetSkill"] = cell.item.target.skill; header["targetRoute"] = cell.item.target.route;
            header["treeRatio"] = cfg.expeditionTreeRatio; header["map"] = goal.map; header["ascension"] = goal.ascension;
            header["character"] = cell.item.character; header["rep"] = cell.rep;

            expedition = cell.item;
            SetProgress(0, i, goal, cell.item.character);
            yield return EnterRun(goal, ch);
            Dictionary<string, object> run = null;
            yield return PlayBattle(header, r => run = r);
            expedition = null;
            if ((string)run["result"] == "yielded") { st.nextExpeditionIndex = i; Yield(st); yield break; }
            recorder.WriteRun(run);
            st.nextExpeditionIndex = i + 1;
            SaveResume(st);
            if ((string)run["result"] == "stuck") Fail("판이 멈춤 — stuck_*.png 참고");
        }
    }

    // ───────────────────────── 스킬 시험장 (gym) ─────────────────────────
    // 격자 한 칸 = 한 셀. 셀은 45~60 게임초라 정주행(판 10~20분)과 달리 **판을 새로 열지 않고** 무대만 비워 이어 돈다.
    // 그래서 셀 경계 양보가 거의 즉시다(`BotRuns/yield`를 쓰면 몇 초 안에 Unity가 빈다).
    //
    // 🔴 판정에 쓰는 지표는 정주행과 **같은 정의**다(BotRecorder를 그대로 쓴다) — 정의가 갈리면 두 측정을 못 붙인다.
    //    다른 것은 무대(시나리오가 적을 선언)와 로드아웃(GymRig이 상태를 강제)뿐이다.
    private IEnumerator Gym()
    {
        List<GymRig.Cell> cells = BuildGymCells();
        Set("gymCells", cells.Count);
        Trace("gym 격자 " + cells.Count + "셀");

        BotResumeState st = LoadResume("gym");
        GymArena arena = null;
        int sinceReload = 0;

        for (int i = st.nextGymIndex; i < cells.Count; i++)
        {
            if (YieldRequested()) { st.nextGymIndex = i; Yield(st); yield break; }
            GymRig.Cell c = cells[i];

            bool needStage = arena == null
                || (cfg.gymReloadEveryCells > 0 && sinceReload >= cfg.gymReloadEveryCells);
            if (needStage)
            {
                Trace("gym 무대 진입 (셀 " + i + ")");
                yield return EnterGymStage();
                arena = new GymArena();
                if (!arena.TakeOver()) Fail("시험장 무대 준비 실패 — EnemySpawner를 못 찾았다");
                // 🔴 프리팹을 못 찾은 시나리오는 **조용히 빈 칸**이 된다. 세션 시작에 한 번 세어 status에 남긴다.
                if (arena.MissingPrefabs.Count > 0)
                    Set("gymMissingPrefabs", string.Join(",", arena.MissingPrefabs));
                // 발생기 필드 이름이 바뀌면 오염이 조용히 돌아온다 — 이름을 못 찾았으면 여기 뜬다.
                if (arena.MissingEmitterFields.Count > 0)
                    Set("gymMissingEmitterFields", string.Join(",", arena.MissingEmitterFields));
                sinceReload = 0;
            }

            var header = BotJson.Obj();
            header["mode"] = "gym"; header["label"] = cfg.label; header["run"] = i; header["cells"] = cells.Count;
            header["map"] = cfg.gymMap; header["ascension"] = cfg.gymAscension; header["character"] = GymCharacter;
            header["skill"] = c.skill.ToString(); header["evoStage"] = c.stage;
            header["route"] = c.stage > 0 ? c.route : -1;
            header["askLevel"] = c.level; header["treeMode"] = c.treeMode; header["rig"] = c.rig;
            header["scenario"] = c.scenario; header["growthCasts"] = c.growthCasts; header["repeat"] = c.repeat;
            header["cellKey"] = c.Key; header["stateKey"] = c.StateKey;
            // 🔴 동반 릭으로 재는 스킬 목록을 **모든 셀에** 적는다. 분석기가 이 목록을 단독 판정에서 빼야 하는데,
            //    동반 셀은 격자 맨 뒤라 "데이터에 있는 rig=companion"으로 유도하면 그 셀이 돌기 전까지 판정이 틀린다.
            //    목록을 분석기에 또 적으면 사본이 둘이 되어 한쪽만 낡는다(CLAUDE.md §7) → 여기서 한 번만 내보낸다.
            header["companionPool"] = cfg.gymCompanion
                ? string.Join(",", CompanionTestSkills.Select(s => s.ToString())) : "";

            Set("gymCell", i); Set("gymCellKey", c.Key); WriteStatus();

            Dictionary<string, object> run = null;
            yield return PlayGymCell(c, arena, header, x => run = x);
            if ((string)run["result"] == "yielded") { st.nextGymIndex = i; Yield(st); yield break; }

            recorder.WriteRunTo("gym.jsonl", run);
            sinceReload++;
            st.nextGymIndex = i + 1;
            SaveResume(st);
        }
        Trace("gym 격자 완료");
    }

    private const string GymCharacter = "Char_Strawberry";

    // 무대 진입. 시험장은 체력을 잠그고 스킬을 강제하므로 캐릭터·맵은 **무대의 크기와 레인**만 제공한다.
    // 🔴 GymRig가 셀마다 세이브를 비우므로(맵 해금도 같이 지워진다) 진입 직전에 반드시 다시 열어야 한다.
    private IEnumerator EnterGymStage()
    {
        string[] mapNames = BotConfig.DefaultGoals.Select(g => g.map).Distinct().ToArray();
        BotTree.BuildReferenceSave(0f, mapNames);   // 빈 트리 + 맵·난이도 해금
        CharacterDefinition ch = BotTree.LoadByName<CharacterDefinition>(GymCharacter);
        if (ch == null) Fail("캐릭터 에셋 없음: " + GymCharacter);
        yield return EnterRun(new BotGoal { map = cfg.gymMap, ascension = cfg.gymAscension }, ch);
    }

    // 한 셀: 무대 비우기 → 정착 → 로드아웃 강제 → 고정 창만큼 측정.
    private IEnumerator PlayGymCell(GymRig.Cell c, GymArena arena,
        Dictionary<string, object> header, Action<Dictionary<string, object>> done)
    {
        GymArena.Def sc = GymArena.Find(c.scenario);
        if (sc == null) Fail("시나리오가 표에 없다: " + c.scenario);

        // ① 앞 셀의 적과 **깔아 둔 지속 오브젝트**를 치우고 정착 시간을 둔다.
        //    🔴 `PlayerSkills.Sealed`를 켜는 이유: 자동 시전 스킬(스나이핑 R1 2차·화살 R2 3차는 `IsAutoCastOnly`)은
        //       HoldSkills를 꺼도 스스로 발동해 정착 중에 **새 지속 오브젝트를 다시 깐다.** Sealed는 쿨도 멈춘다.
        //    아직 recorder를 켜지 않았으므로 이 구간의 타격은 어느 셀에도 안 들어간다.
        BotInput.HoldSkills = false;
        PlayerSkills.Sealed = true;
        arena.Clear();
        float settle = 0f;
        while (settle < cfg.gymSettleSeconds)
        {
            Tick();
            settle += Time.deltaTime;
            yield return null;
        }
        // 🔴 정착 중에 새로 생긴 것(자동 시전·발생기 잔재)을 한 번 더 치운다. Destroy는 프레임 끝에 실행되므로
        //    한 번만 치우고 바로 재면 그 프레임에 태어난 것이 살아남는다 — 실측으로 오염이 계속 1칸 남았다.
        arena.Clear();
        for (int f = 0; f < 3; f++) { Tick(); yield return null; }
        PlayerSkills.Sealed = false;

        // ② 로드아웃 강제. 실패하면 그 셀은 에러로 기록하고 넘어간다(세션을 멈추지 않는다 —
        //    한 조합이 안 되는 것과 격자 전체가 못 도는 것은 다르다).
        GymRig.Result rig = GymRig.Apply(c);
        header["rigOk"] = rig.ok;
        header["rigError"] = rig.error;
        header["gotStage"] = rig.stage; header["gotRoute"] = rig.route; header["gotLevel"] = rig.level;
        header["gotGrowthStacks"] = rig.growthStacks;
        header["gotBaseCooldown"] = rig.baseCooldown; header["gotBaseDamage"] = rig.baseDamage;
        header["enhanceNodes"] = rig.enhanceNodes;
        header["loadout"] = rig.loadout;

        // ③ 측정
        recorder.BeginRun(header);
        // 🔴 무대 세기는 진화 차수에 따라 커진다(GymArena.GroupHpScale) — 한 무대로 250배 파워 범위를 담을 수 없다.
        //    그래서 처리량 무대의 숫자는 **그룹 안에서만** 비교한다. 차수 간 비교는 dps 무대와 생애 사슬로 한다.
        arena.Begin(sc, GymArena.GroupHpScale(c.stage));
        int residual = arena.Residual();   // 0이 아니면 Clear가 못 치운 것이 있다 — 원인 쪽 지표
        float t = 0f;
        float realCap = Time.realtimeSinceStartup + Mathf.Max(60f, sc.window * 6f);
        string result = "cell";
        while (rig.ok && t < sc.window)
        {
            Tick();
            GameManager gm = GameManager.Instance;
            if (gm == null) { result = "error"; lastException = "시험장 중 GameManager가 사라짐"; break; }
            if (gm.IsGameOver) { result = "dead"; break; }      // 체력을 잠갔으니 나면 잠금이 깨진 것이다
            if (gm.IsGameClear) { result = "cleared"; break; }   // 스테이지가 넘어갔다 = 스포너 인수가 풀렸다
            if (YieldNow()) { result = "yielded"; break; }
            if (Time.realtimeSinceStartup > realCap) { result = "timeout"; break; }

            BotInput.HoldSkills = true;
            recorder.Tick();
            arena.Tick(Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }
        if (!rig.ok) result = "rigFailed";
        BotInput.HoldSkills = false;

        Dictionary<string, object> run = recorder.EndRun(result);
        run["window"] = sc.window;
        run["cellGameTime"] = t;
        run["residualAtStart"] = residual;
        // 무엇이 남았는지 이름으로 남긴다 — 개수만으로는 무대 청소에 무엇을 더해야 하는지 알 수 없다.
        run["residualNames"] = residual > 0 ? string.Join(",", arena.ResidualNames) : null;
        run["spawned"] = arena.Spawned;
        run["spawnedHp"] = arena.SpawnedHp;
        run["aliveAtEnd"] = arena.Alive;
        run["scenarioNote"] = sc.note;
        run["hpMult"] = arena.EffectiveHpMult;   // 시나리오 기본값 × 차수 배율 — 그룹 간 비교를 막는 근거가 이 값이다
        run["hpMultBase"] = sc.hpMult;
        run["hpGroupScale"] = GymArena.GroupHpScale(c.stage);
        run["arenaKind"] = sc.Dps ? "dps" : "throughput";
        run["maxAlive"] = sc.maxAlive;
        int kills = ((List<object>)run["stages"]).Cast<Dictionary<string, object>>().Sum(s => Convert.ToInt32(s["kills"]));
        run["kills"] = kills;
        run["killRate"] = arena.Spawned > 0 ? (float)kills / arena.Spawned : 0f;
        // 🔴 **포화율** — 판정 전에 반드시 보는 값. 이 셀의 총 유효딜 ÷ 무대가 내보낸 총 체력.
        //    1에 가까우면 스킬이 무대를 비워 버린 것이고, 그 셀은 더 센 스킬과 **같은 숫자**를 낸다(아무것도 못 가른다).
        //    throughput 무대는 0.9 미만, dps 무대는 표적이 안 죽었으므로 구조적으로 작다.
        var skillRows = ((List<object>)run["skills"]).Cast<Dictionary<string, object>>().ToList();
        float totalEff = skillRows.Sum(s => Convert.ToSingle(s["effDamage"]));
        run["saturation"] = arena.SpawnedHp > 0f ? totalEff / arena.SpawnedHp : 0f;
        // 🔴 **오염 감시** — 로드아웃에 없는(owned=false) 출처가 낸 딜. 앞 셀의 지속 오브젝트가 살아남으면 여기 잡힌다.
        //    0이 아니면 그 셀은 판정에 쓸 수 없다. 분석기가 이 값으로 셀을 걸러낸다.
        run["foreignDamage"] = skillRows
            .Where(s => !Convert.ToBoolean(s["owned"]) && s["id"] as string != "Other")
            .Sum(s => Convert.ToSingle(s["effDamage"]));
        if (result == "error") Fail(lastException);
        done(run);
    }

    // 격자 만들기. 축 순서가 곧 도는 순서다 — 같은 상태의 시나리오들이 붙어 돌아 오염이 있으면 눈에 띈다.
    private List<GymRig.Cell> BuildGymCells()
    {
        var skills = (cfg.gymSkills != null && cfg.gymSkills.Length > 0
                ? cfg.gymSkills.Select(s => (ActiveSkillId)Enum.Parse(typeof(ActiveSkillId), s, true))
                : Enum.GetValues(typeof(ActiveSkillId)).Cast<ActiveSkillId>()).ToArray();
        string[] scenarios = cfg.gymScenarios != null && cfg.gymScenarios.Length > 0
            ? cfg.gymScenarios : GymArena.Table.Select(d => d.id).ToArray();
        int[] levels = cfg.gymLevels != null && cfg.gymLevels.Length > 0 ? cfg.gymLevels : GymArena.DefaultLevels;
        string[] treeModes = cfg.gymTreeModes != null && cfg.gymTreeModes.Length > 0
            ? cfg.gymTreeModes : new[] { GymRig.TreeFull };

        // 상태 = 진화 전 + 루트{0,1} × 차수{1,2}. 문자열 이름은 요청·보고서가 쓰는 좌표다.
        var states = new List<(string name, int stage, int route)> { ("pre", 0, -1) };
        for (int route = 0; route < 2; route++)
            for (int stage = 1; stage <= EvolutionRoutes.MaxStage; stage++)
                states.Add(($"r{route}t{stage}", stage, route));
        if (cfg.gymStates != null && cfg.gymStates.Length > 0)
            states = states.Where(s => cfg.gymStates.Contains(s.name)).ToList();

        var cells = new List<GymRig.Cell>();
        for (int rep = 0; rep < Mathf.Max(1, cfg.gymRepeats); rep++)
        {
            foreach (ActiveSkillId skill in skills)
                foreach (var st in states)
                    foreach (string tree in treeModes)
                        foreach (int level in levels)
                        {
                            // 호밍만 누적 스택 축을 돈다 — 다른 스킬은 판 중에 세지지 않으므로 0 하나면 충분하다.
                            int[] growth = GymRig.GrowsDuringRun(skill) && cfg.gymGrowthCasts != null
                                            && cfg.gymGrowthCasts.Length > 0
                                ? cfg.gymGrowthCasts : new[] { 0 };
                            foreach (int g in growth)
                                foreach (string scn in scenarios)
                                    cells.Add(new GymRig.Cell
                                    {
                                        skill = skill, stage = st.stage, route = st.route, level = level,
                                        treeMode = tree, scenario = scn, growthCasts = g, repeat = rep,
                                    });
                        }

            // Rig 2(동반) — 되감기·산탄·낙뢰는 혼자 두면 딜이 0이다. 기준 3스킬과 함께 넣고 baseline과의 차이를 본다.
            if (!cfg.gymCompanion) continue;
            string[] compScenarios = GymArena.Table.Where(d => d.companion).Select(d => d.id)
                .Where(id => scenarios.Contains(id)).ToArray();
            foreach (string scn in compScenarios)
                foreach (var st in states)
                {
                    // 🔴 baseline은 **그 상태와 같은 무대**에서 재야 한다. 무대 세기가 진화 차수로 정해지므로
                    //    (GymArena.GroupHpScale — 차수마다 ×1·×3·×9) baseline을 stage 0 하나로 두면
                    //    진화 상태 동반 셀은 9배 단단한 적을 상대하고, 그 차이가 "스킬 덕에 딜이 늘었다"로 잘못 읽힌다.
                    //    (실측 2026-09-27: 되감기 R0 2차가 +126%로 나왔는데 전부 무대 차이였다.)
                    //    baseline은 rig가 "baseline"이라 시험 대상을 안 넣으므로 로드아웃은 stage와 무관하게 기준 3개 그대로다.
                    cells.Add(new GymRig.Cell
                    {
                        skill = ActiveSkillId.BasicAttack, rig = "baseline", stage = st.stage, route = st.route,
                        level = BalanceConstants.MaxSkillLevel, treeMode = GymRig.TreeBare,
                        scenario = scn, repeat = rep,
                    });
                    foreach (ActiveSkillId util in CompanionTestSkills)
                    {
                        if (!skills.Contains(util)) continue;
                        cells.Add(new GymRig.Cell
                        {
                            skill = util, rig = "companion", stage = st.stage, route = st.route,
                            level = BalanceConstants.MaxSkillLevel, treeMode = GymRig.TreeFull,
                            scenario = scn, repeat = rep,
                        });
                    }
                }
        }
        return cells;
    }

    // 혼자서는 딜이 거의 0인 것들 — 되감기(남의 쿨을 당긴다) · 산탄(집중 산탄 루트는 버프기) · 낙뢰(IsBuffSkill).
    private static readonly ActiveSkillId[] CompanionTestSkills =
        { ActiveSkillId.Rewind, ActiveSkillId.Shotgun, ActiveSkillId.Lightning };

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
        BotCastPolicy.Install();                       // 시전 판단(스킬별 발사 허가) — 전투 동안만
        header["castPolicy"] = BotCastPolicy.Description;
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
        BotCastPolicy.Clear();
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
                    if (expedition != null)
                    {
                        // 원정: 로드아웃 evolvePriority 순서 — 순번이 가장 낮은 스텝의 대상만 남긴다.
                        var ranked = pool.Select(i => (i, rank: ExpeditionEvoRank(opts.GetValue(i))))
                                         .Where(t => t.rank < int.MaxValue).ToList();
                        if (ranked.Count > 0)
                        {
                            int best = ranked.Min(t => t.rank);
                            pool = ranked.Where(t => t.rank == best).Select(t => t.i).ToList();
                            rule = "expeditionEvolve";
                        }
                    }
                    else
                    {
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
                }
                else if (expedition != null)
                {
                    pool = ExpeditionPool(opts, out rule, out bool wantReroll);
                    // 로드아웃 미완성인데 목록의 새 카드가 안 나왔으면 리롤(스킬트리 리롤 잔여가 있을 때만 버튼이 산다).
                    if (wantReroll)
                    {
                        Button rr = Get<Button>(lu, "rerollButton");
                        if (Usable(rr))
                        {
                            var offeredR = new List<object>();
                            for (int i = 0; i < opts.Length; i++) offeredR.Add(DescribeOption(opts.GetValue(i)));
                            RecordPick("levelUp", offeredR, "reroll", false, "expeditionReroll");
                            rr.onClick.Invoke();
                            return Acted();
                        }
                    }
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
            if (usable.Count == 0)
            {
                // 살 수 있는 2차 노드가 없다(열쇠 미충족 등) — 정상 플레이어처럼 취소로 닫고 판을 계속한다.
                // 취소 버튼(backButton)은 진화 창이 늘 onCancel과 함께 열려 활성이다(LevelUpUI.ResolveEvolutionChoice).
                // 없으면(취소 불가) 예전대로 대기 → 상위 stuck 감지가 잡는다.
                Button back = Get<Button>(et, "backButton");
                if (Usable(back)) { back.onClick.Invoke(); return Acted(); }
                return false;
            }
            EquippedSkill s = Get<EquippedSkill>(et, "currentSkill");
            EquippedPassive p = Get<EquippedPassive>(et, "currentPassive");
            // 원정이면 로드아웃 evolvePriority가 루트를 정한다. 스텝이 없으면 아래 열쇠 로직으로 떨어진다.
            int wantRoute = -1;
            string routeRule = "random";
            if (expedition != null)
            {
                wantRoute = s != null ? expedition.RouteFor(false, s.Id.ToString(), s.EvolutionStage + 1)
                          : p != null ? expedition.RouteFor(true, p.Id.ToString(), p.EvolutionStage + 1) : -1;
                if (wantRoute >= 0) routeRule = "expeditionRoute";
            }
            if (wantRoute < 0)
            {
                // 이 스킬이 다른 스킬의 2차 열쇠면 **열쇠가 요구하는 루트**로 진화시킨다(열쇠는 루트까지 맞아야 한다).
                NeededKeyRoutes(out var keyA, out var keyP);
                wantRoute = s != null && keyA.TryGetValue(s.Id, out int ra) ? ra
                          : p != null && keyP.TryGetValue(p.Id, out int rp) ? rp : -1;
                if (wantRoute >= 0) routeRule = "keyRoute";
            }
            var keyed = usable.Where(u => u.route == wantRoute).ToList();
            var pick = keyed.Count > 0 ? keyed[rng.Next(keyed.Count)] : usable[rng.Next(usable.Count)];
            RecordPick("evolutionRoute", usable.Select(u => (object)u.route).Distinct().ToList(),
                (s != null ? s.Id.ToString() : p != null ? "P:" + p.Id : "?") + ":R" + pick.route, true,
                keyed.Count > 0 ? routeRule : "random");
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

    // ───────────────────────── 원정 픽 (expedition_loadouts.json이 정한다) ─────────────────────────
    // 진화 선택지의 로드아웃 순번. 다음 티어 = 그 스킬의 현재 차수 + 1로 스텝을 찾는다. 목록 밖이면 MaxValue.
    private int ExpeditionEvoRank(object opt)
    {
        var sid = OptField<ActiveSkillId?>(opt, "SkillId");
        var pid = OptField<PassiveSkillId?>(opt, "PassiveId");
        if (sid.HasValue)
        {
            PlayerSkills ps = FindAnyObjectByType<PlayerSkills>();
            EquippedSkill s = ps != null ? ps.EquippedSkills.FirstOrDefault(x => x.Id == sid.Value) : null;
            return s == null ? int.MaxValue : expedition.EvolveRank(false, sid.Value.ToString(), s.EvolutionStage + 1);
        }
        if (pid.HasValue)
        {
            PlayerPassives pp = FindAnyObjectByType<PlayerPassives>();
            EquippedPassive p = pp != null ? pp.GetPassive(pid.Value) : null;
            return p == null ? int.MaxValue : expedition.EvolveRank(true, pid.Value.ToString(), p.EvolutionStage + 1);
        }
        return int.MaxValue;
    }

    // 레벨업 3택(원정): ① 로드아웃의 미보유 카드(적힌 순서 — 액티브 먼저) ② 목표 스킬 레벨업(진화는 만렙 요구)
    // ③ 다음 진화 스텝 스킬의 레벨업 ④ 로드아웃 액티브 레벨업 ⑤ 아무 레벨업 ⑥ 아무 카드(칸 굶주림 방지).
    // wantReroll: 로드아웃이 미완성인데 ①이 없을 때 — 호출부가 리롤 버튼을 시도한다.
    private List<int> ExpeditionPool(Array opts, out string rule, out bool wantReroll)
    {
        var all = Enumerable.Range(0, opts.Length).ToList();
        PlayerSkills ps = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives pp = FindAnyObjectByType<PlayerPassives>();

        var newRanked = all.Select(i =>
        {
            object o = opts.GetValue(i);
            int rank = OptField<bool>(o, "IsNew")
                ? expedition.PickRank(OptField<ActiveSkillId?>(o, "SkillId"), OptField<PassiveSkillId?>(o, "PassiveId"))
                : -1;
            return (i, rank);
        }).Where(t => t.rank >= 0).ToList();
        if (newRanked.Count > 0)
        {
            int best = newRanked.Min(t => t.rank);
            rule = "expeditionNew"; wantReroll = false;
            return newRanked.Where(t => t.rank == best).Select(t => t.i).ToList();
        }
        wantReroll = expedition.Incomplete(ps, pp);

        List<int> LevelUpOf(Func<object, bool> match) => all.Where(i =>
        {
            object o = opts.GetValue(i);
            return !OptField<bool>(o, "IsNew") && match(o);
        }).ToList();

        List<int> pool = LevelUpOf(o =>
        {
            var sid = OptField<ActiveSkillId?>(o, "SkillId");
            return sid.HasValue && sid.Value == expedition.TargetSkill;
        });
        if (pool.Count > 0) { rule = "expeditionTargetLevel"; return pool; }

        ExpeditionEvolveStep next = NextEvolveStep(ps, pp);
        if (next != null)
        {
            pool = LevelUpOf(o =>
            {
                var sid = OptField<ActiveSkillId?>(o, "SkillId");
                var pid = OptField<PassiveSkillId?>(o, "PassiveId");
                return (sid.HasValue && !string.IsNullOrEmpty(next.skill) && sid.Value.ToString() == next.skill)
                    || (pid.HasValue && !string.IsNullOrEmpty(next.passive) && pid.Value.ToString() == next.passive);
            });
            if (pool.Count > 0) { rule = "expeditionKeyLevel"; return pool; }
        }

        pool = LevelUpOf(o =>
        {
            var sid = OptField<ActiveSkillId?>(o, "SkillId");
            return sid.HasValue && expedition.ListsActive(sid.Value);
        });
        if (pool.Count > 0) { rule = "expeditionActiveLevel"; return pool; }

        pool = LevelUpOf(o => OptField<ActiveSkillId?>(o, "SkillId").HasValue || OptField<PassiveSkillId?>(o, "PassiveId").HasValue);
        if (pool.Count > 0) { rule = "expeditionAnyLevel"; return pool; }

        rule = "expeditionOffTable";   // 목록 밖 새 카드뿐 — 리롤이 안 살면 이거라도 집는다
        return all;
    }

    // 로드아웃 evolvePriority에서 아직 안 끝난 첫 스텝. 전부 끝났으면 null.
    private ExpeditionEvolveStep NextEvolveStep(PlayerSkills ps, PlayerPassives pp)
    {
        foreach (ExpeditionEvolveStep step in expedition.evolvePriority)
        {
            if (!string.IsNullOrEmpty(step.skill))
            {
                EquippedSkill eq = ps != null ? ps.EquippedSkills.FirstOrDefault(x => x.Id.ToString() == step.skill) : null;
                if (eq == null || eq.EvolutionStage < step.tier) return step;
            }
            else if (!string.IsNullOrEmpty(step.passive))
            {
                EquippedPassive eq = pp != null
                    ? pp.EquippedPassives.FirstOrDefault(x => x.Id.ToString() == step.passive) : null;
                if (eq == null || eq.EvolutionStage < step.tier) return step;
            }
        }
        return null;
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
