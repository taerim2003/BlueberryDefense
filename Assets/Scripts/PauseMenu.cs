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

    private GameObject panel;
    private CanvasGroup panelGroup;
    private RectTransform boxRect;
    private Tween showTween;
    private Transform leftColumn;
    private Transform rightColumn;
    private TMP_FontAsset font;
    private bool paused;

    private void Awake() => BuildUI();

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

        BuildEntry(leftColumn, null, "<b>[ 액티브 스킬 ]</b>", null);
        if (skills != null)
            foreach (var s in skills.EquippedSkills)
                BuildEntry(leftColumn,
                    levelUp != null ? levelUp.GetActiveIcon(s) : null,
                    PlayerSkills.GetActiveSkillBadge(s.Id) + TitleLine(s.DisplayName, s.Level),
                    BuildActiveDetail(s));

        BuildEntry(rightColumn, null, "<b>[ 패시브 ]</b>", null);
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
        AddImage(panel, new Color(0f, 0f, 0f, 0.8f), true);
        panelGroup = panel.AddComponent<CanvasGroup>();

        var box = NewUI("Box", panel.transform);
        Center(box, new Vector2(1760, 940));
        AddImage(box, new Color(0.06f, 0.06f, 0.1f, 0.98f), true);
        boxRect = (RectTransform)box.transform;

        var title = NewUI("Title", box.transform);
        Top(title, new Vector2(0, -22), new Vector2(1680, 64));
        AddText(title, font, "일시정지", 46, TextAlignmentOptions.Center, Color.white);

        // 2열 컨테이너 — 제목과 힌트 사이 영역을 채움
        var columns = NewUI("Columns", box.transform);
        var colRt = columns.GetComponent<RectTransform>();
        colRt.anchorMin = Vector2.zero; colRt.anchorMax = Vector2.one;
        colRt.offsetMin = new Vector2(44, 130); colRt.offsetMax = new Vector2(-44, -104);
        var hg = columns.AddComponent<HorizontalLayoutGroup>();
        hg.spacing = 48;
        hg.childAlignment = TextAnchor.UpperLeft;
        hg.childControlWidth = true; hg.childControlHeight = true;
        hg.childForceExpandWidth = true; hg.childForceExpandHeight = true;

        leftColumn = MakeColumn(columns.transform);
        rightColumn = MakeColumn(columns.transform);

        var settings = NewUI("SettingsButton", box.transform);
        Bottom(settings, new Vector2(-190, 66), new Vector2(340, 56));
        var settingsBtn = settings.AddComponent<Button>();
        settingsBtn.targetGraphic = AddImage(settings, new Color(0.24f, 0.24f, 0.32f, 1f), true);
        settingsBtn.onClick.AddListener(OpenSettings);
        JuicyTuning.Attach(settings);
        JuicyTuning.CenterPivot(settings);
        var settingsLabel = NewUI("Label", settings.transform);
        Stretch(settingsLabel);
        AddText(settingsLabel, font, "설정", 26, TextAlignmentOptions.Center, new Color(0.92f, 0.92f, 0.95f));

        var giveUp = NewUI("GiveUpButton", box.transform);
        Bottom(giveUp, new Vector2(190, 66), new Vector2(340, 56));
        var giveUpBtn = giveUp.AddComponent<Button>();
        giveUpBtn.targetGraphic = AddImage(giveUp, new Color(0.34f, 0.11f, 0.15f, 1f), true);
        giveUpBtn.onClick.AddListener(GiveUpToTitle);
        JuicyTuning.Attach(giveUp);
        JuicyTuning.CenterPivot(giveUp);
        var giveUpLabel = NewUI("Label", giveUp.transform);
        Stretch(giveUpLabel);
        AddText(giveUpLabel, font, "타이틀로 돌아가기", 26, TextAlignmentOptions.Center, new Color(1f, 0.86f, 0.86f));

        var hint = NewUI("Hint", box.transform);
        Bottom(hint, new Vector2(0, 18), new Vector2(1680, 40));
        AddText(hint, font, "ESC — 계속하기", 22, TextAlignmentOptions.Center, new Color(0.7f, 0.8f, 1f));

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
