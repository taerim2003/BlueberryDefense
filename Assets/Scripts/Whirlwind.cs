using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Whirlwind : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float damagingMoveSpeedMultiplier = 0.4f;
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private float tickInterval = 0.3f;
    // 레벨업으로 이 배율이 내려가면 더 자주 갈아버린다(회오리 성장의 보조축). 1 = 프리팹 기본 주기.
    public float TickIntervalMult { get; set; } = 1f;
    [SerializeField] private GameObject impactVfxPrefab;
    [SerializeField] private float groundY = 0f; // 공중(비행 적 처치 지점 등)에서 생성돼도 이 높이까지 자연스럽게 낙하
    [SerializeField] private float gravity = 25f;

    private float fallVelocity;

    // 스프라이트 피벗이 중앙이라 groundY는 "중심 높이"다. 크기가 작은 미니 회오리는 같은 groundY에서
    // 바닥선이 위로 뜨므로, 소환하는 쪽에서 큰 회오리 바닥선에 맞춘 값을 넣어준다.
    public float GroundY { set => groundY = value; }

    public float Damage { get; set; }
    public bool ApplyGemSlow { get; set; }
    public bool ApplyGemVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 틱마다 개별적으로 치명타를 굴린다
    public int MaxHitCount { get; set; } // 0이면 비활성화(기존처럼 lifetime 기준으로 소멸)
    public float SlowDuration { get; set; } = 3f;
    public float ExtraLifetime { get; set; } // 레벨업 고유 강화: 지속시간(초) 추가
    // 0보다 크면 프리팹의 lifetime 대신 이 값을 쓴다 — `Prog_Whirlwind.baseDuration`이 넘겨 준다.
    public float BaseLifetimeOverride { get; set; }
    public bool TargetHighestHealth { get; set; } // 최고 체력 적을 타겟팅 (지금은 켜는 진화가 없다 — 재활용 가능)
    public bool CanHitFlying { get; set; } = true; // 미니 회오리는 비행 적을 타격할 수 없다

    // 회오리 R0(화살 연계): 이 회오리가 사라질 때 그 자리(소멸 시점 위치)를 알려준다 — PlayerSkills가 미니를 남긴다.
    // ⚠️ 씬 언로드·게임 종료로 파괴될 때는 부르지 않는다. 안 그러면 판이 끝나는 순간 미니가 우수수 생긴다.
    public System.Action<Vector3> OnExpired;

    private static bool quitting;
    private void OnApplicationQuit() => quitting = true;

    private void OnDestroy()
    {
        if (quitting || !gameObject.scene.isLoaded) return;
        OnExpired?.Invoke(transform.position);
    }

    private int hitCount;
    private readonly HashSet<Enemy> overlappingEnemies = new HashSet<Enemy>();
    private readonly Dictionary<Enemy, float> nextTickTime = new Dictionary<Enemy, float>();

    private void Start()
    {
        float baseLife = BaseLifetimeOverride > 0f ? BaseLifetimeOverride : lifetime;
        if (MaxHitCount <= 0) Destroy(gameObject, (baseLife + ExtraLifetime) * MetaBonuses.DurationMult);
    }

    private void Update()
    {
        ApplyGravity();

        // 풀링된 적은 죽어도 null이 되지 않는다 — IsAlive로 걸러야 반납된 적을 계속 때리지 않는다.
        overlappingEnemies.RemoveWhere(e => e == null || !e.IsAlive);

        Vector2 direction = FindHomingDirection();
        float speed = overlappingEnemies.Count > 0 ? moveSpeed * damagingMoveSpeedMultiplier : moveSpeed;
        transform.Translate(direction * speed * Time.deltaTime, Space.World);

        foreach (Enemy enemy in new List<Enemy>(overlappingEnemies))
        {
            if (enemy == null || !enemy.IsAlive || (enemy.RequiresAntiAir && !CanHitFlying) || Time.time < nextTickTime.GetValueOrDefault(enemy, 0f)) continue;
            nextTickTime[enemy] = Time.time + tickInterval * TickIntervalMult;

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
                if (enemy.RequiresAntiAir && !CanHitFlying) continue; // 때릴 수 없는 적은 쫓아가지도 않는다
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
                if (enemy.RequiresAntiAir && !CanHitFlying) continue; // 때릴 수 없는 적은 쫓아가지도 않는다
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
