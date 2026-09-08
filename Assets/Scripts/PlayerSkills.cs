using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public enum ActiveSkillId
{
    BasicAttack,
    Whirlwind,
    Orb,
    Lightning,
    EagleDrop,
    Sniping,  // 신규: 가장 체력 높은 적을 5회 저격
    Homing,   // 신규: 적 추적 미사일(성장형)
    Shotgun,  // 신규: 산탄 장착(타수 버프)
    Rewind,   // 신규: 다른 스킬 쿨타임을 앞당김(되감기)
    Swing,    // 신규: 파인애플 전용 근접 광역 — 후려쳐서 뒤로 밀어낸다
    GrapeToss, // 신규: 포도 전용 — 독성 포도알을 던져 독안개를 깔고 중독시킨다
    // enum 끝에 추가 — 아이콘 인덱스/저장값 유지
}

// 액티브 스킬의 성격 분류(레벨업 카드·일시정지 요약에 배지로 표시).
public enum SkillCategory { Attack, Buff, Utility }

public class EquippedSkill
{
    public ActiveSkillId Id;
    public Key Key;
    public float Cooldown;
    public float Damage;
    public float CooldownTimer;
    public int Level = 1;
    public float Scale = 1f;
    public float ProjectileSpeedMultiplier = 1f;
    public float ProcChanceBonus = 0f;

    // 레벨업 전용 고유 강화치 (진화 트리와 별개)
    public int ExtraPierce = 0; // 기본공격: 관통 +1
    // 한 번의 시전에서 나가는 발사체/투하 수 — 기본공격 화살, 스나이핑 연사, 호밍 미사일, 독수리 투하, 산탄 알
    public int ExtraProjectiles = 0;
    // 동시에 상대하는 적 수 — 오브 동시 타격, 스나이핑 저격 대상
    public int ExtraTargets = 0;
    // 반복 타격 간격 배율(작을수록 자주 때림) — 회오리 피해 주기, 독수리 투하 간격
    public float TickIntervalMult = 1f;
    public float ExtraWhirlwindDuration = 0f; // 회오리: 지속시간(초) 추가
    public float ExtraShotgunDuration = 0f;   // 산탄: 버프 지속 + 관통 산탄 전탄발사 지속(초) 추가
    public int GrowthStacks = 0; // 호밍 미사일: 사용할수록 누적되는 성장 스택(이번 판 한정)
    public float RewindAmount = 1f; // 되감기: 다른 스킬 쿨타임을 앞당기는 시간(초). 짝수 레벨업마다 +0.15

    // 진화 효과 저장소. path 0=기본(무의존), 1=패시브 연계, 2=액티브 연계. 각 값은 도달한 티어(0~3).
    // ⚠️ 이제 이 배열을 직접 올리지 않는다 — 진화는 EvolutionRoutes를 통해 루트/티어로만 다룬다(§EvolutionRoutes).
    //    Fire*/TryUseSkill이 이 값을 그대로 읽으므로 저장 형식만 유지하는 것.
    public readonly int[] PathTier = new int[3];
    public int TotalEvolutionTier => PathTier[0] + PathTier[1] + PathTier[2];

    // ── 진화 상태 (2루트 × 2티어) ──
    public int TotalLevel = 1;      // 진화 리셋과 무관한 누적 레벨 — 표시용(진화 게이트는 표시 레벨 Level을 본다)
    public int EvolutionStage = 0;  // 0=미진화, 1=1차 진화, 2=2차 진화(최종)
    public int Route = -1;          // 1차 진화에서 고른 루트(0/1). 2차는 같은 루트를 이어간다

    // 진화하면 이름이 바뀐다. 미진화면 원래 이름.
    public string DisplayName =>
        EvolutionStage > 0 ? EvolutionRoutes.EvolvedName(Id, Route, EvolutionStage) : PlayerSkills.GetActiveSkillName(Id);
}

public class PlayerSkills : MonoBehaviour
{
    // 전역 상수는 BalanceConstants에 모여 있고 여기선 별칭으로 참조(호출부 이름 유지). 값 편집은 BalanceConstants에서.
    private const float GlobalCooldown = BalanceConstants.GlobalCooldown;
    private const float OrbAltarCooldown = BalanceConstants.OrbAltarCooldown;
    private const float MaxCritChance = BalanceConstants.MaxCritChance;
    private static readonly Key[] SlotKeys = { Key.Q, Key.W, Key.E, Key.R };

    // 회오리 path0(미니 회오리)와 독수리투하 path2(미니 회오리)가 공유하는 피해 배율 보너스 — 둘 중 어느 쪽에 투자해도 서로의 미니 회오리가 함께 강해진다.
    public static float MiniWhirlwindDamageBonus = 0f;

    // ── 포도(독성 포도알) ────────────────────────────────────────────────────
    // 진화로 켜지는 것만 static이다. **Enemy가 중독 틱 안에서 읽어야 해서** — 적은 어느 스킬이
    // 자기를 중독시켰는지 모른다. 나머지(안개 크기·포도알 수·도트 간격)는 skill.PathTier를
    // FireGrapeToss가 실시간으로 읽는다.
    public static bool GrapePoisonExplodeOnDeath;    // 생화학 2차: 중독사 시 폭발
    public static int GrapeStunEveryNPoisonTicks;    // 찌릿찌릿: N번째 중독 피해마다 기절 (0=없음)
    public static bool GrapeStunAppliesVulnerable;   // 찌릿찌릿 2차: 기절 뒤 취약까지

    public const float GrapePoisonDuration = 3f;     // 중독이 몸에 남는 시간(안개 밖으로 나가도 이만큼 아프다)
    public const float GrapePoisonInterval = 0.5f;   // 도트 간격 → 기본 6틱
    public const float GrapeCloudDuration = 4f;      // 안개가 바닥에 깔려 있는 시간
    public const float GrapeCloudRadius = 1.5f;      // 안개 반경(유닛). skill.Scale이 곱해진다
    public const int GrapeBaseBalls = 3;             // 한 번에 던지는 포도알 수
    public const float GrapeFlightTime = 0.55f;
    public const float GrapeArcHeight = 2.2f;
    public const float GrapeStunDuration = 0.5f;
    public const float GrapeStunVulnerableMult = 1.25f;
    public const float GrapeStunVulnerableDuration = 2f;
    public const float GrapeExplodeRadius = 2f;
    public const float GrapeExplodeDamageRatio = 0.35f; // 터진 적 최대체력 대비
    private static readonly Color GrapeCloudColor = new Color(0.42f, 0.16f, 0.55f, 1f);

    private const float MiniWhirlwindScale = 0.4f; // 미니 회오리 크기 배율(바닥선 보정 계산에도 쓰임)
    private const float MiniWhirlwindGroundBlend = 0.5f; // 바닥선 보정을 얼마나 먹일지. 1=큰 회오리와 바닥선 일치(너무 낮았다) / 0=보정 없음

    // 기본공격 공격당 타격횟수(멀티히트). 총 데미지는 유지한 채 N회로 쪼개 각각 크리를 개별 판정 → 메이플식 데미지 숫자. Enemy.TakeSkillHit가 읽음.
    public static int BasicAttackHits = BalanceConstants.BasicAttackBaseHits;

    // 스나이핑: 타겟 1명당 저격 횟수, 저격 간격
    private const int SnipingBaseShots = BalanceConstants.SnipingBaseShots;
    private const int OrbBaseTargets = BalanceConstants.OrbBaseTargets;
    private const int HomingBaseMissiles = BalanceConstants.HomingBaseMissiles;
    private const int EagleBaseDrops = BalanceConstants.EagleBaseDrops;
    private const int ShotgunBasePellets = BalanceConstants.ShotgunBasePellets;
    private const float SnipingShotInterval = BalanceConstants.SnipingShotInterval;

    // 산탄(타수) 버프 — 5초간 스킬 공격 횟수 증가. 전역(모든 스킬) 또는 최고공격력 스킬 1개(Route2).
    private static float shotgunTimer;
    private static int shotgunBonus;
    private static bool shotgunSingleTarget;
    private static ActiveSkillId shotgunTargetSkill;

    // 되감기 Route2: 다음에 사용하는 스킬의 피해를 1회 증가시키는 보너스(ComputeBaseDamage가 소비)
    private static float nextSkillDamageBonus;

    // Enemy.TakeSkillHit가 참조: 스킬 고유 타수(기본공격만 >1, 그 외 1)
    public static int NaturalHits(ActiveSkillId source) =>
        Prog(source) != null && Prog(source).baseHits > 0 ? Prog(source).baseHits
        : source == ActiveSkillId.BasicAttack ? Mathf.Max(1, BasicAttackHits) : 1;

    // Enemy.TakeSkillHit가 참조: 산탄 버프로 추가되는 타격 수
    public static int GlobalBonusHits(ActiveSkillId source)
    {
        if (shotgunTimer <= 0f) return 0;
        if (shotgunSingleTarget) return source == shotgunTargetSkill ? shotgunBonus : 0;
        return shotgunBonus;
    }

    // HUD가 참조: 이 스킬이 지금 산탄 타수버프를 받고 있는지(노란 테두리 하이라이트용)
    public static bool IsShotgunBuffed(ActiveSkillId source)
    {
        if (shotgunTimer <= 0f || shotgunBonus <= 0) return false;
        return !shotgunSingleTarget || source == shotgunTargetSkill;
    }

    private static PlayerSkills instance; // 근거리 판정 등 정적 메서드가 플레이어 위치를 참조하기 위한 인스턴스
    private const float ShotgunCloseRange = 3.5f;

    // ── 스킬트리 강화 노드의 경계값 (2026-09-03 재설계) ──
    // ⚠️ 둘 사이(4초 초과 ~ 5초 미만)인 스킬은 힘·가속 강화를 **둘 다 못 받는다.** 값을 고칠 땐 같이 볼 것.
    private const float StrengthDoubleCooldown = 5f;    // 힘 강화: 이 쿨 **이상**이면 힘 효과 2배
    private const float AccelFastSkillCooldown = 4f;    // 가속 강화: 이 쿨 **이하**면 피해 증가
    private const float AccelFastSkillDamageBonus = 0.3f;

    // Enemy.TakeSkillHit가 참조: 산탄 버프를 받은 공격이 플레이어 근처(ShotgunCloseRange) 적을 때릴 때 추가 타수(+2).
    // 스킬트리 "근거리 조준"(Shotgun_CloseBonus) 해금 시에만.
    public static int CloseRangeBonusHits(ActiveSkillId source, Vector3 enemyPos)
    {
        if (!MetaBonuses.ShotgunCloseBonus || instance == null) return 0;
        if (!IsShotgunBuffed(source)) return 0;
        return Vector2.Distance(instance.transform.position, enemyPos) <= ShotgunCloseRange ? 2 : 0;
    }

    // 버프류 스킬 = 지속시간 버프를 부여하는 스킬. **산탄은 집중 산탄 루트를 탔을 때만** 해당한다 —
    // 미진화 산탄과 관통 산탄은 순수 공격기라 버프를 아예 안 건다(FireShotgun의 PathTier[1] 게이트와 같은 조건).
    // 그래서 id 만으로는 못 가른다. 루트를 들고 있는 EquippedSkill 을 받는다.
    public static bool IsBuffSkill(EquippedSkill skill) =>
        skill.Id == ActiveSkillId.Lightning
        || (skill.Id == ActiveSkillId.Shotgun && skill.PathTier[1] >= 1);

    // 리프레쉬(재사용 초기화)가 발동될 때 — HUD가 구독해 리프레쉬 패시브 아이콘에 보잉 연출
    public static System.Action OnRefreshProc;

    [SerializeField] private GameObject basicAttackProjectilePrefab;
    // 화살 R0(암살 연계, path1) = 관통 무한 **큰 화살 한 발**. 그 한 발만 전용 그림으로 갈아끼운다
    // (뒤따르는 추적 화살은 명세상 "기본 화살"이라 원본 그대로 둔다).
    // 1차(T2)=암살 사격 / 2차(T3)=처형 사격으로 그림이 한 번 더 바뀐다.
    // 진화한 화살 그림(1차 = 암살 사격).
    // 🔴 `Effect_ArrowR1`·`Effect_ArrowR2`는 **둘 다 1차 진화 화살이고, 한 애니메이션의 1·2프레임**이다.
    //    티어가 아니다 — 태리미의 작화 도구가 R1로 저장하면 다음 프레임을 R2로 자동 명명한다.
    //    파일을 자르는 것도 아니다(각 파일이 통짜 64x32 한 장). 한 장만 꽂으면 정지 그림이 된다.
    //    ⚠️ `Icon_*R1/R2`는 **루트**라 같은 접미사가 여기선 다른 뜻이다. 새 그림을 받으면 한 번 물을 것.
    [SerializeField] private Sprite[] evolvedArrowFrames;
    // 2차 진화(처형 사격) 전용 그림이 생기면 여기 꽂는다. **비어 있으면 위 프레임을 그대로 쓴다**
    // (2026-08-26 현재 2차 전용 그림은 없다 — 2차가 1차 그림을 재사용한다).
    [SerializeField] private Sprite[] evolvedArrowFramesTier2;
    [SerializeField] private float evolvedArrowFps = 12f;
    [SerializeField] private GameObject whirlwindPrefab;
    [SerializeField] private GameObject miniWhirlwindPrefab; // 미니 회오리 전용 그림(Effect_MiniTornado). 미배선이면 본체를 축소해 쓴다(도트가 뭉개짐)
    [SerializeField] private GameObject lightningRodPrefab;  // 피뢰침 기둥(Effect_LightningRod). 미배선이면 임시 프리미티브로 폴백
    [SerializeField] private GameObject bigThunderVfxPrefab; // 피뢰침이 유도하는 큰 낙뢰(Effect_BigThunder) — 틱마다 기둥 꼭대기에 내리친다
    // 되감기 표식 — 시전할 때 머리 위에 한 번 떴다 사라진다.
    [SerializeField] private GameObject rewindVfxPrefab;       // 진화 전 기본(Effect_Rewind)
    [SerializeField] private GameObject rewindRoute2VfxPrefab; // R0 충전 되감기 = 다음 스킬 피해(Effect_Rewind_R)
    [SerializeField] private GameObject rockDebrisPrefab;      // 휘두르기에 맞은 적한테서 튀는 돌조각(Particle_Rock)
    // 파인애플 망치는 휘두르기 진화 루트에 따라 그림이 바뀐다(몸 트랙은 그대로, Hammer 자식 트랙만 교체하는 오버라이드).
    [SerializeField] private RuntimeAnimatorController bigHammerController;   // 힘 연계 = 박살내기
    [SerializeField] private RuntimeAnimatorController shockHammerController; // 회오리 연계 = 지진파
    [SerializeField] private GameObject bigTornadoPrefab;
    [SerializeField] private GameObject orbPrefab;
    [SerializeField] private GameObject bigOrbPrefab; // 지식 연계 path1 T2부터 등장하는 큰 초록 오브 비주얼
    [SerializeField] private GameObject orbAltarPrefab;
    [SerializeField] private GameObject shotgunPelletPrefab; // 산탄 알(SmallOrb_Skill 재사용 — 방향성 단발 투사체)
    [SerializeField] private GameObject scatterPelletPrefab;  // 산탄 알 전용 그림(Effect_Scatter 플립북). 미배선이면 shotgunPelletPrefab으로 폴백
    [SerializeField] private GameObject scatterFireVfxPrefab; // 산탄을 뿜을 때 알 뒤에 남는 화약/불꽃(Effect_ScatterFire). 알 개수만큼 스폰
    [SerializeField] private GameObject eagleDropPrefab;
    [SerializeField] private GameObject eagleImpactVfxPrefab;
    [SerializeField] private GameObject snipingEffectPrefab;      // 스나이핑 후속 타격 VFX(Effect_Sniping, 2~5번째 저격)
    [SerializeField] private GameObject snipingSplashPrefab;      // 스나이핑 첫 타격 강조 VFX(Effect_SplashSniping — 기본 이펙트 화려 버전)
    [SerializeField] private GameObject overkillSplashVfxPrefab;  // Route2 초과데미지 연쇄 전용 VFX(Vefects Impact Sparks)
    [SerializeField] private float overkillSplashVfxScale = 0.5f;
    [SerializeField] private GameObject homingMissilePrefab;      // 호밍 미사일 프리팹(추적)
    [SerializeField] private GameObject swingShockwavePrefab;     // 휘두르기 2루트 진화: 맵 끝까지 달리는 충격파
    // 포도알 그림은 기본 오브를 빌려 쓴다(전용 이펙트 없음 — 사용자 결정). 미배선이면 orbPrefab의 그림으로 폴백.
    [SerializeField] private GameObject grapeBallPrefab;
    [SerializeField] private GameObject grapeExplosionVfxPrefab;  // 착탄 순간의 터짐(Effect_Explosion 재사용)
    [SerializeField] private Animator animator;
    [SerializeField] private SkillProgression[] progressions; // 스킬별 시작값+레벨 커브(Tier A). 미할당/미포함 스킬은 코드 기본 규칙 폴백(=현행)

    // 정적 GetDefault*/Apply/Describe가 인스턴스 필드를 못 읽으므로 Awake에서 static 조회맵으로 승격.
    private static System.Collections.Generic.Dictionary<ActiveSkillId, SkillProgression> progressionLookup;
    private static SkillProgression Prog(ActiveSkillId id) =>
        progressionLookup != null && progressionLookup.TryGetValue(id, out var p) ? p : null;

    [SerializeField] private EvolutionProgression[] evolutions; // 진화별(스킬×루트×티어) 시작값+커브. 비어 있으면 현행 폴백

    // 키는 (스킬, 루트, 진화차수). 차수는 화면 기준 1·2다 — PathTier가 아니다.
    private static System.Collections.Generic.Dictionary<(ActiveSkillId, int, int), EvolutionProgression> evolutionLookup;
    private static EvolutionProgression EvoProg(EquippedSkill s) =>
        s != null && s.EvolutionStage > 0 && evolutionLookup != null
        && evolutionLookup.TryGetValue((s.Id, s.Route, s.EvolutionStage), out var e) ? e : null;

    [SerializeField] private AudioClip whirlwindCastSfx;
    [SerializeField] private AudioClip orbCastSfx;
    [SerializeField] private AudioClip eagleDropCastSfx;
    [SerializeField] private AudioClip grapeTossCastSfx;   // 포도알을 던지는 순간
    [SerializeField] private AudioClip grapePopSfx;        // 착탄해서 터지는 순간(알이 여러 개여도 같은 프레임이라 AudioThrottle이 한 번으로 묶는다)
    [SerializeField] private float castSfxVolume = 0.7f;
    [SerializeField] private float orbCastSfxVolume = 0.55f; // 원본 오브 발사음 자체가 다른 캐스트음보다 훨씬 크게(0dBFS 근접) 마스터링되어 있어 별도 볼륨 필요
    [SerializeField] private float grapePopSfxVolume = 0.6f; // 터짐은 한 번 시전에 한 번만 나지만 안개가 계속 깔리므로 캐스트음보다 낮게
    [SerializeField] private float whirlwindCastSfxVolume = 0.4f; // 원본 회오리 소환음 클립이 사실상 무음에 가까운 깨진 파일이었는데, 임포터 normalize 설정 때문에 재생 시 0dB까지 증폭되어 오히려 굉음으로 들리던 버그 — 정상 클립으로 교체 후 볼륨도 재보정

    private readonly List<EquippedSkill> equippedSkills = new List<EquippedSkill>();
    private float globalCooldownTimer;
    private float passiveDamageMultiplier = 1f;
    private float strengthDamageBonus;  // 위 배율 중 **힘 패시브가 얹은 몫**만 따로(스킬트리 "힘" 강화가 이 몫만 2배로 쓴다)
    private PlayerPassives passives;
    private PlayerHealth health;

    public bool HasMaxSkills => equippedSkills.Count >= SlotKeys.Length;
    public IReadOnlyList<EquippedSkill> EquippedSkills => equippedSkills;
    public float GlobalCooldownRatio => Mathf.Clamp01(globalCooldownTimer / GlobalCooldown);

    private void Awake()
    {
        instance = this;
        passives = GetComponent<PlayerPassives>();
        health = GetComponent<PlayerHealth>();
        BuildProgressionLookup(); // 정적 조회맵 승격. AcquireSkill 전에 세팅해야 기본 수치가 채워짐
        ResetRunState();          // static 상태 — 판 시작 시 초기화(도메인 리로드 없이도)
        // 시작 스킬은 선택된 캐릭터에서(없으면 기본공격 = 현행). RunConfig 직접 참조라 RunBootstrap 순서에 무의존.
        AcquireSkill(RunConfig.Character != null ? RunConfig.Character.startingSkill : ActiveSkillId.BasicAttack);
        LightningStorm.OnProc += HandleThunderCooldown;
    }

    private void OnDestroy()
    {
        LightningStorm.OnProc -= HandleThunderCooldown;
    }

    // 이 판에서만 유효한 static 상태 초기화 (RunState에서도 호출)
    public static void ResetRunState()
    {
        MiniWhirlwindDamageBonus = 0f;
        GrapePoisonExplodeOnDeath = false;
        GrapeStunEveryNPoisonTicks = 0;
        GrapeStunAppliesVulnerable = false;
        BasicAttackHits = BalanceConstants.BasicAttackBaseHits;
        shotgunTimer = 0f;
        shotgunBonus = 0;
        shotgunSingleTarget = false;
        nextSkillDamageBonus = 0f;
    }

    // 직렬화된 progressions[]를 id→SO 조회맵으로 승격(정적 메서드용). 미포함 스킬은 조회 실패 → 코드 기본 규칙 폴백.
    private void BuildProgressionLookup()
    {
        progressionLookup = new System.Collections.Generic.Dictionary<ActiveSkillId, SkillProgression>();
        if (progressions != null)
            foreach (var p in progressions)
                if (p != null) progressionLookup[p.skill] = p;

        evolutionLookup = new System.Collections.Generic.Dictionary<(ActiveSkillId, int, int), EvolutionProgression>();
        if (evolutions != null)
            foreach (var e in evolutions)
                if (e != null) evolutionLookup[(e.skill, e.route, e.stage)] = e;
    }

    // 스킬트리 "낙뢰 쿨타임 감소": 낙뢰가 칠 때마다 낙뢰 스킬 쿨타임을 조금씩 당긴다.
    private void HandleThunderCooldown()
    {
        if (MetaBonuses.ThunderCooldownPerStrike <= 0f) return;
        EquippedSkill lightning = equippedSkills.FirstOrDefault(s => s.Id == ActiveSkillId.Lightning);
        if (lightning != null)
            lightning.CooldownTimer = Mathf.Max(0f, lightning.CooldownTimer - MetaBonuses.ThunderCooldownPerStrike);
    }

    public float PassiveDamageMultiplier => passiveDamageMultiplier; // ESC 요약에서 현재 적용 중인 총 피해 배율 표기용

    public void IncreaseDamageMultiplier(float amount)
    {
        passiveDamageMultiplier += amount;
    }

    // 힘 패시브가 얹은 몫만 따로 기억한다 — 스킬트리 "힘: 쿨 5초 이상 스킬에 2배"가
    // **힘의 몫만** 한 번 더 얹기 때문(스킬트리 공격력 노드까지 2배가 되면 안 된다).
    public void IncreaseStrengthDamage(float amount)
    {
        strengthDamageBonus += amount;
        passiveDamageMultiplier += amount;
    }

    private void Update()
    {
        // 레벨업·보물·진화·ESC 창이 떠 있는 동안엔 스킬이 나가지 않는다.
        // Update는 timeScale 0에도 계속 돌아서, 쿨이 차 있던 스킬이 카드 뒤에서 발동돼 버렸다
        // (자동시전 스나이핑도 같은 이유로 매 프레임 나갔다).
        if (ModalPause.IsPaused) return;

        globalCooldownTimer -= Time.deltaTime;
        if (shotgunTimer > 0f) shotgunTimer -= Time.deltaTime;

        foreach (EquippedSkill skill in equippedSkills)
        {
            skill.CooldownTimer -= Time.deltaTime;

            // 스나이핑 path2(Route3) T2+: 수동 사용 불가, 쿨타임마다 자동 시전
            if (IsAutoCastOnly(skill))
            {
                if (skill.CooldownTimer <= 0f && globalCooldownTimer <= 0f)
                    TryUseSkill(skill);
                continue;
            }

            // 꾹 누르고 있어도 쿨이 끝나면 재발동(TryUseSkill이 쿨 통과 여부를 판정)
            if (Keyboard.current[skill.Key].isPressed)
                TryUseSkill(skill);
        }
    }

    // 수동 시전이 막히고 자동으로만 나가는 스킬(스나이핑 Route3 T2+). HUD가 쿨타임 마스크를 계속 씌워 표시한다.
    public static bool IsAutoCastOnly(EquippedSkill skill) =>
        skill.Id == ActiveSkillId.Sniping && skill.PathTier[2] >= 2;

    public bool HasSkill(ActiveSkillId id) => equippedSkills.Any(s => s.Id == id);

    public void AcquireSkill(ActiveSkillId id)
    {
        if (HasMaxSkills || HasSkill(id)) return;

        equippedSkills.Add(new EquippedSkill
        {
            Id = id,
            Key = SlotKeys[equippedSkills.Count],
            Cooldown = GetDefaultCooldown(id),
            Damage = GetDefaultDamage(id),
            // 시작 관통·투사체 수도 에셋이 정한다(둘 다 기본 0이라 값을 안 넣으면 지금과 같다).
            ExtraPierce = GetBasePierce(id),
            ExtraProjectiles = GetBaseProjectiles(id),
        });
        CollectionSave.DiscoverActive(id); // 컬렉션(도감) 발견 기록 — 판을 넘어 남는다
    }

    public void UpgradeSkillDamage(ActiveSkillId id, float amount)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill != null) skill.Damage += amount;
    }

    public void UpgradeSkillCooldown(ActiveSkillId id, float multiplier)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill != null) skill.Cooldown *= multiplier;
    }

    // 만렙(MaxSkillLevel)에 닿으면 더 이상 레벨업 후보로 뜨지 않는다 — 진화해서 Lv.1로 리셋해야 다시 큰다.
    public bool CanUpgradeSkill(EquippedSkill skill) => skill != null && skill.Level < BalanceConstants.MaxSkillLevel;

    public void UpgradeSkillLevel(ActiveSkillId id)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null || skill.Level >= BalanceConstants.MaxSkillLevel) return;

        skill.Level++;
        skill.TotalLevel++;
        ApplyUpgradeEffect(skill, skill.Level);
    }

    // 스킬트리 메타: 특정 스킬을 시작부터 지정 레벨로(진화 게이트 무시). 레벨업 효과를 순서대로 적용.
    public void SetSkillStartLevel(ActiveSkillId id, int targetLevel)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null) return;
        targetLevel = Mathf.Min(targetLevel, BalanceConstants.MaxSkillLevel);
        while (skill.Level < targetLevel)
        {
            skill.Level++;
            skill.TotalLevel++;
            ApplyUpgradeEffect(skill, skill.Level);
        }
    }

    // 레벨업 강화는 스킬별 SkillProgression(SO)이 정의한다. 미할당 스킬은 코드 기본 규칙(SkillProgression.DefaultStep=현행)으로 폴백.
    private static LevelUpStep StepFor(EquippedSkill skill, int level)
    {
        // 진화한 스킬은 자기 커브(Evo_*)를 먼저 본다. 그 에셋의 커브가 비어 있으면 원래 스킬 커브로 떨어진다 —
        // 진화 뒤에도 표시 레벨이 1부터 다시 오르므로 두 커브가 같은 level 값을 쓴다.
        LevelUpStep evoStep = EvoProg(skill)?.StepForLevel(level);
        if (evoStep != null) return evoStep;

        SkillProgression p = Prog(skill.Id);
        return p != null ? p.StepForLevel(level) : SkillProgression.DefaultStep(skill.Id, level);
    }

    private static void ApplyUpgradeEffect(EquippedSkill skill, int level)
    {
        ApplyStep(skill, StepFor(skill, level));
    }

    private static float Op(float cur, LevelUpStep s) => s.op == StatOp.Multiply ? cur * s.amount : cur + s.amount;

    private static void ApplyStep(EquippedSkill skill, LevelUpStep s)
    {
        switch (s.stat)
        {
            case SkillStat.Damage: skill.Damage = Op(skill.Damage, s); break;
            case SkillStat.Cooldown: skill.Cooldown = Mathf.Max(GlobalCooldown, Op(skill.Cooldown, s)); break;
            case SkillStat.ProjectileSpeed: skill.ProjectileSpeedMultiplier = Op(skill.ProjectileSpeedMultiplier, s); break;
            case SkillStat.Pierce: skill.ExtraPierce = Mathf.RoundToInt(Op(skill.ExtraPierce, s)); break;
            case SkillStat.ProjectileCount: skill.ExtraProjectiles = Mathf.RoundToInt(Op(skill.ExtraProjectiles, s)); break;
            case SkillStat.ProcChance: skill.ProcChanceBonus = Op(skill.ProcChanceBonus, s); break;
            // 같은 "지속시간" 스텝이라도 붙는 곳은 스킬마다 다르다(카드 문구는 둘 다 "지속시간 N초 증가"라 거짓말이 아니다).
            case SkillStat.Duration:
                if (skill.Id == ActiveSkillId.Shotgun) skill.ExtraShotgunDuration = Op(skill.ExtraShotgunDuration, s);
                else skill.ExtraWhirlwindDuration = Op(skill.ExtraWhirlwindDuration, s);
                break;
            case SkillStat.Scale: skill.Scale = Op(skill.Scale, s); break;
            case SkillStat.RewindAmount: skill.RewindAmount = Op(skill.RewindAmount, s); break;
            case SkillStat.TickRate: skill.TickIntervalMult = Mathf.Max(0.15f, Op(skill.TickIntervalMult, s)); break;
            case SkillStat.MaxTargets: skill.ExtraTargets = Mathf.RoundToInt(Op(skill.ExtraTargets, s)); break;
        }
    }

    // 화살비 진화 여부를 같이 넘긴다 — 같은 "추가 투사체" 스텝이 그 진화에선 웨이브 수로 읽히기 때문에
    // 문구도 같이 바뀌어야 한다(안 바꾸면 카드는 "투사체 +1"이라 적고 실제로는 비가 길어져 거짓말이 된다).
    public static string DescribeUpgradeEffect(EquippedSkill skill, int nextLevel) =>
        DescribeStep(StepFor(skill, nextLevel), skill.Id, ArrowRainReplacesShot(skill));

    // 미리보기 텍스트를 스텝 데이터에서 생성 → 미리보기·실제 적용이 항상 일치. (Apply와 같은 StepFor 참조)
    private static string DescribeStep(LevelUpStep s, ActiveSkillId id, bool arrowRain)
    {
        switch (s.stat)
        {
            case SkillStat.Damage:
                return s.op == StatOp.Multiply
                    ? Loc.F("step.Damage.mul", Mathf.RoundToInt((s.amount - 1f) * 100f))
                    : Loc.F("step.Damage.add", s.amount.ToString("0.##"));
            case SkillStat.Cooldown:
                return s.op == StatOp.Multiply
                    ? Loc.F("step.Cooldown.mul", Mathf.RoundToInt((1f - s.amount) * 100f))
                    : Loc.F("step.Cooldown.add", (-s.amount).ToString("0.##"));
            case SkillStat.ProjectileSpeed: return Loc.F("step.ProjectileSpeed", Mathf.RoundToInt(s.amount * 100f));
            case SkillStat.Pierce: return Loc.F("step.Pierce", Mathf.RoundToInt(s.amount));
            // 화살비 진화는 정면 화살이 없어 발수로 쓸 데가 없다 — 이 스텝이 "비가 오는 시간"으로 읽힌다.
            case SkillStat.ProjectileCount:
                return arrowRain
                    ? Loc.F("step.ProjectileCount.arrowrain", Mathf.RoundToInt(s.amount))
                    : Loc.F("step.ProjectileCount", Mathf.RoundToInt(s.amount));
            case SkillStat.ProcChance: return Loc.F("step.ProcChance", Mathf.RoundToInt(s.amount * 100f));
            case SkillStat.Duration: return Loc.F("step.Duration", s.amount.ToString("0.##"));
            case SkillStat.Scale: return Loc.F("step.Scale", Mathf.RoundToInt(s.amount * 100f));
            case SkillStat.RewindAmount: return Loc.F("step.RewindAmount", s.amount.ToString("0.##"));
            case SkillStat.TickRate: return Loc.F("step.TickRate", Mathf.RoundToInt((1f - s.amount) * 100f));
            // 오브만 "동시"가 아니라 사라지기 전까지 붙잡는 **총** 적 수(소모성 예산). 스나이핑은 동시 저격 대상 그대로.
            case SkillStat.MaxTargets:
                return id == ActiveSkillId.Orb
                    ? Loc.F("step.MaxTargets.orb", Mathf.RoundToInt(s.amount))
                    : Loc.F("step.MaxTargets", Mathf.RoundToInt(s.amount));
            default: return "";
        }
    }

    // ── 진화 (2루트 × 2티어, 진화 아이템으로만 열림) ────────────────────────────
    // 1차: 만렙(Lv.10) 도달 → 열려 있는 루트 중 하나 선택 (고르면 Lv.1로 리셋)
    // 2차: 다시 만렙 도달 → 1차에서 고른 루트의 다음 티어(선택지 없음)
    public bool CanEvolve(EquippedSkill skill)
    {
        if (skill == null || skill.EvolutionStage >= EvolutionRoutes.MaxStage) return false;
        // 스킬트리 "진화 해금" / "2차 진화 해금". 트리에 그 노드가 없으면 둘 다 true라 게이팅이 없다.
        if (!(skill.EvolutionStage == 0 ? MetaBonuses.EvolutionUnlocked : MetaBonuses.Evolution2Unlocked)) return false;
        if (skill.Level < EvolutionRoutes.RequiredLevel) return false;
        return SelectableRoutes(skill).Any(r => IsRouteUnlocked(skill.Id, r));
    }

    // 루트 잠금: 연계 대상(패시브/액티브)을 보유해야 그 루트를 고를 수 있다(§EvolutionRoutes).
    public bool IsRouteUnlocked(ActiveSkillId id, int route)
    {
        PassiveSkillId? p = EvolutionRoutes.RoutePassivePrereq(id, route);
        if (p.HasValue && (passives == null || !passives.HasPassive(p.Value))) return false;
        ActiveSkillId? a = EvolutionRoutes.RouteActivePrereq(id, route);
        return !a.HasValue || HasSkill(a.Value);
    }

    // 이 진화에서 고를 수 있는 루트들. 1차는 0·1 둘 다, 2차는 이미 고른 루트 하나뿐.
    public static int[] SelectableRoutes(EquippedSkill skill) =>
        skill.EvolutionStage == 0 ? new[] { 0, 1 } : new[] { skill.Route };

    public void EvolveSkill(ActiveSkillId id, int route)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null || !CanEvolve(skill)) return;
        if (skill.EvolutionStage > 0 && route != skill.Route) return; // 2차는 루트 변경 불가
        if (!IsRouteUnlocked(id, route)) return;                      // 연계 스킬 미보유

        int newTier = skill.EvolutionStage + 1;
        int path = EvolutionRoutes.RoutePath(id, route);

        // 기존 티어 효과를 순서대로 적용 — 새 티어1 = 옛 T1+T2, 새 티어2 = 옛 T3
        foreach (int legacyTier in EvolutionRoutes.LegacyTiersFor(newTier))
        {
            skill.PathTier[path] = legacyTier;
            ApplyPathTierEffect(skill, path, legacyTier);
        }
        skill.PathTier[path] = EvolutionRoutes.TargetPathTier(newTier);

        skill.Route = route;
        skill.EvolutionStage = newTier;
        CollectionSave.DiscoverActiveEvo(id, route, newTier); // 컬렉션(도감) 발견 기록
        SfxPlayer.Play(SfxId.Evolution);

        // 기본 스탯 도약 + 레벨 표시 리셋(누적 레벨 TotalLevel은 유지 — 다음 진화 게이트 기준).
        // 레벨업 커브를 처음부터 다시 타므로 "새 스킬을 1레벨부터 키운다"는 감각이 된다.
        skill.Damage *= EvolutionRoutes.EvolveDamageMult;
        skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * EvolutionRoutes.EvolveCooldownMult);
        skill.Level = 1;

        // 진화 전용 에셋(Evo_*)이 값을 정해 뒀으면 **그 축만** 덮어쓴다. 비어 있으면(전부 0) 위 계산 그대로 —
        // 그래서 에셋을 안 채운 진화는 지금까지와 100% 같게 돈다.
        ApplyEvolutionProgression(skill);
    }

    // 🔴 0 = "이 진화는 이 축을 안 정한다". 여기서 덮어쓰는 건 **시작값**이고, 이후 레벨업은
    //    `StepFor`가 진화 커브(있으면) → 원래 스킬 커브 순으로 태운다.
    private static void ApplyEvolutionProgression(EquippedSkill skill)
    {
        EvolutionProgression e = EvoProg(skill);
        if (e == null) return;
        if (e.baseDamage > 0f) skill.Damage = e.baseDamage;
        if (e.baseCooldown > 0f) skill.Cooldown = Mathf.Max(GlobalCooldown, e.baseCooldown);
        if (e.basePierce > 0) skill.ExtraPierce = e.basePierce;
        if (e.baseProjectiles > 0) skill.ExtraProjectiles = e.baseProjectiles;
        // 지속시간은 스킬마다 담기는 필드가 달라 산탄만 따로 받는다(나머지는 BaseDuration이 에셋을 직접 읽는다).
        if (e.baseDuration > 0f && skill.Id == ActiveSkillId.Shotgun)
            skill.ExtraShotgunDuration = e.baseDuration - BaseDuration(skill.Id, 5f + 2f);
    }

    // 티어마다 정확히 하나의 효과만 부여한다. 여기 없는 조합은 PathTier를 직접 읽는 Fire*/TryUseSkill에서
    // 실시간으로 계산하는 기믹(관통 횟수, 분열 소환 수, 슬로우 강화, 낙뢰 재귀/스택 등)이라 영구 스탯 변경이 없다.
    // 진화한 망치로 갈아끼운다. ApplyPathTierEffect가 static이라 instance를 거친다.
    // ⚠️ 딸기처럼 다른 캐릭터가 휘두르기를 진화시켰을 때 파인애플 컨트롤러를 씌우면 **캐릭터가 통째로 파인애플이 된다** —
    //    base 컨트롤러가 같을 때(= 지금 파인애플일 때)만 교체한다.
    private static void ApplyHammerLook(bool shockRoute)
    {
        if (instance == null || instance.animator == null) return;

        RuntimeAnimatorController next = shockRoute ? instance.shockHammerController : instance.bigHammerController;
        if (next == null) return;

        RuntimeAnimatorController cur = instance.animator.runtimeAnimatorController;
        AnimatorOverrideController curOverride = cur as AnimatorOverrideController;
        RuntimeAnimatorController curBase = curOverride != null ? curOverride.runtimeAnimatorController : cur;

        AnimatorOverrideController nextOverride = next as AnimatorOverrideController;
        RuntimeAnimatorController nextBase = nextOverride != null ? nextOverride.runtimeAnimatorController : next;

        if (curBase != nextBase) return; // 다른 캐릭터 — 건드리지 않는다
        instance.animator.runtimeAnimatorController = next;
    }

    // 🔴 **진화는 쿨타임을 줄이지 않는다**(2026-09-07 사용자 결정 — "진화 스킬 쿨들이 다 너무 짧음").
    //    쿨감 분기(낙뢰 R0 1차·독수리 R1 1차·스나이핑 R1 1차·되감기 R1·호밍 R1·오브 R0 2차·처형 사격)를
    //    전부 걷어냈고, `EvolutionRoutes.EvolveCooldownMult`도 1로 뒀다.
    //    ⚠️ **쿨타임을 늘리는 분기는 남긴다** — 그건 강한 진화가 치르는 대가지 불만의 대상이 아니다.
    //    새 진화 효과에 쿨감을 넣지 말 것. 도약은 피해·범위·타수로 준다.
    private static void ApplyPathTierEffect(EquippedSkill skill, int path, int newTier)
    {
        switch (skill.Id, path, newTier)
        {
            // path0(기본) — 회오리 R0·낙뢰 R0만 여기 남는다(나머지 스킬의 path0은 버려진 루트).
            case (ActiveSkillId.Whirlwind, 0, 2):
                MiniWhirlwindDamageBonus += 0.4f; // 미니 회오리 피해량 40% 증가
                break;
            case (ActiveSkillId.Whirlwind, 0, 3):
                MiniWhirlwindDamageBonus += 0.25f; // 미니 회오리 피해량 25% 추가 증가
                break;
            case (ActiveSkillId.Lightning, 0, 2):
                skill.Damage *= 1.4f; // 피해량 40% 증가
                break;
            // Lightning path0 T3(재귀마다 피해량 누적 증가)는 LightningStorm.RecursiveDamageGrowth로 실시간 계산
            // Whirlwind path0 T1/T3(미니 회오리 개수)는 FireWhirlwind에서 매 캐스트마다 실시간 계산

            // path1(패시브 연계)
            // R0(산탄 연계) 1차 = 3초짜리 독수리 비. 화면 전체를 10틱 넘게 두들기는 값이라
            // 예전의 쿨감(×0.85) 대신 **쿨타임을 늘려** 대가를 치르게 한다("한 번 부르면 하늘이 덮이지만 자주 못 부른다").
            case (ActiveSkillId.EagleDrop, 1, 2):
                skill.Cooldown *= 1.4f;
                break;
            // Orb 1,3은 문서상 슬로우 강화만이다(FireOrb 실시간). 딸려 있던 쿨감 0.9배는 걷어냈다.
            case (ActiveSkillId.Orb, 1, 2):
                // "대형 오브" — 크기 증가 + **관통 무한**(FireOrb에서 MaxTargets를 무제한으로).
                // 예전엔 여기에 쿨감 25%까지 붙어 크기·쿨·관통이 전부 좋아지는 이중 강화였다.
                // 관통이 무한이 된 대신 쿨타임을 늘려 "한 번 던지면 다 뚫지만 자주 못 던진다"로 만든다.
                skill.Cooldown *= 1.6f;
                skill.Scale += 0.3f;
                break;
            // R0(암살 연계) 1차: 여러 발을 **한 발로 모으는** 대신 그 한 발이 크게 아프다.
            // 발사 수가 1이 되므로 피해로 보상하지 않으면 진화하고 오히려 약해진다.
            case (ActiveSkillId.BasicAttack, 1, 2):
                skill.Damage *= 3f;
                break;
            case (ActiveSkillId.BasicAttack, 1, 3):
                skill.Damage *= 1.5f;
                break;
            // 낙뢰 R0 1차는 쿨감 25%가 전부였다 — 걷어내서 지금은 진화 공통 피해 도약만 남는다.
            case (ActiveSkillId.Lightning, 1, 3):
                skill.Damage *= 1.3f; // 피해량 30% 증가
                break;

            // path2(액티브 연계)
            // 독수리 R1 1차는 쿨감 40%가 전부였다 — 걷어내서 지금은 진화 공통 피해 도약만 남는다.

            // 스나이핑 (path1 T2 초과데미지연쇄·path2 T2 자동시전은 Fire/Update에서 실시간 처리)
            // ⚠️ 스나이핑은 (타겟 수 × 연사 수 × 피해)로 세 축이 전부 곱해지는 유일한 스킬이라
            //    피해 배수를 그대로 두면 최종 진화에서 혼자 압도적으로 세진다. 배수를 낮춰 축 하나를 눌러 둔다.
            case (ActiveSkillId.Sniping, 1, 1):
                skill.Damage *= 1.4f; // Route2 T1: 피해 40%
                break;
            case (ActiveSkillId.Sniping, 1, 3):
                skill.Damage *= 1.5f; // Route2 T3: 피해 50% (초과 피해가 커져 연쇄도 강해짐)
                break;
            // 스나이핑 Route3 1차는 쿨감 40%가 전부였다 — 걷어냈다. 어차피 이 루트는 1차부터
            // 스킬 쿨 대신 **고정 간격 자동시전**(3초)으로 도므로 쿨감이 발사에 닿지도 않았다.
            case (ActiveSkillId.Sniping, 2, 3):
                skill.Damage *= 1.2f; // Route3 T3: 자동시전 강화(피해 20%)
                break;

            // 호밍 미사일 (폭발 path1 T2는 FireHoming에서 실시간)
            case (ActiveSkillId.Homing, 1, 1):
                skill.Damage *= 1.3f; // Route2 T1: 미사일 피해 30%
                break;
            // R1(가속 연계, path2)의 쿨감(0.7배·0.8배)도 걷어냈다 — 개수·크기는 FireHoming이 실시간으로 맡는다.
            // 산탄(Shotgun)은 전부 FireShotgun/FireShotgunPellets에서 실시간 계산(영구 스탯 변경 없음)

            // 되감기 Route3에 있던 자체 쿨감 30%도 걷어냈다.
            // (T3의 **전역** 쿨감은 GlobalCooldownScale에서 실시간 처리 — 그건 진화가 스킬 쿨을 줄이는 게 아니라
            //  되감기 루트의 정체라서 그대로 둔다.)
            // 되감기 R0(다음 스킬 피해, path1)는 FireRewind에서 실시간 계산

            // 휘두르기 path1(힘 연계) 1차 = 타격 범위 1.45배. 범위가 곧 그대로 화력이라
            // 진화 공통 보너스(피해 1.5배·쿨 0.9배)까지 겹치면 혼자 압도적으로 세진다 —
            // 쿨타임 3배로 대가를 치르게 한다("한 방은 크지만 자주 못 쓴다").
            case (ActiveSkillId.Swing, 1, 2):
                skill.Cooldown *= 3f;
                ApplyHammerLook(shockRoute: false); // 망치가 커진다
                break;

            // 휘두르기 path2(회오리 연계) 1차 = 맵 끝까지 가는 충격파. 본체 판정 밖의 적까지 닿는
            // 사실상 사거리 무제한 공격이라, 1루트와 마찬가지로 쿨타임으로 대가를 치르게 한다.
            case (ActiveSkillId.Swing, 2, 2):
                skill.Cooldown *= 1.8f;
                ApplyHammerLook(shockRoute: true); // 충격파를 내는 망치로
                break;

            // 나머지 휘두르기 진화는 영구 스탯 변경이 없다 — 기절(path1)·충격파(path2) 전부
            // FireSwing/SwingRoutine에서 PathTier를 실시간으로 읽어 처리한다.
            // (진화 자체의 피해 1.5배·쿨 0.9배는 EvolutionRoutes가 공통으로 얹는다.)
        }
    }

    // 루트 잠금 조건은 EvolutionRoutes.RoutePrereq가 (스킬, 루트)별로 단독 소유한다(2026-08-06 개편).

    public static string GetActiveSkillName(ActiveSkillId id) =>
        Loc.TOr("skill.name." + id, id.ToString());

    // ── 스킬 종류(공격/버프/유틸) ── 대부분 공격, 산탄=버프(타수↑), 되감기=유틸(쿨 되감기).
    public static SkillCategory GetSkillCategory(ActiveSkillId id) => id switch
    {
        ActiveSkillId.Shotgun => SkillCategory.Buff,
        ActiveSkillId.Rewind => SkillCategory.Utility,
        _ => SkillCategory.Attack,
    };

    public static string GetSkillCategoryLabel(SkillCategory c) =>
        Loc.TOr("skill.cat." + c, c.ToString());

    // 레벨업 선택지처럼 액티브/패시브가 섞여 나오는 곳에서 쓰는 종류 배지.
    // ⚠️ 리치텍스트 마크업은 코드에 남기고 **낱말만** 표에 둔다 — 번역자가 태그를 깨뜨릴 자리를 만들지 않는다.
    public static string ActiveTypeBadge => "<size=68%><color=#FFD86B>[" + Loc.T("ui.badge.active") + "]</color></size>";
    public static string PassiveTypeBadge => "<size=68%><color=#9BE86B>[" + Loc.T("ui.badge.passive") + "]</color></size>";

    // 레벨업 선택지 제목: 배지를 이름 뒤에 붙인다 — "회오리 [액티브]" / "힘 [패시브]"
    // 🔴 종류 배지(공격/버프/유틸)는 **전부 뺐다**(사용자 결정 2026-09-02) — 액티브·패시브만 남긴다.
    //    `SkillCategory`는 남아 있지만 화면에는 안 나온다(에디터의 번역 수확 도구가 아직 쓴다).
    public static string GetActiveSkillTitleWithTags(ActiveSkillId id) =>
        $"{GetActiveSkillName(id)} {ActiveTypeBadge}";

    public static string GetPassiveSkillTitleWithTags(PassiveSkillId id) =>
        $"{GetPassiveSkillName(id)} {PassiveTypeBadge}";

    // 이미 장착한 스킬은 진화 후 이름(DisplayName)으로 표시한다.
    public static string GetActiveSkillTitleWithTags(EquippedSkill s) =>
        $"{s.DisplayName} {ActiveTypeBadge}";

    public static string GetPassiveSkillTitleWithTags(EquippedPassive p) =>
        $"{p.DisplayName} {PassiveTypeBadge}";

    // 일시정지(ESC) 요약용: 이 스킬이 1레벨 기본값 대비 레벨업으로 얼마나 강해졌는지 항목별로 정리.
    // (진화 효과는 PauseMenu가 PathTier 제목으로 따로 표시하므로 여기선 레벨업 성장분만 다룬다)
    public static List<string> DescribeLevelUpGains(EquippedSkill s)
    {
        var lines = new List<string>();

        float baseDmg = GetDefaultDamage(s.Id);
        if (baseDmg > 0f)
        {
            int dmgPct = Mathf.RoundToInt((s.Damage / baseDmg - 1f) * 100f);
            if (dmgPct != 0) lines.Add(Loc.F("skill.gain.dmg", (dmgPct > 0 ? "+" : "") + dmgPct, baseDmg.ToString("0.#"), s.Damage.ToString("0.#")));
        }

        float baseCd = GetDefaultCooldown(s.Id);
        if (baseCd > 0f)
        {
            int cdPct = Mathf.RoundToInt((1f - s.Cooldown / baseCd) * 100f);
            if (cdPct != 0) lines.Add(Loc.F("skill.gain.cd", (cdPct > 0 ? "-" : "+") + Mathf.Abs(cdPct), baseCd.ToString("0.#"), s.Cooldown.ToString("0.#")));
        }

        if (s.ProjectileSpeedMultiplier > 1.0001f)
            lines.Add(Loc.F("skill.gain.projSpeed", Mathf.RoundToInt((s.ProjectileSpeedMultiplier - 1f) * 100f)));
        if (s.ExtraPierce > 0) lines.Add(Loc.F("skill.gain.pierce", s.ExtraPierce));
        if (s.ExtraProjectiles > 0) lines.Add(Loc.F("skill.gain.projCount", s.ExtraProjectiles));
        if (s.ExtraTargets > 0) lines.Add(s.Id == ActiveSkillId.Orb ? Loc.F("skill.gain.targets.orb", s.ExtraTargets) : Loc.F("skill.gain.targets", s.ExtraTargets));
        if (s.TickIntervalMult < 0.9999f) lines.Add(Loc.F("skill.gain.tickRate", Mathf.RoundToInt((1f - s.TickIntervalMult) * 100f)));
        if (s.ProcChanceBonus > 0f) lines.Add(Loc.F("skill.gain.procChance", Mathf.RoundToInt(s.ProcChanceBonus * 100f)));
        if (s.ExtraWhirlwindDuration > 0f) lines.Add(Loc.F("skill.gain.duration", s.ExtraWhirlwindDuration.ToString("0.#")));
        if (s.Scale > 1.0001f) lines.Add(Loc.F("skill.gain.scale", Mathf.RoundToInt((s.Scale - 1f) * 100f)));
        if (s.Id == ActiveSkillId.Rewind) lines.Add(Loc.F("skill.gain.rewind", s.RewindAmount.ToString("0.#")));
        if (s.Id == ActiveSkillId.Homing && s.GrowthStacks > 0)
            lines.Add(Loc.F("skill.gain.growth", s.GrowthStacks));

        return lines;
    }

    public static string GetPassiveSkillName(PassiveSkillId id) =>
        Loc.TOr("passive.name." + id, id.ToString());

    // 한 티어 = 한 효과. 표시 문구는 번역 표가 소유한다(아래 DescribePathEffect/GetPathTierTitle).
    public string GetPathEffectText(ActiveSkillId id, int path, int tier)
    {
        return DescribePathEffect(id, path, tier);
    }

    public string GetPathTitleText(ActiveSkillId id, int path, int tier)
    {
        return GetPathTierTitle(id, path, tier);
    }

    // 🔴 진화 설명은 **일부러 수치를 안 쓴다.** BTD6 파라곤 설명처럼 그림만 던져서,
    //    "고르면 뭐가 나올까"라는 호기심으로 루트를 고르게 하는 게 목적이다.
    //    (실제 수치는 ApplyPathTierEffect와 각 Fire*에 있다 — 밸런스는 거기서 확인할 것.)
    //    티어1 카드에는 legacy 1·2가 **두 줄로 함께** 뜨므로, 두 문장이 이어 읽히게 쓸 것.
    // 문장은 번역 표(`Assets/Localization/Tables/Game`)가 소유한다. 키 = evo.active.desc.{스킬}.{루트}.{티어}.
    // 정의가 없는 조합은 빈 문자열 — 옛 `_ => ""` 분기와 같다.
    public static string DescribePathEffect(ActiveSkillId id, int path, int tier) =>
        Loc.TOr($"evo.active.desc.{id}.{path}.{tier}", "");

    // 진화 카드에 붙는 짧은 제목. 설명과 마찬가지로 **기능명이 아니라 별명**이다 — 뭘 하는지는 눌러 봐야 안다.
    public static string GetPathTierTitle(ActiveSkillId id, int path, int tier) =>
        Loc.TOr($"evo.active.title.{id}.{path}.{tier}", "");

    private void TryUseSkill(EquippedSkill skill)
    {
        if (globalCooldownTimer > 0f || skill.CooldownTimer > 0f) return;

        // 타격 기준 치명타: 캐스트 시점엔 확률만 확정하고, 실제 치명타 여부는 각 데미지 이벤트(투사체 명중/틱)마다 개별적으로 굴린다.
        float critChance = GetCritChance(skill);
        float damage = ComputeBaseDamage(skill);

        switch (skill.Id)
        {
            case ActiveSkillId.BasicAttack:
                if (!FireBasicAttack(skill, damage, critChance, allowBonusShot: true)) return;
                break;
            case ActiveSkillId.Whirlwind:
                FireWhirlwind(damage, critChance, skill);
                break;
            case ActiveSkillId.Orb:
                FireOrb(damage, critChance, skill);
                break;
            case ActiveSkillId.Lightning:
                // R0(되감기 연계, path0) = **버프 중첩**. 원래 도달 불가능한 path2에 잠들어 있던 효과를 여기로 가져왔다
                // (2026-08-06 명세: "기존 낙뢰 버프 중첩 Path 다시 가져와서 쓰기"). 지속시간이 늘어 스택이 겹치고,
                // 그 스택 수만큼 전체 공격 피해가 오른다 — 낙뢰가 "깔아두는 버프"라는 정체성이 여기서 완성된다.
                float baseDuration = BaseDuration(skill.Id, 10f) * (skill.PathTier[0] >= 2 ? 1.3f : 1f) * MetaBonuses.DurationMult;
                // 기존 스택을 지우지 않고 새로 추가한다 — 평소엔 쿨타임이 지속시간보다 길어 이전 스택이 이미 만료된 상태지만,
                // 지속시간이 늘어나 있으면 새 캐스트가 기존 스택 위에 쌓인다.
                LightningStorm.AddStack(baseDuration);
                LightningStorm.ProcChance = LightningStorm.BaseProcChance + skill.ProcChanceBonus;
                LightningStorm.ProcDamage = damage;
                // 스킬트리 "낙뢰 버프 중첩"(thunder_Stack)이 진화 R0 T2와 같은 문을 연다 — 둘 중 하나만 있어도 켜진다.
                LightningStorm.StackDamageEnabled = skill.PathTier[0] >= 2 || MetaBonuses.ThunderStackable;
                LightningStorm.StackDamageBonusPerStack = skill.PathTier[0] >= 3 ? 0.25f : LightningStorm.BaseStackDamageBonus;
                // R1(힘 연계, path1) = **피뢰침**. 맵 중앙에 꽂아 6초간 1초마다 넓은 범위를 내리친다.
                if (skill.PathTier[1] >= 2)
                    StartCoroutine(LightningRodRoutine(damage, critChance, empowered: skill.PathTier[1] >= 3));
                RefreshLightningBuffDisplay();
                break;
            case ActiveSkillId.EagleDrop:
                StartCoroutine(EagleDropRoutine(damage, critChance, skill));
                break;
            case ActiveSkillId.Sniping:
                if (!FireSniping(damage, critChance, skill)) return; // 조준할 적이 없으면 캐스트 실패(쿨 소모 안 함)
                break;
            case ActiveSkillId.Homing:
                if (!FireHoming(damage, critChance, skill)) return;
                break;
            case ActiveSkillId.Shotgun:
                FireShotgun(damage, critChance, skill);
                break;
            case ActiveSkillId.Rewind:
                FireRewind(skill);
                break;
            case ActiveSkillId.Swing:
                FireSwing(damage, critChance, skill);
                break;
            case ActiveSkillId.GrapeToss:
                FireGrapeToss(damage, skill);
                break;
        }

        // 스킬트리 "되감기: 전역 쿨타임 미발동" — 되감기만 GCD를 안 건다(다른 스킬을 바로 이어 쓸 수 있다).
        if (!(skill.Id == ActiveSkillId.Rewind && MetaBonuses.RewindSkipsGlobalCooldown))
            globalCooldownTimer = GlobalCooldown * GlobalCooldownScale(); // 되감기 Route3 T3: 전역 쿨타임 절반
        // 스나이핑 자동시전(path2 T2+)은 스킬 쿨타임 대신 고정 간격(T2=3초, T3=1.5초)으로 발동
        // (오브 설치기의 긴 전용 쿨은 오브 R1이 추적 오브 무리로 바뀌며 사라졌다 — 이제 평범한 스킬 쿨을 쓴다)
        float baseCd = (skill.Id == ActiveSkillId.Sniping && skill.PathTier[2] >= 2) ? (skill.PathTier[2] >= 3 ? 1.5f : 3f)
            : skill.Cooldown;
        // 암살 연계(path1) 진화: 한 발이 무거워진 대가로 기본 쿨이 +3초. 감소율이 곱해지기 전에 더한다
        // (쿨감을 쌓으면 이 3초도 같이 줄어든다 — 다른 쿨 강화와 같은 층에 두는 게 맞다).
        if (skill.Id == ActiveSkillId.BasicAttack && skill.PathTier[1] >= 2)
            baseCd += AssassinArrowExtraCooldown;
        // 스킬트리 "호밍: 기본 쿨 -1초" — 감소**율**이 곱해지기 전의 기본 쿨에서 먼저 뺀다.
        if (skill.Id == ActiveSkillId.Homing && MetaBonuses.HomingCooldownCut > 0f)
            baseCd = Mathf.Max(GlobalCooldown, baseCd - MetaBonuses.HomingCooldownCut);
        float cdMult = MetaBonuses.CooldownMult;
        // 스킬트리 "신속한 회오리": 회오리는 쿨타임 감소분(1-CooldownMult)을 1.5배로 받음
        if (skill.Id == ActiveSkillId.Whirlwind && MetaBonuses.WhirlwindCooldownBonus)
            cdMult = Mathf.Max(0.05f, 1f - 1.5f * (1f - MetaBonuses.CooldownMult));
        // 리프레쉬 연계 path3 T1: 버프류 스킬(산탄·낙뢰) 쿨타임 감소
        if (PlayerPassives.BuffSkillCooldownMult < 1f && IsBuffSkill(skill))
            cdMult *= PlayerPassives.BuffSkillCooldownMult;
        // 패시브 "가속": 전 스킬 쿨타임 감소. 스킬트리 전역 쿨감과 같은 축이라 곱해서 들어간다.
        // 스킬트리 보너스는 리프레쉬와 같은 방식으로 **가속을 보유했을 때만** 얹힌다.
        if (passives != null && passives.HasPassive(PassiveSkillId.Accel))
        {
            float cut = PlayerPassives.AccelCooldownReduction + MetaBonuses.AccelCooldownBonus;
            if (cut > 0f) cdMult *= Mathf.Max(0.05f, 1f - cut);
        }
        skill.CooldownTimer = baseCd * cdMult;

        // 쿨타임 초기화. 리프레쉬는 폐지됐지만 **가속 진화 path1**이 RefreshChance를 물려받아 켠다 —
        // 그래서 보유 패시브가 아니라 **확률값 자체**를 게이트로 쓴다(둘 중 무엇이 켰든 똑같이 동작).
        if (passives != null && PlayerPassives.RefreshChance > 0f && Random.value < PlayerPassives.RefreshChance)
        {
            skill.CooldownTimer = 0f;
            OnRefreshProc?.Invoke();
            // 리프레쉬 연계 path1: 쿨타임 초기화시마다 체력(또는 초과체력) 회복
            if (health != null && PlayerPassives.RefreshHealOnResetAmount > 0f)
                health.AddOverheal(Mathf.RoundToInt(PlayerPassives.RefreshHealOnResetAmount));
        }
    }

    // 스테이지가 넘어갈 때 GameManager가 호출 — 모든 스킬 쿨타임 초기화
    public void ResetAllCooldowns()
    {
        globalCooldownTimer = 0f;
        foreach (EquippedSkill s in equippedSkills)
            s.CooldownTimer = 0f;
    }

    // 방어 진화 path1(휘두르기 연계): 피격 시 휘두르기를 쿨타임과 무관하게 한 번 자동 발동한다.
    // 휘두르기 **보유**가 그 루트의 잠금 조건이라(EvolutionRoutes.RoutePrereq) 없을 일은 없지만,
    // 캐릭터 변경 등으로 빠졌을 때 조용히 무시하도록 방어적으로 확인한다.
    public void TriggerAutoSwing(float damageMult)
    {
        EquippedSkill swing = equippedSkills.FirstOrDefault(s => s.Id == ActiveSkillId.Swing);
        if (swing == null) return;
        FireSwing(ComputeBaseDamage(swing) * damageMult, GetCritChance(swing), swing);
    }

    // 리프레쉬 연계(패시브 path2)에서 낙뢰 발동 시에도 호출됨
    public void ReduceAllCooldowns(float amount)
    {
        foreach (EquippedSkill s in equippedSkills)
            s.CooldownTimer = Mathf.Max(0f, s.CooldownTimer - amount);
    }

    // 타격 기준 치명타: 여기선 "이번 캐스트에 적용될 확률"만 정하고, 실제 발동 여부는 각 데미지 이벤트에서 개별적으로 굴린다.
    private float GetCritChance(EquippedSkill skill)
    {
        // 암살 연계 path1 T1: 기본공격 전용 추가 치명타 확률
        float chance = skill.Id == ActiveSkillId.BasicAttack && skill.PathTier[1] >= 1 ? 0.15f : 0f;
        if (passives != null && passives.HasPassive(PassiveSkillId.Assassinate))
            chance += PlayerPassives.AssassinateCritChance;
        // 암살 연계 path2(스나이핑): 쿨타임이 긴 스킬은 **확정 치명타**다.
        // 상한(MaxCritChance 70%)보다 먼저 빠져나간다 — "항상 급소"가 이 진화의 정체성이라 상한에 눌리면 안 된다.
        if (PlayerPassives.AssassinateSlowSkillCritCooldown > 0f
            && skill.Cooldown >= PlayerPassives.AssassinateSlowSkillCritCooldown) return 1f;
        chance += MetaBonuses.CritBonus; // 메타 "치명타" 업그레이드(전역 가산)
        // 스킬트리 스킬별 치명타 강화(산탄·스나이핑). ⚠️ 상한(MaxCritChance 70%) 안쪽에서 더해진다 —
        // 다른 치명타 원천이 이미 높으면 +30%p가 그대로 안 들어간다.
        if (skill.Id == ActiveSkillId.Shotgun) chance += MetaBonuses.ShotgunCritBonus;
        else if (skill.Id == ActiveSkillId.Sniping) chance += MetaBonuses.SnipingCritBonus;
        return Mathf.Min(chance, MaxCritChance);
    }

    // 🔴 스킬 **하나**를 받는다 — 스킬트리 "힘(쿨 5초 이상)"·"가속(쿨 4초 이하)" 강화가 그 스킬의
    //    기본 쿨(skill.Cooldown)을 봐야 해서. 판정 기준은 쿨감이 붙기 전의 **기본 쿨**이다
    //    (쿨감으로 경계를 넘나들면 같은 스킬의 피해가 판 중간에 요동친다).
    private float ComputeBaseDamage(EquippedSkill skill)
    {
        ActiveSkillId skillId = skill.Id;
        // 힘 패시브 계열 피해 배율은 하나의 덧셈 풀로 합친다 — path0(전 스킬 공통)과 path2(Q 슬롯 전용)를
        // 곱연산으로 각각 겹쳐 쌓으면 그 스킬에서 배율이 폭발한다. 같은 풀에서 더한 뒤 한 번만 곱한다.
        float damageMultiplier = passiveDamageMultiplier;
        // 힘 연계 path2(패시브): **Q키에 할당된 스킬** 전용 추가 피해량 (덧셈 합류).
        // Q = 가장 먼저 얻은 스킬 = equippedSkills[0] (AcquireSkill이 SlotKeys를 순서대로 붙인다).
        if (PlayerPassives.FirstSlotDamageMultiplierBonus > 0f && equippedSkills.Count > 0 && equippedSkills[0].Id == skillId)
            damageMultiplier += PlayerPassives.FirstSlotDamageMultiplierBonus;

        // 스킬트리 "힘" 강화: 쿨 5초 이상 스킬엔 힘의 몫이 한 번 더 들어간다(= 힘 효과 2배).
        if (MetaBonuses.StrengthSlowSkillDouble && strengthDamageBonus > 0f
            && skill.Cooldown >= StrengthDoubleCooldown)
            damageMultiplier += strengthDamageBonus;

        float damage = skill.Damage * damageMultiplier;

        // 스킬트리 "가속" 강화: 쿨 4초 이하 스킬 피해 +30%. 쿨감(AccelCooldownBonus)과 같은 방식으로
        // **가속 패시브를 보유했을 때만** 얹힌다.
        if (MetaBonuses.AccelFastSkillDamage && skill.Cooldown <= AccelFastSkillCooldown
            && passives != null && passives.HasPassive(PassiveSkillId.Accel))
            damage *= 1f + AccelFastSkillDamageBonus;

        // 낙뢰 연계 path2 T3: 낙뢰 버프 중첩당 전체 공격 피해량 증가
        if (LightningStorm.StackDamageEnabled && LightningStorm.ActiveStackCount > 0)
            damage *= 1f + LightningStorm.StackDamageBonusPerStack * LightningStorm.ActiveStackCount;

        // 건강 연계 path1(패시브): 최대체력에 비례한 전체 피해량 증가
        if (health != null && PlayerPassives.HealthDamagePerHp > 0f)
            damage *= 1f + PlayerPassives.HealthDamagePerHp * health.MaxHealth;

        // 되감기 Route2: 되감기 직후 사용하는 스킬(되감기 제외)의 피해를 1회 증가시킨다.
        if (skillId != ActiveSkillId.Rewind && nextSkillDamageBonus > 0f)
        {
            damage *= 1f + nextSkillDamageBonus;
            nextSkillDamageBonus = 0f;
        }

        return damage;
    }

    private bool FireBasicAttack(EquippedSkill skill, float damage, float critChance, bool allowBonusShot)
    {
        // 적이 화면에 없어도 발사는 되어야 한다(허공에 쏘더라도 키 입력에 무반응인 건 고장난 것처럼 느껴짐).
        // target은 조준에는 안 쓰이고(투사체는 항상 정면으로 직선 발사) 과거엔 "쏠 게 있는지" 게이트로만 쓰였다.
        int pierce = skill.ExtraPierce + MetaBonuses.ArrowExtraPierce; // 레벨업 고유 강화 + 스킬트리 "화살 관통"

        // R0(암살 연계, path1) = **개쎈 화살 한 발**. 여러 발 쏘던 것을 한 발로 모으고 관통을 무한으로 준다
        // (방패 블루베리는 그래도 막는다 — Projectile이 BlocksProjectiles에서 끊는다. 2026-08-06 명세 그대로).
        if (skill.PathTier[1] >= 2) pierce = int.MaxValue;

        // ⚠️ 아래 두 상수는 **암살 연계(path1) 진화 전체**에 걸린다 — 1차(암살 사격)·2차(처형 사격) 둘 다.
        //    한 발이 무거워진 만큼 느리게 날고 쉬는 시간도 길다(2026-09-07 사용자 요청).

        // R1(독수리 연계, path2) T2+ = 기본공격이 **통째로 화살비로 바뀐다.** 정면 화살은 나가지 않는다.
        // 예전엔 정면 화살과 비가 같이 쏟아져 "중복 출력"으로 보였다(8/24 플레이스루).
        // ⚠️ 그 대가로 ExtraPierce가 이 진화에선 죽은 스탯이 된다 — 사용자 결정.
        if (ArrowRainReplacesShot(skill))
        {
            StartCoroutine(ArrowRainRoutine(skill, damage, critChance, ArrowRainWaveCount(skill)));
        }
        else
        {
            // 첫 발은 즉시(입력 반응성), 추가 발사는 시간차를 두고 연사한다.
            SpawnBasicAttackProjectile(skill, damage, critChance, pierce, 0f, allowBonusShot);
            if (BasicAttackBurstCount(skill) > 0)
                StartCoroutine(BasicAttackBurst(skill, damage, critChance, pierce, allowBonusShot));
        }

        animator.SetTrigger("Attack");
        return true;
    }

    // ── 화살 R1(독수리 연계, path2): 화살비 ──
    // 화면 위쪽에서 비스듬히(왼쪽 아래로) 쏟아진다. 각 화살은 **관통이 없다**(명세) — 한 마리 맞히고 사라진다.
    // ⚠️ Projectile은 자기 로컬 left로만 날아간다 → 방향은 회전으로 준다. 스프라이트도 같이 기울어 그림이 맞는다.
    private const float ArrowRainAngle = 50f;          // Vector2.left 기준 시계 방향 = 왼쪽 아래
    private const float ArrowRainWaveInterval = 0.35f;
    private const int ArrowRainArrowsPerWave = 10;
    private const float ArrowRainDamageRatio = 0.5f;   // 발수가 많아 발당 피해는 낮춘다

    // 떨어지면서 빨라진다 — 하늘에서 막 놓인 듯 느리게 시작해 바닥에 꽂힐 즈음 가장 빠르다.
    // 등속으로 내리면 화살이 "떠내려오는" 것처럼 보여 무게가 없었다(8/24 플레이스루: 낙하 속도감 필요).
    private const float ArrowRainStartSpeed = 0.55f;    // 시작 속도 배율
    private const float ArrowRainAcceleration = 3.2f;   // 초당 배율 증가 — 낙하 약 0.64초 동안 5.5 → 26유닛/초

    // 레벨업 "추가 투사체"는 화살비에선 **비가 오는 시간**으로 읽는다(정면 화살이 없어 발수로 쓸 데가 없다).
    // Prog_BasicAttack이 만렙까지 추가 투사체를 3회(레벨 2·6·10) 주므로 2웨이브 → 최대 5(T3면 6)웨이브가 된다.
    private const int ArrowRainBaseWaves = 2;
    private const int ArrowRainMaxWaves = 6;

    // T2부터 기본공격이 화살비로 교체된다. 이 조건이 곧 "정면 화살이 안 나간다"는 뜻이라 한 곳에 모아 둔다.
    // ⚠️ **기본공격 전용 진화다.** Id를 안 보면 path2를 2티어까지 올린 **다른 스킬**까지 걸려서
    //    레벨업 카드에 "화살비 +N차례"가 뜬다(호밍 "소형 미사일 다발"에서 실제로 그랬다).
    private static bool ArrowRainReplacesShot(EquippedSkill skill) =>
        skill.Id == ActiveSkillId.BasicAttack && skill.PathTier[2] >= 2;

    private static int ArrowRainWaveCount(EquippedSkill skill) =>
        Mathf.Min(ArrowRainBaseWaves + (skill.PathTier[2] >= 3 ? 1 : 0) + skill.ExtraProjectiles,
                  ArrowRainMaxWaves);

    private IEnumerator ArrowRainRoutine(EquippedSkill skill, float damage, float critChance, int waves)
    {
        if (basicAttackProjectilePrefab == null) yield break;

        Camera cam = Camera.main;
        if (cam == null) yield break;
        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;
        float centerX = cam.transform.position.x;
        float topY = cam.transform.position.y + halfHeight;

        float rainDamage = damage * ArrowRainDamageRatio;

        for (int w = 0; w < waves; w++)
        {
            for (int i = 0; i < ArrowRainArrowsPerWave; i++)
            {
                // 화면 폭에 고르게 흩되 매 발 조금씩 흔들어 격자처럼 보이지 않게 한다.
                float t = (i + Random.Range(-0.3f, 0.3f)) / (ArrowRainArrowsPerWave - 1);
                float x = centerX + Mathf.Lerp(-halfWidth, halfWidth, Mathf.Clamp01(t));
                Vector3 spawn = new Vector3(x, topY + Random.Range(0.5f, 2f), 0f);

                GameObject obj = Instantiate(basicAttackProjectilePrefab, spawn, Quaternion.Euler(0f, 0f, ArrowRainAngle));
                obj.transform.localScale *= skill.Scale;
                Projectile p = obj.GetComponent<Projectile>();
                if (p == null) continue;
                p.Damage = rainDamage;
                p.CritChance = critChance;
                p.SpeedMultiplier = skill.ProjectileSpeedMultiplier * ArrowRainStartSpeed;
                p.Acceleration = skill.ProjectileSpeedMultiplier * ArrowRainAcceleration;
                p.PierceRemaining = 0;   // 명세: 화살비의 각 화살은 관통 없음
                p.CanHitFlying = true;   // 위에서 떨어지므로 비행 적을 자연히 지나간다
            }
            yield return new WaitForSeconds(ArrowRainWaveInterval);
        }
    }

    // R0(암살 연계)은 **한 발**로 모은다 — 큰 화살 하나가 줄을 통째로 뚫는 게 정체성이라 연사가 있으면 안 된다.
    // (R1(독수리 연계)은 아예 이 함수를 안 탄다 — FireBasicAttack이 화살비로 갈아타 정면 화살을 안 쏜다.)
    // ⚠️ 영구 스탯을 깎지 않고 매 캐스트 실시간으로 계산한다 — 진화 후 레벨업으로 발수를 더 얻어도 그대로 유지된다.
    private static int BasicAttackBurstCount(EquippedSkill skill) =>
        skill.PathTier[1] >= 2 ? 0 : skill.ExtraProjectiles;

    // 추가 발사체를 "두두두둑" 쏟아낸다 — 발사마다 간격을 두고, 세로로 위·아래 번갈아 조금씩 어긋나게.
    // 예전엔 전부 같은 프레임에 0.4씩 위로 쌓아 올려 한 덩어리로 보였다(여러 발 맞는 게 안 보임).
    private IEnumerator BasicAttackBurst(EquippedSkill skill, float damage, float critChance, int pierce, bool allowBonusShot)
    {
        int count = BasicAttackBurstCount(skill); // 캐스트 시점 값으로 고정(도중에 레벨업해도 이번 연사는 그대로)
        for (int i = 1; i <= count; i++)
        {
            yield return new WaitForSeconds(BalanceConstants.BasicAttackBurstInterval);
            // +0.13 / -0.13 / +0.26 / -0.26 … 위아래로 번갈아 벌어진다
            float sign = i % 2 == 1 ? 1f : -1f;
            float magnitude = Mathf.CeilToInt(i * 0.5f) * BalanceConstants.BasicAttackBurstYOffset;
            SpawnBasicAttackProjectile(skill, damage, critChance, pierce, sign * magnitude, allowBonusShot);
        }
    }

    private void SpawnBasicAttackProjectile(EquippedSkill skill, float damage, float critChance, int pierce, float verticalOffset, bool allowBonusShot)
    {
        // 정면 화살은 비행 적을 때리지 못한다. 비행 타격은 화살비(위에서 떨어짐)와 추적 화살(대상 지정)이 맡는다.
        GameObject obj = Instantiate(basicAttackProjectilePrefab, transform.position + Vector3.left * 0.6f + Vector3.down * 0.25f + Vector3.up * verticalOffset, Quaternion.identity);
        obj.transform.localScale *= skill.Scale;
        Projectile projectile = obj.GetComponent<Projectile>();
        projectile.Damage = damage;
        projectile.CritChance = critChance;
        projectile.SpeedMultiplier = skill.ProjectileSpeedMultiplier
            * (skill.PathTier[1] >= 2 ? AssassinArrowSpeedMult : 1f);
        projectile.PierceRemaining = pierce;
        projectile.CanHitFlying = false;
        if (skill.PathTier[1] >= 2) ApplyEvolvedArrowSprite(obj, skill.PathTier[1] >= 3);

        // R0(암살 연계, path1): 이 화살이 **치명타로 때린 적마다** 기본 화살이 따로 날아가 그 적만 노린다.
        // 관통 무한이라 한 발이 여러 적에게 치명타를 낼 수 있고, 그때마다 각각 따라붙는다(명세 그대로).
        // 🔴 **2차 진화(PathTier 3)부터다**(2026-09-07 사용자 결정). 1차(PathTier 2)는 추가 투사체 없이
        //    "개쎈 화살 한 발"만으로 간다 — 그것만으로 충분히 세고, 1차에 얹으니 잘 안 보였다.
        //    ⚠️ `TargetPathTier`가 1차→2, 2차→3으로 매핑한다. `>= 2`로 쓰면 **1차부터** 켜진다.
        bool critChaseArrow = allowBonusShot && skill.PathTier[1] >= 3;
        const int chaseArrows = 2;

        projectile.OnHitBonus = (hitEnemy, hitCrit) =>
        {
            if (critChaseArrow && hitCrit && hitEnemy != null)
                for (int i = 0; i < chaseArrows; i++)
                    SpawnChasingArrow(skill, hitEnemy, damage, critChance);
        };
    }

    // 진화한 화살 그림으로 교체. 프리팹의 SpriteRenderer가 자식에 있을 수도 있어 자식까지 훑는다.
    // 2차 그림이 안 배선돼 있으면 1차 그림으로, 그것도 없으면 아무것도 안 한다(원본 화살로 그대로 날아간다).
    private void ApplyEvolvedArrowSprite(GameObject projectileObj, bool tier2)
    {
        Sprite[] frames = tier2 && evolvedArrowFramesTier2 != null && evolvedArrowFramesTier2.Length > 0
            ? evolvedArrowFramesTier2
            : evolvedArrowFrames;
        if (frames == null || frames.Length == 0) return;

        SpriteRenderer sr = projectileObj.GetComponentInChildren<SpriteRenderer>();
        if (sr == null) return;
        sr.sprite = frames[0];
        if (frames.Length < 2) return; // 한 장뿐이면 굳이 플립북을 안 붙인다

        // 화살은 명중하거나 화면 밖으로 나가서 사라지므로 **루프**로 돌린다
        // (비루프면 SpriteFlipbook이 다 재생한 뒤 풀에 반납하려 드는데, 이건 Instantiate로 만든 오브젝트다).
        SpriteFlipbook fb = sr.GetComponent<SpriteFlipbook>();
        if (fb == null) fb = sr.gameObject.AddComponent<SpriteFlipbook>();
        fb.Play(frames, evolvedArrowFps, true);
    }

    // R0의 따라붙는 기본 화살 — **관통은 없다**(그 대상만 때리고 사라진다).
    // 2026-08-26 사용자 요청으로 셋이 바뀌었다: ① 딸기가 아니라 **암살 화살 뒤쪽**에서 나오고
    // ② **포물선**을 그리며 날아가고 ③ 일반 화살의 **절반 크기**다.
    // 암살 연계(path1) 진화 화살은 느리게 날고 쿨이 길다 — "한 발이 무겁다"를 손으로 느끼게 한다.
    private const float AssassinArrowSpeedMult = 0.6f;   // 정면 화살 속도의 60%
    private const float AssassinArrowExtraCooldown = 3f; // 기본 쿨에 그대로 더한다(감소율보다 먼저)

    private const float ChasingArrowDamageRatio = 0.5f;
    private const float ChasingArrowScale = 0.65f;         // 0.5에서 30% 키움(2026-09-07 — 작아서 안 보였다)
    private const float ChasingArrowRiseOffset = 0.35f;    // 딸기의 발사 지점보다 살짝 위에서 나간다
    // ⚠️ 위로 붕 뜨는 로브가 아니다 — **호밍 미사일처럼 가로로 부드럽게 휘어 들어가는** 궤적이다(사용자 지시).
    //    그래서 처음엔 목표를 정조준하지 않고 **수평으로** 내보내고, Steer가 매 프레임 조금씩 끌어당긴다.
    //    생성 지점이 명중 지점보다 조금 위(ChasingArrowRiseOffset)라 그 낙차만큼 완만한 곡선이 생긴다.
    //    회전 속도가 곧 곡률이다: 낮출수록 크게 휘고, 높이면 거의 직선이 된다.
    private const float ChasingArrowTurnDegPerSec = 200f;

    private void SpawnChasingArrow(EquippedSkill skill, Enemy target, float damage, float critChance)
    {
        if (basicAttackProjectilePrefab == null) return;

        // 🔴 치명타는 **대개 그 적을 죽인다** — 암살 사격은 한 발이 무거운 데다 치명타 배율(기본 3배)까지 얹힌다.
        //    그리고 Enemy.TakeDamage는 같은 호출 안에서 동기적으로 isDead를 세우므로,
        //    Projectile이 OnHitBonus를 부를 때는 이미 `target.IsAlive == false`다.
        //    예전엔 여기서 그대로 return해서 **치명타가 떠도 추격 화살이 한 번도 안 보였다.**
        //    → 대상이 죽었으면 그 자리에서 가장 가까운 산 적으로 갈아탄다(2026-08-25 사용자 결정).
        Vector3 hitPos = target != null ? target.transform.position : transform.position;
        if (target == null || !target.IsAlive)
            target = Projectile.NearestLivingEnemy(hitPos, canHitFlying: true);
        if (target == null) return; // 화면에 산 적이 하나도 없을 때만 안 쏜다

        // 🔴 **딸기에게서** 나간다(2026-09-07 사용자 결정 — 명중 지점 뒤에서 짧게 나가니 안 보였다).
        //    화면을 가로질러 적을 쫓아가므로 호밍 궤적이 눈에 들어온다. 발사 지점은 정면 화살과 같은 총구.
        Vector3 origin = transform.position + Vector3.left * 0.6f + Vector3.down * 0.25f
                         + Vector3.up * ChasingArrowRiseOffset;
        Vector2 toTarget = (Vector2)target.transform.position - (Vector2)origin;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        // 처음엔 목표를 정조준하지 않고 **수평으로** 내보낸다. 세로 차이는 Steer가 부드럽게 메운다.
        Vector2 aim = toTarget.normalized;
        Vector2 launch = new Vector2(aim.x >= 0f ? 1f : -1f, 0f);

        // Projectile은 자기 로컬 left로 날아간다 → left가 발사 방향을 향하도록 회전시킨다.
        float angle = Mathf.Atan2(launch.y, launch.x) * Mathf.Rad2Deg + 180f;

        GameObject obj = Instantiate(basicAttackProjectilePrefab, origin, Quaternion.Euler(0f, 0f, angle));
        obj.transform.localScale *= skill.Scale * ChasingArrowScale;
        Projectile p = obj.GetComponent<Projectile>();
        if (p == null) return;
        p.Damage = damage * ChasingArrowDamageRatio;
        p.CritChance = critChance;
        p.SpeedMultiplier = skill.ProjectileSpeedMultiplier;
        p.PierceRemaining = 0;
        p.CanHitFlying = target.RequiresAntiAir; // 비행 적을 노리고 쏜 화살이면 그 적은 맞힐 수 있어야 한다

        // 날아가는 동안에도 대상이 죽으면 다른 적으로 갈아탄다(호밍 미사일과 같은 장치).
        p.Homing = true;
        p.HomingTarget = target;
        p.TurnDegPerSec = ChasingArrowTurnDegPerSec;
    }

    // source: 데미지 집계에 어느 스킬로 잡힐지. 화살 R1은 화살, 스나이핑 R0은 스나이핑으로 잡혀야 한다.
    private void SpawnMiniEagleSpread(Enemy primary, float damage, float critChance, float scale, int maxTargets, ActiveSkillId source = ActiveSkillId.BasicAttack)
    {
        StartCoroutine(MiniEagleBonus(primary, damage, critChance, scale, source));
        if (maxTargets <= 1) return;

        IEnumerable<Enemy> nearby = FindObjectsByType<Enemy>(FindObjectsSortMode.None)
            .Where(e => e != null && e != primary && Vector2.Distance(primary.transform.position, e.transform.position) <= 6f)
            .OrderBy(e => Vector2.Distance(primary.transform.position, e.transform.position))
            .Take(maxTargets - 1);

        foreach (Enemy e in nearby)
            StartCoroutine(MiniEagleBonus(e, damage, critChance, scale, source));
    }

    // ── 스나이핑: 가장 체력 높은 적(들)을 5회씩 저격 ──
    private bool FireSniping(float damage, float critChance, EquippedSkill skill)
    {
        int targets = 1 + skill.ExtraTargets; // 레벨업 주 성장축: 동시 저격 대상 수
        if (MetaBonuses.SnipingExtraTarget) targets += 1; // 스킬트리 "한 발에 두 놈": +1 타겟

        List<Enemy> chosen = FindObjectsByType<Enemy>(FindObjectsSortMode.None)
            .Where(e => e != null)
            .OrderByDescending(e => e.CurrentHealth)
            .Take(targets)
            .ToList();
        if (chosen.Count == 0) return false; // 조준할 적 없음 → 캐스트 실패

        // R0(독수리 연계, path1) = 저격이 꽂힌 자리 **주변 적에게 독수리를 떨군다**(2026-08-06 명세).
        // 화살 R1의 미니 독수리 확산과 같은 장치를 재사용한다 — 단일 대상기였던 스나이핑에 광역이 붙는다.
        bool eagleSplash = skill.PathTier[1] >= 2;
        int eagleTargets = skill.PathTier[1] >= 3 ? 8 : 4;
        float eagleRatio = skill.PathTier[1] >= 3 ? 0.6f : 0.4f;

        animator.SetTrigger("Attack");
        int shots = SnipingBaseShots + skill.ExtraProjectiles; // 레벨업 보조축: 대상당 연사 수
        foreach (Enemy target in chosen)
            StartCoroutine(SnipeTarget(target, damage, critChance, eagleSplash, eagleRatio, eagleTargets, shots, skill.Scale, skill.EvolutionStage > 0));
        return true;
    }

    private IEnumerator SnipeTarget(Enemy target, float damage, float critChance, bool eagleSplash, float eagleRatio, int eagleTargets, int shots, float scale, bool evolved)
    {
        // 🔴 한 캐스트의 저격은 **전부 같은 그림**을 쓴다. 예전엔 첫 발만 Effect_SplashSniping이었는데,
        //    두 세트는 "기본 vs 화려한 버전"이 아니라 **색 계열이 아예 다르다**(실측: Sniping 계열 hue 42°=주황 /
        //    SplashSniping 계열 hue 214°=하늘·회색). 그래서 미진화 스나이핑에 진화 색이 한 발씩 섞여 나왔다.
        GameObject sparkPrefab = evolved && snipingSplashPrefab != null ? snipingSplashPrefab : snipingEffectPrefab;

        for (int i = 0; i < shots; i++)
        {
            if (target == null) yield break;
            Vector3 pos = target.transform.position;

            if (sparkPrefab != null)
                ObjectPool.Instance.Spawn(sparkPrefab, pos, Quaternion.identity);

            target.TakeSkillHit(damage, critChance, ActiveSkillId.Sniping);

            // R0: 첫 발이 꽂힐 때 그 자리 주변으로 독수리를 떨군다. 연사마다 부르면 한 캐스트에 수십 마리가 되므로 **첫 발만**.
            if (eagleSplash && i == 0 && target != null)
                SpawnMiniEagleSpread(target, damage * eagleRatio, critChance, scale * 0.6f, eagleTargets, ActiveSkillId.Sniping);

            yield return new WaitForSeconds(SnipingShotInterval);
        }
    }

    // 초과 피해 연쇄: fromPos 근처의 산 적(들)에게 overkill을 흘려보내고, 그 적도 초과 피해를 내면 다시 옆 두 적으로 튕긴다.
    // 초과 피해가 없거나 근처에 산 적이 없을 때까지 반복. 매 튐마다 0.3초 텀을 둬서 퍼지는 게 보이게 한다.
    // ⚠️ 2026-08-06 이후 **호출하는 곳이 없다** — 스나이핑 R0이 "초과 피해 연쇄"에서 "주변 독수리 투하"로 바뀌며 빠졌다.
    //    VFX 배선(overkillSplashVfxPrefab)까지 그대로 남겨 뒀으니 되살리려면 SnipeTarget에서 다시 부르면 된다.
    private IEnumerator OverkillChain(Vector3 fromPos, float overkill, float critChance, int fanout, float radius, Enemy exclude)
    {
        if (overkill <= 0f) yield break;
        yield return new WaitForSeconds(0.3f);

        List<Enemy> next = FindObjectsByType<Enemy>(FindObjectsSortMode.None)
            .Where(e => e != null && e != exclude && Vector2.Distance(fromPos, e.transform.position) <= radius)
            .OrderBy(e => Vector2.Distance(fromPos, e.transform.position))
            .Take(fanout)
            .ToList();

        foreach (Enemy e in next)
        {
            if (e == null) continue;
            Vector3 pos = e.transform.position;
            // 초과데미지 스플래시는 스나이핑 이펙트와 확실히 구분되는 전용 VFX(Impact Sparks)
            if (overkillSplashVfxPrefab != null)
            {
                GameObject vfx = ObjectPool.Instance.Spawn(overkillSplashVfxPrefab, pos, Quaternion.identity);
                vfx.transform.localScale = Vector3.one * overkillSplashVfxScale;
                ObjectPool.Instance.Despawn(vfx, 1f);
            }

            e.TakeDamage(overkill, source: ActiveSkillId.Sniping, rollLightning: false); // 흘러들어간 초과 피해는 그대로 적용
            if (e != null && e.CurrentHealth <= 0f)
            {
                float nextOverkill = -e.CurrentHealth;
                if (nextOverkill > 0f)
                    StartCoroutine(OverkillChain(pos, nextOverkill, critChance, 2, radius, e)); // 이후 튐은 두 갈래
            }
        }
    }

    // ── 호밍 미사일: 적 추적 미사일 5개(성장형) ──
    private bool FireHoming(float damage, float critChance, EquippedSkill skill)
    {
        if (homingMissilePrefab == null) return false;

        // 성장: 사용할수록 강해짐(이번 판 한정). 상한이 없어 판이 길수록 혼자 세진다 — 알려진 성질.
        const float growthPerCast = 0.08f;
        skill.GrowthStacks++;
        float missileDamage = damage * (1f + growthPerCast * skill.GrowthStacks);

        // 레벨업 주 성장축: 미사일 수(레벨업=+1씩, 진화=아래 R1이 배수로 얹힌다).
        int count = HomingBaseMissiles + skill.ExtraProjectiles;

        // R1(가속 연계, path2): **더 작은 미사일을 더 여러 개**(2026-08-06 명세).
        // 쿨 감소는 영구 스탯이라 ApplyPathTierEffect가 맡고, 여기선 개수와 크기만 다룬다.
        float missileScale = 1f;
        if (skill.PathTier[2] >= 3) { count *= 3; missileScale = 0.55f; }
        else if (skill.PathTier[2] >= 2) { count *= 2; missileScale = 0.7f; }
        // 스킬트리 "더 많은 폭격"(Homing_MissileNum) 해금 시에만: 10회 사용마다 미사일 +1발
        if (MetaBonuses.HomingMissileGrowth) count += skill.GrowthStacks / 10;
        // Route2(path1): T2 폭발, T3 폭발 강화
        bool explode = skill.PathTier[1] >= 2;
        float explodeRatio = skill.PathTier[1] >= 3 ? 0.6f : 0.4f;
        float explodeRadius = skill.PathTier[1] >= 3 ? 2.5f : 1.5f;

        // 발사각을 매번 조금씩 흔든다 — 같은 부채꼴로만 나가면 여러 발이 한 줄처럼 보인다.
        const float spreadJitter = 15f;

        for (int i = 0; i < count; i++)
        {
            float spread = count > 1 ? Mathf.Lerp(-60f, 60f, i / (float)(count - 1)) : 0f;
            spread += Random.Range(-spreadJitter, spreadJitter);
            Vector2 dir = Quaternion.Euler(0f, 0f, spread) * Vector2.right; // 적 방향(오른쪽) 부채꼴
            GameObject obj = Instantiate(homingMissilePrefab, transform.position + Vector3.up * 0.2f, Quaternion.identity);
            obj.transform.localScale *= missileScale;
            HomingMissile m = obj.GetComponent<HomingMissile>();
            m.Damage = missileDamage;
            m.CritChance = critChance;
            m.Explode = explode;
            m.ExplodeRadius = explodeRadius;
            m.ExplodeRatio = explodeRatio;
            m.TargetRank = i; // 미사일마다 다른 적을 노리게 하는 순번(비행 우선 → 가까운 순으로 i번째)
            // 폭발 VFX는 HomingMissile 프리팹이 자체 보유(실제 폭발 에셋). 여기서 스나이핑 이펙트를 물리지 않는다.
            m.Init(dir);
        }
        animator.SetTrigger("Attack");
        return true;
    }

    // ── 산탄 장착: 전방으로 산탄을 뿌리고, 동시에 5초간 타수 버프를 건다 ──
    // 예전엔 버프만 걸어서 **시전해도 화면에 아무 일도 안 일어나는** 유일한 스킬이었다.
    // 실제로 산탄을 쏘게 해 이름값을 하게 하고, 알 개수를 레벨업 주 성장축으로 삼는다.
    private bool FireShotgun(float damage, float critChance, EquippedSkill skill)
    {
        FireShotgunPellets(damage, critChance, skill);

        // 🔴 **버프는 집중 산탄 루트를 골랐을 때만 켜진다**(사용자 결정 2026-09-02 — 8/27 QA "산탄 구조 개편").
        //    화면의 "루트 1"(= route 0, 스나이핑 연계 · 집중 산탄 → 일점사 산탄)이 그 루트다.
        //    미진화 산탄과 관통 산탄 루트는 **순수 공격기**다 — 예전엔 공격+버프가 한 스킬에 섞여 있어서
        //    진화로 버프를 고를 이유가 없었다.
        if (skill.PathTier[1] <= 0) return true;

        // 루트를 탔으므로 +2초는 늘 붙는다(관통 산탄 루트는 위에서 이미 빠져나갔다).
        // 기본 7초(5+2, 루트를 탔으므로 +2는 늘 붙는다) — 에셋 `Prog_Shotgun.baseDuration`이 있으면 그쪽이 이긴다.
        float duration = BaseDuration(skill.Id, 5f + 2f) + skill.ExtraShotgunDuration; // 레벨업 "지속시간" 스텝
        // 스킬트리 "산탄 타수 +1"은 버프가 주는 타수에 더해진다.
        // ⚠️ 그래서 **집중 산탄 루트를 타야만 효과가 있다** — 바로 위 게이트에서 미진화·관통 산탄은 이미 빠져나갔다.
        int bonus = 1 + MetaBonuses.ShotgunExtraBonusHit;
        // R0(집중 산탄, path1) T2: 최고 공격력 스킬 1개에만, 보너스 2배(T3=3배)
        bool single = skill.PathTier[1] >= 2;
        if (single) bonus *= skill.PathTier[1] >= 3 ? 3 : 2;

        shotgunTimer = duration;
        shotgunBonus = bonus;
        shotgunSingleTarget = single;
        if (single) shotgunTargetSkill = HighestDamageSkill();

        // R1(휘두르기 연계, path2)은 "화면 전체 5회 공격 + 기절"에서 **전방 집중 산탄**으로 바뀌었다(2026-08-06 명세).
        // 실제 변화는 전부 FireShotgunPellets(탄 수·발사 각도·피해)에 있다 — 여기서 따로 할 일이 없다.

        BuffTracker.Set("Shotgun", Time.time + duration);
        return true;
    }

    private ActiveSkillId HighestDamageSkill()
    {
        ActiveSkillId best = ActiveSkillId.BasicAttack;
        float bestDmg = -1f;
        foreach (EquippedSkill s in equippedSkills)
        {
            if (s.Id == ActiveSkillId.Shotgun || s.Id == ActiveSkillId.Rewind) continue; // 자신·피해 없는 유틸 제외
            if (s.Damage > bestDmg) { bestDmg = s.Damage; best = s.Id; }
        }
        return best;
    }

    // ── 휘두르기(파인애플 전용): 앞의 적을 돌망치로 후려쳐 뒤로 밀어낸다 ──
    // 딸기의 화살 쏘기 자리를 대신하는 주력기. 사거리가 짧은 대신 쿨이 짧고, 맞은 적을 왼쪽으로
    // 밀어내 방어선을 되돌린다(디펜스에서 시간을 버는 것이 이 스킬의 정체성).
    // 투사체가 아니라 즉발 판정이라 비행 적도 범위 안이면 같이 맞는다.
    private const float SwingReach = 4.8f;       // 플레이어 앞(왼쪽) 사거리 = 판정의 **끝점**
    private const float SwingHalfHeight = 2f;    // 위아래 판정 반높이 — 비행 적까지 닿게 넉넉히
    private const float SwingKnockback = 2.4f;   // 밀어내는 거리
    // 판정의 **시작점** — 파인애플 몸통 바로 앞. 몸통을 덮던 예전 판정(등 뒤 0.5까지)을 앞으로 밀어낸 것.
    // ⚠️ 일부러 skill.Scale을 곱하지 않는다. 시작점은 "몸통 위치"라 크기 진화와 무관하게 고정돼야 한다
    //    — 곱하면 크기를 올릴수록 코앞이 비어서 근접 캐릭터가 눈앞의 적을 못 때린다.
    //    덕분에 크기 증가는 **앞쪽으로만** 자라고, 초반 화력은 유지되면서 면적 증가폭은 완만해진다.
    // (실측: 플레이어 pivot→앞 몸통 끝 0.75. 파인애플은 스프라이트 폭에 돌망치가 포함돼 더 넓지만
    //  "몸통"만 치면 딸기와 비슷하다 — 눈으로 보고 조정할 손잡이다.)
    private const float SwingNearOffset = 0.7f;
    // 판정 사각형의 **중심 높이**를 플레이어 y에서 위로 얼마나 올릴지. 0이면 위아래가 대칭이라
    // 아래쪽(땅 밑)이 남고 위쪽(비행 적)이 모자란다 — 돌망치를 머리 위로 휘두르는 그림과도 안 맞는다.
    // ⚠️ skill.Scale·reachMult를 곱하지 않는다. 몸통 기준 높이라 크기 진화와 무관하게 고정돼야 한다
    //    (곱하면 크기를 키울수록 판정이 통째로 떠올라 지상 적을 놓친다).
    private const float SwingCenterYOffset = 0.6f;

    // 피해는 캐스트 순간이 아니라 **돌망치가 땅에 꽂히는 4번째 프레임**에 들어간다
    // (그 프레임에 사용자가 충격파를 직접 그려 넣었다 — 그림과 판정이 같은 순간이어야 한다).
    // Pinapple_Attack = 5프레임: 1~4는 각 75ms, 마지막 5번째만 225ms(총 0.525초). 4번째 시작 = 0.225초.
    // ⚠️ 클립 타이밍을 바꾸면 이 값도 같이 고칠 것 — 어긋나면 휘두르기도 전에 적이 날아간다.
    private const float SwingImpactDelay = 0.225f;
    // 2루트 진화 충격파 — 본체보다 약하게 때리고 "살짝" 밀어낸다(기본 넉백 0.8의 절반 이하).
    private const float ShockwaveSpawnOffset = 1.6f;            // 내려찍은 지점에서 출발(앞쪽)
    // 땅을 타고 번지는 연출이라 플레이어 중심이 아니라 **발밑 높이**에서 나가야 한다.
    // (0이면 몸통 한가운데서 나가 공중에 뜬 것처럼 보인다.)
    // (실측: 플레이어 pivot→발밑 1.125, 충격파 스프라이트 반높이 약 0.3 → 발밑에 얹으면 -0.85)
    private const float ShockwaveSpawnYOffset = -0.85f;
    private const float ShockwaveDamageRatio = 0.6f;            // 진화 1차: 본체 피해의 60%
    private const float ShockwaveEmpoweredDamageRatio = 1f;     // 진화 2차: 본체와 같은 피해
    private const float ShockwaveKnockback = 0.35f;
    private const float ShockwaveEmpoweredKnockback = 0.55f;
    private const float SwingStunDuration = 0.5f;               // 1루트 진화 2차: 밀쳐진 적 기절
    // 1루트 진화 1차: 맞은 적 **한 마리당** 초과체력 회복(독수리 "흡혈 군단"과 같은 방식).
    // 광역이라 여럿 맞히면 그만큼 크게 회복된다 — 근접으로 파고드는 위험의 보상.
    private const int SwingLifestealPerHit = 2;

    // ── 돌조각 파티클 ──
    // 노란 범위 사각형을 걷어낸 자리를 대신한다 — "어디까지 맞았는지"를 표시가 아니라 **맞은 적한테서** 보여준다.
    // 진화 루트와 무관하게 휘두르기에 맞으면 항상 튄다.
    private const int RockDebrisPerHit = 4;
    private const float RockDebrisSpeed = 4.5f;

    // ── 포도: 독성 포도알 ──
    // 적이 모인 자리에 포도알을 던져 독안개를 깔고, 안개에 닿은 적을 중독시킨다.
    // 진화 효과는 전부 여기서 PathTier를 실시간으로 읽는다 — 다만 **중독 틱 안에서 도는 것**
    // (폭발·기절)만은 Enemy가 읽어야 해서 static으로 넘긴다.
    private void FireGrapeToss(float damage, EquippedSkill skill)
    {
        animator.SetTrigger("Attack");
        PlayCastSfx(grapeTossCastSfx, castSfxVolume);

        int balls = GrapeBaseBalls + skill.ExtraProjectiles;
        if (skill.PathTier[2] >= 1) balls += 2;              // 찌릿찌릿 1차: 던지는 알이 늘어난다

        float radius = GrapeCloudRadius * skill.Scale;
        if (skill.PathTier[1] >= 1) radius *= 1.4f;          // 생화학 1차: 안개가 커진다

        float interval = GrapePoisonInterval;
        if (skill.PathTier[1] >= 2) interval *= 0.5f;        // 생화학 1차(옛 T2): 도트 간격 절반

        // 중독 틱이 읽을 값 — 진화 직후 첫 시전에 반영되고, 그 전에는 중독 자체가 없다.
        GrapePoisonExplodeOnDeath = skill.PathTier[1] >= 3;  // 생화학 2차
        GrapeStunEveryNPoisonTicks = skill.PathTier[2] >= 3 ? 2 : (skill.PathTier[2] >= 2 ? 3 : 0);
        GrapeStunAppliesVulnerable = skill.PathTier[2] >= 3; // 찌릿찌릿 2차

        foreach (Vector3 spot in PickGrapeSpots(balls, radius))
        {
            GameObject ball = SpawnGrapeBall(transform.position + Vector3.up * 0.4f);
            if (ball == null) { LandGrape(spot, radius, damage, interval); continue; }

            GrapeProjectile gp = ball.GetComponent<GrapeProjectile>();
            if (gp == null) gp = ball.AddComponent<GrapeProjectile>();
            Vector3 target = spot;
            gp.Init(target, GrapeFlightTime, GrapeArcHeight, landed => LandGrape(landed, radius, damage, interval));
        }
    }

    // 착탄 — 터짐을 한 번 보여주고 그 자리에 안개를 남긴다.
    private void LandGrape(Vector3 at, float radius, float damage, float interval)
    {
        PlayCastSfx(grapePopSfx, grapePopSfxVolume);

        if (grapeExplosionVfxPrefab != null)
        {
            GameObject vfx = ObjectPool.Instance.Spawn(grapeExplosionVfxPrefab, at, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * (radius * 0.6f);
            ObjectPool.Instance.Despawn(vfx, 1.5f);
        }

        GameObject cloud = new GameObject("PoisonCloud");
        cloud.transform.position = at;
        cloud.AddComponent<PoisonCloud>()
             .Init(radius, GrapeCloudDuration, damage, GrapePoisonDuration, interval, GrapeCloudColor);
    }

    // 포도알 그림 = 기본 오브. 전용 프리팹이 배선돼 있으면 그쪽을 먼저 쓴다.
    private GameObject SpawnGrapeBall(Vector3 from)
    {
        if (grapeBallPrefab != null) return Instantiate(grapeBallPrefab, from, Quaternion.identity);
        if (orbPrefab == null) return null;

        // 오브 프리팹을 그대로 쓰면 Orb 로직(관통·슬로우)이 같이 붙는다 — **그림만** 떼어 온다.
        SpriteRenderer src = orbPrefab.GetComponentInChildren<SpriteRenderer>();
        if (src == null || src.sprite == null) return null;

        GameObject ball = new GameObject("GrapeBall", typeof(SpriteRenderer));
        ball.transform.position = from;
        ball.transform.localScale = Vector3.one * 0.6f;
        SpriteRenderer sr = ball.GetComponent<SpriteRenderer>();
        sr.sprite = src.sprite;
        sr.color = GrapeCloudColor;
        sr.sortingOrder = 120; // 던지는 알은 적보다 앞 — 궤적이 가려지면 어디 떨어질지 안 보인다
        return ball;
    }

    // 적이 모인 자리를 고른다 — **무리의 앞줄**(플레이어에 가까운 쪽)부터. 안개끼리 겹치면 넓이가 낭비되므로
    // 이미 고른 지점과는 떨어뜨린다. 적이 하나도 없으면 전방 허공에라도 던진다 —
    // 키를 눌렀는데 아무것도 안 나가면 고장난 것처럼 느껴진다.
    private List<Vector3> PickGrapeSpots(int count, float radius)
    {
        List<Vector3> spots = new List<Vector3>();
        List<Enemy> alive = new List<Enemy>();
        foreach (Enemy e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            if (e != null && e.IsAlive) alive.Add(e);

        // 플레이어에 가까운 순 = 무리의 앞줄부터. 뒤에 던지면 앞줄은 이미 지나가 버려 아무도 안 맞는다.
        float playerX = transform.position.x;
        alive.Sort((a, b) => Mathf.Abs(a.transform.position.x - playerX)
                                 .CompareTo(Mathf.Abs(b.transform.position.x - playerX)));

        float minGap = radius * 1.2f;
        for (int i = 0; i < count; i++)
        {
            if (alive.Count == 0)
            {
                spots.Add(transform.position + new Vector3(Random.Range(2f, 7f), Random.Range(-1.5f, 1.5f), 0f));
                continue;
            }

            // 앞줄부터 훑어 이미 고른 자리와 떨어진 첫 적을 쓴다.
            // 다 붙어 있어 못 고르면 **앞에서 i번째** 적으로 떨어진다 — alive[0]로 고정하면
            // 빽빽한 무리에서 알이 전부 한 마리 위에 겹쳐 안개 넓이가 통째로 낭비된다.
            Vector3 pick = LeadGrapeTarget(alive[Mathf.Min(i, alive.Count - 1)], playerX);
            foreach (Enemy e in alive)
            {
                Vector3 cand = LeadGrapeTarget(e, playerX);
                bool far = true;
                foreach (Vector3 s in spots)
                    if (Vector2.Distance(s, cand) < minGap) { far = false; break; }
                if (far) { pick = cand; break; }
            }
            spots.Add(pick);
        }
        return spots;
    }

    // 착탄 시점의 위치를 미리 짚는다 — 포도알은 GrapeFlightTime만큼 날아가는데 그동안 적이 걸어 나가
    // 지금 위치에 던지면 안개가 빈 자리에 깔린다. 적은 x축으로만 걸어오므로 x만 민다.
    private Vector3 LeadGrapeTarget(Enemy e, float playerX)
    {
        Vector3 p = e.transform.position;
        float dir = Mathf.Sign(playerX - p.x);   // 적은 늘 플레이어 쪽으로 온다
        p.x += dir * e.CurrentMoveSpeed * GrapeFlightTime;
        if ((playerX - p.x) * dir < 0f) p.x = playerX; // 플레이어를 지나쳐 뒤로는 안 던진다
        return p;
    }

    private void FireSwing(float damage, float critChance, EquippedSkill skill)
    {
        animator.SetTrigger("Attack");
        StartCoroutine(SwingRoutine(damage, critChance, skill));
    }

    private IEnumerator SwingRoutine(float damage, float critChance, EquippedSkill skill)
    {
        // Route1(힘 연계, path1): 진화 1차 = 타격 범위 확대 / 2차 = 밀쳐진 적 기절.
        float reachMult = skill.PathTier[1] >= 2 ? 1.45f : 1f;
        bool stun = skill.PathTier[1] >= 3;
        // Route2(회오리 연계, path2): 진화 1차 = 맵 끝까지 가는 충격파 / 2차 = 그 충격파가 강해진다.
        bool shockwave = skill.PathTier[2] >= 2;
        bool empoweredShock = skill.PathTier[2] >= 3;

        float reach = SwingReach * reachMult * skill.Scale;
        float halfHeight = SwingHalfHeight * reachMult * skill.Scale;

        yield return new WaitForSeconds(SwingImpactDelay); // 내려찍는 순간까지 기다린다

        // 타격 이펙트는 별도 VFX가 아니라 **4번째 프레임 그림에 그려져 있다**(사용자 아트).
        // 파티클을 겹쳐 봤지만 도트 그림을 가려서 뺐다 — 연출을 더하려면 그림 쪽을 먼저 볼 것.
        // 1루트 1차부터 흡혈이 붙는다 — 범위·피해만 늘던 루트에 "버티는" 성격을 준다.
        int lifesteal = skill.PathTier[1] >= 2 ? SwingLifestealPerHit : 0;
        ScreenShake.Shake(ScreenShake.SwingStrength, ScreenShake.SwingDuration); // 내려찍는 그 순간에 맞춰 흔든다
        SwingHit(damage, critChance, reach, halfHeight, SwingKnockback * MetaBonuses.SwingKnockbackMult, stun, lifesteal); // 배율 = 스킬트리 "휘두르기 넉백"

        if (shockwave) SpawnShockwave(damage, critChance, empoweredShock);
    }

    // 내려찍은 자리에서 맵 끝까지 달려나가는 충격파. 본체보다 약하게 때리고 살짝만 밀어낸다.
    private void SpawnShockwave(float damage, float critChance, bool empowered)
    {
        if (swingShockwavePrefab == null) return;

        Vector3 pos = transform.position + Vector3.left * ShockwaveSpawnOffset + Vector3.up * ShockwaveSpawnYOffset;
        GameObject obj = Instantiate(swingShockwavePrefab, pos, Quaternion.identity);
        SwingShockwave wave = obj.GetComponent<SwingShockwave>();
        wave.Damage = damage * (empowered ? ShockwaveEmpoweredDamageRatio : ShockwaveDamageRatio);
        wave.CritChance = critChance;
        wave.Knockback = empowered ? ShockwaveEmpoweredKnockback : ShockwaveKnockback;
    }

    private void SwingHit(float damage, float critChance, float reach, float halfHeight, float knockback, bool stun, int lifestealPerHit)
    {
        float px = transform.position.x;
        float py = transform.position.y + SwingCenterYOffset; // 판정 사각형의 세로 중심

        foreach (Enemy e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
        {
            if (e == null || !e.IsAlive) continue;
            Vector3 p = e.transform.position;
            float dx = px - p.x;
            if (dx < SwingNearOffset || dx > reach) continue;
            if (Mathf.Abs(p.y - py) > halfHeight) continue;

            e.TakeSkillHit(damage, critChance, ActiveSkillId.Swing);
            e.ApplyKnockback(knockback);
            if (stun) e.ApplySlow(0f, SwingStunDuration); // 감속 0 = 이동 정지(기절)
            if (lifestealPerHit > 0 && health != null) health.AddOverheal(lifestealPerHit);
            SpawnRockDebris(p);
        }
    }

    // 맞은 적 자리에서 돌조각이 위쪽 반원으로 튄다.
    private void SpawnRockDebris(Vector3 at)
    {
        if (rockDebrisPrefab == null) return;

        for (int i = 0; i < RockDebrisPerHit; i++)
        {
            GameObject go = ObjectPool.Instance.Spawn(rockDebrisPrefab, at, Quaternion.identity);
            RockDebris debris = go.GetComponent<RockDebris>();
            if (debris == null) continue;

            float deg = Random.Range(25f, 155f); // 위쪽 반원 — 아래로 파고드는 조각이 없게
            Vector2 dir = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
            debris.Launch(dir * Random.Range(RockDebrisSpeed * 0.6f, RockDebrisSpeed), Random.Range(-360f, 360f));
        }
    }

    private const float RewindVfxHeight = 1.9f; // 되감기 표식이 뜨는 머리 위 높이(유닛)

    // ── 되감기: 다른 스킬의 쿨타임을 앞당긴다 ──
    private void FireRewind(EquippedSkill skill)
    {
        // 되감기 정도(앞당길 시간) — 레벨업 "되감기 시간" 스텝만 늘린다.
        float amount = skill.RewindAmount;

        foreach (EquippedSkill s in equippedSkills)
            if (s != skill) s.CooldownTimer = Mathf.Max(0f, s.CooldownTimer - amount);

        // 스킬트리 "블루베리 둔화"(Rewind_Slow) 해금 시: 되감을 때 모든 적을 천천히 감아 둔화(50% 감속, 2초)
        if (MetaBonuses.RewindSlowAll)
            foreach (Enemy e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                if (e != null) e.ApplySlow(0.5f, 2f);

        // Route2(path1): 다음에 사용하는 스킬의 피해를 1회 증가 (ComputeBaseDamage가 소비)
        if (skill.PathTier[1] >= 1)
            nextSkillDamageBonus = skill.PathTier[1] >= 3 ? 1.0f : skill.PathTier[1] >= 2 ? 0.6f : 0.3f;

        // 되감기는 여태 화면에 아무것도 안 나왔다 — 머리 위에 표식을 한 번 띄운다.
        // R0(충전 되감기)만 전용 그림이 있다. R1(가속 되감기)과 진화 전은 기본 그림.
        GameObject rewindVfx = rewindVfxPrefab;
        if (skill.PathTier[1] >= 1 && rewindRoute2VfxPrefab != null) rewindVfx = rewindRoute2VfxPrefab;
        if (rewindVfx != null)
            ObjectPool.Instance.Spawn(rewindVfx, transform.position + Vector3.up * RewindVfxHeight, Quaternion.identity);

        animator.SetTrigger("Attack");
    }

    // 되감기 Route3 T3을 보유하면 전역 쿨타임(GCD)이 절반이 된다.
    private float GlobalCooldownScale()
    {
        foreach (EquippedSkill s in equippedSkills)
            // 🔴 **1차부터** 켜진다(2026-09-08 사용자 결정 — 옛 2차 효과를 1차로 끌어왔다).
            //    원래 1차 효과였던 "되감기 자체 쿨감"은 진화 쿨감 금지로 사라져서 1차가 빈 칸이 됐었다.
            //    ⚠️ 1차 = PathTier 2, 2차 = 3. `>= 3`으로 쓰면 다시 2차 전용이 된다.
            //    ⚠️ **2차는 지금 효과가 없다** — 태리미가 채울 자리다.
            if (s.Id == ActiveSkillId.Rewind && s.PathTier[2] >= 2) return 0.5f;
        return 1f;
    }

    private void FireWhirlwind(float damage, float critChance, EquippedSkill skill)
    {
        bool applySlow = skill.PathTier[2] >= 1;
        bool applyVulnerable = skill.PathTier[2] >= 3;

        // R0(화살 연계, path0): 본체를 **2개** 소환하고, 각각이 사라질 때 그 자리에 미니 회오리를 남긴다.
        // 예전엔 시전과 동시에 미니를 흩뿌렸다 — 이제는 "큰 게 수명을 다하면 새끼가 남는다"는 2단 구조다(2026-08-06 명세).
        // 미니 피해는 독수리 R1의 미니 회오리와 MiniWhirlwindDamageBonus를 공유한다(스킬 간 증폭 — 의도).
        int mainCount = skill.PathTier[0] >= 2 ? 2 : 1;
        int miniOnExpire = skill.PathTier[0] >= 3 ? 3 : (skill.PathTier[0] >= 2 ? 2 : 0);
        float miniDamage = damage * 0.3f * (1f + MiniWhirlwindDamageBonus);

        if (skill.PathTier[2] >= 2) // 오브 연계 path T2: 거대 회오리로 대체 (여러 개로 안 쪼개짐)
        {
            // R0과 R1은 배타적이라(한 스킬은 루트 하나만 밟는다) 여기서 miniOnExpire는 항상 0이다.
            Vector3 spawnPos = transform.position + Vector3.left * 0.6f + Vector3.down * 1.4f;
            SpawnBigTornado(spawnPos, damage, critChance, skill.Scale, applySlow, applyVulnerable, skill.ExtraWhirlwindDuration);
        }
        else
        {
            for (int i = 0; i < mainCount; i++)
            {
                // 2개일 때만 좌우로 살짝 벌려 겹쳐 보이지 않게 한다.
                float spread = mainCount > 1 ? (i == 0 ? -0.7f : 0.7f) : 0f;
                Vector3 spawnPos = transform.position + Vector3.left * (0.6f - spread) + Vector3.up * 0.6f;
                Whirlwind main = SpawnWhirlwind(spawnPos, damage, critChance, skill.Scale, applySlow, applyVulnerable, maxHitCount: 0, slowDuration: 3f, extraLifetime: skill.ExtraWhirlwindDuration, tickIntervalMult: skill.TickIntervalMult);

                if (miniOnExpire > 0 && main != null)
                {
                    float miniScale = skill.Scale * MiniWhirlwindScale;
                    float miniTick = skill.TickIntervalMult;
                    main.OnExpired = pos =>
                    {
                        if (this == null) return; // 플레이어가 먼저 파괴됐으면 코루틴/소환을 걸지 않는다
                        for (int m = 0; m < miniOnExpire; m++)
                        {
                            Vector3 at = pos + (Vector3)(Random.insideUnitCircle * 0.5f);
                            SpawnWhirlwind(at, miniDamage, critChance, miniScale, applySlow, applyVulnerable, maxHitCount: 6, slowDuration: 3f, isMini: true, tickIntervalMult: miniTick);
                        }
                    };
                }
            }
        }

    }

    // 낙뢰 버프(지속시간 표시)와, path2 T3에서만 뜨는 전체피해 보너스 버프(스택만 표시)를 함께 갱신
    private static void RefreshLightningBuffDisplay()
    {
        BuffTracker.Set("Lightning", LightningStorm.LatestEndTime, () => LightningStorm.ActiveStackCount);
        if (LightningStorm.StackDamageEnabled)
            BuffTracker.Set("LightningDamageBuff", LightningStorm.LatestEndTime, () => LightningStorm.ActiveStackCount, showTimer: false);
        else
            BuffTracker.Clear("LightningDamageBuff");
    }

    private static void PlayCastSfx(AudioClip clip, float volume)
    {
        if (clip != null && AudioThrottle.TryConsume(clip))
            SfxPlayer.Play(clip, volume);
    }

    private void SpawnBigTornado(Vector3 position, float damage, float critChance, float scale, bool applySlow, bool applyVulnerable, float extraLifetime = 0f)
    {
        PlayCastSfx(whirlwindCastSfx, whirlwindCastSfxVolume);
        GameObject prefab = bigTornadoPrefab != null ? bigTornadoPrefab : whirlwindPrefab;
        GameObject obj = Instantiate(prefab, position, Quaternion.identity);
        obj.transform.localScale *= scale;
        Whirlwind whirlwind = obj.GetComponent<Whirlwind>();
        whirlwind.Damage = damage;
        whirlwind.ApplyGemSlow = applySlow;
        whirlwind.ApplyGemVulnerable = applyVulnerable;
        whirlwind.CritChance = critChance;
        whirlwind.SlowDuration = 4.5f;
        whirlwind.ExtraLifetime = extraLifetime;
    }

    // 총구 화염 손잡이. 알마다 하나씩이 아니라 **한 번 쏠 때(=볼리마다) 하나**다(사용자 명세 2026-09-02).
    private const float ScatterFireScale = 1.8f;      // 🔴 절대값이다 — 풀에서 재사용되므로 곱하면 매번 커진다
    private const float ScatterFireMuzzleGap = 0.75f; // 몸 중심에서 총구까지(유닛)

    // ── 전탄발사(관통 산탄 이후) 손잡이 ─────────────────────────────────────
    // 한 방으로 끝나던 산탄이 **여기서만** 일정 시간 전방을 훑는 연사가 된다
    // (사용자 명세 2026-09-02 — 메이플 메탈아머 전탄발사).
    private const float BarrageBaseDuration = 2f;      // 기본 지속(초). 레벨업 "지속시간" 스텝이 더해진다
    private const float BarrageVolleyInterval = 0.12f; // 볼리 간격(초)
    private const float BarrageBandHeight = 4.5f;      // 세로로 훑는 총 높이(유닛)
    private const float BarrageBandDown = 1.2f;        // 그중 발사점 **아래**로 내려가는 몫 — 지면 바로 위까지만
    // 관통 **무한**(사용자 결정 2026-09-02). 알은 사거리(=`Scatter_Pellet`의 lifetime)가 다할 때까지 뚫고 지나간다.
    // 같은 적을 두 번 때리지는 않는다(`SmallOrb.hitEnemies`), 방패 블루베리는 관통과 무관하게 끊는다.
    private const int BarragePierce = int.MaxValue;

    // 🔴 **미진화 산탄은 한 프레임에 전탄을 동시에** 내보낸다(사용자 명세, 8/25 빌드 검수).
    //    공격 애니메이션이 딱 한 번 도는 동안 탄이 전부 나가고 끝나는 것이 그 스킬이 원하는 손맛이다 —
    //    시간차를 두고 나눠 쏘면 애니메이션이 여러 번 도는 것처럼 보여서 산탄이 아니게 된다.
    //    ⚠️ 이 규칙은 이제 **미진화 산탄에만** 적용된다. 관통 산탄(path2 T2+)은 2026-09-02에 연사로 바뀌었고,
    //       거기서는 애니메이션이 볼리마다 도는 것이 의도다(사용자 결정).
    //    빠르기·사거리는 `Scatter_Pellet.prefab`의 `moveSpeed`·`lifetime`이 정한다(게임에서 제일 빠른 투사체).
    // 적이 오는 왼쪽으로 부채꼴 산탄. 알이 늘어도 각도는 그대로라 **촘촘해지는 것**이 눈에 보인다.
    private void FireShotgunPellets(float damage, float critChance, EquippedSkill skill)
    {
        if (shotgunPelletPrefab == null && scatterPelletPrefab == null) return;

        int pellets = Mathf.Max(1, ShotgunBasePellets + skill.ExtraProjectiles); // 레벨업 주 성장축

        // R1(휘두르기 연계, path2): 탄이 많아지고 **부채꼴 대신 전방으로 몰아 쏜다**. 피해도 오른다(2026-08-06 명세).
        // 각도를 0으로 좁히는 게 핵심 — 흩어지던 화력이 정면 한 줄기에 전부 실린다.
        float spreadDegrees = BalanceConstants.ShotgunSpreadDegrees;
        float pelletDamage = damage;
        if (skill.PathTier[2] >= 3) { pellets *= 3; spreadDegrees = 0f; pelletDamage *= 1.6f; }
        else if (skill.PathTier[2] >= 2) { pellets *= 2; spreadDegrees = 0f; pelletDamage *= 1.3f; }

        if (skill.PathTier[2] >= 2) { StartCoroutine(Barrage(skill, pellets, pelletDamage, critChance)); return; }

        FireVolley(skill, pellets, spreadDegrees, pelletDamage, critChance, pierce: 0);
    }

    // 전탄발사 — 같은 볼리를 지속시간 동안 되풀이한다.
    // 🔴 총 피해량은 **한 방이던 시절과 같다**(사용자 결정 2026-09-02). 볼리 수로 나눠 담을 뿐이라
    //    레벨업(탄 수·피해)은 그대로 총량에 실리고, 지속시간만 늘리면 총량은 안 변한다.
    private IEnumerator Barrage(EquippedSkill skill, int pellets, float pelletDamage, float critChance)
    {
        float duration = BarrageBaseDuration + skill.ExtraShotgunDuration;
        int volleys = Mathf.Max(1, Mathf.RoundToInt(duration / BarrageVolleyInterval));

        for (int v = 0; v < volleys; v++)
        {
            FireVolley(skill, pellets, 0f, pelletDamage / volleys, critChance, BarragePierce);
            yield return new WaitForSeconds(BarrageVolleyInterval);
        }
    }

    // 한 번의 발사. 총구 화염 · 알 · 공격 애니메이션이 한 세트다.
    private void FireVolley(EquippedSkill skill, int pellets, float spreadDegrees, float pelletDamage, float critChance, int pierce)
    {
        // 전용 그림이 배선돼 있으면 그쪽. 폴백(shotgunPelletPrefab)은 추적 오브와 공유하는 원본이라 그림이 오브다.
        GameObject pelletPrefab = scatterPelletPrefab != null ? scatterPelletPrefab : shotgunPelletPrefab;
        Vector3 origin = transform.position + Vector3.up * 0.2f;

        SpawnMuzzleFire(origin);

        for (int i = 0; i < pellets; i++)
        {
            float t = pellets > 1 ? i / (float)(pellets - 1) : 0.5f;
            float angle = Mathf.Lerp(-spreadDegrees, spreadDegrees, t);
            Vector2 dir = Quaternion.Euler(0f, 0f, angle) * Vector2.left;

            // 전방 집중(각도 0)이면 전부 한 점에서 겹쳐 한 발처럼 보인다 — 출발 높이를 세로로 벌린다.
            // 🔴 균등 배치가 아니라 **칸을 나눠 그 칸 안에서만 흔든다**(층화 추출). 균등이면 볼리마다 같은 자리라
            //    격자무늬로 보이고, 완전 무작위면 뭉쳐서 빈 구간이 생긴다.
            Vector3 spawnAt = origin;
            if (spreadDegrees <= 0f)
            {
                float slot = (i + Random.value) / pellets;
                spawnAt += Vector3.up * Mathf.Lerp(-BarrageBandDown, BarrageBandHeight - BarrageBandDown, slot);
            }

            // 알 그림은 왼쪽으로 날아가는 형태(궤적이 뒤로 뻗음)라 부채꼴 각도만큼 같이 돌려야 궤적이 진행 방향과 맞는다.
            GameObject obj = Instantiate(pelletPrefab, spawnAt, Quaternion.Euler(0f, 0f, angle));
            obj.transform.localScale *= skill.Scale;
            SmallOrb pellet = obj.GetComponent<SmallOrb>();
            if (pellet == null) continue;
            pellet.CritChance = critChance;
            // 🔴 SmallOrb의 기본 출처가 Orb다 — 안 갈아주면 산탄 피해가 **데미지 미터에 오브로 잡힌다**.
            pellet.Source = ActiveSkillId.Shotgun;
            // 🔴 **미진화 산탄은 관통 0** — 첫 명중에 바로 사라진다. 그 스킬의 정체성이라 켜지 말 것.
            //    관통 산탄(path2 T2+)만 이름값대로 뚫는다(무한, 사용자 결정 2026-09-02).
            pellet.PierceRemaining = pierce;
            pellet.Init(dir, pelletDamage, false);
        }

        animator.SetTrigger("Attack");
    }

    // 총구 화염. 알을 따라가지 않고 정면(왼쪽)을 향한다.
    // 🔴 회전은 0이다 — 불꽃 그림은 **이미 왼쪽(알이 날아가는 쪽)을 보고** 그려져 있다.
    //    예전 -90도 보정은 그림을 위로 세워서 "바닥에서 불이 솟는" 것처럼 보이게 하고 있었다.
    // ⚠️ 앵커를 총구보다 위·뒤에 두는 건 그림 탓이다: 96x96 캔버스의 pivot은 한가운데인데
    //    불꽃은 왼쪽 아래에 치우쳐 그려져 있어(pivot 기준 x -21~+16px · y -39~+3px) 그만큼 되민다.
    private void SpawnMuzzleFire(Vector3 origin)
    {
        if (scatterFireVfxPrefab == null) return;

        float px = ScatterFireScale / 32f;   // 그림 1px이 몇 유닛인가(PPU 32)
        Vector3 muzzle = origin
            + Vector3.left * (ScatterFireMuzzleGap + 16.5f * px)
            + Vector3.up * (18f * px);
        GameObject fire = ObjectPool.Instance.Spawn(scatterFireVfxPrefab, muzzle, Quaternion.identity);
        if (fire != null) fire.transform.localScale = Vector3.one * ScatterFireScale;
    }

    // 반환값은 회오리 R0이 "사라질 때 미니를 남기는" 콜백을 배선하는 데 쓴다(그 외 호출부는 무시해도 된다).
    private Whirlwind SpawnWhirlwind(Vector3 position, float damage, float critChance, float scale, bool applySlow, bool applyVulnerable, int maxHitCount = 0, float slowDuration = 3f, float extraLifetime = 0f, bool isMini = false, float tickIntervalMult = 1f)
    {
        PlayCastSfx(whirlwindCastSfx, whirlwindCastSfxVolume);
        // 미니 전용 프리팹은 그림이 작은 만큼(48px vs 64px) localScale이 크고 콜라이더가 그만큼 작다 —
        // 월드 기준 화면 크기·판정·바닥선 보정이 전부 본체 축소판과 동일하게 나오도록 맞춰 둔 것이다. 한쪽만 고치지 말 것.
        GameObject prefab = isMini && miniWhirlwindPrefab != null ? miniWhirlwindPrefab : whirlwindPrefab;
        GameObject obj = Instantiate(prefab, position, Quaternion.identity);
        obj.transform.localScale *= scale;
        Whirlwind whirlwind = obj.GetComponent<Whirlwind>();
        whirlwind.Damage = damage;
        whirlwind.ApplyGemSlow = applySlow;
        whirlwind.ApplyGemVulnerable = applyVulnerable;
        whirlwind.CritChance = critChance;
        whirlwind.MaxHitCount = maxHitCount;
        whirlwind.SlowDuration = slowDuration;
        whirlwind.ExtraLifetime = extraLifetime;
        // 소용돌이 수명의 기본값도 에셋이 정할 수 있다(0이면 프리팹 값 그대로). 미니도 같은 축을 쓴다.
        whirlwind.BaseLifetimeOverride = BaseDuration(ActiveSkillId.Whirlwind, 0f);
        whirlwind.CanHitFlying = !isMini; // 미니 회오리는 비행 적을 타격할 수 없다
        whirlwind.TickIntervalMult = tickIntervalMult; // 레벨업 보조축: 피해 주기

        // 미니는 크기가 작아 기본 groundY(피벗=중심)에 놓으면 지면 위로 떠 보인다 — 바닥선을 큰 회오리와 맞춘다.
        // 🔴 다만 바닥선을 정확히 맞추면 이번엔 땅에 파묻힌 것처럼 낮게 보인다(8/24 플레이스루 "스폰 포인트가 지나치게 낮음").
        //    보정을 절반만 먹여 큰 회오리 바닥선과 원래 높이(중심=0)의 중간에 놓는다. 이 값이 높이 손잡이다.
        if (isMini)
        {
            SpriteRenderer sr = obj.GetComponent<SpriteRenderer>();
            if (sr != null) whirlwind.GroundY = -sr.bounds.extents.y * (1f / MiniWhirlwindScale - 1f) * MiniWhirlwindGroundBlend;
        }

        return whirlwind;
    }

    private void FireOrb(float damage, float critChance, EquippedSkill skill)
    {
        PlayCastSfx(orbCastSfx, orbCastSfxVolume);

        // R1(호밍 연계, path2): 큰 오브 하나 대신 **작은 오브 여러 개**가 각자 적을 쫓는다(2026-08-06 명세).
        // 각 오브는 관통 3 — 때리고 나서 다시 다음 적을 찾아간다.
        if (skill.PathTier[2] >= 2)
        {
            SpawnHomingSmallOrbs(damage, critChance, skill);
            return;
        }

        // 지식 연계 path: T2부터는 큰 초록 오브 비주얼로 교체
        GameObject prefabToSpawn = skill.PathTier[1] >= 2 && bigOrbPrefab != null ? bigOrbPrefab : orbPrefab;
        GameObject obj = Instantiate(prefabToSpawn, transform.position + Vector3.down * 0.1f, Quaternion.identity);
        obj.transform.localScale *= skill.Scale;
        Orb orb = obj.GetComponent<Orb>();
        orb.Damage = damage;
        orb.CritChance = critChance;
        orb.ApplyGemVulnerable = false;

        // 오브는 진화로 공중 추가 피해를 얻지 않는다 — 비행 타격은 스킬트리(MetaBonuses.OrbCanHitFlying)로만 열린다.
        orb.FlyingDamageMultiplier = 1f;

        // 지식 연계 path: 슬로우 강화 (T1, T3에서 각각)
        float slowMultBonus = 0f;
        float slowDurBonus = 0f;
        if (skill.PathTier[1] >= 1) { slowMultBonus += 0.1f; slowDurBonus += 0.5f; }
        if (skill.PathTier[1] >= 3) { slowMultBonus += 0.1f; slowDurBonus += 0.5f; }
        // 스킬트리 "오브 기본 둔화". ⚠️ 기본 오브는 **이미 둔화를 건다**(Orb.cs 0.65배·2초) —
        //    이 노드는 그걸 켜는 게 아니라 지식 루트 T1과 같은 크기로 **강화**한다.
        if (MetaBonuses.OrbSlowBoost) { slowMultBonus += 0.1f; slowDurBonus += 0.5f; }
        orb.SlowMultiplierBonus = slowMultBonus;
        orb.SlowDurationBonus = slowDurBonus;
        // 레벨업 주 성장축: 사라지기 전까지 붙잡는 총 적 수.
        // 대형 오브(지식 연계 T2+)는 **관통 무한** — 줄을 통째로 뚫고 지나간다(대가는 늘어난 쿨타임).
        // 스킬트리 "오브 관통 +3"은 이 예산에 더해진다(대형 오브는 이미 무한이라 영향 없음).
        orb.MaxTargets = skill.PathTier[1] >= 2 ? int.MaxValue : OrbBaseTargets + skill.ExtraTargets + MetaBonuses.OrbExtraTargets;
    }

    // ── 오브 R1(호밍 연계, path2): 작은 추적 오브 무리 ──
    // 산탄 알 프리팹(SmallOrb)을 재사용하되 추적·관통을 켠다. 알 하나당 관통 3 = 최대 4마리를 때린다.
    private const int HomingOrbPierce = 3;
    private const float HomingOrbDamageRatio = 0.45f;   // 개수가 늘어난 만큼 발당 피해는 낮춘다
    private const float HomingOrbScale = 0.8f;

    private void SpawnHomingSmallOrbs(float damage, float critChance, EquippedSkill skill)
    {
        if (shotgunPelletPrefab == null) return; // 전용 그림이 나오면 여기만 교체하면 된다

        // 레벨업 주 성장축(타겟 수)을 오브 **개수**로 읽는다. 2차는 그 위에 배수.
        int count = OrbBaseTargets + skill.ExtraTargets + MetaBonuses.OrbExtraTargets;
        if (skill.PathTier[2] >= 3) count = Mathf.RoundToInt(count * 1.5f);

        float orbDamage = damage * HomingOrbDamageRatio * (skill.PathTier[2] >= 3 ? 1.5f : 1f);
        Vector3 origin = transform.position + Vector3.down * 0.1f;

        for (int i = 0; i < count; i++)
        {
            // 처음엔 부채꼴로 흩어져 나갔다가 각자 가까운 적을 찾아 휜다.
            float spread = count > 1 ? Mathf.Lerp(-55f, 55f, i / (float)(count - 1)) : 0f;
            Vector2 dir = Quaternion.Euler(0f, 0f, spread) * Vector2.left;

            GameObject obj = Instantiate(shotgunPelletPrefab, origin, Quaternion.identity);
            obj.transform.localScale *= skill.Scale * HomingOrbScale;
            SmallOrb orb = obj.GetComponent<SmallOrb>();
            if (orb == null) continue;
            orb.CritChance = critChance;
            orb.Homing = true;
            orb.PierceRemaining = HomingOrbPierce;
            orb.Source = ActiveSkillId.Orb;
            orb.Init(dir, orbDamage, applyVulnerable: skill.PathTier[2] >= 3);
        }
    }

    // ⚠️ 2026-08-06 이후 **호출하는 곳이 없다** — 오브 R1이 "설치기"에서 "추적 오브 무리"로 바뀌면서 빠졌다.
    //    프리팹(orbAltarPrefab)·OrbAltar.cs와 함께 통째로 남겨 둔다. 되살리려면 FireOrb에서 다시 부르면 되고,
    //    그때 TryUseSkill의 OrbAltarCooldown 분기도 같이 되돌려야 한다(지금은 평범한 스킬 쿨을 쓴다).
    private void SpawnOrbAltar(Vector3 position, float damage, float critChance, EquippedSkill skill)
    {
        if (orbAltarPrefab == null) return;

        float altarDamageMult = skill.PathTier[2] >= 3 ? 1.5f : 1f;

        GameObject obj = Instantiate(orbAltarPrefab, position, Quaternion.identity);
        obj.transform.localScale *= skill.Scale * 0.75f;
        OrbAltar altar = obj.GetComponent<OrbAltar>();
        altar.OrbDamage = damage * 0.4f * altarDamageMult;
        altar.LightningDamage = damage * 0.6f * altarDamageMult;
        altar.ApplyVulnerable = skill.PathTier[2] >= 1;
        altar.CritChance = critChance;
    }

    private IEnumerator EagleDropRoutine(float damage, float critChance, EquippedSkill skill)
    {
        PlayCastSfx(eagleDropCastSfx, castSfxVolume);

        // 지식 연계 path2(패시브): 독수리 투하 시전마다 즉시 경험치 획득
        if (PlayerPassives.EagleDropCastXpBonus > 0)
            PlayerExperience.Instance?.AddXP(PlayerPassives.EagleDropCastXpBonus);

        bool spawnMiniWhirlwind = skill.PathTier[2] >= 2; // 회오리 연계 path T2: 미니 회오리 생성 (T1은 쿨타임/투하횟수 트레이드오프)
        float miniWhirlwindDamageMult = skill.PathTier[2] >= 3 ? 0.35f : 0.25f; // T2=25%, T3=35%
        int miniWhirlwindMaxHits = skill.PathTier[2] >= 3 ? 8 : 5; // T2=5회, T3=+3(총 8회)

        // R0(산탄 연계, path1) = **독수리 비**. 정해진 횟수가 아니라 3초 동안 계속 쏟아진다(2026-08-06 명세).
        // 투하 간격도 촘촘해져 "하늘을 뒤덮는" 그림이 된다.
        bool eagleRain = skill.PathTier[1] >= 2;
        float rainDuration = skill.PathTier[1] >= 3 ? 4.5f : 3f;
        float rainInterval = skill.PathTier[1] >= 3 ? 0.2f : 0.3f;

        // 레벨업 보조축: 투하 횟수(ExtraProjectiles).
        // 스킬트리 "독수리 투하수 +1"도 여기 더해진다(독수리 비 루트는 횟수가 아니라 시간이라 영향 없음).
        // 🔴 예전엔 여기에 `- (PathTier[2] >= 1 ? 1 : 0)`(급강하 폭격 1차 = 투하 −1)이 있었다. 그건
        //    **쿨감 40%의 대가**로 짝지어 둔 것인데, 2026-09-07에 진화 쿨감을 전부 걷어내면서
        //    대가만 남아 그 진화가 **순손해**가 됐다 — 그래서 같이 걷어낸다. 거래의 한쪽만 지우지 말 것.
        int dropCount = Mathf.Max(1, EagleBaseDrops + skill.ExtraProjectiles + MetaBonuses.EagleExtraDrops);
        // 레벨업 주 성장축: 투하 간격(TickIntervalMult)
        float interval = skill.TickIntervalMult;

        // 독수리 비는 투하가 훨씬 촘촘해서(3초/0.3초 = 10틱) 틱당 피해를 낮추지 않으면 혼자 압도적이 된다.
        // 총량은 기존 2~4회 투하의 3~6배 수준으로 맞춘다.
        float rainDamageRatio = skill.PathTier[1] >= 3 ? 0.28f : 0.35f;

        float elapsed = 0f;
        for (int i = 0; eagleRain ? elapsed < rainDuration : i < dropCount; i++)
        {
            List<Enemy> enemies = new List<Enemy>(FindObjectsByType<Enemy>(FindObjectsSortMode.None));

            float dropDamage = damage * (eagleRain ? rainDamageRatio : 1f);

            foreach (Enemy enemy in enemies)
            {
                Vector3 pos = enemy.transform.position;
                enemy.TakeSkillHit(dropDamage, critChance, ActiveSkillId.EagleDrop);

                if (spawnMiniWhirlwind) SpawnWhirlwind(pos, dropDamage * miniWhirlwindDamageMult * (1f + MiniWhirlwindDamageBonus), critChance, skill.Scale * MiniWhirlwindScale, false, false, maxHitCount: miniWhirlwindMaxHits, isMini: true);

                StartCoroutine(MeteorImpact(pos, skill.Scale));
            }

            float wait = eagleRain ? rainInterval : interval;
            yield return new WaitForSeconds(wait);
            elapsed += wait;
        }
    }

    // 🧱 임시 프리미티브 — 전용 도트가 나오면 이 함수의 스프라이트만 교체하면 된다.
    //    휘두르기 범위 표시(SpawnSwingRange)와 같은 방식으로 흰 사각형 하나를 만들어 색만 입힌다.
    // ⚠️ 플레이어의 localScale이 1.5라 자식으로 붙이면 크기가 곱해진다 — 월드에 독립으로 둔다.
    private const float LightningRodWidth = 0.35f;
    private const float LightningRodHeight = 3.2f;
    private static readonly Color LightningRodColor = new Color(0.62f, 0.66f, 0.72f, 1f); // 쇠기둥 회색
    private static readonly Color LightningRodTipColor = new Color(1f, 0.95f, 0.45f, 1f); // 끝에 노란 촉
    private static Sprite lightningRodSprite;

    private GameObject SpawnLightningRod(Vector3 rodCenter, bool empowered)
    {
        // 전용 도트가 배선돼 있으면 그쪽. 프리팹이 이미 1.5배(다른 이펙트와 같은 픽셀 배율)라 여기서 더 곱하지 않는다.
        if (lightningRodPrefab != null)
        {
            GameObject go = Instantiate(lightningRodPrefab, rodCenter, Quaternion.identity);
            if (empowered) go.transform.localScale *= 1.25f;
            return go;
        }

        if (lightningRodSprite == null)
        {
            Texture2D tex = Texture2D.whiteTexture;
            lightningRodSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        }

        float height = LightningRodHeight * (empowered ? 1.25f : 1f);

        GameObject rod = new GameObject("LightningRod(temp)", typeof(SpriteRenderer));
        SpriteRenderer body = rod.GetComponent<SpriteRenderer>();
        body.sprite = lightningRodSprite;
        body.color = LightningRodColor;
        body.sortingOrder = 50; // 배경(-100)보다 앞, 적(100+)보다 뒤
        rod.transform.position = rodCenter;
        rod.transform.localScale = new Vector3(LightningRodWidth, height, 1f);

        // 꼭대기의 촉 — 기둥 하나만 있으면 그냥 막대라, 번개를 받는 곳이 어딘지 보이게 한다.
        GameObject tip = new GameObject("Tip", typeof(SpriteRenderer));
        SpriteRenderer tipSr = tip.GetComponent<SpriteRenderer>();
        tipSr.sprite = lightningRodSprite;
        tipSr.color = LightningRodTipColor;
        tipSr.sortingOrder = 51;
        tip.transform.SetParent(rod.transform, worldPositionStays: false);
        // 부모의 비균등 스케일을 되돌려 정사각형으로 보이게 한다(부모 x 0.35 · y 3.2가 그대로 곱해지므로).
        tip.transform.localScale = new Vector3(1.6f, LightningRodWidth * 1.6f / height, 1f);
        tip.transform.localPosition = new Vector3(0f, 0.5f, 0f); // 부모 높이의 절반 = 꼭대기

        return rod;
    }

    // ── 낙뢰 R1(힘 연계, path1): 맵 중앙에 꽂는 피뢰침 ──
    // 6초간 1초마다 아주 넓은 범위를 내리쳐 큰 피해와 짧은 기절을 준다.
    // ⚠️ 기둥은 **임시 프리미티브**다 — 코드로 만든 회색 사각형(SwingRange와 같은 방식, 별도 에셋 없음).
    //    전용 도트가 나오면 SpawnLightningRod의 스프라이트만 갈아끼우면 된다.
    // ⚠️ rollLightning:false — 이 타격이 다시 낙뢰 발동을 굴리면 피뢰침이 자기 자신을 증폭한다.
    private const float LightningRodDuration = 6f;
    private const float LightningRodInterval = 1f;
    private const float LightningRodRadius = 7f;      // "아주 넓은 범위" — 화면 가로 대부분
    private const float LightningRodStun = 0.5f;
    private const float LightningRodDamageRatio = 3f;          // 1차: 본체 피해의 3배 = "개큰번개"
    private const float LightningRodEmpoweredRatio = 5f;       // 2차
    private const float LightningRodEmpoweredRadiusMult = 1.3f;
    private const float BigThunderHalfHeight = 2.81f; // Effect_BigThunder 120px ÷ PPU32 × scale1.5 ÷ 2

    // 🔴 낙뢰는 **기둥 위가 아니라 기둥 둘레**에 떨어진다(8/25 빌드 검수). 기둥은 번개를 부르는 표지일 뿐이고,
    //    피해 범위가 반경 7유닛이라 한 줄기만 꽂히면 "넓게 때린다"는 게 화면에 안 보였다.
    private const int LightningRodBoltsPerStrike = 3;
    private const float LightningRodBoltSpreadRatio = 0.7f; // 반경의 몇 %까지 흩뿌리나
    private const float LightningRodBoltScale = 1.5f;       // 프리팹(1.5배) 위에 더 키운다 — 화면을 채우는 크기
    private const float LightningRodSpriteHeight = 4.875f;  // Effect_LightningRod 104px ÷ PPU32 × scale1.5
    private const float LightningRodGroundOffset = 2.025f;  // 기둥 밑동이 서는 자리(카메라 중심 기준). 예전 3.25유닛 기둥의 밑동 그대로

    private IEnumerator LightningRodRoutine(float damage, float critChance, bool empowered)
    {
        float ratio = empowered ? LightningRodEmpoweredRatio : LightningRodDamageRatio;
        float radius = LightningRodRadius * (empowered ? LightningRodEmpoweredRadiusMult : 1f);

        Vector3 center = Camera.main != null ? Camera.main.transform.position : transform.position;
        center.z = 0f;

        // 🔴 기둥을 키울 땐 **밑동을 고정하고 위로** 키운다 — 중심을 고정하면 커진 만큼 땅에 파묻힌다.
        float rodHeight = (lightningRodPrefab != null ? LightningRodSpriteHeight : LightningRodHeight)
                          * (empowered ? 1.25f : 1f);
        float groundY = center.y - LightningRodGroundOffset;
        GameObject rod = SpawnLightningRod(new Vector3(center.x, groundY + rodHeight * 0.5f, 0f), empowered);

        // 번개 아래끝도 같은 바닥선에 맞춘다(그림 pivot이 중앙이라 반높이만큼 올린다).
        float boltCenterY = groundY + BigThunderHalfHeight * LightningRodBoltScale;
        Vector3 boltScale = (bigThunderVfxPrefab != null ? bigThunderVfxPrefab.transform.localScale : Vector3.one)
                            * LightningRodBoltScale;

        for (float elapsed = 0f; elapsed < LightningRodDuration; elapsed += LightningRodInterval)
        {
            // 연출과 판정을 같은 틱에 맞춘다 — 번개가 내리치는 순간 아래 루프가 피해·기절을 준다.
            // ⚠️ 풀은 localScale을 되돌려 주지 않는다 — 재사용본이 옛 크기로 나오지 않게 매번 직접 넣는다.
            if (bigThunderVfxPrefab != null)
            {
                for (int i = 0; i < LightningRodBoltsPerStrike; i++)
                {
                    float x = center.x + Random.Range(-1f, 1f) * radius * LightningRodBoltSpreadRatio;
                    GameObject bolt = ObjectPool.Instance.Spawn(bigThunderVfxPrefab, new Vector3(x, boltCenterY, 0f), Quaternion.identity);
                    if (bolt != null) bolt.transform.localScale = boltScale;
                }
            }

            foreach (Enemy e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                if (e == null || !e.IsAlive) continue;
                if (Vector2.Distance(center, e.transform.position) > radius) continue;

                float hit = PlayerPassives.ApplyCrit(damage * ratio, critChance, out bool isCrit);
                e.TakeDamage(hit, isLightningProc: true, isCrit: isCrit, source: ActiveSkillId.Lightning, rollLightning: false);
                if (e != null && e.IsAlive) e.ApplySlow(0f, LightningRodStun); // 감속 0 = 기절
            }
            yield return new WaitForSeconds(LightningRodInterval);
        }

        if (rod != null) Destroy(rod); // 6초가 끝나면 기둥도 같이 사라진다
    }

    private IEnumerator MiniEagleBonus(Enemy target, float damage, float critChance, float scale, ActiveSkillId source)
    {
        if (target == null) yield break;
        Vector3 pos = target.transform.position;
        yield return StartCoroutine(MeteorImpact(pos, scale));
        if (target == null) yield break;

        float hitDamage = PlayerPassives.ApplyCrit(damage, critChance, out bool isCrit);
        target.TakeDamage(hitDamage, isCrit: isCrit, source: source);
    }

    private IEnumerator MeteorImpact(Vector3 targetPos, float scale)
    {
        if (eagleDropPrefab == null) yield break;

        const float fallHeight = 6f;
        const float fallAngleFromVertical = 15f;
        Vector3 landPos = targetPos + new Vector3(0.4f, 0.6f, 0f);
        float horizontalOffset = fallHeight * Mathf.Tan(fallAngleFromVertical * Mathf.Deg2Rad);
        Vector3 start = landPos + new Vector3(horizontalOffset, fallHeight, 0f);
        GameObject eagle = Instantiate(eagleDropPrefab, start, Quaternion.identity);
        eagle.transform.localScale *= scale;

        float duration = 0.3f;
        float t = 0f;
        while (t < duration)
        {
            eagle.transform.position = Vector3.Lerp(start, landPos, t / duration);
            t += Time.deltaTime;
            yield return null;
        }
        Destroy(eagle);

        if (eagleImpactVfxPrefab != null)
        {
            GameObject impact = ObjectPool.Instance.Spawn(eagleImpactVfxPrefab, landPos, Quaternion.identity);
            impact.transform.localScale = Vector3.one * 0.25f * scale;
            ObjectPool.Instance.Despawn(impact, 2f);
        }
    }

    private static float GetDefaultCooldown(ActiveSkillId id)
    {
        SkillProgression p = Prog(id);
        return p != null ? p.baseCooldown : SkillProgression.DefaultBaseCooldown(id);
    }

    // 낙뢰 기본 피해는 고정 상수(BaseProcDamage)를 쓴다 — 실시간 ProcDamage는 배율이 적용된 '현재값'이라
    // 레벨업 성장 표시의 기준(기본값)으로 쓰면 부호가 뒤집힌다. 나머지 스킬은 progression 참조.
    // ── 에셋(Prog_*)이 정하는 시작값들 ────────────────────────────────────────
    // 넷 다 **0이면 "안 쓴다"** 는 뜻이라 호출부의 코드 기본값이 그대로 남는다(에셋을 안 채운 스킬은 현행 유지).
    private static int GetBasePierce(ActiveSkillId id) => Prog(id) != null ? Prog(id).basePierce : 0;
    private static int GetBaseProjectiles(ActiveSkillId id) => Prog(id) != null ? Prog(id).baseProjectiles : 0;

    // 지속시간은 스킬마다 코드 기본값이 달라서 호출부가 자기 기본값을 넘긴다.
    public static float BaseDuration(ActiveSkillId id, float codeDefault)
    {
        SkillProgression p = Prog(id);
        return p != null && p.baseDuration > 0f ? p.baseDuration : codeDefault;
    }

    private static float GetDefaultDamage(ActiveSkillId id)
    {
        if (id == ActiveSkillId.Lightning) return LightningStorm.BaseProcDamage;
        SkillProgression p = Prog(id);
        return p != null ? p.baseDamage : SkillProgression.DefaultBaseDamage(id);
    }
}
