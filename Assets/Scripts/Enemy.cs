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
    [SerializeField] private float essenceDropChance = 0.15f; // 처치 시 정수(태양빛) 드랍 확률
    [SerializeField] private int essenceDropAmount = 2;        // 드랍 시 지급 정수량(보물은 확률 무시·확정 드랍)
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
    [SerializeField] private bool blocksProjectiles = false; // 방패 블루베리: 관통 투사체·오브가 이 적을 통과하지 못하고 여기서 소멸

    [Header("사망 시 분출(대왕 블루베리 = BTD 비행선 방식)")]
    [SerializeField] private GameObject[] deathSpawnPrefabs; // 사망 시 흩뿌릴 적들(마리마다 랜덤 선택). 대왕: 일반+리젠트+UFO
    [SerializeField] private int deathSpawnCount = 0;         // 흩뿌릴 총 마릿수(0=없음)
    [SerializeField] private float deathSpawnRadius = 0.6f;   // 초기 흩뿌림 반경
    [SerializeField] private GameObject deathBurstVfxPrefab;  // 분출 시 대형 VFX(폭발)
    [SerializeField] private float deathBurstVfxScale = 1f;

    [Header("팝콘 등장(사망분출로 튀어나온 잡몹)")]
    [SerializeField] private float popGravity = 30f;
    private bool popping;
    private float popVelY, popVelX, popGroundY;

    // GameManager가 Awake에서 할당 — 모든 적 프리팹에 개별로 물릴 필요 없이 한 곳에서 관리
    public static GameObject HeartPickupPrefab;
    // 하트(체력회복) 드랍은 스킬트리 루트(root_hp) 해금 시에만 발동 — 확률은 MetaBonuses.HealDropChanceBonus로 전적으로 결정

    // 재귀로 발동되는 낙뢰(힘 연계 path0)를 재귀 횟수별로 색깔을 다르게 표시 (1회=노랑, 2회=파랑, 3회=보라, 4회=마젠타)
    private static readonly Color[] RecursiveLightningColors =
    {
        new Color(1f, 0.95f, 0.3f),
        new Color(0.35f, 0.55f, 1f),
        new Color(0.75f, 0.35f, 1f),
        new Color(1f, 0.35f, 0.85f),
    };

    public bool IsFlying => isFlying;
    public bool BlocksProjectiles => blocksProjectiles;
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

    // 사망분출로 튀어나온 잡몹이 팝콘처럼 위로 튀어올랐다가 착지할 때까지의 연출. 착지 전엔 행진하지 않는다.
    public void PopIn(float upVel, float sideVel, float groundY)
    {
        popping = true;
        popVelY = upVel;
        popVelX = sideVel;
        popGroundY = groundY;
    }

    private void Update()
    {
        if (popping)
        {
            popVelY -= popGravity * Time.deltaTime;
            Vector3 p = transform.position;
            p.x += popVelX * Time.deltaTime;
            p.y += popVelY * Time.deltaTime;
            if (popVelY < 0f && p.y <= popGroundY) { p.y = popGroundY; popping = false; }
            transform.position = p;
            spriteRenderer.sortingOrder = alwaysBackLayer ? 1 : 100 + Mathf.RoundToInt(transform.position.x * 10f);
            return;
        }

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

    // rollLightning: 이 타격이 낙뢰 발동을 굴릴지. 멀티히트(TakeSkillHit)에선 첫 서브히트만 true로 넘겨
    //                공격당 낙뢰 기회를 1회로 유지한다(히트가 쪼개졌다고 낙뢰 빈도가 뻥튀기되지 않게).
    // hitIndex:     멀티히트 서브히트 순번. 데미지 숫자를 위로 주루룩 쌓는 데 씀.
    public void TakeDamage(float amount, bool isLightningProc = false, int lightningChainDepth = 0, bool isCrit = false, bool suppressLightningStrikeVfx = false, ActiveSkillId? source = null, bool rollLightning = true, int hitIndex = 0)
    {
        if (isDead) return; // Destroy()는 프레임 끝에 실행되므로, 같은 프레임 내 중복 피격으로 사망 처리가 두 번 도는 것을 막음

        float actualDamage = amount * vulnerableMultiplier;
        // 스킬트리: 비행 적 추가피해(전역 + 독수리 전용)
        if (isFlying)
            actualDamage *= 1f + MetaBonuses.FlyDamageBonus
                + (source == ActiveSkillId.EagleDrop ? MetaBonuses.EagleFlyDamageBonus : 0f);
        currentHealth -= actualDamage;
        DamageMeter.Record(isLightningProc ? ActiveSkillId.Lightning : source, actualDamage);
        SpawnDamageNumber(actualDamage, isCrit, hitIndex);
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

        bool canChainAgain = (!isLightningProc && rollLightning) || (LightningStorm.RecursiveProcEnabled && lightningChainDepth < MaxLightningChain);
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

            // 아웃게임 정수(태양빛) 드랍 — 하트처럼 물리적 픽업이 플레이어에게 흡입되어 적립됨
            if (isTreasure || Random.value < essenceDropChance)
            {
                if (EssencePickup.Prefab != null)
                    Instantiate(EssencePickup.Prefab, transform.position, Quaternion.identity)
                        .GetComponent<EssencePickup>().SetAmount(essenceDropAmount);
                else
                    MetaRun.Collect(essenceDropAmount); // 프리팹 미설정 시 즉시 적립(폴백)
            }

            if (HeartPickupPrefab != null && Random.value < MetaBonuses.HealDropChanceBonus)
                Instantiate(HeartPickupPrefab, transform.position, Quaternion.identity);

            SpawnDeathBurst();

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

    // 보스 사망 시 잡몹 블루베리들을 사방으로 흩뿌린다(BTD 비행선처럼). 흩뿌린 잡몹은 그냥 Enemy라
    // GameManager의 "잔몹 0" 클리어 조건에 자연히 포함된다. 무한 연쇄를 막으려 흩뿌린 잡몹 프리팹엔 deathSpawnCount=0.
    private void SpawnDeathBurst()
    {
        if (deathBurstVfxPrefab != null)
        {
            GameObject vfx = ObjectPool.Instance.Spawn(deathBurstVfxPrefab, transform.position, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * deathBurstVfxScale;
            ObjectPool.Instance.Despawn(vfx, 2f);
        }

        if (deathSpawnPrefabs == null || deathSpawnPrefabs.Length == 0 || deathSpawnCount <= 0) return;
        for (int i = 0; i < deathSpawnCount; i++)
        {
            GameObject prefab = deathSpawnPrefabs[Random.Range(0, deathSpawnPrefabs.Length)];
            if (prefab == null) continue;
            Vector2 offset = Random.insideUnitCircle * deathSpawnRadius;
            GameObject go = Instantiate(prefab, transform.position + (Vector3)offset, Quaternion.identity);
            // 팝콘처럼 위로 튀어올랐다가 바닥(y=0)에 착지 후 행진
            Enemy e = go.GetComponent<Enemy>();
            if (e != null) e.PopIn(Random.Range(5f, 9f), Random.Range(-5f, 5f), 0f);
        }
    }

    // 공격당 타격횟수(멀티히트) 진입점. baseDamage를 hits회로 쪼개 각각 크리를 개별 판정하고
    // 위로 주루룩 데미지 숫자를 띄운다. 기본공격만 hits>1(PlayerSkills.BasicAttackHits), 그 외 스킬은 1회.
    // 반환값 = 서브히트 중 하나라도 치명타였는지(호출부 OnHitBonus 등 크리 연동용).
    public bool TakeSkillHit(float baseDamage, float critChance, ActiveSkillId source)
    {
        int natural = Mathf.Max(1, PlayerSkills.NaturalHits(source)); // 스킬 고유 타수(기본공격=BasicAttackHits, 그 외 1)
        int total = natural + PlayerSkills.GlobalBonusHits(source);   // 산탄(타수) 버프로 추가된 타격 수
        float per = baseDamage / natural;                             // 자연 타수 기준 1히트 크기 → 보너스 히트는 추가 데미지

        if (total <= 1)
        {
            float d0 = PlayerPassives.ApplyCrit(per, critChance, out bool c0);
            TakeDamage(d0, isCrit: c0, source: source);
            return c0;
        }

        bool anyCrit = false;
        for (int i = 0; i < total; i++)
        {
            float d = PlayerPassives.ApplyCrit(per, critChance, out bool c);
            anyCrit |= c;
            TakeDamage(d, isCrit: c, source: source, rollLightning: i == 0, hitIndex: i); // 첫 히트만 낙뢰 굴림
            if (isDead) break;
        }
        return anyCrit;
    }

    private void SpawnDamageNumber(float amount, bool isCrit = false, int hitIndex = 0)
    {
        if (damageNumberPrefab == null) return;

        GameObject obj = ObjectPool.Instance.Spawn(damageNumberPrefab, transform.position, Quaternion.identity);
        // 멀티히트: 순번마다 위로 조금씩 올리고 좌우로 살짝 흔들어 "주루룩" 쌓이는 느낌 + 순번 비례 등장 딜레이로 순차 표시
        Vector3 offset = new Vector3((hitIndex % 2 == 0 ? 1f : -1f) * 0.18f * ((hitIndex + 1) / 2), 0.32f * hitIndex, 0f);
        float delay = 0.06f * hitIndex;
        obj.GetComponent<DamageNumber>().Init(amount, isCrit, offset, delay);
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
