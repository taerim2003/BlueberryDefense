using System.Collections.Generic;
using UnityEngine;

public enum PassiveSkillId
{
    Strength,
    Health,
    Knowledge,
    Assassinate,
    Refresh,
}

public class PlayerPassives : MonoBehaviour
{
    private const int MaxPassives = 4;
    private const float StrengthDamageBonus = 0.1f;
    private const float KnowledgeXPBonus = 0.1f;
    private const int HealthBonus = 20;

    public const float AssassinateCritChance = 0.15f;
    public const float AssassinateCritMultiplier = 3f;
    public const float RefreshChance = 0.05f;

    private readonly List<PassiveSkillId> acquiredPassives = new List<PassiveSkillId>();

    public bool HasMaxPassives => acquiredPassives.Count >= MaxPassives;
    public IReadOnlyList<PassiveSkillId> AcquiredPassives => acquiredPassives;

    public bool HasPassive(PassiveSkillId id) => acquiredPassives.Contains(id);

    // 타격 기준 치명타: 각 데미지 이벤트(투사체 명중, 회오리/오브 틱 등)마다 개별적으로 굴린다.
    public static float ApplyCrit(float damage, float critChance, out bool isCrit)
    {
        isCrit = critChance > 0f && Random.value < critChance;
        return isCrit ? damage * AssassinateCritMultiplier : damage;
    }

    public void AcquirePassive(PassiveSkillId id)
    {
        if (HasMaxPassives || HasPassive(id)) return;

        acquiredPassives.Add(id);

        switch (id)
        {
            case PassiveSkillId.Strength:
                GetComponent<PlayerSkills>().IncreaseDamageMultiplier(StrengthDamageBonus);
                break;
            case PassiveSkillId.Health:
                GetComponent<PlayerHealth>().IncreaseMaxHealth(HealthBonus);
                break;
            case PassiveSkillId.Knowledge:
                GetComponent<PlayerExperience>().IncreaseXPMultiplier(KnowledgeXPBonus);
                break;
        }
    }
}
