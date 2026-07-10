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
    public bool IsCrit { get; set; }

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
        enemy.TakeDamage(Damage, isCrit: IsCrit);
        if (ApplyVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

        if (impactVfxPrefab != null)
            ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(impactVfxPrefab, enemy.transform.position, Quaternion.identity), 2f);

        Destroy(gameObject);
    }
}
