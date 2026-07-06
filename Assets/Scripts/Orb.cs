using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Orb : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float slowMultiplier = 0.5f;
    [SerializeField] private float slowDuration = 2f;

    public float Damage { get; set; }
    public bool ApplyGemVulnerable { get; set; }

    private void Update()
    {
        transform.Translate(Vector2.left * moveSpeed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null) return;

        enemy.TakeDamage(Damage);
        enemy.ApplySlow(slowMultiplier, slowDuration);
        if (ApplyGemVulnerable) enemy.ApplyVulnerable(1.5f, 3f);
        Destroy(gameObject);
    }
}
