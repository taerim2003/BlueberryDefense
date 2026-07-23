using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;

public class GameManager : MonoBehaviour
{
    private const int FinalStage = 15; // 이 스테이지를 클리어하면 게임 클리어(15라운드 보스전)

    [SerializeField] private float stageBreakDuration = 4.5f; // 스테이지 전환 시 적 스폰이 멈추는 텀
    [SerializeField] private StageTable stageTable;
    [SerializeField] private GameObject heartPickupPrefab;
    [SerializeField] private GameObject essencePickupPrefab;

    public static GameManager Instance { get; private set; }

    public bool IsGameOver { get; private set; }
    public bool IsGameClear { get; private set; }
    public int CurrentStage { get; private set; } = 1;
    public bool IsStageBreak => stageBreakTimer > 0f;
    public bool IsSpawningPaused => stageBreakTimer > 0f;
    // 물량 기반: 스테이지 진행률 = 스폰한 수 / 총 물량 (스포너가 소유). HUD 진행바 등이 참조.
    public float StageElapsedRatio => spawner != null ? spawner.SpawnRatio : 0f;

    public StageData CurrentStageData => stageTable != null ? stageTable.GetStage(CurrentStage) : null;

    // RunBootstrap이 판 시작 시 선택된 맵의 StageTable을 주입(기본맵이면 기존 값과 동일).
    public void SetStageTable(StageTable table) => stageTable = table;

    private float stageBreakTimer;
    private EnemySpawner spawner;

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

        RunState.ResetAll(); // 지난 판의 static 효과(진화 보정·낙뢰 상태 등)가 넘어오지 않도록 전부 초기화
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

        // 물량 기반 클리어: 이 스테이지의 스폰 쿼터를 다 소진하고 + 남은 적이 0이 되면 클리어
        if (spawner == null) spawner = FindAnyObjectByType<EnemySpawner>();
        if (spawner != null && !spawner.StageSpawnComplete) return;      // 아직 스폰 중
        if (FindObjectsByType<Enemy>(FindObjectsSortMode.None).Length > 0) return; // 잔몹 처리 대기

        AdvanceStage();
    }

    // 현재 스테이지 클리어 처리 → 다음 스테이지(또는 게임 클리어)
    private void AdvanceStage()
    {
        // 스킬트리 자원은 정수 하나로 통일 — 정수는 인게임 적 처치로만 획득(스테이지 클리어 별도 지급 없음).
        // 승천(난이도 등급) 해금은 별도 시스템에서 처리 예정.
        if (CurrentStage >= FinalStage)
        {
            GameClear();
            return;
        }

        CurrentStage++;
        stageBreakTimer = stageBreakDuration;
        FindAnyObjectByType<PlayerSkills>()?.ResetAllCooldowns();
    }

    public void SkipToNextStage()
    {
        if (CurrentStage >= FinalStage)
        {
            GameClear();
            return;
        }
        CurrentStage++;
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
        AscensionSave.UnlockUpTo(RunConfig.AscensionLevel + 1); // 이 등급 클리어 → 다음 승천 해금(StS식 루프)
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
