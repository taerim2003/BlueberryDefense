using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
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
    [SerializeField] private TMP_Text currencyText;

    private const string GameSceneName = "SampleScene";

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

    private void Play() => SceneManager.LoadScene(GameSceneName);

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
