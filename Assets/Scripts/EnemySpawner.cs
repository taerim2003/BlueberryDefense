using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject treasureEnemyPrefab;
    [SerializeField] private float spawnInterval = 1.5f;
    [SerializeField] private float treasureChance = 0.15f;

    private float timer;

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer < spawnInterval) return;

        timer = 0f;

        bool spawnTreasure = treasureEnemyPrefab != null && Random.value < treasureChance;
        Instantiate(spawnTreasure ? treasureEnemyPrefab : enemyPrefab, transform.position, Quaternion.identity);
    }
}
