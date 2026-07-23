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

    private readonly List<GameObject> cardFrames = new List<GameObject>();
    private int selectedIndex = -1;
    private bool built;

    private int ascensionLevel = 1;
    private AscensionTable AscTable => ascensionTable != null ? ascensionTable : AscensionTable.Default;
    // 고를 수 있는 최고 등급 = 해금된 만큼(표에 존재하는 범위 내)
    private int MaxSelectableAscension => Mathf.Clamp(AscensionSave.Unlocked, 1, AscTable.MaxLevel);

    private const string GameSceneName = "SampleScene";

    private void Awake()
    {
        if (startButton != null) startButton.onClick.AddListener(Confirm);
        if (backButton != null) backButton.onClick.AddListener(Close);
        if (changeCharacterButton != null && characterSelect != null)
            changeCharacterButton.onClick.AddListener(characterSelect.Open);
        if (characterSelect != null) characterSelect.OnSelectionChanged += RefreshCharacter;
        if (ascPrevButton != null) ascPrevButton.onClick.AddListener(() => ChangeAscension(-1));
        if (ascNextButton != null) ascNextButton.onClick.AddListener(() => ChangeAscension(+1));
        if (cardTemplate != null) cardTemplate.SetActive(false);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Open()
    {
        if (!built) BuildCards();
        if (panelRoot != null) panelRoot.SetActive(true);
        Select(maps != null && maps.Length > 0 ? 0 : -1);
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

        if (ascLevelText != null) ascLevelText.text = "승천 " + ascensionLevel;
        if (ascDescText != null) ascDescText.text = DescribeAscension(ascensionLevel, max);
        if (ascPrevButton != null) ascPrevButton.interactable = ascensionLevel > 1;
        if (ascNextButton != null) ascNextButton.interactable = ascensionLevel < max;
    }

    // 등급 효과를 % 증가로 표기. 최고 해금 등급에 있고 위 등급이 더 있으면 해금 안내를 덧붙인다.
    private string DescribeAscension(int level, int max)
    {
        string body;
        if (level <= 1)
        {
            body = "기본 난이도";
        }
        else
        {
            AscensionTier t = AscTable.Get(level);
            body = $"적 체력 +{Pct(t.hpMult)}   이동속도 +{Pct(t.speedMult)}   피해 +{Pct(t.damageMult)}"
                 + $"\n<color=#8FE38A>정수 획득 +{Pct(t.essenceMult)}</color>";
        }

        if (level >= max && max < AscTable.MaxLevel)
            body += "\n<color=#FFC864>클리어하면 다음 승천이 열립니다</color>";
        return body;
    }

    private static string Pct(float mult) => Mathf.RoundToInt((mult - 1f) * 100f) + "%";

    // 현재 선택된 캐릭터를 맵 화면 하단에 표시(이름·초상화). 초상화 없으면 숨김.
    private void RefreshCharacter()
    {
        var chr = characterSelect != null ? characterSelect.Selected : null;
        if (characterNameText != null)
            characterNameText.text = chr == null ? "" :
                (string.IsNullOrEmpty(chr.displayName) ? chr.name : chr.displayName);
        if (characterPortrait != null)
        {
            bool hasPortrait = chr != null && chr.portrait != null;
            if (hasPortrait) characterPortrait.sprite = chr.portrait;
            characterPortrait.enabled = hasPortrait;
        }
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
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

            var thumb = card.transform.Find("Thumb")?.GetComponent<Image>();
            if (thumb != null && map != null) thumb.sprite = map.background;

            var nameText = card.transform.Find("Name")?.GetComponent<TMP_Text>();
            if (nameText != null && map != null)
                nameText.text = string.IsNullOrEmpty(map.displayName) ? map.name : map.displayName;

            var frame = card.transform.Find("Frame")?.gameObject;
            if (frame != null) frame.SetActive(false);
            cardFrames.Add(frame);

            int idx = i; // 클로저 캡처
            var btn = card.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() => Select(idx));
        }
    }

    private void Select(int index)
    {
        selectedIndex = index;
        for (int i = 0; i < cardFrames.Count; i++)
            if (cardFrames[i] != null) cardFrames[i].SetActive(i == index);
        if (startButton != null) startButton.interactable = index >= 0;
    }

    private void Confirm()
    {
        if (maps == null || selectedIndex < 0 || selectedIndex >= maps.Length) return;
        RunConfig.Map = maps[selectedIndex];
        RunConfig.Character = characterSelect != null ? characterSelect.Selected : null;
        RunConfig.AscensionLevel = Mathf.Clamp(ascensionLevel, 1, MaxSelectableAscension);
        SceneManager.LoadScene(GameSceneName);
    }
}
