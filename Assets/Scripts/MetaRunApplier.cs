using UnityEngine;

// 게임 씬에 하나 배치. 판 시작 시 스킬트리 해금집합을 읽어
// 인게임 스탯(플레이어 컴포넌트)과 전투 런타임 보너스(MetaBonuses)에 반영한다.
public class MetaRunApplier : MonoBehaviour
{
    // 스킬 해금 게이팅에 필요한 트리 에셋(효과 반영은 id 레지스트리라 트리 없이도 되지만,
    // 어떤 스킬을 해금 노드가 열어주는지는 트리 데이터가 있어야 안다). MainSkillTree.asset을 배선.
    [SerializeField] private SkillTreeData tree;

    // 승천 등급별 정수 획득 배율표. 미할당이면 코드 기본표 폴백(EnemySpawner와 같은 방식).
    [SerializeField] private AscensionTable ascension;

    private SkillEffects.Totals totals;

    private void Awake()
    {
        // 전투 static이 이전 판/도메인 리로드 잔여값을 쓰지 않도록 먼저 초기화한 뒤 세팅.
        MetaBonuses.Reset();
        MetaRun.Reset();
        // 🔴 일반(Normal) 노드의 효과 축은 **에셋**이 들고 있다 — tree 없이는 스탯 노드가 통째로 무효다.
        //    (예전엔 노드 id 스위치라 tree 없이도 됐다. 재설계로 축이 에셋으로 넘어가면서 필수가 됐다.)
        if (tree == null) Debug.LogWarning("[MetaRunApplier] 스킬트리 에셋 미배선 — 일반 노드 효과가 전부 무효가 됩니다.");
        totals = SkillEffects.Compute(tree);
        ApplyRuntimeBonuses();
        ApplySkillGating();
    }

    // 스킬트리의 해금 노드 → 인게임 카드 풀 게이팅 집합 세팅(트리 미배선이면 게이팅 없음 = 현행).
    private void ApplySkillGating()
    {
        if (tree == null) return;
        foreach (var s in SkillTreeSave.GatedSkills(tree)) MetaBonuses.GatedSkills.Add(s);
        foreach (var s in SkillTreeSave.UnlockedSkills(tree)) MetaBonuses.TreeUnlockedSkills.Add(s);
    }

    private void Start()
    {
        // 플레이어 컴포넌트 Awake가 모두 끝난 뒤(Start 시점) 스탯을 얹는다.
        ApplyPlayerStats();
    }

    private void ApplyRuntimeBonuses()
    {
        MetaBonuses.CooldownMult = 1f - 0.01f * totals.CdReducePct;
        MetaBonuses.DurationMult = 1f + 0.01f * totals.DurationPct;
        MetaBonuses.RegenPer5s = totals.RegenPer5s;
        MetaBonuses.CritBonus = 0.01f * totals.CritPct;
        MetaBonuses.CritDamageBonus = 0.01f * totals.CritDmgPct;
        MetaBonuses.BossDamageBonus = 0.01f * totals.BossDmgPct;
        // 정수 획득량 = 스킬트리(부유) 보너스 × 승천 등급 보상 배율
        AscensionTable ascTable = ascension != null ? ascension : AscensionTable.Default;
        MetaBonuses.CurrencyMult = (1f + 0.01f * totals.CurrencyPct) * ascTable.Get(RunConfig.AscensionLevel).essenceMult;
        MetaBonuses.FlyDamageBonus = 0.01f * totals.FlyDmgPct;
        MetaBonuses.EagleFlyDamageBonus = 0.01f * totals.EagleFlyDmgPct;
        MetaBonuses.WhirlwindFlyDamageBonus = 0.01f * totals.WhirlwindFlyDmgPct;
        MetaBonuses.HealDropChanceBonus = 0.01f * totals.HealDropPct;
        MetaBonuses.OrbCanHitFlying = totals.OrbFly;
        MetaBonuses.HomingMissileGrowth = totals.HomingGrowth;
        MetaBonuses.WhirlwindCooldownBonus = totals.WhirlwindCdBonus;
        MetaBonuses.AccelCooldownBonus = 0.01f * totals.AccelCdPct;
        MetaBonuses.ThunderCooldownPerStrike = totals.ThunderCdPerStrike;
        MetaBonuses.SnipingExtraTarget = totals.SnipingExtraTarget;
        MetaBonuses.RewindSlowAll = totals.RewindSlow;
        MetaBonuses.ShotgunCloseBonus = totals.ShotgunClose;
        MetaBonuses.RerollCount = totals.RerollCount;
        if (totals.ArrowStartLevel > 1) MetaBonuses.ArrowStartLevel = totals.ArrowStartLevel;
        if (totals.SwingStartLevel > 1) MetaBonuses.SwingStartLevel = totals.SwingStartLevel;

        // ── 스킬 강화 노드(2026-09-03 재설계) ──
        MetaBonuses.ArrowExtraPierce = totals.ArrowPierce;
        MetaBonuses.SwingKnockbackMult = totals.SwingKnockbackMult;
        MetaBonuses.OrbSlowBoost = totals.OrbSlowBoost;
        MetaBonuses.OrbExtraTargets = totals.OrbTargets;
        MetaBonuses.EagleExtraDrops = totals.EagleDrops;
        MetaBonuses.ThunderStackable = totals.ThunderStack;
        MetaBonuses.ShotgunExtraBonusHit = totals.ShotgunBonusHit;
        MetaBonuses.ShotgunCritBonus = 0.01f * totals.ShotgunCritPct;
        MetaBonuses.SnipingCritBonus = 0.01f * totals.SnipingCritPct;
        MetaBonuses.HomingCooldownCut = totals.HomingCdCut;
        MetaBonuses.RewindSkipsGlobalCooldown = totals.RewindNoGcd;

        MetaBonuses.PassiveBaseStrength = totals.PassiveStrength;
        MetaBonuses.PassiveBaseHealth = totals.PassiveHealth;
        MetaBonuses.PassiveBaseKnowledge = totals.PassiveKnowledge;
        MetaBonuses.PassiveBaseAssassinate = totals.PassiveAssassinate;
        MetaBonuses.PassiveBaseDefense = totals.PassiveDefense;
        MetaBonuses.PassiveBaseAccel = totals.PassiveAccel;

        MetaBonuses.HealItemDouble = totals.HealItemDouble;
        MetaBonuses.StrengthSlowSkillDouble = totals.StrengthSlowSkillDouble;
        MetaBonuses.AssassinFullCritExtraHit = totals.AssassinFullCritExtraHit;
        MetaBonuses.DefenseRevive = totals.DefenseRevive;
        MetaBonuses.AccelFastSkillDamage = totals.AccelFastSkillDamage;
        MetaBonuses.ShowEvolutionHint = totals.ShowEvolutionHint;

        MetaBonuses.EvolutionUnlocked = totals.EvolutionUnlocked;
        MetaBonuses.Evolution2Unlocked = totals.Evolution2Unlocked;
    }

    private void ApplyPlayerStats()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        PlayerExperience exp = PlayerExperience.Instance;

        if (skills != null && totals.DamagePct > 0f) skills.IncreaseDamageMultiplier(0.01f * totals.DamagePct);
        if (health != null && totals.HpAdd > 0) health.IncreaseMaxHealth(totals.HpAdd);
        if (exp != null && totals.XpPct > 0f) exp.IncreaseXPMultiplier(0.01f * totals.XpPct);

        // 기본공격 시작 레벨 강화 — 스킬 Awake 완료 후(Start) 적용.
        // 캐릭터마다 기본공격이 달라서 노드도 둘이다(딸기=화살 / 파인애플=휘두르기).
        // 안 쓰는 쪽은 SetSkillStartLevel이 그 스킬을 못 찾아 조용히 넘어간다.
        if (skills != null && MetaBonuses.ArrowStartLevel > 1)
            skills.SetSkillStartLevel(ActiveSkillId.BasicAttack, MetaBonuses.ArrowStartLevel);
        if (skills != null && MetaBonuses.SwingStartLevel > 1)
            skills.SetSkillStartLevel(ActiveSkillId.Swing, MetaBonuses.SwingStartLevel);

        // 레벨업 리롤 횟수(게임당) 초기화 — LevelUpUI.Awake(Instance 세팅) 이후
        if (LevelUpUI.Instance != null) LevelUpUI.Instance.InitRerolls(MetaBonuses.RerollCount);
    }
}
