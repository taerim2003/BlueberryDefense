#if UNITY_EDITOR || BOT_QA
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// QA chaos 인스턴스 전용 — 정주행 봇 위에 **엣지 케이스 행동**을 무작위로 섞는다(`BotConfig.chaos`).
// 행동마다 `LastActions`(최근 10개)에 남기므로, 오류가 나면 errors.jsonl에 "직전에 무엇을 했나"가 같이 찍힌다.
//
// 🔴 **사람이 할 수 없는 조작은 하지 않는다.** 버튼은 onClick을 직접 부르지 않고, 그 버튼 자리에 UI 레이캐스트를 쏴서
//    **맨 위에 맞은 것**을 누른다(모달에 가려져 있으면 안 눌린다). ESC는 Input System에 진짜 키 이벤트로 넣는다.
//    직접 부르는 것은 치트성 행동(skip_stage·levelup_burst)뿐이고, 이름으로 구분된다 — 그 행동 뒤의 오류는 triage에서 걸러 볼 것.
// 🔴 행동 하나가 예외를 내도 봇이 죽지 않게 `Guarded`가 MoveNext를 감싼다. 예외는 LogException으로 흘려서 오류로 기록된다.
public class BotChaos : MonoBehaviour
{
    private struct ActionDef
    {
        public string name;
        public float weight;
        public Func<bool> when;
        public Func<IEnumerator> body;
    }

    private BotConfig cfg;
    private System.Random rng;
    private readonly Queue<string> recent = new Queue<string>();
    private List<object> runLog = new List<object>();
    private List<ActionDef> battleActions;
    private float nextActionReal, overlaySince = -1f;
    private bool busy;

    // BotPilot이 전투 루프 동안만 켠다.
    public bool BattleActive { get; set; }
    public List<object> LastActions => recent.Cast<object>().ToList();

    public void Init(BotConfig config)
    {
        cfg = config;
        rng = new System.Random(config.seed ^ 0x5eed);
        battleActions = new List<ActionDef>
        {
            Def("esc", 3f, null, EscTapAndMaybeClose),
            Def("esc_double", 2f, null, EscDouble),
            Def("esc_on_levelup", 3f, LevelUpOpen, EscTapAndMaybeClose),
            Def("monkey_click", 4f, null, () => MonkeyClick(1 + rng.Next(3))),
            Def("drag", 2f, null, Drag),
            Def("options_cycle", 2f, () => OptionsMenu.Instance != null, OptionsCycle),
            Def("locale_next", 2f, null, LocaleNext),
            Def("focus_blip", 2f, null, FocusBlip),
            Def("resolution", 1f, null, Resolution),
            Def("click_spam", 2f, LevelUpOpen, ClickSpam),
            Def("reroll_all", 1f, () => LevelUpOpen() && Usable(Field<Button>(LevelUpUI.Instance, "rerollButton")), RerollAll),
            Def("levelup_burst", 1f, () => PlayerExperience.Instance != null, LevelUpBurst),
            Def("skip_stage", 1f, () => GameManager.Instance != null, SkipStage),
            // 판 도중 Retry·ReturnToTitle 직접 호출은 뺐다 — 사람에겐 그 버튼이 결과 화면에만 있다(가짜 버그를 만든다).
            // 판 도중에 나가는 사람의 길은 일시정지 → 포기뿐이고, 그게 give_up이다. 결과 화면 쪽 출구는 ResultBody가 탄다.
            Def("give_up", 0.6f, null, GiveUp),
        };
        ScheduleNext();
    }

    private static ActionDef Def(string name, float w, Func<bool> when, Func<IEnumerator> body) =>
        new ActionDef { name = name, weight = w, when = when, body = body };

    public List<object> TakeRunLog()
    {
        List<object> l = runLog;
        runLog = new List<object>();
        return l;
    }

    // 일시정지·옵션 창이 떠 있는가. 떠 있는 동안 BotPilot은 레벨업 카드를 누르지 않는다(사람도 창 너머는 못 누른다).
    public bool OverlayOpen => PauseOpen() || (OptionsMenu.Instance != null && OptionsMenu.Instance.IsOpen);

    private void ScheduleNext()
    {
        float lo = Mathf.Max(0.5f, cfg.chaosMinInterval), hi = Mathf.Max(lo, cfg.chaosMaxInterval);
        nextActionReal = Time.realtimeSinceStartup + lo + (float)rng.NextDouble() * (hi - lo);
    }

    private void Update()
    {
        if (!BattleActive || busy) return;
        float now = Time.realtimeSinceStartup;

        // 몽키 클릭이 열어 두고 간 일시정지·옵션을 치운다 — 그대로 두면 봇이 멈춤 판정으로 판을 버린다.
        if (OverlayOpen)
        {
            if (overlaySince < 0f) overlaySince = now;
            else if (now - overlaySince > 6f) { overlaySince = -1f; StartCoroutine(Guarded("tidy_esc", EscTapAndMaybeClose())); return; }
        }
        else overlaySince = -1f;

        if (now < nextActionReal) return;
        ScheduleNext();
        var pool = battleActions.Where(a => a.when == null || SafeBool(a.when)).ToList();
        if (pool.Count == 0) return;
        float total = pool.Sum(a => a.weight), roll = (float)rng.NextDouble() * total;
        foreach (ActionDef a in pool)
        {
            roll -= a.weight;
            if (roll > 0f) continue;
            StartCoroutine(Guarded(a.name, a.body()));
            return;
        }
    }

    // 행동 본문을 한 걸음씩 돌리며 예외를 가둔다. 본문은 **중첩 IEnumerator를 yield하지 말 것**(그 안의 예외는 여기서 못 잡는다).
    public IEnumerator Guarded(string name, IEnumerator body)
    {
        busy = true;
        Note(name);
        while (true)
        {
            object current;
            try
            {
                if (!body.MoveNext()) break;
                current = body.Current;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                break;
            }
            yield return current;
        }
        busy = false;
    }

    private void Note(string name)
    {
        string scene = SceneManager.GetActiveScene().name;
        int stage = GameManager.Instance != null ? GameManager.Instance.CurrentStage : 0;
        recent.Enqueue(Time.realtimeSinceStartup.ToString("0.0") + " " + scene + ":" + stage + " " + name);
        while (recent.Count > 10) recent.Dequeue();
        var d = BotJson.Obj();
        d["action"] = name; d["scene"] = scene; d["stage"] = stage;
        runLog.Add(d);
    }

    // ───────────────────────── 타이틀 · 결과 화면 (BotPilot이 판 사이에 부른다) ─────────────────────────

    // 타이틀에서 패널을 순서 없이 열고 닫은 뒤, 봇이 판에 들어갈 수 있게 전부 치운다.
    public IEnumerator TitleBody()
    {
        TitleController title = FindAnyObjectByType<TitleController>();
        if (title == null) yield break;
        string[] buttons = { "upgradeButton", "collectionButton", "settingsButton", "creditsButton" };
        int n = rng.Next(0, 4);
        for (int i = 0; i < n; i++)
        {
            int pick = rng.Next(buttons.Length + 3);
            if (pick < buttons.Length) { Note("title_open:" + buttons[pick]); RealClick(Field<Button>(title, buttons[pick])); }
            else if (pick == buttons.Length) { Note("title_monkey"); MonkeyOnce(title); }
            else if (pick == buttons.Length + 1) { Note("title_esc"); QueueEsc(true); yield return null; QueueEsc(false); }
            else { Note("title_locale"); StepLocale(); }
            yield return new WaitForSecondsRealtime(0.3f + (float)rng.NextDouble() * 1.2f);
        }

        // 치우기: 사람처럼 ESC 몇 번, 그래도 남은 패널은 닫기 함수로.
        for (int k = 0; k < 3; k++) { QueueEsc(true); yield return null; QueueEsc(false); yield return new WaitForSecondsRealtime(0.15f); }
        if (OptionsMenu.Instance != null && OptionsMenu.Instance.IsOpen) OptionsMenu.Instance.Close();
        if (CollectionUI.Instance != null && CollectionUI.Instance.IsOpen) CollectionUI.Instance.Close();
        SkillTreeUI tree = Field<SkillTreeUI>(title, "skillTree");
        GameObject treeRoot = tree != null ? Field<GameObject>(tree, "panelRoot") : null;
        if (treeRoot != null && treeRoot.activeSelf) tree.Close();
        CreditsUI credits = Field<CreditsUI>(title, "credits");
        if (credits != null && Field<bool>(credits, "isOpen")) credits.Close();
        yield return new WaitForSecondsRealtime(0.6f);
    }

    // 게임오버/클리어 화면에서: 그냥 넘기기 · 스킬트리로 · 다시 하기(→ 새 판이 뜨자마자 버려진다) · 결과 화면 몽키 클릭.
    public IEnumerator ResultBody()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) yield break;
        double r = rng.NextDouble();
        if (r < 0.5) yield break;
        if (r < 0.7) { Note("result_to_skilltree"); gm.ReturnToTitleAndOpenSkillTree(); yield break; }
        if (r < 0.9)
        {
            Note("result_retry");
            gm.Retry();
            float end = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < end && (GameManager.Instance == gm || GameManager.Instance == null)) yield return null;
            yield return new WaitForSecondsRealtime(1.5f);
            yield break;
        }
        Note("result_monkey");
        MonkeyOnce(null);
        yield return new WaitForSecondsRealtime(1f);
    }

    // ───────────────────────── 전투 행동 ─────────────────────────

    private IEnumerator EscTapAndMaybeClose()
    {
        QueueEsc(true); yield return null; QueueEsc(false);
        yield return new WaitForSecondsRealtime(0.2f + (float)rng.NextDouble() * 2.8f);
        if (OverlayOpen) { QueueEsc(true); yield return null; QueueEsc(false); }
        yield return null;
        if (OverlayOpen) { QueueEsc(true); yield return null; QueueEsc(false); } // 옵션이 위에 있었으면 한 번 더
    }

    private IEnumerator EscDouble()
    {
        QueueEsc(true); yield return null; QueueEsc(false); yield return null;
        QueueEsc(true); yield return null; QueueEsc(false);
    }

    private IEnumerator MonkeyClick(int times)
    {
        for (int i = 0; i < times; i++)
        {
            MonkeyOnce(FindAnyObjectByType<TitleController>());
            yield return new WaitForSecondsRealtime(0.1f + (float)rng.NextDouble() * 0.6f);
        }
    }

    private IEnumerator OptionsCycle()
    {
        OptionsMenu om = OptionsMenu.Instance;
        om.Open();
        yield return new WaitForSecondsRealtime(0.3f);
        foreach (string s in new[] { "masterSlider", "bgmSlider", "sfxSlider" })
        {
            Slider sl = Field<Slider>(om, s);
            if (sl != null && rng.NextDouble() < 0.5) sl.value = Mathf.Lerp(sl.minValue, sl.maxValue, (float)rng.NextDouble());
        }
        if (rng.NextDouble() < 0.4) RealClick(Field<Button>(om, rng.NextDouble() < 0.5 ? "languageNext" : "languagePrev"));
        yield return new WaitForSecondsRealtime(0.2f + (float)rng.NextDouble());
        if (rng.NextDouble() < 0.5) { QueueEsc(true); yield return null; QueueEsc(false); }
        else RealClick(Field<Button>(om, "closeButton"));
        yield return null;
        if (om != null && om.IsOpen) om.Close();
    }

    private IEnumerator LocaleNext()
    {
        StepLocale();
        yield break;
    }

    private void StepLocale()
    {
        int count = Loc.Locales.Count;
        if (count > 1) Loc.SetLocale((Loc.CurrentIndex + 1) % count);
    }

    // 창 포커스를 잃었다 되찾는 것 흉내 — OnApplicationFocus/OnApplicationPause를 받는 쪽이 있으면 그 경로를 탄다.
    private IEnumerator FocusBlip()
    {
        Broadcast("OnApplicationFocus", false);
        Broadcast("OnApplicationPause", true);
        yield return new WaitForSecondsRealtime(0.5f + (float)rng.NextDouble() * 2.5f);
        Broadcast("OnApplicationPause", false);
        Broadcast("OnApplicationFocus", true);
    }

    private static void Broadcast(string message, bool value)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            foreach (GameObject root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                root.BroadcastMessage(message, value, SendMessageOptions.DontRequireReceiver);
    }

    private static readonly Vector2Int[] Resolutions =
    {
        new Vector2Int(640, 360), new Vector2Int(480, 270), new Vector2Int(800, 450), new Vector2Int(960, 540), new Vector2Int(640, 480),
    };

    // 창 모드만 쓴다 — 전체 화면으로 바꾸면 무인 실행 중인 모니터를 덮는다.
    private IEnumerator Resolution()
    {
        Vector2Int r = Resolutions[rng.Next(Resolutions.Length)];
        Screen.SetResolution(r.x, r.y, FullScreenMode.Windowed);
        yield break;
    }

    // 레벨업 카드 하나를 한 프레임에 세 번 누른다(더블클릭·연타가 두 장을 먹는지).
    private IEnumerator ClickSpam()
    {
        LevelUpUI lu = LevelUpUI.Instance;
        var btns = new[] { "optionButtonA", "optionButtonB", "optionButtonC" }
            .Select(n => Field<Button>(lu, n)).Where(Usable).ToList();
        if (btns.Count == 0) yield break;
        Button b = btns[rng.Next(btns.Count)];
        for (int i = 0; i < 3; i++) RealClick(b);
    }

    private IEnumerator RerollAll()
    {
        Button reroll = Field<Button>(LevelUpUI.Instance, "rerollButton");
        for (int i = 0; i < 10 && Usable(reroll); i++)
        {
            RealClick(reroll);
            if (rng.NextDouble() < 0.5) yield return null; // 절반은 같은 프레임 연타
        }
    }

    // 치트성: 레벨업을 여러 번 한꺼번에 쌓는다(대기열·창 겹침).
    private IEnumerator LevelUpBurst()
    {
        PlayerExperience xp = PlayerExperience.Instance;
        int n = 2 + rng.Next(4);
        for (int i = 0; i < n; i++) xp.AddXP(Mathf.Max(1, xp.XPToNextLevel - xp.CurrentXP));
        yield break;
    }

    // 치트성: 스테이지 건너뛰기(보스 스테이지·최종 스테이지 경계).
    private IEnumerator SkipStage()
    {
        int n = 1 + rng.Next(2);
        for (int i = 0; i < n && GameManager.Instance != null; i++) GameManager.Instance.SkipToNextStage();
        yield break;
    }

    private IEnumerator GiveUp()
    {
        if (!PauseOpen()) { QueueEsc(true); yield return null; QueueEsc(false); }
        yield return new WaitForSecondsRealtime(0.4f);
        PauseMenu pm = FindAnyObjectByType<PauseMenu>();
        if (pm != null) RealClick(Field<Button>(pm, "giveUpButton"));
    }

    // 드래그를 받는 것(스킬트리 팬 TreePanDrag · 도감/크레딧 스크롤)을 아무거나 끌어 본다. 클릭만으로는 이 경로가 안 돈다.
    private IEnumerator Drag()
    {
        EventSystem es = EventSystem.current;
        var targets = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude)
            .Where(m => m is IDragHandler && m.gameObject.activeInHierarchy).ToList();
        if (es == null || targets.Count == 0) yield break;
        MonoBehaviour t = targets[rng.Next(targets.Count)];
        var rt = t.transform as RectTransform;
        if (rt == null) yield break;
        Canvas canvas = t.GetComponentInParent<Canvas>();
        Camera cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 from = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
        Vector2 delta = new Vector2((float)rng.NextDouble() * 400f - 200f, (float)rng.NextDouble() * 400f - 200f);

        var ped = new PointerEventData(es) { position = from, button = PointerEventData.InputButton.Left, pressPosition = from };
        var hits = new List<RaycastResult>();
        es.RaycastAll(ped, hits);
        if (hits.Count > 0) { ped.pointerCurrentRaycast = hits[0]; ped.pointerPressRaycast = hits[0]; }
        ped.pointerPress = t.gameObject;
        ped.pointerDrag = t.gameObject;
        recent.Enqueue("  drag " + PathOf(t.transform));
        while (recent.Count > 10) recent.Dequeue();

        ExecuteEvents.Execute(t.gameObject, ped, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(t.gameObject, ped, ExecuteEvents.beginDragHandler);
        for (int i = 1; i <= 4; i++)
        {
            ped.delta = delta / 4f;
            ped.position = from + delta * (i / 4f);
            ExecuteEvents.Execute(t.gameObject, ped, ExecuteEvents.dragHandler);
            yield return null;
        }
        ExecuteEvents.Execute(t.gameObject, ped, ExecuteEvents.endDragHandler);
        ExecuteEvents.Execute(t.gameObject, ped, ExecuteEvents.pointerUpHandler);
    }

    // ───────────────────────── 입력 ─────────────────────────

    // Input System에 진짜 ESC 이벤트를 넣는다 — 게임의 `escapeKey.wasPressedThisFrame` 경로를 그대로 탄다.
    private static void QueueEsc(bool down)
    {
        Keyboard kb = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(kb, down ? new KeyboardState(Key.Escape) : new KeyboardState());
    }

    // 화면에 떠 있고 누를 수 있는 것 중 하나를 골라 사람처럼 누른다. 가려져 있으면 아무 일도 없다.
    private void MonkeyOnce(TitleController title)
    {
        var deny = new HashSet<Selectable>();
        if (title != null)
        {
            deny.Add(Field<Button>(title, "quitButton")); // 누르면 프로세스가 끝난다 — 크래시와 구분이 안 된다
            deny.Add(Field<Button>(title, "playButton")); // 판 진입은 BotPilot이 정상 흐름으로 한다
        }
        OptionsMenu om = OptionsMenu.Instance;
        if (om != null)
            foreach (string f in new[] { "fullscreenToggle", "resolutionPrev", "resolutionNext" }) // 전체 화면은 무인 실행 모니터를 덮는다
                deny.Add(Field<Selectable>(om, f));

        List<Selectable> all = FindObjectsByType<Selectable>(FindObjectsInactive.Exclude)
            .Where(s => s != null && s.IsActive() && s.IsInteractable() && !deny.Contains(s)).ToList();
        if (all.Count == 0) return;
        Selectable pick = all[rng.Next(all.Count)];
        recent.Enqueue("  click " + PathOf(pick.transform));
        while (recent.Count > 10) recent.Dequeue();
        RealClick(pick);
    }

    // 셀렉터블 가운데에 UI 레이캐스트를 쏴서 맨 위에 맞은 것에 down/up/click을 보낸다.
    private static void RealClick(Selectable target)
    {
        if (target == null || !target.IsActive() || !target.IsInteractable()) return;
        EventSystem es = EventSystem.current;
        if (es == null) return;
        var rt = target.transform as RectTransform;
        if (rt == null) return;
        Canvas canvas = target.GetComponentInParent<Canvas>();
        Camera cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));

        var ped = new PointerEventData(es) { position = screen, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>();
        es.RaycastAll(ped, hits);
        if (hits.Count == 0) return;
        GameObject hitGo = hits[0].gameObject;
        GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitGo)
                             ?? ExecuteEvents.GetEventHandler<IPointerDownHandler>(hitGo);
        if (handler == null) return;

        ped.pointerCurrentRaycast = hits[0];
        ped.pointerPressRaycast = hits[0];
        ped.pointerPress = handler;
        ped.rawPointerPress = hitGo;
        ped.eligibleForClick = true;
        ExecuteEvents.Execute(handler, ped, ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(handler, ped, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(handler, ped, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(handler, ped, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(handler, ped, ExecuteEvents.pointerExitHandler);
    }

    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (int i = 0; t != null && i < 4; i++, t = t.parent) parts.Insert(0, t.name);
        return string.Join("/", parts);
    }

    // ───────────────────────── 상태 읽기 ─────────────────────────

    private static bool LevelUpOpen() => LevelUpUI.Instance != null && Field<bool>(LevelUpUI.Instance, "isOpen");

    private static bool PauseOpen()
    {
        PauseMenu pm = FindAnyObjectByType<PauseMenu>();
        return pm != null && Field<bool>(pm, "paused");
    }

    private static bool Usable(Button b) => b != null && b.gameObject.activeInHierarchy && b.interactable;

    private static bool SafeBool(Func<bool> f)
    {
        try { return f(); }
        catch (Exception) { return false; }
    }

    // BotPilot.Get과 달리 없으면 기본값 — chaos는 필드 하나가 없다고 세션을 멈출 이유가 없다.
    private static T Field<T>(object target, string name)
    {
        if (target == null) return default;
        FieldInfo fi = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (fi == null) { Debug.LogWarning("[BotChaos] " + target.GetType().Name + "." + name + " 필드가 없다(이름 변경?)"); return default; }
        object v = fi.GetValue(target);
        return v is T t ? t : default;
    }
}
#endif
