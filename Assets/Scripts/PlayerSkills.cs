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
}

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
    public int ThirdSlotCount = 0; // 레벨업 3번째 슬롯(스킬 고유 강화)이 몇 번째로 발동됐는지 — 스킬별 순환 스케줄에 사용

    // 레벨업 전용 고유 강화치 (진화 트리와 별개)
    public int ExtraPierce = 0; // 기본공격: 관통 +1
    public int ExtraProjectiles = 0; // 기본공격: 위아래로 추가 발사 (1당 위/아래 1발씩)
    public float ExtraWhirlwindDuration = 0f; // 회오리: 지속시간(초) 추가
    public float ExtraEagleDropIntervalReduction = 0f; // 독수리투하: 투하 간격(초) 감소

    // 진화 트리: path 0=기본(무의존), 1=패시브 연계, 2=액티브 연계. 각 값은 도달한 티어(0~3).
    public readonly int[] PathTier = new int[3];
    public int TotalEvolutionTier => PathTier[0] + PathTier[1] + PathTier[2];
}

public class PlayerSkills : MonoBehaviour
{
    private const float GlobalCooldown = 0.4f;
    private const float OrbAltarCooldown = 15f;
    private static readonly Key[] SlotKeys = { Key.Q, Key.W, Key.E, Key.R };

    [SerializeField] private GameObject basicAttackProjectilePrefab;
    [SerializeField] private GameObject whirlwindPrefab;
    [SerializeField] private GameObject bigTornadoPrefab;
    [SerializeField] private GameObject orbPrefab;
    [SerializeField] private GameObject orbAltarPrefab;
    [SerializeField] private GameObject eagleDropPrefab;
    [SerializeField] private GameObject eagleImpactVfxPrefab;
    [SerializeField] private Animator animator;
    [SerializeField] private EvolutionTierTextTableSO evolutionTextOverrides;

    [SerializeField] private AudioClip whirlwindCastSfx;
    [SerializeField] private AudioClip orbCastSfx;
    [SerializeField] private AudioClip eagleDropCastSfx;
    [SerializeField] private float castSfxVolume = 0.7f;

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
        passives = GetComponent<PlayerPassives>();
        health = GetComponent<PlayerHealth>();
        AcquireSkill(ActiveSkillId.BasicAttack);
    }

    public void IncreaseDamageMultiplier(float amount)
    {
        passiveDamageMultiplier += amount;
    }

    private void Update()
    {
        globalCooldownTimer -= Time.deltaTime;

        foreach (EquippedSkill skill in equippedSkills)
        {
            skill.CooldownTimer -= Time.deltaTime;

            if (Keyboard.current[skill.Key].wasPressedThisFrame)
                TryUseSkill(skill);
        }
    }

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

    private static void ApplyUpgradeEffect(EquippedSkill skill, int level)
    {
        switch (level % 3)
        {
            case 1:
                skill.Damage *= 1.2f;
                break;
            case 2:
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.95f);
                break;
            default:
                ApplyThirdUpgradeEffect(skill);
                break;
        }
    }

    // 스킬별 3번째 슬롯 고유 강화는 ThirdSlotCount(몇 번째 발동인지)를 기준으로 순환한다.
    // Describe와 Apply가 같은 occurrence(=ThirdSlotCount, 아직 증가 전 값)를 참조해야 미리보기 텍스트와 실제 적용이 일치한다.
    private static void ApplyThirdUpgradeEffect(EquippedSkill skill)
    {
        int occurrence = skill.ThirdSlotCount;
        switch (skill.Id)
        {
            case ActiveSkillId.BasicAttack:
                switch (occurrence % 3)
                {
                    case 0: skill.ExtraPierce += 1; break;
                    case 1: skill.ProjectileSpeedMultiplier += 0.1f; break;
                    default: skill.ExtraProjectiles += 1; break;
                }
                break;
            case ActiveSkillId.Lightning:
                skill.ProcChanceBonus += 0.03f;
                break;
            case ActiveSkillId.Whirlwind:
                if (occurrence % 2 == 0) skill.ExtraWhirlwindDuration += 0.5f;
                else skill.Scale += 0.05f;
                break;
            case ActiveSkillId.EagleDrop:
                skill.ExtraEagleDropIntervalReduction += 0.5f;
                break;
            default: // Orb
                skill.Scale += 0.05f;
                break;
        }
        skill.ThirdSlotCount++;
    }

    public static string DescribeUpgradeEffect(EquippedSkill skill, int nextLevel)
    {
        switch (nextLevel % 3)
        {
            case 1: return "피해량 20% 증가";
            case 2: return "재사용 대기시간 5% 감소";
            default: return DescribeThirdUpgradeEffect(skill);
        }
    }

    private static string DescribeThirdUpgradeEffect(EquippedSkill skill)
    {
        int occurrence = skill.ThirdSlotCount;
        switch (skill.Id)
        {
            case ActiveSkillId.BasicAttack:
                return (occurrence % 3) switch
                {
                    0 => "관통 1회 추가",
                    1 => "투사체 속도 10% 증가",
                    _ => "위아래로 투사체 1발씩 추가 발사",
                };
            case ActiveSkillId.Lightning:
                return "발동 확률 3%p 증가";
            case ActiveSkillId.Whirlwind:
                return occurrence % 2 == 0 ? "지속시간 0.5초 증가" : "크기 5% 증가";
            case ActiveSkillId.EagleDrop:
                return "투하 간격 0.5초 감소";
            default: // Orb
                return "크기 5% 증가";
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
                skill.Damage *= 1.25f;
                break;
            case (ActiveSkillId.Orb, 0, 2):
            case (ActiveSkillId.Lightning, 0, 2):
                skill.Damage *= 1.4f; // 피해량 40% 증가
                break;
            // EagleDrop path0 T2(화면 내 적 수 반비례 스케일링)는 EagleDropRoutine에서 매 캐스트마다 실시간 계산
            // Lightning path0 T3(재귀마다 피해량 누적 증가)는 LightningStorm.RecursiveDamageGrowth로 실시간 계산

            // path1(패시브 연계)
            case (ActiveSkillId.Whirlwind, 1, 2):
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
                skill.Cooldown += 3f; // 쿨타임 3초 증가
                skill.Damage *= 1.4f; // 피해량 40% 증가
                break;
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

    private bool HasPathPrereq(ActiveSkillId skillId, int path)
    {
        if (path == 1)
        {
            PassiveSkillId? req = GetPassivePrereq(skillId);
            return req.HasValue && passives != null && passives.HasPassive(req.Value);
        }
        if (path == 2)
        {
            ActiveSkillId? req = GetActivePrereq(skillId);
            return req.HasValue && HasSkill(req.Value);
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
        _ => id.ToString(),
    };

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
        (ActiveSkillId.BasicAttack, 0, 1) => "관통 3회 추가",
        (ActiveSkillId.BasicAttack, 0, 2) => "피해량 100% 증가",
        (ActiveSkillId.BasicAttack, 0, 3) => "관통 5회 추가 (총 8회)",
        (ActiveSkillId.BasicAttack, 1, 1) => "기본공격 치명타 확률 40% 증가",
        (ActiveSkillId.BasicAttack, 1, 2) => "치명타 적중 시 투사체 1회 추가 발사",
        (ActiveSkillId.BasicAttack, 1, 3) => "재사용 대기시간 50% 감소",
        (ActiveSkillId.BasicAttack, 2, 1) => "명중 시 미니 독수리 투하 (피해량 40%)",
        (ActiveSkillId.BasicAttack, 2, 2) => "미니 독수리가 주변 적까지 확산 (최대 4마리)",
        (ActiveSkillId.BasicAttack, 2, 3) => "미니 독수리 확산 범위 확대 (최대 10마리)",

        // Whirlwind
        (ActiveSkillId.Whirlwind, 0, 1) => "회오리 1개 추가 소환 (총 2개)",
        (ActiveSkillId.Whirlwind, 0, 2) => "피해량 25% 증가",
        (ActiveSkillId.Whirlwind, 0, 3) => "회오리 1개 추가 소환 (총 3개)",
        (ActiveSkillId.Whirlwind, 1, 1) => "리프레쉬 발동 시 전체 스킬 쿨타임 1초 감소",
        (ActiveSkillId.Whirlwind, 1, 2) => "재사용 대기시간 15% 감소",
        (ActiveSkillId.Whirlwind, 1, 3) => "재사용 대기시간 10% 추가 감소",
        (ActiveSkillId.Whirlwind, 2, 1) => "적 둔화 부여 (30% 감속, 3초)",
        (ActiveSkillId.Whirlwind, 2, 2) => "거대 회오리로 변화 (분열 없이 하나의 거대한 회오리, 둔화 지속시간 4.5초)",
        (ActiveSkillId.Whirlwind, 2, 3) => "취약 부여 (추가 피해 50%)",

        // Orb
        (ActiveSkillId.Orb, 0, 1) => "공중 타격 가능, 공중 적 추가 피해 40%",
        (ActiveSkillId.Orb, 0, 2) => "피해량 40% 증가",
        (ActiveSkillId.Orb, 0, 3) => "공중 적 추가 피해 100%",
        (ActiveSkillId.Orb, 1, 1) => "슬로우 효과 10%p 강화",
        (ActiveSkillId.Orb, 1, 2) => "재사용 대기시간 25% 감소, 오브 크기 증가",
        (ActiveSkillId.Orb, 1, 3) => "슬로우 효과 10%p 추가 강화 (총 20%p), 재사용 대기시간 10% 추가 감소",
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
        (ActiveSkillId.EagleDrop, 0, 1) => "투하 횟수 1회 추가 (총 4회)",
        (ActiveSkillId.EagleDrop, 0, 2) => "화면 내 적이 적을수록 피해량 증가 (최대 450%)",
        (ActiveSkillId.EagleDrop, 0, 3) => "투하 간격 50% 감소, 피해량 50% 증가",
        (ActiveSkillId.EagleDrop, 1, 1) => "적중 시 초과체력(오버힐) 2 획득",
        (ActiveSkillId.EagleDrop, 1, 2) => "재사용 대기시간 15% 감소",
        (ActiveSkillId.EagleDrop, 1, 3) => "오버힐 획득량 2 추가 (총 4)",
        (ActiveSkillId.EagleDrop, 2, 1) => "재사용 대기시간 3초 증가, 피해량 40% 증가",
        (ActiveSkillId.EagleDrop, 2, 2) => "착탄 시 미니 회오리 생성 (피해량 30%, 최대 6회 타격)",
        (ActiveSkillId.EagleDrop, 2, 3) => "미니 회오리 피해량 45%로 증가, 최대 타격 횟수 +5회 (총 11회)",

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

        (ActiveSkillId.Whirlwind, 0, 1) => "분열 소환",
        (ActiveSkillId.Whirlwind, 0, 2) => "피해량 강화",
        (ActiveSkillId.Whirlwind, 0, 3) => "분열 소환 II",
        (ActiveSkillId.Whirlwind, 1, 1) => "쿨타임 리셋",
        (ActiveSkillId.Whirlwind, 1, 2) => "쿨타임 감소",
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
        (ActiveSkillId.EagleDrop, 2, 1) => "피해 강화 (쿨타임 증가)",
        (ActiveSkillId.EagleDrop, 2, 2) => "미니 회오리",
        (ActiveSkillId.EagleDrop, 2, 3) => "미니 회오리 강화",

        _ => "",
    };

    private void TryUseSkill(EquippedSkill skill)
    {
        if (globalCooldownTimer > 0f || skill.CooldownTimer > 0f) return;

        // 타격 기준 치명타: 캐스트 시점엔 확률만 확정하고, 실제 치명타 여부는 각 데미지 이벤트(투사체 명중/틱)마다 개별적으로 굴린다.
        float critChance = GetCritChance(skill);
        float damage = ComputeBaseDamage(skill.Damage);

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
                // 낙뢰 연계 path2 T1: 낙뢰 지속시간 30% 증가
                float baseDuration = 6f * (skill.PathTier[2] >= 1 ? 1.3f : 1f);
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
        }

        globalCooldownTimer = GlobalCooldown;
        // 오브가 설치기(낙뢰 연계)로 대체된 상태에서는 훨씬 긴 별도 쿨타임을 사용
        skill.CooldownTimer = (skill.Id == ActiveSkillId.Orb && skill.PathTier[2] >= 2) ? OrbAltarCooldown : skill.Cooldown;

        if (passives != null && passives.HasPassive(PassiveSkillId.Refresh) && Random.value < PlayerPassives.RefreshChance)
        {
            skill.CooldownTimer = 0f;
            if (skill.Id == ActiveSkillId.Whirlwind && skill.PathTier[1] >= 1)
                ReduceAllCooldowns(1f);
        }
    }

    // 스테이지가 넘어갈 때 GameManager가 호출 — 모든 스킬 쿨타임 초기화
    public void ResetAllCooldowns()
    {
        globalCooldownTimer = 0f;
        foreach (EquippedSkill s in equippedSkills)
            s.CooldownTimer = 0f;
    }

    private void ReduceAllCooldowns(float amount)
    {
        foreach (EquippedSkill s in equippedSkills)
            s.CooldownTimer = Mathf.Max(0f, s.CooldownTimer - amount);
    }

    // 타격 기준 치명타: 여기선 "이번 캐스트에 적용될 확률"만 정하고, 실제 발동 여부는 각 데미지 이벤트에서 개별적으로 굴린다.
    private float GetCritChance(EquippedSkill skill)
    {
        // 암살 연계 path1 T1: 기본공격 전용 추가 치명타 확률
        float chance = skill.Id == ActiveSkillId.BasicAttack && skill.PathTier[1] >= 1 ? 0.4f : 0f;
        if (passives != null && passives.HasPassive(PassiveSkillId.Assassinate))
            chance += PlayerPassives.AssassinateCritChance;
        return chance;
    }

    private float ComputeBaseDamage(float baseDamage)
    {
        float damage = baseDamage * passiveDamageMultiplier;

        // 낙뢰 연계 path2 T3: 낙뢰 버프 중첩당 전체 공격 피해량 증가
        if (LightningStorm.StackDamageEnabled && LightningStorm.ActiveStackCount > 0)
            damage *= 1f + LightningStorm.StackDamageBonusPerStack * LightningStorm.ActiveStackCount;

        return damage;
    }

    private bool FireBasicAttack(EquippedSkill skill, float damage, float critChance, bool allowBonusShot)
    {
        Enemy target = FindFrontmostEnemy();
        if (target == null) return false;

        int pierce = 0; // 기본 path: 관통 (T1=3회, T3=추가 5회)
        if (skill.PathTier[0] >= 1) pierce += 3;
        if (skill.PathTier[0] >= 3) pierce += 5;
        pierce += skill.ExtraPierce; // 레벨업 고유 강화

        SpawnBasicAttackProjectile(skill, damage, critChance, pierce, 0f, allowBonusShot);
        for (int i = 1; i <= skill.ExtraProjectiles; i++) // 레벨업 고유 강화: 위아래로 추가 발사
        {
            SpawnBasicAttackProjectile(skill, damage, critChance, pierce, 0.5f * i, allowBonusShot);
            SpawnBasicAttackProjectile(skill, damage, critChance, pierce, -0.5f * i, allowBonusShot);
        }

        animator.SetTrigger("Attack");
        return true;
    }

    private void SpawnBasicAttackProjectile(EquippedSkill skill, float damage, float critChance, int pierce, float verticalOffset, bool allowBonusShot)
    {
        GameObject obj = Instantiate(basicAttackProjectilePrefab, transform.position + Vector3.left * 0.6f + Vector3.down * 0.25f + Vector3.up * verticalOffset, Quaternion.identity);
        obj.transform.localScale *= skill.Scale;
        Projectile projectile = obj.GetComponent<Projectile>();
        projectile.Damage = damage;
        projectile.CritChance = critChance;
        projectile.SpeedMultiplier = skill.ProjectileSpeedMultiplier;
        projectile.PierceRemaining = pierce;

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

    private void FireWhirlwind(float damage, float critChance, EquippedSkill skill)
    {
        bool applySlow = skill.PathTier[2] >= 1;
        bool applyVulnerable = skill.PathTier[2] >= 3;

        if (skill.PathTier[2] >= 2) // 오브 연계 path T2: 거대 회오리로 대체 (여러 개로 안 쪼개짐)
        {
            Vector3 spawnPos = transform.position + Vector3.left * 0.6f + Vector3.down * 0.7f;
            SpawnBigTornado(spawnPos, damage, critChance, skill.Scale, applySlow, applyVulnerable, skill.ExtraWhirlwindDuration);
        }
        else
        {
            int count = 1; // 기본 path: 소형 회오리 분열 소환 (T1, T3에서 1개씩 추가)
            if (skill.PathTier[0] >= 1) count++;
            if (skill.PathTier[0] >= 3) count++;
            float perDamage = damage / count;
            float perScale = skill.Scale / Mathf.Sqrt(count);

            for (int i = 0; i < count; i++)
            {
                Vector2 offset = count == 1 ? Vector2.zero : Random.insideUnitCircle * 0.4f;
                Vector3 spawnPos = transform.position + Vector3.left * 0.6f + Vector3.up * 0.6f + (Vector3)offset;
                SpawnWhirlwind(spawnPos, perDamage, critChance, perScale, applySlow, applyVulnerable, maxHitCount: 0, slowDuration: 3f, extraLifetime: skill.ExtraWhirlwindDuration);
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
        PlayCastSfx(whirlwindCastSfx, castSfxVolume);
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

    private void SpawnWhirlwind(Vector3 position, float damage, float critChance, float scale, bool applySlow, bool applyVulnerable, int maxHitCount = 0, float slowDuration = 3f, float extraLifetime = 0f)
    {
        PlayCastSfx(whirlwindCastSfx, castSfxVolume);
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
    }

    private void FireOrb(float damage, float critChance, EquippedSkill skill)
    {
        PlayCastSfx(orbCastSfx, castSfxVolume);

        if (skill.PathTier[2] >= 2) // 낙뢰 연계 path T2: 날아가는 오브 대신 캐릭터 앞에 고정 설치기 소환
        {
            Vector3 altarPos = transform.position + Vector3.left * 1.2f;
            SpawnOrbAltar(altarPos, damage, critChance, skill);
            return;
        }

        GameObject obj = Instantiate(orbPrefab, transform.position + Vector3.down * 0.1f, Quaternion.identity);
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

        bool spawnMiniWhirlwind = skill.PathTier[2] >= 2; // 회오리 연계 path T2: 미니 회오리 생성 (T1은 쿨타임/피해량 트레이드오프)
        float miniWhirlwindDamageMult = skill.PathTier[2] >= 3 ? 0.45f : 0.3f; // T2=30%, T3=45%(30%에서 50% 증가)
        int miniWhirlwindMaxHits = skill.PathTier[2] >= 3 ? 11 : 6; // T2=6회, T3=+5(총 11회)

        int overhealPerHit = 0; // 건강 연계 path: 초과체력 획득 (T1, T3에서 2씩)
        if (skill.PathTier[1] >= 1) overhealPerHit += 2;
        if (skill.PathTier[1] >= 3) overhealPerHit += 2;

        int dropCount = 3 + (skill.PathTier[0] >= 1 ? 1 : 0); // 기본 path T1: 투하 횟수 +1 (총 4회)
        bool scaleByEnemyCount = skill.PathTier[0] >= 2; // 기본 path T2: 화면 내 적 수에 반비례한 피해량 스케일링(최대 450%)
        float t3DamageMult = skill.PathTier[0] >= 3 ? 1.5f : 1f; // 기본 path T3: 피해량 50% 증가
        float interval = Mathf.Max(0.2f, (skill.PathTier[0] >= 3 ? 0.5f : 1f) - skill.ExtraEagleDropIntervalReduction); // 기본 path T3: 투하 간격 50% 감소 + 레벨업 고유 강화

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
                float hitDamage = PlayerPassives.ApplyCrit(dropDamage, critChance, out bool isCrit);
                enemy.TakeDamage(hitDamage, isCrit: isCrit);

                if (overhealPerHit > 0 && health != null) health.AddOverheal(overhealPerHit);
                if (spawnMiniWhirlwind) SpawnWhirlwind(pos, dropDamage * miniWhirlwindDamageMult, critChance, skill.Scale * 0.4f, false, false, maxHitCount: miniWhirlwindMaxHits);

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
        target.TakeDamage(hitDamage, isCrit: isCrit);
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

    private Enemy FindFrontmostEnemy()
    {
        Enemy[] enemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
        Enemy frontmost = null;
        float maxX = float.NegativeInfinity;

        foreach (Enemy enemy in enemies)
        {
            if (enemy.transform.position.x > maxX)
            {
                maxX = enemy.transform.position.x;
                frontmost = enemy;
            }
        }

        return frontmost;
    }

    private static float GetDefaultCooldown(ActiveSkillId id) => id switch
    {
        ActiveSkillId.BasicAttack => 1f,
        ActiveSkillId.Whirlwind => 5f,
        ActiveSkillId.Orb => 7f,
        ActiveSkillId.Lightning => 12f,
        ActiveSkillId.EagleDrop => 15f,
        _ => 1f,
    };

    private static float GetDefaultDamage(ActiveSkillId id) => id switch
    {
        ActiveSkillId.BasicAttack => 20f,
        ActiveSkillId.Whirlwind => 6f,
        ActiveSkillId.Orb => 7f,
        ActiveSkillId.Lightning => LightningStorm.ProcDamage,
        ActiveSkillId.EagleDrop => 10f,
        _ => 6f,
    };
}
