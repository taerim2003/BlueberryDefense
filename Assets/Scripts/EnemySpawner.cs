using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject treasureEnemyPrefab;
    [SerializeField] private GameObject eliteEnemyPrefab;
    [SerializeField] private GameObject paperPlaneEnemyPrefab;
    [SerializeField] private GameObject ufoEnemyPrefab;
    [SerializeField] private GameObject shieldEnemyPrefab;
    [SerializeField] private GameObject bossEnemyPrefab;   // 보스 블루베리(BTD 비행선). bossStage의 마지막 물량으로 1회 등장
    [SerializeField] private int bossStage = 15;
    [SerializeField] private float spawnInterval = 1.5f;
    [SerializeField] private int defaultSpawnCount = 20; // StageData 없을 때 폴백 물량

    // 3스테이지마다 추가로 붙는 체력/이속 배율 — 처음엔 거의 안 느껴지다가 갈수록 증가폭이 커짐(후반일수록 스텝당 증가폭 자체가 커짐)
    private static readonly float[] HpStepBonus = { 0f, 0.06f, 0.16f, 0.30f, 0.48f, 0.72f, 1.0f };
    private static readonly float[] SpeedStepBonus = { 0f, 0.025f, 0.06f, 0.11f, 0.17f, 0.24f, 0.33f };

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

    private void Update()
    {
        GameManager gm = GameManager.Instance;
        StageData stage = gm != null ? gm.CurrentStageData : null;
        int currentStage = gm != null ? gm.CurrentStage : 1;

        // 스테이지가 바뀌면(전환 텀 진입 시점 포함) 이 스테이지의 물량 카운트를 리셋 — 정지 체크보다 먼저 돌아야 함
        if (currentStage != stageBeingCounted)
        {
            stageBeingCounted = currentStage;
            SpawnedThisStage = 0;
            SpawnTarget = stage != null ? stage.spawnCount : defaultSpawnCount;
            treasureSpawnedForStage = 0;
            bossSpawnedThisStage = false;
            timer = 0f;
            treasureDelayTimer = 0f;
        }

        if (gm != null && gm.IsSpawningPaused) return; // 스테이지 전환 텀: 스폰 정지
        if (StageSpawnComplete) return;                // 이 스테이지 물량 다 스폰함 — 잔몹 처리는 GameManager가 대기

        int treasureCount = currentStage >= 11 ? 2 : 1; // 11스테이지부터 스테이지 종료 보물상자 블루베리 2마리
        bool isBossStage = currentStage == bossStage && bossEnemyPrefab != null;
        bool treasureStage = !isBossStage && treasureEnemyPrefab != null;

        // 스테이지 종료 보물상자: 일반 몹이 전부 나온 뒤(SpawnTarget-treasureCount 도달) 5초 텀을 두고 등장.
        // 그동안 스폰은 멈춰 있어 플레이어가 잔몹을 정리하고 보물상자를 확실히 먹을 수 있다.
        if (treasureStage && treasureSpawnedForStage != currentStage && SpawnedThisStage >= SpawnTarget - treasureCount)
        {
            treasureDelayTimer += Time.deltaTime;
            if (treasureDelayTimer < TreasureDelay) return;
            SpawnEnemies(treasureEnemyPrefab, treasureCount, stage, currentStage);
            treasureSpawnedForStage = currentStage;
            return;
        }

        float interval = stage != null ? stage.spawnInterval : spawnInterval;
        float eliteChance = stage != null ? stage.eliteChance : 0f;
        float paperPlaneChance = stage != null ? stage.paperPlaneChance : 0f;
        float ufoChance = stage != null ? stage.ufoChance : 0f;
        float shieldChance = stage != null ? stage.shieldChance : 0f;
        bool paperPlaneOnlyStage = currentStage == 7;

        timer += Time.deltaTime;
        if (timer < interval) return;

        timer = 0f;

        GameObject prefabToSpawn = paperPlaneOnlyStage && paperPlaneEnemyPrefab != null ? paperPlaneEnemyPrefab : enemyPrefab;
        // 보스 블루베리: 보스 스테이지의 마지막 물량으로 1회 등장(그 뒤 잔몹 + 분출 블루베리까지 잡아야 클리어)
        if (isBossStage && !bossSpawnedThisStage && SpawnedThisStage >= SpawnTarget - 1)
        {
            prefabToSpawn = bossEnemyPrefab;
            bossSpawnedThisStage = true;
        }
        else if (treasureEnemyPrefab != null && ExtraTreasureChance > 0f && Random.value < ExtraTreasureChance)
            prefabToSpawn = treasureEnemyPrefab;
        else if (!paperPlaneOnlyStage && eliteEnemyPrefab != null && Random.value < eliteChance)
            prefabToSpawn = eliteEnemyPrefab;
        else if (!paperPlaneOnlyStage && paperPlaneEnemyPrefab != null && currentStage >= 5 && Random.value < paperPlaneChance)
            prefabToSpawn = paperPlaneEnemyPrefab;
        else if (!paperPlaneOnlyStage && ufoEnemyPrefab != null && currentStage >= 11 && Random.value < ufoChance)
            prefabToSpawn = ufoEnemyPrefab;
        else if (!paperPlaneOnlyStage && shieldEnemyPrefab != null && Random.value < shieldChance)
            prefabToSpawn = shieldEnemyPrefab;

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
                    int step = Mathf.Clamp(currentStage / 3, 0, HpStepBonus.Length - 1);
                    float hpMult = stage.enemyHpMultiplier * (1f + HpStepBonus[step]);
                    float speedMult = stage.enemySpeedMultiplier * (1f + SpeedStepBonus[step]);
                    enemy.ApplyStageMultipliers(hpMult, speedMult, stage.enemyDamageMultiplier);
                }
            }
        }

        SpawnedThisStage += count;
    }
}
