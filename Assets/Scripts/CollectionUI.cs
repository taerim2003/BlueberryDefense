using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using DG.Tweening;
using TMPro;

// 컬렉션(도감) — 지금까지 얻어 본 스킬과 그 진화 트리를 보는 화면. 타이틀 Btn_컬렉션이 연다.
//
// 🔴 **화면은 이 코드가 아니라 프리팹이 갖는다** — Assets/Prefabs/UI/CollectionPanel.prefab.
//    Title 씬에 그 프리팹 인스턴스가 놓여 있고, 여기서는 참조를 받아 내용을 채울 뿐이다.
//    위치·크기·색·스프라이트는 전부 인스펙터에서 고친다.
//    (예전엔 이 클래스가 BuildUI()로 Canvas부터 통째로 지어서 인스펙터에 아무것도 안 보였다.)
//    판·글자 규격은 CLAUDE.md §5-1 표가 원본이다 — 여기 옮겨 적지 않는다.
//
// ⚠️ **반복 칸(스킬 16칸 · 진화 노드 4칸)은 인스펙터 배선이 아니라 이름 규칙으로 찾는다** —
//    `Slot_A{액티브 enum 값}` · `Slot_P{패시브 enum 값}` · `Node_R{루트}T{티어}`.
//    칸 하나하나를 배열에 꽂아 두면 로스터가 바뀔 때 조용히 어긋나서, 이름을 단일 소스로 삼았다.
//    **프리팹에서 이 칸들의 이름을 바꾸거나 지우면 그 칸이 사라진다.** 개수가 로스터와 어긋나면
//    Awake가 경고를 찍는다 — 그때는 프리팹을 다시 구워야 한다(BakeRuntimePanels 참고).
//
// 아이콘은 씬 배선이 아니라 Resources의 SkillIconLibrary에서 집는다 — 타이틀 씬엔 LevelUpUI가 없다.
// 그 에셋은 `Window > Blueberry Defense > 스킬 아이콘 라이브러리 굽기`로 굽는다.
// **enum ↔ 파일명 표는 그 도구가 단독 소유**한다 — 여기서 이름을 다시 매핑하지 말 것.
//
// 발견 기록은 `CollectionSave`(PlayerPrefs에 CSV 한 줄). 기록을 남기는 지점은 `Acquire`/`Evolve` **4곳뿐**이라,
// 새 획득 경로를 만들면 거기서도 불러줘야 도감에 뜬다.
public class CollectionUI : MonoBehaviour
{
    public static CollectionUI Instance { get; private set; }

    // 폐지된 Refresh는 뺀다 — enum엔 남아 있지만(정수 직렬화) 게임에 안 나오는 칸이다.
    private static readonly PassiveSkillId[] PassiveRoster =
    {
        PassiveSkillId.Strength, PassiveSkillId.Health, PassiveSkillId.Knowledge,
        PassiveSkillId.Assassinate, PassiveSkillId.Defense, PassiveSkillId.Accel,
    };

    private static readonly ActiveSkillId[] ActiveRoster =
        (ActiveSkillId[])Enum.GetValues(typeof(ActiveSkillId));

    // 색은 UISkin이 스프라이트에 곱할 값. 발견 여부·선택 여부에 따라 런타임에 갈리는 것만 남긴다
    // (판 바탕색처럼 안 변하는 것은 프리팹이 갖는다).
    private static readonly Color SkinColor = new Color(0.420f, 0.482f, 0.910f, 1f);
    private static readonly Color SelectedColor = new Color(1f, 0.878f, 0.302f, 1f);
    private static readonly Color LockedColor = new Color(0.22f, 0.22f, 0.30f, 1f);
    private static readonly Color LockedTextColor = new Color(0.62f, 0.62f, 0.70f, 1f);
    private static readonly Color SilhouetteColor = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color SubTextColor = new Color(0.88f, 0.90f, 1f, 1f);

    [Header("골격")]
    [SerializeField] private GameObject panel;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform content;

    [Header("머리 · 상세")]
    [SerializeField] private TMP_Text headerLabel;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text activeGroupLabel;
    [SerializeField] private TMP_Text passiveGroupLabel;
    [SerializeField] private Image detailIcon;
    [SerializeField] private TMP_Text detailName;
    [SerializeField] private TMP_Text detailSub;

    [Header("루트 (0 · 1)")]
    [SerializeField] private TMP_Text[] routeLabels = new TMP_Text[2];
    [SerializeField] private TMP_Text[] routeArrows = new TMP_Text[2];

    [Header("닫기")]
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text backLabel;

    // 한 칸(스킬 하나). 발견 여부에 따라 실루엣/원본으로 갈린다.
    private class Slot
    {
        public Image frame;
        public Image icon;
        public Image glow;      // 고른 칸 바깥을 두르는 노란 테(`SelectGlow`). 맵·캐릭터 카드와 같은 장치.
        public bool isPassive;
        public int id;          // (int)ActiveSkillId 또는 (int)PassiveSkillId
    }

    // 진화 트리 한 칸(루트 r, 티어 t).
    private class Node
    {
        public Image frame;
        public Image icon;
        public TMP_Text title;
        public TMP_Text desc;
    }

    private Tween showTween;
    private bool isOpen;

    private readonly List<Slot> slots = new List<Slot>();
    private readonly Node[,] nodes = new Node[2, 2];   // [루트][티어-1]

    private bool selectedIsPassive;
    private int selectedId;

    private void Awake()
    {
        Instance = this;
        BindSlots();
        BindNodes();
        if (backButton != null) backButton.onClick.AddListener(Close);
        panel.SetActive(false);
        Loc.LocaleChanged += ApplyText;
    }

    private void OnDestroy()
    {
        Loc.LocaleChanged -= ApplyText;
        if (Instance == this) Instance = null;
    }

    // ── 배선 ──

    // 이름 규칙으로 칸을 모은다. 순서는 상관없다 — Refresh가 칸마다 자기 id로 조회한다.
    private void BindSlots()
    {
        slots.Clear();
        foreach (var tr in content.GetComponentsInChildren<Transform>(true))
        {
            bool passive;
            if (tr.name.StartsWith("Slot_A")) passive = false;
            else if (tr.name.StartsWith("Slot_P")) passive = true;
            else continue;

            if (!int.TryParse(tr.name.Substring("Slot_A".Length), out int id))
            {
                Debug.LogWarning("[CollectionUI] 칸 이름에서 id를 못 읽었다: " + tr.name, tr);
                continue;
            }

            var icon = FindDeep(tr, "Icon");
            var glow = FindDeep(tr, "SelectGlow");
            var slot = new Slot
            {
                frame = tr.GetComponent<Image>(),
                icon = icon != null ? icon.GetComponent<Image>() : null,
                glow = glow != null ? glow.GetComponent<Image>() : null,
                isPassive = passive,
                id = id,
            };
            if (slot.frame == null || slot.icon == null)
            {
                Debug.LogWarning("[CollectionUI] 칸에 Image나 Icon 자식이 없다: " + tr.name, tr);
                continue;
            }
            slots.Add(slot);

            var btn = tr.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() => { Select(slot.isPassive, slot.id); Refresh(); });
        }

        int expected = ActiveRoster.Length + PassiveRoster.Length;
        if (slots.Count != expected)
            Debug.LogWarning("[CollectionUI] 칸 수가 로스터와 다르다 — 프리팹이 낡았다. 프리팹=" + slots.Count + " 로스터=" + expected, this);
    }

    private void BindNodes()
    {
        for (int route = 0; route < 2; route++)
            for (int tierIdx = 0; tierIdx < 2; tierIdx++)
            {
                string name = "Node_R" + route + "T" + (tierIdx + 1);
                var tr = FindDeep(content, name);
                if (tr == null)
                {
                    Debug.LogWarning("[CollectionUI] 진화 노드를 못 찾았다: " + name, this);
                    continue;
                }
                var icon = FindDeep(tr, "Icon");
                var title = FindDeep(tr, "Title");
                var desc = FindDeep(tr, "Desc");
                var node = new Node
                {
                    frame = tr.GetComponent<Image>(),
                    icon = icon != null ? icon.GetComponent<Image>() : null,
                    title = title != null ? title.GetComponent<TMP_Text>() : null,
                    desc = desc != null ? desc.GetComponent<TMP_Text>() : null,
                };
                nodes[route, tierIdx] = node;

                // 조각이 하나만 빠져도 그 칸은 조용히 비어 보인다 — 어느 조각인지 이름을 찍어 준다.
                if (node.frame == null || node.icon == null || node.title == null || node.desc == null)
                    Debug.LogWarning("[CollectionUI] " + name + "에 조각이 없다 —"
                        + (node.frame == null ? " Image" : "") + (node.icon == null ? " Icon" : "")
                        + (node.title == null ? " Title" : "") + (node.desc == null ? " Desc" : ""), tr);
            }
    }

    // 🔴 자식을 **깊이** 찾는다 — 직속 자식만 보면 안 된다.
    //    칸 안에 액자를 한 겹 더 두는 배치(`Node_R0T1/IconBox/Icon`)가 실제로 쓰이고 있고,
    //    그때 `Find("Icon")`은 조용히 null을 돌려줘서 아이콘이 통째로 안 그려진다.
    private static Transform FindDeep(Transform root, string name)
    {
        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            if (tr.name == name) return tr;
        return null;
    }

    // 언어가 바뀌면 고정 문구를 다시 채운다(나머지는 Open→Refresh가 채운다).
    // 예전엔 이 글자들이 Awake에 한 번만 정해져서, 언어를 바꾼 뒤 도감을 열면 옛 언어로 남아 있었다.
    private void ApplyText()
    {
        if (headerLabel != null) headerLabel.text = Loc.T("ui.collection.title");
        if (activeGroupLabel != null) activeGroupLabel.text = Loc.T("ui.collection.active");
        if (passiveGroupLabel != null) passiveGroupLabel.text = Loc.T("ui.collection.passive");
        if (backLabel != null) backLabel.text = Loc.T("ui.common.back");
    }

    // ── 열고 닫기 ──

    public bool IsOpen => isOpen;

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;

        ApplyText();
        Select(false, (int)ActiveRoster[0]);
        Refresh();

        panel.SetActive(true);
        PlayShow();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        PlayHide();
    }

    private void Update()
    {
        if (!isOpen) return;

        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
    }

    private void PlayShow()
    {
        showTween?.Kill();
        group.alpha = 0f;
        content.localScale = Vector3.one * 0.94f;

        var seq = DOTween.Sequence().SetUpdate(true);
        seq.Append(group.DOFade(1f, 0.1f));
        seq.Join(content.DOScale(1f, 0.22f).SetEase(Ease.OutBack));
        showTween = seq;
    }

    private void PlayHide()
    {
        showTween?.Kill();

        var seq = DOTween.Sequence().SetUpdate(true);
        seq.Append(content.DOScale(0.94f, 0.1f).SetEase(Ease.InBack));
        seq.Join(group.DOFade(0f, 0.1f));
        seq.OnComplete(() => panel.SetActive(false));
        showTween = seq;
    }

    // ── 내용 채우기 ──

    private void Select(bool isPassive, int id)
    {
        selectedIsPassive = isPassive;
        selectedId = id;
    }

    private void Refresh()
    {
        int found = 0;
        foreach (var slot in slots)
        {
            bool discovered = slot.isPassive
                ? CollectionSave.HasPassive((PassiveSkillId)slot.id)
                : CollectionSave.HasActive((ActiveSkillId)slot.id);
            if (discovered) found++;

            bool selected = slot.isPassive == selectedIsPassive && slot.id == selectedId;
            // 판 자체를 노랗게 물들이지 않는다 — JuicyButton이 호버·클릭 때 이 색을 되돌려서
            // 노랗게 번쩍했다 돌아오는 것처럼 보였다. 선택 표시는 판 **바깥**의 테가 맡는다.
            slot.frame.color = discovered ? SkinColor : LockedColor;
            if (slot.glow != null) slot.glow.color = selected ? SelectedColor : UISkin.Transparent;

            slot.icon.sprite = slot.isPassive
                ? SkillIconLibrary.Passive((PassiveSkillId)slot.id)
                : SkillIconLibrary.Active((ActiveSkillId)slot.id);
            slot.icon.enabled = slot.icon.sprite != null;
            slot.icon.color = discovered ? Color.white : SilhouetteColor;
        }

        progressText.text = Loc.F("ui.collection.progress", found, slots.Count);
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        bool discovered = selectedIsPassive
            ? CollectionSave.HasPassive((PassiveSkillId)selectedId)
            : CollectionSave.HasActive((ActiveSkillId)selectedId);
        string unknown = Loc.T("ui.collection.unknown");

        detailIcon.sprite = selectedIsPassive
            ? SkillIconLibrary.Passive((PassiveSkillId)selectedId)
            : SkillIconLibrary.Active((ActiveSkillId)selectedId);
        detailIcon.enabled = detailIcon.sprite != null;
        detailIcon.color = discovered ? Color.white : SilhouetteColor;

        if (discovered)
        {
            detailName.text = selectedIsPassive
                ? PlayerSkills.GetPassiveSkillName((PassiveSkillId)selectedId)
                : PlayerSkills.GetActiveSkillName((ActiveSkillId)selectedId);
            detailSub.text = selectedIsPassive
                ? PlayerSkills.PassiveTypeBadge
                : PlayerSkills.ActiveTypeBadge + " " + PlayerSkills.GetActiveSkillBadge((ActiveSkillId)selectedId).TrimEnd();
        }
        else
        {
            detailName.text = unknown;
            detailSub.text = Loc.T("ui.collection.hint");
        }

        for (int route = 0; route < 2; route++)
        {
            // 연계 대상은 **이름 대신 그 스킬의 아이콘**으로 보여준다(8/27 빌드 QA — 글자만으로는
            // 어떤 스킬인지 한눈에 안 들어왔다). 이름 자리를 비운 접두사만 남기고 그 뒤에 아이콘을 놓는다.
            var pre = selectedIsPassive
                ? EvolutionRoutes.RoutePrereq((PassiveSkillId)selectedId, route)
                : EvolutionRoutes.RoutePrereq((ActiveSkillId)selectedId, route);
            bool hasPrereq = pre.Passive.HasValue || pre.Active.HasValue;

            routeLabels[route].text = discovered && hasPrereq
                ? Loc.F("ui.collection.route", route + 1) + "   " + Loc.F("ui.collection.prereq", "")
                : Loc.F("ui.collection.route", route + 1);
            SetPrereqIcon(route, discovered && hasPrereq ? PrereqIcon(pre) : null);
            routeArrows[route].color = discovered ? SubTextColor : LockedTextColor;

            for (int tierIdx = 0; tierIdx < 2; tierIdx++)
                RefreshNode(nodes[route, tierIdx], route, tierIdx + 1, discovered, unknown);
        }
    }

    // ── 루트 라벨 뒤에 붙는 연계 스킬 아이콘 ─────────────────────────────────
    private const float PrereqIconSize = 56f;  // 라벨 높이 96 안에 여백을 두고 들어가는 크기
    private const float PrereqIconGap = 6f;    // 글자 끝과 아이콘 사이

    private readonly Image[] prereqIcons = new Image[2];

    private static Sprite PrereqIcon((PassiveSkillId? Passive, ActiveSkillId? Active) pre) =>
        pre.Passive.HasValue ? SkillIconLibrary.Passive(pre.Passive.Value)
        : pre.Active.HasValue ? SkillIconLibrary.Active(pre.Active.Value)
        : null;

    private void SetPrereqIcon(int route, Sprite sprite)
    {
        Image img = prereqIcons[route];
        if (img == null)
        {
            if (sprite == null) return;         // 쓸 일이 없으면 만들지도 않는다
            img = prereqIcons[route] = CreatePrereqIcon(routeLabels[route]);
        }

        img.sprite = sprite;
        img.enabled = sprite != null;
        if (sprite == null) return;

        // 라벨이 Left 정렬이라 글자는 rect 왼쪽 끝에서 시작한다 — 그 **실제 폭**만큼 오른쪽에 놓는다.
        // ⚠️ `preferredWidth`는 마지막으로 갱신된 메시 기준이라, 방금 바꾼 text를 반영하려면
        //    강제로 한 번 재계산해야 한다(안 하면 이전 문구 폭으로 자리를 잡는다).
        TMP_Text label = routeLabels[route];
        label.ForceMeshUpdate();
        var rt = img.rectTransform;
        rt.anchoredPosition = new Vector2(label.preferredWidth + PrereqIconGap, 0f);
    }

    private static Image CreatePrereqIcon(TMP_Text label)
    {
        var go = new GameObject("PrereqIcon", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(label.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);  // 라벨 왼쪽 끝 기준 — 글자와 같은 출발선
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(PrereqIconSize, PrereqIconSize);

        var img = go.GetComponent<Image>();
        img.preserveAspect = true;   // 아이콘류는 확대해 쓰는 예외라 PA가 필수(CLAUDE.md §5-1)
        img.raycastTarget = false;
        return img;
    }

    private void RefreshNode(Node node, int route, int tier, bool skillDiscovered, string unknown)
    {
        if (node == null) return;   // 프리팹에서 노드가 빠졌을 때 — Awake가 이미 경고를 찍었다

        bool evoFound = skillDiscovered && (selectedIsPassive
            ? CollectionSave.HasPassiveEvo((PassiveSkillId)selectedId, route, tier)
            : CollectionSave.HasActiveEvo((ActiveSkillId)selectedId, route, tier));

        // 조각별로 막는다 — 칸 배치를 바꾸다 하나가 빠져도 화면 전체가 예외로 죽지는 않게(경고는 Awake가 찍었다).
        if (node.frame != null) node.frame.color = evoFound ? SkinColor : LockedColor;

        if (node.icon != null)
        {
            node.icon.sprite = selectedIsPassive
                ? SkillIconLibrary.PassiveEvo((PassiveSkillId)selectedId, route)
                : SkillIconLibrary.ActiveEvo((ActiveSkillId)selectedId, route);
            node.icon.enabled = node.icon.sprite != null;
            node.icon.color = evoFound ? Color.white : SilhouetteColor;
        }

        if (node.title != null)
        {
            node.title.text = evoFound
                ? (selectedIsPassive
                    ? EvolutionRoutes.EvolvedName((PassiveSkillId)selectedId, route, tier)
                    : EvolutionRoutes.EvolvedName((ActiveSkillId)selectedId, route, tier))
                : unknown;
            node.title.color = evoFound ? Color.white : LockedTextColor;
        }

        if (node.desc != null)
        {
            node.desc.text = evoFound ? EvoDescription(route, tier) : Loc.T("ui.collection.notFound");
            node.desc.color = evoFound ? Color.white : LockedTextColor;
        }
    }

    // 새 티어 하나가 옛 티어 여러 개를 한꺼번에 준다 — 설명도 이어 붙인다(인게임 진화 창과 같은 규칙).
    private string EvoDescription(int route, int tier)
    {
        int path = selectedIsPassive
            ? EvolutionRoutes.RoutePath((PassiveSkillId)selectedId, route)
            : EvolutionRoutes.RoutePath((ActiveSkillId)selectedId, route);

        var parts = new List<string>();
        foreach (int legacyTier in EvolutionRoutes.LegacyTiersFor(tier))
        {
            string text = selectedIsPassive
                ? PlayerPassives.DescribePathEffect((PassiveSkillId)selectedId, path, legacyTier)
                : PlayerSkills.DescribePathEffect((ActiveSkillId)selectedId, path, legacyTier);
            if (!string.IsNullOrEmpty(text)) parts.Add(text);
        }
        return string.Join("\n", parts);
    }
}
