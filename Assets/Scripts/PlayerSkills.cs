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
    public readonly List<GemType> EquippedGems = new List<GemType>();
}

public class PlayerSkills : MonoBehaviour
{
    private const float GlobalCooldown = 0.2f;
    private static readonly Key[] SlotKeys = { Key.Q, Key.W, Key.E, Key.R };

    [SerializeField] private GameObject basicAttackProjectilePrefab;
    [SerializeField] private GameObject whirlwindPrefab;
    [SerializeField] private GameObject orbPrefab;
    [SerializeField] private GameObject eagleDropPrefab;
    [SerializeField] private GameObject eagleImpactVfxPrefab;
    [SerializeField] private Animator animator;

    private readonly List<EquippedSkill> equippedSkills = new List<EquippedSkill>();
    private float globalCooldownTimer;
    private float passiveDamageMultiplier = 1f;
    private PlayerPassives passives;

    public bool HasMaxSkills => equippedSkills.Count >= SlotKeys.Length;
    public IReadOnlyList<EquippedSkill> EquippedSkills => equippedSkills;
    public float GlobalCooldownRatio => Mathf.Clamp01(globalCooldownTimer / GlobalCooldown);

    private void Awake()
    {
        passives = GetComponent<PlayerPassives>();
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

    public bool CanUpgradeSkill(EquippedSkill skill) => skill.EquippedGems.Count >= skill.Level / 5;

    public void UpgradeSkillLevel(ActiveSkillId id)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null || !CanUpgradeSkill(skill)) return;

        skill.Level++;
        skill.Damage *= 1.15f;
    }

    public void EquipGem(ActiveSkillId id, GemType gem)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null || skill.EquippedGems.Count >= 3 || skill.EquippedGems.Contains(gem)) return;

        skill.EquippedGems.Add(gem);

        switch (gem)
        {
            case GemType.Emerald:
                skill.Damage *= 1.5f;
                break;
            case GemType.Topaz:
                skill.Cooldown *= 0.65f;
                break;
        }
    }

    private void TryUseSkill(EquippedSkill skill)
    {
        if (globalCooldownTimer > 0f || skill.CooldownTimer > 0f) return;

        float damage = ComputeFinalDamage(skill.Damage);

        switch (skill.Id)
        {
            case ActiveSkillId.BasicAttack:
                if (!FireBasicAttack(damage, skill)) return;
                break;
            case ActiveSkillId.Whirlwind:
                FireWhirlwind(damage, skill);
                break;
            case ActiveSkillId.Orb:
                FireOrb(damage, skill);
                break;
            case ActiveSkillId.Lightning:
                LightningStorm.ActiveUntil = Time.time + 6f;
                break;
            case ActiveSkillId.EagleDrop:
                StartCoroutine(EagleDropRoutine(damage, skill));
                break;
        }

        globalCooldownTimer = GlobalCooldown;
        skill.CooldownTimer = skill.Cooldown;

        if (passives != null && passives.HasPassive(PassiveSkillId.Refresh) && Random.value < PlayerPassives.RefreshChance)
            skill.CooldownTimer = 0f;
    }

    private float ComputeFinalDamage(float baseDamage)
    {
        float damage = baseDamage * passiveDamageMultiplier;

        if (passives != null && passives.HasPassive(PassiveSkillId.Assassinate) && Random.value < PlayerPassives.AssassinateCritChance)
            damage *= PlayerPassives.AssassinateCritMultiplier;

        return damage;
    }

    private bool FireBasicAttack(float damage, EquippedSkill skill)
    {
        Enemy target = FindFrontmostEnemy();
        if (target == null) return false;

        GameObject obj = Instantiate(basicAttackProjectilePrefab, transform.position, Quaternion.identity);
        Projectile projectile = obj.GetComponent<Projectile>();
        projectile.Damage = damage;
        projectile.ApplyGemSlow = skill.EquippedGems.Contains(GemType.Amethyst);
        projectile.ApplyGemVulnerable = skill.EquippedGems.Contains(GemType.Garnet);
        animator.SetTrigger("Attack");
        return true;
    }

    private void FireWhirlwind(float damage, EquippedSkill skill)
    {
        GameObject obj = Instantiate(whirlwindPrefab, transform.position, Quaternion.identity);
        Whirlwind whirlwind = obj.GetComponent<Whirlwind>();
        whirlwind.Damage = damage;
        whirlwind.ApplyGemSlow = skill.EquippedGems.Contains(GemType.Amethyst);
        whirlwind.ApplyGemVulnerable = skill.EquippedGems.Contains(GemType.Garnet);
    }

    private void FireOrb(float damage, EquippedSkill skill)
    {
        GameObject obj = Instantiate(orbPrefab, transform.position, Quaternion.identity);
        Orb orb = obj.GetComponent<Orb>();
        orb.Damage = damage;
        orb.ApplyGemVulnerable = skill.EquippedGems.Contains(GemType.Garnet);
    }

    private IEnumerator EagleDropRoutine(float damage, EquippedSkill skill)
    {
        bool applySlow = skill.EquippedGems.Contains(GemType.Amethyst);
        bool applyVulnerable = skill.EquippedGems.Contains(GemType.Garnet);

        for (int i = 0; i < 3; i++)
        {
            foreach (Enemy enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                Vector3 pos = enemy.transform.position;
                enemy.TakeDamage(damage);
                if (applySlow) enemy.ApplySlow(0.3f, 3f);
                if (applyVulnerable) enemy.ApplyVulnerable(1.5f, 3f);
                StartCoroutine(MeteorImpact(pos));
            }

            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator MeteorImpact(Vector3 targetPos)
    {
        if (eagleDropPrefab == null) yield break;

        Vector3 start = targetPos + Vector3.up * 4f;
        GameObject meteor = Instantiate(eagleDropPrefab, start, Quaternion.identity);
        meteor.transform.localScale = Vector3.one * 0.2f;
        foreach (ParticleSystem ps in meteor.GetComponentsInChildren<ParticleSystem>(true))
            ps.Play();

        float duration = 0.2f;
        float t = 0f;
        while (t < duration)
        {
            meteor.transform.position = Vector3.Lerp(start, targetPos, t / duration);
            t += Time.deltaTime;
            yield return null;
        }
        Destroy(meteor);

        if (eagleImpactVfxPrefab != null)
        {
            GameObject impact = Instantiate(eagleImpactVfxPrefab, targetPos, Quaternion.identity);
            impact.transform.localScale = Vector3.one * 0.25f;
            Destroy(impact, 2f);
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
        ActiveSkillId.BasicAttack => 10f,
        ActiveSkillId.Whirlwind => 15f,
        ActiveSkillId.Orb => 12f,
        ActiveSkillId.Lightning => LightningStorm.ProcDamage,
        ActiveSkillId.EagleDrop => 10f,
        _ => 10f,
    };
}
