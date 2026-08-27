using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using DG.Tweening;
using TMPro;

// ESC로 게임 일시정지 + 현재 획득한 스킬/패시브의 레벨·진화 효과 요약 표시.
// 레이아웃: 넓은 창을 2열로 — 왼쪽=액티브 스킬, 오른쪽=패시브. 각 항목에 스킬 아이콘 표시.
//
// 🔴 **창(셸)은 이 코드가 아니라 프리팹이 갖는다** — Assets/Prefabs/UI/PausePanel.prefab.
//    Battle 씬에 그 프리팹 인스턴스가 놓여 있고, 여기서는 참조를 받아 열고 닫고 채울 뿐이다.
//    위치·크기·색·스프라이트는 전부 인스펙터에서 고친다.
//    (예전엔 이 클래스가 BuildUI()로 Canvas부터 통째로 지어서 인스펙터에 아무것도 안 보였다.)
//    **두 열의 항목만은 예외로 런타임에 만든다** — 그 판에서 뭘 들었는지에 따라 개수가 달라진다.
//
// 🔴 판 그림(`베개같이생긴네모_색칠` 373×195)은 **둥근 베개 모양**이라, 칸(rect)과 그림이 두 번 어긋난다:
//    ① 그림 둘레에 투명 여백이 있고(좌26·우20·상13·하18px), ② 안쪽 채워진 면은 그보다 더 작은 **둥근** 모양이다.
//    Preserve Aspect는 이걸 조용히 맞춰 그리므로 rect만 보고 자식을 놓으면 **판 밖 허공에** 놓인다 —
//    제목이 판 위로 떠 있고 버튼·ESC 힌트가 판 아래로 빠져 있었다(8/25 빌드 검수 "일시정지 UI 깨짐").
//    아래 좌표는 채워진 면의 실루엣을 PNG 알파로 재서 Box 좌표로 환산한 것이다(원본 y22~149 · x34~344,
//    Box 1760×940 기준 배율 1760/373 = 4.719). 세로 위치마다 쓸 수 있는 가로폭이 다르다 — **위아래 끝일수록 좁다.**
//      Box y 807 → x 684~1284 · y 741 → x 321~1477 · y 505 → x 160~1619 · y 275 → x 269~1520
//    ⚠️ **프리팹에서 Box 크기나 판 그림을 바꾸면 이 표가 통째로 낡는다** — 다시 찍어 잴 것.
public class PauseMenu : MonoBehaviour
{
    [Header("골격")]
    [SerializeField] private GameObject panel;
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private RectTransform boxRect;

    [Header("2열 — 항목은 열 때마다 런타임에 채운다")]
    [SerializeField] private Transform leftColumn;
    [SerializeField] private Transform rightColumn;

    [Header("버튼")]
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button giveUpButton;

    // 셸(제목·버튼·힌트)의 글자. 언어는 이 창 위에 뜬 설정 패널에서 바뀌므로 잡아두고 다시 채운다
    // (열 내용은 열 때마다 새로 짓는다).
    [Header("언어가 바뀌면 다시 채우는 글자")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text settingsText;
    [SerializeField] private TMP_Text giveUpText;
    [SerializeField] private TMP_Text hintText;

    private Tween showTween;
    private TMP_FontAsset font;
    private bool paused;

    private void Awake()
    {
        // 런타임에 만드는 항목 글자도 셸과 같은 폰트를 써야 한다(한글이 깨지지 않게).
        if (titleText != null) font = titleText.font;

        if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
        if (giveUpButton != null) giveUpButton.onClick.AddListener(GiveUpToTitle);

        panel.SetActive(false);
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
        ApplyShellText();
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

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private TMP_Text AddWrapText(Transform parent, string txt, int size, Color c)
    {
        var go = NewUI("Line", parent);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = txt; t.fontSize = size; t.alignment = TextAlignmentOptions.TopLeft;
        t.color = c; t.raycastTarget = false;
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
}
