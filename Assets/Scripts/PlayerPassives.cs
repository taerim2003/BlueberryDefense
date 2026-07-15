using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum PassiveSkillId
{
    Strength,
    Health,
    Knowledge,
    Assassinate,
    Refresh,
}

public class EquippedPassive
{
    public PassiveSkillId Id;
    public int Level = 1;

    // 진화 트리: path 0=기본(무의존), 1=패시브 연계, 2=액티브 연계. 각 값은 도달한 티어(0~3).
    public readonly int[] PathTier = new int[3];
    public int TotalEvolutionTier => PathTier[0] + PathTier[1] + PathTier[2];
}

public class PlayerPassives : MonoBehaviour
{
    private const int MaxPassives = 4;
    private const float StrengthDamageBonus = 0.07f;
    private const float KnowledgeXPBonus = 0.08f;
    private const int HealthBonus = 20;

    // 아래 값들은 진화/레벨업으로 계속 갱신되는 정적 상태 — 플레이어가 한 명뿐이라 LightningStorm과 같은 방식으로 관리한다.
    public static float AssassinateCritChance = 0.15f;
    public static float AssassinateCritMultiplier = 3f;
    public static float RefreshChance = 0.1f;
    public static float AssassinateKillXpMultiplier = 1f; // 암살 연계 path1: 치명타 처치 시 경험치 배율
    public static float RefreshHealOnResetAmount = 0f; // 리프레쉬 연계 path1: 쿨타임 초기화시 회복량
    public static float RefreshLightningCooldownProcChance = 0f; // 리프레쉬 연계 path2: 낙뢰 발동시 전체 쿨타임 감소 확률
    public static float HealthDamagePerHp = 0f; // 건강 연계 path1: 최대체력 1당 피해량 배율 보너스
    public static float HealthRetaliationMultiplier = 0f; // 건강 연계 path2: 피격 시 피격 피해량 대비 전체 피해 배율
    public static float BasicAttackDamageMultiplierBonus = 0f; // 힘 연계 path2: 기본공격 전용 추가 피해 배율
    public static float AssassinateWhirlwindCritBonus = 0f; // 암살 연계 path2: 회오리 전용 추가 치명타 확률
    public static bool AssassinateWhirlwindTargetHighest = false; // 암살 연계 path2: 회오리가 최고 체력 적을 타겟팅
    public static int EagleDropCastXpBonus = 0; // 지식 연계 path2: 독수리 투하 시전마다 즉시 획득하는 경험치

    private readonly List<EquippedPassive> equippedPassives = new List<EquippedPassive>();
    private PlayerSkills skills;
    private PlayerHealth health;

    private float regenTimer;
    private float regenAmount;
    private float regenInterval;

    public bool HasMaxPassives => equippedPassives.Count >= MaxPassives;
    public IReadOnlyList<EquippedPassive> EquippedPassives => equippedPassives;

    public bool HasPassive(PassiveSkillId id) => equippedPassives.Any(p => p.Id == id);
    public EquippedPassive GetPassive(PassiveSkillId id) => equippedPassives.FirstOrDefault(p => p.Id == id);

    private void Awake()
    {
        skills = GetComponent<PlayerSkills>();
        health = GetComponent<PlayerHealth>();
        if (health != null) health.OnDamageTaken += HandleDamageTaken;
        LightningStorm.OnProc += HandleLightningProc;
    }

    private void Update()
    {
        if (regenInterval <= 0f || regenAmount <= 0f || health == null) return;

        regenTimer += Time.deltaTime;
        if (regenTimer >= regenInterval)
        {
            regenTimer = 0f;
            health.Heal(Mathf.RoundToInt(regenAmount));
        }
    }

    private void HandleDamageTaken(int amount)
    {
        if (HealthRetaliationMultiplier <= 0f) return;

        float damage = amount * HealthRetaliationMultiplier;
        foreach (Enemy enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            enemy.TakeDamage(damage);
    }

    private void HandleLightningProc()
    {
        if (RefreshLightningCooldownProcChance <= 0f || skills == null) return;
        if (Random.value < RefreshLightningCooldownProcChance)
            skills.ReduceAllCooldowns(1f);
    }

    // 타격 기준 치명타: 각 데미지 이벤트(투사체 명중, 회오리/오브 틱 등)마다 개별적으로 굴린다.
    public static float ApplyCrit(float damage, float critChance, out bool isCrit)
    {
        isCrit = critChance > 0f && Random.value < critChance;
        return isCrit ? damage * AssassinateCritMultiplier : damage;
    }

    public void AcquirePassive(PassiveSkillId id)
    {
        if (HasMaxPassives || HasPassive(id)) return;

        equippedPassives.Add(new EquippedPassive { Id = id });

        switch (id)
        {
            case PassiveSkillId.Strength:
                skills.IncreaseDamageMultiplier(StrengthDamageBonus);
                break;
            case PassiveSkillId.Health:
                health.IncreaseMaxHealth(HealthBonus);
                break;
            case PassiveSkillId.Knowledge:
                PlayerExperience.Instance.IncreaseXPMultiplier(KnowledgeXPBonus);
                break;
        }
    }

    public bool CanUpgradePassive(EquippedPassive passive) => passive.TotalEvolutionTier >= passive.Level / 5;

    public void UpgradePassiveLevel(PassiveSkillId id)
    {
        EquippedPassive passive = GetPassive(id);
        if (passive == null || !CanUpgradePassive(passive)) return;

        passive.Level++;
        ApplyPassiveLevelEffect(passive);
    }

    private void ApplyPassiveLevelEffect(EquippedPassive passive)
    {
        switch (passive.Id)
        {
            case PassiveSkillId.Strength:
                skills.IncreaseDamageMultiplier(StrengthDamageBonus);
                break;
            case PassiveSkillId.Health:
                health.IncreaseMaxHealth(HealthBonus);
                break;
            case PassiveSkillId.Knowledge:
                PlayerExperience.Instance.IncreaseXPMultiplier(KnowledgeXPBonus);
                break;
            case PassiveSkillId.Assassinate:
                AssassinateCritChance += 0.04f;
                break;
            case PassiveSkillId.Refresh:
                RefreshChance += 0.02f;
                break;
        }
    }

    public static string DescribePassiveLevelEffect(PassiveSkillId id) => id switch
    {
        PassiveSkillId.Strength => "피해량 7% 증가",
        PassiveSkillId.Health => "최대 체력 20 증가",
        PassiveSkillId.Knowledge => "경험치 획득량 8% 증가",
        PassiveSkillId.Assassinate => "치명타 확률 4%p 증가",
        PassiveSkillId.Refresh => "재사용 초기화 확률 2%p 증가",
        _ => "",
    };

    // path: 0=기본(무의존), 1=패시브 연계, 2=액티브 연계
    public bool CanEvolvePath(EquippedPassive passive, int path)
    {
        int tier = passive.PathTier[path];
        if (tier >= 3) return false;

        if (tier == 0)
        {
            int investedPaths = CountInvestedPaths(passive);
            return investedPaths < 2;
        }

        if (tier == 1)
        {
            int advancingPath = GetAdvancingPath(passive);
            if (advancingPath != -1 && advancingPath != path) return false;
            return HasPathPrereq(passive.Id, path);
        }

        return true;
    }

    public bool CanEvolveAnyPath(EquippedPassive passive) =>
        CanEvolvePath(passive, 0) || CanEvolvePath(passive, 1) || CanEvolvePath(passive, 2);

    public void EvolvePassive(PassiveSkillId id, int path)
    {
        EquippedPassive passive = GetPassive(id);
        if (passive == null || !CanEvolvePath(passive, path)) return;

        passive.PathTier[path]++;
        ApplyPassivePathTierEffect(passive, path, passive.PathTier[path]);
    }

    private void ApplyPassivePathTierEffect(EquippedPassive passive, int path, int newTier)
    {
        switch (passive.Id, path, newTier)
        {
            // 힘
            case (PassiveSkillId.Strength, 0, 1): skills.IncreaseDamageMultiplier(0.08f); break;
            case (PassiveSkillId.Strength, 0, 2): skills.IncreaseDamageMultiplier(0.15f); break;
            case (PassiveSkillId.Strength, 0, 3): skills.IncreaseDamageMultiplier(0.22f); break;
            case (PassiveSkillId.Strength, 1, 1): AssassinateCritMultiplier += 0.15f; break;
            case (PassiveSkillId.Strength, 1, 2): AssassinateCritMultiplier += 0.35f; break;
            case (PassiveSkillId.Strength, 1, 3): AssassinateCritMultiplier += 0.5f; break;
            case (PassiveSkillId.Strength, 2, 1): BasicAttackDamageMultiplierBonus += 0.10f; break;
            case (PassiveSkillId.Strength, 2, 2): BasicAttackDamageMultiplierBonus += 0.175f; break;
            case (PassiveSkillId.Strength, 2, 3): BasicAttackDamageMultiplierBonus += 0.25f; break;

            // 건강
            case (PassiveSkillId.Health, 0, 1): regenAmount = 2f; regenInterval = 5f; break;
            case (PassiveSkillId.Health, 0, 2): regenAmount = 5f; regenInterval = 5f; break;
            case (PassiveSkillId.Health, 0, 3): regenAmount = 8f; regenInterval = 3f; break;
            case (PassiveSkillId.Health, 1, 1): HealthDamagePerHp += 0.0005f; break;
            case (PassiveSkillId.Health, 1, 2): HealthDamagePerHp += 0.0005f; break;
            case (PassiveSkillId.Health, 1, 3): HealthDamagePerHp += 0.0005f; break;
            case (PassiveSkillId.Health, 2, 1): HealthRetaliationMultiplier += 0.5f; break;
            case (PassiveSkillId.Health, 2, 2): HealthRetaliationMultiplier += 0.5f; break;
            case (PassiveSkillId.Health, 2, 3): HealthRetaliationMultiplier += 0.5f; break;

            // 지식
            case (PassiveSkillId.Knowledge, 0, 1): PlayerExperience.Instance.IncreaseXPMultiplier(0.08f); break;
            case (PassiveSkillId.Knowledge, 0, 2): PlayerExperience.Instance.IncreaseXPMultiplier(0.15f); break;
            case (PassiveSkillId.Knowledge, 0, 3): PlayerExperience.Instance.IncreaseXPMultiplier(0.22f); break;
            case (PassiveSkillId.Knowledge, 1, 1): EnemySpawner.ExtraTreasureChance += 0.005f; break;
            case (PassiveSkillId.Knowledge, 1, 2): EnemySpawner.ExtraTreasureChance += 0.005f; break;
            case (PassiveSkillId.Knowledge, 1, 3): EnemySpawner.ExtraTreasureChance += 0.005f; break;
            case (PassiveSkillId.Knowledge, 2, 1): EagleDropCastXpBonus += 3; break;
            case (PassiveSkillId.Knowledge, 2, 2): EagleDropCastXpBonus += 3; break;
            case (PassiveSkillId.Knowledge, 2, 3): EagleDropCastXpBonus += 4; break;

            // 암살
            case (PassiveSkillId.Assassinate, 0, 1): AssassinateCritChance += 0.08f; break;
            case (PassiveSkillId.Assassinate, 0, 2): AssassinateCritChance += 0.15f; break;
            case (PassiveSkillId.Assassinate, 0, 3): AssassinateCritChance += 0.22f; break;
            case (PassiveSkillId.Assassinate, 1, 1): AssassinateKillXpMultiplier += 0.25f; break;
            case (PassiveSkillId.Assassinate, 1, 2): AssassinateKillXpMultiplier += 0.75f; break;
            case (PassiveSkillId.Assassinate, 1, 3): AssassinateKillXpMultiplier += 0.5f; break;
            case (PassiveSkillId.Assassinate, 2, 1): AssassinateWhirlwindTargetHighest = true; AssassinateWhirlwindCritBonus += 0.1f; break;
            case (PassiveSkillId.Assassinate, 2, 2): AssassinateWhirlwindCritBonus += 0.1f; break;
            case (PassiveSkillId.Assassinate, 2, 3): AssassinateWhirlwindCritBonus += 0.1f; break;

            // 리프레쉬
            case (PassiveSkillId.Refresh, 0, 1): RefreshChance += 0.03f; break;
            case (PassiveSkillId.Refresh, 0, 2): RefreshChance += 0.02f; break;
            case (PassiveSkillId.Refresh, 0, 3): RefreshChance += 0.03f; break;
            case (PassiveSkillId.Refresh, 1, 1): RefreshHealOnResetAmount += 2f; break;
            case (PassiveSkillId.Refresh, 1, 2): RefreshHealOnResetAmount += 2f; break;
            case (PassiveSkillId.Refresh, 1, 3): RefreshHealOnResetAmount += 2f; break;
            case (PassiveSkillId.Refresh, 2, 1): RefreshLightningCooldownProcChance += 0.03f; break;
            case (PassiveSkillId.Refresh, 2, 2): RefreshLightningCooldownProcChance += 0.02f; break;
            case (PassiveSkillId.Refresh, 2, 3): RefreshLightningCooldownProcChance += 0.03f; break;
        }
    }

    private static int CountInvestedPaths(EquippedPassive passive) =>
        (passive.PathTier[0] > 0 ? 1 : 0) + (passive.PathTier[1] > 0 ? 1 : 0) + (passive.PathTier[2] > 0 ? 1 : 0);

    private static int GetAdvancingPath(EquippedPassive passive)
    {
        for (int i = 0; i < 3; i++)
            if (passive.PathTier[i] >= 2) return i;
        return -1;
    }

    // path1/path2 진화는 연계 대상(패시브/액티브)을 보유하는 것만으로는 부족하고, Lv.5 이상이어야 함
    private bool HasPathPrereq(PassiveSkillId id, int path)
    {
        if (path == 1)
        {
            PassiveSkillId? req = GetPassivePrereq(id);
            if (!req.HasValue) return false;
            EquippedPassive p = GetPassive(req.Value);
            return p != null && p.Level >= 5;
        }
        if (path == 2)
        {
            ActiveSkillId? req = GetActivePrereq(id);
            if (!req.HasValue || skills == null) return false;
            EquippedSkill s = skills.EquippedSkills.FirstOrDefault(x => x.Id == req.Value);
            return s != null && s.Level >= 5;
        }
        return true;
    }

    public static PassiveSkillId? GetPassivePrereq(PassiveSkillId id) => id switch
    {
        PassiveSkillId.Strength => PassiveSkillId.Assassinate,
        PassiveSkillId.Health => PassiveSkillId.Strength,
        PassiveSkillId.Knowledge => PassiveSkillId.Refresh,
        PassiveSkillId.Assassinate => PassiveSkillId.Knowledge,
        PassiveSkillId.Refresh => PassiveSkillId.Health,
        _ => null,
    };

    public static ActiveSkillId? GetActivePrereq(PassiveSkillId id) => id switch
    {
        PassiveSkillId.Strength => ActiveSkillId.BasicAttack,
        PassiveSkillId.Health => ActiveSkillId.Orb,
        PassiveSkillId.Knowledge => ActiveSkillId.EagleDrop,
        PassiveSkillId.Assassinate => ActiveSkillId.Whirlwind,
        PassiveSkillId.Refresh => ActiveSkillId.Lightning,
        _ => null,
    };

    // 한 티어 = 한 효과. (id, path, tier)별로 실제로 적용되는 수치만 그대로 표기한다.
    public static string DescribePathEffect(PassiveSkillId id, int path, int tier) => (id, path, tier) switch
    {
        (PassiveSkillId.Strength, 0, 1) => "피해량 8% 추가 증가",
        (PassiveSkillId.Strength, 0, 2) => "피해량 15% 추가 증가",
        (PassiveSkillId.Strength, 0, 3) => "피해량 22% 추가 증가",
        (PassiveSkillId.Strength, 1, 1) => "치명타 피해 배율 +0.15",
        (PassiveSkillId.Strength, 1, 2) => "치명타 피해 배율 +0.35",
        (PassiveSkillId.Strength, 1, 3) => "치명타 피해 배율 +0.5",
        (PassiveSkillId.Strength, 2, 1) => "기본 공격 피해량 10% 증가",
        (PassiveSkillId.Strength, 2, 2) => "기본 공격 피해량 18% 증가",
        (PassiveSkillId.Strength, 2, 3) => "기본 공격 피해량 25% 증가",

        (PassiveSkillId.Health, 0, 1) => "5초마다 체력 2 재생",
        (PassiveSkillId.Health, 0, 2) => "5초마다 체력 5 재생",
        (PassiveSkillId.Health, 0, 3) => "3초마다 체력 8 재생",
        (PassiveSkillId.Health, 1, 1) => "최대 체력 비례 피해량 증가 (체력 1당 0.05%)",
        (PassiveSkillId.Health, 1, 2) => "최대 체력 비례 피해량 추가 증가 (총 체력 1당 0.1%)",
        (PassiveSkillId.Health, 1, 3) => "최대 체력 비례 피해량 추가 증가 (총 체력 1당 0.15%)",
        (PassiveSkillId.Health, 2, 1) => "피격 시 받은 피해의 50%만큼 전체 적에게 피해",
        (PassiveSkillId.Health, 2, 2) => "피격 시 받은 피해의 100%만큼 전체 적에게 피해",
        (PassiveSkillId.Health, 2, 3) => "피격 시 받은 피해의 150%만큼 전체 적에게 피해",

        (PassiveSkillId.Knowledge, 0, 1) => "경험치 획득량 8% 추가 증가",
        (PassiveSkillId.Knowledge, 0, 2) => "경험치 획득량 15% 추가 증가",
        (PassiveSkillId.Knowledge, 0, 3) => "경험치 획득량 22% 추가 증가",
        (PassiveSkillId.Knowledge, 1, 1) => "블루베리 등장 시 0.5% 확률로 보물상자 블루베리로 변신",
        (PassiveSkillId.Knowledge, 1, 2) => "변신 확률 1%로 증가",
        (PassiveSkillId.Knowledge, 1, 3) => "변신 확률 1.5%로 증가",
        (PassiveSkillId.Knowledge, 2, 1) => "독수리 투하 시전 시 경험치 3 즉시 획득",
        (PassiveSkillId.Knowledge, 2, 2) => "독수리 투하 시전 시 경험치 6 즉시 획득",
        (PassiveSkillId.Knowledge, 2, 3) => "독수리 투하 시전 시 경험치 10 즉시 획득",

        (PassiveSkillId.Assassinate, 0, 1) => "치명타 확률 8%p 증가",
        (PassiveSkillId.Assassinate, 0, 2) => "치명타 확률 15%p 증가",
        (PassiveSkillId.Assassinate, 0, 3) => "치명타 확률 22%p 증가",
        (PassiveSkillId.Assassinate, 1, 1) => "치명타로 처치한 적 경험치 1.25배",
        (PassiveSkillId.Assassinate, 1, 2) => "치명타로 처치한 적 경험치 2배",
        (PassiveSkillId.Assassinate, 1, 3) => "치명타로 처치한 적 경험치 2.5배",
        (PassiveSkillId.Assassinate, 2, 1) => "회오리가 가장 체력이 높은 적을 타겟팅, 회오리 치명타 확률 10%p 증가",
        (PassiveSkillId.Assassinate, 2, 2) => "회오리 치명타 확률 20%p 증가",
        (PassiveSkillId.Assassinate, 2, 3) => "회오리 치명타 확률 30%p 증가",

        (PassiveSkillId.Refresh, 0, 1) => "재사용 초기화 확률 3%p 증가",
        (PassiveSkillId.Refresh, 0, 2) => "재사용 초기화 확률 5%p 증가",
        (PassiveSkillId.Refresh, 0, 3) => "재사용 초기화 확률 8%p 증가",
        (PassiveSkillId.Refresh, 1, 1) => "쿨타임 초기화 시마다 체력(또는 초과체력) 2 회복",
        (PassiveSkillId.Refresh, 1, 2) => "회복량 4로 증가",
        (PassiveSkillId.Refresh, 1, 3) => "회복량 6으로 증가",
        (PassiveSkillId.Refresh, 2, 1) => "낙뢰 발동 시 3% 확률로 모든 스킬 쿨타임 -1초",
        (PassiveSkillId.Refresh, 2, 2) => "발동 확률 5%로 증가",
        (PassiveSkillId.Refresh, 2, 3) => "발동 확률 8%로 증가",

        _ => "",
    };

    // 진화 카드에 붙는 짧은 제목
    public static string GetPathTierTitle(PassiveSkillId id, int path, int tier) => (id, path, tier) switch
    {
        (PassiveSkillId.Strength, 0, 1) => "피해량 강화",
        (PassiveSkillId.Strength, 0, 2) => "피해량 강화 II",
        (PassiveSkillId.Strength, 0, 3) => "피해량 강화 III",
        (PassiveSkillId.Strength, 1, 1) => "치명타 피해 강화",
        (PassiveSkillId.Strength, 1, 2) => "치명타 피해 강화 II",
        (PassiveSkillId.Strength, 1, 3) => "치명타 피해 강화 III",
        (PassiveSkillId.Strength, 2, 1) => "기본공격 피해 강화",
        (PassiveSkillId.Strength, 2, 2) => "기본공격 피해 강화 II",
        (PassiveSkillId.Strength, 2, 3) => "기본공격 피해 강화 III",

        (PassiveSkillId.Health, 0, 1) => "체력 재생",
        (PassiveSkillId.Health, 0, 2) => "체력 재생 강화",
        (PassiveSkillId.Health, 0, 3) => "체력 재생 강화 II",
        (PassiveSkillId.Health, 1, 1) => "체력 비례 피해",
        (PassiveSkillId.Health, 1, 2) => "체력 비례 피해 강화",
        (PassiveSkillId.Health, 1, 3) => "체력 비례 피해 강화 II",
        (PassiveSkillId.Health, 2, 1) => "피격 반격",
        (PassiveSkillId.Health, 2, 2) => "피격 반격 강화",
        (PassiveSkillId.Health, 2, 3) => "피격 반격 강화 II",

        (PassiveSkillId.Knowledge, 0, 1) => "경험치 강화",
        (PassiveSkillId.Knowledge, 0, 2) => "경험치 강화 II",
        (PassiveSkillId.Knowledge, 0, 3) => "경험치 강화 III",
        (PassiveSkillId.Knowledge, 1, 1) => "보물 변신",
        (PassiveSkillId.Knowledge, 1, 2) => "보물 변신 강화",
        (PassiveSkillId.Knowledge, 1, 3) => "보물 변신 강화 II",
        (PassiveSkillId.Knowledge, 2, 1) => "독수리 경험치",
        (PassiveSkillId.Knowledge, 2, 2) => "독수리 경험치 강화",
        (PassiveSkillId.Knowledge, 2, 3) => "독수리 경험치 강화 II",

        (PassiveSkillId.Assassinate, 0, 1) => "치명타 확률 강화",
        (PassiveSkillId.Assassinate, 0, 2) => "치명타 확률 강화 II",
        (PassiveSkillId.Assassinate, 0, 3) => "치명타 확률 강화 III",
        (PassiveSkillId.Assassinate, 1, 1) => "암살 경험치",
        (PassiveSkillId.Assassinate, 1, 2) => "암살 경험치 강화",
        (PassiveSkillId.Assassinate, 1, 3) => "암살 경험치 강화 II",
        (PassiveSkillId.Assassinate, 2, 1) => "회오리 저격",
        (PassiveSkillId.Assassinate, 2, 2) => "회오리 저격 강화",
        (PassiveSkillId.Assassinate, 2, 3) => "회오리 저격 강화 II",

        (PassiveSkillId.Refresh, 0, 1) => "초기화 확률 강화",
        (PassiveSkillId.Refresh, 0, 2) => "초기화 확률 강화 II",
        (PassiveSkillId.Refresh, 0, 3) => "초기화 확률 강화 III",
        (PassiveSkillId.Refresh, 1, 1) => "초기화 회복",
        (PassiveSkillId.Refresh, 1, 2) => "초기화 회복 강화",
        (PassiveSkillId.Refresh, 1, 3) => "초기화 회복 강화 II",
        (PassiveSkillId.Refresh, 2, 1) => "낙뢰 쿨타임 감소",
        (PassiveSkillId.Refresh, 2, 2) => "낙뢰 쿨타임 감소 강화",
        (PassiveSkillId.Refresh, 2, 3) => "낙뢰 쿨타임 감소 강화 II",

        _ => "",
    };
}
