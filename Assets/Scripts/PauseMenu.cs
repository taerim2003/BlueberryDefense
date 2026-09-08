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
//
// 🔴 **두 열의 항목도 이제 프리팹이 갖는다**(2026-09-02, 사용자 요청 — 런타임 생성 금지).
//    `LeftColumn/Entry_A0~3` · `RightColumn/Entry_P0~3` **고정 8칸**이고, 각 칸은 `Icon` + `Text/TitleLine` + `Text/DetailLine`.
//    칸 수는 로스터 상한과 맞춰 둔 것이다(액티브 = `SlotKeys` 4개, 패시브 = `MaxPassives` 4개).
//    ⚠️ **프리팹에서 이 이름을 바꾸거나 칸을 지우면 그 칸이 사라진다.** 상한과 어긋나면 Awake가 경고를 찍는다.
//
// 🔴 판 그림(`진짜큰네모_색칠` 1080×880 = Box 원본 크기)은 **둥근 모서리 + 둘레 투명 여백**이라
//    rect만 보고 자식을 놓으면 판 밖 허공에 간다(8/25 빌드 검수 "일시정지 UI 깨짐"이 그것이었다).
//    PNG 알파 실측(불투명): 세로 y60~800만 쓸 수 있고, 그 구간의 가로는 x55~1013이다(위아래 끝일수록 좁다).
//    그래서 `Columns`는 좌우 70 · 위 70 · 아래 315(버튼 윗변 292 + 여백)를 비워 둔다.
//    ⚠️ **프리팹에서 Box 크기나 판 그림을 바꾸면 이 수치가 통째로 낡는다** — 다시 찍어 잴 것.
public class PauseMenu : MonoBehaviour
{
    [Header("골격")]
    [SerializeField] private GameObject panel;
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private RectTransform boxRect;

    [Header("2열 — 칸은 프리팹이 갖고, 여기서는 내용만 채운다")]
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
    private bool paused;

    // 한 칸(스킬/패시브 하나). 프리팹이 만들어 둔 것을 이름 규칙으로 집는다.
    private class Entry
    {
        public GameObject root;
        public Image icon;
        public TMP_Text title;
        public TMP_Text detail;
    }

    private readonly List<Entry> activeEntries = new List<Entry>();
    private readonly List<Entry> passiveEntries = new List<Entry>();
    private TMP_Text activeHeader, passiveHeader;

    private void Awake()
    {
        BindColumn(leftColumn, "Entry_A", activeEntries, ref activeHeader);
        BindColumn(rightColumn, "Entry_P", passiveEntries, ref passiveHeader);

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

        // 🔴 중첩 레이아웃(열 → 항목 → 글자칸) 안의 TMP는 **폭이 확정되기 전 조판으로 높이를 보고한다.**
        //    그대로 두면 긴 설명 칸이 실제보다 낮게 잡혀(실측 117 vs 필요 143) 마지막 줄이 **다음 항목 위로 넘친다.**
        //    ⚠️ 레이아웃만 두 번 돌려도 안 고쳐진다 — TMP는 조판을 프레임 끝에 갱신해서 두 번 다 같은 옛 값을 준다.
        //    폭을 확정 → **ForceMeshUpdate로 그 폭에 맞춰 다시 조판** → 그 높이로 다시 쌓기, 세 단계여야 한다.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(boxRect);
        foreach (var t in boxRect.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate(boxRect);

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

    // ── 칸 배선 ──
    // 이름 규칙으로 모은다(도감과 같은 방식) — 배열에 하나씩 꽂아 두면 로스터가 바뀔 때 조용히 어긋난다.
    private void BindColumn(Transform column, string prefix, List<Entry> into, ref TMP_Text header)
    {
        into.Clear();
        if (column == null) { Debug.LogWarning("[PauseMenu] 열이 배선되지 않았다: " + prefix, this); return; }

        foreach (Transform child in column)
        {
            if (child.name == "Header") { header = child.GetComponent<TMP_Text>(); continue; }
            if (!child.name.StartsWith(prefix)) continue;

            var iconTr = UITreeUtil.FindDeep(child, "Icon");
            var titleTr = UITreeUtil.FindDeep(child, "TitleLine");
            var detailTr = UITreeUtil.FindDeep(child, "DetailLine");
            if (titleTr == null || detailTr == null)
            {
                Debug.LogWarning("[PauseMenu] " + child.name + "에 조각이 없다 —"
                    + (titleTr == null ? " TitleLine" : "") + (detailTr == null ? " DetailLine" : ""), child);
                continue;
            }
            into.Add(new Entry
            {
                root = child.gameObject,
                icon = iconTr != null ? iconTr.GetComponent<Image>() : null,
                title = titleTr.GetComponent<TMP_Text>(),
                detail = detailTr.GetComponent<TMP_Text>(),
            });
        }
    }

    // ── 스킬/패시브 요약 채우기 (열 때마다 갱신) ──
    private void PopulateColumns()
    {
        if (activeHeader != null) activeHeader.text = "<b>[ " + Loc.T("ui.pause.activeHeader") + " ]</b>";
        if (passiveHeader != null) passiveHeader.text = "<b>[ " + Loc.T("ui.pause.passiveHeader") + " ]</b>";

        var skills = Object.FindAnyObjectByType<PlayerSkills>();
        var passives = Object.FindAnyObjectByType<PlayerPassives>();
        var levelUp = LevelUpUI.Instance;

        int used = 0;
        if (skills != null)
            foreach (var s in skills.EquippedSkills)
            {
                if (used >= activeEntries.Count) break;
                SetEntry(activeEntries[used++],
                    levelUp != null ? levelUp.GetActiveIcon(s) : null,
                    TitleLine(s.DisplayName, s.Level),
                    BuildActiveDetail(s));
            }
        HideRest(activeEntries, used, skills != null ? skills.EquippedSkills.Count : 0, "액티브");

        used = 0;
        if (passives != null)
            foreach (var pv in passives.EquippedPassives)
            {
                if (used >= passiveEntries.Count) break;
                SetEntry(passiveEntries[used++],
                    levelUp != null ? levelUp.GetPassiveIcon(pv) : null,
                    TitleLine(pv.DisplayName, pv.Level),
                    BuildPassiveDetail(passives, pv));
            }
        HideRest(passiveEntries, used, passives != null ? passives.EquippedPassives.Count : 0, "패시브");
    }

    private static void SetEntry(Entry e, Sprite icon, string title, string detail)
    {
        e.root.SetActive(true);
        // 아이콘이 없으면 칸 자체를 접는다 — Image만 끄면 LayoutElement가 46px를 그대로 먹는다.
        if (e.icon != null) { e.icon.sprite = icon; e.icon.gameObject.SetActive(icon != null); }
        e.title.text = title;
        e.detail.text = detail;
        e.detail.gameObject.SetActive(!string.IsNullOrEmpty(detail));
    }

    // 남는 칸은 끈다. 들고 있는 수가 칸 수를 넘으면 조용히 잘리므로 그때만 경고한다.
    private void HideRest(List<Entry> entries, int used, int owned, string what)
    {
        for (int i = used; i < entries.Count; i++) entries[i].root.SetActive(false);
        if (owned > entries.Count)
            Debug.LogWarning("[PauseMenu] " + what + " 칸이 모자라다 — 프리팹 " + entries.Count + "칸 / 보유 " + owned, this);
    }

    private static string TitleLine(string name, int level) =>
        $"<b>{name}</b>  <color=#AECBFF>Lv.{level}</color>";

    // 액티브는 **레벨업 효과 목록 대신 스킬 설명**을 보여준다(사용자 결정 2026-09-02).
    // 만렙 4개가 들어가면 목록이 열을 넘겼고, 이 화면에서 급한 건 "이게 뭐 하는 스킬인가"였다.
    // 진화 줄(→)은 남긴다 — 바뀐 이름만으론 그 루트가 뭘 하는지 안 보인다.
    private static string BuildActiveDetail(EquippedSkill s)
    {
        var sb = new StringBuilder();

        // 🔴 진화했으면 **원래 스킬 설명은 이미 사실이 아니다**("관통 산탄"인데 "5초 동안 공격 횟수 증가"라고 적혀 있었다).
        //    그때는 그 루트의 설명으로 갈아끼운다(패시브와 같은 "제목 — 효과" 형식).
        if (s.EvolutionStage <= 0)
        {
            string desc = Loc.TOr("skill.desc." + s.Id, "");
            if (!string.IsNullOrEmpty(desc)) sb.AppendLine(desc);
        }
        AppendPathLines(sb, s.PathTier,
            (p, t) => PlayerSkills.GetPathTierTitle(s.Id, p, t),
            (p, t) => PlayerSkills.DescribePathEffect(s.Id, p, t));

        // 진화 설명이 정의되지 않은 조합이면 빈 칸이 되므로 원래 설명으로 떨어진다.
        string body = sb.ToString().TrimEnd();
        return body.Length > 0 ? body : Loc.TOr("skill.desc." + s.Id, "");
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
                    sb.AppendLine("<color=#FFC864>→</color> " + (string.IsNullOrEmpty(title) ? effect
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
            if (parts.Count > 0) sb.AppendLine("<color=#FFC864>→</color> " + string.Join(", ", parts));
        }
    }

}
