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
        [System.NonSerialized] public Image shotgunFrame; // 산탄 버프 시 아이콘 뒤에 켜지는 노란 하이라이트 프레임(런타임 생성)
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
            // 판 길이가 승천마다 달라졌으므로(20/25/30) 총 스테이지 수를 같이 보여준다 — 안 그러면 얼마나 남았는지 알 수 없다.
            stageText.text = $"Stage {stage}/{GameManager.Instance.FinalStage}";

            if (lastSeenStage == -1) lastSeenStage = stage;
            else if (stage != lastSeenStage)
            {
                lastSeenStage = stage;
                ShowStageBanner(stage);
                PunchIcon(stageText.rectTransform);
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
                if (slot.shotgunFrame != null) slot.shotgunFrame.enabled = false;
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
            // 자동 시전 전용 스킬은 수동으로 못 쓴다는 걸 강조하려고 마스크를 항상 100%로 덮는다.
            slot.cooldownOverlay.fillAmount = PlayerSkills.IsAutoCastOnly(skill)
                ? 1f
                : Mathf.Max(ownCooldownRatio, playerSkills.GlobalCooldownRatio);
            slot.cooldownText.enabled = onCooldown;
            if (onCooldown) slot.cooldownText.text = skill.CooldownTimer.ToString("F1");

            if (activeSlotWasOnCooldown[i] && !onCooldown) PunchIcon(slot.icon.rectTransform);
            activeSlotWasOnCooldown[i] = onCooldown;

            SetPathIcons(slot, skill);
            UpdateShotgunHighlight(slot, skill.Id);
        }
    }

    // 산탄 타수버프를 받는 동안 해당 스킬 아이콘 "테두리 바깥"에 노란 하이라이트 프레임을 켠다.
    // (예전엔 아이콘 이미지에 uGUI Outline을 붙여 스프라이트가 4방향으로 복제돼 이미지 위에 뭔가 덧씌운 것처럼 보였음)
    private void UpdateShotgunHighlight(ActiveSlot slot, ActiveSkillId id)
    {
        if (slot.icon == null) return;
        bool buffed = PlayerSkills.IsShotgunBuffed(id);

        // 예전 방식(아이콘 위 Outline 효과)이 남아 있으면 제거
        Outline legacy = slot.icon.GetComponent<Outline>();
        if (legacy != null) Destroy(legacy);

        if (slot.shotgunFrame == null)
        {
            if (!buffed) return; // 필요할 때만 생성
            slot.shotgunFrame = CreateShotgunFrame(slot.icon);
        }
        slot.shotgunFrame.enabled = buffed;
    }

    // 아이콘의 부모 아래, 아이콘보다 한 사이즈 크게 뒤(첫 형제)에 깔리는 노란 프레임을 만든다.
    private Image CreateShotgunFrame(Image icon)
    {
        GameObject go = new GameObject("ShotgunFrame", typeof(RectTransform), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        RectTransform iconRt = icon.rectTransform;
        rt.SetParent(iconRt.parent, false);
        rt.anchorMin = iconRt.anchorMin;
        rt.anchorMax = iconRt.anchorMax;
        rt.pivot = iconRt.pivot;
        rt.anchoredPosition = iconRt.anchoredPosition;
        rt.sizeDelta = iconRt.sizeDelta + new Vector2(12f, 12f); // 테두리 바깥으로 6px씩 삐져나오게
        rt.SetAsFirstSibling(); // 아이콘·키라벨·쿨오버레이 뒤에 깔림

        Image frame = go.GetComponent<Image>();
        frame.color = new Color(1f, 0.85f, 0.1f, 1f);
        frame.raycastTarget = false;
        return frame;
    }

    // 슬롯 아래 보석 = 진화 횟수(0~2). 예전엔 투자한 path 수였지만 진화가 2루트×2티어로 바뀌면서
    // 루트는 항상 하나뿐이라 "몇 차 진화까지 갔나"를 보여주는 게 맞다.
    private void SetPathIcons(ActiveSlot slot, EquippedSkill skill)
    {
        if (slot.gemIcons == null) return;

        int stage = skill != null ? skill.EvolutionStage : 0;
        for (int i = 0; i < slot.gemIcons.Length; i++)
        {
            bool filled = i < stage;
            slot.gemIcons[i].enabled = filled;
            if (filled) slot.gemIcons[i].sprite = GetIcon(gemIconSprites, i);
        }
    }

    private static Sprite GetIcon(Sprite[] icons, int index) =>
        index >= 0 && index < icons.Length ? icons[index] : null;
}
