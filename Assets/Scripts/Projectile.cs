using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private GameObject impactVfxPrefab;

    public float Damage { get; set; }
    public bool ApplyGemSlow { get; set; }
    public bool ApplyGemVulnerable { get; set; }
    public float SpeedMultiplier { get; set; } = 1f;

    private void Update()
    {
        transform.Translate(Vector2.left * moveSpeed * SpeedMultiplier * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null) return;

        enemy.TakeDamage(Damage);
        if (ApplyGemSlow) enemy.ApplySlow(0.3f, 3f);
        if (ApplyGemVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

        if (impactVfxPrefab != null)
            Destroy(Instantiate(impactVfxPrefab, transform.position, Quaternion.identity), 2f);

        Destroy(gameObject);
    }
}
