using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

// 컬렉션(도감) — 지금까지 얻어 본 스킬과 그 진화 트리를 보는 화면. 타이틀 Btn_컬렉션이 연다.
//
// 레퍼런스는 **캐릭터 선택 화면**이다: 큰 판 하나로 화면을 덮지 않고, 흐르는 블루베리 벽지 위에
// 작은 판을 여러 개 놓아 영역을 나눈다. 오른쪽 상세의 2루트 × 2티어 배치는 인게임 진화 창
// (EvolutionTreeUI)을 그대로 따른다 — 같은 그림을 두 화면에서 다르게 보여주면 다시 배워야 한다.
//
// 🔴 **판은 그림 원본 크기를 넘기지 않는다.** 자리가 모자라면 판을 키우는 게 아니라 더 쪼갠다.
//    (베개네모 373x195 · 넓은바 561x145 · 바 361x103 · 아이콘칸 143x141 — UISkinApply의 표가 원본)
//
// OptionsMenu·PauseMenu처럼 씬 배치 없이 런타임에 자체 Canvas를 만든다(옷은 UISkin이 입힌다).
// 아이콘은 씬 배선이 아니라 Resources의 SkillIconLibrary에서 집는다 — 타이틀 씬엔 LevelUpUI가 없다.
// 그 에셋은 `Window > Blueberry Defense > 스킬 아이콘 라이브러리 굽기`로 굽는다.
// **enum ↔ 파일명 표는 그 도구가 단독 소유**한다 — 여기서 이름을 다시 매핑하지 말 것.
//
// 발견 기록은 `CollectionSave`(PlayerPrefs에 CSV 한 줄). 기록을 남기는 지점은 `Acquire`/`Evolve` **4곳뿐**이라,
// 새 획득 경로를 만들면 거기서도 불러줘야 도감에 뜬다.
public class CollectionUI : MonoBehaviour
{
    public static CollectionUI Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("CollectionUI");
        DontDestroyOnLoad(go);
        go.AddComponent<CollectionUI>();
    }

    // 폐지된 Refresh는 뺀다 — enum엔 남아 있지만(정수 직렬화) 게임에 안 나오는 칸이다.
    private static readonly PassiveSkillId[] PassiveRoster =
    {
        PassiveSkillId.Strength, PassiveSkillId.Health, PassiveSkillId.Knowledge,
        PassiveSkillId.Assassinate, PassiveSkillId.Defense, PassiveSkillId.Accel,
    };

    private static readonly ActiveSkillId[] ActiveRoster =
        (ActiveSkillId[])Enum.GetValues(typeof(ActiveSkillId));

    // 색은 UISkin이 스프라이트에 곱할 값 겸, 스킨 에셋이 없을 때의 폴백(OptionsMenu와 같은 규칙).
    private static readonly Color SkinColor = new Color(0.420f, 0.482f, 0.910f, 1f);
    private static readonly Color DimColor = new Color(0.031f, 0.020f, 0.051f, 0.961f);
    private static readonly Color SelectedColor = new Color(1f, 0.878f, 0.302f, 1f);
    private static readonly Color LockedColor = new Color(0.22f, 0.22f, 0.30f, 1f);
    private static readonly Color LockedTextColor = new Color(0.62f, 0.62f, 0.70f, 1f);
    private static readonly Color SilhouetteColor = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color SubTextColor = new Color(0.88f, 0.90f, 1f, 1f);

    // ── 칸 크기 (전부 그림 원본 이하) ──
    private const float SlotSize = 110f;      // 아이콘칸 143x141
    private const float SlotStep = 124f;
    // 열 수는 칸 수에 맞춰 직사각형이 되게 나눈다 — 액티브 10=5×2, 패시브 6=3×2.
    // (둘 다 5열로 두면 패시브 마지막 한 칸이 홀로 남아 줄이 깨진다)
    private const int ActiveCols = 5, PassiveCols = 3;
    private const float NodeW = 360f, NodeH = 188f;   // 베개네모 373x195
    private const float RosterX = 210f;
    private const float DetailX = 900f;
    private const float NodeGap = 60f;

    // 한 칸(스킬 하나). 발견 여부에 따라 실루엣/원본으로 갈린다.
    private class Slot
    {
        public Image frame;
        public Image icon;
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

    private GameObject panel;
    private CanvasGroup group;
    private RectTransform content;
    private Tween showTween;
    private bool isOpen;

    private TMP_FontAsset font;   // 씬에 이미 쓰이는 폰트를 한 번만 잡아 둔다(한글이 깨지지 않게)
    private readonly List<Slot> slots = new List<Slot>();
    private TMP_Text progressText;
    private Image detailIcon;
    private TMP_Text detailName;
    private TMP_Text detailSub;
    private readonly TMP_Text[] routeLabels = new TMP_Text[2];
    private readonly TMP_Text[] routeArrows = new TMP_Text[2];
    private readonly Node[,] nodes = new Node[2, 2];   // [루트][티어-1]

    private bool selectedIsPassive;
    private int selectedId;

    private void Awake()
    {
        Instance = this;
        BuildUI();
    }

    // ── 열고 닫기 ──

    public bool IsOpen => isOpen;

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;

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
        if (isOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
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

    // ── 화면 짓기 ──

    private void BuildUI()
    {
        font = FindSceneFont();

        var canvasGo = new GameObject("CollectionCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900; // 설정(1100)·일시정지(1000)보다 아래 — 그 위에 설정이 뜰 수 있다
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        panel = NewUI("Panel", canvasGo.transform);
        Stretch(panel);
        UISkin.Dim(AddImage(panel, DimColor, true), DimColor.a);
        group = panel.AddComponent<CanvasGroup>();

        AddWallpaper(panel.transform);

        var contentGo = NewUI("Content", panel.transform);
        Stretch(contentGo);
        content = (RectTransform)contentGo.transform;
        Transform root = contentGo.transform;

        // 제목·발견도 — 각각 자기 판을 쓴다(머리 판 하나로 가로를 다 먹지 않게).
        var header = MakePlate(root, "Header", new Vector2(760, -36), new Vector2(400, 104), PlateKind.BarWide);
        AddText(header, Loc.T("ui.collection.title"), 40, TextAlignmentOptions.Center, Color.white);

        var progress = MakePlate(root, "Progress", new Vector2(1500, -45), new Vector2(300, 86), PlateKind.Bar);
        progressText = AddText(progress, "", 24, TextAlignmentOptions.Center, Color.white);

        BuildRoster(root);
        BuildDetail(root);

        var close = MakeButton(root, Loc.T("ui.common.back"), Close);
        Bottom(close, new Vector2(0, 40), new Vector2(280, 80));
        JuicyTuning.CenterPivot(close);

        UISkin.Refit(panel); // 크기가 다 정해진 뒤에 9-slice 테두리를 다시 재단
        panel.SetActive(false);
    }

    // 캐릭터 선택 화면의 흐르는 벽지를 그대로 복제한다(속도·타일 크기까지 같아야 한 화면으로 보인다).
    // 씬에 없으면(인게임 등) 그냥 딤만 남는다 — 컬렉션은 타이틀에서만 열린다.
    private void AddWallpaper(Transform parent)
    {
        ScrollingWallpaper source = null;
        foreach (var w in FindObjectsByType<ScrollingWallpaper>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            source = w;
            break;
        }
        if (source == null) return;

        var go = Instantiate(source.gameObject, parent, false);
        go.name = "Wallpaper";
        go.SetActive(true);
        Stretch(go);
        var img = go.GetComponent<RawImage>();
        if (img != null) img.raycastTarget = false;
    }

    // 왼쪽 — 액티브·패시브 아이콘 격자. 격자를 감싸는 판은 두지 않는다(칸 하나하나가 판이다).
    private void BuildRoster(Transform root)
    {
        float y = -172f;
        MakeGroupLabel(root, ref y, Loc.T("ui.collection.active"));
        MakeSlotGrid(root, ref y, false, Array.ConvertAll(ActiveRoster, a => (int)a), ActiveCols);

        y -= 30f;
        MakeGroupLabel(root, ref y, Loc.T("ui.collection.passive"));
        MakeSlotGrid(root, ref y, true, Array.ConvertAll(PassiveRoster, p => (int)p), PassiveCols);
    }

    private void MakeGroupLabel(Transform parent, ref float y, string text)
    {
        var plate = MakePlate(parent, "Label_" + text, new Vector2(RosterX, y), new Vector2(220, 64), PlateKind.Bar);
        AddText(plate, text, 22, TextAlignmentOptions.Center, Color.white);
        y -= 76f;
    }

    private void MakeSlotGrid(Transform parent, ref float y, bool isPassive, int[] ids, int perRow)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            int col = i % perRow;
            int row = i / perRow;

            var go = MakePlate(parent, "Slot_" + (isPassive ? "P" : "A") + ids[i],
                new Vector2(RosterX + col * SlotStep, y - row * SlotStep),
                new Vector2(SlotSize, SlotSize), PlateKind.IconBox);

            var iconGo = NewUI("Icon", go.transform);
            Center(iconGo, new Vector2(SlotSize - 34, SlotSize - 34));
            var icon = AddImage(iconGo, Color.white, false);
            icon.preserveAspect = true;

            var slot = new Slot { frame = go.GetComponent<Image>(), icon = icon, isPassive = isPassive, id = ids[i] };
            slots.Add(slot);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = slot.frame;
            btn.transition = Selectable.Transition.None; // 선택 하이라이트를 직접 칠하므로 기본 틴트는 끈다
            btn.onClick.AddListener(() => { Select(slot.isPassive, slot.id); Refresh(); });
            JuicyTuning.Attach(go);
        }

        int rows = Mathf.CeilToInt(ids.Length / (float)perRow);
        y -= rows * SlotStep;
    }

    // 오른쪽 — 고른 스킬의 아이콘·이름표와 2루트 × 2티어 진화 트리. 여기도 감싸는 판 없이 조각들만.
    private void BuildDetail(Transform root)
    {
        var iconPlate = MakePlate(root, "DetailIcon", new Vector2(DetailX, -160), new Vector2(130, 128), PlateKind.IconBox);
        var iconGo = NewUI("Icon", iconPlate.transform);
        Center(iconGo, new Vector2(92, 92));
        detailIcon = AddImage(iconGo, Color.white, false);
        detailIcon.preserveAspect = true;

        var namePlate = MakePlate(root, "DetailName", new Vector2(DetailX + 146, -159), new Vector2(520, 130), PlateKind.BarWide);
        var nameGo = NewUI("Name", namePlate.transform);
        Anchored(nameGo, new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(440, 46));
        detailName = AddText(nameGo, "", 32, TextAlignmentOptions.Center, Color.white);

        var subGo = NewUI("Sub", namePlate.transform);
        Anchored(subGo, new Vector2(0.5f, 1f), new Vector2(0, -76), new Vector2(440, 34));
        detailSub = AddText(subGo, "", 18, TextAlignmentOptions.Center, SubTextColor, true);

        float y = -300f;
        for (int route = 0; route < 2; route++) BuildRouteRow(root, ref y, route);
    }

    private void BuildRouteRow(Transform parent, ref float y, int route)
    {
        // 560x96 — 넓은바 그림 원본(561x145)을 넘지 않는 최대 폭.
        var plate = MakePlate(parent, "Route" + route, new Vector2(DetailX, y), new Vector2(560, 96), PlateKind.BarWide);
        routeLabels[route] = AddText(plate, "", 20, TextAlignmentOptions.Center, Color.white, true);
        y -= 106f;

        for (int tierIdx = 0; tierIdx < 2; tierIdx++)
            nodes[route, tierIdx] = MakeNode(parent, new Vector2(DetailX + tierIdx * (NodeW + NodeGap), y));

        // 티어 사이 화살표 — 픽셀 폰트엔 ▶ 글리프가 없어 본문 폰트로 그린다(설정 화면 화살표와 같은 이유).
        var arrow = NewUI("Arrow" + route, parent);
        Anchored(arrow, new Vector2(0f, 1f), new Vector2(DetailX + NodeW, y - NodeH * 0.5f + 22), new Vector2(NodeGap, 44));
        routeArrows[route] = AddText(arrow, "▶", 28, TextAlignmentOptions.Center, SubTextColor, true);

        y -= NodeH + 24f;
    }

    private Node MakeNode(Transform parent, Vector2 pos)
    {
        var go = MakePlate(parent, "Node", pos, new Vector2(NodeW, NodeH), PlateKind.Panel);

        var iconGo = NewUI("Icon", go.transform);
        Anchored(iconGo, new Vector2(0f, 1f), new Vector2(26, -26), new Vector2(52, 52));
        var icon = AddImage(iconGo, Color.white, false);
        icon.preserveAspect = true;

        var titleGo = NewUI("Title", go.transform);
        Anchored(titleGo, new Vector2(0f, 1f), new Vector2(88, -30), new Vector2(246, 40));
        var title = AddText(titleGo, "", 22, TextAlignmentOptions.Left, Color.white);

        var descGo = NewUI("Desc", go.transform);
        Anchored(descGo, new Vector2(0f, 1f), new Vector2(28, -88), new Vector2(NodeW - 56, NodeH - 116));
        var desc = AddText(descGo, "", 15, TextAlignmentOptions.TopLeft, Color.white, true);
        desc.enableWordWrapping = true;

        return new Node { frame = go.GetComponent<Image>(), icon = icon, title = title, desc = desc };
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
            slot.frame.color = selected ? SelectedColor : (discovered ? SkinColor : LockedColor);

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
            string prereq = selectedIsPassive
                ? EvolutionRoutes.RoutePrereqName((PassiveSkillId)selectedId, route)
                : EvolutionRoutes.RoutePrereqName((ActiveSkillId)selectedId, route);

            routeLabels[route].text = discovered && !string.IsNullOrEmpty(prereq)
                ? Loc.F("ui.collection.route", route + 1) + "   " + Loc.F("ui.collection.prereq", prereq)
                : Loc.F("ui.collection.route", route + 1);
            routeArrows[route].color = discovered ? SubTextColor : LockedTextColor;

            for (int tierIdx = 0; tierIdx < 2; tierIdx++)
                RefreshNode(nodes[route, tierIdx], route, tierIdx + 1, discovered, unknown);
        }
    }

    private void RefreshNode(Node node, int route, int tier, bool skillDiscovered, string unknown)
    {
        bool evoFound = skillDiscovered && (selectedIsPassive
            ? CollectionSave.HasPassiveEvo((PassiveSkillId)selectedId, route, tier)
            : CollectionSave.HasActiveEvo((ActiveSkillId)selectedId, route, tier));

        node.frame.color = evoFound ? SkinColor : LockedColor;

        node.icon.sprite = selectedIsPassive
            ? SkillIconLibrary.PassiveEvo((PassiveSkillId)selectedId, route)
            : SkillIconLibrary.ActiveEvo((ActiveSkillId)selectedId, route);
        node.icon.enabled = node.icon.sprite != null;
        node.icon.color = evoFound ? Color.white : SilhouetteColor;

        node.title.text = evoFound
            ? (selectedIsPassive
                ? EvolutionRoutes.EvolvedName((PassiveSkillId)selectedId, route, tier)
                : EvolutionRoutes.EvolvedName((ActiveSkillId)selectedId, route, tier))
            : unknown;
        node.title.color = evoFound ? Color.white : LockedTextColor;

        node.desc.text = evoFound ? EvoDescription(route, tier) : Loc.T("ui.collection.notFound");
        node.desc.color = evoFound ? Color.white : LockedTextColor;
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

    // ── 헬퍼 ──

    private enum PlateKind { Bar, BarWide, Panel, IconBox }

    // 판 하나 = 그림 하나. 크기는 호출측이 주되 **그림 원본을 넘기지 않는 값**이어야 한다.
    private GameObject MakePlate(Transform parent, string name, Vector2 pos, Vector2 size, PlateKind kind)
    {
        var go = NewUI(name, parent);
        Anchored(go, new Vector2(0f, 1f), pos, size);
        var img = AddImage(go, SkinColor, true);
        switch (kind)
        {
            case PlateKind.Bar: UISkin.Bar(img); break;
            case PlateKind.BarWide: UISkin.BarWide(img); break;
            case PlateKind.Panel: UISkin.Panel(img); break;
            case PlateKind.IconBox: UISkin.IconBox(img); break;
        }
        return go;
    }

    private GameObject MakeButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        var go = NewUI("Button_" + text, parent);
        var btn = go.AddComponent<Button>();
        var bg = AddImage(go, SkinColor, true);
        UISkin.Bar(bg);
        btn.targetGraphic = bg;
        btn.onClick.AddListener(onClick);

        var label = NewUI("Label", go.transform);
        Stretch(label);
        AddText(label, text, 26, TextAlignmentOptions.Center, Color.white);

        JuicyTuning.Attach(go);
        return go;
    }

    private static TMP_FontAsset FindSceneFont()
    {
        foreach (var t in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.font != null) return t.font;
        return null;
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

    // Image가 이미 붙은 오브젝트면 텍스트를 자식으로 깐다(한 오브젝트에 Graphic 둘은 불가).
    private TMP_Text AddText(GameObject go, string txt, int size, TextAlignmentOptions align, Color c, bool body = false)
    {
        var host = go.GetComponent<Graphic>() != null ? NewUI("Text", go.transform) : go;
        if (host != go) Stretch(host);

        var t = host.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = txt; t.fontSize = size; t.alignment = align; t.color = c; t.raycastTarget = false;
        UISkin.Text(t, body); // fontSize를 정한 뒤라야 크기별 머티리얼이 갈린다
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

    private static void Bottom(GameObject go, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    // anchor 한 점에 붙여 배치. pivot을 anchor와 같게 잡아 위치 계산을 단순하게 유지한다.
    private static void Anchored(GameObject go, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }
}
