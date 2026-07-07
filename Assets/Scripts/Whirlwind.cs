using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Whirlwind : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float lifetime = 3f;
    [SerializeField] private GameObject impactVfxPrefab;

    public float Damage { get; set; }
    public bool ApplyGemSlow { get; set; }
    public bool ApplyGemVulnerable { get; set; }

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        transform.Translate(Vector2.left * moveSpeed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null) return;

        enemy.TakeDamage(Damage);
        if (ApplyGemSlow) enemy.ApplySlow(0.3f, 3f);
        if (ApplyGemVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

        if (impactVfxPrefab != null)
            Destroy(Instantiate(impactVfxPrefab, other.transform.position, Quaternion.identity), 2f);
    }
}
