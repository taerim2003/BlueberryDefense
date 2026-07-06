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

    public const float AssassinateCritChance = 0.04f;
    public const float AssassinateCritMultiplier = 3f;
    public const float RefreshChance = 0.05f;

    private readonly List<PassiveSkillId> acquiredPassives = new List<PassiveSkillId>();

    public bool HasMaxPassives => acquiredPassives.Count >= MaxPassives;

    public bool HasPassive(PassiveSkillId id) => acquiredPassives.Contains(id);

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
