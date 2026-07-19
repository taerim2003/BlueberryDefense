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
    [SerializeField] private float groundY = 0f; // 공중(비행 적 처치 지점 등)에서 생성돼도 이 높이까지 자연스럽게 낙하
    [SerializeField] private float gravity = 25f;

    private float fallVelocity;

    public float Damage { get; set; }
    public bool ApplyGemSlow { get; set; }
    public bool ApplyGemVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 틱마다 개별적으로 치명타를 굴린다
    public int MaxHitCount { get; set; } // 0이면 비활성화(기존처럼 lifetime 기준으로 소멸)
    public float SlowDuration { get; set; } = 3f;
    public float ExtraLifetime { get; set; } // 레벨업 고유 강화: 지속시간(초) 추가
    public bool TargetHighestHealth { get; set; } // 암살 연계 (패시브 path2): 최고 체력 적을 타겟팅
    public bool CanHitFlying { get; set; } = true; // 미니 회오리는 비행 적을 타격할 수 없다

    private int hitCount;
    private readonly HashSet<Enemy> overlappingEnemies = new HashSet<Enemy>();
    private readonly Dictionary<Enemy, float> nextTickTime = new Dictionary<Enemy, float>();

    private void Start()
    {
        if (MaxHitCount <= 0) Destroy(gameObject, (lifetime + ExtraLifetime) * MetaBonuses.DurationMult);
    }

    private void Update()
    {
        ApplyGravity();

        overlappingEnemies.RemoveWhere(e => e == null);

        Vector2 direction = FindHomingDirection();
        float speed = overlappingEnemies.Count > 0 ? moveSpeed * damagingMoveSpeedMultiplier : moveSpeed;
        transform.Translate(direction * speed * Time.deltaTime, Space.World);

        foreach (Enemy enemy in new List<Enemy>(overlappingEnemies))
        {
            if (enemy == null || (enemy.IsFlying && !CanHitFlying) || Time.time < nextTickTime.GetValueOrDefault(enemy, 0f)) continue;
            nextTickTime[enemy] = Time.time + tickInterval;

            enemy.TakeSkillHit(Damage, CritChance, ActiveSkillId.Whirlwind);
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

    private void ApplyGravity()
    {
        if (transform.position.y <= groundY)
        {
            fallVelocity = 0f;
            return;
        }

        fallVelocity += gravity * Time.deltaTime;
        float newY = Mathf.Max(groundY, transform.position.y - fallVelocity * Time.deltaTime);
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    private Vector2 FindHomingDirection()
    {
        Enemy[] enemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
        Enemy target = null;

        if (TargetHighestHealth)
        {
            float highestHealth = float.NegativeInfinity;
            foreach (Enemy enemy in enemies)
            {
                if (enemy.CurrentHealth > highestHealth)
                {
                    highestHealth = enemy.CurrentHealth;
                    target = enemy;
                }
            }
        }
        else
        {
            float nearestSqrDist = float.MaxValue;
            foreach (Enemy enemy in enemies)
            {
                float sqrDist = ((Vector2)enemy.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (sqrDist < nearestSqrDist)
                {
                    nearestSqrDist = sqrDist;
                    target = enemy;
                }
            }
        }

        if (target == null) return Vector2.left;
        return target.transform.position.x >= transform.position.x ? Vector2.right : Vector2.left;
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
