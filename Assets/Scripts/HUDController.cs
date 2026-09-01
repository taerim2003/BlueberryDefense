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
    private int lastSeenCurrency = -1;
    private TMP_Text essenceText;      // 이번 판 정수 표시 — 씬에 없어서 런타임 생성한다
    private Sequence stageBannerSeq;

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
            // 판 길이가 승천마다 달라졌으므로(15/20/25) 총 스테이지 수를 같이 보여준다 — 안 그러면 얼마나 남았는지 알 수 없다.
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
        UpdateHealthFill();
        UpdateOverhealFill();
        UpdateDangerVignette();

        if (PlayerExperience.Instance != null)
        {
            levelText.text = $"{PlayerExperience.Instance.Level} LV";
            expLevelText.text = $"Lv.{PlayerExperience.Instance.Level}";
            UpdateExpFill();
        }

        UpdateEssenceText();
        UpdatePassiveSlots();
        UpdateActiveSlots();
        UpdateBuffSlots();
    }

    // ── 이번 판에 모은 정수(HUD 좌상단) ──
    // 씬에 없는 요소라 런타임에 만든다. HUD 좌상단은 유일하게 비어 있는 모서리다
    // (중앙 위=스테이지 / 우상단=체력·버프 / 좌하단=레벨·패시브 / 우하단=액티브).
    private void UpdateEssenceText()
    {
        if (essenceText == null)
        {
            if (levelText == null) return;   // 폰트·크기를 빌려올 원본이 있어야 만든다
            GameObject go = new GameObject("EssenceText", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(28f, -28f);
            rt.sizeDelta = new Vector2(320f, 44f);

            essenceText = go.AddComponent<TextMeshProUGUI>();
            essenceText.font = levelText.font;
            essenceText.fontSize = levelText.fontSize;
            essenceText.color = new Color(1f, 0.85f, 0.25f); // 정수(태양빛)
            essenceText.alignment = TextAlignmentOptions.TopLeft;
            essenceText.raycastTarget = false;
        }

        if (MetaRun.RunCurrency == lastSeenCurrency) return;
        bool grew = lastSeenCurrency >= 0;
        lastSeenCurrency = MetaRun.RunCurrency;
        essenceText.text = Loc.F("ui.essence", MetaRun.RunCurrency);
        if (grew) PunchIcon(essenceText.rectTransform);
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

    // 체력바의 눈금 기준. 오버힐이 붙으면 **같은 길이 안에서** 체력과 오버힐이 비율을 나눠 갖는다.
    //
    // 🔴 예전엔 이 비율만큼 바 자체를 왼쪽으로 늘렸다("체력 1당 픽셀 밀도 유지"). 그런데 늘어나는 건
    //    바탕(`HealthPanel`) 하나뿐이었다 — 테두리(`HealthOuter`)도 채움 층도 형제/고정 크기라
    //    따라가지 않는다. 오버힐 상한(200)에 최대체력 100이면 바탕만 370→1110px로 자라
    //    **테두리 밖으로 740px이 삐져나왔다**(8/27 빌드 QA "추가 체력 증가 시 체력바가 범위를 벗어남").
    //    셋을 같이 늘리는 길은 막혀 있다: 테두리는 `Simple + Preserve Aspect`라(CLAUDE.md §5-1)
    //    가로로 늘리면 세로까지 따라 커지고, PA를 끄면 도트 테두리가 가로로 뭉갠다.
    //    그래서 **바 길이를 고정**한다. 아래 두 Fill이 이미 이 값으로 비율을 내고 있어 계산은 그대로다.
    private float HealthBarScale => playerHealth.MaxHealth + playerHealth.Overheal;

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

    // 체력 위험 시 화면 가장자리 붉은 점멸. 오버힐은 빼고 **순수 최대 체력** 대비로 판정한다 —
    // 위 UpdateHealthFill의 비율은 바 길이용(HealthBarScale = MaxHealth + Overheal)이라 여기 쓰면 기준이 흔들린다.
    private DangerVignette dangerVignette;

    private void UpdateDangerVignette()
    {
        if (dangerVignette == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            dangerVignette = DangerVignette.Create(canvas.rootCanvas != null ? canvas.rootCanvas : canvas);
        }

        float ratio = playerHealth.MaxHealth > 0 ? (float)playerHealth.CurrentHealth / playerHealth.MaxHealth : 1f;
        dangerVignette.SetDanger(playerHealth.CurrentHealth > 0 && ratio <= DangerVignette.DangerRatio);
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
                slot.icon.sprite = EvoIcon(acquired[i]) ?? GetIcon(passiveIcons, (int)acquired[i].Id);
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
        rt.localScale = Vector3.one * 0.6f;
        stageBannerGroup.alpha = 0f;

        stageBannerSeq = DOTween.Sequence();
        stageBannerSeq.Append(stageBannerGroup.DOFade(1f, 0.14f));
        stageBannerSeq.Join(rt.DOScale(1f, 0.24f).SetEase(Ease.OutBack));
        stageBannerSeq.AppendInterval(2.2f);
        stageBannerSeq.Append(stageBannerGroup.DOFade(0f, 0.3f));
    }

    private static void PunchIcon(RectTransform rect)
    {
        rect.localScale = Vector3.one;
        rect.DOKill();
        rect.DOPunchScale(Vector3.one * 0.45f, 0.16f, 9, 0.5f);
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
            slot.icon.sprite = EvoIcon(skill) ?? GetIcon(activeIcons, (int)skill.Id);
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

    private const float ShotgunOutlineWidth = 6f; // 칸 바깥으로 삐져나오는 테 두께(px)

    // 칸 **바깥**을 두르는 노란 테를 만든다.
    // 🔴 예전엔 아이콘의 형제로 만들어 `SetAsFirstSibling()`으로 깔았는데, 그건 슬롯 판(`IconFrame`)
    //    **위**다 — 부모 자신의 Image가 자식보다 먼저 그려지기 때문이다. 그래서 칸이 통째로
    //    노랗게 채워져 보였다(8/27 빌드 QA "아이콘 칸 전체가 노랗게 채워진 상태").
    //    슬롯의 **형제**로 옮기고 슬롯 바로 앞에 꽂으면, 불투명한 슬롯 판이 가운데를 가려
    //    삐져나온 테두리만 남는다.
    // ⚠️ `UI/SelectOutline` 셰이더를 쓰지 않는 이유: 그건 실루엣 **바깥**을 칠하는데
    //    `IconFrame`은 40×40이 전부 불투명이라 rect 안에 칠할 여백이 없다(실측). 테가 통째로 사라진다.
    private Image CreateShotgunFrame(Image icon)
    {
        GameObject go = new GameObject("ShotgunFrame", typeof(RectTransform), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        RectTransform slotRt = (RectTransform)icon.rectTransform.parent;
        rt.SetParent(slotRt.parent, false);
        rt.anchorMin = slotRt.anchorMin;
        rt.anchorMax = slotRt.anchorMax;
        // 슬롯 pivot을 그대로 쓰면 크기를 키울 때 한쪽으로만 자라 칸과 중심이 어긋난다 —
        // 중심 기준으로 잡고 슬롯의 중심 좌표를 직접 계산해 얹는다.
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = slotRt.anchoredPosition + new Vector2(
            (0.5f - slotRt.pivot.x) * slotRt.rect.width,
            (0.5f - slotRt.pivot.y) * slotRt.rect.height);
        rt.sizeDelta = slotRt.rect.size + Vector2.one * (ShotgunOutlineWidth * 2f);
        rt.SetSiblingIndex(slotRt.GetSiblingIndex()); // 슬롯 바로 앞 = 슬롯보다 먼저 그려짐(뒤에 깔림)

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

    // 진화 아이콘은 LevelUpUI가 단독으로 배선해 두고 여기선 빌려 쓴다(같은 32칸 배열을 두 벌 두지 않으려고).
    // 미진화이거나 그림이 비어 있으면 null → 호출부가 원본 아이콘으로 떨어진다.
    private static Sprite EvoIcon(EquippedSkill s) => LevelUpUI.Instance != null && s.EvolutionStage > 0 && s.Route >= 0
        ? LevelUpUI.Instance.GetActiveEvoIcon(s.Id, s.Route) : null;

    private static Sprite EvoIcon(EquippedPassive p) => LevelUpUI.Instance != null && p.EvolutionStage > 0 && p.Route >= 0
        ? LevelUpUI.Instance.GetPassiveEvoIcon(p.Id, p.Route) : null;
}
