using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject treasureEnemyPrefab;
    [SerializeField] private GameObject eliteEnemyPrefab;
    [SerializeField] private GameObject paperPlaneEnemyPrefab;
    [SerializeField] private float spawnInterval = 1.5f;
    [SerializeField] private float treasureSpawnRatio = 0.85f; // 스테이지 진행률이 이 이상일 때만 보물상자 블루베리 등장
    [SerializeField] private float treasureGapAfter = 1.5f; // 보물상자 등장 직후 다음 스폰까지 추가 텀

    // 5스테이지마다 추가로 붙는 체력/이속 배율 — 처음엔 거의 안 느껴지다가 갈수록 증가폭이 커짐
    private static readonly float[] HpStepBonus = { 0f, 0.03f, 0.08f, 0.15f, 0.25f };
    private static readonly float[] SpeedStepBonus = { 0f, 0.02f, 0.05f, 0.09f, 0.15f };

    private float timer;
    private int treasureSpawnedForStage = 0;

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.IsSpawningPaused) return; // 스테이지 전환/클리어 대기 중: 스폰 정지

        StageData stage = GameManager.Instance != null ? GameManager.Instance.CurrentStageData : null;
        float interval = stage != null ? stage.spawnInterval : spawnInterval;
        float eliteChance = stage != null ? stage.eliteChance : 0f;
        float paperPlaneChance = stage != null ? stage.paperPlaneChance : 0f;
        int currentStage = GameManager.Instance != null ? GameManager.Instance.CurrentStage : 1;
        float stageRatio = GameManager.Instance != null ? GameManager.Instance.StageElapsedRatio : 0f;
        bool paperPlaneOnlyStage = currentStage == 7;

        timer += Time.deltaTime;
        if (timer < interval) return;

        timer = 0f;

        GameObject prefabToSpawn = paperPlaneOnlyStage && paperPlaneEnemyPrefab != null ? paperPlaneEnemyPrefab : enemyPrefab;
        if (treasureEnemyPrefab != null && currentStage != treasureSpawnedForStage && stageRatio >= treasureSpawnRatio)
        {
            prefabToSpawn = treasureEnemyPrefab;
            treasureSpawnedForStage = currentStage;
            timer = -treasureGapAfter;
        }
        else if (!paperPlaneOnlyStage && eliteEnemyPrefab != null && Random.value < eliteChance)
            prefabToSpawn = eliteEnemyPrefab;
        else if (!paperPlaneOnlyStage && paperPlaneEnemyPrefab != null && currentStage >= 5 && Random.value < paperPlaneChance)
            prefabToSpawn = paperPlaneEnemyPrefab;

        GameObject obj = Instantiate(prefabToSpawn, transform.position, Quaternion.identity);

        if (stage != null)
        {
            Enemy enemy = obj.GetComponent<Enemy>();
            if (enemy != null)
            {
                int step = Mathf.Clamp(currentStage / 5, 0, HpStepBonus.Length - 1);
                float hpMult = stage.enemyHpMultiplier * (1f + HpStepBonus[step]);
                float speedMult = stage.enemySpeedMultiplier * (1f + SpeedStepBonus[step]);
                enemy.ApplyStageMultipliers(hpMult, speedMult, stage.enemyDamageMultiplier);
            }
        }
    }
}
