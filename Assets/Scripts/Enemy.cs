using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Enemy : MonoBehaviour
{
    [SerializeField] private EnemyDefinition definition; // 밸런스 스탯(속도·피해·체력·xp·정수드랍). Awake에서 런타임 필드로 복사
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
    [SerializeField] private bool blocksProjectiles = false; // 방패 블루베리: 관통 투사체·오브가 이 적을 통과하지 못하고 여기서 소멸

    [Header("수송선(UFO) — 화면 위에서 내려와 부대 투하 후 상승 퇴장")]
    [SerializeField] private bool isCarrier = false;         // 켜면 좌진 행진 대신 하강→투하→상승 궤적을 탄다
    [SerializeField] private GameObject carrierDropPrefab;   // 투하할 잡몹(기본 블루베리)
    [SerializeField] private GameObject carrierRegentPrefab; // 투하 1회마다 섞이는 리젠트 1마리
    [SerializeField] private int carrierDropMin = 4;
    [SerializeField] private int carrierDropMax = 5;
    [SerializeField] private int carrierDropCount = 3;       // 호버 중 1초 간격으로 이만큼 반복 투하
    [SerializeField] private float carrierDropInterval = 1f; // 투하 간격(초)
    [SerializeField] private float carrierDescendSpeed = 4f;
    [SerializeField] private float carrierAscendSpeed = 5.5f;

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

    // 플레이어 앞 정체(박치기) 상태 — 수치는 BalanceConstants에 모여 있다.
    private float headbuttTimer;
    private float holdBaseX;        // 돌진 전 제자리 x(돌진 후 여기로 복귀)
    private float lungeTimer = -1f; // 0 이상이면 돌진 중
    private bool lungeDamageDone;
    private bool isHolding;         // 이번 프레임에 멈춰 서 있는가(추월 판정에서 참조)
    private Quaternion baseRotation; // 돌진 기울기를 얹기 전의 원래 회전(복귀 기준)
    private float laneJitter;       // 스폰 시 부여되는 y 흔들림 — 줄이 딱 맞게 정렬되지 않도록

    // 앞 적 감지용 공유 버퍼(적마다 새로 할당하지 않게 static 1개만 돌려쓴다).
    private static readonly List<Collider2D> aheadHits = new List<Collider2D>();
    private static ContactFilter2D aheadFilter = new ContactFilter2D { useTriggers = true };
    private static ContactFilter2D AheadFilter => aheadFilter;

    // 씬이 바뀌면 자동으로 null이 되어 다시 찾는다(판 간 static 누수 없음).
    private static PlayerHealth cachedPlayer;
    private static PlayerHealth Player
    {
        get
        {
            if (cachedPlayer == null) cachedPlayer = FindAnyObjectByType<PlayerHealth>();
            return cachedPlayer;
        }
    }

    // GameManager가 Awake에서 할당 — 모든 적 프리팹에 개별로 물릴 필요 없이 한 곳에서 관리
    public static GameObject HeartPickupPrefab;
    // 하트(체력회복) 드랍 확률 = 기본 3% + MetaBonuses.HealDropChanceBonus(스킬트리 가산, 현재 대응 노드 없음)
    private const float BaseHealDropChance = 0.03f;

    // 재귀로 발동되는 낙뢰(힘 연계 path0)를 재귀 횟수별로 색깔을 다르게 표시 (1회=노랑, 2회=파랑, 3회=보라, 4회=마젠타)
    private static readonly Color[] RecursiveLightningColors =
    {
        new Color(1f, 0.95f, 0.3f),
        new Color(0.35f, 0.55f, 1f),
        new Color(0.75f, 0.35f, 1f),
        new Color(1f, 0.35f, 0.85f),
    };

    public bool IsFlying => isFlying;
    public bool IsCarrier => isCarrier;
    public bool BlocksProjectiles => blocksProjectiles;
    public float CurrentHealth => currentHealth;
    public float SpawnYOffset => spawnYOffset; // 이 종류가 서는 자연 높이(레인 y=0 기준). 분출 팝콘의 착지 높이로 사용

    // definition에서 복사한 런타임 스탯 — 스테이지 배율(ApplyStageMultipliers)이 여기에만 곱해져 공유 SO를 오염시키지 않음.
    private float moveSpeed;
    private int damage;
    private float maxHealth;
    private int xpValue;
    private float essenceDropChance;
    private int essenceDropAmount;

    private float currentHealth;
    private bool isDead;

    // 캐리어(UFO) 궤적 상태 — 화면 위 스폰 → 하강 → 호버(투하) → 상승 퇴장
    private enum CarrierPhase { Descend, Hover, Ascend }
    private CarrierPhase carrierPhase;
    private float carrierTopY;   // 스폰·퇴장 높이(화면 위 바로 바깥) — Awake에서 카메라 기준 계산
    private float carrierHoverY; // 투하 고도(화면 상단부)
    private float carrierLaneY;  // 스포너가 준 레인 기준 y — 투하물이 착지할 지면 높이
    private float carrierHoverTimer;
    private int carrierDropsDone;
    // 스테이지 배율을 기억해 투하물에 동일 적용(공유 SO 오염 없이 자식도 같은 난이도로)
    private float appliedHpMult = 1f, appliedSpeedMult = 1f, appliedDamageMult = 1f;

    private float slowMultiplier = 1f;
    private float slowTimer;
    private float vulnerableMultiplier = 1f;
    private float vulnerableTimer;
    private SpriteRenderer spriteRenderer;
    private Animator animator; // 걷기 애니메이터(없을 수 있음) — 기절 중 정지시키기 위해 캐시

    private void Awake()
    {
        moveSpeed = definition.moveSpeed;
        damage = definition.damage;
        maxHealth = definition.maxHealth;
        xpValue = definition.xpValue;
        essenceDropChance = definition.essenceDropChance;
        essenceDropAmount = definition.essenceDropAmount;

        currentHealth = maxHealth;
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponentInChildren<Animator>();
        baseRotation = transform.localRotation;

        // 스폰 순간부터 y를 살짝 흔들어 둔다 — 겹쳐 쌓일 때 자로 잰 듯한 일렬이 아니라 두께 있는 무리로 보이게.
        // (캐리어는 Awake에서 스스로 화면 위로 재배치하므로 제외)
        laneJitter = Random.Range(-BalanceConstants.EnemySpawnYJitter, BalanceConstants.EnemySpawnYJitter);
        if (!isCarrier) transform.position += Vector3.up * laneJitter;

        if (isCarrier)
        {
            // 스포너가 준 스폰 y = 레인 지면 → 투하물 착지 높이로 보관. 그 뒤 화면 위 랜덤 x로 재배치.
            carrierLaneY = transform.position.y;
            Camera cam = Camera.main;
            float camX = cam != null ? cam.transform.position.x : 0f;
            float camY = cam != null ? cam.transform.position.y : 0f;
            float halfH = cam != null ? cam.orthographicSize : 5f;
            float halfW = cam != null ? halfH * cam.aspect : halfH * 1.78f;
            carrierTopY = camY + halfH + 1f;        // 화면 위 바로 바깥에서 등장
            carrierHoverY = camY + halfH * 0.28f;   // 화면 상단부에서 호버·투하(살짝 더 아래로 내려와 투하)
            // 플레이어(우측)에게 부대가 곧장 떨어지지 않도록, 화면 우측 1/3은 피하고 좌측~중앙에 등장
            float spawnX = camX + Random.Range(-halfW * 0.6f, halfW * 0.3f);
            transform.position = new Vector3(spawnX, carrierTopY, transform.position.z);
            carrierPhase = CarrierPhase.Descend;
        }
        else if (spawnYOffset != 0f) transform.position += Vector3.up * spawnYOffset;
    }

    // 기절 = 이동정지(slowMultiplier≈0). 이때 걷기 애니메이션도 함께 멈추고, 풀리면 다시 재생한다.
    private void SetAnimatorFrozen(bool frozen)
    {
        if (animator != null) animator.speed = frozen ? 0f : 1f;
    }

    public void ApplyStageMultipliers(float hpMultiplier, float speedMultiplier, float damageMultiplier)
    {
        appliedHpMult = hpMultiplier;       // 캐리어 투하물에 동일 배율을 물려주기 위해 기억
        appliedSpeedMult = speedMultiplier;
        appliedDamageMult = damageMultiplier;
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
        popGroundY = groundY + laneJitter; // 분출 팝콘도 같은 흔들림을 받아 착지 높이가 조금씩 다르게
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
            {
                slowMultiplier = 1f;
                SetAnimatorFrozen(false); // 기절 해제 → 걷기 재생 복구
            }
        }

        if (vulnerableTimer > 0f)
        {
            vulnerableTimer -= Time.deltaTime;
            if (vulnerableTimer <= 0f)
                vulnerableMultiplier = 1f;
        }

        if (isCarrier)
        {
            UpdateCarrier();
            return;
        }

        // 플레이어 앞에 닿았거나 앞 적에 막혔으면 그 자리에서 대기(전진 정지) — 줄줄이 쌓인다.
        bool holding = HoldAtPlayer();
        isHolding = holding; // 뒤 적이 "멈춰 선 적"인지 판단하는 데 쓴다(추월 예외 처리)
        if (!holding)
            transform.Translate(Vector2.right * moveSpeed * slowMultiplier * Time.deltaTime);

        // 제자리에 서 있으면 걷기 애니메이션도 멈춘다(기절 정지와 같은 스위치를 공유).
        SetAnimatorFrozen(holding || slowMultiplier <= 0.01f);

        spriteRenderer.sortingOrder = alwaysBackLayer ? 1 : 100 + Mathf.RoundToInt(transform.position.x * 10f);
    }

    // 멈춰야 하면 true. 플레이어에 실제로 닿은 맨 앞 적만 박치기하고,
    // 뒤에 막혀 있는 적들은 대기만 한다(그래서 동시에 때리는 건 각 레인의 선두 하나뿐).
    private bool HoldAtPlayer()
    {
        PlayerHealth player = Player;
        if (player == null) return false;

        if (transform.position.x >= player.transform.position.x - BalanceConstants.ContactStopDistance)
        {
            if (lungeTimer < 0f) holdBaseX = transform.position.x; // 돌진 중이 아닐 때의 제자리를 기억해 둔다
            Headbutt(player);
            return true;
        }

        return BlockedAhead();
    }

    // 예전에는 닿는 순간 damage를 한 번 주고 자폭했지만, 이제는 주기적으로 약하게 때린다.
    // 적은 죽여야만 사라지므로 "좀 맞고 있어도 버틸 수 있는" 수준으로 1회 피해를 낮춘다.
    private void Headbutt(PlayerHealth player)
    {
        if (lungeTimer >= 0f) { AdvanceLunge(player); return; }

        headbuttTimer += Time.deltaTime;
        if (headbuttTimer < BalanceConstants.HeadbuttInterval) return;

        headbuttTimer = 0f;
        lungeTimer = 0f;
        lungeDamageDone = false;
    }

    // 앞으로 튀어나갔다가 제자리로 돌아오는 돌진 박치기. sin 곡선이라 0 → 최대 → 0으로 자연히 왕복한다.
    // 피해는 가장 앞으로 나간 순간(k=0.5, 실제로 부딪히는 그림)에 들어간다.
    private void AdvanceLunge(PlayerHealth player)
    {
        lungeTimer += Time.deltaTime;
        float k = lungeTimer / BalanceConstants.HeadbuttLungeDuration;

        if (!lungeDamageDone && k >= 0.5f)
        {
            lungeDamageDone = true;
            int hit = Mathf.Max(1, Mathf.RoundToInt(damage * BalanceConstants.HeadbuttDamageScale));
            player.TakeDamage(hit);

            if (playerCollisionVfxPrefab != null)
                ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(playerCollisionVfxPrefab, transform.position, Quaternion.identity), 2f);
            SpawnHitParticles(hit);
        }

        Vector3 p = transform.position;
        if (k >= 1f)
        {
            lungeTimer = -1f;
            p.x = holdBaseX;
            transform.localRotation = baseRotation;
        }
        else
        {
            // 앞으로 나간 거리와 기울기가 같은 곡선을 타서, 튀어나가며 숙였다가 돌아오며 다시 선다.
            float curve = Mathf.Sin(Mathf.PI * k);
            p.x = holdBaseX + BalanceConstants.HeadbuttLungeDistance * curve;
            // 적은 +x로 전진하므로 앞으로 기울이려면 시계방향(-Z)
            transform.localRotation = baseRotation * Quaternion.Euler(0f, 0f, -BalanceConstants.HeadbuttLungeTilt * curve);
        }
        transform.position = p;
    }

    // 바로 앞(진행 방향)에 같은 레인의 다른 적이 있으면 막힌다. 레인 구분(y 허용치)이 있어서
    // 지상 줄과 공중 줄이 서로를 막지 않는다.
    private bool BlockedAhead()
    {
        // 프로브를 정지 판정에 직접 쓰면 앞 적의 **콜라이더 가장자리**에 닿는 순간 멈춰서
        // 간격이 스프라이트 폭(약 1.1유닛)만큼 벌어진다 = 겹치지 않고 줄 서 있는 그림.
        // 그래서 프로브는 후보를 넓게 긁어오는 용도로만 쓰고, 실제 판정은 **중심 간 x거리**로 한다.
        aheadHits.Clear();
        Physics2D.OverlapCircle(transform.position, BalanceConstants.EnemyStackSearchRadius, AheadFilter, aheadHits);

        for (int i = 0; i < aheadHits.Count; i++)
        {
            Collider2D hit = aheadHits[i];
            if (hit == null || hit.gameObject == gameObject) continue;
            if (!hit.TryGetComponent(out Enemy other)) continue;
            if (other.isCarrier || other.isDead || other.popping) continue;
            // 나보다 느린 적은 막지 못한다 — 라이더가 일반 블루베리를 추월해 제 속도로 달려간다.
            // 단 **이미 멈춰 선 적은 속도와 무관하게 막는다**: 안 그러면 추월한 적이 플레이어 앞에 선 적을
            // 그대로 통과해 같은 자리에 겹쳐 서고, 선두가 여러 마리가 되어 피해가 배로 들어간다.
            if (other.moveSpeed < moveSpeed - 0.01f && !other.isHolding) continue;

            float dx = other.transform.position.x - transform.position.x;
            if (dx <= 0f || dx >= BalanceConstants.EnemyStackSpacing) continue; // 뒤에 있거나 아직 여유 있음
            if (Mathf.Abs(other.transform.position.y - transform.position.y) > BalanceConstants.EnemyLaneTolerance) continue;

            return true;
        }
        return false;
    }

    // 캐리어 궤적: 하강 → 호버(중간에 1회 투하) → 상승 후 화면 위로 퇴장(Destroy).
    // 투하 전에 격추당하면(하강 중 사망) 부대는 안 나온다 — 빠른 대공에 대한 보상.
    private void UpdateCarrier()
    {
        Vector3 p = transform.position;
        switch (carrierPhase)
        {
            case CarrierPhase.Descend:
                p.y -= carrierDescendSpeed * slowMultiplier * Time.deltaTime;
                if (p.y <= carrierHoverY) { p.y = carrierHoverY; carrierPhase = CarrierPhase.Hover; carrierHoverTimer = 0f; }
                break;
            case CarrierPhase.Hover:
                carrierHoverTimer += Time.deltaTime;
                // 1초 간격으로 carrierDropCount번 반복 투하(첫 투하는 호버 진입 즉시). 마지막 투하 뒤 한 텀 있다가 상승.
                if (carrierDropsDone < carrierDropCount && carrierHoverTimer >= carrierDropsDone * carrierDropInterval)
                {
                    DropSquad();
                    carrierDropsDone++;
                }
                else if (carrierDropsDone >= carrierDropCount && carrierHoverTimer >= carrierDropCount * carrierDropInterval)
                    carrierPhase = CarrierPhase.Ascend;
                break;
            case CarrierPhase.Ascend:
                p.y += carrierAscendSpeed * slowMultiplier * Time.deltaTime;
                if (p.y >= carrierTopY) { Destroy(gameObject); return; }
                break;
        }
        transform.position = p;
        spriteRenderer.sortingOrder = alwaysBackLayer ? 1 : 100 + Mathf.RoundToInt(transform.position.x * 10f);
    }

    // 호버 지점에서 잡몹 부대를 팝콘처럼 흩뿌린다. 각 투하물은 레인 지면(carrierLaneY)으로 낙하하며,
    // 캐리어가 받은 스테이지 배율을 그대로 물려받아 후반 스테이지에서도 유의미한 위협이 된다.
    private void DropSquad()
    {
        if (carrierDropPrefab == null) return;
        int n = Random.Range(carrierDropMin, carrierDropMax + 1);
        for (int i = 0; i < n; i++)
        {
            // 투하 1회마다 첫 마리는 리젠트, 나머지는 기본 블루베리
            GameObject prefab = (i == 0 && carrierRegentPrefab != null) ? carrierRegentPrefab : carrierDropPrefab;
            Vector2 offset = new Vector2(Random.Range(-0.8f, 0.8f), Random.Range(-0.2f, 0.4f));
            GameObject go = Instantiate(prefab, transform.position + (Vector3)offset, Quaternion.identity);
            Enemy e = go.GetComponent<Enemy>();
            if (e == null) continue;
            e.ApplyStageMultipliers(appliedHpMult, appliedSpeedMult, appliedDamageMult);
            e.PopIn(Random.Range(2f, 4f), Random.Range(-3f, 3f), carrierLaneY + e.SpawnYOffset);
        }
    }

    // 보스 사망분출로 튀어나온 캐리어(UFO)는 Awake에서 화면 위로 순간이동해버려, 그대로 두면 하강하며
    // 플레이어 머리 위로 떨어져 확정 피해를 준다. 보스 죽은 자리로 되돌리고 투하 없이 곧장 상승 퇴장시킨다.
    public void EmergeAsBurstCarrier(Vector3 emergePos)
    {
        transform.position = emergePos;
        carrierPhase = CarrierPhase.Ascend;
    }

    public void ApplySlow(float multiplier, float duration)
    {
        slowMultiplier = multiplier;
        slowTimer = duration;
        SetAnimatorFrozen(multiplier <= 0.01f); // 기절(감속 0)이면 걷기 애니메이션도 정지
    }

    public void ApplyVulnerable(float multiplier, float duration)
    {
        vulnerableMultiplier = multiplier;
        vulnerableTimer = duration;
    }

    private const int MaxLightningChain = 4;

    // rollLightning:    이 타격이 낙뢰 발동을 굴릴지. 멀티히트(TakeSkillHit)에선 첫 서브히트만 true로 넘겨
    //                   공격당 낙뢰 기회를 1회로 유지한다(히트가 쪼개졌다고 낙뢰 빈도가 뻥튀기되지 않게).
    // hitIndex:        멀티히트 서브히트 순번 — 데미지 숫자를 세로로 정렬해 쌓는 데 씀.
    // forceShowNumber: 이미 죽은 뒤의 멀티히트 남은 서브히트도 데미지 숫자만은 띄운다(공격이 항상 같은 타수로 보이게).
    public void TakeDamage(float amount, bool isLightningProc = false, int lightningChainDepth = 0, bool isCrit = false, bool suppressLightningStrikeVfx = false, ActiveSkillId? source = null, bool rollLightning = true, int hitIndex = 0, bool forceShowNumber = false)
    {
        if (popping) return; // 팝콘 등장(튀어오르는) 중엔 무적 — 보스 분출 직후 광역기에 즉사해 "안 튀어나온 것처럼" 보이는 걸 막음
        if (isDead)
        {
            // 같은 프레임 중복 사망 처리는 막되(Destroy는 프레임 끝 실행), 멀티히트의 남은 숫자는 계속 쌓아 보여준다.
            if (forceShowNumber) SpawnDamageNumber(amount * vulnerableMultiplier, isCrit, hitIndex);
            return;
        }

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
            // 경험치 보석이 경험치 바까지 날아가 도착하는 순간 적립된다. 연출이 불가능하면(HUD 없는 씬 등) 즉시 적립.
            if (!XpGemFlight.TrySpawn(transform.position, grantedXp))
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

            if (HeartPickupPrefab != null && Random.value < BaseHealDropChance + MetaBonuses.HealDropChanceBonus)
                Instantiate(HeartPickupPrefab, transform.position, Quaternion.identity);

            // 만화식 의성어: 보스(사망분출을 가진 대왕)는 무조건 SMASH!,
            // 수송선을 투하 전에 격추하면 BOOM!(대공 보상), 나머지는 멀티킬 누적에만 기여.
            if (deathSpawnCount > 0)
                ComicBurst.Pop(transform.position, ComicBurst.Word.Smash, ignoreInterval: true);
            else if (isCarrier && carrierDropsDone == 0)
                ComicBurst.Pop(transform.position, ComicBurst.Word.Boom);
            else
                ComicBurst.NotifyKill(transform.position);

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

        // 레인 기준선(스포너 Y) = 보스는 y로 움직이지 않으므로 자기 위치에서 자기 spawnYOffset을 빼면 역산된다.
        // 각 팝콘은 (기준선 + 그 종류의 spawnYOffset)에 착지 → 레인이 y=0이 아니어도 종류별 자연 높이에 정확히 내려앉는다.
        float laneBaselineY = transform.position.y - spawnYOffset;
        for (int i = 0; i < deathSpawnCount; i++)
        {
            GameObject prefab = deathSpawnPrefabs[Random.Range(0, deathSpawnPrefabs.Length)];
            if (prefab == null) continue;
            Vector2 offset = Random.insideUnitCircle * deathSpawnRadius;
            GameObject go = Instantiate(prefab, transform.position + (Vector3)offset, Quaternion.identity);
            Enemy e = go.GetComponent<Enemy>();
            if (e == null) continue;
            // 캐리어(UFO)는 팝콘 낙하 대신 보스 죽은 자리에서 등장해 상승 퇴장(플레이어 위로 하강해 확정 피해 주던 문제 제거).
            // 그 외는 팝콘처럼 위로 튀어올랐다가 각 종류의 자연 높이로 착지(종이비행기는 공중, 일반은 바닥) → "둥둥 떠있는" 느낌 제거.
            if (e.IsCarrier)
                e.EmergeAsBurstCarrier(transform.position + (Vector3)offset);
            else
                e.PopIn(Random.Range(5f, 9f), Random.Range(-5f, 5f), laneBaselineY + e.SpawnYOffset);
        }
    }

    // 공격당 타격횟수(멀티히트) 진입점. baseDamage를 hits회로 쪼개 각각 크리를 개별 판정하고
    // 위로 주루룩 데미지 숫자를 띄운다. 기본공격만 hits>1(PlayerSkills.BasicAttackHits), 그 외 스킬은 1회.
    // 반환값 = 서브히트 중 하나라도 치명타였는지(호출부 OnHitBonus 등 크리 연동용).
    public bool TakeSkillHit(float baseDamage, float critChance, ActiveSkillId source)
    {
        int natural = Mathf.Max(1, PlayerSkills.NaturalHits(source)); // 스킬 고유 타수(기본공격=BasicAttackHits, 그 외 1)
        int total = natural + PlayerSkills.GlobalBonusHits(source)     // 산탄(타수) 버프로 추가된 타격 수
                    + PlayerSkills.CloseRangeBonusHits(source, transform.position); // 산탄 근거리 조준(+2, 스킬트리)
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
            // 적이 중간에 죽어도 끊지 않는다 — 남은 서브히트는 데미지만 무효(죽은 상태)이고 숫자는 계속 쌓아
            // "3번 때렸다가 1번 때렸다가" 하는 들쭉날쭉함을 없앤다. 첫 히트만 낙뢰 굴림.
            TakeDamage(d, isCrit: c, source: source, rollLightning: i == 0, hitIndex: i, forceShowNumber: true);
        }
        return anyCrit;
    }

    // 데미지 숫자는 적 머리 위(DamageNumberBaseHeight)에서 뜨고, 같은 공격의 서브히트는
    // 가로 정렬(x 오프셋 0)로 세로로만 쌓는다(9/9/9). 뜬 자리에 월드 고정되어 위로만 올라간다.
    private const float DamageNumberBaseHeight = 0.85f;
    private const float DamageNumberStackStep = 0.62f;

    private void SpawnDamageNumber(float amount, bool isCrit = false, int hitIndex = 0)
    {
        if (damageNumberPrefab == null) return;

        GameObject obj = ObjectPool.Instance.Spawn(damageNumberPrefab, transform.position, Quaternion.identity);
        Vector3 offset = new Vector3(0f, DamageNumberBaseHeight + DamageNumberStackStep * hitIndex, 0f);
        obj.GetComponent<DamageNumber>().Init(amount, isCrit, offset);
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

}
