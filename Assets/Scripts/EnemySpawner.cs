using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    // 적 로스터·스폰 파라미터는 MapDefinition이 소유한다. RunBootstrap이 판 시작 시 ActiveMap을 세팅(Start 전).
    // fallbackMap = 씬 단독 실행 시(RunBootstrap 없거나 RunConfig.Map null) 사용할 기본 맵.
    [SerializeField] private MapDefinition fallbackMap;
    [SerializeField] private ScalingTable scaling;       // 후반 체력/이속 스텝 배율(전역). 미할당 시 기본값 폴백

    public MapDefinition ActiveMap { get; set; }
    private MapDefinition Map => ActiveMap != null ? ActiveMap : fallbackMap;

    // 3스테이지마다 추가로 붙는 체력/이속 배율 — 처음엔 거의 안 느껴지다가 갈수록 증가폭이 커짐(후반일수록 스텝당 증가폭 자체가 커짐)
    private ScalingTable Scaling => scaling != null ? scaling : ScalingTable.Default;

    // 지식 연계 path1: 블루베리 스폰 시 이 확률로 보물상자 블루베리로 대체
    public static float ExtraTreasureChance = 0f;

    private const float TreasureDelay = 5f; // 일반 몹이 전부 나온 뒤 이 시간만큼 텀을 두고 보물상자 등장(잔몹 정리 시간)

    private float timer;
    private float treasureDelayTimer;
    private int treasureSpawnedForStage = 0;
    private int stageBeingCounted = -1;
    private bool bossSpawnedThisStage;

    // 물량 기반 스폰 진행 상태 — GameManager가 클리어 판정에, HUD가 진행바에 참조
    public int SpawnedThisStage { get; private set; }
    public int SpawnTarget { get; private set; }
    public bool StageSpawnComplete => SpawnedThisStage >= SpawnTarget;
    public float SpawnRatio => SpawnTarget > 0 ? Mathf.Clamp01(SpawnedThisStage / (float)SpawnTarget) : 1f;

    // 첫 프레임에 GameManager.Update가 EnemySpawner.Update보다 먼저 돌면 SpawnTarget이 0이라
    // StageSpawnComplete가 참이 되어 1스테이지가 즉시 넘어가버린다(첫 판이 2스테이지에서 시작).
    // Start(모든 Awake 이후·첫 Update 이전)에서 현재 스테이지 물량을 미리 셋업해 방지.
    private void Start()
    {
        BeginStageCount(GameManager.Instance);
    }

    private void BeginStageCount(GameManager gm)
    {
        StageData stage = gm != null ? gm.CurrentStageData : null;
        stageBeingCounted = gm != null ? gm.CurrentStage : 1;
        SpawnedThisStage = 0;
        SpawnTarget = stage != null ? stage.spawnCount : Map.defaultSpawnCount;
        treasureSpawnedForStage = 0;
        bossSpawnedThisStage = false;
        timer = 0f;
        treasureDelayTimer = 0f;
    }

    private void Update()
    {
        GameManager gm = GameManager.Instance;
        StageData stage = gm != null ? gm.CurrentStageData : null;
        int currentStage = gm != null ? gm.CurrentStage : 1;
        MapDefinition map = Map;

        // 스테이지가 바뀌면(전환 텀 진입 시점 포함) 이 스테이지의 물량 카운트를 리셋 — 정지 체크보다 먼저 돌아야 함
        if (currentStage != stageBeingCounted)
        {
            BeginStageCount(gm);
        }

        if (gm != null && gm.IsSpawningPaused) return; // 스테이지 전환 텀: 스폰 정지
        if (StageSpawnComplete) return;                // 이 스테이지 물량 다 스폰함 — 잔몹 처리는 GameManager가 대기

        int treasureCount = currentStage >= 11 ? 2 : 1; // 11스테이지부터 스테이지 종료 보물상자 블루베리 2마리
        bool isBossStage = currentStage == map.bossStage && map.bossEnemyPrefab != null;
        bool treasureStage = !isBossStage && map.treasureEnemyPrefab != null;

        // 스테이지 종료 보물상자: 일반 몹이 전부 나온 뒤(SpawnTarget-treasureCount 도달) 5초 텀을 두고 등장.
        // 그동안 스폰은 멈춰 있어 플레이어가 잔몹을 정리하고 보물상자를 확실히 먹을 수 있다.
        if (treasureStage && treasureSpawnedForStage != currentStage && SpawnedThisStage >= SpawnTarget - treasureCount)
        {
            treasureDelayTimer += Time.deltaTime;
            if (treasureDelayTimer < TreasureDelay) return;
            SpawnEnemies(map.treasureEnemyPrefab, treasureCount, stage, currentStage);
            treasureSpawnedForStage = currentStage;
            return;
        }

        float interval = stage != null ? stage.spawnInterval : map.spawnInterval;
        float eliteChance = stage != null ? stage.eliteChance : 0f;
        float paperPlaneChance = stage != null ? stage.paperPlaneChance : 0f;
        float ufoChance = stage != null ? stage.ufoChance : 0f;
        float shieldChance = stage != null ? stage.shieldChance : 0f;

        timer += Time.deltaTime;
        if (timer < interval) return;

        timer = 0f;

        GameObject prefabToSpawn = map.enemyPrefab;
        // 보스 블루베리: 보스 스테이지의 마지막 물량으로 1회 등장(그 뒤 잔몹 + 분출 블루베리까지 잡아야 클리어)
        if (isBossStage && !bossSpawnedThisStage && SpawnedThisStage >= SpawnTarget - 1)
        {
            prefabToSpawn = map.bossEnemyPrefab;
            bossSpawnedThisStage = true;
        }
        else if (map.treasureEnemyPrefab != null && ExtraTreasureChance > 0f && Random.value < ExtraTreasureChance)
            prefabToSpawn = map.treasureEnemyPrefab;
        else if (map.eliteEnemyPrefab != null && Random.value < eliteChance)
            prefabToSpawn = map.eliteEnemyPrefab;
        else if (map.paperPlaneEnemyPrefab != null && Random.value < paperPlaneChance)
            prefabToSpawn = map.paperPlaneEnemyPrefab;
        else if (map.ufoEnemyPrefab != null && currentStage >= 11 && Random.value < ufoChance)
            prefabToSpawn = map.ufoEnemyPrefab;
        else if (map.shieldEnemyPrefab != null && Random.value < shieldChance)
            prefabToSpawn = map.shieldEnemyPrefab;

        SpawnEnemies(prefabToSpawn, 1, stage, currentStage);
    }

    private void SpawnEnemies(GameObject prefab, int count, StageData stage, int currentStage)
    {
        for (int i = 0; i < count; i++)
        {
            // 2마리 이상 동시 스폰 시 겹치지 않게 뒤쪽(왼쪽)으로 살짝 벌려 단일 대열 유지
            Vector3 spawnPos = transform.position + Vector3.left * (1.2f * i);
            GameObject obj = Instantiate(prefab, spawnPos, Quaternion.identity);

            if (stage != null)
            {
                Enemy enemy = obj.GetComponent<Enemy>();
                if (enemy != null)
                {
                    int step = currentStage / 3;
                    float hpMult = stage.enemyHpMultiplier * (1f + Scaling.HpStepBonusAt(step));
                    float speedMult = stage.enemySpeedMultiplier * (1f + Scaling.SpeedStepBonusAt(step));
                    enemy.ApplyStageMultipliers(hpMult, speedMult, stage.enemyDamageMultiplier);
                }
            }
        }

        SpawnedThisStage += count;
    }
}
