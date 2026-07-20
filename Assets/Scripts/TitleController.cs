using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 타이틀 씬의 메인 메뉴. Play/업그레이드/컬렉션/설정/종료.
public class TitleController : MonoBehaviour
{
    [SerializeField] private Button playButton;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private Button collectionButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private SkillTreeUI skillTree;
    [SerializeField] private MapSelectUI mapSelect;
    [SerializeField] private TMP_Text currencyText;

    private void Start()
    {
        if (playButton != null) playButton.onClick.AddListener(Play);
        if (upgradeButton != null) upgradeButton.onClick.AddListener(OpenUpgrade);
        if (quitButton != null) quitButton.onClick.AddListener(Quit);
        // 컬렉션/설정은 Phase 2 — 지금은 자리만
        RefreshCurrency();
    }

    private void OnEnable() => RefreshCurrency();

    public void RefreshCurrency()
    {
        if (currencyText != null) currencyText.text = SkillTreeSave.EssenceEarned + " 정수";
    }

    private void Play()
    {
        // 바로 씬 로드하지 않고 맵 선택 패널을 연다. 실제 로드는 MapSelectUI가 "시작" 버튼으로 처리.
        if (mapSelect != null) mapSelect.Open();
    }

    private void OpenUpgrade()
    {
        if (skillTree != null) skillTree.Open();
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
