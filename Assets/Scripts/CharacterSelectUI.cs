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

    // 잠긴 캐릭터 카드의 초상화 색 — 거의 검은 실루엣만 남긴다.
    private static readonly Color LockedSilhouette = new Color(0.08f, 0.08f, 0.1f, 0.85f);

    private readonly List<GameObject> cardFrames = new List<GameObject>();
    private int selectedIndex;   // 기본 0 = 로스터 첫 캐릭터(= 프리팹 기본값과 동일)
    private bool built;

    // 현재 선택된 캐릭터. 로스터가 비어 있으면 null(→ RunConfig.Character=null → 프리팹 기본값).
    public CharacterDefinition Selected =>
        characters != null && selectedIndex >= 0 && selectedIndex < characters.Length
            ? characters[selectedIndex] : null;

    private void Awake()
    {
        RestoreSelection(); // 카드를 짓기 전에 복원해야 Open() 없이도 Selected가 옳다(MapSelectUI가 바로 읽는다)
        if (backButton != null) backButton.onClick.AddListener(Close);
        if (cardTemplate != null) cardTemplate.SetActive(false);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // 지난 판에서 고른 캐릭터를 되살린다. 저장된 이름이 로스터에 없거나(에셋 개명) 아직 잠겨 있으면
    // 조용히 첫 캐릭터로 돌아간다 — 선택 불가 캐릭터로 판이 시작되는 것보다 낫다.
    private void RestoreSelection()
    {
        string saved = CharacterSave.Selected;
        if (string.IsNullOrEmpty(saved) || characters == null) return;

        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] == null || characters[i].name != saved) continue;
            if (!characters[i].IsUnlocked) return; // 잠긴 상태면 폴백(selectedIndex=0 유지)
            selectedIndex = i;
            return;
        }
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

            bool locked = chr != null && !chr.IsUnlocked;

            var thumb = card.transform.Find("Thumb")?.GetComponent<Image>();
            if (thumb != null)
            {
                bool hasPortrait = chr != null && chr.portrait != null;
                if (hasPortrait) thumb.sprite = chr.portrait;
                thumb.enabled = hasPortrait; // 초상화 없으면 빈 박스 대신 숨김(이름만 표시)
                // 잠긴 캐릭터는 실루엣으로만 보여준다 — 뭐가 있는지는 알되 누군지는 모르게.
                thumb.color = locked ? LockedSilhouette : Color.white;
            }

            var nameText = card.transform.Find("Name")?.GetComponent<TMP_Text>();
            if (nameText != null && chr != null)
                nameText.text = locked ? "???"
                    : (string.IsNullOrEmpty(chr.displayName) ? chr.name : chr.displayName);

            var frame = card.transform.Find("Frame")?.gameObject;
            if (frame != null) frame.SetActive(false);
            cardFrames.Add(frame);

            int idx = i; // 클로저 캡처
            var btn = card.GetComponent<Button>();
            if (btn != null)
            {
                btn.interactable = !locked; // 잠긴 카드는 눌러도 선택되지 않는다
                btn.onClick.AddListener(() => Pick(idx));
            }
        }
    }

    // 카드 클릭: 선택 확정 → 표시 갱신 알림 → 팝업 닫기.
    private void Pick(int index)
    {
        if (characters != null && index >= 0 && index < characters.Length
            && characters[index] != null && !characters[index].IsUnlocked) return; // 잠긴 캐릭터는 선택 불가

        selectedIndex = index;
        if (characters[index] != null) CharacterSave.Save(characters[index].name); // 다음 판에도 이 캐릭터로 시작한다
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
