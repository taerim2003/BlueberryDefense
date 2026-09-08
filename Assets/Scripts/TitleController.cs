using UnityEngine;
using UnityEngine.UI;

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
    [SerializeField] private CharacterSelectUI characterSelect;

    // 게임오버 화면의 "업그레이드"가 세워두는 표시. 타이틀이 뜨면 스킬트리를 바로 연다.
    // static이라 씬 전환을 넘어가고, 한 번 쓰면 여기서 지운다(다음에 타이틀로 와도 안 열리게).
    public static bool OpenSkillTreeOnStart;

    private void Start()
    {
        if (playButton != null) playButton.onClick.AddListener(Play);
        if (upgradeButton != null) upgradeButton.onClick.AddListener(OpenUpgrade);
        if (collectionButton != null) collectionButton.onClick.AddListener(OpenCollection);
        if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
        if (quitButton != null) quitButton.onClick.AddListener(Quit);

        if (OpenSkillTreeOnStart)
        {
            OpenSkillTreeOnStart = false;
            OpenUpgrade();
        }
    }

    // OptionsMenu는 씬에 프리팹 인스턴스로 놓여 있다 — 자기 Instance를 세우므로 클릭 시점에 찾는다.
    private void OpenSettings()
    {
        if (OptionsMenu.Instance != null) OptionsMenu.Instance.Open();
    }

    // CollectionUI도 같은 방식(씬의 프리팹 인스턴스)이라 클릭 시점에 찾는다.
    private void OpenCollection()
    {
        if (CollectionUI.Instance != null) CollectionUI.Instance.Open();
    }

    private void Play()
    {
        // 캐릭터 → 맵 순으로 두 단계다. 캐릭터를 고르면 MapSelectUI가 이어받아 맵 화면을 연다.
        if (characterSelect != null) characterSelect.Open();
        else if (mapSelect != null) mapSelect.Open(); // 캐릭터 화면이 없으면 곧장 맵으로
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
