using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 게임오버 또는 게임클리어 시 딜미터기(스킬별 누적 피해량) 패널을 띄운다.
public class DamageMeterUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private UITransition panelTransition; // 있으면 뜰 때 팝 연출을 대신 태운다
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private TMP_Text earnedText;       // 이번 판 획득 정수
    [SerializeField] private Button returnToTitleButton; // 타이틀로 복귀
    [SerializeField] private Button retryButton;         // 같은 캐릭터·맵·승천으로 다시하기
    [SerializeField] private Button upgradeButton;       // 타이틀로 가면서 스킬트리를 바로 연다

    private static readonly Color GameOverColor = new Color(1f, 0.3f, 0.3f);
    private static readonly Color GameClearColor = new Color(1f, 0.85f, 0.3f);

    private bool shown;

    // 키보드/패드 포커스. 화면 배치는 왼쪽부터 다시하기 · 업그레이드 · 타이틀로(실측)이고 왼쪽에서 시작한다.
    // ⚠️ 이 화면은 다른 모달과 달리 ModalPause를 쓰지 않는다(GameManager가 직접 timeScale을 0으로 만든다).
    private readonly UIFocusGroup focus = new UIFocusGroup();

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (returnToTitleButton != null)
            returnToTitleButton.onClick.AddListener(() => GameManager.Instance?.ReturnToTitle());
        if (retryButton != null)
            retryButton.onClick.AddListener(() => GameManager.Instance?.Retry());
        if (upgradeButton != null)
            upgradeButton.onClick.AddListener(() => GameManager.Instance?.ReturnToTitleAndOpenSkillTree());
    }

    private void Update()
    {
        if (shown)
        {
            focus.Tick();
            return;
        }
        if (GameManager.Instance == null) return;
        if (!GameManager.Instance.IsGameOver && !GameManager.Instance.IsGameClear) return;

        shown = true;
        Show(GameManager.Instance.IsGameClear);
    }

    private void Show(bool isClear)
    {
        if (panelTransition != null) panelTransition.Show();
        else if (panelRoot != null) panelRoot.SetActive(true);
        if (titleText != null)
        {
            titleText.text = Loc.T(isClear ? "ui.result.gameClear" : "ui.result.gameOver");
            titleText.color = isClear ? GameClearColor : GameOverColor;
        }
        if (earnedText != null) earnedText.text = Loc.F("ui.result.earned", MetaRun.RunCurrency);

        // 🔴 아래 `bodyText == null` 조기 반환보다 **앞에서** 연다 — 뒤에 두면 딜미터 본문이 비어 있는
        //    프리팹에서 키보드 조작만 조용히 죽는다(화면은 멀쩡해서 원인을 못 찾는다).
        focus.Open(new[] { retryButton, upgradeButton, returnToTitleButton }, 0);

        if (bodyText == null) return;

        var breakdown = DamageMeter.GetBreakdown();
        float total = DamageMeter.TotalDamage;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine(Loc.F("ui.result.total", total.ToString("N0")));
        sb.AppendLine();

        foreach (var (name, damage) in breakdown)
        {
            float percent = total > 0f ? damage / total * 100f : 0f;
            sb.AppendLine($"{name}   {damage:N0}   ({percent:F1}%)");
        }

        bodyText.text = sb.ToString();
    }
}
