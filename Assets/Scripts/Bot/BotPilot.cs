#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 봇 플레이테스트 본체. `BotRuns/active/session.json`이 있을 때만 플레이모드 시작에 생성된다(BotLauncher가 만든다).
//
// 정책(사용자 지정 — 바꾸면 이전 측정과 비교할 수 없게 되므로 `balance` 스킬부터 볼 것):
//   · QWER 꾹(BotInput.HoldSkills) · 레벨업 카드는 균등 무작위, 리롤 안 씀
//   · 진화할 수 있으면 무조건 진화(보물 갈림길 → 진화), 무엇을·어느 루트로는 무작위
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        BotInput.HoldSkills = false;
        cfg = BotConfig.LoadActive();
        if (cfg == null)
        {
            SaveStore.UseProfile(null); // 도메인 리로드가 꺼져 있어도 지난 봇 세션의 프로필이 남지 않게
            return;
        }
        SaveStore.UseProfile("bot");
        var go = new GameObject("[BotPilot]");
        DontDestroyOnLoad(go);
        go.AddComponent<BotPilot>();
    }

    private void Awake()
    {
        rng = new System.Random(cfg.seed);
        UnityEngine.Random.InitState(cfg.seed);
        recorder = new BotRecorder(cfg.sessionDir);
        Time.captureDeltaTime = Mathf.Max(0f, cfg.simStep);
        Application.runInBackground = true;
        Application.logMessageReceived += OnLog;
        status["label"] = cfg.label;
        status["mode"] = cfg.mode;
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
            EditorApplication.isPlaying = false;
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (now - lastStatusReal > 5f) WriteStatus();
        // 메인 코루틴이 예외로 죽으면 Unity는 조용히 멈춘다 — 심장박동이 끊긴 것으로 잡는다.
        if (!finished && now - lastMainTickReal > Mathf.Max(90f, cfg.stuckRealSeconds * 1.5f))
            Finish("error", "메인 코루틴이 멈췄다" + (lastException != null ? ": " + lastException : ""));
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
        for (int c = 0; c < cfg.campaigns; c++)
        {
            BotTree.ResetSave();
            int[] attempts = new int[goals.Length];
            var firstClear = new Dictionary<int, Dictionary<string, object>>();
            Dictionary<string, object> allNodesAt = null;
            int runs = 0, cumPicks = 0;
            float cumGame = 0f;
            string lastChar = null;
            string outcome;

            while (true)
            {
                int gi = Array.FindIndex(goals, g => !MapClearSave.HasCleared(g.map, g.ascension));
                bool allNodes = BotTree.AllMaxed();
                if (gi < 0 && allNodes) { outcome = "complete"; break; }
                if (gi >= 0 && attempts[gi] >= cfg.maxAttemptsPerGoal) { outcome = "stuckGoal"; break; }
                if (runs >= cfg.maxRunsPerCampaign) { outcome = "runCap"; break; }

                int target = gi >= 0 ? gi : goals.Length - 1; // 전부 깼으면 남은 노드를 위해 마지막 목표 반복
                if (gi >= 0) attempts[gi]++;
                CharacterDefinition ch = NextCharacter(ref lastChar);

                var header = BotJson.Obj();
                header["mode"] = "campaign"; header["label"] = cfg.label; header["campaign"] = c; header["run"] = runs;
                header["goalIndex"] = target; header["map"] = goals[target].map; header["ascension"] = goals[target].ascension;
                header["character"] = ch.name; header["attempt"] = gi >= 0 ? attempts[gi] : 0; header["farming"] = gi < 0;
                header["nodesOwnedBefore"] = BotTree.OwnedLevels(); header["nodesTotal"] = BotTree.TotalLevels();
                header["spentBefore"] = BotTree.Spent(); header["treeTotalCost"] = BotTree.TotalCost();
                header["essenceEarnedBefore"] = SkillTreeSave.EssenceEarned;

                SetProgress(c, runs, goals[target], ch.name);
                yield return EnterRun(goals[target], ch);
                Dictionary<string, object> run = null;
                yield return PlayBattle(header, r => run = r);

                run["purchases"] = BotTree.BuyCheapestFirst(rng);
                run["nodesOwnedAfter"] = BotTree.OwnedLevels();
                run["spentAfter"] = BotTree.Spent();
                cumGame += (float)run["gameTime"];
                cumPicks += ((List<object>)run["picks"]).Count;
                run["cumGameTime"] = cumGame;
                run["cumPicks"] = cumPicks;
                runs++;
                run["cumRuns"] = runs;

                if ((string)run["result"] == "clear" && gi >= 0 && !firstClear.ContainsKey(gi))
                {
                    var fc = BotJson.Obj();
                    fc["attempts"] = attempts[gi]; fc["cumRuns"] = runs; fc["cumGameTime"] = cumGame; fc["cumPicks"] = cumPicks;
                    fc["nodesOwned"] = BotTree.OwnedLevels();
                    firstClear[gi] = fc;
                }
                if (allNodesAt == null && BotTree.AllMaxed())
                {
                    allNodesAt = BotJson.Obj();
                    allNodesAt["cumRuns"] = runs; allNodesAt["cumGameTime"] = cumGame; allNodesAt["cumPicks"] = cumPicks;
                }
                recorder.WriteRun(run);
                if ((string)run["result"] == "stuck") Fail("판이 멈춤 — stuck_*.png와 runs.jsonl 마지막 줄 참고");
            }

            var summary = BotJson.Obj();
            summary["label"] = cfg.label; summary["campaign"] = c; summary["outcome"] = outcome;
            summary["runs"] = runs; summary["cumGameTime"] = cumGame; summary["cumPicks"] = cumPicks;
            summary["allNodesAt"] = allNodesAt;
            var goalRows = new List<object>();
            for (int i = 0; i < goals.Length; i++)
            {
                var row = BotJson.Obj();
                row["map"] = goals[i].map; row["ascension"] = goals[i].ascension; row["attempts"] = attempts[i];
                row["firstClear"] = firstClear.TryGetValue(i, out var fc) ? fc : null;
                goalRows.Add(row);
            }
            summary["goals"] = goalRows;
            File.AppendAllText(Path.Combine(cfg.sessionDir, "campaigns.jsonl"), BotJson.Write(summary) + "\n");
        }
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

        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            BotTree.BuildReferenceSave(cell.ratio, mapNames);
            CharacterDefinition ch = BotTree.LoadByName<CharacterDefinition>(cell.ch);
            if (ch == null) Fail("캐릭터 에셋 없음: " + cell.ch);

            var header = BotJson.Obj();
            header["mode"] = "probe"; header["label"] = cfg.label; header["run"] = i; header["cells"] = cells.Count;
            header["treeRatio"] = cell.ratio; header["map"] = cell.goal.map; header["ascension"] = cell.goal.ascension;
            header["character"] = cell.ch; header["rep"] = cell.rep;
            header["nodesOwnedBefore"] = BotTree.OwnedLevels(); header["nodesTotal"] = BotTree.TotalLevels();
            header["spentBefore"] = BotTree.Spent(); header["treeTotalCost"] = BotTree.TotalCost();

            SetProgress(0, i, cell.goal, cell.ch);
            yield return EnterRun(cell.goal, ch);
            Dictionary<string, object> run = null;
            yield return PlayBattle(header, r => run = r);
            recorder.WriteRun(run);
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
        if (SceneManager.GetActiveScene().name != TitleScene)
        {
            if (GameManager.Instance != null) GameManager.Instance.ReturnToTitle();
            else SceneFade.LoadScene(TitleScene);
        }
        yield return WaitReal(() => SceneManager.GetActiveScene().name == TitleScene && FindAnyObjectByType<TitleController>() != null, 60f, "타이틀 로드");
        yield return new WaitForSecondsRealtime(1.2f);
        Tick();

        TitleController title = FindAnyObjectByType<TitleController>();
        Get<Button>(title, "playButton").onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.4f);
        Tick();

        var cs = Get<CharacterSelectUI>(title, "characterSelect");
        var chars = Get<CharacterDefinition[]>(cs, "characters");
        int ci = Array.FindIndex(chars, c => c != null && c.name == ch.name);
        if (ci < 0) Fail("CharacterSelectUI.characters에 " + ch.name + " 없음");
        Call(cs, "Pick", ci);
        if (cs.Selected != chars[ci]) Fail("캐릭터 선택이 거부됨(잠김?): " + ch.name);
        Get<Button>(cs, "confirmButton").onClick.Invoke();
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
        Button start = Get<Button>(ms, "startButton");
        if (!start.interactable) Fail("시작 버튼 비활성: " + goal.map);
        start.onClick.Invoke();

        yield return WaitReal(() => SceneManager.GetActiveScene().name == BattleScene && GameManager.Instance != null
                                     && FindAnyObjectByType<PlayerSkills>() != null, 60f, "전투 로드");
        yield return new WaitForSecondsRealtime(0.3f);
        if (RunConfig.Map == null || RunConfig.Map.name != goal.map || RunConfig.AscensionLevel != goal.ascension
            || RunConfig.Character == null || RunConfig.Character.name != ch.name)
            Fail("RunConfig가 요청과 다름 — 선택 화면 흐름이 바뀌었는지 확인");
    }

    // ───────────────────────── 전투 ─────────────────────────
    private IEnumerator PlayBattle(Dictionary<string, object> header, Action<Dictionary<string, object>> done)
    {
        header["seed"] = cfg.seed;
        recorder.BeginRun(header);
        float runStartReal = Time.realtimeSinceStartup;
        float lastProgressReal = runStartReal;
        float lastGameTime = 0f;
        string result;

        while (true)
        {
            Tick();
            GameManager gm = GameManager.Instance;
            if (gm == null) { result = "error"; lastException = "전투 중 GameManager가 사라짐"; break; }
            if (gm.IsGameClear) { result = "clear"; break; }
            if (gm.IsGameOver) { result = "dead"; break; }

            BotInput.HoldSkills = true;
            recorder.Tick();
            bool acted = HandleModals();

            float now = Time.realtimeSinceStartup;
            if (acted || recorder.GameTime > lastGameTime + 0.0001f) { lastProgressReal = now; lastGameTime = recorder.GameTime; }
            if (now - lastProgressReal > cfg.stuckRealSeconds) { result = "stuck"; DumpStuck(); break; }
            if (now - runStartReal > cfg.maxRunRealSeconds) { result = "timeout"; break; }
            if (GameManager.Instance != null) Set("stage", GameManager.Instance.CurrentStage);
            yield return null;
        }

        BotInput.HoldSkills = false;
        Dictionary<string, object> run = recorder.EndRun(result);
        if (result == "error") Fail(lastException);
        done(run);
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
                if (evoMode)
                {
                    var evoIdx = pool.Where(i => OptField<bool>(opts.GetValue(i), "IsEvolution")).ToList();
                    if (evoIdx.Count > 0) pool = evoIdx;
                }
                int oi = pool[rng.Next(pool.Count)];
                int slot = opts.Length == 1 ? 1 : oi; // LevelUpUI.SlotForOption과 같은 규칙
                Button b = Get<Button>(lu, slot == 0 ? "optionButtonA" : slot == 1 ? "optionButtonB" : "optionButtonC");
                if (!Usable(b)) return false;

                var offered = new List<object>();
                for (int i = 0; i < opts.Length; i++) offered.Add(DescribeOption(opts.GetValue(i)));
                RecordPick(evoMode ? "evolutionTarget" : "levelUp", offered, DescribeOption(opts.GetValue(oi)),
                    OptField<bool>(opts.GetValue(oi), "IsEvolution"));
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
            var pick = usable[rng.Next(usable.Count)];
            EquippedSkill s = Get<EquippedSkill>(et, "currentSkill");
            EquippedPassive p = Get<EquippedPassive>(et, "currentPassive");
            RecordPick("evolutionRoute", usable.Select(u => (object)u.route).Distinct().ToList(),
                (s != null ? s.Id.ToString() : p != null ? "P:" + p.Id : "?") + ":R" + pick.route, true);
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

    private void RecordPick(string screen, IEnumerable<object> offered, object picked, bool wasEvolution)
    {
        if (recorder.Picks == null) return;
        var d = BotJson.Obj();
        d["screen"] = screen;
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

    private void WriteStatus()
    {
        lastStatusReal = Time.realtimeSinceStartup;
        if (cfg == null || string.IsNullOrEmpty(cfg.sessionDir)) return;
        status["heartbeatUtc"] = DateTime.UtcNow.ToString("o");
        status["realElapsed"] = Time.realtimeSinceStartup;
        string path = Path.Combine(cfg.sessionDir, "status.json");
        try
        {
            File.WriteAllText(path + ".tmp", BotJson.Write(status));
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
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
        EditorApplication.isPlaying = false;
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
