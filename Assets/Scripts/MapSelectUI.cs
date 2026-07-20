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

    private readonly List<GameObject> cardFrames = new List<GameObject>();
    private int selectedIndex = -1;
    private bool built;

    private const string GameSceneName = "SampleScene";

    private void Awake()
    {
        if (startButton != null) startButton.onClick.AddListener(Confirm);
        if (backButton != null) backButton.onClick.AddListener(Close);
        if (cardTemplate != null) cardTemplate.SetActive(false);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Open()
    {
        if (!built) BuildCards();
        if (panelRoot != null) panelRoot.SetActive(true);
        Select(maps != null && maps.Length > 0 ? 0 : -1);
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
        SceneManager.LoadScene(GameSceneName);
    }
}
