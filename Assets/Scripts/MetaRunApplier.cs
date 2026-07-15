using UnityEngine;

// 게임 씬에 하나 배치. 판 시작 시 스킬트리 해금집합을 읽어
// 인게임 스탯(플레이어 컴포넌트)과 전투 런타임 보너스(MetaBonuses)에 반영한다.
public class MetaRunApplier : MonoBehaviour
{
    private SkillEffects.Totals totals;

    private void Awake()
    {
        // 전투 static이 이전 판/도메인 리로드 잔여값을 쓰지 않도록 먼저 초기화한 뒤 세팅.
        MetaBonuses.Reset();
        MetaRun.Reset();
        totals = SkillEffects.Compute();
        ApplyRuntimeBonuses();
    }

    private void Start()
    {
        // 플레이어 컴포넌트 Awake가 모두 끝난 뒤(Start 시점) 스탯을 얹는다.
        ApplyPlayerStats();
    }

    private void ApplyRuntimeBonuses()
    {
        MetaBonuses.CooldownMult = 1f - 0.01f * totals.CdReducePct;
        MetaBonuses.CritBonus = 0.01f * totals.CritPct;
        MetaBonuses.CurrencyMult = 1f + 0.01f * totals.CurrencyPct;
        MetaBonuses.FlyDamageBonus = 0.01f * totals.FlyDmgPct;
        MetaBonuses.EagleFlyDamageBonus = 0.01f * totals.EagleFlyDmgPct;
        MetaBonuses.HealDropChanceBonus = 0.01f * totals.HealDropPct;
        MetaBonuses.OrbCanHitFlying = totals.OrbFly;
        MetaBonuses.WhirlwindCooldownBonus = totals.WhirlwindCdBonus;
        MetaBonuses.RefreshChanceBonus = 0.01f * totals.RefreshPct;
        MetaBonuses.ThunderCooldownPerStrike = totals.ThunderCdPerStrike;
        MetaBonuses.RerollCount = totals.RerollCount;
        if (totals.ArrowStartLevel > 1) MetaBonuses.ArrowStartLevel = totals.ArrowStartLevel;
        // Duration/Regen은 현재 트리에 대응 노드 없음 → Reset 기본값 유지.
    }

    private void ApplyPlayerStats()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        PlayerExperience exp = PlayerExperience.Instance;

        if (skills != null && totals.DamagePct > 0f) skills.IncreaseDamageMultiplier(0.01f * totals.DamagePct);
        if (health != null && totals.HpAdd > 0) health.IncreaseMaxHealth(totals.HpAdd);
        if (exp != null && totals.XpPct > 0f) exp.IncreaseXPMultiplier(0.01f * totals.XpPct);

        // 화살(기본공격) 시작 레벨 강화 — 스킬 Awake 완료 후(Start) 적용
        if (skills != null && MetaBonuses.ArrowStartLevel > 1)
            skills.SetSkillStartLevel(ActiveSkillId.BasicAttack, MetaBonuses.ArrowStartLevel);

        // 레벨업 리롤 횟수(게임당) 초기화 — LevelUpUI.Awake(Instance 세팅) 이후
        if (LevelUpUI.Instance != null) LevelUpUI.Instance.InitRerolls(MetaBonuses.RerollCount);
    }
}
