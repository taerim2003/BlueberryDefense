using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class HUDController : MonoBehaviour
{
    [System.Serializable]
    private class ActiveSlot
    {
        public Image icon;
        public Image cooldownOverlay;
        public TMP_Text keyLabel;
        public TMP_Text cooldownText;
        public TMP_Text levelLabel;
        public Image[] gemIcons;
    }

    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerSkills playerSkills;
    [SerializeField] private PlayerPassives playerPassives;

    [SerializeField] private TMP_Text stageText;
    [SerializeField] private TMP_Text stageBannerText;
    [SerializeField] private CanvasGroup stageBannerGroup;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text expLevelText;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Image healthFill;
    [SerializeField] private Image overhealFill;
    [SerializeField] private RectTransform healthPanel;
    [SerializeField] private Image expFill;

    [System.Serializable]
    private class BuffSlot
    {
        public GameObject root;
        public Image icon;
        public TMP_Text timerText;
        public TMP_Text stackText;
    }

    [SerializeField] private Image[] passiveSlots;
    [SerializeField] private ActiveSlot[] activeSlots;
    [SerializeField] private BuffSlot[] buffSlots; // 지속시간 있는 버프 표시용 — 슬롯 개수만큼 BuffTracker의 활성 버프를 순서대로 채운다

    [SerializeField] private Sprite[] passiveIcons;
    [SerializeField] private Sprite[] activeIcons;
    [SerializeField] private Sprite[] gemIconSprites;

    private float healthFillTarget = -1f;
    private float overhealFillTarget = -1f;
    private float expFillTarget = -1f;
    private Tween healthFillTween;
    private Tween overhealFillTween;
    private Tween expFillTween;
    private bool[] passiveSlotWasFilled;
    private bool[] activeSlotWasFilled;
    private bool[] activeSlotWasOnCooldown;
    private int lastSeenStage = -1;
    private Sequence stageBannerSeq;

    private void Update()
    {
        if (GameManager.Instance != null)
        {
            int stage = GameManager.Instance.CurrentStage;
            stageText.text = $"Stage {stage}";

            if (lastSeenStage == -1) lastSeenStage = stage;
            else if (stage != lastSeenStage)
            {
                lastSeenStage = stage;
                ShowStageBanner(stage);
            }
        }

        healthText.text = $"{playerHealth.CurrentHealth} / {playerHealth.MaxHealth}";
        UpdateHealthFill();
        UpdateOverhealFill();

        if (PlayerExperience.Instance != null)
        {
            levelText.text = $"{PlayerExperience.Instance.Level} LV";
            expLevelText.text = $"Lv.{PlayerExperience.Instance.Level}";
            UpdateExpFill();
        }

        UpdatePassiveSlots();
        UpdateActiveSlots();
        UpdateBuffSlots();
    }

    // BuffTracker에 등록된 버프를 슬롯 개수만큼 순서대로 채운다. 새 버프가 늘어나도
    // 발생 지점에서 BuffTracker.Set(...)만 호출하면 되고, 여기 손댈 곳은 GetBuffIcon 매핑 한 줄뿐이다.
    private void UpdateBuffSlots()
    {
        if (buffSlots == null || buffSlots.Length == 0) return;

        var active = BuffTracker.GetActive();
        for (int i = 0; i < buffSlots.Length; i++)
        {
            BuffSlot slot = buffSlots[i];
            bool hasBuff = i < active.Count;
            if (slot.root != null) slot.root.SetActive(hasBuff);
            if (!hasBuff) continue;

            BuffTracker.Entry entry = active[i];
            if (slot.icon != null) slot.icon.sprite = GetBuffIcon(entry.Key);

            if (slot.timerText != null)
            {
                slot.timerText.enabled = entry.ShowTimer;
                if (entry.ShowTimer) slot.timerText.text = Mathf.Max(0f, entry.EndTime - Time.time).ToString("F1");
            }

            int stacks = entry.StackCount != null ? entry.StackCount() : 0;
            if (slot.stackText != null)
            {
                slot.stackText.enabled = stacks > 0;
                if (stacks > 0) slot.stackText.text = "x" + stacks;
            }
        }
    }

    private Sprite GetBuffIcon(string key) => key switch
    {
        "Lightning" => GetIcon(activeIcons, (int)ActiveSkillId.Lightning),
        "OrbAltar" => GetIcon(activeIcons, (int)ActiveSkillId.Orb),
        "LightningDamageBuff" => GetIcon(passiveIcons, (int)PassiveSkillId.Strength),
        _ => null,
    };

    private void UpdateHealthFill()
    {
        float ratio = playerHealth.MaxHealth > 0 ? (float)playerHealth.CurrentHealth / playerHealth.MaxHealth : 0f;
        if (Mathf.Approximately(ratio, healthFillTarget)) return;

        bool isDamage = ratio < healthFillTarget;
        healthFillTarget = ratio;

        healthFillTween?.Kill();
        healthFillTween = healthFill.DOFillAmount(ratio, 0.25f).SetEase(isDamage ? Ease.OutCubic : Ease.OutBack);

        if (isDamage && healthPanel != null)
            healthPanel.DOShakeAnchorPos(0.3f, 8f, 15, 90, false, true);
    }

    private void UpdateOverhealFill()
    {
        if (overhealFill == null || playerHealth.MaxHealth <= 0) return;

        float ratio = Mathf.Clamp01((float)(playerHealth.CurrentHealth + playerHealth.Overheal) / playerHealth.MaxHealth);
        if (Mathf.Approximately(ratio, overhealFillTarget)) return;

        overhealFillTarget = ratio;
        overhealFillTween?.Kill();
        overhealFillTween = overhealFill.DOFillAmount(ratio, 0.25f).SetEase(Ease.OutCubic);
    }

    private void UpdateExpFill()
    {
        float ratio = (float)PlayerExperience.Instance.CurrentXP / PlayerExperience.Instance.XPToNextLevel;
        if (Mathf.Approximately(ratio, expFillTarget)) return;

        expFillTarget = ratio;
        expFillTween?.Kill();
        expFillTween = expFill.DOFillAmount(ratio, 0.3f).SetEase(Ease.OutCubic);
    }

    private void UpdatePassiveSlots()
    {
        var acquired = playerPassives.AcquiredPassives;
        if (passiveSlotWasFilled == null) passiveSlotWasFilled = new bool[passiveSlots.Length];

        for (int i = 0; i < passiveSlots.Length; i++)
        {
            bool hasPassive = i < acquired.Count;
            passiveSlots[i].enabled = hasPassive;
            if (hasPassive) passiveSlots[i].sprite = GetIcon(passiveIcons, (int)acquired[i]);

            if (hasPassive && !passiveSlotWasFilled[i]) PunchIcon(passiveSlots[i].rectTransform);
            passiveSlotWasFilled[i] = hasPassive;
        }
    }

    private void ShowStageBanner(int stage)
    {
        if (stageBannerGroup == null || stageBannerText == null) return;

        stageBannerText.text = $"STAGE {stage}";

        RectTransform rt = stageBannerGroup.GetComponent<RectTransform>();
        stageBannerSeq?.Kill();
        rt.DOKill();
        rt.localScale = Vector3.one * 0.7f;
        stageBannerGroup.alpha = 0f;

        stageBannerSeq = DOTween.Sequence();
        stageBannerSeq.Append(stageBannerGroup.DOFade(1f, 0.3f));
        stageBannerSeq.Join(rt.DOScale(1f, 0.4f).SetEase(Ease.OutBack));
        stageBannerSeq.AppendInterval(2.2f);
        stageBannerSeq.Append(stageBannerGroup.DOFade(0f, 0.5f));
    }

    private static void PunchIcon(RectTransform rect)
    {
        rect.localScale = Vector3.one;
        rect.DOKill();
        rect.DOPunchScale(Vector3.one * 0.4f, 0.18f, 6, 0.4f);
    }

    private void UpdateActiveSlots()
    {
        var equipped = playerSkills.EquippedSkills;
        if (activeSlotWasFilled == null) activeSlotWasFilled = new bool[activeSlots.Length];
        if (activeSlotWasOnCooldown == null) activeSlotWasOnCooldown = new bool[activeSlots.Length];

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
                if (slot.levelLabel != null) slot.levelLabel.enabled = false;
                SetPathIcons(slot, null);
                activeSlotWasFilled[i] = false;
                activeSlotWasOnCooldown[i] = false;
                continue;
            }

            if (!activeSlotWasFilled[i]) PunchIcon(slot.icon.rectTransform);
            activeSlotWasFilled[i] = true;

            EquippedSkill skill = equipped[i];
            slot.icon.sprite = GetIcon(activeIcons, (int)skill.Id);
            slot.keyLabel.text = skill.Key.ToString();

            if (slot.levelLabel != null)
            {
                slot.levelLabel.enabled = true;
                slot.levelLabel.text = "Lv." + skill.Level;
            }

            bool onCooldown = skill.CooldownTimer > 0f;
            float ownCooldownRatio = skill.Cooldown > 0f ? Mathf.Clamp01(skill.CooldownTimer / skill.Cooldown) : 0f;
            slot.cooldownOverlay.fillAmount = Mathf.Max(ownCooldownRatio, playerSkills.GlobalCooldownRatio);
            slot.cooldownText.enabled = onCooldown;
            if (onCooldown) slot.cooldownText.text = skill.CooldownTimer.ToString("F1");

            if (activeSlotWasOnCooldown[i] && !onCooldown) PunchIcon(slot.icon.rectTransform);
            activeSlotWasOnCooldown[i] = onCooldown;

            SetPathIcons(slot, skill);
        }
    }

    private void SetPathIcons(ActiveSlot slot, EquippedSkill skill)
    {
        if (slot.gemIcons == null) return;

        for (int i = 0; i < slot.gemIcons.Length; i++)
        {
            bool hasPath = skill != null && i < skill.PathTier.Length && skill.PathTier[i] > 0;
            slot.gemIcons[i].enabled = hasPath;
            if (hasPath) slot.gemIcons[i].sprite = GetIcon(gemIconSprites, i);
        }
    }

    private static Sprite GetIcon(Sprite[] icons, int index) =>
        index >= 0 && index < icons.Length ? icons[index] : null;
}
