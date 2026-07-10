using UnityEngine;
using DG.Tweening;

public class GameManager : MonoBehaviour
{
    [SerializeField] private float stageDuration = 45f;
    [SerializeField] private float stageBreakDuration = 4.5f; // 스테이지 전환 시 적 스폰이 멈추는 텀
    [SerializeField] private StageTable stageTable;

    public static GameManager Instance { get; private set; }

    public bool IsGameOver { get; private set; }
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
    }

    private void Update()
    {
        if (IsGameOver) return;

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
        Time.timeScale = 0f;
    }
}

[System.Serializable]
public class StageData
{
    public int stageNumber = 1;
    public float duration = 45f;
    public float spawnInterval = 1.5f;
    public float eliteChance = 0f;
    public float paperPlaneChance = 0f;
    public float enemyHpMultiplier = 1f;
    public float enemySpeedMultiplier = 1f;
    public float enemyDamageMultiplier = 1f;
}

[CreateAssetMenu(fileName = "StageTable", menuName = "BlueberryDefense/Stage Table")]
public class StageTable : ScriptableObject
{
    public StageData[] stages;

    public StageData GetStage(int stageNumber)
    {
        if (stages == null || stages.Length == 0) return null;
        int index = Mathf.Clamp(stageNumber - 1, 0, stages.Length - 1);
        return stages[index];
    }
}
