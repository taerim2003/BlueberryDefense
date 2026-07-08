using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject treasureEnemyPrefab;
    [SerializeField] private GameObject eliteEnemyPrefab;
    [SerializeField] private GameObject paperPlaneEnemyPrefab;
    [SerializeField] private float spawnInterval = 1.5f;

    private float timer;
    private int treasureSpawnedForStage = 0;

    private void Update()
    {
        StageData stage = GameManager.Instance != null ? GameManager.Instance.CurrentStageData : null;
        float interval = stage != null ? stage.spawnInterval : spawnInterval;
        float eliteChance = stage != null ? stage.eliteChance : 0f;
        float paperPlaneChance = stage != null ? stage.paperPlaneChance : 0f;
        int currentStage = GameManager.Instance != null ? GameManager.Instance.CurrentStage : 1;

        timer += Time.deltaTime;
        if (timer < interval) return;

        timer = 0f;

        GameObject prefabToSpawn = enemyPrefab;
        if (treasureEnemyPrefab != null && currentStage != treasureSpawnedForStage)
        {
            prefabToSpawn = treasureEnemyPrefab;
            treasureSpawnedForStage = currentStage;
        }
        else if (eliteEnemyPrefab != null && Random.value < eliteChance)
            prefabToSpawn = eliteEnemyPrefab;
        else if (paperPlaneEnemyPrefab != null && Random.value < paperPlaneChance)
            prefabToSpawn = paperPlaneEnemyPrefab;

        GameObject obj = Instantiate(prefabToSpawn, transform.position, Quaternion.identity);

        if (stage != null)
        {
            Enemy enemy = obj.GetComponent<Enemy>();
            if (enemy != null) enemy.ApplyStageMultipliers(stage.enemyHpMultiplier, stage.enemySpeedMultiplier, stage.enemyDamageMultiplier);
        }
    }
}
