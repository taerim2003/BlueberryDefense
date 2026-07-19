using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SmallOrb : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float lifetime = 2f;
    [SerializeField] private GameObject impactVfxPrefab;

    private Vector2 direction = Vector2.left;
    private bool hasHit;

    public float Damage { get; set; }
    public bool ApplyVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 명중 시 개별적으로 치명타를 굴린다

    public void Init(Vector2 dir, float damage, bool applyVulnerable)
    {
        direction = dir.normalized;
        Damage = damage;
        ApplyVulnerable = applyVulnerable;
    }

    private void Start() => Destroy(gameObject, lifetime);

    private void Update() => transform.Translate(direction * moveSpeed * Time.deltaTime, Space.World);

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit) return;
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null) return;

        hasHit = true;
        enemy.TakeSkillHit(Damage, CritChance, ActiveSkillId.Orb);
        if (ApplyVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

        if (impactVfxPrefab != null)
            ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(impactVfxPrefab, enemy.transform.position, Quaternion.identity), 2.2f);

        Destroy(gameObject);
    }
}
