using System.Collections.Generic;
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
    [SerializeField] private GameObject chainLightningVfxPrefab;
    [SerializeField] private GameObject deathVfxPrefab;
    [SerializeField] private GameObject hitParticlePrefab;
    [SerializeField] private Sprite[] hitParticleSprites;
    [SerializeField] private GameObject playerCollisionVfxPrefab;
    [SerializeField] private float spawnYOffset = 0f;
    [SerializeField] private bool alwaysBackLayer = false;
    [SerializeField] private bool isFlying = false;

    public bool IsFlying => isFlying;

    private float currentHealth;
    private bool isDead;
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

    private const int MaxLightningChain = 4;

    public void TakeDamage(float amount, bool isLightningProc = false, int lightningChainDepth = 0, bool isCrit = false, bool suppressLightningStrikeVfx = false)
    {
        if (isDead) return; // Destroy()는 프레임 끝에 실행되므로, 같은 프레임 내 중복 피격으로 사망 처리가 두 번 도는 것을 막음

        float actualDamage = amount * vulnerableMultiplier;
        currentHealth -= actualDamage;
        SpawnDamageNumber(actualDamage, isCrit);
        SpawnHitParticles(actualDamage);

        // 체인 라이트닝으로 전이된 타격은 연결선(beam)으로 이미 시각화되므로,
        // 하늘에서 세로로 내리치는 낙뢰 VFX를 여기서도 또 띄우면 "이어진다"는 느낌이 묻힘 — 이 경우만 생략.
        if (isLightningProc && !suppressLightningStrikeVfx && lightningVfxPrefab != null)
            ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(lightningVfxPrefab, transform.position, Quaternion.identity), 2f);

        if (currentHealth <= 0f)
        {
            isDead = true;

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

        if (isLightningProc && lightningChainDepth == 1 && LightningStorm.ChainEnabled)
            ChainLightningToNearby();

        bool canChainAgain = !isLightningProc || (LightningStorm.RecursiveProcEnabled && lightningChainDepth < MaxLightningChain);
        if (canChainAgain && Time.time < LightningStorm.ActiveUntil && Random.value < LightningStorm.ProcChance)
            TakeDamage(LightningStorm.ProcDamage, isLightningProc: true, lightningChainDepth: lightningChainDepth + 1);
    }

    // 체인 라이트닝: 첫 낙뢰 피격 시 주변 적 최대 3마리에게 전이 (재귀적으로 더 퍼지지는 않음)
    private void ChainLightningToNearby()
    {
        Enemy[] all = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
        List<Enemy> candidates = new List<Enemy>();
        foreach (Enemy e in all)
        {
            if (e == this) continue;
            if (Vector2.Distance(transform.position, e.transform.position) <= LightningStorm.ChainRadius)
                candidates.Add(e);
        }
        candidates.Sort((a, b) => Vector2.Distance(transform.position, a.transform.position)
            .CompareTo(Vector2.Distance(transform.position, b.transform.position)));

        int count = Mathf.Min(LightningStorm.ChainCount, candidates.Count);
        for (int i = 0; i < count; i++)
        {
            Enemy target = candidates[i];
            if (chainLightningVfxPrefab != null)
            {
                GameObject beam = ObjectPool.Instance.Spawn(chainLightningVfxPrefab, transform.position, Quaternion.identity);
                beam.GetComponent<ChainLightningBeam>().Init(transform.position, target.transform.position);
                ObjectPool.Instance.Despawn(beam, 0.35f);
            }
            target.TakeDamage(LightningStorm.ProcDamage, isLightningProc: true, lightningChainDepth: MaxLightningChain, suppressLightningStrikeVfx: true);
        }
    }

    private void SpawnDamageNumber(float amount, bool isCrit = false)
    {
        if (damageNumberPrefab == null) return;

        GameObject obj = ObjectPool.Instance.Spawn(damageNumberPrefab, transform.position, Quaternion.identity);
        obj.GetComponent<DamageNumber>().Init(amount, isCrit);
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
