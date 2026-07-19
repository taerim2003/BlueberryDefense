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
    [SerializeField] private float treasureSpawnRatio = 0.85f; // 스테이지 진행률이 이 이상일 때만 보물상자 블루베리 등장
    [SerializeField] private float treasureGapAfter = 1.5f; // 보물상자 등장 직후 다음 스폰까지 추가 텀

    // 3스테이지마다 추가로 붙는 체력/이속 배율 — 처음엔 거의 안 느껴지다가 갈수록 증가폭이 커짐(후반일수록 스텝당 증가폭 자체가 커짐)
    private static readonly float[] HpStepBonus = { 0f, 0.04f, 0.10f, 0.18f, 0.28f, 0.40f, 0.55f };
    private static readonly float[] SpeedStepBonus = { 0f, 0.025f, 0.06f, 0.11f, 0.17f, 0.24f, 0.33f };

    // 지식 연계 path1: 블루베리 스폰 시 이 확률로 보물상자 블루베리로 대체
    public static float ExtraTreasureChance = 0f;

    private float timer;
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
        }

        if (gm != null && gm.IsSpawningPaused) return; // 스테이지 전환 텀: 스폰 정지
        if (StageSpawnComplete) return;                // 이 스테이지 물량 다 스폰함 — 잔몹 처리는 GameManager가 대기

        float interval = stage != null ? stage.spawnInterval : spawnInterval;
        float eliteChance = stage != null ? stage.eliteChance : 0f;
        float paperPlaneChance = stage != null ? stage.paperPlaneChance : 0f;
        float ufoChance = stage != null ? stage.ufoChance : 0f;
        float shieldChance = stage != null ? stage.shieldChance : 0f;
        float stageRatio = SpawnRatio;
        bool paperPlaneOnlyStage = currentStage == 7;

        timer += Time.deltaTime;
        if (timer < interval) return;

        timer = 0f;

        GameObject prefabToSpawn = paperPlaneOnlyStage && paperPlaneEnemyPrefab != null ? paperPlaneEnemyPrefab : enemyPrefab;
        int batchCount = 1;
        // 보스 블루베리: 보스 스테이지의 마지막 물량으로 1회 등장(그 뒤 잔몹 + 분출 블루베리까지 잡아야 클리어)
        if (currentStage == bossStage && bossEnemyPrefab != null && !bossSpawnedThisStage && SpawnedThisStage >= SpawnTarget - 1)
        {
            prefabToSpawn = bossEnemyPrefab;
            bossSpawnedThisStage = true;
        }
        else if (treasureEnemyPrefab != null && currentStage != treasureSpawnedForStage && stageRatio >= treasureSpawnRatio)
        {
            prefabToSpawn = treasureEnemyPrefab;
            treasureSpawnedForStage = currentStage;
            timer = -treasureGapAfter;
            if (currentStage >= 11) batchCount = 2; // 11스테이지부터 스테이지 종료 보물상자 블루베리 2마리
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

        for (int i = 0; i < batchCount; i++)
        {
            // 보물상자 2마리 스폰 시 겹치지 않게 뒤쪽(왼쪽)으로 살짝 벌려 단일 대열 유지
            Vector3 spawnPos = transform.position + Vector3.left * (1.2f * i);
            GameObject obj = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);

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

        SpawnedThisStage += batchCount;
    }
}
