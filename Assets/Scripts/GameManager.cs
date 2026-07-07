using UnityEngine;

public class GameManager : MonoBehaviour
{
    [SerializeField] private float stageDuration = 45f;

    public static GameManager Instance { get; private set; }

    public bool IsGameOver { get; private set; }
    public int CurrentStage { get; private set; } = 1;

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

        stageTimer += Time.deltaTime;
        if (stageTimer >= stageDuration)
        {
            stageTimer -= stageDuration;
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
