using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject treasureEnemyPrefab;
    [SerializeField] private GameObject eliteEnemyPrefab;
    [SerializeField] private GameObject paperPlaneEnemyPrefab;
    [SerializeField] private GameObject ufoEnemyPrefab;
    [SerializeField] private float spawnInterval = 1.5f;
    [SerializeField] private float treasureSpawnRatio = 0.85f; // 스테이지 진행률이 이 이상일 때만 보물상자 블루베리 등장
    [SerializeField] private float treasureGapAfter = 1.5f; // 보물상자 등장 직후 다음 스폰까지 추가 텀

    // 3스테이지마다 추가로 붙는 체력/이속 배율 — 처음엔 거의 안 느껴지다가 갈수록 증가폭이 커짐(후반일수록 스텝당 증가폭 자체가 커짐)
    private static readonly float[] HpStepBonus = { 0f, 0.04f, 0.10f, 0.18f, 0.28f, 0.40f, 0.55f };
    private static readonly float[] SpeedStepBonus = { 0f, 0.025f, 0.06f, 0.11f, 0.17f, 0.24f, 0.33f };

    // 지식 연계 path1: 블루베리 스폰 시 이 확률로 보물상자 블루베리로 대체
    public static float ExtraTreasureChance = 0f;

    private float timer;
    private int treasureSpawnedForStage = 0;

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.IsSpawningPaused) return; // 스테이지 전환/클리어 대기 중: 스폰 정지

        StageData stage = GameManager.Instance != null ? GameManager.Instance.CurrentStageData : null;
        float interval = stage != null ? stage.spawnInterval : spawnInterval;
        float eliteChance = stage != null ? stage.eliteChance : 0f;
        float paperPlaneChance = stage != null ? stage.paperPlaneChance : 0f;
        float ufoChance = stage != null ? stage.ufoChance : 0f;
        int currentStage = GameManager.Instance != null ? GameManager.Instance.CurrentStage : 1;
        float stageRatio = GameManager.Instance != null ? GameManager.Instance.StageElapsedRatio : 0f;
        bool paperPlaneOnlyStage = currentStage == 7;

        timer += Time.deltaTime;
        if (timer < interval) return;

        timer = 0f;

        GameObject prefabToSpawn = paperPlaneOnlyStage && paperPlaneEnemyPrefab != null ? paperPlaneEnemyPrefab : enemyPrefab;
        int spawnCount = 1;
        if (treasureEnemyPrefab != null && currentStage != treasureSpawnedForStage && stageRatio >= treasureSpawnRatio)
        {
            prefabToSpawn = treasureEnemyPrefab;
            treasureSpawnedForStage = currentStage;
            timer = -treasureGapAfter;
            if (currentStage >= 11) spawnCount = 2; // 11스테이지부터 스테이지 종료 보물상자 블루베리 2마리
        }
        else if (treasureEnemyPrefab != null && ExtraTreasureChance > 0f && Random.value < ExtraTreasureChance)
            prefabToSpawn = treasureEnemyPrefab;
        else if (!paperPlaneOnlyStage && eliteEnemyPrefab != null && Random.value < eliteChance)
            prefabToSpawn = eliteEnemyPrefab;
        else if (!paperPlaneOnlyStage && paperPlaneEnemyPrefab != null && currentStage >= 5 && Random.value < paperPlaneChance)
            prefabToSpawn = paperPlaneEnemyPrefab;
        else if (!paperPlaneOnlyStage && ufoEnemyPrefab != null && currentStage >= 11 && Random.value < ufoChance)
            prefabToSpawn = ufoEnemyPrefab;

        for (int i = 0; i < spawnCount; i++)
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
    }
}
