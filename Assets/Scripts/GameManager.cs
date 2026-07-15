using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;

public class GameManager : MonoBehaviour
{
    private const int FinalStage = 20; // 이 스테이지를 클리어하면 게임 클리어

    [SerializeField] private float stageDuration = 45f;
    [SerializeField] private float stageBreakDuration = 4.5f; // 스테이지 전환 시 적 스폰이 멈추는 텀
    [SerializeField] private StageTable stageTable;
    [SerializeField] private GameObject heartPickupPrefab;
    [SerializeField] private GameObject essencePickupPrefab;

    public static GameManager Instance { get; private set; }

    public bool IsGameOver { get; private set; }
    public bool IsGameClear { get; private set; }
    public int CurrentStage { get; private set; } = 1;
    public bool IsStageBreak => stageBreakTimer > 0f;
    public bool IsSpawningPaused => stageBreakTimer > 0f || waitingForClear;
    public float StageElapsedRatio => Mathf.Clamp01(stageTimer / (CurrentStageData != null ? CurrentStageData.duration : stageDuration));

    public StageData CurrentStageData => stageTable != null ? stageTable.GetStage(CurrentStage) : null;

    private float stageTimer;
    private float stageBreakTimer;
    private bool waitingForClear;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 레벨업 패널이 Time.timeScale=0으로 게임을 멈춘 채 뜨기 때문에,
        // JuicyUI(DOTween) 애니메이션이 그 상태에서도 재생되도록 전역 기본값을 unscaled로 설정.
        DOTween.defaultTimeScaleIndependent = true;

        DamageMeter.Reset();
        Enemy.HeartPickupPrefab = heartPickupPrefab;
        EssencePickup.Prefab = essencePickupPrefab;
    }

    private void Update()
    {
        if (IsGameOver || IsGameClear) return;

        if (stageBreakTimer > 0f)
        {
            stageBreakTimer -= Time.deltaTime;
            return;
        }

        if (waitingForClear)
        {
            if (FindObjectsByType<Enemy>(FindObjectsSortMode.None).Length == 0)
            {
                waitingForClear = false;

                // 스킬트리 자원: 스테이지 클리어마다 가루 +1, 15/20 클리어 시 태양결정 +1
                SkillTreeSave.AddPowder(1);
                if (CurrentStage == 15 || CurrentStage == 20) SkillTreeSave.AddCrystal(1);

                if (CurrentStage >= FinalStage)
                {
                    GameClear();
                    return;
                }

                CurrentStage++;
                stageBreakTimer = stageBreakDuration;
                FindAnyObjectByType<PlayerSkills>()?.ResetAllCooldowns();
            }
            return;
        }

        float duration = CurrentStageData != null ? CurrentStageData.duration : stageDuration;
        stageTimer += Time.deltaTime;
        if (stageTimer >= duration)
        {
            stageTimer = 0f;
            waitingForClear = true; // 스폰은 멈추고, 남은 적을 전부 잡을 때까지 대기
        }
    }

    public void SkipToNextStage()
    {
        waitingForClear = false;
        CurrentStage++;
        stageTimer = 0f;
        stageBreakTimer = stageBreakDuration;
        FindAnyObjectByType<PlayerSkills>()?.ResetAllCooldowns();
    }

    public void GameOver()
    {
        if (IsGameOver) return;

        IsGameOver = true;
        Debug.Log("Game Over");
        BankRunCurrency();
        Time.timeScale = 0f;
    }

    public void GameClear()
    {
        if (IsGameClear) return;

        IsGameClear = true;
        Debug.Log("Game Clear");
        BankRunCurrency();
        Time.timeScale = 0f;
    }

    // 이번 판에서 모은 정수를 영구 저장에 적립(판 종료 시 1회)
    private void BankRunCurrency()
    {
        SkillTreeSave.AddEssence(MetaRun.RunCurrency);
    }

    // 게임오버/클리어 패널의 "타이틀로" 버튼이 호출
    public void ReturnToTitle()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Title");
    }
}
