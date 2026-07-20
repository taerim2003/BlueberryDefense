using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 맵 선택 화면 위에 뜨는 캐릭터 선택 팝업. MapSelectUI의 "캐릭터 변경" 버튼이 연다.
// MapSelectUI(맵 선택)와 대칭인 카드 UI지만, 씬을 로드하지 않고 선택값만 갱신한 뒤 팝업을 닫는다.
// 카드 클릭 = 즉시 선택 후 닫힘. 선택이 바뀌면 OnSelectionChanged로 MapSelectUI 표시를 갱신.
public class CharacterSelectUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private CharacterDefinition[] characters;

    [Header("Shell")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform cardContainer;   // 카드들이 담기는 컨테이너
    [SerializeField] private GameObject cardTemplate;    // 비활성 카드 원본. 자식: Thumb(Image)/Name(TMP_Text)/Frame(Image)

    [Header("Actions")]
    [SerializeField] private Button backButton;

    // 선택이 바뀌면 발생. MapSelectUI가 구독해 현재 캐릭터 표시를 갱신한다.
    public event Action OnSelectionChanged;

    private readonly List<GameObject> cardFrames = new List<GameObject>();
    private int selectedIndex;   // 기본 0 = 로스터 첫 캐릭터(= 프리팹 기본값과 동일)
    private bool built;

    // 현재 선택된 캐릭터. 로스터가 비어 있으면 null(→ RunConfig.Character=null → 프리팹 기본값).
    public CharacterDefinition Selected =>
        characters != null && selectedIndex >= 0 && selectedIndex < characters.Length
            ? characters[selectedIndex] : null;

    private void Awake()
    {
        if (backButton != null) backButton.onClick.AddListener(Close);
        if (cardTemplate != null) cardTemplate.SetActive(false);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Open()
    {
        if (!built) BuildCards();
        if (panelRoot != null) panelRoot.SetActive(true);
        Highlight(selectedIndex);
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void BuildCards()
    {
        built = true;
        if (characters == null || cardTemplate == null || cardContainer == null) return;

        for (int i = 0; i < characters.Length; i++)
        {
            var chr = characters[i];
            var card = Instantiate(cardTemplate, cardContainer);
            card.SetActive(true);

            var thumb = card.transform.Find("Thumb")?.GetComponent<Image>();
            if (thumb != null)
            {
                bool hasPortrait = chr != null && chr.portrait != null;
                if (hasPortrait) thumb.sprite = chr.portrait;
                thumb.enabled = hasPortrait; // 초상화 없으면 빈 박스 대신 숨김(이름만 표시)
            }

            var nameText = card.transform.Find("Name")?.GetComponent<TMP_Text>();
            if (nameText != null && chr != null)
                nameText.text = string.IsNullOrEmpty(chr.displayName) ? chr.name : chr.displayName;

            var frame = card.transform.Find("Frame")?.gameObject;
            if (frame != null) frame.SetActive(false);
            cardFrames.Add(frame);

            int idx = i; // 클로저 캡처
            var btn = card.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() => Pick(idx));
        }
    }

    // 카드 클릭: 선택 확정 → 표시 갱신 알림 → 팝업 닫기.
    private void Pick(int index)
    {
        selectedIndex = index;
        Highlight(index);
        OnSelectionChanged?.Invoke();
        Close();
    }

    private void Highlight(int index)
    {
        for (int i = 0; i < cardFrames.Count; i++)
            if (cardFrames[i] != null) cardFrames[i].SetActive(i == index);
    }
}
