using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private GameObject impactVfxPrefab;
    [SerializeField] private AudioClip fireSfx;
    [SerializeField] private float fireSfxVolume = 0.45f;

    public float Damage { get; set; }
    public bool ApplyGemSlow { get; set; }
    public bool ApplyGemVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 명중할 때마다 개별적으로 치명타를 굴린다
    public float SpeedMultiplier { get; set; } = 1f;
    public int PierceRemaining { get; set; }
    public System.Action<Enemy, bool> OnHitBonus { get; set; } // (적, 이번 타격의 치명타 여부)

    private readonly HashSet<Enemy> hitEnemies = new HashSet<Enemy>();

    private void Awake()
    {
        // 발사체에 AudioSource를 직접 붙이면 명중 즉시 Destroy될 때 소리가 중간에 끊긴다(관통 없이 가까운 적에게 바로 맞는 경우 등).
        // PlayClipAtPoint는 호출마다 3D AudioSource가 달린 새 GameObject를 만들어서 거리 감쇠로 작게 들리고,
        // 연타 시 임시 오브젝트가 쌓여 재생이 불안정해진다 — SfxPlayer(상시 2D AudioSource + PlayOneShot)로 대체.
        // 같은 프레임에 여러 발이 동시 발사돼도(추가 발사체 레벨업, 크리티컬 보너스 발사 등) 무제한 중첩되지 않도록 디바운스한다.
        if (fireSfx != null && AudioThrottle.TryConsume(fireSfx))
            SfxPlayer.Play(fireSfx, fireSfxVolume);
    }

    private void Update()
    {
        transform.Translate(Vector2.left * moveSpeed * SpeedMultiplier * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null || enemy.IsFlying || hitEnemies.Contains(enemy)) return;
        hitEnemies.Add(enemy);

        float hitDamage = PlayerPassives.ApplyCrit(Damage, CritChance, out bool isCrit);
        enemy.TakeDamage(hitDamage, isCrit: isCrit);
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
