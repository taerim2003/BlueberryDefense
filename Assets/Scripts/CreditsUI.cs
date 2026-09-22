using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 타이틀의 크레딧 창. Btn_크레딧(블루베리 버튼)이 연다.
// 명단 글자는 씬에 있다 — Title `Canvas/CreditsRoot/Scroll/Content` 아래 TMP를 직접 고칠 것(섹션 제목은 LocalizedTmp 키).
public class CreditsUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private UITransition panelTransition; // 있으면 열고 닫을 때 패널 연출을 태운다
    [SerializeField] private Button closeButton;
    [SerializeField] private ScrollRect scroll;

    private bool isOpen;

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
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        if (panelTransition != null) panelTransition.Hide();
        else if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Update()
    {
        if (!isOpen) return;
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
    }
}
