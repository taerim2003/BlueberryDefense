using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using DG.Tweening;
using TMPro;

// ESC로 게임 일시정지 + 현재 획득한 스킬/패시브의 레벨·진화 효과 요약 표시.
// 씬에 배치할 필요 없이 게임 시작 시 자동 부트스트랩되어 자체 Canvas/UI를 런타임 생성한다.
// 레이아웃: 넓은 창을 2열로 — 왼쪽=액티브 스킬, 오른쪽=패시브. 각 항목에 스킬 아이콘 표시.
public class PauseMenu : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("PauseMenu");
        DontDestroyOnLoad(go);
        go.AddComponent<PauseMenu>();
    }

    // 판·버튼 바탕은 UISkin이 스프라이트째로 덮어쓴다(캐릭터/맵 선택 화면과 같은 옷).
    // 아래 색은 스킨 에셋이 없을 때의 폴백 + 스프라이트에 곱해질 색을 겸한다.
    private static readonly Color SkinColor = new Color(0.420f, 0.482f, 0.910f, 1f);
    private static readonly Color DangerColor = new Color(0.55f, 0.20f, 0.28f, 1f);

    private GameObject panel;
    private CanvasGroup panelGroup;
    private RectTransform boxRect;
    private Tween showTween;
    private Transform leftColumn;
    private Transform rightColumn;
    private TMP_FontAsset font;
    private bool paused;

    // 셸(제목·버튼·힌트)은 Awake에 한 번만 짓는데, 언어는 이 창 위에 뜬 설정 패널에서 바뀐다.
    // 그래서 그 글자들만 잡아두고 LocaleChanged에 다시 채운다(열 내용은 열 때마다 새로 짓는다).
    private TMP_Text titleText, settingsText, giveUpText, hintText;

    private void Awake()
    {
        BuildUI();
        Loc.LocaleChanged += ApplyShellText;
    }

    private void OnDestroy() => Loc.LocaleChanged -= ApplyShellText;

    private void ApplyShellText()
    {
        if (titleText != null) titleText.text = Loc.T("ui.pause.title");
        if (settingsText != null) settingsText.text = Loc.T("ui.options.title");
        if (giveUpText != null) giveUpText.text = Loc.T("ui.pause.giveUp");
        if (hintText != null) hintText.text = Loc.T("ui.pause.hint");
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

        // 설정 패널이 이 창 위에 떠 있으면 ESC는 그것부터 닫는다(일시정지는 유지).
        if (OptionsMenu.Instance != null && OptionsMenu.Instance.IsOpen)
        {
            OptionsMenu.Instance.Close();
            return;
        }

        if (paused) Resume();
        else if (CanPause()) PauseAndShow();
    }

    private bool CanPause()
    {
        if (Object.FindAnyObjectByType<PlayerSkills>() == null) return false; // 인게임(플레이어 존재)에서만
        var gm = GameManager.Instance;
        return gm == null || (!gm.IsGameOver && !gm.IsGameClear);
    }

    private void PauseAndShow()
    {
        paused = true;
        ModalPause.Push();
        PopulateColumns();
        panel.SetActive(true);

        // timeScale 0에서 열리므로 SetUpdate(true).
        showTween?.Kill();
        panelGroup.alpha = 0f;
        boxRect.localScale = Vector3.one * 0.78f;
        var seq = DOTween.Sequence().SetUpdate(true);
        seq.Append(panelGroup.DOFade(1f, 0.09f));
        seq.Join(boxRect.DOScale(1f, 0.2f).SetEase(Ease.OutBack));
        showTween = seq;
    }

    private void Resume()
    {
        paused = false;
        showTween?.Kill();
        panel.SetActive(false);
        ModalPause.Pop();
    }

    private void OpenSettings()
    {
        if (OptionsMenu.Instance != null) OptionsMenu.Instance.Open();
    }

    // 판을 포기하고 게임오버 흐름을 탄다 — 정수 적립·결과 패널·타이틀 복귀는 전부 기존 경로가 처리한다.
    private void GiveUpToTitle()
    {
        Resume();                         // Pop을 먼저 — GameOver 뒤에 부르면 timeScale이 1로 되돌아가 패널 뒤에서 게임이 계속 돈다
        GameManager.Instance?.GameOver(); // 정수 적립 + timeScale 0 → DamageMeterUI가 "GAME OVER / 획득 정수 / 타이틀로"를 띄움
    }

    // ── 스킬/패시브 요약 채우기 (열 때마다 갱신) ──
    private void PopulateColumns()
    {
        ClearChildren(leftColumn);
        ClearChildren(rightColumn);

        var skills = Object.FindAnyObjectByType<PlayerSkills>();
        var passives = Object.FindAnyObjectByType<PlayerPassives>();
        var levelUp = LevelUpUI.Instance;

        BuildEntry(leftColumn, null, "<b>[ " + Loc.T("ui.pause.activeHeader") + " ]</b>", null);
        if (skills != null)
            foreach (var s in skills.EquippedSkills)
                BuildEntry(leftColumn,
                    levelUp != null ? levelUp.GetActiveIcon(s) : null,
                    PlayerSkills.GetActiveSkillBadge(s.Id) + TitleLine(s.DisplayName, s.Level),
                    BuildActiveDetail(s));

        BuildEntry(rightColumn, null, "<b>[ " + Loc.T("ui.pause.passiveHeader") + " ]</b>", null);
        if (passives != null)
            foreach (var pv in passives.EquippedPassives)
                BuildEntry(rightColumn,
                    levelUp != null ? levelUp.GetPassiveIcon(pv) : null,
                    TitleLine(pv.DisplayName, pv.Level),
                    BuildPassiveDetail(passives, pv));
    }

    private static string TitleLine(string name, int level) =>
        $"<b>{name}</b>  <color=#AECBFF>Lv.{level}</color>";

    private static string BuildActiveDetail(EquippedSkill s)
    {
        var sb = new StringBuilder();
        foreach (var g in PlayerSkills.DescribeLevelUpGains(s))
            sb.AppendLine($"<color=#9FE0A0>·</color> {g}");
        AppendPathLines(sb, s.PathTier, (p, t) => PlayerSkills.GetPathTierTitle(s.Id, p, t));
        return sb.ToString().TrimEnd();
    }

    // 패시브는 "지금 적용 중인 수치"(현재값) + 진화 효과를 효과 설명까지 펼쳐서 보여준다.
    private static string BuildPassiveDetail(PlayerPassives passives, EquippedPassive pv)
    {
        var sb = new StringBuilder();
        foreach (var g in passives.DescribeCurrentEffect(pv))
            sb.AppendLine($"<color=#9FE0A0>·</color> {g}");
        AppendPathLines(sb, pv.PathTier,
            (p, t) => PlayerPassives.GetPathTierTitle(pv.Id, p, t),
            (p, t) => PlayerPassives.DescribePathEffect(pv.Id, p, t));
        return sb.ToString().TrimEnd();
    }

    // effectFn을 주면 진화 티어를 "제목 — 효과" 한 줄씩으로, 없으면 제목만 한 줄에 모아 표시.
    private static void AppendPathLines(StringBuilder sb, int[] pathTier, System.Func<int, int, string> titleFn,
        System.Func<int, int, string> effectFn = null)
    {
        for (int p = 0; p < pathTier.Length; p++)
        {
            int tier = pathTier[p];
            if (tier <= 0) continue;

            if (effectFn != null)
            {
                for (int t = 1; t <= tier; t++)
                {
                    string title = titleFn(p, t);
                    string effect = effectFn(p, t);
                    if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(effect)) continue;
                    sb.AppendLine("<color=#FFC864>▸</color> " + (string.IsNullOrEmpty(title) ? effect
                        : string.IsNullOrEmpty(effect) ? title : $"{title} — {effect}"));
                }
                continue;
            }

            var parts = new List<string>();
            for (int t = 1; t <= tier; t++)
            {
                string title = titleFn(p, t);
                if (!string.IsNullOrEmpty(title)) parts.Add(title);
            }
            if (parts.Count > 0) sb.AppendLine("<color=#FFC864>▸</color> " + string.Join(", ", parts));
        }
    }

    // 한 항목 = 아이콘 + (제목 / 상세). icon null이면 아이콘 없이(섹션 헤더용), detail 비면 상세 생략.
    private void BuildEntry(Transform column, Sprite icon, string titleRich, string detailRich)
    {
        var entry = NewUI("Entry", column);
        var h = entry.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 12;
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;

        if (icon != null)
        {
            var iconGo = NewUI("Icon", entry.transform);
            var img = iconGo.AddComponent<Image>();
            img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
            var le = iconGo.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 46;
            le.minHeight = le.preferredHeight = 46;
        }

        var textCol = NewUI("Text", entry.transform);
        var v = textCol.AddComponent<VerticalLayoutGroup>();
        v.spacing = 2;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        textCol.AddComponent<LayoutElement>().flexibleWidth = 1;

        AddWrapText(textCol.transform, titleRich, 26, new Color(0.95f, 0.95f, 0.95f));
        if (!string.IsNullOrEmpty(detailRich))
            AddWrapText(textCol.transform, detailRich, 20, new Color(0.82f, 0.82f, 0.82f));
    }

    // 🔴 판 그림(`베개같이생긴네모_색칠` 373×195)은 **둥근 베개 모양**이라, 칸(rect)과 그림이 두 번 어긋난다:
    //    ① 그림 둘레에 투명 여백이 있고(좌26·우20·상13·하18px), ② 안쪽 채워진 면은 그보다 더 작은 **둥근** 모양이다.
    //    Preserve Aspect는 이걸 조용히 맞춰 그리므로 rect만 보고 자식을 놓으면 **판 밖 허공에** 놓인다 —
    //    제목이 판 위로 떠 있고 버튼·ESC 힌트가 판 아래로 빠져 있었다(8/25 빌드 검수 "일시정지 UI 깨짐").
    //    아래 좌표는 채워진 면의 실루엣을 PNG 알파로 재서 Box 좌표로 환산한 것이다(원본 y22~149 · x34~344,
    //    Box 배율 1760/373 = 4.719). 세로 위치마다 쓸 수 있는 가로폭이 다르다 — **위아래 끝일수록 좁다.**
    //      Box y 807 → x 684~1284 · y 741 → x 321~1477 · y 505 → x 160~1619 · y 275 → x 269~1520
    //    ⚠️ 칸 크기(BoxW/BoxH)나 판 그림을 바꾸면 이 표가 통째로 낡는다 — 다시 찍어 잴 것.
    private const float BoxW = 1760f, BoxH = 940f;

    // ── 런타임 UI 생성 (정적 셸: 창/제목/2열 컨테이너/힌트) ──
    private void BuildUI()
    {
        foreach (var t in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.font != null) { font = t.font; break; }

        var canvasGo = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        panel = NewUI("Panel", canvasGo.transform);
        Stretch(panel);
        UISkin.Dim(AddImage(panel, new Color(0.031f, 0.020f, 0.051f, 0.8f), true), 0.8f);
        panelGroup = panel.AddComponent<CanvasGroup>();

        var box = NewUI("Box", panel.transform);
        Center(box, new Vector2(BoxW, BoxH));
        UISkin.Panel(AddImage(box, SkinColor, true));
        boxRect = (RectTransform)box.transform;

        var title = NewUI("Title", box.transform);
        Top(title, new Vector2(0, -140), new Vector2(560, 64));
        titleText = AddText(title, font, Loc.T("ui.pause.title"), 46, TextAlignmentOptions.Center, Color.white);

        // 2열 컨테이너 — 제목과 힌트 사이 영역을 채움
        var columns = NewUI("Columns", box.transform);
        var colRt = columns.GetComponent<RectTransform>();
        colRt.anchorMin = Vector2.zero; colRt.anchorMax = Vector2.one;
        colRt.offsetMin = new Vector2(330, 395);
        colRt.offsetMax = new Vector2(-290, -212);
        var hg = columns.AddComponent<HorizontalLayoutGroup>();
        hg.spacing = 48;
        hg.childAlignment = TextAnchor.UpperLeft;
        hg.childControlWidth = true; hg.childControlHeight = true;
        hg.childForceExpandWidth = true; hg.childForceExpandHeight = true;

        leftColumn = MakeColumn(columns.transform);
        rightColumn = MakeColumn(columns.transform);

        var settings = NewUI("SettingsButton", box.transform);
        // 🔴 CLAUDE.md §5-1 — 칸 비율을 판 그림(`가로길쭉이_색칠` 361×103)에 맞춘다.
        //    예전 340×56은 비율이 6.07이라 Preserve Aspect가 판을 196×56으로 **줄여 가운데에만** 그렸고,
        //    글자는 340 폭을 그대로 써서 판 밖으로 삐져나왔다.
        Bottom(settings, new Vector2(-200, 275), new Vector2(360, 103));
        var settingsBtn = settings.AddComponent<Button>();
        var settingsBg = AddImage(settings, SkinColor, true);
        UISkin.BarTinted(settingsBg, SkinColor);
        settingsBtn.targetGraphic = settingsBg;
        settingsBtn.onClick.AddListener(OpenSettings);
        JuicyTuning.Attach(settings);
        JuicyTuning.CenterPivot(settings);
        var settingsLabel = NewUI("Label", settings.transform);
        Stretch(settingsLabel);
        settingsText = AddText(settingsLabel, font, Loc.T("ui.options.title"), 26, TextAlignmentOptions.Center, new Color(0.92f, 0.92f, 0.95f));

        var giveUp = NewUI("GiveUpButton", box.transform);
        Bottom(giveUp, new Vector2(200, 275), new Vector2(360, 103));
        var giveUpBtn = giveUp.AddComponent<Button>();
        var giveUpBg = AddImage(giveUp, DangerColor, true);
        UISkin.BarTinted(giveUpBg, DangerColor); // 색은 위험 빨강 그대로, 모양만 스킨
        giveUpBtn.targetGraphic = giveUpBg;
        giveUpBtn.onClick.AddListener(GiveUpToTitle);
        JuicyTuning.Attach(giveUp);
        JuicyTuning.CenterPivot(giveUp);
        var giveUpLabel = NewUI("Label", giveUp.transform);
        Stretch(giveUpLabel);
        giveUpText = AddText(giveUpLabel, font, Loc.T("ui.pause.giveUp"), 26, TextAlignmentOptions.Center, new Color(1f, 0.86f, 0.86f));

        var hint = NewUI("Hint", box.transform);
        Bottom(hint, new Vector2(0, 215), new Vector2(900, 40));
        hintText = AddText(hint, font, Loc.T("ui.pause.hint"), 22, TextAlignmentOptions.Center, new Color(0.7f, 0.8f, 1f));

        UISkin.Refit(panel); // 크기가 다 정해진 뒤에 9-slice 테두리를 다시 재단
        panel.SetActive(false);
    }

    private static Transform MakeColumn(Transform parent)
    {
        var col = NewUI("Column", parent);
        var v = col.AddComponent<VerticalLayoutGroup>();
        v.spacing = 10;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        col.AddComponent<LayoutElement>().flexibleWidth = 1;
        return col.transform;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private TMP_Text AddWrapText(Transform parent, string txt, int size, Color c)
    {
        var go = NewUI("Line", parent);
        var t = AddText(go, font, txt, size, TextAlignmentOptions.TopLeft, c);
        t.enableWordWrapping = true;
        UISkin.Text(t, true); // 여러 줄 설명은 선택 화면과 같은 본문 폰트로
        return t;
    }

    private static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }
    private static Image AddImage(GameObject go, Color c, bool raycast)
    {
        var img = go.AddComponent<Image>();
        img.color = c; img.raycastTarget = raycast;
        return img;
    }
    private static TMP_Text AddText(GameObject go, TMP_FontAsset font, string txt, int size, TextAlignmentOptions align, Color c)
    {
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = txt; t.fontSize = size; t.alignment = align; t.color = c; t.raycastTarget = false;
        UISkin.Text(t, false); // 폰트·아웃라인 머티리얼을 선택 화면과 같게 (fontSize를 정한 뒤라야 크기별 머티리얼이 갈린다)
        return t;
    }
    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
    private static void Center(GameObject go, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
    }
    private static void Top(GameObject go, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }
    private static void Bottom(GameObject go, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }
}
