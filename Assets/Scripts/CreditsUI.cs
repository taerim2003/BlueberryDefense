using UnityEngine;
using UnityEngine.UI;

// 타이틀의 크레딧 창. Btn_크레딧(블루베리 버튼)이 연다.
// 명단 글자는 씬에 있다 — Title `Canvas/CreditsRoot/Scroll/Content` 아래 TMP를 직접 고칠 것(섹션 제목은 LocalizedTmp 키).
public class CreditsUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private UITransition panelTransition; // 있으면 열고 닫을 때 패널 연출을 태운다
    [SerializeField] private Button closeButton;
    [SerializeField] private ScrollRect scroll;

    private const float PadScrollSpeed = 900f; // 스틱을 끝까지 기울였을 때 초당 몇 px 내려가나

    private bool isOpen;
    private readonly UIFocusGroup focus = new UIFocusGroup();

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;
        if (panelTransition != null) panelTransition.Show();
        else if (panelRoot != null) panelRoot.SetActive(true);
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        focus.Open(new[] { closeButton }, 0, closeButton); // 고를 것은 닫기뿐이다. ESC·B = 닫기
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        focus.Close();
        if (panelTransition != null) panelTransition.Hide();
        else if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Update()
    {
        if (!isOpen) return;
        focus.Tick();

        // 스크롤바가 없어서 마우스 휠 말고는 명단을 내릴 수단이 없었다 — 패드는 스틱 위아래로 내린다.
        float axis = focus.IsActive ? GameInput.ScrollAxis() : 0f;
        if (scroll == null || axis == 0f || scroll.content == null) return;
        RectTransform view = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform; // ScrollRect도 비면 자기 rect를 쓴다
        float range = scroll.content.rect.height - view.rect.height;
        if (range <= 0f) return;
        scroll.verticalNormalizedPosition = Mathf.Clamp01(
            scroll.verticalNormalizedPosition + axis * PadScrollSpeed * Time.unscaledDeltaTime / range);
    }
}
