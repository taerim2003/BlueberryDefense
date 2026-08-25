using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
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
    // 진화 아이콘 — 인덱스 = (int)id * 2 + route. 액티브 10종 × 2 = 20칸, 패시브 7종 × 2 = 14칸
    // (폐지된 Refresh 자리도 비운 채 세어야 뒤가 안 밀린다). 배선은 Tools > 진화 아이콘 배선 메뉴가 한다.
    [SerializeField] private Sprite[] activeEvoIcons;
    [SerializeField] private Sprite[] passiveEvoIcons;
    [SerializeField] private Button rerollButton;   // 스킬트리 리롤 해금 시 노출
    [SerializeField] private TMP_Text rerollLabel;      // 버튼 이름("다시 뽑기")
    [SerializeField] private TMP_Text rerollLeftLabel;  // 남은 횟수("3회 남음") — 씬의 RerollButton/LeftText

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
    // 갈림길(보물 상자 vs 진화)의 **왼쪽 카드**에 크게 까는 상자 그림. 비어 있으면 그냥 안 그린다.
    [SerializeField] private Sprite treasureChoiceSprite;

    // ⚠️ const였다가 프로퍼티가 됐다 — 언어가 바뀌면 값도 바뀌어야 해서 컴파일 시점에 고정할 수 없다.
    private static string EvolutionHeader => Loc.T("ui.levelup.evoHeader");
    private static string EvolutionFallbackHeader => Loc.T("ui.levelup.evoFallbackHeader");
    private static string TreasureChoiceHeader => Loc.T("ui.levelup.treasureHeader");
    private const int EvolutionFallbackLevels = 3; // 진화 대상이 없을 때 주는 대체 레벨업 수
    // 🔴 씬의 글자를 Awake에 잡아두던 자리다. 그러면 언어를 바꿔도 **처음 언어가 그대로 남는다**
    //    (씬 TMP는 LocalizedTmp가 갱신해도 이 사본은 안 따라온다) — 그래서 표에서 그때그때 읽는다.
    private static string HeaderDefaultText => Loc.T("ui.levelup.header");
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

        if (headerText != null) headerDefaultColor = headerText.color;
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
        SfxPlayer.Play(SfxId.LevelUp);
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
        headerText.text = HeaderDefaultText;
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
        SetChoiceLayout(treasureChoiceMode); // 갈림길(2택)만 좌 그림 + 우 패널 2장, 나머지는 씬 원본(가로 3택)
        // 되돌아가기는 "갈림길에서 진화로 들어온 진화 대상 선택" 화면에서만 뜬다.
        // 여기서 매번 끄고 켜야 3택·보물 화면에 X가 남지 않는다.
        SetChoiceBackButton(evolutionMode && evolutionCameFromChoice);

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

    // ── 갈림길 전용 레이아웃 ──────────────────────────────────────────────
    // 참고 화면: Ball x Pit의 "융합 / 분열" 선택창 — 한쪽에 큰 그림, 반대쪽에 가로 패널 두 장을 세로로 쌓는다.
    // 우리는 그림을 **보물상자 블루베리**로 바꾸고 **왼쪽**에 둔다(일반 보물 획득 창도 같은 상자 그림을
    // 쓰므로 두 화면이 한 벌로 보인다). 패널 2장은 오른쪽 열.
    // ⚠️ 일반 레벨업(3택)은 씬에 저작된 가로 배치를 **그대로 쓴다** — 그래서 원본을 캐시해 두고 되돌린다.
    private const float ChoiceCardWidth = 480f;
    private const float ChoiceCardHeight = 145f;  // 가로길쭉길쭉이 원본이 561x145 — 세로를 원본 그대로 쓴다
    private const float ChoiceCardGap = 40f;
    private const float ChoiceCardX = 210f;       // 오른쪽 열 중심
    // 패널 안쪽은 가로 433 · 세로 69뿐이다(테두리 좌30·우17·위26·아래50) — 제목 + 부제 두 줄이 상한.
    private const float ChoiceTextWidth = 380f;
    private const float ChoiceTextX = -10f;       // 폭 380의 중심 = 패널 왼쪽 안쪽(-200)에서 시작하도록
    // 왼쪽 큰 그림 — 카드가 아니라 Dialog 직속이라 카드 루프에 얹을 자리가 없다. 런타임에 만든다.
    private const float ChoiceArtSize = 360f;
    private const float ChoiceArtX = -250f;
    private const float ChoiceArtY = -20f;
    // 되돌아가기(X) — Dialog(1060x840) 좌상단 안쪽. 테두리(좌37·위20)를 피해 앉힌다.
    private const float ChoiceBackSize = 64f;
    private const float ChoiceBackX = -450f;
    private const float ChoiceBackY = 355f;

    private readonly Dictionary<RectTransform, (Vector2 pos, Vector2 size)> savedRects
        = new Dictionary<RectTransform, (Vector2, Vector2)>();
    private readonly Dictionary<TMP_Text, TextAlignmentOptions> savedAligns
        = new Dictionary<TMP_Text, TextAlignmentOptions>();
    private bool verticalLayout;

    private void SetChoiceLayout(bool vertical)
    {
        if (vertical == verticalLayout) return; // 매 모달마다 좌표를 다시 쓰지 않게
        verticalLayout = vertical;
        SetChoiceArt(vertical);

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
                if (frame != null) frame.gameObject.SetActive(true);
                continue;
            }

            // 갈림길은 늘 2택이다. 오른쪽 열에 가로 패널 두 장을 세로로 쌓는다(i=0 위 · i=1 아래).
            Place(card, new Vector2(ChoiceCardX, (0.5f - i) * (ChoiceCardHeight + ChoiceCardGap)),
                        new Vector2(ChoiceCardWidth, ChoiceCardHeight));

            // 🔴 갈림길의 두 선택지는 아이콘 액자를 쓰지 않는다 — 켜 두면 빈 회색 액자가 남는다
            //    (3택 레벨업은 아이콘 없는 스킬을 일부러 빈 틀로 남기는 게 의도라 건드리지 않는다).
            //    상자 그림은 **왼쪽 큰 그림**이 대표하므로 패널 안에 다시 넣지 않는다.
            if (frame != null) frame.gameObject.SetActive(false);

            // 제목 + 부제 한 줄, 둘 다 왼쪽 정렬. 부제 자리는 보물=설명 / 진화=개수로 갈리는데
            // ShowTreasureChoice가 **한쪽만** 채워 보내므로 같은 자리에 놓아도 겹치지 않는다.
            // 안쪽 세로는 -22.5 ~ +46.5 딱 69px이다. 제목 36 + 부제 26을 그 안에 앉힌다
            // (26f/-10f는 계산해서 맞춘 값 — 여기서 1~2px만 내려도 부제가 아래 테두리를 파고든다).
            Place(titles[i], new Vector2(ChoiceTextX,  26f), new Vector2(ChoiceTextWidth, 36f), TextAlignmentOptions.Left);
            Place(levels[i], new Vector2(ChoiceTextX, -9.5f), new Vector2(ChoiceTextWidth, 26f), TextAlignmentOptions.Left);
            Place(descs[i],  new Vector2(ChoiceTextX, -9.5f), new Vector2(ChoiceTextWidth, 26f), TextAlignmentOptions.Left);
        }
    }

    // 갈림길 왼쪽의 큰 보물상자 그림. 카드가 아니라 Dialog 직속이라 카드 루프에 얹을 자리가 없어
    // 여기서 한 번 만들고 이후엔 껐다 켜기만 한다(3택으로 돌아가면 끈다).
    private RectTransform choiceArt;

    private void SetChoiceArt(bool on)
    {
        if (!on)
        {
            if (choiceArt != null) choiceArt.gameObject.SetActive(false);
            return;
        }
        if (choiceArt == null)
        {
            Transform parent = optionButtonA != null ? optionButtonA.transform.parent : null;
            if (parent == null) return;
            var go = new GameObject("ChoiceArtwork", typeof(RectTransform), typeof(Image));
            choiceArt = (RectTransform)go.transform;
            choiceArt.SetParent(parent, false);
            choiceArt.anchorMin = choiceArt.anchorMax = new Vector2(0.5f, 0.5f);
            choiceArt.pivot = new Vector2(0.5f, 0.5f);
            choiceArt.anchoredPosition = new Vector2(ChoiceArtX, ChoiceArtY);
            choiceArt.sizeDelta = new Vector2(ChoiceArtSize, ChoiceArtSize);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;   // 뒤에 깔린 카드 클릭을 막지 않는다
            img.preserveAspect = true;
            img.sprite = treasureChoiceSprite;
            img.enabled = treasureChoiceSprite != null;
        }
        choiceArt.gameObject.SetActive(true);
    }

    // 갈림길에서 진화를 골라 들어왔을 때만 뜨는 되돌아가기(X).
    // 진화 대상 목록을 보고 마음에 드는 게 없으면 갈림길로 돌아가 보물 상자를 고를 수 있다 —
    // 갈림길 창에서 조합 미리보기를 뺀 대신 이 길을 열어 둔 것이다(Ball x Pit의 융합 창과 같은 흐름).
    private bool evolutionCameFromChoice;
    private Button choiceBackButton;

    private void SetChoiceBackButton(bool on)
    {
        if (!on)
        {
            if (choiceBackButton != null) choiceBackButton.gameObject.SetActive(false);
            return;
        }
        if (choiceBackButton == null)
        {
            Transform parent = optionButtonA != null ? optionButtonA.transform.parent : null;
            if (parent == null) return;

            var go = new GameObject("ChoiceBackButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(ChoiceBackX, ChoiceBackY);
            rt.sizeDelta = new Vector2(ChoiceBackSize, ChoiceBackSize);
            UISkin.BoxTinted(go.GetComponent<Image>(), Color.white);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            var lrt = (RectTransform)labelGo.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "X";
            label.fontSize = 30f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            UISkin.Text(label, false);

            choiceBackButton = go.GetComponent<Button>();
            choiceBackButton.onClick.AddListener(BackToTreasureChoice);
        }
        choiceBackButton.gameObject.SetActive(true);
    }

    private void BackToTreasureChoice()
    {
        evolutionMode = false;
        evolutionCameFromChoice = false;
        Close();
        StartCoroutine(ReopenTreasureChoice());
    }

    // 닫힘 연출이 끝난 뒤에 다시 열어야 한다 — 연출이 도는 중에 열면 그 연출이 새 모달을 꺼버린다
    // (ShowOptions의 panelTransition 주석과 같은 함정).
    private IEnumerator ReopenTreasureChoice()
    {
        yield return new WaitWhile(() => panel.activeSelf);
        ShowTreasureChoice();
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
    private const float ComboRowMaxWidth = ChoiceTextWidth; // 한 줄 최대 폭 — 세로 카드의 글자 폭에 맞춘다
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
        plus.color = EvolveTagColor; // 진화 강조와 같은 주황
        plus.fontStyle = FontStyles.Bold;
        UISkin.Text(plus, false);    // 카드 위 글자라 나머지와 같은 아웃라인을 입힌다
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
        if (!show) return;
        // 버튼 이름과 남은 횟수를 두 줄로 나눠 쓴다(씬의 Label 28pt / LeftText 18pt).
        if (rerollLabel != null) rerollLabel.text = Loc.T("ui.levelup.reroll");
        if (rerollLeftLabel != null) rerollLeftLabel.text = Loc.F("ui.levelup.rerollLeft", rerollsRemaining);
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
                level.text = Loc.T("ui.levelup.tagNew");
                level.color = NewTagColor;
            }
            else if (option.IsEvolution)
            {
                level.text = Loc.T("ui.levelup.tagEvolve") + " " + option.LevelText;
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
            // ⚠️ 패시브를 새로 만들면 여기 안 넣으면 게임에 안 뜬다. Refresh는 폐지돼 빠졌다(PassiveSkillId 주석 참고).
            foreach (PassiveSkillId id in new[] { PassiveSkillId.Strength, PassiveSkillId.Health, PassiveSkillId.Knowledge, PassiveSkillId.Assassinate, PassiveSkillId.Defense, PassiveSkillId.Accel })
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
                LevelText = Loc.F("ui.levelup.level", captured.Level + 1),
                Description = PlayerSkills.DescribeUpgradeEffect(captured, captured.Level + 1),
                Icon = GetActiveIcon(captured),
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
                LevelText = Loc.F("ui.levelup.level", captured.Level + 1),
                Description = PlayerPassives.DescribePassiveLevelEffect(captured.Id),
                Icon = GetPassiveIcon(captured),
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
        Title = Loc.T("ui.levelup.essenceTitle"),
        Description = Loc.F("ui.levelup.essenceDesc", EssenceReward),
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
                Title = Loc.T("ui.treasure.boxTitle"),
                Description = Loc.T("ui.treasure.boxDesc"),
                // 상자 그림은 **왼쪽 큰 그림**(SetChoiceArt)이 대표한다 — 패널 안에 또 넣으면 같은 그림이 둘이 된다.
            },
            new Option
            {
                Title = Loc.T("ui.treasure.evoTitle"),
                // 부제는 개수 한 줄만. 패널 안쪽이 두 줄뿐이고, **무엇을** 진화시킬 수 있는지는
                // 다음 화면(진화 대상 선택)에서 직접 보고 마음에 안 들면 X로 여기로 돌아온다.
                // (그래서 조합 미리보기를 뺐다 — Ball x Pit의 융합 창과 같은 흐름.)
                LevelText = Loc.F("ui.treasure.evoCount", evolvable),
                IsEvolution = true,
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
                        // 타겟은 **진화 후** 그림 — 루트마다 달라서 "이 조합을 하면 뭐가 되는지"가 그림으로 보인다.
                        g.Add((GetActiveEvoIcon(s.Id, r) ?? GetActiveIcon(s.Id), PrereqIcon(s.Id, r)));
                if (g.Count > 0) groups.Add(g);
            }

        if (passives != null)
            foreach (EquippedPassive p in passives.EquippedPassives)
            {
                if (!passives.CanEvolve(p)) continue;
                var g = new List<(Sprite, Sprite)>();
                foreach (int r in PlayerPassives.SelectableRoutes(p))
                    if (passives.IsRouteUnlocked(p.Id, r))
                        g.Add((GetPassiveEvoIcon(p.Id, r) ?? GetPassiveIcon(p.Id), PrereqIcon(p.Id, r)));
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
        ShowEvolution(true);
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

        SfxPlayer.Play(SfxId.TreasureOpen);
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
        yield return new WaitUntil(() => treasureDismissed || TreasureSkipKeyPressed());

        treasureMode = false;
        CloseTreasure();
        QueueNextPending();
    }

    // 보물 화면은 마우스 클릭 전용이었다 — 키보드로도 넘길 수 있게 한다(칸반 "플테후 제안사항").
    // ESC는 일부러 뺐다. 일시정지 메뉴가 같은 키를 먹는다.
    private static bool TreasureSkipKeyPressed()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return false;
        return kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame
            || kb.numpadEnterKey.wasPressedThisFrame;
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
        rt.DOScale(Vector3.one, 0.2f).SetEase(Ease.OutBack).SetUpdate(true);
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
                LevelText = Loc.F("ui.levelup.level", captured.Level + 1),
                Description = PlayerSkills.DescribeUpgradeEffect(captured, captured.Level + 1),
                Icon = GetActiveIcon(captured),
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
                LevelText = Loc.F("ui.levelup.level", captured.Level + 1),
                Description = PlayerPassives.DescribePassiveLevelEffect(captured.Id),
                Icon = GetPassiveIcon(captured),
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

    // fromChoice = 갈림길("보물 상자 vs 진화")에서 진화를 골라 들어온 경우.
    // 그때만 되돌아가기(X)가 뜬다 — 레벨업 보상이나 밀린 대기열로 열린 진화는 돌아갈 곳이 없다.
    private void ShowEvolution(bool fromChoice = false)
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        evolutionCameFromChoice = fromChoice;
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
                LevelText = Loc.F("ui.levelup.evoStage", captured.EvolutionStage + 1),
                IsEvolution = true,
                Description = DescribeEvolutionChoice(skills, captured),
                Icon = GetActiveIcon(captured), // 2차 진화 대상이면 1차 때 고른 루트의 그림이 뜬다
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
                LevelText = Loc.F("ui.levelup.evoStage", captured.EvolutionStage + 1),
                IsEvolution = true,
                Description = DescribeEvolutionChoice(passives, captured),
                Icon = GetPassiveIcon(captured),
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
                LevelText = Loc.F("ui.levelup.levelRange", captured.Level, target),
                Description = Loc.F("ui.levelup.instantLevels", target - captured.Level),
                Icon = GetActiveIcon(captured),
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
                LevelText = Loc.F("ui.levelup.levelRange", captured.Level, target),
                Description = Loc.F("ui.levelup.instantLevels", target - captured.Level),
                Icon = GetPassiveIcon(captured),
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
        return Loc.F("ui.levelup.routeChoice", string.Join(" / ", names));
    }

    private static string DescribeEvolutionChoice(PlayerPassives passives, EquippedPassive p)
    {
        if (p.EvolutionStage > 0) return $"→ {EvolutionRoutes.EvolvedName(p.Id, p.Route, 2)}";
        string[] names = PlayerPassives.SelectableRoutes(p)
            .Where(r => passives.IsRouteUnlocked(p.Id, r))
            .Select(r => EvolutionRoutes.EvolvedName(p.Id, r, 1))
            .ToArray();
        return Loc.F("ui.levelup.routeChoice", string.Join(" / ", names));
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

    // ── 진화 아이콘 ─────────────────────────────────────────────────────────
    // 루트마다 그림이 다르다(파일명 R1=루트0 / R2=루트1). 1차·2차는 같은 그림을 쓴다.
    // 진화 아이콘 배열은 **여기 하나만** 배선한다 — HUD·진화 트리는 LevelUpUI.Instance에서 빌려 간다.
    // (원본 아이콘처럼 3곳에 중복 배선하면 32칸짜리 배열이 3벌이 되어 서로 어긋난다.)
    public Sprite GetActiveEvoIcon(ActiveSkillId id, int route) => GetIcon(activeEvoIcons, (int)id * 2 + route);
    public Sprite GetPassiveEvoIcon(PassiveSkillId id, int route) => GetIcon(passiveEvoIcons, (int)id * 2 + route);

    // "지금 이 스킬의 아이콘" — 진화했으면 고른 루트의 진화 아이콘, 아니면 원본. 그림이 비면 원본으로 떨어진다.
    public Sprite GetActiveIcon(EquippedSkill s) =>
        (s.EvolutionStage > 0 && s.Route >= 0 ? GetActiveEvoIcon(s.Id, s.Route) : null) ?? GetActiveIcon(s.Id);

    public Sprite GetPassiveIcon(EquippedPassive p) =>
        (p.EvolutionStage > 0 && p.Route >= 0 ? GetPassiveEvoIcon(p.Id, p.Route) : null) ?? GetPassiveIcon(p.Id);

    // 캐릭터 선택 화면도 시작 스킬 설명을 그대로 쓴다(설명 문구가 두 군데로 갈리지 않게).
    // ⚠️ BasicAttack·Swing은 시작 전용이라 레벨업 선택지엔 안 뜨지만 캐릭터 선택 화면이 설명을 보여준다 —
    //    표에 키가 둘 다 있어야 한다.
    public static string GetActiveSkillDescription(ActiveSkillId id) =>
        Loc.TOr("skill.desc." + id, "");

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
