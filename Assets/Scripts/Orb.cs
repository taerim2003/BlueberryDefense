using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Orb : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float slowMultiplier = 0.5f;
    [SerializeField] private float slowDuration = 2f;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private float tickInterval = 0.3f;
    [SerializeField] private GameObject impactVfxPrefab;

    public float Damage { get; set; }
    public bool ApplyGemVulnerable { get; set; }

    private readonly HashSet<Enemy> overlappingEnemies = new HashSet<Enemy>();
    private readonly Dictionary<Enemy, float> nextTickTime = new Dictionary<Enemy, float>();

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        transform.Translate(Vector2.left * moveSpeed * Time.deltaTime);

        overlappingEnemies.RemoveWhere(e => e == null);
        foreach (Enemy enemy in new List<Enemy>(overlappingEnemies))
        {
            if (enemy == null || Time.time < nextTickTime.GetValueOrDefault(enemy, 0f)) continue;
            nextTickTime[enemy] = Time.time + tickInterval;

            enemy.TakeDamage(Damage);
            enemy.ApplySlow(slowMultiplier, slowDuration);
            if (ApplyGemVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

            if (impactVfxPrefab != null)
                ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(impactVfxPrefab, enemy.transform.position, Quaternion.identity), 2f);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null) overlappingEnemies.Add(enemy);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null)
        {
            overlappingEnemies.Remove(enemy);
            nextTickTime.Remove(enemy);
        }
    }
}
