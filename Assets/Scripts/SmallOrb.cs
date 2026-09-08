using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SmallOrb : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float lifetime = 2f;
    [SerializeField] private GameObject impactVfxPrefab;

    private Vector2 direction = Vector2.left;
    private bool hasHit;
    private Enemy homingTarget;

    public float Damage { get; set; }
    public bool ApplyVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 명중 시 개별적으로 치명타를 굴린다

    // ── 오브 R1(호밍 연계) 전용 ──
    // 산탄 알(기본 용도)은 둘 다 꺼진 채로 직진한다. 오브 R1만 켠다.
    public bool Homing { get; set; }
    public int PierceRemaining { get; set; }        // 남은 관통 횟수. 0이면 첫 명중에 소멸(=산탄 알의 기존 동작)
    public ActiveSkillId Source { get; set; } = ActiveSkillId.Orb; // 데미지 집계용 출처

    private readonly HashSet<Enemy> hitEnemies = new HashSet<Enemy>();

    public void Init(Vector2 dir, float damage, bool applyVulnerable)
    {
        direction = dir.normalized;
        Damage = damage;
        ApplyVulnerable = applyVulnerable;
    }

    private void Start() => Destroy(gameObject, lifetime);

    private void Update()
    {
        // 추적: 아직 안 때린 적 중 가장 가까운 쪽으로 방향을 튼다.
        // ⚠️ 풀링된 적은 죽어도 참조가 null이 안 된다 — IsAlive를 같이 봐야 반납된 적을 영영 쫓지 않는다.
        if (Homing)
        {
            if (homingTarget == null || !homingTarget.IsAlive || hitEnemies.Contains(homingTarget))
                homingTarget = Projectile.NearestLivingEnemy(transform.position, true, hitEnemies);
            if (homingTarget != null)
                direction = ((Vector2)homingTarget.transform.position - (Vector2)transform.position).normalized;
        }
        transform.Translate(direction * moveSpeed * Time.deltaTime, Space.World);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit) return;
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null || hitEnemies.Contains(enemy)) return;
        hitEnemies.Add(enemy);

        enemy.TakeSkillHit(Damage, CritChance, Source);
        if (ApplyVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

        if (impactVfxPrefab != null)
            ObjectPool.Instance.SpawnTimed(impactVfxPrefab, enemy.transform.position, 2.2f);

        // 관통이 남았으면 살아서 다음 적을 찾아간다(오브 R1). 방패는 관통과 무관하게 끊는다.
        if (PierceRemaining > 0 && !enemy.BlocksProjectiles)
        {
            PierceRemaining--;
            homingTarget = null; // 즉시 재타겟 — 다음 프레임을 기다리지 않는다
            return;
        }

        hasHit = true;
        Destroy(gameObject);
    }
}
