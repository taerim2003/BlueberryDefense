using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Orb : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float slowMultiplier = 0.65f;
    [SerializeField] private float slowDuration = 2f;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private float tickInterval = 0.3f;
    [SerializeField] private GameObject impactVfxPrefab;

    public float Damage { get; set; }
    public bool ApplyGemVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 틱마다 개별적으로 치명타를 굴린다
    public float FlyingDamageMultiplier { get; set; } = 1f;
    public float SlowMultiplierBonus { get; set; } // 뺄셈 (0~slowMultiplier)
    public float SlowDurationBonus { get; set; } // 덧셈(초)
    // 이 오브가 **평생** 붙잡을 수 있는 적 수(레벨업 주 성장축). FireOrb가 세팅.
    // 매 틱 리셋되는 동시 타격 한도가 아니라 소모성 예산이다 — 다 쓰고 붙잡은 적이 전부 정리되면 오브가 사라진다.
    public int MaxTargets { get; set; } = 4;

    private const float ImpactSfxCooldown = 0.9f; // Whirlwind와 동일한 이유: 임팩트 클립 길이가 틱 간격(0.3초)보다 길어서 매 틱 재생하면 겹쳐 쌓인다.

    private float nextImpactSfxTime;
    private bool consumed; // 방패에 막혀 소멸 확정 — 같은 프레임에 다른 방패와도 겹쳐 있으면 중복 타격되는 것을 막는다
    private int budgetRemaining = -1;   // 아직 붙잡을 수 있는 적 수. -1 = 미초기화(Start에서 MaxTargets로 채움)
    private readonly HashSet<Enemy> overlappingEnemies = new HashSet<Enemy>();
    private readonly HashSet<Enemy> claimed = new HashSet<Enemy>(); // 예산을 이미 소모한 적 — 얘들은 계속 무료로 간다
    private readonly Dictionary<Enemy, float> nextTickTime = new Dictionary<Enemy, float>();

    private void Start()
    {
        budgetRemaining = Mathf.Max(1, MaxTargets);
        // 수명은 이제 주 소멸 조건이 아니라 **안전망**이다 — 아무도 못 만난 오브가 영원히 날아가지 않게.
        Destroy(gameObject, lifetime * MetaBonuses.DurationMult);
    }

    private void Update()
    {
        transform.Translate(Vector2.left * moveSpeed * Time.deltaTime);

        // 풀링된 적은 죽어도 null이 되지 않는다 — IsAlive로 걸러야 반납된 적을 계속 붙잡고 있지 않는다.
        overlappingEnemies.RemoveWhere(e => e == null || !e.IsAlive);
        bool canHitFlying = FlyingDamageMultiplier > 1f || MetaBonuses.OrbCanHitFlying; // 기본 오브는 비행형 타격 불가, 공중 적 추가 피해 진화(path0) 또는 스킬트리로 해금

        // 겹쳐 있는 적을 전부 갈아버리지 않고 **가까운 순으로** 예산이 닿는 만큼만 붙잡는다.
        // 예산은 "처음 만난 적"에만 소모되고, 한 번 붙잡은 적은 죽을 때까지 계속 간다.
        // 레벨업으로 이 예산이 올라가는 게 오브의 주 성장축(4마리 → 7마리).
        List<Enemy> ordered = new List<Enemy>(overlappingEnemies);
        ordered.Sort((a, b) =>
        {
            if (a == null || b == null) return 0;
            float da = ((Vector2)a.transform.position - (Vector2)transform.position).sqrMagnitude;
            float db = ((Vector2)b.transform.position - (Vector2)transform.position).sqrMagnitude;
            return da.CompareTo(db);
        });

        foreach (Enemy enemy in ordered)
        {
            if (enemy == null || !enemy.IsAlive || Time.time < nextTickTime.GetValueOrDefault(enemy, 0f)) continue;
            if (enemy.RequiresAntiAir && !canHitFlying) continue; // 대공 전용 적(UFO)만 차단 — 종이비행기는 히트박스로만 판정

            if (!claimed.Contains(enemy))
            {
                if (budgetRemaining <= 0) continue; // 예산 소진 — 새 적은 더 못 잡는다(이미 잡은 적은 아래로 계속 진행)
                claimed.Add(enemy);
                budgetRemaining--;
            }

            nextTickTime[enemy] = Time.time + tickInterval;

            float baseDamage = enemy.IsFlying ? Damage * FlyingDamageMultiplier : Damage;
            enemy.TakeSkillHit(baseDamage, CritChance, ActiveSkillId.Orb);
            enemy.ApplySlow(Mathf.Clamp01(slowMultiplier - SlowMultiplierBonus), slowDuration + SlowDurationBonus);
            if (ApplyGemVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

            if (impactVfxPrefab != null)
            {
                GameObject vfx = ObjectPool.Instance.Spawn(impactVfxPrefab, enemy.transform.position, Quaternion.identity);
                if (Time.time < nextImpactSfxTime)
                {
                    foreach (AudioSource src in vfx.GetComponentsInChildren<AudioSource>()) src.Stop();
                }
                else
                {
                    nextImpactSfxTime = Time.time + ImpactSfxCooldown;
                }
                ObjectPool.Instance.Despawn(vfx, 2.2f);
            }
        }

        // 예산을 다 쓰고 붙잡고 있던 적이 전부 정리되면(죽었거나 오브가 지나쳤거나) 임무 완료 — 그 자리에서 사라진다.
        // 여기서 IsAlive를 빠뜨리면 붙잡은 적이 죽어도 claimed가 안 비어서 **오브가 영영 안 사라진다**.
        claimed.RemoveWhere(e => e == null || !e.IsAlive);
        if (budgetRemaining <= 0 && claimed.Count == 0)
            Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed) return;

        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null) return;

        // 방패 블루베리: 오브도 통과하지 못하고 여기서 소멸 — 마지막으로 한 번 타격을 주고 사라진다
        if (enemy.BlocksProjectiles)
        {
            consumed = true;
            enemy.TakeSkillHit(Damage, CritChance, ActiveSkillId.Orb);
            if (impactVfxPrefab != null)
                ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(impactVfxPrefab, enemy.transform.position, Quaternion.identity), 2.2f);
            Destroy(gameObject);
            return;
        }

        overlappingEnemies.Add(enemy);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null)
        {
            overlappingEnemies.Remove(enemy);
            // 오브가 지나쳐버린 적은 더 이상 "갈고 있는 중"이 아니다 — 예산은 이미 썼으니 돌려주지 않는다.
            claimed.Remove(enemy);
            nextTickTime.Remove(enemy);
        }
    }
}
