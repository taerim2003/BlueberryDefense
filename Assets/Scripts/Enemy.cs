using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Enemy : MonoBehaviour
{
    [SerializeField] private EnemyDefinition definition; // 밸런스 스탯(속도·피해·체력·xp·정수드랍). Awake에서 런타임 필드로 복사
    [SerializeField] private bool isTreasure;

    // 보스 표식. 프리팹이 아니라 EnemySpawner가 스폰 직후 켜준다
    // (보스 여부는 "어떤 프리팹이냐"가 아니라 "보스 슬롯으로 스폰됐느냐"로 정해진다 —
    //  같은 프리팹이 다른 슬롯으로도 나올 수 있다).
    // 쓰임: 군중제어 감쇄. 보스는 둔화·기절·넉백을 절반만 받는다 — 안 그러면 넉백·기절을 연달아 걸어
    // 보스가 한 발짝도 못 오는 **무한 스톨링**이 된다(디펜스에서 보스전이 통째로 무력화된다).
    private bool isBoss;
    public void MarkAsBoss() => isBoss = true;
    private const float BossCrowdControlScale = 0.5f;

    [SerializeField] private GameObject damageNumberPrefab;
    [SerializeField] private GameObject lightningVfxPrefab;
    [SerializeField] private GameObject chainLightningVfxPrefab;
    [SerializeField] private GameObject deathVfxPrefab;
    [SerializeField] private GameObject hitParticlePrefab;
    [SerializeField] private Sprite[] hitParticleSprites;
    [SerializeField] private GameObject playerCollisionVfxPrefab;
    [SerializeField] private float spawnYOffset = 0f;
    // 개체마다 이동속도를 ±이 비율만큼 흩는다(0.15 = ±15%). 0이면 전원 같은 속도(=기존 적 전부).
    // 무리로 나오는 적이 자로 잰 듯 같은 속도로 붙어 오는 걸 깨는 용도.
    [SerializeField] private float speedVariance = 0f;
    [SerializeField] private bool alwaysBackLayer = false;
    [SerializeField] private bool isFlying = false;
    // "비행 유닛인가"(isFlying = 추가피해·호밍 우선타겟 같은 분류)와 "대공 능력이 있어야만 맞힐 수 있는가"를 분리한다.
    // ⚠️ 기본값은 반드시 **false**(= 히트박스로만 판정). true로 두면 이 필드가 직렬화되지 않은
    //    기존 프리팹 전부가 "대공 필요"로 잡혀 지상 적조차 아무 스킬에도 안 맞는다(실제로 한 번 터진 버그).
    //    대공 전용으로 만들 적(UFO)에만 프리팹에서 켤 것.
    [SerializeField] private bool requiresAntiAir = false;
    [SerializeField] private bool blocksProjectiles = false; // 방패 블루베리: 관통 투사체·오브가 이 적을 통과하지 못하고 여기서 소멸

    [Header("대각선 강하(종이비행기·서핑) — 화면 위/바다에서 플레이어로 직선 수렴")]
    [SerializeField] private bool isDiveFlyer = false;
    // 스폰 높이 = 카메라 중심 + (화면 절반 높이 × 이 비율). 비율로 두면 카메라 크기를 바꿔도 "화면 어디쯤"이 유지된다.
    // 종이비행기 0.4~0.95 = 화면 위쪽 중간~거의 최상단 → 완만한 강하와 급강하가 섞인다.
    // 서핑 0.03~0.14 = 물결 바로 위. **`cameraYLift` 덕분에 카메라 중심이 곧 배경의 물가선**이라
    // 낮은 비율이 그대로 "바다에서 나온다"가 된다(모래 위에서 서핑하지 않게).
    [SerializeField] private float diveSpawnHeightRatioMin = 0.4f;
    [SerializeField] private float diveSpawnHeightRatioMax = 0.95f;
    // 조준점을 플레이어에서 위아래로 이만큼 흩는다(월드 유닛, ±). 0이면 전원이 플레이어 한 점으로 수렴한다.
    // 0보다 크면 **날아가는 내내 세로 간격이 유지돼** 한 덩어리(무리)로 몰려오는 그림이 된다.
    // 스폰 높이만 흩어봐야 조준점이 같으면 접근할수록 한 줄로 좁혀지므로, 무리 느낌은 이 값이 만든다.
    [SerializeField] private float diveAimYSpread = 0f;
    // 강하 경로 위에 얹는 위아래 흔들림(파도 타는 느낌). 0이면 없음(=종이비행기는 기존 그대로 직선).
    [SerializeField] private float diveBobAmplitude = 0f;
    [SerializeField] private float diveBobSpeed = 2.2f;
    private const float DiveBobSettleTime = 0.3f; // 멈춰 선 뒤 흔들림이 0으로 잦아드는 시간(초)
    // 개체마다 위상·속도를 흩는다 — 안 그러면 무리 전체가 한 파도를 타듯 똑같이 출렁인다.
    private float diveBobPhase, diveBobPhase2, diveBobRate;
    private float diveBobPrev;  // 지난 프레임에 얹은 오프셋(경로에 누적되지 않게 차분만 더한다)
    private float diveBobTimer;

    [Header("호핑(콩콩이) — 지상 유닛이 크게 뛰면서 전진")]
    // isFlying은 끈 채로 둔다(분류상 지상 유닛). 대신 **떠 있는 동안 히트박스가 위로 올라가** 지상 스킬을
    // 흘려보내는 게 이 적의 정체성이다 — 높이가 곧 회피. 넓은 맵의 빈 세로 공간을 쓰라고 만든 유닛.
    [SerializeField] private bool isHopper = false;
    [SerializeField] private float hopHeight = 2.2f;       // 도약 최고점(월드 유닛, 착지 지면 기준)
    [SerializeField] private float hopDuration = 0.62f;    // 도약~착지까지 걸리는 시간
    [SerializeField] private float hopGroundPause = 0.12f; // 착지 후 다음 도약까지 웅크리는 시간
    private float hopBaseY;  // 착지 지면(= 스폰 시의 레인 y). 팝인으로 소환되면 착지 지점으로 갱신된다
    private float hopTimer;

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

    // 넉백은 등속이 아니라 **처음에 확 튕겨나갔다가 끝에서 멎는다**(EaseOutCubic).
    // 등속으로 밀면 "질질 끌려가는" 느낌이라 타격감이 죽는다.
    private const float KnockbackDuration = 0.25f;
    private float knockbackDistance; // 이번 넉백의 총 거리(0이면 넉백 중이 아님)
    private float knockbackElapsed;
    private float knockbackMoved;    // 지금까지 실제로 이동한 거리
    private Vector2 diveDir = Vector2.right; // 대각선 강하 방향(스폰 시 1회 결정)
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
    public bool RequiresAntiAir => requiresAntiAir; // 스킬이 "대공 불가라 못 맞힘" 판정에 쓰는 값(분류용 IsFlying과 별개)
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

    // 죽었거나 풀에 반납된 적을 걸러내는 생존 판정.
    // ⚠️ 풀링 전에는 죽은 적이 Unity의 가짜 null이 되어 `== null`만으로 정리됐지만, 풀은 오브젝트를
    //    비활성화만 하므로 참조가 살아남는다. **적 참조를 프레임 넘어 들고 있는 쪽은 반드시 이걸 봐야 한다**
    //    (안 그러면 유도미사일이 재활용된 새 적을 계속 쫓는다). 소비처: Orb·Whirlwind·HomingMissile.
    public bool IsAlive => !isDead && gameObject.activeInHierarchy;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponentInChildren<Animator>();
        InitializeSpawn(transform.position);
    }

    // 풀에서 꺼내 적을 스폰한다. 신규 생성이면 Awake가, 재사용이면 여기서 InitializeSpawn이 초기화한다.
    // 적을 만드는 4곳(스포너 정문·중간 소환·UFO 투하·사망 분출)은 전부 이 함수를 지나간다.
    public static Enemy Spawn(GameObject prefab, Vector3 position)
    {
        GameObject go = ObjectPool.Instance.Spawn(prefab, position, Quaternion.identity, out bool reused);
        if (go == null) return null;
        Enemy enemy = go.GetComponent<Enemy>();
        if (enemy != null && reused) enemy.InitializeSpawn(position); // 신규는 Awake가 방금 했다 — 두 번 하지 않는다
        return enemy;
    }

    // 스폰 시점의 전체 초기화.
    // ⚠️ **풀 재사용에는 Awake가 다시 돌지 않는다.** 런타임에 변하는 필드는 빠짐없이 여기서 되돌려야 한다.
    //    하나라도 빠뜨리면 "소환되자마자 죽어 있는 적"·"박치기 자세로 굳은 적"처럼 간헐적으로만 재현되는 버그가 된다.
    //    Enemy에 런타임 상태 필드를 추가하면 여기도 같이 고칠 것.
    private void InitializeSpawn(Vector3 spawnPos)
    {
        transform.position = spawnPos;
        transform.localRotation = Quaternion.identity;

        // definition → 런타임 스탯 복사. 스테이지 배율은 스폰 직후 ApplyStageMultipliers가 이 위에 곱한다.
        moveSpeed = definition.moveSpeed;
        // 개체차는 스테이지 배율보다 **먼저** 곱한다 — 배율이 뒤에 곱해져도 비율(±variance)이 그대로 유지된다.
        if (speedVariance > 0f) moveSpeed *= 1f + Random.Range(-speedVariance, speedVariance);
        damage = definition.damage;
        maxHealth = definition.maxHealth;
        xpValue = definition.xpValue;
        essenceDropChance = definition.essenceDropChance;
        essenceDropAmount = definition.essenceDropAmount;
        currentHealth = maxHealth;
        appliedHpMult = appliedSpeedMult = appliedDamageMult = 1f;

        isDead = false;

        popping = false; popVelY = 0f; popVelX = 0f; popGroundY = 0f;
        // 박치기 타이머를 주기만큼 채운 채로 시작한다 — 플레이어 앞에 도착하는 즉시 첫 박치기가 나간다
        // (0으로 두면 도착 후 HeadbuttInterval만큼 멀뚱히 서 있다가 때린다).
        headbuttTimer = BalanceConstants.HeadbuttInterval;
        holdBaseX = 0f; lungeTimer = -1f; lungeDamageDone = false; isHolding = false;
        knockbackDistance = 0f; knockbackElapsed = 0f; knockbackMoved = 0f;
        slowMultiplier = 1f; slowTimer = 0f; vulnerableMultiplier = 1f; vulnerableTimer = 0f;
        diveDir = Vector2.right;
        // 파도 흔들림: 위상 2개와 속도를 개체마다 새로 굴려 무리가 한 몸처럼 출렁이지 않게.
        diveBobPhase = Random.Range(0f, Mathf.PI * 2f);
        diveBobPhase2 = Random.Range(0f, Mathf.PI * 2f);
        diveBobRate = Random.Range(0.8f, 1.25f);
        diveBobPrev = 0f;
        diveBobTimer = 0f;
        // 캐리어 좌표 3종은 아래 isCarrier 분기에서 다시 계산되지만, 여기서도 0으로 되돌린다.
        // 비캐리어에겐 읽히지 않는 값이라 지금은 무해하지만 — "런타임 필드는 예외 없이 전부 리셋된다"는
        // 불변식을 깨 두면 나중에 이 값을 읽는 경로가 생겼을 때 잠복 버그가 된다.
        carrierPhase = CarrierPhase.Descend; carrierHoverTimer = 0f; carrierDropsDone = 0;
        carrierTopY = 0f; carrierHoverY = 0f; carrierLaneY = 0f;
        SetAnimatorFrozen(false); // 기절/정지로 animator.speed=0인 채 반납됐을 수 있다

        baseRotation = transform.localRotation;

        // 스폰 순간부터 y를 살짝 흔들어 둔다 — 겹쳐 쌓일 때 자로 잰 듯한 일렬이 아니라 두께 있는 무리로 보이게.
        // (캐리어는 스스로 화면 위로 재배치하므로 제외)
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
        else if (isDiveFlyer) SetupDive();
        else if (spawnYOffset != 0f) transform.position += Vector3.up * spawnYOffset;

        // 호핑 기준 지면 = y 보정이 전부 끝난 최종 스폰 높이.
        // 위상은 랜덤하게 흩어 둔다 — 안 그러면 같이 나온 콩콩이들이 한 몸처럼 동시에 뛰어서 군무가 된다.
        hopBaseY = transform.position.y;
        hopTimer = isHopper ? Random.Range(0f, hopDuration + hopGroundPause) : 0f;
    }

    // 화면 위쪽에서 등장해 **스폰 순간 정한 방향으로 직선 강하**한다(유도 아님 — 쭉 뻗은 선이 플레이어로 수렴).
    // 스폰 높이가 매번 달라 여러 마리가 부채꼴로 모여든다. 높이 있는 동안엔 지상 스킬의 히트박스가 안 닿아
    // 딜을 넣기 까다롭고, 플레이어에 가까워질수록 낮아져 대부분의 스킬에 맞기 시작한다.
    private void SetupDive()
    {
        Camera cam = Camera.main;
        float camY = cam != null ? cam.transform.position.y : 0f;
        float halfH = cam != null ? cam.orthographicSize : 5f;

        Vector3 p = transform.position;
        p.y = camY + halfH * Random.Range(diveSpawnHeightRatioMin, diveSpawnHeightRatioMax) + laneJitter;
        transform.position = p;

        PlayerHealth player = Player;
        // 조준점을 개체마다 살짝 어긋나게 잡아 무리의 세로 두께가 접근 중에도 유지되게 한다(diveAimYSpread).
        float aimJitter = diveAimYSpread > 0f ? Random.Range(-diveAimYSpread, diveAimYSpread) : 0f;
        Vector2 aim = player != null
            ? (Vector2)player.transform.position + Vector2.up * aimJitter
            : new Vector2(p.x + 20f, camY + aimJitter); // 플레이어가 없으면 오른쪽으로 완만히 강하
        diveDir = (aim - (Vector2)p).normalized;
        if (diveDir.sqrMagnitude < 0.0001f) diveDir = Vector2.right;

        // 기수를 진행 방향으로 — 종이비행기가 실제로 꽂히듯 기울어 날아간다.
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(diveDir.y, diveDir.x) * Mathf.Rad2Deg);
        baseRotation = transform.localRotation; // 돌진 기울기는 이 각도 위에 얹힌다
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
            // 팝인으로 소환된 콩콩이는 튀어오른 자리가 곧 자기 지면이 된다 — 착지 높이를 도약 기준으로 넘겨받는다.
            if (popVelY < 0f && p.y <= popGroundY) { p.y = popGroundY; popping = false; hopBaseY = popGroundY; }
            transform.position = p;
            spriteRenderer.sortingOrder = alwaysBackLayer ? 1 : 100 + Mathf.RoundToInt(transform.position.x * 10f);
            return;
        }

        // ⚠️ 넉백 중엔 기절 타이머가 흐르지 않는다. 밀려나는 동안 기절이 소진되면
        //    "밀친 뒤 굳는다"는 연출이 통째로 사라진다(박살내기 기절 0.5초를 넉백이 다 먹었다).
        //    캐리어는 ApplyKnockback에서 걸러져 knockbackDistance가 늘 0이라 영향받지 않는다.
        if (slowTimer > 0f && knockbackDistance <= 0f)
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

        // 밀려나는 동안엔 전진도 박치기도 하지 않는다 — 넉백만 한다.
        // ⚠️ 이 분기가 없으면 덜덜 떨린다: 박치기 돌진(AdvanceLunge)이 매 프레임 x를 holdBaseX 기준으로
        //    **덮어쓰기** 때문에, 넉백으로 민 위치가 sin 곡선에 먹히고 돌진이 끝날 때 holdBaseX로 스냅한다.
        //    전진도 마찬가지로 넉백과 매 프레임 반대 방향으로 싸워 정지 판정 경계에서 진동한다.
        if (knockbackDistance > 0f)
        {
            if (lungeTimer >= 0f) { lungeTimer = -1f; transform.localRotation = baseRotation; } // 돌진 취소
            TickKnockback();
            holdBaseX = transform.position.x; // 밀려난 자리가 곧 새 제자리
            isHolding = false;
            SetAnimatorFrozen(true);
            if (isHopper) UpdateHop();
            if (isDiveFlyer && diveBobAmplitude > 0f) UpdateDiveBob(false);
            spriteRenderer.sortingOrder = alwaysBackLayer ? 1 : 100 + Mathf.RoundToInt(transform.position.x * 10f);
            return;
        }

        // 플레이어 앞에 닿았거나 앞 적에 막혔으면 그 자리에서 대기(전진 정지) — 줄줄이 쌓인다.
        bool holding = HoldAtPlayer();
        isHolding = holding; // 뒤 적이 "멈춰 선 적"인지 판단하는 데 쓴다(추월 예외 처리)
        if (!holding)
        {
            // 강하 유닛은 기수가 돌아가 있으므로 반드시 월드 기준으로 이동해야 한다(로컬 right는 기울어져 있음).
            if (isDiveFlyer)
                transform.Translate(diveDir * moveSpeed * slowMultiplier * Time.deltaTime, Space.World);
            else
                transform.Translate(Vector2.right * moveSpeed * slowMultiplier * Time.deltaTime);
        }

        // 제자리에 서 있으면 걷기 애니메이션도 멈춘다(기절 정지와 같은 스위치를 공유).
        SetAnimatorFrozen(holding || slowMultiplier <= 0.01f);

        if (isHopper) UpdateHop();
        if (isDiveFlyer && diveBobAmplitude > 0f) UpdateDiveBob(holding);

        spriteRenderer.sortingOrder = alwaysBackLayer ? 1 : 100 + Mathf.RoundToInt(transform.position.x * 10f);
    }

    // 포물선 도약을 반복한다. y를 "지면 + 도약 높이"로 **덮어쓰는** 방식이라(누적 아님)
    // 박치기 돌진·기절처럼 x만 만지는 다른 로직과 섞여도 높이가 어긋나 쌓이지 않는다.
    // 플레이어 앞에 멈춰 선 뒤에도 계속 뛴다 — 콩콩이는 서 있는 그림이 없다.
    private void UpdateHop()
    {
        float cycle = hopDuration + hopGroundPause;
        hopTimer += Time.deltaTime * slowMultiplier; // 기절하면 공중에 굳는 게 아니라 도약 자체가 느려진다
        if (hopTimer >= cycle) hopTimer -= cycle;

        float lift = 0f;
        if (hopTimer < hopDuration)
        {
            float k = hopTimer / hopDuration;
            lift = hopHeight * 4f * k * (1f - k); // k=0.5에서 정확히 hopHeight
        }

        Vector3 p = transform.position;
        p.y = hopBaseY + lift;
        transform.position = p;
    }

    // 강하 경로 위에 파도 흔들림을 얹는다(서핑). 주기가 다른 사인 2개를 겹쳐 규칙적인 왕복이 아니라
    // 불규칙한 너울처럼 보이게 한다.
    // ⚠️ 흔들림은 **차분(이번 값 − 지난 값)만 더한다.** 매 프레임 offset을 그냥 더하면 경로에 누적돼
    //    적이 하늘로 떠오른다. 콩콩이(y를 절대값으로 덮어씀)와 달리 여기선 전진이 Translate 누적이라
    //    절대 대입을 쓸 수 없어서, 이 방식이 강하 이동과 섞이는 유일하게 안전한 형태다.
    private void UpdateDiveBob(bool holding)
    {
        float bob;
        if (holding)
        {
            // 플레이어 앞에 멈춰 섰다 = 모래에 올라섰다. 흔들림을 0으로 되돌려 가라앉힌다
            // (그냥 멈추면 최대 ±진폭만큼 뜬 채로 굳어서 땅에서 떠 있는 것처럼 보인다).
            bob = Mathf.MoveTowards(diveBobPrev, 0f, diveBobAmplitude / DiveBobSettleTime * Time.deltaTime);
        }
        else
        {
            diveBobTimer += Time.deltaTime * slowMultiplier;
            float w = diveBobSpeed * diveBobRate;
            bob = diveBobAmplitude *
                (Mathf.Sin(diveBobTimer * w + diveBobPhase) * 0.7f +
                 Mathf.Sin(diveBobTimer * w * 1.7f + diveBobPhase2) * 0.3f);
        }

        transform.position += Vector3.up * (bob - diveBobPrev);
        diveBobPrev = bob;
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
                if (p.y >= carrierTopY) { Despawn(); return; }
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
            Enemy e = Spawn(prefab, transform.position + (Vector3)offset);
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
        // 보스는 **감속의 세기**만 절반으로 받는다(지속시간은 그대로). 세기를 깎는 쪽이라
        // 기절(multiplier 0)조차 "느려짐"으로 바뀌어 보스가 계속 전진한다 — 무한 스톨링을 끊는 지점이 여기다.
        if (isBoss) multiplier = 1f - (1f - multiplier) * BossCrowdControlScale;

        slowMultiplier = multiplier;
        slowTimer = duration;
        SetAnimatorFrozen(multiplier <= 0.01f); // 기절(감속 0)이면 걷기 애니메이션도 정지
    }

    // 휘두르기처럼 밀어내는 공격 — 적을 진행 반대(왼쪽)로 물러나게 한다.
    // 예전엔 한 프레임에 순간이동시켰는데, 그러면 "밀렸다"가 눈에 안 보인다(특히 타격 이펙트가
    // 그 프레임을 가린다). 거리를 KnockbackSpeed로 나눠 몇 프레임에 걸쳐 미끄러지게 한다.
    public void ApplyKnockback(float distance)
    {
        if (isDead || popping || isCarrier) return; // 캐리어는 자기 상태기계로 움직여 밀면 궤적이 깨진다
        if (isBoss) distance *= BossCrowdControlScale; // 보스는 절반만 밀린다
        // 밀리는 도중에 또 맞으면 **끊고 처음부터 다시** 튕긴다. 남은 거리에 더하기만 하면
        // 이징이 이미 감속 구간에 들어가 있어서 두 번째 타격이 "씹힌" 것처럼 보인다.
        knockbackDistance = distance;
        knockbackElapsed = 0f;
        knockbackMoved = 0f;
    }

    // 이징 곡선 위의 "지금 있어야 할 위치"와 실제 이동량의 차이만큼 옮긴다.
    // x만 만지므로 콩콩이 도약(y 절대 대입)·서핑 너울(y 차분 누적)과 섞여도 높이가 어긋나 쌓이지 않는다.
    private void TickKnockback()
    {
        knockbackElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(knockbackElapsed / KnockbackDuration);
        float eased = 1f - Mathf.Pow(1f - t, 3f); // EaseOutCubic: 초반이 가장 빠르다
        float target = knockbackDistance * eased;

        transform.position += Vector3.left * (target - knockbackMoved);
        knockbackMoved = target;

        if (t >= 1f) knockbackDistance = 0f; // 끝 — 다음 프레임부터 평소대로 움직인다
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

            Despawn();
        }
    }

    // 파괴 대신 풀에 반납한다. 비활성화는 즉시 반영되고 `FindObjectsByType`은 기본이 "비활성 제외"라
    // GameManager의 "잔몹 0" 클리어 판정·회오리/미사일의 타겟 탐색에서 곧바로 빠진다.
    // (풀을 거치지 않고 씬에 직접 놓인 적은 ObjectPool.Despawn이 알아서 Destroy로 폴백한다)
    private void Despawn() => ObjectPool.Instance.Despawn(gameObject);

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
            Enemy e = Spawn(prefab, transform.position + (Vector3)offset);
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
