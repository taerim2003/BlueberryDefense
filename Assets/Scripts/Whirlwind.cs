using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Whirlwind : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float damagingMoveSpeedMultiplier = 0.4f;
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private float tickInterval = 0.3f;
    [SerializeField] private GameObject impactVfxPrefab;

    public float Damage { get; set; }
    public bool ApplyGemSlow { get; set; }
    public bool ApplyGemVulnerable { get; set; }
    public bool IsCrit { get; set; }
    public int MaxHitCount { get; set; } // 0이면 비활성화(기존처럼 lifetime 기준으로 소멸)
    public float SlowDuration { get; set; } = 3f;

    private int hitCount;
    private readonly HashSet<Enemy> overlappingEnemies = new HashSet<Enemy>();
    private readonly Dictionary<Enemy, float> nextTickTime = new Dictionary<Enemy, float>();

    private void Start()
    {
        if (MaxHitCount <= 0) Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        overlappingEnemies.RemoveWhere(e => e == null);

        Vector2 direction = FindHomingDirection();
        float speed = overlappingEnemies.Count > 0 ? moveSpeed * damagingMoveSpeedMultiplier : moveSpeed;
        transform.Translate(direction * speed * Time.deltaTime, Space.World);

        foreach (Enemy enemy in new List<Enemy>(overlappingEnemies))
        {
            if (enemy == null || Time.time < nextTickTime.GetValueOrDefault(enemy, 0f)) continue;
            nextTickTime[enemy] = Time.time + tickInterval;

            enemy.TakeDamage(Damage, isCrit: IsCrit);
            if (ApplyGemSlow) enemy.ApplySlow(0.3f, SlowDuration);
            if (ApplyGemVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

            if (impactVfxPrefab != null)
                ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(impactVfxPrefab, enemy.transform.position, Quaternion.identity), 2f);

            if (MaxHitCount > 0 && ++hitCount >= MaxHitCount)
            {
                Destroy(gameObject);
                return;
            }
        }
    }

    private Vector2 FindHomingDirection()
    {
        Enemy[] enemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
        Enemy nearest = null;
        float nearestSqrDist = float.MaxValue;
        foreach (Enemy enemy in enemies)
        {
            float sqrDist = ((Vector2)enemy.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = enemy;
            }
        }

        if (nearest == null) return Vector2.left;
        return nearest.transform.position.x >= transform.position.x ? Vector2.right : Vector2.left;
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
