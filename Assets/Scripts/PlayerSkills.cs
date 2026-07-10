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

    // 진화 트리: path 0=기본(무의존), 1=패시브 연계, 2=액티브 연계. 각 값은 도달한 티어(0~3).
    public readonly int[] PathTier = new int[3];
    public int TotalEvolutionTier => PathTier[0] + PathTier[1] + PathTier[2];
}

public class PlayerSkills : MonoBehaviour
{
    private const float GlobalCooldown = 0.4f;
    private const float OrbAltarCooldown = 20f;
    private static readonly Vector3 MapCenter = new Vector3(0f, 0.1f, 0f);
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
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.9f);
                break;
            default:
                ApplyThirdUpgradeEffect(skill);
                break;
        }
    }

    private static void ApplyThirdUpgradeEffect(EquippedSkill skill)
    {
        switch (skill.Id)
        {
            case ActiveSkillId.BasicAttack:
                skill.ProjectileSpeedMultiplier += 0.1f;
                break;
            case ActiveSkillId.Lightning:
                skill.ProcChanceBonus += 0.03f;
                break;
            default:
                skill.Scale += 0.05f;
                break;
        }
    }

    public static string DescribeUpgradeEffect(ActiveSkillId id, int nextLevel)
    {
        switch (nextLevel % 3)
        {
            case 1: return "피해량 20% 증가";
            case 2: return "재사용 대기시간 10% 감소";
            default:
                return id switch
                {
                    ActiveSkillId.BasicAttack => "투사체 속도 10% 증가",
                    ActiveSkillId.Lightning => "발동 확률 3%p 증가",
                    _ => "크기 5% 증가",
                };
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

    // 티어마다 정확히 하나의 효과만 부여한다 (수치 강화는 path0=T2, path1=T2 위주, 그 외 티어는 고유 기믹 unlock).
    // 기믹(관통 횟수, 분열 소환 수, 슬로우 강화 등)은 PathTier를 직접 읽는 Fire* 메서드에서 계산한다.
    private static void ApplyPathTierEffect(EquippedSkill skill, int path, int newTier)
    {
        switch (skill.Id, path, newTier)
        {
            // path0(기본) - 피해량 25% 증가는 각 스킬 T2에서 한 번만 부여 (낙뢰는 T3에 20%p 추가)
            case (ActiveSkillId.BasicAttack, 0, 2):
            case (ActiveSkillId.Whirlwind, 0, 2):
            case (ActiveSkillId.Orb, 0, 2):
            case (ActiveSkillId.Lightning, 0, 2):
            case (ActiveSkillId.EagleDrop, 0, 2):
                skill.Damage *= 1.25f;
                break;
            case (ActiveSkillId.Lightning, 0, 3):
                skill.Damage *= 1.2f;
                break;

            // path1(패시브 연계) - 쿨타임 감소는 T2 위주
            case (ActiveSkillId.BasicAttack, 1, 2):
            case (ActiveSkillId.Whirlwind, 1, 2):
            case (ActiveSkillId.Orb, 1, 2):
            case (ActiveSkillId.EagleDrop, 1, 2):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.85f);
                break;
            case (ActiveSkillId.BasicAttack, 1, 3):
            case (ActiveSkillId.Whirlwind, 1, 3):
            case (ActiveSkillId.Orb, 1, 3):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.9f);
                break;
            case (ActiveSkillId.Lightning, 1, 2):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.85f);
                break;
            case (ActiveSkillId.Lightning, 1, 3):
                skill.Cooldown = Mathf.Max(GlobalCooldown, skill.Cooldown * 0.9f);
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
        (ActiveSkillId.BasicAttack, 0, 1) => "관통 1회 추가",
        (ActiveSkillId.BasicAttack, 0, 2) => "피해량 25% 증가",
        (ActiveSkillId.BasicAttack, 0, 3) => "관통 1회 추가 (총 2회)",
        (ActiveSkillId.BasicAttack, 1, 1) => "치명타 적중 시 투사체 1회 추가 발사",
        (ActiveSkillId.BasicAttack, 1, 2) => "재사용 대기시간 15% 감소",
        (ActiveSkillId.BasicAttack, 1, 3) => "재사용 대기시간 10% 추가 감소",
        (ActiveSkillId.BasicAttack, 2, 1) => "명중 시 미니 독수리 투하 (피해량 40%)",
        (ActiveSkillId.BasicAttack, 2, 2) => "미니 독수리 피해량 10%p 증가 (총 50%)",
        (ActiveSkillId.BasicAttack, 2, 3) => "미니 독수리 피해량 10%p 증가 (총 60%)",

        // Whirlwind
        (ActiveSkillId.Whirlwind, 0, 1) => "회오리 1개 추가 소환 (총 2개)",
        (ActiveSkillId.Whirlwind, 0, 2) => "피해량 25% 증가",
        (ActiveSkillId.Whirlwind, 0, 3) => "회오리 1개 추가 소환 (총 3개)",
        (ActiveSkillId.Whirlwind, 1, 1) => "리프레쉬 발동 시 전체 스킬 쿨타임 1초 감소",
        (ActiveSkillId.Whirlwind, 1, 2) => "재사용 대기시간 15% 감소",
        (ActiveSkillId.Whirlwind, 1, 3) => "재사용 대기시간 10% 추가 감소",
        (ActiveSkillId.Whirlwind, 2, 1) => "적 둔화 부여 (30% 감속, 3초)",
        (ActiveSkillId.Whirlwind, 2, 2) => "개큰 회오리로 변화 (분열 없이 하나의 거대한 회오리, 둔화 지속시간 4.5초)",
        (ActiveSkillId.Whirlwind, 2, 3) => "취약 부여 (추가 피해 50%)",

        // Orb
        (ActiveSkillId.Orb, 0, 1) => "공중 적 추가 피해 25%",
        (ActiveSkillId.Orb, 0, 2) => "피해량 25% 증가",
        (ActiveSkillId.Orb, 0, 3) => "공중 적 추가 피해 25%p 증가 (총 50%)",
        (ActiveSkillId.Orb, 1, 1) => "슬로우 효과 10%p 강화",
        (ActiveSkillId.Orb, 1, 2) => "재사용 대기시간 15% 감소",
        (ActiveSkillId.Orb, 1, 3) => "슬로우 효과 10%p 추가 강화 (총 20%p)",
        (ActiveSkillId.Orb, 2, 1) => "취약 부여 (추가 피해 50%)",
        (ActiveSkillId.Orb, 2, 2) => "오브 설치기로 대체 (제자리에 설치되어 8방향 미니 오브 2초마다 발사 + 4초마다 화면 내 모든 적에게 낙뢰)",
        (ActiveSkillId.Orb, 2, 3) => "설치기 피해량 50% 증가",

        // Lightning
        (ActiveSkillId.Lightning, 0, 1) => "낙뢰가 낙뢰를 유발 가능 (최대 4연쇄)",
        (ActiveSkillId.Lightning, 0, 2) => "피해량 25% 증가",
        (ActiveSkillId.Lightning, 0, 3) => "피해량 20%p 추가 증가 (총 50%)",
        (ActiveSkillId.Lightning, 1, 1) => "체인 라이트닝: 낙뢰 첫 발동 시 주변 적 3마리에게 전이",
        (ActiveSkillId.Lightning, 1, 2) => "재사용 대기시간 15% 감소",
        (ActiveSkillId.Lightning, 1, 3) => "재사용 대기시간 10% 추가 감소 (총 23.5%)",
        (ActiveSkillId.Lightning, 2, 1) => "회오리 소환 시 낙뢰 지속시간 1.5초 증가",
        (ActiveSkillId.Lightning, 2, 2) => "회오리 소환 시 낙뢰 지속시간 1.5초 추가 증가 (총 3초)",
        (ActiveSkillId.Lightning, 2, 3) => "낙뢰 발동 확률 10%p 증가",

        // EagleDrop
        (ActiveSkillId.EagleDrop, 0, 1) => "투하 간격 15% 감소",
        (ActiveSkillId.EagleDrop, 0, 2) => "피해량 25% 증가",
        (ActiveSkillId.EagleDrop, 0, 3) => "투하 횟수 1회 추가 (총 4회)",
        (ActiveSkillId.EagleDrop, 1, 1) => "적중 시 초과체력(오버힐) 2 획득",
        (ActiveSkillId.EagleDrop, 1, 2) => "재사용 대기시간 15% 감소",
        (ActiveSkillId.EagleDrop, 1, 3) => "오버힐 획득량 2 추가 (총 4)",
        (ActiveSkillId.EagleDrop, 2, 1) => "착탄 시 미니 회오리 생성 (피해량 30%, 10회 피해 후 소멸)",
        (ActiveSkillId.EagleDrop, 2, 2) => "미니 회오리 피해량 10%p 증가 (총 40%)",
        (ActiveSkillId.EagleDrop, 2, 3) => "미니 회오리 최대 타격 횟수 5회 증가 (총 15회)",

        _ => "",
    };

    // 진화 카드에 붙는 짧은 제목 (설명 문장과 별개)
    public static string GetPathTierTitle(ActiveSkillId id, int path, int tier) => (id, path, tier) switch
    {
        (ActiveSkillId.BasicAttack, 0, 1) => "관통 강화",
        (ActiveSkillId.BasicAttack, 0, 2) => "피해량 강화",
        (ActiveSkillId.BasicAttack, 0, 3) => "관통 강화 II",
        (ActiveSkillId.BasicAttack, 1, 1) => "치명 연사",
        (ActiveSkillId.BasicAttack, 1, 2) => "쿨타임 감소",
        (ActiveSkillId.BasicAttack, 1, 3) => "쿨타임 감소 II",
        (ActiveSkillId.BasicAttack, 2, 1) => "미니 독수리",
        (ActiveSkillId.BasicAttack, 2, 2) => "미니 독수리 강화",
        (ActiveSkillId.BasicAttack, 2, 3) => "미니 독수리 강화 II",

        (ActiveSkillId.Whirlwind, 0, 1) => "분열 소환",
        (ActiveSkillId.Whirlwind, 0, 2) => "피해량 강화",
        (ActiveSkillId.Whirlwind, 0, 3) => "분열 소환 II",
        (ActiveSkillId.Whirlwind, 1, 1) => "쿨타임 리셋",
        (ActiveSkillId.Whirlwind, 1, 2) => "쿨타임 감소",
        (ActiveSkillId.Whirlwind, 1, 3) => "쿨타임 감소 II",
        (ActiveSkillId.Whirlwind, 2, 1) => "둔화 부여",
        (ActiveSkillId.Whirlwind, 2, 2) => "개큰 회오리",
        (ActiveSkillId.Whirlwind, 2, 3) => "취약 부여",

        (ActiveSkillId.Orb, 0, 1) => "공중 추가피해",
        (ActiveSkillId.Orb, 0, 2) => "피해량 강화",
        (ActiveSkillId.Orb, 0, 3) => "공중 추가피해 II",
        (ActiveSkillId.Orb, 1, 1) => "슬로우 강화",
        (ActiveSkillId.Orb, 1, 2) => "쿨타임 감소",
        (ActiveSkillId.Orb, 1, 3) => "슬로우 강화 II",
        (ActiveSkillId.Orb, 2, 1) => "취약 부여",
        (ActiveSkillId.Orb, 2, 2) => "오브 설치기",
        (ActiveSkillId.Orb, 2, 3) => "설치기 강화",

        (ActiveSkillId.Lightning, 0, 1) => "연쇄 낙뢰",
        (ActiveSkillId.Lightning, 0, 2) => "피해량 강화",
        (ActiveSkillId.Lightning, 0, 3) => "피해량 강화 II",
        (ActiveSkillId.Lightning, 1, 1) => "체인 라이트닝",
        (ActiveSkillId.Lightning, 1, 2) => "쿨타임 감소",
        (ActiveSkillId.Lightning, 1, 3) => "쿨타임 감소 II",
        (ActiveSkillId.Lightning, 2, 1) => "지속시간 강화",
        (ActiveSkillId.Lightning, 2, 2) => "지속시간 강화 II",
        (ActiveSkillId.Lightning, 2, 3) => "발동확률 강화",

        (ActiveSkillId.EagleDrop, 0, 1) => "투하 간격 감소",
        (ActiveSkillId.EagleDrop, 0, 2) => "피해량 강화",
        (ActiveSkillId.EagleDrop, 0, 3) => "투하 횟수 증가",
        (ActiveSkillId.EagleDrop, 1, 1) => "오버힐 획득",
        (ActiveSkillId.EagleDrop, 1, 2) => "쿨타임 감소",
        (ActiveSkillId.EagleDrop, 1, 3) => "오버힐 강화",
        (ActiveSkillId.EagleDrop, 2, 1) => "미니 회오리",
        (ActiveSkillId.EagleDrop, 2, 2) => "미니 회오리 강화",
        (ActiveSkillId.EagleDrop, 2, 3) => "미니 회오리 강화 II",

        _ => "",
    };

    private void TryUseSkill(EquippedSkill skill)
    {
        if (globalCooldownTimer > 0f || skill.CooldownTimer > 0f) return;

        float damage = ComputeFinalDamage(skill.Damage, out bool wasCrit);

        switch (skill.Id)
        {
            case ActiveSkillId.BasicAttack:
                if (!FireBasicAttack(damage, skill, wasCrit)) return;
                if (wasCrit && skill.PathTier[1] >= 1)
                {
                    float extraDamage = ComputeFinalDamage(skill.Damage, out bool extraCrit);
                    FireBasicAttack(extraDamage, skill, extraCrit);
                }
                break;
            case ActiveSkillId.Whirlwind:
                FireWhirlwind(damage, skill, wasCrit);
                break;
            case ActiveSkillId.Orb:
                FireOrb(damage, skill, wasCrit);
                break;
            case ActiveSkillId.Lightning:
                LightningStorm.ActiveUntil = Time.time + 6f;
                LightningStorm.ProcChance = LightningStorm.BaseProcChance + skill.ProcChanceBonus + (skill.PathTier[2] >= 3 ? 0.1f : 0f);
                LightningStorm.ProcDamage = damage;
                LightningStorm.RecursiveProcEnabled = skill.PathTier[0] >= 1;
                LightningStorm.ChainEnabled = skill.PathTier[1] >= 1;
                break;
            case ActiveSkillId.EagleDrop:
                StartCoroutine(EagleDropRoutine(damage, skill, wasCrit));
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

    private float ComputeFinalDamage(float baseDamage, out bool wasCrit)
    {
        float damage = baseDamage * passiveDamageMultiplier;
        wasCrit = false;

        if (passives != null && passives.HasPassive(PassiveSkillId.Assassinate) && Random.value < PlayerPassives.AssassinateCritChance)
        {
            damage *= PlayerPassives.AssassinateCritMultiplier;
            wasCrit = true;
        }

        return damage;
    }

    private bool FireBasicAttack(float damage, EquippedSkill skill, bool isCrit = false)
    {
        Enemy target = FindFrontmostEnemy();
        if (target == null) return false;

        GameObject obj = Instantiate(basicAttackProjectilePrefab, transform.position + Vector3.left * 0.6f + Vector3.down * 0.25f, Quaternion.identity);
        obj.transform.localScale *= skill.Scale;
        Projectile projectile = obj.GetComponent<Projectile>();
        projectile.Damage = damage;
        projectile.IsCrit = isCrit;
        projectile.SpeedMultiplier = skill.ProjectileSpeedMultiplier;
        int pierce = 0; // 기본 path: 관통 (T1, T3에서 1회씩 추가)
        if (skill.PathTier[0] >= 1) pierce++;
        if (skill.PathTier[0] >= 3) pierce++;
        projectile.PierceRemaining = pierce;

        if (skill.PathTier[2] >= 1) // 독수리투하 연계 path: 명중 시 미니 독수리
        {
            float miniDamage = damage * (0.3f + 0.1f * skill.PathTier[2]);
            float miniScale = skill.Scale * 0.5f;
            projectile.OnHitBonus = hitEnemy => StartCoroutine(MiniEagleBonus(hitEnemy, miniDamage, miniScale, isCrit));
        }

        animator.SetTrigger("Attack");
        return true;
    }

    private void FireWhirlwind(float damage, EquippedSkill skill, bool isCrit = false)
    {
        bool applySlow = skill.PathTier[2] >= 1;
        bool applyVulnerable = skill.PathTier[2] >= 3;

        if (skill.PathTier[2] >= 2) // 오브 연계 path T2: 개큰 회오리로 대체 (여러 개로 안 쪼개짐)
        {
            Vector3 spawnPos = transform.position + Vector3.left * 0.6f + Vector3.down * 0.3f;
            SpawnBigTornado(spawnPos, damage, skill.Scale, applySlow, applyVulnerable, isCrit);
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
                SpawnWhirlwind(spawnPos, perDamage, perScale, applySlow, applyVulnerable, isCrit: isCrit, slowDuration: 3f);
            }
        }

        EquippedSkill lightning = equippedSkills.FirstOrDefault(s => s.Id == ActiveSkillId.Lightning);
        if (lightning != null && lightning.PathTier[2] >= 1) // 낙뢰 연계 path: 회오리 소환 시 낙뢰 지속시간 증가
            LightningStorm.ActiveUntil = Mathf.Max(LightningStorm.ActiveUntil, Time.time) + lightning.PathTier[2] * 1.5f;
    }

    private void SpawnBigTornado(Vector3 position, float damage, float scale, bool applySlow, bool applyVulnerable, bool isCrit = false)
    {
        GameObject prefab = bigTornadoPrefab != null ? bigTornadoPrefab : whirlwindPrefab;
        GameObject obj = Instantiate(prefab, position, Quaternion.identity);
        obj.transform.localScale *= scale;
        Whirlwind whirlwind = obj.GetComponent<Whirlwind>();
        whirlwind.Damage = damage;
        whirlwind.ApplyGemSlow = applySlow;
        whirlwind.ApplyGemVulnerable = applyVulnerable;
        whirlwind.IsCrit = isCrit;
        whirlwind.SlowDuration = 4.5f;
    }

    private void SpawnWhirlwind(Vector3 position, float damage, float scale, bool applySlow, bool applyVulnerable, int maxHitCount = 0, float slowDuration = 3f, bool isCrit = false)
    {
        GameObject obj = Instantiate(whirlwindPrefab, position, Quaternion.identity);
        obj.transform.localScale *= scale;
        Whirlwind whirlwind = obj.GetComponent<Whirlwind>();
        whirlwind.Damage = damage;
        whirlwind.ApplyGemSlow = applySlow;
        whirlwind.ApplyGemVulnerable = applyVulnerable;
        whirlwind.IsCrit = isCrit;
        whirlwind.MaxHitCount = maxHitCount;
        whirlwind.SlowDuration = slowDuration;
    }

    private void FireOrb(float damage, EquippedSkill skill, bool isCrit = false)
    {
        if (skill.PathTier[2] >= 2) // 낙뢰 연계 path T2: 날아가는 오브 대신 맵 중앙에 고정 설치기 소환
        {
            SpawnOrbAltar(MapCenter, damage, skill, isCrit);
            return;
        }

        GameObject obj = Instantiate(orbPrefab, transform.position + Vector3.down * 0.1f, Quaternion.identity);
        obj.transform.localScale *= skill.Scale;
        Orb orb = obj.GetComponent<Orb>();
        orb.Damage = damage;
        orb.IsCrit = isCrit;
        orb.ApplyGemVulnerable = false;

        // 기본 path: 공중 적 추가 피해 (T1, T3에서 25%p씩)
        float flyingBonus = 0f;
        if (skill.PathTier[0] >= 1) flyingBonus += 0.25f;
        if (skill.PathTier[0] >= 3) flyingBonus += 0.25f;
        orb.FlyingDamageMultiplier = 1f + flyingBonus;

        // 지식 연계 path: 슬로우 강화 (T1, T3에서 각각)
        float slowMultBonus = 0f;
        float slowDurBonus = 0f;
        if (skill.PathTier[1] >= 1) { slowMultBonus += 0.1f; slowDurBonus += 0.5f; }
        if (skill.PathTier[1] >= 3) { slowMultBonus += 0.1f; slowDurBonus += 0.5f; }
        orb.SlowMultiplierBonus = slowMultBonus;
        orb.SlowDurationBonus = slowDurBonus;
    }

    private void SpawnOrbAltar(Vector3 position, float damage, EquippedSkill skill, bool isCrit = false)
    {
        if (orbAltarPrefab == null) return;

        float altarDamageMult = skill.PathTier[2] >= 3 ? 1.5f : 1f;

        GameObject obj = Instantiate(orbAltarPrefab, position, Quaternion.identity);
        obj.transform.localScale *= skill.Scale * 0.75f;
        OrbAltar altar = obj.GetComponent<OrbAltar>();
        altar.OrbDamage = damage * 0.4f * altarDamageMult;
        altar.LightningDamage = damage * 0.6f * altarDamageMult;
        altar.ApplyVulnerable = skill.PathTier[2] >= 1;
        altar.IsCrit = isCrit;
    }

    private IEnumerator EagleDropRoutine(float damage, EquippedSkill skill, bool isCrit = false)
    {
        bool spawnMiniWhirlwind = skill.PathTier[2] >= 1; // 회오리 연계 path: 미니 회오리 생성
        float miniWhirlwindDamageMult = 0.3f + (skill.PathTier[2] >= 2 ? 0.1f : 0f); // T1=30%, T2=40%
        int miniWhirlwindMaxHits = skill.PathTier[2] >= 3 ? 15 : 10; // T3에서 최대 타격 횟수 증가

        int overhealPerHit = 0; // 건강 연계 path: 초과체력 획득 (T1, T3에서 2씩)
        if (skill.PathTier[1] >= 1) overhealPerHit += 2;
        if (skill.PathTier[1] >= 3) overhealPerHit += 2;

        int dropCount = 3 + (skill.PathTier[0] >= 3 ? 1 : 0); // 기본 path: T3에서 투하 횟수 +1
        float interval = skill.PathTier[0] >= 1 ? Mathf.Max(0.4f, 1f - 0.15f) : 1f; // 기본 path: T1에서 투하 간격 감소

        for (int i = 0; i < dropCount; i++)
        {
            foreach (Enemy enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                Vector3 pos = enemy.transform.position;
                enemy.TakeDamage(damage, isCrit: isCrit);

                if (overhealPerHit > 0 && health != null) health.AddOverheal(overhealPerHit);
                if (spawnMiniWhirlwind) SpawnWhirlwind(pos, damage * miniWhirlwindDamageMult, skill.Scale * 0.4f, false, false, maxHitCount: miniWhirlwindMaxHits, isCrit: isCrit);

                StartCoroutine(MeteorImpact(pos, skill.Scale));
            }

            yield return new WaitForSeconds(interval);
        }
    }

    private IEnumerator MiniEagleBonus(Enemy target, float damage, float scale, bool isCrit = false)
    {
        if (target == null) yield break;
        Vector3 pos = target.transform.position;
        yield return StartCoroutine(MeteorImpact(pos, scale));
        if (target != null) target.TakeDamage(damage, isCrit: isCrit);
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
