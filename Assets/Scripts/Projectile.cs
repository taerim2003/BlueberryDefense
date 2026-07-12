using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private GameObject impactVfxPrefab;

    public float Damage { get; set; }
    public bool ApplyGemSlow { get; set; }
    public bool ApplyGemVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 명중할 때마다 개별적으로 치명타를 굴린다
    public float SpeedMultiplier { get; set; } = 1f;
    public int PierceRemaining { get; set; }
    public bool CanHitFlying { get; set; } // 기본 path T1: 비행 적 타격 가능
    public System.Action<Enemy, bool> OnHitBonus { get; set; } // (적, 이번 타격의 치명타 여부)

    private readonly HashSet<Enemy> hitEnemies = new HashSet<Enemy>();

    // 발사음은 이 컴포넌트가 아니라 PlayerSkills.FireBasicAttack에서 한 캐스트당 정확히 한 번만 재생한다
    // (Projectile은 캐스트 한 번에 여러 발 생성될 수 있어, 발사체 쪽에 소리를 두면 재생 시점이 GameObject
    // 생성/컴포넌트 초기화 타이밍에 얽혀 불안정해진다).

    private void Update()
    {
        transform.Translate(Vector2.left * moveSpeed * SpeedMultiplier * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null || (enemy.IsFlying && !CanHitFlying) || hitEnemies.Contains(enemy)) return;
        hitEnemies.Add(enemy);

        float hitDamage = PlayerPassives.ApplyCrit(Damage, CritChance, out bool isCrit);
        enemy.TakeDamage(hitDamage, isCrit: isCrit, source: ActiveSkillId.BasicAttack);
        if (ApplyGemSlow) enemy.ApplySlow(0.3f, 3f);
        if (ApplyGemVulnerable) enemy.ApplyVulnerable(1.5f, 3f);
        OnHitBonus?.Invoke(enemy, isCrit);

        if (impactVfxPrefab != null)
            ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(impactVfxPrefab, transform.position, Quaternion.identity), 2f);

        if (PierceRemaining > 0)
        {
            PierceRemaining--;
            return;
        }

        Destroy(gameObject);
    }
}
