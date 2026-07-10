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
    public bool IsCrit { get; set; }
    public float SpeedMultiplier { get; set; } = 1f;
    public int PierceRemaining { get; set; }
    public System.Action<Enemy> OnHitBonus { get; set; }

    private readonly HashSet<Enemy> hitEnemies = new HashSet<Enemy>();

    private void Update()
    {
        transform.Translate(Vector2.left * moveSpeed * SpeedMultiplier * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null || enemy.IsFlying || hitEnemies.Contains(enemy)) return;
        hitEnemies.Add(enemy);

        enemy.TakeDamage(Damage, isCrit: IsCrit);
        if (ApplyGemSlow) enemy.ApplySlow(0.3f, 3f);
        if (ApplyGemVulnerable) enemy.ApplyVulnerable(1.5f, 3f);
        OnHitBonus?.Invoke(enemy);

        if (impactVfxPrefab != null)
            Destroy(Instantiate(impactVfxPrefab, transform.position, Quaternion.identity), 2f);

        if (PierceRemaining > 0)
        {
            PierceRemaining--;
            return;
        }

        Destroy(gameObject);
    }
}
