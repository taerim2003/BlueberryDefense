using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Enemy : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private int damage = 10;
    [SerializeField] private float maxHealth = 20f;
    [SerializeField] private int xpValue = 5;
    [SerializeField] private bool isTreasure;
    [SerializeField] private GameObject damageNumberPrefab;
    [SerializeField] private GameObject lightningVfxPrefab;
    [SerializeField] private GameObject deathVfxPrefab;
    [SerializeField] private GameObject hitParticlePrefab;
    [SerializeField] private Sprite[] hitParticleSprites;
    [SerializeField] private GameObject playerCollisionVfxPrefab;
    [SerializeField] private float spawnYOffset = 0f;
    [SerializeField] private bool alwaysBackLayer = false;

    private float currentHealth;
    private float slowMultiplier = 1f;
    private float slowTimer;
    private float vulnerableMultiplier = 1f;
    private float vulnerableTimer;
    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        currentHealth = maxHealth;
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spawnYOffset != 0f) transform.position += Vector3.up * spawnYOffset;
    }

    public void ApplyStageMultipliers(float hpMultiplier, float speedMultiplier, float damageMultiplier)
    {
        maxHealth *= hpMultiplier;
        currentHealth = maxHealth;
        moveSpeed *= speedMultiplier;
        damage = Mathf.RoundToInt(damage * damageMultiplier);
    }

    private void Update()
    {
        if (slowTimer > 0f)
        {
            slowTimer -= Time.deltaTime;
            if (slowTimer <= 0f)
                slowMultiplier = 1f;
        }

        if (vulnerableTimer > 0f)
        {
            vulnerableTimer -= Time.deltaTime;
            if (vulnerableTimer <= 0f)
                vulnerableMultiplier = 1f;
        }

        transform.Translate(Vector2.right * moveSpeed * slowMultiplier * Time.deltaTime);
        spriteRenderer.sortingOrder = alwaysBackLayer ? 1 : 100 + Mathf.RoundToInt(transform.position.x * 10f);
    }

    public void ApplySlow(float multiplier, float duration)
    {
        slowMultiplier = multiplier;
        slowTimer = duration;
    }

    public void ApplyVulnerable(float multiplier, float duration)
    {
        vulnerableMultiplier = multiplier;
        vulnerableTimer = duration;
    }

    public void TakeDamage(float amount, bool isLightningProc = false)
    {
        float actualDamage = amount * vulnerableMultiplier;
        currentHealth -= actualDamage;
        SpawnDamageNumber(actualDamage);
        SpawnHitParticles(actualDamage);

        if (isLightningProc && lightningVfxPrefab != null)
            ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(lightningVfxPrefab, transform.position, Quaternion.identity), 2f);

        if (currentHealth <= 0f)
        {
            if (deathVfxPrefab != null)
            {
                GameObject deathVfx = ObjectPool.Instance.Spawn(deathVfxPrefab, transform.position, Quaternion.identity);
                deathVfx.transform.localScale = Vector3.one * 0.2f;
                ObjectPool.Instance.Despawn(deathVfx, 2f);
            }

            PlayerExperience.Instance?.AddXP(xpValue);
            if (isTreasure) LevelUpUI.Instance.ShowTreasureReward();
            Destroy(gameObject);
            return;
        }

        if (!isLightningProc && Time.time < LightningStorm.ActiveUntil && Random.value < LightningStorm.ProcChance)
            TakeDamage(LightningStorm.ProcDamage, isLightningProc: true);
    }

    private void SpawnDamageNumber(float amount)
    {
        if (damageNumberPrefab == null) return;

        GameObject obj = ObjectPool.Instance.Spawn(damageNumberPrefab, transform.position, Quaternion.identity);
        obj.GetComponent<DamageNumber>().Init(amount);
    }

    private void SpawnHitParticles(float damage)
    {
        if (hitParticlePrefab == null || hitParticleSprites == null || hitParticleSprites.Length == 0) return;

        int count = Mathf.Clamp(2 + Mathf.FloorToInt(damage / 8f), 2, 9);
        for (int i = 0; i < count; i++)
        {
            GameObject p = ObjectPool.Instance.Spawn(hitParticlePrefab, transform.position, Quaternion.identity);
            Sprite sprite = hitParticleSprites[Random.Range(0, hitParticleSprites.Length)];
            Vector2 dir = new Vector2(Random.Range(-1f, 1f), Random.Range(0.6f, 1f)).normalized;
            float speed = Random.Range(4f, 8f);
            p.GetComponent<HitParticle>().Init(sprite, dir * speed);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth == null) return;

        playerHealth.TakeDamage(damage);

        if (playerCollisionVfxPrefab != null)
            ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(playerCollisionVfxPrefab, transform.position, Quaternion.identity), 2f);
        SpawnHitParticles(damage);

        Destroy(gameObject);
    }
}
