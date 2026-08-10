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

    private const float MiniWhirlwindScale = 0.4f; // 미니 회오리 크기 배율(바닥선 보정 계산에도 쓰임)

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
        source == ActiveSkillId.BasicAttack ? Mathf.Max(1, BasicAttackHits) : 1;

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

    // Enemy.TakeSkillHit가 참조: 산탄 버프를 받은 공격이 플레이어 근처(ShotgunCloseRange) 적을 때릴 때 추가 타수(+2).
    // 스킬트리 "근거리 조준"(Shotgun_CloseBonus) 해금 시에만.
    public static int CloseRangeBonusHits(ActiveSkillId source, Vector3 enemyPos)
    {
        if (!MetaBonuses.ShotgunCloseBonus || instance == null) return 0;
        if (!IsShotgunBuffed(source)) return 0;
        return Vector2.Distance(instance.transform.position, enemyPos) <= ShotgunCloseRange ? 2 : 0;
    }

    // 버프류 스킬 = 지속시간 버프를 부여하고 HUD 우상단에 버프 아이콘이 뜨는 스킬 (산탄·낙뢰)
    public static bool IsBuffSkill(ActiveSkillId id) => id == ActiveSkillId.Shotgun || id == ActiveSkillId.Lightning;

    // 리프레쉬(재사용 초기화)가 발동될 때 — HUD가 구독해 리프레쉬 패시브 아이콘에 보잉 연출
    public static System.Action OnRefreshProc;

    [SerializeField] private GameObject basicAttackProjectilePrefab;
    // 화살 R0(암살 연계, path1) = 관통 무한 **큰 화살 한 발**. 그 한 발만 전용 그림으로 갈아끼운다
    // (뒤따르는 추적 화살은 명세상 "기본 화살"이라 원본 그대로 둔다).
    // 1차(T2)=암살 사격 / 2차(T3)=처형 사격으로 그림이 한 번 더 바뀐다.
    [SerializeField] private Sprite evolvedArrowSprite;
    [SerializeField] private Sprite evolvedArrowSpriteTier2;
    private const float FlyingArrowSpawnRaise = BalanceConstants.FlyingArrowSpawnRaise; // 비행 적 타격 진화 시 발사점 상승(세로 긴 히트박스와 합쳐 지상/비행 동시 커버)
    [SerializeField] private GameObject whirlwindPrefab;
    [SerializeField] private GameObject bigTornadoPrefab;
    [SerializeField] private GameObject orbPrefab;
    [SerializeField] private GameObject bigOrbPrefab; // 지식 연계 path1 T2부터 등장하는 큰 초록 오브 비주얼
    [SerializeField] private GameObject orbAltarPrefab;
    [SerializeField] private GameObject shotgunPelletPrefab; // 산탄 알(SmallOrb_Skill 재사용 — 방향성 단발 투사체)
    [SerializeField] private GameObject eagleDropPrefab;
    [SerializeField] private GameObject eagleImpactVfxPrefab;
    [SerializeField] private GameObject snipingEffectPrefab;      // 스나이핑 후속 타격 VFX(Effect_Sniping, 2~5번째 저격)
    [SerializeField] private GameObject snipingSplashPrefab;      // 스나이핑 첫 타격 강조 VFX(Effect_SplashSniping — 기본 이펙트 화려 버전)
    [SerializeField] private GameObject overkillSplashVfxPrefab;  // Route2 초과데미지 연쇄 전용 VFX(Vefects Impact Sparks)
    [SerializeField] private float overkillSplashVfxScale = 0.5f;
    [SerializeField] private GameObject homingMissilePrefab;      // 호밍 미사일 프리팹(추적)
    [SerializeField] private GameObject swingShockwavePrefab;     // 휘두르기 2루트 진화: 맵 끝까지 달리는 충격파
    [SerializeField] private Animator animator;
    [SerializeField] private EvolutionTierTextTableSO evolutionTextOverrides;
    [SerializeField] private SkillProgression[] progressions; // 스킬별 시작값+레벨 커브(Tier A). 미할당/미포함 스킬은 코드 기본 규칙 폴백(=현행)

    // 정적 GetDefault*/Apply/Describe가 인스턴스 필드를 못 읽으므로 Awake에서 static 조회맵으로 승격.
    private static System.Collections.Generic.Dictionary<ActiveSkillId, SkillProgression> progressionLookup;
    private static SkillProgression Prog(ActiveSkillId id) =>
        progressionLookup != null && progressionLookup.TryGetValue(id, out var p) ? p : null;

    [SerializeField] private AudioClip whirlwindCastSfx;
    [SerializeField] private AudioClip orbCastSfx;
    [SerializeField] private AudioClip eagleDropCastSfx;
    [SerializeField] private float castSfxVolume = 0.7f;
    [SerializeField] private float orbCastSfxVolume = 0.55f; // 원본 오브 발사음 자체가 다른 캐스트음보다 훨씬 크게(0dBFS 근접) 마스터링되어 있어 별도 볼륨 필요
    [SerializeField] private float whirlwindCastSfxVolume = 0.4f; // 원본 회오리 소환음 클립이 사실상 무음에 가까운 깨진 파일이었는데, 임포터 normalize 설정 때문에 재생 시 0dB까지 증폭되어 오히려 굉음으로 들리던 버그 — 정상 클립으로 교체 후 볼륨도 재보정

    private readonly List<EquippedSkill> equippedSkills = new List<EquippedSkill>();
    private float globalCooldownTimer;
    private float passiveDamageMultiplier = 1f;
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
        if (progressions == null) return;
        foreach (var p in progressions)
            if (p != null) progressionLookup[p.skill] = p;
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
        });
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
    private static LevelUpStep StepFor(ActiveSkillId id, int level)
    {
        SkillProgression p = Prog(id);
        return p != null ? p.StepForLevel(level) : SkillProgression.DefaultStep(id, level);
    }

    private static void ApplyUpgradeEffect(EquippedSkill skill, int level)
    {
        ApplyStep(skill, StepFor(skill.Id, level));
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
            case SkillStat.Duration: skill.ExtraWhirlwindDuration = Op(skill.ExtraWhirlwindDuration, s); break;
            case SkillStat.Scale: skill.Scale = Op(skill.Scale, s); break;
            case SkillStat.RewindAmount: skill.RewindAmount = Op(skill.RewindAmount, s); break;
            case SkillStat.TickRate: skill.TickIntervalMult = Mathf.Max(0.15f, Op(skill.TickIntervalMult, s)); break;
            case SkillStat.MaxTargets: skill.ExtraTargets = Mathf.RoundToInt(Op(skill.ExtraTargets, s)); break;
        }
    }

    public static string DescribeUpgradeEffect(EquippedSkill skill, int nextLevel) => DescribeStep(StepFor(skill.Id, nextLevel), skill.Id);

    // 미리보기 텍스트를 스텝 데이터에서 생성 → 미리보기·실제 적용이 항상 일치. (Apply와 같은 StepFor 참조)
    private static string DescribeStep(LevelUpStep s, ActiveSkillId id)
    {
        switch (s.stat)
        {
            case SkillStat.Damage:
                return s.op == StatOp.Multiply
                    ? $"피해량 {Mathf.RoundToInt((s.amount - 1f) * 100f)}% 증가"
                    : $"피해량 {s.amount:0.##} 증가";
            case SkillStat.Cooldown:
                return s.op == StatOp.Multiply
                    ? $"재사용 대기시간 {Mathf.RoundToInt((1f - s.amount) * 100f)}% 감소"
                    : $"재사용 대기시간 {-s.amount:0.##}초 감소";
            case SkillStat.ProjectileSpeed: return $"투사체 속도 {Mathf.RoundToInt(s.amount * 100f)}% 증가";
            case SkillStat.Pierce: return $"관통 {Mathf.RoundToInt(s.amount)}회 추가";
            case SkillStat.ProjectileCount: return $"투사체 +{Mathf.RoundToInt(s.amount)}";
            case SkillStat.ProcChance: return $"발동 확률 {Mathf.RoundToInt(s.amount * 100f)}%p 증가";
            case SkillStat.Duration: return $"지속시간 {s.amount:0.##}초 증가";
            case SkillStat.Scale: return $"크기 {Mathf.RoundToInt(s.amount * 100f)}% 증가";
            case SkillStat.RewindAmount: return $"되감기 시간 {s.amount:0.##}초 증가";
            case SkillStat.TickRate: return $"타격 주기 {Mathf.RoundToInt((1f - s.amount) * 100f)}% 빨라짐";
            // 오브만 "동시"가 아니라 사라지기 전까지 붙잡는 **총** 적 수(소모성 예산). 스나이핑은 동시 저격 대상 그대로.
            case SkillStat.MaxTargets:
                return id == ActiveSkillId.Orb
                    ? $"관통 대상 +{Mathf.RoundToInt(s.amount)}"
                    : $"동시 대상 +{Mathf.RoundToInt(s.amount)}";
            default: return "";
        }
    }

    // ── 진화 (2루트 × 2티어, 진화 아이템으로만 열림) ────────────────────────────
    // 1차: 만렙(Lv.10) 도달 → 열려 있는 루트 중 하나 선택 (고르면 Lv.1로 리셋)
    // 2차: 다시 만렙 도달 → 1차에서 고른 루트의 다음 티어(선택지 없음)
    public bool CanEvolve(EquippedSkill skill)
    {
        if (skill == null || skill.EvolutionStage >= EvolutionRoutes.MaxStage) return false;
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
        SfxPlayer.Play(SfxId.Evolution);

        // 기본 스탯 도약 + 레벨 표시 리셋(누적 레벨 TotalLevel은 유지 — 다음 진화 게이트 기준).
        // 레벨업 커브를 처음부터 다시 타므로 "새 스킬을 1레벨부터 키운다"는 감각이 된다.
        skill.Damage *= EvolutionRoutes.EvolveDamageMult;
        skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * EvolutionRoutes.EvolveCooldownMult);
        skill.Level = 1;
    }

    // 티어마다 정확히 하나의 효과만 부여한다. 여기 없는 조합은 PathTier를 직접 읽는 Fire*/TryUseSkill에서
    // 실시간으로 계산하는 기믹(관통 횟수, 분열 소환 수, 슬로우 강화, 낙뢰 재귀/스택 등)이라 영구 스탯 변경이 없다.
    private static void ApplyPathTierEffect(EquippedSkill skill, int path, int newTier)
    {
        switch (skill.Id, path, newTier)
        {
            // path0(기본)
            case (ActiveSkillId.BasicAttack, 0, 2):
                skill.Damage *= 2f; // 피해량 100% 증가
                break;
            case (ActiveSkillId.Whirlwind, 0, 2):
                MiniWhirlwindDamageBonus += 0.4f; // 미니 회오리 피해량 40% 증가
                break;
            case (ActiveSkillId.Whirlwind, 0, 3):
                MiniWhirlwindDamageBonus += 0.25f; // 미니 회오리 피해량 25% 추가 증가
                break;
            case (ActiveSkillId.Orb, 0, 2):
            case (ActiveSkillId.Lightning, 0, 2):
                skill.Damage *= 1.4f; // 피해량 40% 증가
                break;
            // EagleDrop path0 T2(화면 내 적 수 반비례 스케일링)는 EagleDropRoutine에서 매 캐스트마다 실시간 계산
            // Lightning path0 T3(재귀마다 피해량 누적 증가)는 LightningStorm.RecursiveDamageGrowth로 실시간 계산
            // Whirlwind path0 T1/T3(미니 회오리 개수)는 FireWhirlwind에서 매 캐스트마다 실시간 계산

            // path1(패시브 연계)
            case (ActiveSkillId.Whirlwind, 1, 1):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.8f); // 쿨감 20%
                break;
            // R0(산탄 연계) 1차 = 3초짜리 독수리 비. 화면 전체를 10틱 넘게 두들기는 값이라
            // 예전의 쿨감(×0.85) 대신 **쿨타임을 늘려** 대가를 치르게 한다("한 번 부르면 하늘이 덮이지만 자주 못 부른다").
            case (ActiveSkillId.EagleDrop, 1, 2):
                skill.Cooldown *= 1.4f;
                break;
            case (ActiveSkillId.Whirlwind, 1, 3):
            case (ActiveSkillId.Orb, 1, 3):
                // Orb 1,3은 문서상 슬로우 강화만이지만, 기존부터 쿨감도 함께 적용되던 걸 그대로 유지
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.9f);
                break;
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
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.5f); // 쿨감 50%
                break;
            case (ActiveSkillId.Lightning, 1, 1):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.75f); // 쿨감 25%
                break;
            case (ActiveSkillId.Lightning, 1, 3):
                skill.Damage *= 1.3f; // 피해량 30% 증가
                break;

            // path2(액티브 연계)
            case (ActiveSkillId.EagleDrop, 2, 1):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.6f); // 쿨감 40%
                break;

            // 스나이핑 (path0 타겟수·path1 T2 초과데미지연쇄·path2 T2 자동시전은 Fire/Update에서 실시간 처리)
            // ⚠️ 스나이핑은 (타겟 수 × 연사 수 × 피해)로 세 축이 전부 곱해지는 유일한 스킬이라
            //    피해 배수를 그대로 두면 최종 진화에서 혼자 압도적으로 세진다. 배수를 낮춰 축 하나를 눌러 둔다.
            case (ActiveSkillId.Sniping, 1, 1):
                skill.Damage *= 1.4f; // Route2 T1: 피해 40%
                break;
            case (ActiveSkillId.Sniping, 1, 3):
                skill.Damage *= 1.5f; // Route2 T3: 피해 50% (초과 피해가 커져 연쇄도 강해짐)
                break;
            case (ActiveSkillId.Sniping, 2, 1):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.6f); // Route3 T1: 쿨감 40%
                break;
            case (ActiveSkillId.Sniping, 2, 3):
                skill.Damage *= 1.2f; // Route3 T3: 자동시전 강화(피해 20%)
                break;

            // 호밍 미사일 (개수 path0·폭발 path1 T2는 FireHoming에서 실시간)
            case (ActiveSkillId.Homing, 1, 1):
                skill.Damage *= 1.3f; // Route2 T1: 미사일 피해 30%
                break;
            // R1(가속 연계, path2): 쿨타임 감소. 개수·크기는 FireHoming이 실시간으로 맡는다.
            case (ActiveSkillId.Homing, 2, 2):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.7f);
                break;
            case (ActiveSkillId.Homing, 2, 3):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.8f);
                break;
            // 산탄(Shotgun)은 전부 FireShotgun/FireShotgunPellets에서 실시간 계산(영구 스탯 변경 없음)

            // 되감기 Route3: 되감기 자체 쿨타임 감소 (T3의 글로벌 쿨감은 GlobalCooldownScale에서 실시간 처리)
            case (ActiveSkillId.Rewind, 2, 1):
            case (ActiveSkillId.Rewind, 2, 2):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.7f); // 쿨감 30%
                break;
            // 되감기 Route1(되감기 정도)·Route2(다음 스킬 피해)는 FireRewind에서 실시간 계산

            // 휘두르기 path1(힘 연계) 1차 = 타격 범위 1.45배. 범위가 곧 그대로 화력이라
            // 진화 공통 보너스(피해 1.5배·쿨 0.9배)까지 겹치면 혼자 압도적으로 세진다 —
            // 쿨타임 3배로 대가를 치르게 한다("한 방은 크지만 자주 못 쓴다").
            case (ActiveSkillId.Swing, 1, 2):
                skill.Cooldown *= 3f;
                break;

            // 휘두르기 path2(회오리 연계) 1차 = 맵 끝까지 가는 충격파. 본체 판정 밖의 적까지 닿는
            // 사실상 사거리 무제한 공격이라, 1루트와 마찬가지로 쿨타임으로 대가를 치르게 한다.
            case (ActiveSkillId.Swing, 2, 2):
                skill.Cooldown *= 1.8f;
                break;

            // 나머지 휘두르기 진화는 영구 스탯 변경이 없다 — 기절(path1)·충격파(path2) 전부
            // FireSwing/SwingRoutine에서 PathTier를 실시간으로 읽어 처리한다.
            // (진화 자체의 피해 1.5배·쿨 0.9배는 EvolutionRoutes가 공통으로 얹는다.)
        }
    }

    // 루트 잠금 조건은 EvolutionRoutes.RoutePrereq가 (스킬, 루트)별로 단독 소유한다(2026-08-06 개편).

    public static string GetActiveSkillName(ActiveSkillId id) => id switch
    {
        ActiveSkillId.BasicAttack => "화살 쏘기",
        ActiveSkillId.Whirlwind => "회오리",
        ActiveSkillId.Orb => "오브",
        ActiveSkillId.Lightning => "낙뢰",
        ActiveSkillId.EagleDrop => "독수리 투하",
        ActiveSkillId.Sniping => "스나이핑",
        ActiveSkillId.Homing => "호밍 미사일",
        ActiveSkillId.Shotgun => "산탄 장착",
        ActiveSkillId.Rewind => "되감기",
        ActiveSkillId.Swing => "휘두르기",
        _ => id.ToString(),
    };

    // ── 스킬 종류(공격/버프/유틸) ── 대부분 공격, 산탄=버프(타수↑), 되감기=유틸(쿨 되감기).
    public static SkillCategory GetSkillCategory(ActiveSkillId id) => id switch
    {
        ActiveSkillId.Shotgun => SkillCategory.Buff,
        ActiveSkillId.Rewind => SkillCategory.Utility,
        _ => SkillCategory.Attack,
    };

    public static string GetSkillCategoryLabel(SkillCategory c) => c switch
    {
        SkillCategory.Buff => "버프",
        SkillCategory.Utility => "유틸",
        _ => "공격",
    };

    private static string CategoryColorHex(SkillCategory c) => c switch
    {
        SkillCategory.Buff => "6FD3FF",    // 하늘 = 버프
        SkillCategory.Utility => "C7A8FF", // 보라 = 유틸
        _ => "FF8A6B",                     // 주황 = 공격
    };

    // 레벨업 선택지처럼 액티브/패시브가 섞여 나오는 곳에서 쓰는 종류 배지.
    public static string ActiveTypeBadge => "<size=68%><color=#FFD86B>[액티브]</color></size>";
    public static string PassiveTypeBadge => "<size=68%><color=#9BE86B>[패시브]</color></size>";

    // 레벨업 선택지 제목: 배지를 이름 뒤에 붙인다 — "회오리 [액티브] [공격]" / "힘 [패시브]"
    public static string GetActiveSkillTitleWithTags(ActiveSkillId id) =>
        $"{GetActiveSkillName(id)} {ActiveTypeBadge} {GetActiveSkillBadge(id).TrimEnd()}";

    public static string GetPassiveSkillTitleWithTags(PassiveSkillId id) =>
        $"{GetPassiveSkillName(id)} {PassiveTypeBadge}";

    // 이미 장착한 스킬은 진화 후 이름(DisplayName)으로 표시한다.
    public static string GetActiveSkillTitleWithTags(EquippedSkill s) =>
        $"{s.DisplayName} {ActiveTypeBadge} {GetActiveSkillBadge(s.Id).TrimEnd()}";

    public static string GetPassiveSkillTitleWithTags(EquippedPassive p) =>
        $"{p.DisplayName} {PassiveTypeBadge}";

    // 이름 앞에 붙이는 리치텍스트 배지("[공격] " 등). UI 요소 추가 없이 제목에 인라인.
    public static string GetActiveSkillBadge(ActiveSkillId id)
    {
        SkillCategory c = GetSkillCategory(id);
        return $"<size=68%><color=#{CategoryColorHex(c)}>[{GetSkillCategoryLabel(c)}]</color></size> ";
    }

    // 일시정지(ESC) 요약용: 이 스킬이 1레벨 기본값 대비 레벨업으로 얼마나 강해졌는지 항목별로 정리.
    // (진화 효과는 PauseMenu가 PathTier 제목으로 따로 표시하므로 여기선 레벨업 성장분만 다룬다)
    public static List<string> DescribeLevelUpGains(EquippedSkill s)
    {
        var lines = new List<string>();

        float baseDmg = GetDefaultDamage(s.Id);
        if (baseDmg > 0f)
        {
            int dmgPct = Mathf.RoundToInt((s.Damage / baseDmg - 1f) * 100f);
            if (dmgPct != 0) lines.Add($"피해 {(dmgPct > 0 ? "+" : "")}{dmgPct}%  ({baseDmg:0.#}→{s.Damage:0.#})");
        }

        float baseCd = GetDefaultCooldown(s.Id);
        if (baseCd > 0f)
        {
            int cdPct = Mathf.RoundToInt((1f - s.Cooldown / baseCd) * 100f);
            if (cdPct != 0) lines.Add($"재사용 대기시간 {(cdPct > 0 ? "-" : "+")}{Mathf.Abs(cdPct)}%  ({baseCd:0.#}→{s.Cooldown:0.#}초)");
        }

        if (s.ProjectileSpeedMultiplier > 1.0001f)
            lines.Add($"투사체 속도 +{Mathf.RoundToInt((s.ProjectileSpeedMultiplier - 1f) * 100f)}%");
        if (s.ExtraPierce > 0) lines.Add($"관통 +{s.ExtraPierce}회");
        if (s.ExtraProjectiles > 0) lines.Add($"발사 수 +{s.ExtraProjectiles}");
        if (s.ExtraTargets > 0) lines.Add(s.Id == ActiveSkillId.Orb ? $"관통 대상 +{s.ExtraTargets}" : $"동시 대상 +{s.ExtraTargets}");
        if (s.TickIntervalMult < 0.9999f) lines.Add($"타격 주기 -{Mathf.RoundToInt((1f - s.TickIntervalMult) * 100f)}%");
        if (s.ProcChanceBonus > 0f) lines.Add($"발동 확률 +{Mathf.RoundToInt(s.ProcChanceBonus * 100f)}%p");
        if (s.ExtraWhirlwindDuration > 0f) lines.Add($"지속시간 +{s.ExtraWhirlwindDuration:0.#}초");
        if (s.Scale > 1.0001f) lines.Add($"크기 +{Mathf.RoundToInt((s.Scale - 1f) * 100f)}%");
        if (s.Id == ActiveSkillId.Rewind) lines.Add($"되감기 시간 {s.RewindAmount:0.#}초");
        if (s.Id == ActiveSkillId.Homing && s.GrowthStacks > 0)
            lines.Add($"성장 스택 {s.GrowthStacks} (사용할수록 강해짐)");

        return lines;
    }

    public static string GetPassiveSkillName(PassiveSkillId id) => id switch
    {
        PassiveSkillId.Strength => "힘",
        PassiveSkillId.Health => "건강",
        PassiveSkillId.Knowledge => "지식",
        PassiveSkillId.Assassinate => "암살",
        PassiveSkillId.Refresh => "리프레쉬",
        PassiveSkillId.Defense => "방어",
        PassiveSkillId.Accel => "가속",
        _ => id.ToString(),
    };

    // 한 티어 = 한 효과. (id, path, tier)별로 실제로 적용되는 수치만 그대로 표기한다.
    // 에디터에서 등록한 텍스트 override가 있으면 그걸 쓰고, 없으면 기존 하드코딩 텍스트로 폴백.
    public string GetPathEffectText(ActiveSkillId id, int path, int tier)
    {
        if (evolutionTextOverrides != null && evolutionTextOverrides.TryGet(id, path, tier, out EvolutionTierTextEntry entry) && !string.IsNullOrEmpty(entry.description))
            return entry.description;
        return DescribePathEffect(id, path, tier);
    }

    public string GetPathTitleText(ActiveSkillId id, int path, int tier)
    {
        if (evolutionTextOverrides != null && evolutionTextOverrides.TryGet(id, path, tier, out EvolutionTierTextEntry entry) && !string.IsNullOrEmpty(entry.title))
            return entry.title;
        return GetPathTierTitle(id, path, tier);
    }

    // 🔴 진화 설명은 **일부러 수치를 안 쓴다.** BTD6 파라곤 설명처럼 그림만 던져서,
    //    "고르면 뭐가 나올까"라는 호기심으로 루트를 고르게 하는 게 목적이다.
    //    (실제 수치는 ApplyPathTierEffect와 각 Fire*에 있다 — 밸런스는 거기서 확인할 것.)
    //    티어1 카드에는 legacy 1·2가 **두 줄로 함께** 뜨므로, 두 문장이 이어 읽히게 쓸 것.
    public static string DescribePathEffect(ActiveSkillId id, int path, int tier) => (id, path, tier) switch
    {
        // BasicAttack
        (ActiveSkillId.BasicAttack, 0, 1) => "화살이 무엇도 개의치 않고 꿰뚫는다",
        (ActiveSkillId.BasicAttack, 0, 2) => "촉이 묵직해져 더 깊이 박힌다",
        (ActiveSkillId.BasicAttack, 0, 3) => "한 줄로 선 것들은 한 줄로 사라진다",
        (ActiveSkillId.BasicAttack, 1, 1) => "여러 발을 한 발에 담는다",
        (ActiveSkillId.BasicAttack, 1, 2) => "한 발이 줄을 통째로 꿰고, 급소를 찾으면 화살이 따라붙는다",
        (ActiveSkillId.BasicAttack, 1, 3) => "따라붙는 화살이 늘고 시위가 쉴 틈이 없다",
        (ActiveSkillId.BasicAttack, 2, 1) => "시위를 당기면 하늘이 어두워진다",
        (ActiveSkillId.BasicAttack, 2, 2) => "쏠 때마다 화살이 비처럼 쏟아진다",
        (ActiveSkillId.BasicAttack, 2, 3) => "그 비가 그칠 줄을 모른다",

        // Whirlwind
        (ActiveSkillId.Whirlwind, 0, 1) => "회오리가 둘로 늘어난다",
        (ActiveSkillId.Whirlwind, 0, 2) => "둘이 나란히 돌고, 사라진 자리엔 새끼가 남는다",
        (ActiveSkillId.Whirlwind, 0, 3) => "남는 새끼가 더 늘고 전부 사나워진다",
        (ActiveSkillId.Whirlwind, 1, 1) => "바람은 기다려 주지 않는다",
        (ActiveSkillId.Whirlwind, 1, 2) => "한 번 돌기 시작하면 멈출 이유가 없다",
        (ActiveSkillId.Whirlwind, 1, 3) => "부르는 즉시 온다",
        (ActiveSkillId.Whirlwind, 2, 1) => "지나간 자리에서 발이 땅에 붙는다",
        (ActiveSkillId.Whirlwind, 2, 2) => "모든 것을 갈아버리는 하나의 거대한 소용돌이",
        (ActiveSkillId.Whirlwind, 2, 3) => "한 번 갈린 것은 다시 서지 못한다",

        // Orb
        (ActiveSkillId.Orb, 0, 1) => "하늘에 있다고 안전하진 않다",
        (ActiveSkillId.Orb, 0, 2) => "구슬이 눈에 띄게 무거워진다",
        (ActiveSkillId.Orb, 0, 3) => "날개 달린 것들이 특히 후회한다",
        (ActiveSkillId.Orb, 1, 1) => "지나간 자리가 늪이 된다",
        (ActiveSkillId.Orb, 1, 2) => "더 크게, 더 자주, 더 푸르게",
        (ActiveSkillId.Orb, 1, 3) => "늪은 깊어지고 손은 빨라진다",
        (ActiveSkillId.Orb, 2, 1) => "닿은 것은 물러진다",
        (ActiveSkillId.Orb, 2, 2) => "구슬 하나 대신 작은 것들이 무리 지어 쫓는다",
        (ActiveSkillId.Orb, 2, 3) => "무리가 더 불어나고 스치는 것마다 물러진다",

        // Lightning
        (ActiveSkillId.Lightning, 0, 1) => "구름이 좀처럼 걷히지 않는다",
        (ActiveSkillId.Lightning, 0, 2) => "쌓인 구름이 모든 일격에 실린다",
        (ActiveSkillId.Lightning, 0, 3) => "쌓일수록 감당이 안 된다",
        (ActiveSkillId.Lightning, 1, 1) => "폭풍이 짧게 쉰다",
        (ActiveSkillId.Lightning, 1, 2) => "땅 한가운데 쇠기둥이 박히고 하늘이 그것만 노린다",
        (ActiveSkillId.Lightning, 1, 3) => "기둥이 더 오래, 더 넓게 하늘을 끌어당긴다",
        (ActiveSkillId.Lightning, 2, 1) => "폭풍이 오래 머문다",
        (ActiveSkillId.Lightning, 2, 2) => "회오리가 불면 폭풍도 눌러앉는다",
        (ActiveSkillId.Lightning, 2, 3) => "머문 폭풍이 모든 일격에 실린다",

        // EagleDrop
        (ActiveSkillId.EagleDrop, 0, 1) => "한 마리가 더 날아온다",
        (ActiveSkillId.EagleDrop, 0, 2) => "먹잇감이 적을수록 더 크게 덮친다",
        (ActiveSkillId.EagleDrop, 0, 3) => "쉬지 않고 쏟아진다",
        (ActiveSkillId.EagleDrop, 1, 1) => "한 마리가 오래 머문다",
        (ActiveSkillId.EagleDrop, 1, 2) => "부르면 하늘이 독수리로 뒤덮인다",
        (ActiveSkillId.EagleDrop, 1, 3) => "뒤덮인 하늘이 좀처럼 걷히지 않는다",
        (ActiveSkillId.EagleDrop, 2, 1) => "적게, 대신 훨씬 자주",
        (ActiveSkillId.EagleDrop, 2, 2) => "발톱이 닿은 자리에 바람이 남는다",
        (ActiveSkillId.EagleDrop, 2, 3) => "남은 바람이 오래 갈아댄다",

        // Sniping (Route1=path0 타겟수, Route2=path1 피해+스플래시, Route3=path2 자동시전)
        (ActiveSkillId.Sniping, 0, 1) => "조준선이 하나 더 생긴다",
        (ActiveSkillId.Sniping, 0, 2) => "명단에 이름이 더 적힌다",
        (ActiveSkillId.Sniping, 0, 3) => "명단이 끝을 모르고 길어진다",
        (ActiveSkillId.Sniping, 1, 1) => "한 발의 무게가 달라진다",
        (ActiveSkillId.Sniping, 1, 2) => "총알이 꽂힌 자리로 독수리가 내려앉는다",
        (ActiveSkillId.Sniping, 1, 3) => "내려앉는 것들이 훨씬 많아진다",
        (ActiveSkillId.Sniping, 2, 1) => "장전이 빨라진다",
        (ActiveSkillId.Sniping, 2, 2) => "손을 떼도 총구가 스스로 움직인다",
        (ActiveSkillId.Sniping, 2, 3) => "눈 깜빡일 새가 없다",

        // Homing (Route1=path0 미사일 수, Route2=path1 폭발, Route3=path2 성장률)
        (ActiveSkillId.Homing, 0, 1) => "탄창이 두 배로 두꺼워진다",
        (ActiveSkillId.Homing, 0, 2) => "하늘이 미사일로 덮인다",
        (ActiveSkillId.Homing, 0, 3) => "세어 볼 생각을 접게 된다",
        (ActiveSkillId.Homing, 1, 1) => "탄두가 묵직해진다",
        (ActiveSkillId.Homing, 1, 2) => "닿는 순간 터진다",
        (ActiveSkillId.Homing, 1, 3) => "터진 자리가 훨씬 넓다",
        (ActiveSkillId.Homing, 2, 1) => "탄이 가벼워진다",
        (ActiveSkillId.Homing, 2, 2) => "작아진 만큼 몇 배로 쏟아지고 손도 빨라진다",
        (ActiveSkillId.Homing, 2, 3) => "세어 볼 엄두가 안 난다",

        // Shotgun (Route1=path0 타수, Route2=path1 집중산탄, Route3=path2 전체산탄+기절)
        (ActiveSkillId.Shotgun, 0, 1) => "모든 스킬이 한 번 더 나간다",
        (ActiveSkillId.Shotgun, 0, 2) => "손이 하나 더 붙은 것 같다",
        (ActiveSkillId.Shotgun, 0, 3) => "무엇을 눌러도 쏟아진다",
        (ActiveSkillId.Shotgun, 1, 1) => "열기가 오래 남는다",
        (ActiveSkillId.Shotgun, 1, 2) => "가장 센 하나에 전부 몰아준다",
        (ActiveSkillId.Shotgun, 1, 3) => "그 하나가 감당이 안 된다",
        (ActiveSkillId.Shotgun, 2, 1) => "열기가 더 오래 남는다",
        (ActiveSkillId.Shotgun, 2, 2) => "흩어지던 알이 정면 한 줄기로 몰린다",
        (ActiveSkillId.Shotgun, 2, 3) => "그 한 줄기가 훨씬 두꺼워진다",

        // Rewind (Route1=path0 되감기 정도, Route2=path1 다음 스킬 피해, Route3=path2 쿨감+글로벌쿨감)
        (ActiveSkillId.Rewind, 0, 1) => "시간이 조금 물러선다",
        (ActiveSkillId.Rewind, 0, 2) => "더 멀리 물러선다",
        (ActiveSkillId.Rewind, 0, 3) => "방금 쓴 것이 없던 일이 된다",
        (ActiveSkillId.Rewind, 1, 1) => "되감긴 다음 한 방이 더 아프다",
        (ActiveSkillId.Rewind, 1, 2) => "훨씬 더 아프다",
        (ActiveSkillId.Rewind, 1, 3) => "그 한 방이 판을 끝낸다",
        (ActiveSkillId.Rewind, 2, 1) => "되감는 것도 빨라진다",
        (ActiveSkillId.Rewind, 2, 2) => "더 빨라진다",
        (ActiveSkillId.Rewind, 2, 3) => "모든 것이 절반의 시간에 돌아온다",

        (ActiveSkillId.Swing, 1, 1) => "팔이 더 크게 돈다",
        (ActiveSkillId.Swing, 1, 2) => "한 번에 훨씬 넓게 쓸어담는다",
        (ActiveSkillId.Swing, 1, 3) => "밀쳐진 것들이 한동안 일어나질 못한다",
        (ActiveSkillId.Swing, 2, 1) => "내려찍은 충격이 땅을 타고 번진다",
        (ActiveSkillId.Swing, 2, 2) => "그 충격이 저 끝까지 달려나간다",
        (ActiveSkillId.Swing, 2, 3) => "달려나가는 충격이 훨씬 사나워진다",

        _ => "",
    };

    // 진화 카드에 붙는 짧은 제목. 설명과 마찬가지로 **기능명이 아니라 별명**이다 — 뭘 하는지는 눌러 봐야 안다.
    public static string GetPathTierTitle(ActiveSkillId id, int path, int tier) => (id, path, tier) switch
    {
        (ActiveSkillId.BasicAttack, 0, 1) => "꿰뚫는 화살",
        (ActiveSkillId.BasicAttack, 0, 2) => "더 무거운 촉",
        (ActiveSkillId.BasicAttack, 0, 3) => "일렬로 사라진다",
        (ActiveSkillId.BasicAttack, 1, 1) => "한 발에 담는다",
        (ActiveSkillId.BasicAttack, 1, 2) => "줄을 꿰는 한 발",
        (ActiveSkillId.BasicAttack, 1, 3) => "따라붙는 화살",
        (ActiveSkillId.BasicAttack, 2, 1) => "어두워지는 하늘",
        (ActiveSkillId.BasicAttack, 2, 2) => "쏟아지는 화살비",
        (ActiveSkillId.BasicAttack, 2, 3) => "그치지 않는 비",

        (ActiveSkillId.Whirlwind, 0, 1) => "둘로 늘어난다",
        (ActiveSkillId.Whirlwind, 0, 2) => "남기고 가는 새끼",
        (ActiveSkillId.Whirlwind, 0, 3) => "사나워진 무리",
        (ActiveSkillId.Whirlwind, 1, 1) => "기다리지 않는 바람",
        (ActiveSkillId.Whirlwind, 1, 2) => "멈출 이유가 없다",
        (ActiveSkillId.Whirlwind, 1, 3) => "부르면 온다",
        (ActiveSkillId.Whirlwind, 2, 1) => "발이 땅에 붙는다",
        (ActiveSkillId.Whirlwind, 2, 2) => "모든 것을 갈아버리는 대회오리",
        (ActiveSkillId.Whirlwind, 2, 3) => "다시 서지 못한다",

        (ActiveSkillId.Orb, 0, 1) => "하늘도 예외 없다",
        (ActiveSkillId.Orb, 0, 2) => "무거운 구슬",
        (ActiveSkillId.Orb, 0, 3) => "날개 달린 후회",
        (ActiveSkillId.Orb, 1, 1) => "지나간 자리의 늪",
        (ActiveSkillId.Orb, 1, 2) => "크고 푸른 구슬",
        (ActiveSkillId.Orb, 1, 3) => "깊어지는 늪",
        (ActiveSkillId.Orb, 2, 1) => "물러지는 것들",
        (ActiveSkillId.Orb, 2, 2) => "쫓아가는 작은 것들",
        (ActiveSkillId.Orb, 2, 3) => "불어난 무리",

        (ActiveSkillId.Lightning, 0, 1) => "걷히지 않는 구름",
        (ActiveSkillId.Lightning, 0, 2) => "일격마다 실리는 구름",
        (ActiveSkillId.Lightning, 0, 3) => "쌓일수록 사납게",
        (ActiveSkillId.Lightning, 1, 1) => "짧은 숨",
        (ActiveSkillId.Lightning, 1, 2) => "땅에 박은 쇠기둥",
        (ActiveSkillId.Lightning, 1, 3) => "하늘을 끌어당긴다",
        (ActiveSkillId.Lightning, 2, 1) => "머무는 폭풍",
        (ActiveSkillId.Lightning, 2, 2) => "눌러앉은 폭풍",
        (ActiveSkillId.Lightning, 2, 3) => "일격마다 실리는 폭풍",

        (ActiveSkillId.EagleDrop, 0, 1) => "한 마리 더",
        (ActiveSkillId.EagleDrop, 0, 2) => "적을수록 크게",
        (ActiveSkillId.EagleDrop, 0, 3) => "쏟아지는 하늘",
        (ActiveSkillId.EagleDrop, 1, 1) => "오래 머문다",
        (ActiveSkillId.EagleDrop, 1, 2) => "하늘을 뒤덮는다",
        (ActiveSkillId.EagleDrop, 1, 3) => "걷히지 않는 하늘",
        (ActiveSkillId.EagleDrop, 2, 1) => "적게, 대신 자주",
        (ActiveSkillId.EagleDrop, 2, 2) => "발톱이 남긴 바람",
        (ActiveSkillId.EagleDrop, 2, 3) => "오래 가는 바람",

        (ActiveSkillId.Sniping, 0, 1) => "또 하나의 조준선",
        (ActiveSkillId.Sniping, 0, 2) => "길어지는 명단",
        (ActiveSkillId.Sniping, 0, 3) => "명단의 끝",
        (ActiveSkillId.Sniping, 1, 1) => "달라진 무게",
        (ActiveSkillId.Sniping, 1, 2) => "내려앉는 독수리",
        (ActiveSkillId.Sniping, 1, 3) => "더 많이 내려앉는다",
        (ActiveSkillId.Sniping, 2, 1) => "빠른 장전",
        (ActiveSkillId.Sniping, 2, 2) => "스스로 움직이는 총구",
        (ActiveSkillId.Sniping, 2, 3) => "깜빡일 새 없이",

        (ActiveSkillId.Homing, 0, 1) => "두꺼운 탄창",
        (ActiveSkillId.Homing, 0, 2) => "미사일로 덮인 하늘",
        (ActiveSkillId.Homing, 0, 3) => "셀 수 없다",
        (ActiveSkillId.Homing, 1, 1) => "묵직한 탄두",
        (ActiveSkillId.Homing, 1, 2) => "닿으면 터진다",
        (ActiveSkillId.Homing, 1, 3) => "넓어진 불길",
        (ActiveSkillId.Homing, 2, 1) => "가벼워진 탄",
        (ActiveSkillId.Homing, 2, 2) => "몇 배로 쏟아진다",
        (ActiveSkillId.Homing, 2, 3) => "셀 엄두가 안 난다",

        (ActiveSkillId.Shotgun, 0, 1) => "한 번 더",
        (ActiveSkillId.Shotgun, 0, 2) => "손이 하나 더",
        (ActiveSkillId.Shotgun, 0, 3) => "무엇을 눌러도",
        (ActiveSkillId.Shotgun, 1, 1) => "오래 남는 열기",
        (ActiveSkillId.Shotgun, 1, 2) => "하나에 전부",
        (ActiveSkillId.Shotgun, 1, 3) => "감당 못 할 하나",
        (ActiveSkillId.Shotgun, 2, 1) => "더 오래",
        (ActiveSkillId.Shotgun, 2, 2) => "정면 한 줄기",
        (ActiveSkillId.Shotgun, 2, 3) => "두꺼워진 줄기",

        (ActiveSkillId.Rewind, 0, 1) => "물러서는 시간",
        (ActiveSkillId.Rewind, 0, 2) => "더 멀리",
        (ActiveSkillId.Rewind, 0, 3) => "없던 일",
        (ActiveSkillId.Rewind, 1, 1) => "더 아픈 한 방",
        (ActiveSkillId.Rewind, 1, 2) => "훨씬 더",
        (ActiveSkillId.Rewind, 1, 3) => "판을 끝내는 한 방",
        (ActiveSkillId.Rewind, 2, 1) => "빨라지는 되감기",
        (ActiveSkillId.Rewind, 2, 2) => "더 빨리",
        (ActiveSkillId.Rewind, 2, 3) => "절반의 시간",

        (ActiveSkillId.Swing, 1, 1) => "크게 도는 팔",
        (ActiveSkillId.Swing, 1, 2) => "한 아름씩",
        (ActiveSkillId.Swing, 1, 3) => "일어나질 못한다",
        (ActiveSkillId.Swing, 2, 1) => "땅을 타는 충격",
        (ActiveSkillId.Swing, 2, 2) => "끝까지 달린다",
        (ActiveSkillId.Swing, 2, 3) => "사나운 진동",

        _ => "",
    };

    private void TryUseSkill(EquippedSkill skill)
    {
        if (globalCooldownTimer > 0f || skill.CooldownTimer > 0f) return;

        // 타격 기준 치명타: 캐스트 시점엔 확률만 확정하고, 실제 치명타 여부는 각 데미지 이벤트(투사체 명중/틱)마다 개별적으로 굴린다.
        float critChance = GetCritChance(skill);
        float damage = ComputeBaseDamage(skill.Damage, skill.Id);

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
                float baseDuration = 10f * (skill.PathTier[0] >= 2 ? 1.3f : 1f) * MetaBonuses.DurationMult;
                // 기존 스택을 지우지 않고 새로 추가한다 — 평소엔 쿨타임이 지속시간보다 길어 이전 스택이 이미 만료된 상태지만,
                // 지속시간이 늘어나 있으면 새 캐스트가 기존 스택 위에 쌓인다.
                LightningStorm.AddStack(baseDuration);
                LightningStorm.ProcChance = LightningStorm.BaseProcChance + skill.ProcChanceBonus;
                LightningStorm.ProcDamage = damage;
                LightningStorm.StackDamageEnabled = skill.PathTier[0] >= 2;
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
        }

        globalCooldownTimer = GlobalCooldown * GlobalCooldownScale(); // 되감기 Route3 T3: 전역 쿨타임 절반
        // 스나이핑 자동시전(path2 T2+)은 스킬 쿨타임 대신 고정 간격(T2=3초, T3=1.5초)으로 발동
        // (오브 설치기의 긴 전용 쿨은 오브 R1이 추적 오브 무리로 바뀌며 사라졌다 — 이제 평범한 스킬 쿨을 쓴다)
        float baseCd = (skill.Id == ActiveSkillId.Sniping && skill.PathTier[2] >= 2) ? (skill.PathTier[2] >= 3 ? 1.5f : 3f)
            : skill.Cooldown;
        float cdMult = MetaBonuses.CooldownMult;
        // 스킬트리 "신속한 회오리": 회오리는 쿨타임 감소분(1-CooldownMult)을 1.5배로 받음
        if (skill.Id == ActiveSkillId.Whirlwind && MetaBonuses.WhirlwindCooldownBonus)
            cdMult = Mathf.Max(0.05f, 1f - 1.5f * (1f - MetaBonuses.CooldownMult));
        // 리프레쉬 연계 path3 T1: 버프류 스킬(산탄·낙뢰) 쿨타임 감소
        if (PlayerPassives.BuffSkillCooldownMult < 1f && IsBuffSkill(skill.Id))
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
            if (skill.Id == ActiveSkillId.Whirlwind && skill.PathTier[1] >= 2)
                ReduceAllCooldowns(1f);
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
        FireSwing(ComputeBaseDamage(swing.Damage, ActiveSkillId.Swing) * damageMult, GetCritChance(swing), swing);
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
        return Mathf.Min(chance, MaxCritChance);
    }

    private float ComputeBaseDamage(float baseDamage, ActiveSkillId skillId)
    {
        // 힘 패시브 계열 피해 배율은 하나의 덧셈 풀로 합친다 — path0(전 스킬 공통)과 path2(Q 슬롯 전용)를
        // 곱연산으로 각각 겹쳐 쌓으면 그 스킬에서 배율이 폭발한다. 같은 풀에서 더한 뒤 한 번만 곱한다.
        float damageMultiplier = passiveDamageMultiplier;
        // 힘 연계 path2(패시브): **Q키에 할당된 스킬** 전용 추가 피해량 (덧셈 합류).
        // Q = 가장 먼저 얻은 스킬 = equippedSkills[0] (AcquireSkill이 SlotKeys를 순서대로 붙인다).
        if (PlayerPassives.FirstSlotDamageMultiplierBonus > 0f && equippedSkills.Count > 0 && equippedSkills[0].Id == skillId)
            damageMultiplier += PlayerPassives.FirstSlotDamageMultiplierBonus;

        float damage = baseDamage * damageMultiplier;

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
        int pierce = 0; // 기본 path: 관통 (T1=3회, T3=추가 10회)
        if (skill.PathTier[0] >= 1) pierce += 3;
        if (skill.PathTier[0] >= 3) pierce += 10;
        pierce += skill.ExtraPierce; // 레벨업 고유 강화

        // R0(암살 연계, path1) = **개쎈 화살 한 발**. 여러 발 쏘던 것을 한 발로 모으고 관통을 무한으로 준다
        // (방패 블루베리는 그래도 막는다 — Projectile이 BlocksProjectiles에서 끊는다. 2026-08-06 명세 그대로).
        if (skill.PathTier[1] >= 2) pierce = int.MaxValue;

        // 첫 발은 즉시(입력 반응성), 추가 발사는 시간차를 두고 연사한다.
        SpawnBasicAttackProjectile(skill, damage, critChance, pierce, 0f, allowBonusShot);
        if (BasicAttackBurstCount(skill) > 0)
            StartCoroutine(BasicAttackBurst(skill, damage, critChance, pierce, allowBonusShot));

        // R1(독수리 연계, path2) = 시전할 때마다 **화면 전체에 비스듬한 화살비**가 2회 내린다.
        if (skill.PathTier[2] >= 2)
            StartCoroutine(ArrowRainRoutine(skill, damage, critChance, waves: skill.PathTier[2] >= 3 ? 3 : 2));

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
                p.SpeedMultiplier = skill.ProjectileSpeedMultiplier;
                p.PierceRemaining = 0;   // 명세: 화살비의 각 화살은 관통 없음
                p.CanHitFlying = true;   // 위에서 떨어지므로 비행 적을 자연히 지나간다
            }
            yield return new WaitForSeconds(ArrowRainWaveInterval);
        }
    }

    // R0(암살 연계)은 **한 발**로 모은다 — 큰 화살 하나가 줄을 통째로 뚫는 게 정체성이라 연사가 있으면 안 된다.
    // R1(독수리 연계)은 화살비가 따로 쏟아지므로 추가 연사를 **절반으로** 깎는다.
    // ⚠️ 영구 스탯을 깎지 않고 매 캐스트 실시간으로 계산한다 — 진화 후 레벨업으로 발수를 더 얻어도 그대로 유지된다.
    private static int BasicAttackBurstCount(EquippedSkill skill) =>
        skill.PathTier[1] >= 2 ? 0
        : skill.PathTier[2] >= 2 ? skill.ExtraProjectiles / 2
        : skill.ExtraProjectiles;

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
        bool canHitFlying = skill.PathTier[0] >= 1;
        // 비행 적 타격 진화 시 발사점을 위로 올려 발사한다(비행 적은 y≈1.1 위쪽 띠에 있어 지상 높이 수평 발사로는 안 맞음).
        // 세로로 긴 히트박스와 합쳐, 수평으로 날아가는 한 발이 지상·비행 띠를 동시에 지나가게 한다 — 진화 전에는 원래 높이 유지.
        float spawnRaise = canHitFlying ? FlyingArrowSpawnRaise : 0f;
        GameObject obj = Instantiate(basicAttackProjectilePrefab, transform.position + Vector3.left * 0.6f + Vector3.down * 0.25f + Vector3.up * (verticalOffset + spawnRaise), Quaternion.identity);
        obj.transform.localScale *= skill.Scale;
        Projectile projectile = obj.GetComponent<Projectile>();
        projectile.Damage = damage;
        projectile.CritChance = critChance;
        projectile.SpeedMultiplier = skill.ProjectileSpeedMultiplier;
        projectile.PierceRemaining = pierce;
        projectile.CanHitFlying = canHitFlying;
        if (skill.PathTier[1] >= 2) ApplyEvolvedArrowSprite(obj, skill.PathTier[1] >= 3);

        // R0(암살 연계, path1): 이 화살이 **치명타로 때린 적마다** 기본 화살이 따로 날아가 그 적만 노린다.
        // 관통 무한이라 한 발이 여러 적에게 치명타를 낼 수 있고, 그때마다 각각 따라붙는다(명세 그대로).
        bool critChaseArrow = allowBonusShot && skill.PathTier[1] >= 2;
        int chaseArrows = skill.PathTier[1] >= 3 ? 2 : 1;

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
        Sprite sprite = tier2 && evolvedArrowSpriteTier2 != null ? evolvedArrowSpriteTier2 : evolvedArrowSprite;
        if (sprite == null) return;
        SpriteRenderer sr = projectileObj.GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.sprite = sprite;
    }

    // R0의 따라붙는 기본 화살 — 맞은 그 적을 향해 조준해 쏘고, **관통은 없다**(그 대상만 때리고 사라진다).
    // 진짜 유도가 아니라 발사 시점 조준이다. 적이 느려서 대부분 꽂히고, 코드는 Projectile 그대로 쓴다.
    private const float ChasingArrowDamageRatio = 0.5f;

    private void SpawnChasingArrow(EquippedSkill skill, Enemy target, float damage, float critChance)
    {
        if (basicAttackProjectilePrefab == null || target == null || !target.IsAlive) return;

        Vector3 origin = transform.position + Vector3.left * 0.6f + Vector3.down * 0.25f;
        Vector2 toTarget = (Vector2)target.transform.position - (Vector2)origin;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        // Projectile은 자기 로컬 left로 날아간다 → left가 목표를 향하도록 회전시킨다.
        float angle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg + 180f;

        GameObject obj = Instantiate(basicAttackProjectilePrefab, origin, Quaternion.Euler(0f, 0f, angle));
        obj.transform.localScale *= skill.Scale;
        Projectile p = obj.GetComponent<Projectile>();
        if (p == null) return;
        p.Damage = damage * ChasingArrowDamageRatio;
        p.CritChance = critChance;
        p.SpeedMultiplier = skill.ProjectileSpeedMultiplier;
        p.PierceRemaining = 0;
        p.CanHitFlying = target.RequiresAntiAir; // 비행 적을 노리고 쏜 화살이면 그 적은 맞힐 수 있어야 한다
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
        if (skill.PathTier[0] >= 1) targets += 1; // Route1 T1: +1 (총 2)
        if (skill.PathTier[0] >= 2) targets += 1; // T2(타겟수++): +1 (총 3)
        if (skill.PathTier[0] >= 3) targets += 2; // T3: +2 (총 5)
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
            StartCoroutine(SnipeTarget(target, damage, critChance, eagleSplash, eagleRatio, eagleTargets, shots, skill.Scale));
        return true;
    }

    private IEnumerator SnipeTarget(Enemy target, float damage, float critChance, bool eagleSplash, float eagleRatio, int eagleTargets, int shots, float scale)
    {
        for (int i = 0; i < shots; i++)
        {
            if (target == null) yield break;
            Vector3 pos = target.transform.position;

            // 첫 타격은 화려한 스플래시-룩(Effect_SplashSniping), 나머지 저격은 기본 스파크(Effect_Sniping).
            GameObject sparkPrefab = i == 0 ? snipingSplashPrefab : snipingEffectPrefab;
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

        // 레벨업 주 성장축: 미사일 수. Route1(path0) 진화는 그 위에 배수로 얹힌다(레벨업=+1씩, 진화=배수).
        int count = HomingBaseMissiles + skill.ExtraProjectiles;
        if (skill.PathTier[0] >= 3) count *= 5; else if (skill.PathTier[0] >= 2) count *= 3; else if (skill.PathTier[0] >= 1) count *= 2;

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

        float duration = 5f + (skill.PathTier[1] >= 1 ? 2f : 0f) + (skill.PathTier[2] >= 1 ? 2f : 0f);
        // Route1(path0): 공격 횟수 추가
        int bonus = 1 + (skill.PathTier[0] >= 1 ? 1 : 0) + (skill.PathTier[0] >= 2 ? 1 : 0) + (skill.PathTier[0] >= 3 ? 2 : 0);
        // Route2(path1) T2: 최고 공격력 스킬 1개에만, 보너스 2배(T3=3배)
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

    // ── 범위 표시 ──
    // 피해 판정이 직사각형이라 표시도 직사각형이다(원으로 그리면 모서리가 어긋난다).
    // 휘두르기 전용 — 시전하고 피해가 들어갈 때까지 떠 있다가 타격 후 사라진다.
    private const float SwingRangeAlpha = 0.28f;
    private const float SwingRangeFadeDuration = 0.25f;
    private const int SwingRangeSortingOrder = 0;   // 적(100+)보다 뒤에 깔린다
    private static readonly Color SwingRangeColor = new Color(1f, 0.85f, 0.15f, SwingRangeAlpha);
    private static Sprite swingRangeSprite;

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

        SpriteRenderer range = SpawnSwingRange(reach, halfHeight);

        yield return new WaitForSeconds(SwingImpactDelay); // 내려찍는 순간까지 기다린다

        // 타격 이펙트는 별도 VFX가 아니라 **4번째 프레임 그림에 그려져 있다**(사용자 아트).
        // 파티클을 겹쳐 봤지만 도트 그림을 가려서 뺐다 — 연출을 더하려면 그림 쪽을 먼저 볼 것.
        // 1루트 1차부터 흡혈이 붙는다 — 범위·피해만 늘던 루트에 "버티는" 성격을 준다.
        int lifesteal = skill.PathTier[1] >= 2 ? SwingLifestealPerHit : 0;
        SwingHit(damage, critChance, reach, halfHeight, SwingKnockback, stun, lifesteal);

        if (shockwave) SpawnShockwave(damage, critChance, empoweredShock);

        // 피해가 들어간 뒤에야 범위 표시가 사라진다 — "어디까지 맞았는지"를 결과와 함께 보여준다.
        float t = 0f;
        while (range != null && t < SwingRangeFadeDuration)
        {
            t += Time.deltaTime;
            Color c = SwingRangeColor;
            c.a = Mathf.Lerp(SwingRangeAlpha, 0f, t / SwingRangeFadeDuration);
            range.color = c;
            yield return null;
        }
        if (range != null) Destroy(range.gameObject);
    }

    // 판정과 **똑같은** 사각형을 깔아 준다: 앞(왼쪽)으로 SwingNearOffset~reach, 위아래 halfHeight.
    // ⚠️ 플레이어의 localScale이 1.5라 자식으로 붙이면 크기가 곱해진다 — 월드에 독립으로 둔다.
    private SpriteRenderer SpawnSwingRange(float reach, float halfHeight)
    {
        if (swingRangeSprite == null)
        {
            // 1×1유닛짜리 흰 사각형 하나면 충분하다(크기는 localScale로 준다) — 별도 아트 파일이 필요 없다.
            Texture2D tex = Texture2D.whiteTexture;
            swingRangeSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        }

        GameObject go = new GameObject("SwingRange", typeof(SpriteRenderer));
        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = swingRangeSprite;
        sr.color = SwingRangeColor;
        sr.sortingOrder = SwingRangeSortingOrder;

        go.transform.position = transform.position
            + Vector3.left * ((SwingNearOffset + reach) * 0.5f)
            + Vector3.up * SwingCenterYOffset;
        go.transform.localScale = new Vector3(reach - SwingNearOffset, halfHeight * 2f, 1f);
        return sr;
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
        float py = transform.position.y + SwingCenterYOffset; // 범위 표시(SpawnSwingRange)와 같은 중심을 쓴다

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
        }
    }

    // ── 되감기: 다른 스킬의 쿨타임을 앞당긴다 ──
    private void FireRewind(EquippedSkill skill)
    {
        // 되감기 정도(앞당길 시간). Route1(path0)로 증가.
        float amount = skill.RewindAmount;
        if (skill.PathTier[0] >= 1) amount += 0.5f;
        if (skill.PathTier[0] >= 2) amount += 0.5f;
        if (skill.PathTier[0] >= 3) amount += 1f;

        foreach (EquippedSkill s in equippedSkills)
            if (s != skill) s.CooldownTimer = Mathf.Max(0f, s.CooldownTimer - amount);

        // 스킬트리 "블루베리 둔화"(Rewind_Slow) 해금 시: 되감을 때 모든 적을 천천히 감아 둔화(50% 감속, 2초)
        if (MetaBonuses.RewindSlowAll)
            foreach (Enemy e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                if (e != null) e.ApplySlow(0.5f, 2f);

        // Route2(path1): 다음에 사용하는 스킬의 피해를 1회 증가 (ComputeBaseDamage가 소비)
        if (skill.PathTier[1] >= 1)
            nextSkillDamageBonus = skill.PathTier[1] >= 3 ? 1.0f : skill.PathTier[1] >= 2 ? 0.6f : 0.3f;

        animator.SetTrigger("Attack");
    }

    // 되감기 Route3 T3을 보유하면 전역 쿨타임(GCD)이 절반이 된다.
    private float GlobalCooldownScale()
    {
        foreach (EquippedSkill s in equippedSkills)
            if (s.Id == ActiveSkillId.Rewind && s.PathTier[2] >= 3) return 0.5f;
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

        EquippedSkill lightning = equippedSkills.FirstOrDefault(s => s.Id == ActiveSkillId.Lightning);
        if (lightning != null && lightning.PathTier[2] >= 2) // 낙뢰 연계 path T2: 살아있는 낙뢰 스택 전부 지속시간 연장
        {
            LightningStorm.ExtendActiveStacks(1.5f);
            RefreshLightningBuffDisplay();
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

    // 적이 오는 왼쪽으로 부채꼴 산탄. 알이 늘어도 각도는 그대로라 **촘촘해지는 것**이 눈에 보인다.
    private void FireShotgunPellets(float damage, float critChance, EquippedSkill skill)
    {
        if (shotgunPelletPrefab == null) return;

        int pellets = Mathf.Max(1, ShotgunBasePellets + skill.ExtraProjectiles); // 레벨업 주 성장축

        // R1(휘두르기 연계, path2): 탄이 많아지고 **부채꼴 대신 전방으로 몰아 쏜다**. 피해도 오른다(2026-08-06 명세).
        // 각도를 0으로 좁히는 게 핵심 — 흩어지던 화력이 정면 한 줄기에 전부 실린다.
        float spreadDegrees = BalanceConstants.ShotgunSpreadDegrees;
        float pelletDamage = damage;
        if (skill.PathTier[2] >= 3) { pellets *= 3; spreadDegrees = 0f; pelletDamage *= 1.6f; }
        else if (skill.PathTier[2] >= 2) { pellets *= 2; spreadDegrees = 0f; pelletDamage *= 1.3f; }

        Vector3 origin = transform.position + Vector3.up * 0.2f;

        for (int i = 0; i < pellets; i++)
        {
            float t = pellets > 1 ? i / (float)(pellets - 1) : 0.5f;
            float angle = Mathf.Lerp(-spreadDegrees, spreadDegrees, t);
            Vector2 dir = Quaternion.Euler(0f, 0f, angle) * Vector2.left;

            // 각도가 0이면(전방 집중) 전부 한 점에서 겹쳐 한 발처럼 보인다 — 출발 높이를 위아래로 벌려 "다발"이 보이게 한다.
            Vector3 spawnAt = origin + (spreadDegrees <= 0f ? Vector3.up * Mathf.Lerp(-0.45f, 0.45f, t) : Vector3.zero);

            GameObject obj = Instantiate(shotgunPelletPrefab, spawnAt, Quaternion.identity);
            obj.transform.localScale *= skill.Scale;
            SmallOrb pellet = obj.GetComponent<SmallOrb>();
            if (pellet == null) continue;
            pellet.CritChance = critChance;
            pellet.Init(dir, pelletDamage, false);
        }

        animator.SetTrigger("Attack");
    }

    // 반환값은 회오리 R0이 "사라질 때 미니를 남기는" 콜백을 배선하는 데 쓴다(그 외 호출부는 무시해도 된다).
    private Whirlwind SpawnWhirlwind(Vector3 position, float damage, float critChance, float scale, bool applySlow, bool applyVulnerable, int maxHitCount = 0, float slowDuration = 3f, float extraLifetime = 0f, bool isMini = false, float tickIntervalMult = 1f)
    {
        PlayCastSfx(whirlwindCastSfx, whirlwindCastSfxVolume);
        GameObject obj = Instantiate(whirlwindPrefab, position, Quaternion.identity);
        obj.transform.localScale *= scale;
        Whirlwind whirlwind = obj.GetComponent<Whirlwind>();
        whirlwind.Damage = damage;
        whirlwind.ApplyGemSlow = applySlow;
        whirlwind.ApplyGemVulnerable = applyVulnerable;
        whirlwind.CritChance = critChance;
        whirlwind.MaxHitCount = maxHitCount;
        whirlwind.SlowDuration = slowDuration;
        whirlwind.ExtraLifetime = extraLifetime;
        whirlwind.CanHitFlying = !isMini; // 미니 회오리는 비행 적을 타격할 수 없다
        whirlwind.TickIntervalMult = tickIntervalMult; // 레벨업 보조축: 피해 주기

        // 미니는 크기가 작아 기본 groundY(피벗=중심)에 놓으면 지면 위로 떠 보인다 — 바닥선을 큰 회오리와 맞춘다.
        if (isMini)
        {
            SpriteRenderer sr = obj.GetComponent<SpriteRenderer>();
            if (sr != null) whirlwind.GroundY = -sr.bounds.extents.y * (1f / MiniWhirlwindScale - 1f);
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

        // 기본 path: 공중 적 추가 피해 (T1=40%, T3=총 100%)
        float flyingBonus = skill.PathTier[0] >= 3 ? 1f : (skill.PathTier[0] >= 1 ? 0.4f : 0f);
        orb.FlyingDamageMultiplier = 1f + flyingBonus;

        // 지식 연계 path: 슬로우 강화 (T1, T3에서 각각)
        float slowMultBonus = 0f;
        float slowDurBonus = 0f;
        if (skill.PathTier[1] >= 1) { slowMultBonus += 0.1f; slowDurBonus += 0.5f; }
        if (skill.PathTier[1] >= 3) { slowMultBonus += 0.1f; slowDurBonus += 0.5f; }
        orb.SlowMultiplierBonus = slowMultBonus;
        orb.SlowDurationBonus = slowDurBonus;
        // 레벨업 주 성장축: 사라지기 전까지 붙잡는 총 적 수.
        // 대형 오브(지식 연계 T2+)는 **관통 무한** — 줄을 통째로 뚫고 지나간다(대가는 늘어난 쿨타임).
        orb.MaxTargets = skill.PathTier[1] >= 2 ? int.MaxValue : OrbBaseTargets + skill.ExtraTargets;
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
        int count = OrbBaseTargets + skill.ExtraTargets;
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

        // 레벨업 보조축: 투하 횟수(ExtraProjectiles). 기본 path T1: +1, 회오리 연계 path T1: -1(쿨감 트레이드오프)
        int dropCount = Mathf.Max(1, EagleBaseDrops + skill.ExtraProjectiles
                                     + (skill.PathTier[0] >= 1 ? 1 : 0) - (skill.PathTier[2] >= 1 ? 1 : 0));
        bool scaleByEnemyCount = skill.PathTier[0] >= 2; // 기본 path T2: 화면 내 적 수에 반비례한 피해량 스케일링(최대 450%)
        float t3DamageMult = skill.PathTier[0] >= 3 ? 1.5f : 1f; // 기본 path T3: 피해량 50% 증가
        // 레벨업 주 성장축: 투하 간격(TickIntervalMult). 기본 path T3: 추가로 50% 감소
        float interval = (skill.PathTier[0] >= 3 ? 0.5f : 1f) * skill.TickIntervalMult;

        // 독수리 비는 투하가 훨씬 촘촘해서(3초/0.3초 = 10틱) 틱당 피해를 낮추지 않으면 혼자 압도적이 된다.
        // 총량은 기존 2~4회 투하의 3~6배 수준으로 맞춘다.
        float rainDamageRatio = skill.PathTier[1] >= 3 ? 0.28f : 0.35f;

        float elapsed = 0f;
        for (int i = 0; eagleRain ? elapsed < rainDuration : i < dropCount; i++)
        {
            List<Enemy> enemies = new List<Enemy>(FindObjectsByType<Enemy>(FindObjectsSortMode.None));

            float countMult = 1f;
            if (scaleByEnemyCount)
            {
                int count = Mathf.Max(enemies.Count, 1);
                countMult = Mathf.Clamp(450f / count, 100f, 450f) / 100f;
            }
            float dropDamage = damage * countMult * t3DamageMult * (eagleRain ? rainDamageRatio : 1f);

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

    private GameObject SpawnLightningRod(Vector3 center, bool empowered)
    {
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
        rod.transform.position = center + Vector3.down * 0.4f;
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

    private IEnumerator LightningRodRoutine(float damage, float critChance, bool empowered)
    {
        float ratio = empowered ? LightningRodEmpoweredRatio : LightningRodDamageRatio;
        float radius = LightningRodRadius * (empowered ? LightningRodEmpoweredRadiusMult : 1f);

        Vector3 center = Camera.main != null ? Camera.main.transform.position : transform.position;
        center.z = 0f;

        GameObject rod = SpawnLightningRod(center, empowered);

        for (float elapsed = 0f; elapsed < LightningRodDuration; elapsed += LightningRodInterval)
        {
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
    private static float GetDefaultDamage(ActiveSkillId id)
    {
        if (id == ActiveSkillId.Lightning) return LightningStorm.BaseProcDamage;
        SkillProgression p = Prog(id);
        return p != null ? p.baseDamage : SkillProgression.DefaultBaseDamage(id);
    }
}
