using UnityEngine;

// 게임 씬에 하나 배치. 판 시작 시 메타 저장값을 읽어
// 인게임 스탯(플레이어 컴포넌트)과 전투 런타임 보너스(MetaBonuses)에 반영한다.
public class MetaRunApplier : MonoBehaviour
{
    private void Awake()
    {
        // 전투 static이 이전 판/도메인 리로드 잔여값을 쓰지 않도록 먼저 초기화한 뒤 세팅.
        MetaBonuses.Reset();
        MetaRun.Reset();
        ApplyRuntimeBonuses();
    }

    private void Start()
    {
        // 플레이어 컴포넌트 Awake가 모두 끝난 뒤(Start 시점) 스탯을 얹는다.
        ApplyPlayerStats();
    }

    private void ApplyRuntimeBonuses()
    {
        MetaBonuses.CooldownMult = 1f - 0.01f * Total(MetaUpgradeId.Cooldown);
        MetaBonuses.DurationMult = 1f + 0.01f * Total(MetaUpgradeId.Duration);
        MetaBonuses.CritBonus = 0.01f * Total(MetaUpgradeId.Crit);
        MetaBonuses.CurrencyMult = 1f + 0.01f * Total(MetaUpgradeId.Wealth);
        MetaBonuses.RegenPer5s = Mathf.RoundToInt(Total(MetaUpgradeId.Regen));
    }

    private void ApplyPlayerStats()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        PlayerExperience exp = PlayerExperience.Instance;

        float atk = Total(MetaUpgradeId.Attack);
        if (skills != null && atk > 0f) skills.IncreaseDamageMultiplier(0.01f * atk);

        float hp = Total(MetaUpgradeId.Health);
        if (health != null && hp > 0f) health.IncreaseMaxHealth(Mathf.RoundToInt(hp));

        float xp = Total(MetaUpgradeId.Xp);
        if (exp != null && xp > 0f) exp.IncreaseXPMultiplier(0.01f * xp);
    }

    // 해당 업그레이드의 현재 누적 수치(PerLevel × 저장된 레벨)
    private static float Total(MetaUpgradeId id) => MetaUpgrades.Get(id).TotalAt(MetaSave.GetLevel(id));
}
