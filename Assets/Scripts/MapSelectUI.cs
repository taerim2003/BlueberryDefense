using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// 타이틀 씬의 맵 선택 패널. Play 버튼이 열고, 맵 카드로 선택한 뒤 "시작"으로 확정한다.
// SkillTreeRoot와 동일한 "버튼으로 토글하는 전체화면 패널" 패턴. 카드는 template을 런타임 복제해 생성.
// 카드 클릭 = 선택(하이라이트)만, 실제 게임 진입은 startButton(확인 단계).
public class MapSelectUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private MapDefinition[] maps;

    [Header("Shell")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private UITransition panelTransition; // 있으면 열고 닫을 때 팝 연출을 대신 태운다
    [SerializeField] private PanelSplitTransition splitTransition; // 위아래로 갈라지는 화면 전환(우선)
    [SerializeField] private Transform cardContainer;   // HorizontalLayoutGroup — 카드들이 담김
    [SerializeField] private GameObject cardTemplate;    // 비활성 카드 원본(container 안). 자식: Thumb(Image)/Name(TMP_Text)/Frame(Image)

    [Header("Actions")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button backButton;

    [Header("Ascension (맵 아래 승천 선택 — StS식 ◀ 등급 ▶)")]
    [SerializeField] private AscensionTable ascensionTable; // 미할당 시 기본표 폴백
    [SerializeField] private Button ascPrevButton;          // ◀ 낮추기
    [SerializeField] private Button ascNextButton;          // ▶ 올리기
    [SerializeField] private TMP_Text ascLevelText;         // "승천 2"
    [SerializeField] private TMP_Text ascDescText;          // 해당 등급 효과 설명

    [Header("Character (팝업으로 변경)")]
    [SerializeField] private CharacterSelectUI characterSelect;   // "캐릭터 변경"이 여는 팝업
    [SerializeField] private Button changeCharacterButton;        // 캐릭터 변경 팝업 열기
    [SerializeField] private TMP_Text characterNameText;          // 현재 선택된 캐릭터 이름
    [SerializeField] private Image characterPortrait;             // 현재 캐릭터 초상화(선택, 없으면 숨김)

    // 잠긴 맵 카드의 썸네일 색 — 실루엣만 남긴다(CharacterSelectUI와 같은 규칙).
    private static readonly Color LockedSilhouette = new Color(0.08f, 0.08f, 0.1f, 0.85f);

    // 맵을 고르면 그 카드의 배경 컷을 한 바퀴 돌린다. 인게임 루프(backgroundFrameSeconds=1.2초)는
    // 카드 연출로 쓰기엔 늘어져서 여기만 따로 빠르게 센다.
    [SerializeField] private float cardAnimFrameSeconds = 0.18f;

    private readonly List<GameObject> cardFrames = new List<GameObject>();
    private readonly List<JuicyButton> cardJuicy = new List<JuicyButton>(); // 고른 카드만 원본 크기·색으로 남긴다
    private readonly List<Image> cardThumbs = new List<Image>();            // 배경 컷을 여기서 돌린다
    private Coroutine thumbRoutine;
    private Image animThumb;      // 지금 돌고 있는 썸네일(끊겼을 때 정지 그림으로 되돌리려고)
    private Sprite animStill;
    private int selectedIndex = -1;
    private bool built;

    private int ascensionLevel = 1;
    private AscensionTable AscTable => ascensionTable != null ? ascensionTable : AscensionTable.Default;
    // 고를 수 있는 최고 등급 = **선택한 맵에서** 깬 만큼 + 1 (표에 존재하는 범위 내).
    // 맵마다 따로 올린다 — 앞 맵에서 승천을 올려놨다고 새 맵이 고승천으로 시작하면
    // 맵을 넘어갈 때 난이도가 안 떨어져 진행이 무너진다(StS가 캐릭터별로 승천을 나누는 것과 같은 이유).
    // ⚠️ 근거가 전역 AscensionSave가 아니라 맵별 MapClearSave다. 맵을 바꾸면 이 값도 바뀌므로
    //    Select()에서 RefreshAscension()을 반드시 다시 부를 것.
    private int MaxSelectableAscension
    {
        get
        {
            MapDefinition map = (maps != null && selectedIndex >= 0 && selectedIndex < maps.Length)
                ? maps[selectedIndex] : null;
            int cleared = map != null ? MapClearSave.ClearedAscension(map.name) : 0;
            return Mathf.Clamp(cleared + 1, 1, AscTable.MaxLevel);
        }
    }

    private const string GameSceneName = "SampleScene";

    private void Awake()
    {
        if (startButton != null) startButton.onClick.AddListener(Confirm);
        if (backButton != null) backButton.onClick.AddListener(Back);
        if (changeCharacterButton != null && characterSelect != null)
            changeCharacterButton.onClick.AddListener(characterSelect.Open);
        if (characterSelect != null)
        {
            characterSelect.OnSelectionChanged += RefreshCharacter; // 고르는 중엔 표시만 갱신
            characterSelect.OnConfirmed += OnCharacterConfirmed;    // 확정해야 이 화면이 열린다
        }
        if (ascPrevButton != null) ascPrevButton.onClick.AddListener(() => ChangeAscension(-1));
        if (ascNextButton != null) ascNextButton.onClick.AddListener(() => ChangeAscension(+1));
        if (cardTemplate != null) cardTemplate.SetActive(false);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Open()
    {
        if (!built) BuildCards();
        if (splitTransition != null) splitTransition.Show();
        else if (panelTransition != null) panelTransition.Show();
        else if (panelRoot != null) panelRoot.SetActive(true);
        Select(FirstUnlockedIndex());
        RefreshCharacter();
        // 지난 선택을 이어받되 해금 범위로 클램프
        ascensionLevel = Mathf.Clamp(RunConfig.AscensionLevel, 1, MaxSelectableAscension);
        RefreshAscension();
    }

    // ── 승천 선택 (StS식: 화살표로 등급 조절, 해금된 데까지만) ──
    private void ChangeAscension(int delta)
    {
        ascensionLevel = Mathf.Clamp(ascensionLevel + delta, 1, MaxSelectableAscension);
        RefreshAscension();
    }

    private void RefreshAscension()
    {
        int max = MaxSelectableAscension;
        ascensionLevel = Mathf.Clamp(ascensionLevel, 1, max);

        if (ascLevelText != null) ascLevelText.text = DifficultyName(ascensionLevel);
        if (ascDescText != null) ascDescText.text = DescribeAscension(ascensionLevel, max);
        if (ascPrevButton != null) ascPrevButton.interactable = ascensionLevel > 1;
        if (ascNextButton != null) ascNextButton.interactable = ascensionLevel < max;
    }

    // 난이도 이름. 표에 등급이 셋뿐이라 그대로 쉬움/보통/어려움으로 부른다.
    // 등급이 늘어나면 이름이 모자라므로 그때는 "어려움 +N"으로 이어 붙인다.
    private const int DifficultyNameCount = 3;

    private static string DifficultyName(int level)
    {
        int i = Mathf.Clamp(level, 1, int.MaxValue) - 1;
        if (i < DifficultyNameCount) return Loc.T("ui.difficulty." + i);
        return Loc.F("ui.difficulty.plus", Loc.T("ui.difficulty." + (DifficultyNameCount - 1)), i - DifficultyNameCount + 1);
    }

    // 등급 효과를 % 증가로 표기. 최고 해금 등급에 있고 위 등급이 더 있으면 해금 안내를 덧붙인다.
    private string DescribeAscension(int level, int max)
    {
        string body;
        if (level <= 1)
        {
            body = Loc.T("ui.ascension.base");
        }
        else
        {
            AscensionTier t = AscTable.Get(level);
            body = Loc.F("ui.ascension.stats", Pct(t.hpMult), Pct(t.speedMult), Pct(t.damageMult))
                 + "\n<color=#8FE38A>" + Loc.F("ui.ascension.essence", Pct(t.essenceMult)) + "</color>";
        }

        if (level >= max && max < AscTable.MaxLevel)
            body += "\n<color=#FFC864>" + Loc.T("ui.ascension.nextUnlock") + "</color>";
        return body;
    }

    private static string Pct(float mult) => Mathf.RoundToInt((mult - 1f) * 100f) + "%";

    // 현재 선택된 캐릭터를 맵 화면 하단에 표시(이름·초상화). 초상화 없으면 숨김.
    private void RefreshCharacter()
    {
        var chr = characterSelect != null ? characterSelect.Selected : null;
        if (characterNameText != null)
            characterNameText.text = chr == null ? "" : chr.Name;
        if (characterPortrait != null)
        {
            bool hasPortrait = chr != null && chr.portrait != null;
            if (hasPortrait) characterPortrait.sprite = chr.portrait;
            characterPortrait.enabled = hasPortrait;
        }
    }

    public void Close()
    {
        if (splitTransition != null) splitTransition.Hide();
        else if (panelTransition != null) panelTransition.Hide();
        else if (panelRoot != null) panelRoot.SetActive(false);
    }

    // 캐릭터 화면에서 "선택"을 누르면 그 다음 단계인 맵 선택으로 넘어온다
    // (한 화면에 다 담으면 번잡해서 둘로 나눴다).
    private void OnCharacterConfirmed()
    {
        RefreshCharacter();
        Open();
    }

    // 맵 화면에서 뒤로 = 한 단계 앞인 캐릭터 선택으로 돌아간다.
    private void Back()
    {
        Close();
        if (characterSelect != null) characterSelect.Open();
    }

    private void BuildCards()
    {
        built = true;
        if (maps == null || cardTemplate == null || cardContainer == null) return;

        for (int i = 0; i < maps.Length; i++)
        {
            var map = maps[i];
            var card = Instantiate(cardTemplate, cardContainer);
            card.SetActive(true);

            bool locked = map != null && !map.IsUnlocked;

            var thumb = FindDeep(card.transform, "Thumb")?.GetComponent<Image>();
            if (thumb != null && map != null)
            {
                thumb.sprite = map.background;
                // 잠긴 맵은 실루엣으로만 — 뭐가 있는지는 알되 어떤 곳인지는 모르게.
                thumb.color = locked ? LockedSilhouette : Color.white;
            }
            cardThumbs.Add(thumb);

            var nameText = FindDeep(card.transform, "Name")?.GetComponent<TMP_Text>();
            if (nameText != null && map != null)
                nameText.text = locked
                    ? "🔒 " + map.UnlockConditionText()
                    : map.Name;

            var frame = FindDeep(card.transform, "Frame")?.gameObject;
            if (frame != null) frame.SetActive(false);
            cardFrames.Add(frame);

            int idx = i; // 클로저 캡처
            var juicy = card.GetComponent<JuicyButton>();
            cardJuicy.Add(juicy);

            var btn = card.GetComponent<Button>();
            if (btn != null)
            {
                btn.interactable = !locked; // 잠긴 맵은 눌러도 선택되지 않는다
                btn.onClick.AddListener(() => Select(idx));

                // JuicyButton은 Button.interactable을 보지 않는다 — 끄지 않으면 잠긴 카드도 호버에 반응한다.
                if (juicy != null) juicy.enabled = !locked;
            }
        }
    }

    // 카드 속 부품을 깊이와 상관없이 찾는다. Transform.Find는 직속 자식만 보기 때문에,
    // 씬에서 Thumb을 Bg 아래로 옮기는(마스크를 걸려고) 순간 조용히 null이 되어
    // 모든 카드가 템플릿에 구워진 그림을 그대로 쓰게 된다.
    private static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // 처음 열 때 커서를 둘 곳. 잠긴 맵에 커서가 앉으면 "시작"이 눌리는 순간 잠긴 판이 시작된다.
    private int FirstUnlockedIndex()
    {
        if (maps == null) return -1;
        for (int i = 0; i < maps.Length; i++)
            if (maps[i] != null && maps[i].IsUnlocked) return i;
        return -1;
    }

    private void Select(int index)
    {
        // 잠긴 맵은 선택 자체를 막는다(버튼도 비활성이지만 코드 경로가 하나 더 있다 — Open의 초기 선택).
        if (maps != null && index >= 0 && index < maps.Length && maps[index] != null && !maps[index].IsUnlocked) return;

        selectedIndex = index;
        for (int i = 0; i < cardFrames.Count; i++)
            if (cardFrames[i] != null) cardFrames[i].SetActive(i == index);
        for (int i = 0; i < cardJuicy.Count; i++)
            if (cardJuicy[i] != null) cardJuicy[i].SetSelected(i == index);
        if (startButton != null) startButton.interactable = index >= 0;

        PlayThumbAnimation(index);

        // 승천 상한은 맵마다 다르다 — 맵을 바꾸면 범위와 화살표 활성 상태를 다시 계산해야 한다.
        RefreshAscension();
    }

    // 고른 맵의 배경 컷을 한 바퀴 돌리고 정지 그림으로 돌아온다.
    // backgroundFrames가 비어 있는 맵(해안가)은 조용히 넘어간다.
    private void PlayThumbAnimation(int index)
    {
        if (thumbRoutine != null) { StopCoroutine(thumbRoutine); thumbRoutine = null; }
        StopThumbAnimation();

        var map = maps != null && index >= 0 && index < maps.Length ? maps[index] : null;
        if (map == null || map.backgroundFrames == null || map.backgroundFrames.Length < 2) return;
        if (index >= cardThumbs.Count || cardThumbs[index] == null) return;

        thumbRoutine = StartCoroutine(ThumbRoutine(cardThumbs[index], map));
    }

    private IEnumerator ThumbRoutine(Image thumb, MapDefinition map)
    {
        animThumb = thumb;
        animStill = map.background;

        float step = Mathf.Max(0.02f, cardAnimFrameSeconds);
        foreach (var frame in map.backgroundFrames)
        {
            if (frame != null) thumb.sprite = frame;
            // 타이틀 화면은 timeScale이 0일 수 있다 — 실시간으로 센다.
            yield return new WaitForSecondsRealtime(step);
        }

        thumbRoutine = null;
        StopThumbAnimation();
    }

    private void StopThumbAnimation()
    {
        if (animThumb == null) return;
        if (animStill != null) animThumb.sprite = animStill;
        animThumb = null;
        animStill = null;
    }

    private void Confirm()
    {
        if (maps == null || selectedIndex < 0 || selectedIndex >= maps.Length) return;
        if (maps[selectedIndex] != null && !maps[selectedIndex].IsUnlocked) return; // 잠긴 맵으로는 판이 시작되지 않는다
        RunConfig.Map = maps[selectedIndex];
        RunConfig.Character = characterSelect != null ? characterSelect.Selected : null;
        RunConfig.AscensionLevel = Mathf.Clamp(ascensionLevel, 1, MaxSelectableAscension);
        SceneManager.LoadScene(GameSceneName);
    }
}
