using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUDController : MonoBehaviour
{
    [System.Serializable]
    private class ActiveSlot
    {
        public Image icon;
        public Image cooldownOverlay;
        public TMP_Text keyLabel;
        public TMP_Text cooldownText;
        public Image[] gemIcons;
    }

    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerSkills playerSkills;
    [SerializeField] private PlayerPassives playerPassives;

    [SerializeField] private TMP_Text stageText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text expLevelText;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Image healthFill;
    [SerializeField] private Image expFill;

    [SerializeField] private Image[] passiveSlots;
    [SerializeField] private ActiveSlot[] activeSlots;

    [SerializeField] private Sprite[] passiveIcons;
    [SerializeField] private Sprite[] activeIcons;
    [SerializeField] private Sprite[] gemIconSprites;

    private void Update()
    {
        if (GameManager.Instance != null)
            stageText.text = $"Stage {GameManager.Instance.CurrentStage}";

        healthText.text = $"{playerHealth.CurrentHealth} / {playerHealth.MaxHealth}";
        healthFill.fillAmount = playerHealth.MaxHealth > 0 ? (float)playerHealth.CurrentHealth / playerHealth.MaxHealth : 0f;

        if (PlayerExperience.Instance != null)
        {
            levelText.text = $"{PlayerExperience.Instance.Level} LV";
            expLevelText.text = $"Lv.{PlayerExperience.Instance.Level}";
            expFill.fillAmount = (float)PlayerExperience.Instance.CurrentXP / PlayerExperience.Instance.XPToNextLevel;
        }

        UpdatePassiveSlots();
        UpdateActiveSlots();
    }

    private void UpdatePassiveSlots()
    {
        var acquired = playerPassives.AcquiredPassives;
        for (int i = 0; i < passiveSlots.Length; i++)
        {
            bool hasPassive = i < acquired.Count;
            passiveSlots[i].enabled = hasPassive;
            if (hasPassive) passiveSlots[i].sprite = GetIcon(passiveIcons, (int)acquired[i]);
        }
    }

    private void UpdateActiveSlots()
    {
        var equipped = playerSkills.EquippedSkills;
        for (int i = 0; i < activeSlots.Length; i++)
        {
            ActiveSlot slot = activeSlots[i];
            bool hasSkill = i < equipped.Count;

            slot.icon.enabled = hasSkill;
            if (!hasSkill)
            {
                slot.keyLabel.text = "";
                slot.cooldownOverlay.fillAmount = 0f;
                slot.cooldownText.enabled = false;
                SetGemIcons(slot, null);
                continue;
            }

            EquippedSkill skill = equipped[i];
            slot.icon.sprite = GetIcon(activeIcons, (int)skill.Id);
            slot.keyLabel.text = skill.Key.ToString();

            bool onCooldown = skill.CooldownTimer > 0f;
            float ownCooldownRatio = skill.Cooldown > 0f ? Mathf.Clamp01(skill.CooldownTimer / skill.Cooldown) : 0f;
            slot.cooldownOverlay.fillAmount = Mathf.Max(ownCooldownRatio, playerSkills.GlobalCooldownRatio);
            slot.cooldownText.enabled = onCooldown;
            if (onCooldown) slot.cooldownText.text = skill.CooldownTimer.ToString("F1");

            SetGemIcons(slot, skill.EquippedGems);
        }
    }

    private void SetGemIcons(ActiveSlot slot, System.Collections.Generic.IReadOnlyList<GemType> gems)
    {
        if (slot.gemIcons == null) return;

        for (int i = 0; i < slot.gemIcons.Length; i++)
        {
            bool hasGem = gems != null && i < gems.Count;
            slot.gemIcons[i].enabled = hasGem;
            if (hasGem) slot.gemIcons[i].sprite = GetIcon(gemIconSprites, (int)gems[i]);
        }
    }

    private static Sprite GetIcon(Sprite[] icons, int index) =>
        index >= 0 && index < icons.Length ? icons[index] : null;
}
