using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class LevelUpUI : MonoBehaviour
{
    public static LevelUpUI Instance { get; private set; }

    private class Option
    {
        public string Title;
        public string LevelText;
        public bool IsNew;
        public bool IsEvolution;      // 진화 아이템 모달의 카드 — 주황 아웃라인 + "진화!" 태그
        public string Description;
        public Sprite Icon;
        public System.Action Apply;
        // 진화 연쇄(레벨업 후 진화창)·보물상자 에스컬레이션이 대상 스킬/패시브를 식별하기 위한 정보
        public ActiveSkillId? SkillId;
        public PassiveSkillId? PassiveId;
        public bool IsEssence;
        // 갈림길의 진화 카드에만 채운다 — 카드 아래에 "무엇 + 무엇" 짝을 미리 보여준다.
        // 바깥 리스트 = 스킬(화면의 한 줄), 안쪽 = 그 스킬의 열린 루트들.
        public List<List<(Sprite target, Sprite prereq)>> ComboPreview;
    }

    [SerializeField] private GameObject panel;
    [SerializeField] private UITransition panelTransition;
    [SerializeField] private Button optionButtonA;
    [SerializeField] private Button optionButtonB;
    [SerializeField] private Button optionButtonC;
    [SerializeField] private TMP_Text titleA;
    [SerializeField] private TMP_Text titleB;
    [SerializeField] private TMP_Text titleC;
    [SerializeField] private TMP_Text levelA;
    [SerializeField] private TMP_Text levelB;
    [SerializeField] private TMP_Text levelC;
    [SerializeField] private TMP_Text descA;
    [SerializeField] private TMP_Text descB;
    [SerializeField] private TMP_Text descC;
    [SerializeField] private Image iconA;
    [SerializeField] private Image iconB;
    [SerializeField] private Image iconC;
    [SerializeField] private Sprite[] activeIcons;
    [SerializeField] private Sprite[] passiveIcons;
    [SerializeField] private Button rerollButton;   // 스킬트리 리롤 해금 시 노출
    [SerializeField] private TMP_Text rerollLabel;

    [SerializeField] private TMP_Text headerText;        // 패널 제목(일반=레벨 업, 보물=보물 획득)
    [SerializeField] private GameObject[] treasureDecor; // 보물 패널의 장식(정수 비·보물상자)

    // ── 보물상자 전용 패널 ──
    // 🔴 레벨업 카드(LevelUpPanel)와 **완전히 다른 뷰**다. 고르는 게 아니라 받는 것이므로
    //    제목/설명/버튼 없이 **획득한 아이콘만 하나씩 쌓이고**, 다 뜨면 클릭해서 넘긴다(뱀서 방식).
    [SerializeField] private GameObject treasurePanel;       // Canvas/TreasurePanel
    [SerializeField] private RectTransform treasureIconRow;  // 아이콘이 런타임으로 붙는 줄(HorizontalLayoutGroup)
    [SerializeField] private TMP_Text treasureContinueText;  // 전부 뜬 뒤에만 보이는 "클릭하여 계속"
    [SerializeField] private Button treasureDismissButton;   // 패널 전체를 덮는 투명 버튼(클릭=닫기)
    [SerializeField] private Sprite treasureIconFrame;       // HUD 스킬 슬롯과 같은 틀(IconFrame) — 딤 위에서 아이콘이 묻히지 않게
    [SerializeField] private float treasureIconSize = 130f;

    private const string EvolutionHeader = "진화!";
    private const string EvolutionFallbackHeader = "진화할 스킬이 없다 — 대신 레벨업";
    private const string TreasureChoiceHeader = "보물 상자 — 무엇을 받을까?";
    private const int EvolutionFallbackLevels = 3; // 진화 대상이 없을 때 주는 대체 레벨업 수
    private string headerDefaultText;
    private Color headerDefaultColor;

    private static readonly Color NewTagColor = new Color(1f, 0.85f, 0.2f, 1f);
    private static readonly Color LevelTagColor = new Color(0.75f, 0.85f, 1f, 1f);
    private static readonly Color EvolveTagColor = new Color(1f, 0.55f, 0.1f, 1f); // 진화 가능 강조(주황)

    private Outline[] optionOutlines;

    private const int EssenceReward = 10; // 레벨업할 게 없을 때 대체로 지급하는 정수량

    private Option[] currentOptions;
    private int rerollsRemaining;  // 게임당 남은 리롤 횟수
    private bool rerollable;        // 이번 모달이 리롤 가능한가(진화 선택 모달은 불가)
    private bool treasureMode;      // 이번 모달이 보물상자 보너스(자동 레벨업)인가
    private bool treasureDismissed; // 보물 패널을 클릭해 넘겼는가(아이콘이 전부 뜬 뒤에만 true가 된다)
    private bool evolutionMode;     // 이번 모달이 진화 선택(대상 고르기)인가
    private bool treasureChoiceMode; // 이번 모달이 보물상자의 "레벨업 vs 진화" 갈림길인가

    // 모달이 열려 있는 동안 들어온 레벨업/보물상자 요청 — 닫힐 때 하나씩 이어서 띄운다.
    // (보물상자 블루베리 2마리를 연달아 먹으면 두 번째 보상이 첫 번째를 덮어써 사라지던 문제)
    private bool isOpen;
    private int pendingLevelUps;
    private int pendingTreasures;
    private int pendingEvolutions;

    private Button[] optionButtons;

    // 보물상자: 선택 없이 굴려서 나온 만큼 자동 레벨업(뱀서식). 보통 1개, 운 좋으면 3개, 더 좋으면 5개.
    private const float TreasureEscalateChance = 0.45f; // 1 → 3 → 5로 한 단계 더 올라갈 확률
    private const int TreasureMaxRolls = 5;
    private const float TreasureRevealInterval = 0.55f; // 결과 하나가 뜨고 다음 것이 뜰 때까지

    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);

        if (headerText != null)
        {
            headerDefaultText = headerText.text;
            headerDefaultColor = headerText.color;
        }
        SetTreasureDecor(false);
        if (treasurePanel != null) treasurePanel.SetActive(false);

        optionButtonA.onClick.AddListener(() => Choose(0));
        optionButtonB.onClick.AddListener(() => Choose(1));
        optionButtonC.onClick.AddListener(() => Choose(2));
        if (rerollButton != null) rerollButton.onClick.AddListener(OnReroll);
        // 보물 패널은 어디를 눌러도 넘어간다. 아이콘이 다 뜨기 전엔 interactable=false라 안 먹는다.
        if (treasureDismissButton != null) treasureDismissButton.onClick.AddListener(() => treasureDismissed = true);

        // 진화 가능 레벨업 강조용 셀 아웃라인(기본 꺼짐)
        optionOutlines = new[] { MakeOutline(optionButtonA), MakeOutline(optionButtonB), MakeOutline(optionButtonC) };

        optionButtons = new[] { optionButtonA, optionButtonB, optionButtonC };
    }

    private static Outline MakeOutline(Button btn)
    {
        if (btn == null) return null;
        Outline o = btn.GetComponent<Outline>();
        if (o == null) o = btn.gameObject.AddComponent<Outline>();
        o.effectColor = EvolveTagColor;
        o.effectDistance = new Vector2(5f, 5f);
        o.enabled = false;
        return o;
    }

    // 판 시작 시 MetaRunApplier가 호출 — 스킬트리 리롤 노드 해금 수만큼 리롤 부여
    public void InitRerolls(int count) => rerollsRemaining = count;

    public void Show()
    {
        if (isOpen) { pendingLevelUps++; return; }
        ShowLevelUp();
    }

    private void ShowLevelUp()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        rerollable = true;
        treasureMode = false;
        ResetHeader();
        currentOptions = BuildOptions(skills, passives);
        ShowOptions();
    }

    // 보물 패널 장식(정수 비·보물상자) 토글. 제목은 보물 패널이 자기 것을 갖고 있으므로 안 건드린다.
    // 배열 앞쪽부터 켜지므로 보물상자를 정수 비보다 먼저 두면 정수 비 OnEnable에서 상자 연출을 안전하게 건다.
    private void SetTreasureDecor(bool on)
    {
        if (treasureDecor == null) return;
        foreach (GameObject go in treasureDecor)
            if (go != null) go.SetActive(on);
    }

    // 레벨업 카드의 기본 제목으로 되돌린다 — 진화 모달이 "진화!"로 바꿔놓고 갈 수 있다.
    private void ResetHeader()
    {
        if (headerText == null) return;
        headerText.text = headerDefaultText;
        headerText.color = headerDefaultColor;
    }

    // 밀려 있던 보상을 하나 이어서 연다(보물상자 우선). 닫힘 연출(UITransition.Hide)이 끝난 뒤에
    // 열어야 연출이 새 모달을 다시 꺼버리지 않는다.
    private void QueueNextPending()
    {
        if (pendingEvolutions <= 0 && pendingTreasures <= 0 && pendingLevelUps <= 0) return;
        StartCoroutine(ShowNextPendingWhenClosed());
    }

    private IEnumerator ShowNextPendingWhenClosed()
    {
        yield return new WaitWhile(() => panel.activeSelf);

        if (pendingEvolutions > 0) { pendingEvolutions--; ShowEvolution(); }
        else if (pendingTreasures > 0) { pendingTreasures--; ShowTreasureChoice(); } // 밀린 상자도 갈림길을 거친다
        else if (pendingLevelUps > 0) { pendingLevelUps--; ShowLevelUp(); }
    }

    private void OnReroll()
    {
        if (!rerollable || rerollsRemaining <= 0) return;
        rerollsRemaining--;
        ShowLevelUp(); // 후보 재구성 + 재셔플 (이미 열려 있으므로 대기열을 거치지 않는다)
    }

    private void ShowOptions()
    {
        bool alreadyOpen = isOpen;
        SetChoiceLayout(treasureChoiceMode); // 갈림길(2택)만 세로 카드, 나머지는 씬 원본(가로 3택)

        // 선택지는 1~3개로 가변 — 남는 슬롯의 옵션 카드(버튼)는 통째로 숨긴다.
        // 슬롯은 씬에 y=+190/0/-190으로 고정 배치돼 있어서, 1개짜리는 가운데 슬롯에 넣어야 덩그러니 위에 붙지 않는다.
        Option[] slots = new Option[3];
        for (int i = 0; i < currentOptions.Length && i < slots.Length; i++)
            slots[SlotForOption(i)] = currentOptions[i];

        SetSlot(0, optionButtonA, titleA, levelA, descA, iconA, slots[0]);
        SetSlot(1, optionButtonB, titleB, levelB, descB, iconB, slots[1]);
        SetSlot(2, optionButtonC, titleC, levelC, descC, iconC, slots[2]);

        UpdateRerollButton();

        // 닫힘 연출이 아직 돌고 있으면 panel.activeSelf가 true라 SetActive(true)로는 다시 열리지 않는다
        // (연출이 끝나며 패널을 꺼버려 '보이지 않는 모달 + timeScale 0' 상태가 됨) — Show()로 연출을 되돌린다.
        if (panelTransition != null) panelTransition.Show();
        else panel.SetActive(true);
        if (!alreadyOpen) ModalPause.Push();
        isOpen = true;
    }

    private void SetSlot(int index, Button button, TMP_Text title, TMP_Text level, TMP_Text desc, Image icon, Option option)
    {
        if (button != null)
        {
            button.gameObject.SetActive(option != null);
            button.interactable = true; // 보물 에스컬레이션에서 껐던 상호작용 복구
        }
        if (optionOutlines != null && optionOutlines[index] != null)
            optionOutlines[index].enabled = option != null && option.IsEvolution;
        if (option != null) SetRow(title, level, desc, icon, option);
        SetComboPreview(desc, option?.ComboPreview);
    }

    // ── 갈림길 전용 세로 레이아웃 ────────────────────────────────────────────
    // 보물상자 갈림길("보물 상자 vs 진화")은 선택지가 **2개뿐**이라, 가로로 긴 카드 3장 자리에
    // 두 장만 뜨면 화면이 휑하다. 이 화면에서만 카드를 세로로 세워 둘을 나란히 놓는다.
    // ⚠️ 일반 레벨업(3택)은 씬에 저작된 가로 배치를 **그대로 쓴다** — 그래서 원본을 캐시해 두고 되돌린다.
    private const float ChoiceCardWidth = 340f;
    private const float ChoiceCardHeight = 540f;
    private const float ChoiceCardGap = 30f;

    private readonly Dictionary<RectTransform, (Vector2 pos, Vector2 size)> savedRects
        = new Dictionary<RectTransform, (Vector2, Vector2)>();
    private readonly Dictionary<TMP_Text, TextAlignmentOptions> savedAligns
        = new Dictionary<TMP_Text, TextAlignmentOptions>();
    private bool verticalLayout;

    private void SetChoiceLayout(bool vertical)
    {
        if (vertical == verticalLayout) return; // 매 모달마다 좌표를 다시 쓰지 않게
        verticalLayout = vertical;

        Button[] buttons = { optionButtonA, optionButtonB, optionButtonC };
        TMP_Text[] titles = { titleA, titleB, titleC };
        TMP_Text[] levels = { levelA, levelB, levelC };
        TMP_Text[] descs = { descA, descB, descC };
        Image[] icons = { iconA, iconB, iconC };

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            RectTransform card = (RectTransform)buttons[i].transform;
            RectTransform frame = card.Find("Frame") as RectTransform;
            RectTransform icon = icons[i] != null ? icons[i].rectTransform : null;

            if (!vertical)
            {
                Restore(card); Restore(frame); Restore(icon);
                Restore(titles[i]); Restore(levels[i]); Restore(descs[i]);
                continue;
            }

            // 갈림길은 늘 2택이라 두 장을 화면 가운데 기준 좌우로 놓는다(i=0 → 왼쪽, i=1 → 오른쪽).
            Place(card, new Vector2((i - 0.5f) * (ChoiceCardWidth + ChoiceCardGap), 0f),
                        new Vector2(ChoiceCardWidth, ChoiceCardHeight));
            Place(frame, new Vector2(0f, 175f), new Vector2(120f, 120f));
            Place(icon, new Vector2(0f, 175f), new Vector2(108f, 108f));
            Place(titles[i], new Vector2(0f, 82f), new Vector2(300f, 44f), TextAlignmentOptions.Center);
            Place(levels[i], new Vector2(0f, 40f), new Vector2(300f, 34f), TextAlignmentOptions.Center);
            // 설명 박스를 위로 당겨(하단 -90) 카드 바닥에 미리보기 3줄분 공간을 남긴다.
            Place(descs[i], new Vector2(0f, -35f), new Vector2(300f, 110f), TextAlignmentOptions.Top);
        }
    }

    private void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        if (rt == null) return;
        if (!savedRects.ContainsKey(rt)) savedRects[rt] = (rt.anchoredPosition, rt.sizeDelta);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private void Place(TMP_Text t, Vector2 pos, Vector2 size, TextAlignmentOptions align)
    {
        if (t == null) return;
        if (!savedAligns.ContainsKey(t)) savedAligns[t] = t.alignment;
        Place(t.rectTransform, pos, size);
        t.alignment = align;
    }

    private void Restore(RectTransform rt)
    {
        if (rt == null || !savedRects.TryGetValue(rt, out (Vector2 pos, Vector2 size) s)) return;
        rt.anchoredPosition = s.pos;
        rt.sizeDelta = s.size;
    }

    private void Restore(TMP_Text t)
    {
        if (t == null) return;
        Restore(t.rectTransform);
        if (savedAligns.TryGetValue(t, out TextAlignmentOptions a)) t.alignment = a;
    }

    // ── 진화 카드 조합 미리보기 ──────────────────────────────────────────────
    // 세로 카드의 **바닥에 박아 둔다**(설명 길이와 무관하게 늘 같은 자리 = 눈이 찾기 쉽다).
    private const string ComboRowName = "ComboPreview";
    private const float ComboIconSize = 46f;      // 아이콘 한 변 — 한 줄에 욱여넣지 않고 크게 보여준다
    private const float ComboPlusWidth = 16f;     // 짝 사이 "+" 자리
    private const float ComboEntryGap = 14f;      // 짝과 짝 사이(가로)
    private const float ComboLineGap = 8f;        // 줄과 줄 사이(세로)
    private const float ComboRowMaxWidth = 300f;  // 한 줄 최대 폭(세로 카드 340 안)
    private const int ComboMaxPerLine = 2;        // 한 줄에 최대 두 짝 — 스킬 하나의 루트가 둘뿐이라 이걸로 딱 맞는다
    private const int ComboMaxLines = 4;          // 이보다 많아지면 그때 전체를 줄인다
    private const float ComboRowYOffset = 14f;    // 카드 바닥에서 띄우는 거리

    private static float ComboEntryWidth(bool paired) =>
        paired ? ComboIconSize * 2f + ComboPlusWidth : ComboIconSize;

    // 스킬 묶음을 줄로 편다 — **한 스킬이 한 줄**을 차지한다(폭이 남아도 다음 스킬을 끌어올리지 않는다).
    // 루트가 셋 이상인 스킬이 생기면 그때만 그 스킬 안에서 줄이 나뉜다.
    private static List<List<(Sprite target, Sprite prereq)>> ComboLines(
        List<List<(Sprite target, Sprite prereq)>> groups)
    {
        var lines = new List<List<(Sprite target, Sprite prereq)>>();
        foreach (var g in groups)
            for (int i = 0; i < g.Count; i += ComboMaxPerLine)
                lines.Add(g.GetRange(i, Mathf.Min(ComboMaxPerLine, g.Count - i)));
        return lines;
    }

    private void SetComboPreview(TMP_Text desc, List<List<(Sprite target, Sprite prereq)>> groups)
    {
        if (desc == null) return;
        RectTransform card = desc.rectTransform.parent as RectTransform; // 설명의 부모 = 카드(버튼)
        if (card == null) return;

        // 같은 프레임에 다시 만들기 때문에 Destroy(지연 파괴)만으론 Find가 옛것을 잡는다 — 이름을 먼저 뗀다.
        Transform old = card.Find(ComboRowName);
        if (old != null) { old.name = ComboRowName + "_dead"; Destroy(old.gameObject); }

        if (groups == null || groups.Count == 0) return;

        // 줄 수는 스킬 수로 정해진다(폭과 무관). 줄이 너무 많을 때만 전체를 줄여 카드 안에 넣는다.
        var lines = ComboLines(groups);
        float scale = lines.Count > ComboMaxLines ? (float)ComboMaxLines / lines.Count : 1f;

        float iconH = ComboIconSize * scale;
        float totalH = lines.Count * iconH + (lines.Count - 1) * ComboLineGap * scale;

        GameObject row = new GameObject(ComboRowName, typeof(RectTransform));
        RectTransform rowRT = (RectTransform)row.transform;
        // 카드 바닥 기준으로 앉힌다 — 설명이 몇 줄이든 미리보기 위치는 고정된다.
        rowRT.SetParent(card, false);
        rowRT.anchorMin = rowRT.anchorMax = rowRT.pivot = new Vector2(0.5f, 0f);
        rowRT.anchoredPosition = new Vector2(0f, ComboRowYOffset);
        rowRT.sizeDelta = new Vector2(ComboRowMaxWidth, totalH);

        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];

            // 줄마다 실제 폭을 재서 가운데 정렬한다(짝이 하나뿐인 줄도 치우치지 않게).
            float lineW = 0f;
            for (int k = 0; k < line.Count; k++)
            {
                lineW += ComboEntryWidth(line[k].prereq != null) * scale;
                if (k > 0) lineW += ComboEntryGap * scale;
            }

            float x = -lineW * 0.5f;
            float y = totalH * 0.5f - iconH * 0.5f - li * (iconH + ComboLineGap * scale);
            float half = iconH * 0.5f;

            for (int k = 0; k < line.Count; k++)
            {
                (Sprite target, Sprite prereq) c = line[k];
                float w = ComboEntryWidth(c.prereq != null) * scale;
                AddComboIcon(rowRT, c.target, new Vector2(x + half, y), scale);
                if (c.prereq != null)
                {
                    AddComboPlus(rowRT, new Vector2(x + w * 0.5f, y), scale);
                    AddComboIcon(rowRT, c.prereq, new Vector2(x + w - half, y), scale);
                }
                x += w + ComboEntryGap * scale;
            }
        }
    }

    private void AddComboIcon(RectTransform parent, Sprite sprite, Vector2 center, float scale)
    {
        RectTransform rt = NewComboChild(parent, "Icon", center);
        rt.sizeDelta = Vector2.one * (ComboIconSize * scale);

        // HUD 스킬 슬롯과 같은 틀을 깔아 준다 — 도트 아이콘이 어두운 카드 배경에 묻히지 않게.
        Image frame = rt.gameObject.AddComponent<Image>();
        frame.raycastTarget = false; // 카드 버튼의 클릭을 가리면 안 된다
        frame.sprite = treasureIconFrame;
        frame.enabled = treasureIconFrame != null;

        RectTransform inner = NewComboChild(rt, "Fill", Vector2.zero);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        float pad = ComboIconSize * scale * 0.14f;
        inner.offsetMin = new Vector2(pad, pad);
        inner.offsetMax = new Vector2(-pad, -pad);

        Image img = inner.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        img.preserveAspect = true;
        img.sprite = sprite;
        img.enabled = sprite != null; // 아이콘이 아직 없는 스킬(휘두르기)은 빈 틀로 남는다
    }

    private static void AddComboPlus(RectTransform parent, Vector2 center, float scale)
    {
        RectTransform rt = NewComboChild(parent, "Plus", center);
        rt.sizeDelta = new Vector2(ComboPlusWidth * scale, ComboIconSize * scale);

        TMP_Text plus = rt.gameObject.AddComponent<TextMeshProUGUI>();
        plus.text = "+";
        plus.alignment = TextAlignmentOptions.Center;
        plus.fontSize = 26f * scale;
        plus.raycastTarget = false;
        plus.color = EvolveTagColor; // 흰색은 밝은 카드 배경에 묻힌다 — 진화 강조와 같은 주황
        plus.fontStyle = FontStyles.Bold;
    }

    private static RectTransform NewComboChild(RectTransform parent, string name, Vector2 center)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center;
        return rt;
    }

    private void UpdateRerollButton()
    {
        if (rerollButton == null) return;
        bool show = rerollable && rerollsRemaining > 0;
        rerollButton.gameObject.SetActive(show);
        if (show && rerollLabel != null) rerollLabel.text = $"다시 뽑기 ({rerollsRemaining})";
    }

    private static void SetRow(TMP_Text title, TMP_Text level, TMP_Text desc, Image icon, Option option)
    {
        title.text = option.Title;
        desc.text = option.Description;
        SetIcon(icon, option.Icon);

        if (level != null)
        {
            if (option.IsNew)
            {
                level.text = "신규!";
                level.color = NewTagColor;
            }
            else if (option.IsEvolution)
            {
                level.text = "진화! " + option.LevelText;
                level.color = EvolveTagColor;
            }
            else if (!string.IsNullOrEmpty(option.LevelText))
            {
                level.text = option.LevelText;
                level.color = LevelTagColor;
            }
            else
            {
                level.text = "";
            }
        }
    }

    private static void SetIcon(Image image, Sprite sprite)
    {
        image.enabled = sprite != null;
        image.sprite = sprite;
    }

    private Option[] BuildOptions(PlayerSkills skills, PlayerPassives passives)
    {
        List<Option> candidates = new List<Option>();
        CharacterDefinition character = RunConfig.Character; // 후보 풀 게이팅(null이면 전체 허용 = 현행)

        if (!skills.HasMaxSkills)
        {
            foreach (ActiveSkillId id in new[] { ActiveSkillId.Whirlwind, ActiveSkillId.Orb, ActiveSkillId.Lightning, ActiveSkillId.EagleDrop, ActiveSkillId.Sniping, ActiveSkillId.Homing, ActiveSkillId.Shotgun, ActiveSkillId.Rewind })
            {
                if (skills.HasSkill(id)) continue;
                if (character != null && !character.AllowsActive(id)) continue;
                if (!MetaBonuses.SkillUnlockedForRun(id)) continue; // 스킬트리 해금 게이팅
                ActiveSkillId captured = id;
                candidates.Add(new Option
                {
                    Title = PlayerSkills.GetActiveSkillTitleWithTags(captured),
                    IsNew = true,
                    Description = GetActiveSkillDescription(captured),
                    Icon = GetIcon(activeIcons, (int)captured),
                    Apply = () => skills.AcquireSkill(captured),
                    SkillId = captured,
                });
            }
        }

        if (!passives.HasMaxPassives)
        {
            foreach (PassiveSkillId id in new[] { PassiveSkillId.Strength, PassiveSkillId.Health, PassiveSkillId.Knowledge, PassiveSkillId.Assassinate, PassiveSkillId.Refresh })
            {
                if (passives.HasPassive(id)) continue;
                if (character != null && !character.AllowsPassive(id)) continue;
                PassiveSkillId captured = id;
                candidates.Add(new Option
                {
                    Title = PlayerSkills.GetPassiveSkillTitleWithTags(captured),
                    IsNew = true,
                    Description = GetPassiveSkillDescription(captured),
                    Icon = GetIcon(passiveIcons, (int)captured),
                    Apply = () => passives.AcquirePassive(captured),
                    PassiveId = captured,
                });
            }
        }

        foreach (EquippedSkill equipped in skills.EquippedSkills)
        {
            if (!skills.CanUpgradeSkill(equipped)) continue;
            EquippedSkill captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetActiveSkillTitleWithTags(captured),
                LevelText = "레벨: " + (captured.Level + 1),
                Description = PlayerSkills.DescribeUpgradeEffect(captured, captured.Level + 1),
                Icon = GetIcon(activeIcons, (int)captured.Id),
                Apply = () => skills.UpgradeSkillLevel(captured.Id),
                SkillId = captured.Id,
            });
        }

        foreach (EquippedPassive equipped in passives.EquippedPassives)
        {
            if (!passives.CanUpgradePassive(equipped)) continue;
            EquippedPassive captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetPassiveSkillTitleWithTags(captured),
                LevelText = "레벨: " + (captured.Level + 1),
                Description = PlayerPassives.DescribePassiveLevelEffect(captured.Id),
                Icon = GetIcon(passiveIcons, (int)captured.Id),
                Apply = () => passives.UpgradePassiveLevel(captured.Id),
                PassiveId = captured.Id,
            });
        }

        Shuffle(candidates);
        List<Option> options = candidates.Take(3).ToList();

        // 레벨업 가능한 후보가 3개보다 적으면 '정수 +10' 선택지를 하나만 끼운다.
        // 후보가 0~1개면 선택지 자체가 1~2개만 뜬다.
        if (options.Count < 3)
            options.Add(EssenceOption());

        return options.ToArray();
    }

    // 레벨업할 스킬이 부족할 때 자리를 메우는 대체 보상: 이번 판 정수 +10
    private static Option EssenceOption() => new Option
    {
        Title = "정수 획득",
        Description = "정수 +" + EssenceReward,
        Apply = () => MetaRun.Collect(EssenceReward),
        IsEssence = true,
    };

    // 보물상자 블루베리 보상. **진화 획득 경로는 이 상자 하나로 통합돼 있다**(볼x핏 방식).
    // 진화 가능한 게 없으면 예전처럼 그냥 받는 랜덤 레벨업 보너스고(1개 → 운 좋으면 3개 → 5개),
    // 진화 가능한 게 있으면 "레벨업 보너스 vs 진화"를 고르게 된다 — 상자 하나로 둘 다는 못 받는 게 선택의 무게다.
    public void ShowTreasureReward()
    {
        // 보물상자 블루베리는 경험치가 커서 Enemy.Die가 ShowTreasureReward보다 먼저 AddXP를 호출하면
        // 같은 프레임에 '일반 레벨업' 모달이 먼저 열려 버린다. 그 상태로 대기열에 넣으면 플레이어는
        // 에스컬레이션 없는 평범한 카드부터 고르게 된다(= 보물 보너스가 안 터지는 것처럼 보임).
        // 그래서 아직 아무것도 안 고른 레벨업 모달은 뒤로 미루고 보물 보상을 먼저 띄운다.
        if (isOpen)
        {
            if (treasureMode) { pendingTreasures++; return; } // 보물 모달이 이미 떠 있으면 순서대로
            pendingLevelUps++;
        }
        ShowTreasureChoice();
    }

    // 상자를 열기 전 갈림길: 진화 가능한 게 하나라도 있으면 "레벨업 보너스 vs 진화"를 먼저 묻는다.
    // 하나도 없으면 물어볼 게 없으므로 곧장 기존 상자 연출로 간다.
    private void ShowTreasureChoice()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();
        int evolvable = CountEvolvable(skills, passives);
        if (evolvable == 0) { ShowTreasure(); return; }

        rerollable = false;   // 갈림길은 리롤 불가
        treasureMode = false;
        evolutionMode = false;
        treasureChoiceMode = true;

        currentOptions = new Option[]
        {
            new Option
            {
                Title = "보물 상자",
                Description = "가지고 있는 스킬이 무작위로 강화된다 (운이 좋으면 여러 번)",
            },
            new Option
            {
                Title = "진화",
                LevelText = evolvable + "개 가능",
                IsEvolution = true,
                Description = "스킬 하나를 골라 진화시킨다",
                ComboPreview = BuildComboPreview(skills, passives),
            },
        };

        if (headerText != null)
        {
            headerText.text = TreasureChoiceHeader;
            headerText.color = EvolveTagColor;
        }
        ShowOptions();
    }

    private static int CountEvolvable(PlayerSkills skills, PlayerPassives passives)
    {
        int n = 0;
        if (skills != null)
            foreach (EquippedSkill s in skills.EquippedSkills) if (skills.CanEvolve(s)) n++;
        if (passives != null)
            foreach (EquippedPassive p in passives.EquippedPassives) if (passives.CanEvolve(p)) n++;
        return n;
    }

    // 진화 카드에 미리 보여줄 조합 목록 — **루트 하나 = 항목 하나, 스킬 하나 = 묶음 하나**.
    // 바깥 리스트가 스킬(=화면의 한 줄), 안쪽 리스트가 그 스킬의 열린 루트들이다.
    // 두 루트가 다 열려 있으면 한 줄에 둘, 하나만 열려 있으면 그 줄엔 하나만 두고 다음 스킬은 새 줄로 간다
    // — 줄이 곧 스킬의 경계라서 "어느 아이콘이 어느 스킬 것인지"가 눈에 바로 들어온다.
    // 연계 조건이 없는 루트(회오리·낙뢰의 path0)는 prereq가 null이라 아이콘 하나로만 그려진다.
    private List<List<(Sprite target, Sprite prereq)>> BuildComboPreview(PlayerSkills skills, PlayerPassives passives)
    {
        var groups = new List<List<(Sprite, Sprite)>>();

        if (skills != null)
            foreach (EquippedSkill s in skills.EquippedSkills)
            {
                if (!skills.CanEvolve(s)) continue;
                var g = new List<(Sprite, Sprite)>();
                foreach (int r in PlayerSkills.SelectableRoutes(s))
                    if (skills.IsRouteUnlocked(s.Id, r))
                        g.Add((GetActiveIcon(s.Id), PrereqIcon(s.Id, r)));
                if (g.Count > 0) groups.Add(g);
            }

        if (passives != null)
            foreach (EquippedPassive p in passives.EquippedPassives)
            {
                if (!passives.CanEvolve(p)) continue;
                var g = new List<(Sprite, Sprite)>();
                foreach (int r in PlayerPassives.SelectableRoutes(p))
                    if (passives.IsRouteUnlocked(p.Id, r))
                        g.Add((GetPassiveIcon(p.Id), PrereqIcon(p.Id, r)));
                if (g.Count > 0) groups.Add(g);
            }

        return groups;
    }

    private Sprite PrereqIcon(ActiveSkillId id, int route)
    {
        PassiveSkillId? p = EvolutionRoutes.RoutePassivePrereq(id, route);
        if (p.HasValue) return GetPassiveIcon(p.Value);
        ActiveSkillId? a = EvolutionRoutes.RouteActivePrereq(id, route);
        return a.HasValue ? GetActiveIcon(a.Value) : null;
    }

    private Sprite PrereqIcon(PassiveSkillId id, int route)
    {
        PassiveSkillId? p = EvolutionRoutes.RoutePassivePrereq(id, route);
        if (p.HasValue) return GetPassiveIcon(p.Value);
        ActiveSkillId? a = EvolutionRoutes.RouteActivePrereq(id, route);
        return a.HasValue ? GetActiveIcon(a.Value) : null;
    }

    // 갈림길에서 고른 뒤. 모달이 완전히 닫힌 다음에 다음 모달을 열어야 새 모달이 같이 꺼지지 않는다.
    private IEnumerator ResolveTreasureChoice(bool evolve)
    {
        yield return new WaitWhile(() => panel.activeSelf);

        if (!evolve) { ShowTreasure(); yield break; }

        // 진화 대상이 하나뿐이면 "어느 걸 진화할지" 고르는 단계는 의미가 없다 — 곧장 진화 트리로.
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();
        Option[] targets = BuildEvolutionOptions(skills, passives);
        if (targets.Length == 1 && targets[0].IsEvolution)
        {
            yield return ResolveEvolutionChoice(targets[0]);
            yield break;
        }
        ShowEvolution();
    }

    // 보물상자는 선택지가 없다 — 전용 패널에 획득 아이콘만 하나씩 쌓인다.
    private void ShowTreasure()
    {
        rerollable = false;   // 보물상자 보상은 리롤 불가
        treasureMode = true;
        evolutionMode = false;
        currentOptions = new Option[0];

        // 레벨업 카드가 떠 있었다면 감춘다(대기열에 이미 밀어 넣었으므로 사라지지 않는다).
        if (panelTransition != null) panelTransition.Hide();
        else if (panel != null) panel.SetActive(false);

        ClearTreasureIcons();
        if (treasureContinueText != null) treasureContinueText.gameObject.SetActive(false);
        if (treasureDismissButton != null) treasureDismissButton.interactable = false;
        if (treasurePanel != null) treasurePanel.SetActive(true);
        SetTreasureDecor(true);

        bool alreadyOpen = isOpen;
        if (!alreadyOpen) ModalPause.Push();
        isOpen = true;

        StartCoroutine(TreasureRollRoutine());
    }

    // 뱀서식 상자: 몇 개 나올지 먼저 굴리고(1 → 3 → 5), 그 수만큼 하나씩 랜덤 레벨업을 떨군다.
    // 아이콘은 **지워지지 않고 옆으로 쌓여서**, 끝나면 이번에 뭘 얻었는지 한눈에 남는다.
    // 만렙에 닿은 대상은 **다음 시행부터 후보에서 빠지므로** 초과분이 허공에 버려지지 않는다.
    private IEnumerator TreasureRollRoutine()
    {
        int rolls = 1;
        while (rolls < TreasureMaxRolls && Random.value < TreasureEscalateChance) rolls += 2;

        for (int i = 0; i < rolls; i++)
        {
            yield return new WaitForSecondsRealtime(i == 0 ? 0.4f : TreasureRevealInterval);

            // 후보는 매 시행마다 다시 만든다 — 방금 만렙이 된 것을 곧바로 걸러내기 위해.
            Option reward = PickTreasureUpgrade();
            reward.Apply?.Invoke();
            AddTreasureIcon(reward);
        }

        // 전부 뜬 뒤에야 넘길 수 있다 — 마지막 아이콘이 튀는 걸 못 보고 닫는 사고를 막는다.
        if (treasureContinueText != null) treasureContinueText.gameObject.SetActive(true);
        if (treasureDismissButton != null) treasureDismissButton.interactable = true;
        treasureDismissed = false;
        yield return new WaitUntil(() => treasureDismissed);

        treasureMode = false;
        CloseTreasure();
        QueueNextPending();
    }

    // 획득 아이콘 한 칸을 줄 끝에 붙인다(런타임 생성 — 씬에 아이콘을 미리 깔아두지 않는다).
    // HUD 스킬 슬롯과 같은 틀을 깔고 그 위에 아이콘을 얹는다. 딤 배경 위에서 아이콘만 두면 묻힌다.
    // 아이콘이 없는 보상(정수)은 스프라이트 대신 이름을 보여준다.
    private void AddTreasureIcon(Option reward)
    {
        if (treasureIconRow == null) return;

        GameObject go = new GameObject("TreasureIcon", typeof(RectTransform), typeof(Image));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(treasureIconRow, false);
        rt.sizeDelta = new Vector2(treasureIconSize, treasureIconSize);

        Image frame = go.GetComponent<Image>();
        frame.raycastTarget = false;
        frame.sprite = treasureIconFrame;
        frame.enabled = treasureIconFrame != null;

        // 틀 안쪽에 아이콘(또는 이름). 틀 두께만큼 여백을 준다.
        GameObject inner = new GameObject("Icon", typeof(RectTransform));
        RectTransform irt = (RectTransform)inner.transform;
        irt.SetParent(rt, false);
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        float pad = treasureIconSize * 0.14f;
        irt.offsetMin = new Vector2(pad, pad);
        irt.offsetMax = new Vector2(-pad, -pad);

        if (reward.Icon != null)
        {
            Image img = inner.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.sprite = reward.Icon;
        }
        else if (!string.IsNullOrEmpty(reward.Title))
        {
            TMP_Text label = inner.AddComponent<TextMeshProUGUI>();
            label.text = reward.Title;
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 22f;
            label.raycastTarget = false;
        }

        // 팡 튀어나오는 등장. 모달이 timeScale 0이므로 반드시 unscaled로 돌린다.
        rt.localScale = Vector3.zero;
        rt.DOScale(Vector3.one, 0.32f).SetEase(Ease.OutBack).SetUpdate(true);
    }

    private void ClearTreasureIcons()
    {
        if (treasureIconRow == null) return;
        for (int i = treasureIconRow.childCount - 1; i >= 0; i--)
        {
            Transform child = treasureIconRow.GetChild(i);
            child.DOKill();
            Destroy(child.gameObject);
        }
    }

    private void CloseTreasure()
    {
        isOpen = false;
        ModalPause.Pop();
        SetTreasureDecor(false);
        if (treasurePanel != null) treasurePanel.SetActive(false);
        ClearTreasureIcons();
    }

    // 지금 올릴 수 있는 것 중 하나를 무작위로. 전부 만렙이면 정수로 바꿔 준다(보상이 버려지지 않게).
    private Option PickTreasureUpgrade()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();
        List<Option> candidates = new List<Option>();

        foreach (EquippedSkill equipped in skills.EquippedSkills)
        {
            if (!skills.CanUpgradeSkill(equipped)) continue;
            EquippedSkill captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetActiveSkillTitleWithTags(captured),
                LevelText = "레벨: " + (captured.Level + 1),
                Description = PlayerSkills.DescribeUpgradeEffect(captured, captured.Level + 1),
                Icon = GetIcon(activeIcons, (int)captured.Id),
                Apply = () => skills.UpgradeSkillLevel(captured.Id),
                SkillId = captured.Id,
            });
        }

        foreach (EquippedPassive equipped in passives.EquippedPassives)
        {
            if (!passives.CanUpgradePassive(equipped)) continue;
            EquippedPassive captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetPassiveSkillTitleWithTags(captured),
                LevelText = "레벨: " + (captured.Level + 1),
                Description = PlayerPassives.DescribePassiveLevelEffect(captured.Id),
                Icon = GetIcon(passiveIcons, (int)captured.Id),
                Apply = () => passives.UpgradePassiveLevel(captured.Id),
                PassiveId = captured.Id,
            });
        }

        if (candidates.Count == 0) return EssenceOption();
        return candidates[Random.Range(0, candidates.Count)];
    }

    // ── 진화 아이템 보상 ────────────────────────────────────────────────────
    // 벽 스테이지 엘리트가 떨군 진화 아이템을 먹으면 열린다. 진화 가능한 스킬/패시브 중
    // 3개를 제시하고, 고르면 진화 트리(루트 선택)로 이어진다.
    // 진화 가능한 게 하나도 없으면 대신 "고른 스킬 3레벨업"을 준다(아이템이 버려지지 않도록).
    public void ShowEvolutionReward()
    {
        if (isOpen) { pendingEvolutions++; return; }
        ShowEvolution();
    }

    private void ShowEvolution()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        rerollable = false;   // 진화 선택은 리롤 불가
        treasureMode = false;
        currentOptions = BuildEvolutionOptions(skills, passives);
        evolutionMode = true;
        if (headerText != null)
        {
            headerText.text = currentOptions.Length > 0 && currentOptions[0].IsEvolution ? EvolutionHeader : EvolutionFallbackHeader;
            headerText.color = EvolveTagColor;
        }
        ShowOptions();
    }

    private Option[] BuildEvolutionOptions(PlayerSkills skills, PlayerPassives passives)
    {
        List<Option> candidates = new List<Option>();

        foreach (EquippedSkill equipped in skills.EquippedSkills)
        {
            if (!skills.CanEvolve(equipped)) continue;
            EquippedSkill captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetActiveSkillTitleWithTags(captured),
                LevelText = (captured.EvolutionStage + 1) + "차 진화",
                IsEvolution = true,
                Description = DescribeEvolutionChoice(skills, captured),
                Icon = GetIcon(activeIcons, (int)captured.Id),
                SkillId = captured.Id,
            });
        }

        foreach (EquippedPassive equipped in passives.EquippedPassives)
        {
            if (!passives.CanEvolve(equipped)) continue;
            EquippedPassive captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetPassiveSkillTitleWithTags(captured),
                LevelText = (captured.EvolutionStage + 1) + "차 진화",
                IsEvolution = true,
                Description = DescribeEvolutionChoice(passives, captured),
                Icon = GetIcon(passiveIcons, (int)captured.Id),
                PassiveId = captured.Id,
            });
        }

        // 진화 대상이 없으면 대체 보상(레벨업)인데, 이건 고를 게 없는 보상이라 3지선다로 낼 이유가 없다.
        // 랜덤으로 하나만 뽑아 가운데에 한 장 띄우고 누르게 한다.
        if (candidates.Count == 0)
        {
            List<Option> fallback = BuildEvolutionFallbackOptions(skills, passives);
            Shuffle(fallback);
            return fallback.Take(1).ToArray();
        }

        Shuffle(candidates);
        return candidates.Take(3).ToArray();
    }

    // 진화 대상이 없을 때의 대체 보상: 아무 스킬/패시브 하나를 골라 3레벨업.
    // 만렙에 걸린 대상은 올릴 자리가 없으니 후보에서 빼고, 남은 여유분만큼만 표기한다.
    private List<Option> BuildEvolutionFallbackOptions(PlayerSkills skills, PlayerPassives passives)
    {
        List<Option> candidates = new List<Option>();

        foreach (EquippedSkill equipped in skills.EquippedSkills)
        {
            if (!skills.CanUpgradeSkill(equipped)) continue;
            EquippedSkill captured = equipped;
            int target = Mathf.Min(captured.Level + EvolutionFallbackLevels, BalanceConstants.MaxSkillLevel);
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetActiveSkillTitleWithTags(captured),
                LevelText = $"레벨: {captured.Level} → {target}",
                Description = $"{target - captured.Level}레벨 즉시 상승",
                Icon = GetIcon(activeIcons, (int)captured.Id),
                Apply = () => { for (int i = 0; i < EvolutionFallbackLevels; i++) skills.UpgradeSkillLevel(captured.Id); },
                SkillId = captured.Id,
            });
        }

        foreach (EquippedPassive equipped in passives.EquippedPassives)
        {
            if (!passives.CanUpgradePassive(equipped)) continue;
            EquippedPassive captured = equipped;
            int target = Mathf.Min(captured.Level + EvolutionFallbackLevels, BalanceConstants.MaxSkillLevel);
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetPassiveSkillTitleWithTags(captured),
                LevelText = $"레벨: {captured.Level} → {target}",
                Description = $"{target - captured.Level}레벨 즉시 상승",
                Icon = GetIcon(passiveIcons, (int)captured.Id),
                Apply = () => { for (int i = 0; i < EvolutionFallbackLevels; i++) passives.UpgradePassiveLevel(captured.Id); },
                PassiveId = captured.Id,
            });
        }

        if (candidates.Count == 0) candidates.Add(EssenceOption());
        return candidates;
    }

    // 1차 진화 카드는 "고를 수 있는" 루트만 나열한다 — 연계 스킬이 없어 잠긴 루트는 이름조차 안 보여준다.
    private static string DescribeEvolutionChoice(PlayerSkills skills, EquippedSkill s)
    {
        if (s.EvolutionStage > 0) return $"→ {EvolutionRoutes.EvolvedName(s.Id, s.Route, 2)}";
        string[] names = PlayerSkills.SelectableRoutes(s)
            .Where(r => skills.IsRouteUnlocked(s.Id, r))
            .Select(r => EvolutionRoutes.EvolvedName(s.Id, r, 1))
            .ToArray();
        return "루트 선택: " + string.Join(" / ", names);
    }

    private static string DescribeEvolutionChoice(PlayerPassives passives, EquippedPassive p)
    {
        if (p.EvolutionStage > 0) return $"→ {EvolutionRoutes.EvolvedName(p.Id, p.Route, 2)}";
        string[] names = PlayerPassives.SelectableRoutes(p)
            .Where(r => passives.IsRouteUnlocked(p.Id, r))
            .Select(r => EvolutionRoutes.EvolvedName(p.Id, r, 1))
            .ToArray();
        return "루트 선택: " + string.Join(" / ", names);
    }

    // 진화 카드를 고른 뒤: 진화면 트리 창으로, 대체 보상이면 곧바로 적용.
    private IEnumerator ResolveEvolutionChoice(Option opt)
    {
        yield return new WaitWhile(() => panel.activeSelf); // 닫힘 연출이 끝나야 다음 모달이 안 꺼진다

        if (opt.IsEvolution)
        {
            PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
            PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();
            bool done = false;

            if (opt.SkillId.HasValue)
            {
                EquippedSkill s = skills.EquippedSkills.FirstOrDefault(x => x.Id == opt.SkillId.Value);
                if (s != null) EvolutionTreeUI.Instance.Show(skills, s, () => done = true); else done = true;
            }
            else if (opt.PassiveId.HasValue)
            {
                EquippedPassive p = passives.GetPassive(opt.PassiveId.Value);
                if (p != null) EvolutionTreeUI.Instance.Show(passives, p, () => done = true); else done = true;
            }
            else done = true;

            yield return new WaitUntil(() => done);
        }
        else
        {
            opt.Apply?.Invoke();
        }

        QueueNextPending();
    }

    private static Sprite GetIcon(Sprite[] icons, int index) =>
        icons != null && index >= 0 && index < icons.Length ? icons[index] : null;

    // 다른 UI(일시정지 요약 등)가 스킬 아이콘을 재사용할 수 있도록 노출.
    public Sprite GetActiveIcon(ActiveSkillId id) => GetIcon(activeIcons, (int)id);
    public Sprite GetPassiveIcon(PassiveSkillId id) => GetIcon(passiveIcons, (int)id);

    private static string GetActiveSkillDescription(ActiveSkillId id) => id switch
    {
        ActiveSkillId.Whirlwind => "적을 자동으로 추적하는 회오리를 소환(지속시간 4초). 닿아있는 동안 지속 피해를 주며, 피해를 주는 동안 이동 속도가 느려짐",
        ActiveSkillId.Orb => "전방으로 관통하며 나아가는 오브를 소환. 닿아있는 모든 적에게 지속 피해를 주고 느려지게 함",
        ActiveSkillId.Lightning => "6초간 공격 피해를 입는 모든 적들에게 30% 확률로 낙뢰가 떨어져 피해",
        ActiveSkillId.EagleDrop => "화면 전체에 독수리를 1초 간격으로 2회 투하해 모든 적에게 피해",
        ActiveSkillId.Sniping => "가장 체력이 높은 적을 3회 저격해 큰 피해를 줌",
        ActiveSkillId.Homing => "적을 추적하는 미사일 3개를 발사. 레벨업마다 미사일이 크게 늘고, 사용할수록 강해짐(이번 판 한정)",
        ActiveSkillId.Shotgun => "전방으로 산탄을 뿌리고, 5초 동안 모든 스킬의 공격 횟수가 1회 증가",
        ActiveSkillId.Rewind => "다른 모든 스킬의 재사용 대기시간을 1초 앞당김. 레벨업할수록 더 크게 되감음",
        _ => "",
    };

    private static string GetPassiveSkillDescription(PassiveSkillId id) => PlayerPassives.DescribePassiveAcquire(id);

    // 선택지가 1개뿐일 때만 슬롯과 옵션 인덱스가 어긋난다(옵션 0 → 가운데 슬롯 1).
    private int SlotForOption(int optionIndex) => currentOptions.Length == 1 ? 1 : optionIndex;
    private int OptionForSlot(int slot) => currentOptions.Length == 1 ? 0 : slot;

    private void Choose(int slot)
    {
        int index = OptionForSlot(slot);
        if (index < 0 || index >= currentOptions.Length) return;
        Option opt = currentOptions[index];

        if (treasureChoiceMode)
        {
            treasureChoiceMode = false;
            bool evolve = opt.IsEvolution;
            Close();
            StartCoroutine(ResolveTreasureChoice(evolve));
            return;
        }

        if (evolutionMode)
        {
            evolutionMode = false;
            Close();
            StartCoroutine(ResolveEvolutionChoice(opt));
            return;
        }

        opt.Apply?.Invoke();
        Close();
        QueueNextPending();
    }


    private void Close()
    {
        isOpen = false;
        ModalPause.Pop();
        if (panelTransition != null) panelTransition.Hide();
        else panel.SetActive(false);
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

}
