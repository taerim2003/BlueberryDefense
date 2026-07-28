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
    public int ExtraProjectiles = 0; // 기본공격: 투사체 추가 발사 (1당 1발)
    public float ExtraWhirlwindDuration = 0f; // 회오리: 지속시간(초) 추가
    public int GrowthStacks = 0; // 호밍 미사일: 사용할수록 누적되는 성장 스택(이번 판 한정)
    public float RewindAmount = 1f; // 되감기: 다른 스킬 쿨타임을 앞당기는 시간(초). 짝수 레벨업마다 +0.15

    // 진화 트리: path 0=기본(무의존), 1=패시브 연계, 2=액티브 연계. 각 값은 도달한 티어(0~3).
    public readonly int[] PathTier = new int[3];
    public int TotalEvolutionTier => PathTier[0] + PathTier[1] + PathTier[2];
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
    private const float FlyingArrowSpawnRaise = BalanceConstants.FlyingArrowSpawnRaise; // 비행 적 타격 진화 시 발사점 상승(세로 긴 히트박스와 합쳐 지상/비행 동시 커버)
    [SerializeField] private GameObject whirlwindPrefab;
    [SerializeField] private GameObject bigTornadoPrefab;
    [SerializeField] private GameObject orbPrefab;
    [SerializeField] private GameObject bigOrbPrefab; // 지식 연계 path1 T2부터 등장하는 큰 초록 오브 비주얼
    [SerializeField] private GameObject orbAltarPrefab;
    [SerializeField] private GameObject eagleDropPrefab;
    [SerializeField] private GameObject eagleImpactVfxPrefab;
    [SerializeField] private GameObject snipingEffectPrefab;      // 스나이핑 후속 타격 VFX(Effect_Sniping, 2~5번째 저격)
    [SerializeField] private GameObject snipingSplashPrefab;      // 스나이핑 첫 타격 강조 VFX(Effect_SplashSniping — 기본 이펙트 화려 버전)
    [SerializeField] private GameObject overkillSplashVfxPrefab;  // Route2 초과데미지 연쇄 전용 VFX(Vefects Impact Sparks)
    [SerializeField] private float overkillSplashVfxScale = 0.5f;
    [SerializeField] private GameObject homingMissilePrefab;      // 호밍 미사일 프리팹(추적)
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

    public bool CanUpgradeSkill(EquippedSkill skill) => skill.TotalEvolutionTier >= skill.Level / 5;

    public void UpgradeSkillLevel(ActiveSkillId id)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null || !CanUpgradeSkill(skill)) return;

        skill.Level++;
        ApplyUpgradeEffect(skill, skill.Level);
    }

    // 스킬트리 메타: 특정 스킬을 시작부터 지정 레벨로(진화 게이트 무시). 레벨업 효과를 순서대로 적용.
    public void SetSkillStartLevel(ActiveSkillId id, int targetLevel)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null) return;
        while (skill.Level < targetLevel)
        {
            skill.Level++;
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
        }
    }

    public static string DescribeUpgradeEffect(EquippedSkill skill, int nextLevel) => DescribeStep(StepFor(skill.Id, nextLevel));

    // 미리보기 텍스트를 스텝 데이터에서 생성 → 미리보기·실제 적용이 항상 일치. (Apply와 같은 StepFor 참조)
    private static string DescribeStep(LevelUpStep s)
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
            default: return "";
        }
    }

    // path: 0=기본(무의존, 데미지), 1=패시브 연계(쿨타임), 2=액티브 연계(제어기)
    public bool CanEvolvePath(EquippedSkill skill, int path)
    {
        int tier = skill.PathTier[path];
        if (tier >= 3) return false;

        if (tier == 0)
        {
            int investedPaths = CountInvestedPaths(skill);
            return investedPaths < 2;
        }

        if (tier == 1)
        {
            int advancingPath = GetAdvancingPath(skill);
            if (advancingPath != -1 && advancingPath != path) return false;
            return HasPathPrereq(skill.Id, path);
        }

        // tier == 2 → 3: 이미 advancingPath로 확정된 경로만 여기 올 수 있음
        return true;
    }

    public bool CanEvolveAnyPath(EquippedSkill skill) =>
        CanEvolvePath(skill, 0) || CanEvolvePath(skill, 1) || CanEvolvePath(skill, 2);

    public void EvolveSkill(ActiveSkillId id, int path)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null || !CanEvolvePath(skill, path)) return;

        skill.PathTier[path]++;
        ApplyPathTierEffect(skill, path, skill.PathTier[path]);
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
            case (ActiveSkillId.EagleDrop, 1, 2):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.85f);
                break;
            case (ActiveSkillId.Whirlwind, 1, 3):
            case (ActiveSkillId.Orb, 1, 3):
                // Orb 1,3은 문서상 슬로우 강화만이지만, 기존부터 쿨감도 함께 적용되던 걸 그대로 유지
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.9f);
                break;
            case (ActiveSkillId.Orb, 1, 2):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.75f); // 쿨감 25%
                skill.Scale += 0.3f; // 오브 크기 증가 (스프라이트 교체는 별도 아트 필요, 수치만 우선 적용)
                break;
            case (ActiveSkillId.BasicAttack, 1, 3):
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
            case (ActiveSkillId.Sniping, 1, 1):
                skill.Damage *= 1.7f; // Route2 T1: 피해 70%
                break;
            case (ActiveSkillId.Sniping, 1, 3):
                skill.Damage *= 2f; // Route2 T3: 피해 100% (초과 피해가 커져 연쇄도 강해짐)
                break;
            case (ActiveSkillId.Sniping, 2, 1):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.6f); // Route3 T1: 쿨감 40%
                break;
            case (ActiveSkillId.Sniping, 2, 3):
                skill.Damage *= 1.3f; // Route3 T3: 자동시전 강화(피해 30%)
                break;

            // 호밍 미사일 (개수 path0·폭발 path1 T2·성장률 path2는 FireHoming에서 실시간)
            case (ActiveSkillId.Homing, 1, 1):
                skill.Damage *= 1.3f; // Route2 T1: 미사일 피해 30%
                break;
            // 산탄(Shotgun)은 전부 FireShotgun에서 실시간 계산(영구 스탯 변경 없음)

            // 되감기 Route3: 되감기 자체 쿨타임 감소 (T3의 글로벌 쿨감은 GlobalCooldownScale에서 실시간 처리)
            case (ActiveSkillId.Rewind, 2, 1):
            case (ActiveSkillId.Rewind, 2, 2):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.7f); // 쿨감 30%
                break;
            // 되감기 Route1(되감기 정도)·Route2(다음 스킬 피해)는 FireRewind에서 실시간 계산
        }
    }

    private static int CountInvestedPaths(EquippedSkill skill) =>
        (skill.PathTier[0] > 0 ? 1 : 0) + (skill.PathTier[1] > 0 ? 1 : 0) + (skill.PathTier[2] > 0 ? 1 : 0);

    private static int GetAdvancingPath(EquippedSkill skill)
    {
        for (int i = 0; i < 3; i++)
            if (skill.PathTier[i] >= 2) return i;
        return -1;
    }

    // path1/path2 진화는 연계 대상(패시브/액티브)을 보유하는 것만으로는 부족하고, Lv.5 이상이어야 함
    private bool HasPathPrereq(ActiveSkillId skillId, int path)
    {
        if (path == 1)
        {
            PassiveSkillId? req = GetPassivePrereq(skillId);
            if (!req.HasValue) return true; // 연계 미지정 스킬(신규)은 연계 조건 없이 자유 진화
            if (passives == null) return false;
            EquippedPassive p = passives.GetPassive(req.Value);
            return p != null && p.Level >= 5;
        }
        if (path == 2)
        {
            ActiveSkillId? req = GetActivePrereq(skillId);
            if (!req.HasValue) return true; // 연계 미지정 스킬(신규)은 자유 진화
            EquippedSkill s = equippedSkills.FirstOrDefault(x => x.Id == req.Value);
            return s != null && s.Level >= 5;
        }
        return true;
    }

    public static PassiveSkillId? GetPassivePrereq(ActiveSkillId id) => id switch
    {
        ActiveSkillId.BasicAttack => PassiveSkillId.Assassinate,
        ActiveSkillId.Whirlwind => PassiveSkillId.Refresh,
        ActiveSkillId.Orb => PassiveSkillId.Knowledge,
        ActiveSkillId.Lightning => PassiveSkillId.Strength,
        ActiveSkillId.EagleDrop => PassiveSkillId.Health,
        _ => null,
    };

    public static ActiveSkillId? GetActivePrereq(ActiveSkillId id) => id switch
    {
        ActiveSkillId.BasicAttack => ActiveSkillId.EagleDrop,
        ActiveSkillId.Whirlwind => ActiveSkillId.Orb,
        ActiveSkillId.Orb => ActiveSkillId.Lightning,
        ActiveSkillId.Lightning => ActiveSkillId.Whirlwind,
        ActiveSkillId.EagleDrop => ActiveSkillId.Whirlwind,
        _ => null,
    };

    public static string GetActiveSkillName(ActiveSkillId id) => id switch
    {
        ActiveSkillId.BasicAttack => "기본 공격",
        ActiveSkillId.Whirlwind => "회오리",
        ActiveSkillId.Orb => "오브",
        ActiveSkillId.Lightning => "낙뢰",
        ActiveSkillId.EagleDrop => "독수리 투하",
        ActiveSkillId.Sniping => "스나이핑",
        ActiveSkillId.Homing => "호밍 미사일",
        ActiveSkillId.Shotgun => "산탄 장착",
        ActiveSkillId.Rewind => "되감기",
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
        if (s.ExtraProjectiles > 0) lines.Add($"투사체 +{s.ExtraProjectiles}발");
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

    public static string DescribePathEffect(ActiveSkillId id, int path, int tier) => (id, path, tier) switch
    {
        // BasicAttack
        (ActiveSkillId.BasicAttack, 0, 1) => "관통 3회 추가, 비행 적 타격 가능",
        (ActiveSkillId.BasicAttack, 0, 2) => "피해량 100% 증가",
        (ActiveSkillId.BasicAttack, 0, 3) => "관통 10회 추가",
        (ActiveSkillId.BasicAttack, 1, 1) => "기본공격 치명타 확률 15% 증가",
        (ActiveSkillId.BasicAttack, 1, 2) => "치명타 적중 시 투사체 1회 추가 발사",
        (ActiveSkillId.BasicAttack, 1, 3) => "재사용 대기시간 50% 감소",
        (ActiveSkillId.BasicAttack, 2, 1) => "명중 시 미니 독수리 투하 (피해량 40%)",
        (ActiveSkillId.BasicAttack, 2, 2) => "미니 독수리가 주변 적까지 확산 (최대 4마리)",
        (ActiveSkillId.BasicAttack, 2, 3) => "미니 독수리 확산 범위 확대 (최대 10마리)",

        // Whirlwind
        (ActiveSkillId.Whirlwind, 0, 1) => "회오리 소환 시 미니 회오리 2개 추가 소환 (비행 적 타격 불가)",
        (ActiveSkillId.Whirlwind, 0, 2) => "미니 회오리 피해량 40% 증가",
        (ActiveSkillId.Whirlwind, 0, 3) => "미니 회오리 1개 추가 소환, 피해량 25% 추가 증가",
        (ActiveSkillId.Whirlwind, 1, 1) => "회오리 쿨타임 20% 감소",
        (ActiveSkillId.Whirlwind, 1, 2) => "리프레쉬 발동 시 전체 스킬 쿨타임 1초 감소",
        (ActiveSkillId.Whirlwind, 1, 3) => "재사용 대기시간 10% 추가 감소",
        (ActiveSkillId.Whirlwind, 2, 1) => "적 둔화 부여 (30% 감속, 3초)",
        (ActiveSkillId.Whirlwind, 2, 2) => "거대 회오리로 변화 (분열 없이 하나의 거대한 회오리, 둔화 지속시간 4.5초)",
        (ActiveSkillId.Whirlwind, 2, 3) => "취약 부여 (추가 피해 50%)",

        // Orb
        (ActiveSkillId.Orb, 0, 1) => "공중 타격 가능, 공중 적 추가 피해 40%",
        (ActiveSkillId.Orb, 0, 2) => "피해량 40% 증가",
        (ActiveSkillId.Orb, 0, 3) => "공중 적 추가 피해 100%",
        (ActiveSkillId.Orb, 1, 1) => "슬로우 효과 10%p 강화",
        (ActiveSkillId.Orb, 1, 2) => "재사용 대기시간 25% 감소, 오브 크기 증가, 큰 초록 오브로 변화",
        (ActiveSkillId.Orb, 1, 3) => "슬로우 효과 10%p 추가 강화, 재사용 대기시간 10% 추가 감소",
        (ActiveSkillId.Orb, 2, 1) => "취약 부여 (추가 피해 50%)",
        (ActiveSkillId.Orb, 2, 2) => "오브 설치기로 대체 (캐릭터 앞에 설치되어 전방으로 미니 오브 1초마다 발사 + 2.5초마다 화면 내 모든 적에게 낙뢰)",
        (ActiveSkillId.Orb, 2, 3) => "설치기 피해량 50% 증가",

        // Lightning
        (ActiveSkillId.Lightning, 0, 1) => "낙뢰가 낙뢰를 유발 가능 (최대 4연쇄)",
        (ActiveSkillId.Lightning, 0, 2) => "피해량 40% 증가",
        (ActiveSkillId.Lightning, 0, 3) => "재귀마다 낙뢰 피해량 30% 증가 (재귀로 갈수록 더 강해짐)",
        (ActiveSkillId.Lightning, 1, 1) => "재사용 대기시간 25% 감소",
        (ActiveSkillId.Lightning, 1, 2) => "체인 라이트닝: 낙뢰 첫 발동 시 주변 적 3마리에게 전이",
        (ActiveSkillId.Lightning, 1, 3) => "피해량 30% 증가, 체인 라이트닝 전이 대상 10마리로 확장",
        (ActiveSkillId.Lightning, 2, 1) => "낙뢰 지속시간 30% 증가",
        (ActiveSkillId.Lightning, 2, 2) => "회오리 소환 시 낙뢰 지속시간 1.5초 증가, 낙뢰 버프 중첩 가능",
        (ActiveSkillId.Lightning, 2, 3) => "낙뢰 버프 중첩당 모든 공격 피해량 15% 증가",

        // EagleDrop
        (ActiveSkillId.EagleDrop, 0, 1) => "투하 횟수 1회 추가",
        (ActiveSkillId.EagleDrop, 0, 2) => "화면 내 적이 적을수록 피해량 증가 (최대 450%)",
        (ActiveSkillId.EagleDrop, 0, 3) => "투하 간격 50% 감소, 피해량 50% 증가",
        (ActiveSkillId.EagleDrop, 1, 1) => "독수리 타격마다 흡혈(체력 2 회복), 초과 회복량은 보호막으로 전환",
        (ActiveSkillId.EagleDrop, 1, 2) => "재사용 대기시간 15% 감소",
        (ActiveSkillId.EagleDrop, 1, 3) => "흡혈량 2 추가",
        (ActiveSkillId.EagleDrop, 2, 1) => "재사용 대기시간 40% 감소, 투하 횟수 1회 감소",
        (ActiveSkillId.EagleDrop, 2, 2) => "착탄 시 미니 회오리 생성 (피해량 25%, 최대 5회 타격)",
        (ActiveSkillId.EagleDrop, 2, 3) => "미니 회오리 피해량 35%로 증가, 최대 타격 횟수 +3회",

        // Sniping (Route1=path0 타겟수, Route2=path1 피해+스플래시, Route3=path2 자동시전)
        (ActiveSkillId.Sniping, 0, 1) => "저격 타겟 1명 추가 (총 2명)",
        (ActiveSkillId.Sniping, 0, 2) => "저격 타겟 1명 추가 (총 3명)",
        (ActiveSkillId.Sniping, 0, 3) => "저격 타겟 2명 추가 (총 5명)",
        (ActiveSkillId.Sniping, 1, 1) => "피해량 70% 증가",
        (ActiveSkillId.Sniping, 1, 2) => "처치 시 남은 초과 피해가 옆 적에게 흘러 연쇄 (0.3초마다 튐)",
        (ActiveSkillId.Sniping, 1, 3) => "피해량 100% 증가, 초과 피해 연쇄 범위·분기 확대",
        (ActiveSkillId.Sniping, 2, 1) => "재사용 대기시간 40% 감소",
        (ActiveSkillId.Sniping, 2, 2) => "수동 사용 불가, 3초마다 가장 체력 높은 적에게 자동 시전",
        (ActiveSkillId.Sniping, 2, 3) => "자동 시전 간격 1.5초로 단축, 피해량 30% 증가",

        // Homing (Route1=path0 미사일 수, Route2=path1 폭발, Route3=path2 성장률)
        (ActiveSkillId.Homing, 0, 1) => "미사일 수 증가 (5 → 7)",
        (ActiveSkillId.Homing, 0, 2) => "미사일 수 2배 (→ 10)",
        (ActiveSkillId.Homing, 0, 3) => "미사일 수 3배 (→ 15)",
        (ActiveSkillId.Homing, 1, 1) => "미사일 피해량 30% 증가",
        (ActiveSkillId.Homing, 1, 2) => "미사일 타격 시 폭발, 주변 적에게 피해 (40%)",
        (ActiveSkillId.Homing, 1, 3) => "폭발 피해 60%로 증가, 범위 확대",
        (ActiveSkillId.Homing, 2, 1) => "성장률 소폭 강화",
        (ActiveSkillId.Homing, 2, 2) => "성장률 강화 (사용할수록 더 빨리 강해짐)",
        (ActiveSkillId.Homing, 2, 3) => "성장률 대폭 강화",

        // Shotgun (Route1=path0 타수, Route2=path1 집중산탄, Route3=path2 전체산탄+기절)
        (ActiveSkillId.Shotgun, 0, 1) => "공격 횟수 추가 (버프 중 +2)",
        (ActiveSkillId.Shotgun, 0, 2) => "공격 횟수 추가 (버프 중 +3)",
        (ActiveSkillId.Shotgun, 0, 3) => "공격 횟수 추가 (버프 중 +5)",
        (ActiveSkillId.Shotgun, 1, 1) => "버프 지속시간 2초 증가",
        (ActiveSkillId.Shotgun, 1, 2) => "가장 공격력 높은 스킬 1개에만 적용, 공격 횟수 추가 2배",
        (ActiveSkillId.Shotgun, 1, 3) => "그 스킬 공격 횟수 추가 3배",
        (ActiveSkillId.Shotgun, 2, 1) => "버프 지속시간 2초 증가",
        (ActiveSkillId.Shotgun, 2, 2) => "사용 시 화면의 모든 적을 5회 공격",
        (ActiveSkillId.Shotgun, 2, 3) => "전체 공격이 적을 1.5초간 기절시킴",

        // Rewind (Route1=path0 되감기 정도, Route2=path1 다음 스킬 피해, Route3=path2 쿨감+글로벌쿨감)
        (ActiveSkillId.Rewind, 0, 1) => "되감기 시간 0.5초 증가",
        (ActiveSkillId.Rewind, 0, 2) => "되감기 시간 0.5초 추가 증가",
        (ActiveSkillId.Rewind, 0, 3) => "되감기 시간 1초 추가 증가",
        (ActiveSkillId.Rewind, 1, 1) => "다음에 사용하는 스킬 피해 30% 증가",
        (ActiveSkillId.Rewind, 1, 2) => "다음에 사용하는 스킬 피해 60% 증가",
        (ActiveSkillId.Rewind, 1, 3) => "다음에 사용하는 스킬 피해 100% 증가",
        (ActiveSkillId.Rewind, 2, 1) => "되감기 재사용 대기시간 30% 감소",
        (ActiveSkillId.Rewind, 2, 2) => "되감기 재사용 대기시간 30% 추가 감소",
        (ActiveSkillId.Rewind, 2, 3) => "전역 재사용 대기시간(GCD) 절반으로 감소",

        _ => "",
    };

    // 진화 카드에 붙는 짧은 제목 (설명 문장과 별개)
    public static string GetPathTierTitle(ActiveSkillId id, int path, int tier) => (id, path, tier) switch
    {
        (ActiveSkillId.BasicAttack, 0, 1) => "관통 강화",
        (ActiveSkillId.BasicAttack, 0, 2) => "피해량 강화",
        (ActiveSkillId.BasicAttack, 0, 3) => "관통 강화 II",
        (ActiveSkillId.BasicAttack, 1, 1) => "치명타 확률 강화",
        (ActiveSkillId.BasicAttack, 1, 2) => "치명 연사",
        (ActiveSkillId.BasicAttack, 1, 3) => "쿨타임 감소",
        (ActiveSkillId.BasicAttack, 2, 1) => "미니 독수리",
        (ActiveSkillId.BasicAttack, 2, 2) => "미니 독수리 확산",
        (ActiveSkillId.BasicAttack, 2, 3) => "미니 독수리 확산 II",

        (ActiveSkillId.Whirlwind, 0, 1) => "미니 회오리",
        (ActiveSkillId.Whirlwind, 0, 2) => "미니 회오리 강화",
        (ActiveSkillId.Whirlwind, 0, 3) => "미니 회오리 강화 II",
        (ActiveSkillId.Whirlwind, 1, 1) => "쿨타임 감소",
        (ActiveSkillId.Whirlwind, 1, 2) => "쿨타임 리셋",
        (ActiveSkillId.Whirlwind, 1, 3) => "쿨타임 감소 II",
        (ActiveSkillId.Whirlwind, 2, 1) => "둔화 부여",
        (ActiveSkillId.Whirlwind, 2, 2) => "거대 회오리",
        (ActiveSkillId.Whirlwind, 2, 3) => "취약 부여",

        (ActiveSkillId.Orb, 0, 1) => "공중 추가피해",
        (ActiveSkillId.Orb, 0, 2) => "피해량 강화",
        (ActiveSkillId.Orb, 0, 3) => "공중 추가피해 II",
        (ActiveSkillId.Orb, 1, 1) => "슬로우 강화",
        (ActiveSkillId.Orb, 1, 2) => "쿨타임 감소 & 크기 증가",
        (ActiveSkillId.Orb, 1, 3) => "슬로우 강화 II",
        (ActiveSkillId.Orb, 2, 1) => "취약 부여",
        (ActiveSkillId.Orb, 2, 2) => "오브 설치기",
        (ActiveSkillId.Orb, 2, 3) => "설치기 강화",

        (ActiveSkillId.Lightning, 0, 1) => "연쇄 낙뢰",
        (ActiveSkillId.Lightning, 0, 2) => "피해량 강화",
        (ActiveSkillId.Lightning, 0, 3) => "재귀 피해 강화",
        (ActiveSkillId.Lightning, 1, 1) => "쿨타임 감소",
        (ActiveSkillId.Lightning, 1, 2) => "체인 라이트닝",
        (ActiveSkillId.Lightning, 1, 3) => "피해량 & 전이 강화",
        (ActiveSkillId.Lightning, 2, 1) => "지속시간 강화",
        (ActiveSkillId.Lightning, 2, 2) => "지속시간 강화 & 중첩",
        (ActiveSkillId.Lightning, 2, 3) => "전체 피해량 강화",

        (ActiveSkillId.EagleDrop, 0, 1) => "투하 횟수 증가",
        (ActiveSkillId.EagleDrop, 0, 2) => "피해량 강화",
        (ActiveSkillId.EagleDrop, 0, 3) => "투하 간격 & 피해량 강화",
        (ActiveSkillId.EagleDrop, 1, 1) => "오버힐 획득",
        (ActiveSkillId.EagleDrop, 1, 2) => "쿨타임 감소",
        (ActiveSkillId.EagleDrop, 1, 3) => "오버힐 강화",
        (ActiveSkillId.EagleDrop, 2, 1) => "쿨타임 감소 (투하 횟수 감소)",
        (ActiveSkillId.EagleDrop, 2, 2) => "미니 회오리",
        (ActiveSkillId.EagleDrop, 2, 3) => "미니 회오리 강화",

        (ActiveSkillId.Sniping, 0, 1) => "타겟 추가",
        (ActiveSkillId.Sniping, 0, 2) => "타겟 추가 II",
        (ActiveSkillId.Sniping, 0, 3) => "타겟 추가 III",
        (ActiveSkillId.Sniping, 1, 1) => "피해 강화",
        (ActiveSkillId.Sniping, 1, 2) => "초과 연쇄",
        (ActiveSkillId.Sniping, 1, 3) => "연쇄·피해 강화",
        (ActiveSkillId.Sniping, 2, 1) => "쿨타임 감소",
        (ActiveSkillId.Sniping, 2, 2) => "자동 조준",
        (ActiveSkillId.Sniping, 2, 3) => "자동 조준 II",

        (ActiveSkillId.Homing, 0, 1) => "미사일 증가",
        (ActiveSkillId.Homing, 0, 2) => "미사일 증가 II",
        (ActiveSkillId.Homing, 0, 3) => "미사일 증가 III",
        (ActiveSkillId.Homing, 1, 1) => "피해 강화",
        (ActiveSkillId.Homing, 1, 2) => "폭발",
        (ActiveSkillId.Homing, 1, 3) => "폭발 II",
        (ActiveSkillId.Homing, 2, 1) => "성장 강화",
        (ActiveSkillId.Homing, 2, 2) => "성장 강화 II",
        (ActiveSkillId.Homing, 2, 3) => "성장 강화 III",

        (ActiveSkillId.Shotgun, 0, 1) => "타수 증가",
        (ActiveSkillId.Shotgun, 0, 2) => "타수 증가 II",
        (ActiveSkillId.Shotgun, 0, 3) => "타수 증가 III",
        (ActiveSkillId.Shotgun, 1, 1) => "지속 증가",
        (ActiveSkillId.Shotgun, 1, 2) => "집중 산탄",
        (ActiveSkillId.Shotgun, 1, 3) => "집중 산탄 II",
        (ActiveSkillId.Shotgun, 2, 1) => "지속 증가",
        (ActiveSkillId.Shotgun, 2, 2) => "전체 산탄",
        (ActiveSkillId.Shotgun, 2, 3) => "기절 산탄",

        (ActiveSkillId.Rewind, 0, 1) => "되감기 강화",
        (ActiveSkillId.Rewind, 0, 2) => "되감기 강화 II",
        (ActiveSkillId.Rewind, 0, 3) => "되감기 강화 III",
        (ActiveSkillId.Rewind, 1, 1) => "다음 타 강화",
        (ActiveSkillId.Rewind, 1, 2) => "다음 타 강화 II",
        (ActiveSkillId.Rewind, 1, 3) => "다음 타 강화 III",
        (ActiveSkillId.Rewind, 2, 1) => "쿨타임 감소",
        (ActiveSkillId.Rewind, 2, 2) => "쿨타임 감소 II",
        (ActiveSkillId.Rewind, 2, 3) => "글로벌 쿨감",

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
                // 낙뢰 연계 path2 T1: 낙뢰 지속시간 30% 증가 (메타 "지속시간" 업그레이드가 곱해짐)
                float baseDuration = 10f * (skill.PathTier[2] >= 1 ? 1.3f : 1f) * MetaBonuses.DurationMult;
                // 기존 스택을 지우지 않고 새로 추가한다 — 평소엔 쿨타임이 지속시간보다 길어 이전 스택이 이미 만료된 상태지만,
                // 회오리 연계로 지속시간이 계속 연장돼 있으면 새 캐스트가 기존 스택 위에 쌓인다.
                LightningStorm.AddStack(baseDuration);
                LightningStorm.ProcChance = LightningStorm.BaseProcChance + skill.ProcChanceBonus;
                LightningStorm.ProcDamage = damage;
                LightningStorm.RecursiveProcEnabled = skill.PathTier[0] >= 1;
                LightningStorm.RecursiveDamageGrowth = skill.PathTier[0] >= 3 ? 0.3f : 0f;
                LightningStorm.ChainEnabled = skill.PathTier[1] >= 2;
                LightningStorm.ChainCount = skill.PathTier[1] >= 3 ? 10 : 3;
                LightningStorm.StackDamageEnabled = skill.PathTier[2] >= 3;
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
        }

        globalCooldownTimer = GlobalCooldown * GlobalCooldownScale(); // 되감기 Route3 T3: 전역 쿨타임 절반
        // 오브가 설치기(낙뢰 연계)로 대체된 상태에서는 훨씬 긴 별도 쿨타임을 사용
        // (메타 "쿨타임" 업그레이드가 전역 배율로 곱해짐)
        // 스나이핑 자동시전(path2 T2+)은 스킬 쿨타임 대신 고정 간격(T2=3초, T3=1.5초)으로 발동
        float baseCd = (skill.Id == ActiveSkillId.Orb && skill.PathTier[2] >= 2) ? OrbAltarCooldown
            : (skill.Id == ActiveSkillId.Sniping && skill.PathTier[2] >= 2) ? (skill.PathTier[2] >= 3 ? 1.5f : 3f)
            : skill.Cooldown;
        float cdMult = MetaBonuses.CooldownMult;
        // 스킬트리 "신속한 회오리": 회오리는 쿨타임 감소분(1-CooldownMult)을 1.5배로 받음
        if (skill.Id == ActiveSkillId.Whirlwind && MetaBonuses.WhirlwindCooldownBonus)
            cdMult = Mathf.Max(0.05f, 1f - 1.5f * (1f - MetaBonuses.CooldownMult));
        // 리프레쉬 연계 path3 T1: 버프류 스킬(산탄·낙뢰) 쿨타임 감소
        if (PlayerPassives.BuffSkillCooldownMult < 1f && IsBuffSkill(skill.Id))
            cdMult *= PlayerPassives.BuffSkillCooldownMult;
        skill.CooldownTimer = baseCd * cdMult;

        if (passives != null && passives.HasPassive(PassiveSkillId.Refresh) && Random.value < PlayerPassives.RefreshChance + MetaBonuses.RefreshChanceBonus)
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
        // 암살 연계 path2(패시브): 회오리 전용 추가 치명타 확률
        if (skill.Id == ActiveSkillId.Whirlwind)
            chance += PlayerPassives.AssassinateWhirlwindCritBonus;
        chance += MetaBonuses.CritBonus; // 메타 "치명타" 업그레이드(전역 가산)
        return Mathf.Min(chance, MaxCritChance);
    }

    private float ComputeBaseDamage(float baseDamage, ActiveSkillId skillId)
    {
        // 힘 패시브 계열 피해 배율은 하나의 덧셈 풀로 합친다 — path0(전 스킬 공통)과 path2(기본공격 전용)를
        // 곱연산으로 각각 겹쳐 쌓으면 기본공격에서 배율이 폭발한다. 같은 풀에서 더한 뒤 한 번만 곱한다.
        float damageMultiplier = passiveDamageMultiplier;
        if (skillId == ActiveSkillId.BasicAttack) // 힘 연계 path2(패시브): 기본공격 전용 추가 피해량 (덧셈 합류)
            damageMultiplier += PlayerPassives.BasicAttackDamageMultiplierBonus;

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

        SpawnBasicAttackProjectile(skill, damage, critChance, pierce, 0f, allowBonusShot);
        for (int i = 1; i <= skill.ExtraProjectiles; i++) // 레벨업 고유 강화: 투사체 추가 발사
            SpawnBasicAttackProjectile(skill, damage, critChance, pierce, 0.4f * i, allowBonusShot);

        animator.SetTrigger("Attack");
        return true;
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

        bool spawnMiniEagle = skill.PathTier[2] >= 1; // 독수리투하 연계 path: 명중 시 미니 독수리 (T2/T3에서 주변 적까지 확산)
        int maxTargets = skill.PathTier[2] >= 3 ? 10 : (skill.PathTier[2] >= 2 ? 4 : 1);
        bool triggerBonusShot = allowBonusShot && skill.PathTier[1] >= 2; // 암살 연계 path1 T2: 치명타 적중 시 투사체 1회 추가 발사
        bool bonusShotFired = false;

        projectile.OnHitBonus = (hitEnemy, hitCrit) =>
        {
            if (spawnMiniEagle)
                SpawnMiniEagleSpread(hitEnemy, damage * 0.4f, critChance, skill.Scale * 0.5f, maxTargets);

            if (triggerBonusShot && hitCrit && !bonusShotFired)
            {
                bonusShotFired = true; // 관통으로 여러 적을 맞혀도 발사체 한 발당 보너스 발사는 한 번만
                FireBasicAttack(skill, damage, critChance, allowBonusShot: false);
            }
        };
    }

    private void SpawnMiniEagleSpread(Enemy primary, float damage, float critChance, float scale, int maxTargets)
    {
        StartCoroutine(MiniEagleBonus(primary, damage, critChance, scale));
        if (maxTargets <= 1) return;

        IEnumerable<Enemy> nearby = FindObjectsByType<Enemy>(FindObjectsSortMode.None)
            .Where(e => e != null && e != primary && Vector2.Distance(primary.transform.position, e.transform.position) <= 6f)
            .OrderBy(e => Vector2.Distance(primary.transform.position, e.transform.position))
            .Take(maxTargets - 1);

        foreach (Enemy e in nearby)
            StartCoroutine(MiniEagleBonus(e, damage, critChance, scale));
    }

    // ── 스나이핑: 가장 체력 높은 적(들)을 5회씩 저격 ──
    private bool FireSniping(float damage, float critChance, EquippedSkill skill)
    {
        int targets = 1;
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

        bool overkillSplash = skill.PathTier[1] >= 2;                 // Route2 T2: 초과 피해 연쇄
        float overkillRadius = skill.PathTier[1] >= 3 ? 3.5f : 2.5f;  // T3: 연쇄 탐색 범위 확대
        int firstFanout = skill.PathTier[1] >= 3 ? 2 : 1;            // T3: 첫 튐부터 두 갈래

        animator.SetTrigger("Attack");
        foreach (Enemy target in chosen)
            StartCoroutine(SnipeTarget(target, damage, critChance, overkillSplash, overkillRadius, firstFanout));
        return true;
    }

    private IEnumerator SnipeTarget(Enemy target, float damage, float critChance, bool overkillSplash, float overkillRadius, int firstFanout)
    {
        for (int i = 0; i < SnipingBaseShots; i++)
        {
            if (target == null) yield break;
            Vector3 pos = target.transform.position;

            // 첫 타격은 화려한 스플래시-룩(Effect_SplashSniping), 나머지 저격은 기본 스파크(Effect_Sniping).
            GameObject sparkPrefab = i == 0 ? snipingSplashPrefab : snipingEffectPrefab;
            if (sparkPrefab != null)
                ObjectPool.Instance.Spawn(sparkPrefab, pos, Quaternion.identity);

            target.TakeSkillHit(damage, critChance, ActiveSkillId.Sniping);

            // Route2: 이 저격이 적을 죽이고 초과 피해가 남으면, 남은 만큼을 옆 적에게 흘려보낸다.
            if (overkillSplash && target != null && target.CurrentHealth <= 0f)
            {
                float overkill = -target.CurrentHealth; // 대상 체력을 넘겨 들어간 피해량
                if (overkill > 0f)
                    StartCoroutine(OverkillChain(pos, overkill, critChance, firstFanout, overkillRadius, target));
                yield break; // 대상이 죽었으니 남은 저격은 종료 — 초과 피해가 이어받아 퍼진다
            }

            yield return new WaitForSeconds(SnipingShotInterval);
        }
    }

    // 초과 피해 연쇄: fromPos 근처의 산 적(들)에게 overkill을 흘려보내고, 그 적도 초과 피해를 내면 다시 옆 두 적으로 튕긴다.
    // 초과 피해가 없거나 근처에 산 적이 없을 때까지 반복. 매 튐마다 0.3초 텀을 둬서 퍼지는 게 보이게 한다.
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

        // 성장: 사용할수록 강해짐(이번 판 한정). Route3(path2)로 성장률 강화.
        float growthPerCast = 0.08f + (skill.PathTier[2] >= 1 ? 0.04f : 0f) + (skill.PathTier[2] >= 2 ? 0.06f : 0f) + (skill.PathTier[2] >= 3 ? 0.1f : 0f);
        skill.GrowthStacks++;
        float missileDamage = damage * (1f + growthPerCast * skill.GrowthStacks);

        // Route1(path0): 미사일 개수 N배
        int count = skill.PathTier[0] >= 3 ? 15 : skill.PathTier[0] >= 2 ? 10 : skill.PathTier[0] >= 1 ? 7 : 5;
        // 스킬트리 "더 많은 폭격"(Homing_MissileNum) 해금 시에만: 10회 사용마다 미사일 +1발
        if (MetaBonuses.HomingMissileGrowth) count += skill.GrowthStacks / 10;
        // Route2(path1): T2 폭발, T3 폭발 강화
        bool explode = skill.PathTier[1] >= 2;
        float explodeRatio = skill.PathTier[1] >= 3 ? 0.6f : 0.4f;
        float explodeRadius = skill.PathTier[1] >= 3 ? 2.5f : 1.5f;

        for (int i = 0; i < count; i++)
        {
            float spread = count > 1 ? Mathf.Lerp(-60f, 60f, i / (float)(count - 1)) : 0f;
            Vector2 dir = Quaternion.Euler(0f, 0f, spread) * Vector2.right; // 적 방향(오른쪽) 부채꼴
            GameObject obj = Instantiate(homingMissilePrefab, transform.position + Vector3.up * 0.2f, Quaternion.identity);
            HomingMissile m = obj.GetComponent<HomingMissile>();
            m.Damage = missileDamage;
            m.CritChance = critChance;
            m.Explode = explode;
            m.ExplodeRadius = explodeRadius;
            m.ExplodeRatio = explodeRatio;
            // 폭발 VFX는 HomingMissile 프리팹이 자체 보유(실제 폭발 에셋). 여기서 스나이핑 이펙트를 물리지 않는다.
            m.Init(dir);
        }
        animator.SetTrigger("Attack");
        return true;
    }

    // ── 산탄 장착: 5초간 스킬 공격 횟수 증가(타수 버프) ──
    private bool FireShotgun(float damage, float critChance, EquippedSkill skill)
    {
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

        // Route3(path2) T2: 사용 시 화면 모든 적 5회 공격, T3: 1.5초 기절
        if (skill.PathTier[2] >= 2)
        {
            bool stun = skill.PathTier[2] >= 3;
            foreach (Enemy e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                if (e == null) continue;
                for (int i = 0; i < 5; i++) e.TakeSkillHit(damage, critChance, ActiveSkillId.Shotgun);
                if (stun) e.ApplySlow(0f, 1.5f); // 기절 = 이동 정지
            }
        }

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

        Vector3 spawnPos;
        if (skill.PathTier[2] >= 2) // 오브 연계 path T2: 거대 회오리로 대체 (여러 개로 안 쪼개짐)
        {
            spawnPos = transform.position + Vector3.left * 0.6f + Vector3.down * 0.7f;
            SpawnBigTornado(spawnPos, damage, critChance, skill.Scale, applySlow, applyVulnerable, skill.ExtraWhirlwindDuration);
        }
        else
        {
            spawnPos = transform.position + Vector3.left * 0.6f + Vector3.up * 0.6f;
            SpawnWhirlwind(spawnPos, damage, critChance, skill.Scale, applySlow, applyVulnerable, maxHitCount: 0, slowDuration: 3f, extraLifetime: skill.ExtraWhirlwindDuration);
        }

        // 기본 path: 미니 회오리 추가 소환 (T1=2개, T3=+1개 총 3개). 거대 회오리(오브 연계 path)와도 독립적으로
        // 함께 나온다 — 두 path에 모두 투자하면 거대 회오리 + 미니 회오리가 같이 소환된다.
        // 독수리투하 연계(path T3)의 미니 회오리와 MiniWhirlwindDamageBonus를 공유해 서로의 미니 회오리가 함께 강해진다.
        int miniCount = 0;
        if (skill.PathTier[0] >= 1) miniCount += 2;
        if (skill.PathTier[0] >= 3) miniCount += 1;

        if (miniCount > 0)
        {
            float miniDamage = damage * 0.3f * (1f + MiniWhirlwindDamageBonus);
            for (int i = 0; i < miniCount; i++)
            {
                Vector2 offset = Random.insideUnitCircle * 0.5f;
                Vector3 miniSpawnPos = spawnPos + (Vector3)offset;
                SpawnWhirlwind(miniSpawnPos, miniDamage, critChance, skill.Scale * MiniWhirlwindScale, applySlow, applyVulnerable, maxHitCount: 6, slowDuration: 3f, isMini: true);
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
        whirlwind.TargetHighestHealth = PlayerPassives.AssassinateWhirlwindTargetHighest;
    }

    private void SpawnWhirlwind(Vector3 position, float damage, float critChance, float scale, bool applySlow, bool applyVulnerable, int maxHitCount = 0, float slowDuration = 3f, float extraLifetime = 0f, bool isMini = false)
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
        whirlwind.TargetHighestHealth = PlayerPassives.AssassinateWhirlwindTargetHighest;
        whirlwind.CanHitFlying = !isMini; // 미니 회오리는 비행 적을 타격할 수 없다

        // 미니는 크기가 작아 기본 groundY(피벗=중심)에 놓으면 지면 위로 떠 보인다 — 바닥선을 큰 회오리와 맞춘다.
        if (isMini)
        {
            SpriteRenderer sr = obj.GetComponent<SpriteRenderer>();
            if (sr != null) whirlwind.GroundY = -sr.bounds.extents.y * (1f / MiniWhirlwindScale - 1f);
        }
    }

    private void FireOrb(float damage, float critChance, EquippedSkill skill)
    {
        PlayCastSfx(orbCastSfx, orbCastSfxVolume);

        if (skill.PathTier[2] >= 2) // 낙뢰 연계 path T2: 날아가는 오브 대신 캐릭터 앞에 고정 설치기 소환
        {
            Vector3 altarPos = transform.position + Vector3.left * 1.2f;
            SpawnOrbAltar(altarPos, damage, critChance, skill);
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
    }

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

        int overhealPerHit = 0; // 건강 연계 path: 초과체력 획득 (T1, T3에서 2씩)
        if (skill.PathTier[1] >= 1) overhealPerHit += 2;
        if (skill.PathTier[1] >= 3) overhealPerHit += 2;

        int dropCount = 3 + (skill.PathTier[0] >= 1 ? 1 : 0) - (skill.PathTier[2] >= 1 ? 1 : 0); // 기본 path T1: 투하 횟수 +1, 회오리 연계 path T1: 투하 횟수 -1 (쿨감 트레이드오프)
        bool scaleByEnemyCount = skill.PathTier[0] >= 2; // 기본 path T2: 화면 내 적 수에 반비례한 피해량 스케일링(최대 450%)
        float t3DamageMult = skill.PathTier[0] >= 3 ? 1.5f : 1f; // 기본 path T3: 피해량 50% 증가
        float interval = skill.PathTier[0] >= 3 ? 0.5f : 1f; // 기본 path T3: 투하 간격 50% 감소

        for (int i = 0; i < dropCount; i++)
        {
            List<Enemy> enemies = new List<Enemy>(FindObjectsByType<Enemy>(FindObjectsSortMode.None));

            float countMult = 1f;
            if (scaleByEnemyCount)
            {
                int count = Mathf.Max(enemies.Count, 1);
                countMult = Mathf.Clamp(450f / count, 100f, 450f) / 100f;
            }
            float dropDamage = damage * countMult * t3DamageMult;

            foreach (Enemy enemy in enemies)
            {
                Vector3 pos = enemy.transform.position;
                enemy.TakeSkillHit(dropDamage, critChance, ActiveSkillId.EagleDrop);

                if (overhealPerHit > 0 && health != null) health.AddOverheal(overhealPerHit);
                if (spawnMiniWhirlwind) SpawnWhirlwind(pos, dropDamage * miniWhirlwindDamageMult * (1f + MiniWhirlwindDamageBonus), critChance, skill.Scale * MiniWhirlwindScale, false, false, maxHitCount: miniWhirlwindMaxHits, isMini: true);

                StartCoroutine(MeteorImpact(pos, skill.Scale));
            }

            yield return new WaitForSeconds(interval);
        }
    }

    private IEnumerator MiniEagleBonus(Enemy target, float damage, float critChance, float scale)
    {
        if (target == null) yield break;
        Vector3 pos = target.transform.position;
        yield return StartCoroutine(MeteorImpact(pos, scale));
        if (target == null) yield break;

        float hitDamage = PlayerPassives.ApplyCrit(damage, critChance, out bool isCrit);
        target.TakeDamage(hitDamage, isCrit: isCrit, source: ActiveSkillId.BasicAttack);
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
