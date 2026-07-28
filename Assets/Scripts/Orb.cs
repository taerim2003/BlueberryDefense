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

    private const float ImpactSfxCooldown = 0.9f; // Whirlwind와 동일한 이유: 임팩트 클립 길이가 틱 간격(0.3초)보다 길어서 매 틱 재생하면 겹쳐 쌓인다.

    private float nextImpactSfxTime;
    private bool consumed; // 방패에 막혀 소멸 확정 — 같은 프레임에 다른 방패와도 겹쳐 있으면 중복 타격되는 것을 막는다
    private readonly HashSet<Enemy> overlappingEnemies = new HashSet<Enemy>();
    private readonly Dictionary<Enemy, float> nextTickTime = new Dictionary<Enemy, float>();

    private void Start()
    {
        Destroy(gameObject, lifetime * MetaBonuses.DurationMult);
    }

    private void Update()
    {
        transform.Translate(Vector2.left * moveSpeed * Time.deltaTime);

        overlappingEnemies.RemoveWhere(e => e == null);
        bool canHitFlying = FlyingDamageMultiplier > 1f || MetaBonuses.OrbCanHitFlying; // 기본 오브는 비행형 타격 불가, 공중 적 추가 피해 진화(path0) 또는 스킬트리로 해금
        foreach (Enemy enemy in new List<Enemy>(overlappingEnemies))
        {
            if (enemy == null || Time.time < nextTickTime.GetValueOrDefault(enemy, 0f)) continue;
            if (enemy.RequiresAntiAir && !canHitFlying) continue; // 대공 전용 적(UFO)만 차단 — 종이비행기는 히트박스로만 판정
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
            nextTickTime.Remove(enemy);
        }
    }
}
