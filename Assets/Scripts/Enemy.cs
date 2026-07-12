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

    // GameManager가 Awake에서 할당 — 모든 적 프리팹에 개별로 물릴 필요 없이 한 곳에서 관리
    public static GameObject HeartPickupPrefab;
    public const float HeartDropChance = 0.06f; // 처치 시 하트 드랍 확률

    // 재귀로 발동되는 낙뢰(힘 연계 path0)를 재귀 횟수별로 색깔을 다르게 표시 (1회=노랑, 2회=파랑, 3회=보라, 4회=마젠타)
    private static readonly Color[] RecursiveLightningColors =
    {
        new Color(1f, 0.95f, 0.3f),
        new Color(0.35f, 0.55f, 1f),
        new Color(0.75f, 0.35f, 1f),
        new Color(1f, 0.35f, 0.85f),
    };

    public bool IsFlying => isFlying;
    public float CurrentHealth => currentHealth;

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

    public void TakeDamage(float amount, bool isLightningProc = false, int lightningChainDepth = 0, bool isCrit = false, bool suppressLightningStrikeVfx = false, ActiveSkillId? source = null)
    {
        if (isDead) return; // Destroy()는 프레임 끝에 실행되므로, 같은 프레임 내 중복 피격으로 사망 처리가 두 번 도는 것을 막음

        float actualDamage = amount * vulnerableMultiplier;
        currentHealth -= actualDamage;
        DamageMeter.Record(isLightningProc ? ActiveSkillId.Lightning : source, actualDamage);
        SpawnDamageNumber(actualDamage, isCrit);
        SpawnHitParticles(actualDamage);

        // 체인 라이트닝으로 전이된 타격은 연결선(beam)으로 이미 시각화되므로,
        // 하늘에서 세로로 내리치는 낙뢰 VFX를 여기서도 또 띄우면 "이어진다"는 느낌이 묻힘 — 이 경우만 생략.
        if (isLightningProc && !suppressLightningStrikeVfx && lightningVfxPrefab != null)
        {
            GameObject strikeVfx = ObjectPool.Instance.Spawn(lightningVfxPrefab, transform.position, Quaternion.identity);
            if (lightningChainDepth >= 1) TintLightningVfx(strikeVfx, RecursiveLightningColors[Mathf.Min(lightningChainDepth, RecursiveLightningColors.Length) - 1]);
            ObjectPool.Instance.Despawn(strikeVfx, 2f);
        }

        // 체인 라이트닝과 낙뢰 발동 판정은 이 타격이 적을 죽이는지와 무관하게 실행돼야 한다(사망 처리보다 뒤에 있으면,
        // 기본공격처럼 잡몹을 한 방에 죽이는 일이 잦은 공격에서는 그 킬각 타격이 애초에 낙뢰를 굴려볼 기회조차 못 얻는다).
        if (isLightningProc && lightningChainDepth == 1 && LightningStorm.ChainEnabled)
            ChainLightningToNearby();

        bool canChainAgain = !isLightningProc || (LightningStorm.RecursiveProcEnabled && lightningChainDepth < MaxLightningChain);
        if (canChainAgain)
        {
            // 낙뢰 버프는 스택형이라 살아있는 스택 수만큼 발동 확률을 독립적으로 판정한다 (스택 2개=최대 2번 발동).
            int procCount = LightningStorm.RollProcCount();
            for (int i = 0; i < procCount; i++)
            {
                // 힘 연계 path0 T3: 재귀로 떨어지는 낙뢰일수록(체인 깊이가 깊을수록) 더 강해짐
                float procDamage = LightningStorm.ProcDamage * Mathf.Pow(1f + LightningStorm.RecursiveDamageGrowth, lightningChainDepth);
                TakeDamage(procDamage, isLightningProc: true, lightningChainDepth: lightningChainDepth + 1);
                LightningStorm.OnProc?.Invoke();
            }
        }

        // 위 재귀 프록 도중에 이미 사망 처리가 끝났을 수 있으므로(같은 프레임 재진입) 여기서 한 번 더 막는다.
        if (currentHealth <= 0f && !isDead)
        {
            isDead = true;

            if (deathVfxPrefab != null)
            {
                GameObject deathVfx = ObjectPool.Instance.Spawn(deathVfxPrefab, transform.position, Quaternion.identity);
                deathVfx.transform.localScale = Vector3.one * 0.2f;
                ObjectPool.Instance.Despawn(deathVfx, 2f);
            }

            // 암살 연계 path1: 치명타로 처치한 적은 경험치를 배율만큼 추가로 지급
            int grantedXp = isCrit ? Mathf.RoundToInt(xpValue * PlayerPassives.AssassinateKillXpMultiplier) : xpValue;
            PlayerExperience.Instance?.AddXP(grantedXp);
            if (isTreasure) LevelUpUI.Instance.ShowTreasureReward();

            if (HeartPickupPrefab != null && Random.value < HeartDropChance)
                Instantiate(HeartPickupPrefab, transform.position, Quaternion.identity);

            Destroy(gameObject);
        }
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
                ObjectPool.Instance.Despawn(beam, 0.6f);
            }
            target.TakeDamage(LightningStorm.ProcDamage, isLightningProc: true, lightningChainDepth: MaxLightningChain, suppressLightningStrikeVfx: true);
        }
    }

    private static void TintLightningVfx(GameObject vfx, Color color)
    {
        foreach (ParticleSystem ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = ps.main;
            main.startColor = color;
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
