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

    [System.Serializable]
    private class PassiveSlot
    {
        public Image icon;
        public TMP_Text levelLabel;
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

    [SerializeField] private PassiveSlot[] passiveSlots;
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
    private float baseHealthPanelWidth = -1f;

    private void OnEnable() => PlayerSkills.OnRefreshProc += PulseRefreshIcon;
    private void OnDisable() => PlayerSkills.OnRefreshProc -= PulseRefreshIcon;

    // 리프레쉬 발동 시 리프레쉬 패시브 아이콘에 보잉(액티브 쿨타임 완료 연출과 동일)
    private void PulseRefreshIcon()
    {
        if (playerPassives == null || passiveSlots == null) return;
        var acquired = playerPassives.EquippedPassives;
        for (int i = 0; i < passiveSlots.Length && i < acquired.Count; i++)
            if (acquired[i].Id == PassiveSkillId.Refresh && passiveSlots[i].icon != null)
            {
                PunchIcon(passiveSlots[i].icon.rectTransform);
                return;
            }
    }

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

        healthText.text = playerHealth.Overheal > 0
            ? $"{playerHealth.CurrentHealth} (+{playerHealth.Overheal}) / {playerHealth.MaxHealth}"
            : $"{playerHealth.CurrentHealth} / {playerHealth.MaxHealth}";
        UpdateHealthPanelWidth();
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
        "Shotgun" => GetIcon(activeIcons, (int)ActiveSkillId.Shotgun),
        _ => null,
    };

    // 오버힐(보호막)이 있으면 체력바 자체의 폭을 (최대체력+오버힐)/최대체력 비율만큼 왼쪽으로 늘린다 (pivot이 우측 고정이라
    // sizeDelta만 키우면 자동으로 왼쪽으로만 자라 화면 밖으로 삐져나가지 않는다). 체력 1당 픽셀 밀도는 그대로 유지되므로
    // 최대체력 100·오버힐 20이면 정확히 5:1 비율로 배분되어 보인다.
    private float HealthBarScale => playerHealth.MaxHealth + playerHealth.Overheal;

    private void UpdateHealthPanelWidth()
    {
        if (healthPanel == null) return;
        if (baseHealthPanelWidth < 0f) baseHealthPanelWidth = healthPanel.sizeDelta.x;

        float widthMultiplier = playerHealth.MaxHealth > 0 ? HealthBarScale / playerHealth.MaxHealth : 1f;
        Vector2 sizeDelta = healthPanel.sizeDelta;
        sizeDelta.x = baseHealthPanelWidth * widthMultiplier;
        healthPanel.sizeDelta = sizeDelta;
    }

    private void UpdateHealthFill()
    {
        float scale = HealthBarScale;
        float ratio = scale > 0 ? (float)playerHealth.CurrentHealth / scale : 0f;
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

        float scale = HealthBarScale;
        float ratio = scale > 0 ? (float)(playerHealth.CurrentHealth + playerHealth.Overheal) / scale : 0f;
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
        var acquired = playerPassives.EquippedPassives;
        if (passiveSlotWasFilled == null) passiveSlotWasFilled = new bool[passiveSlots.Length];

        for (int i = 0; i < passiveSlots.Length; i++)
        {
            PassiveSlot slot = passiveSlots[i];
            bool hasPassive = i < acquired.Count;
            slot.icon.enabled = hasPassive;
            if (slot.levelLabel != null) slot.levelLabel.enabled = hasPassive;

            if (hasPassive)
            {
                slot.icon.sprite = GetIcon(passiveIcons, (int)acquired[i].Id);
                if (slot.levelLabel != null) slot.levelLabel.text = "Lv." + acquired[i].Level;
            }

            if (hasPassive && !passiveSlotWasFilled[i]) PunchIcon(slot.icon.rectTransform);
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
                Outline emptyOutline = slot.icon.GetComponent<Outline>();
                if (emptyOutline != null) emptyOutline.enabled = false;
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
            UpdateShotgunHighlight(slot, skill.Id);
        }
    }

    // 산탄 타수버프를 받는 동안 해당 스킬 아이콘에 노란 테두리를 켠다 (버프가 실제로 걸렸는지 눈에 보이게).
    private void UpdateShotgunHighlight(ActiveSlot slot, ActiveSkillId id)
    {
        if (slot.icon == null) return;
        bool buffed = PlayerSkills.IsShotgunBuffed(id);
        Outline outline = slot.icon.GetComponent<Outline>();
        if (outline == null)
        {
            if (!buffed) return; // 필요할 때만 컴포넌트를 붙인다
            outline = slot.icon.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.85f, 0.1f, 1f);
            outline.effectDistance = new Vector2(4f, 4f);
        }
        outline.enabled = buffed;
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
