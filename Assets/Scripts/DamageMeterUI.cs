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
        if (shown || GameManager.Instance == null) return;
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
            titleText.text = isClear ? "GAME CLEAR" : "GAME OVER";
            titleText.color = isClear ? GameClearColor : GameOverColor;
        }
        if (earnedText != null) earnedText.text = Loc.F("ui.result.earned", MetaRun.RunCurrency);
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
