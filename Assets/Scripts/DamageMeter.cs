using System.Collections.Generic;
using System.Linq;

// 게임오버 시 보여줄 딜미터기 — 스킬별 누적 피해량을 집계한다. 플레이어가 한 명뿐이라 LightningStorm과 같은 방식(정적 상태)으로 관리한다.
public static class DamageMeter
{
    private static readonly Dictionary<ActiveSkillId, float> damageBySkill = new Dictionary<ActiveSkillId, float>();
    private static float otherDamage; // 스킬에 속하지 않는 피해(건강 연계 반격 등)

    public static void Reset()
    {
        damageBySkill.Clear();
        otherDamage = 0f;
    }

    public static void Record(ActiveSkillId? source, float amount)
    {
        if (amount <= 0f) return;

        if (!source.HasValue)
        {
            otherDamage += amount;
            return;
        }

        damageBySkill.TryGetValue(source.Value, out float existing);
        damageBySkill[source.Value] = existing + amount;
    }

    public static float TotalDamage => damageBySkill.Values.Sum() + otherDamage;

    // 피해량 내림차순으로 정렬된 (이름, 피해량) 목록. "기타" 항목은 값이 있을 때만 마지막에 포함.
    public static List<(string Name, float Damage)> GetBreakdown()
    {
        List<(string Name, float Damage)> result = damageBySkill
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (PlayerSkills.GetActiveSkillName(kv.Key), kv.Value))
            .ToList();

        if (otherDamage > 0f) result.Add((Loc.T("ui.result.other"), otherDamage));
        return result;
    }
}
