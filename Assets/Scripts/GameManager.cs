using UnityEngine;

public class GameManager : MonoBehaviour
{
    [SerializeField] private float stageDuration = 45f;
    [SerializeField] private StageTable stageTable;

    public static GameManager Instance { get; private set; }

    public bool IsGameOver { get; private set; }
    public int CurrentStage { get; private set; } = 1;

    public StageData CurrentStageData => stageTable != null ? stageTable.GetStage(CurrentStage) : null;

    private float stageTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        if (IsGameOver) return;

        float duration = CurrentStageData != null ? CurrentStageData.duration : stageDuration;
        stageTimer += Time.deltaTime;
        if (stageTimer >= duration)
        {
            stageTimer -= duration;
            CurrentStage++;
        }
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
