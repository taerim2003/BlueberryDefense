using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LevelUpUI : MonoBehaviour
{
    public static LevelUpUI Instance { get; private set; }

    private class Option
    {
        public string Title;
        public string LevelText;
        public bool IsNew;
        public string Description;
        public Sprite Icon;
        public System.Action Apply;
    }

    [SerializeField] private GameObject panel;
    [SerializeField] private UITransition panelTransition;
    [SerializeField] private Button optionButtonA;
    [SerializeField] private Button optionButtonB;
    [SerializeField] private Button optionButtonC;
    [SerializeField] private TMP_Text titleA;
    [SerializeField] private TMP_Text titleB;
    [SerializeField] private TMP_Text titleC;
    [SerializeField] private TMP_Text levelA;
    [SerializeField] private TMP_Text levelB;
    [SerializeField] private TMP_Text levelC;
    [SerializeField] private TMP_Text descA;
    [SerializeField] private TMP_Text descB;
    [SerializeField] private TMP_Text descC;
    [SerializeField] private Image iconA;
    [SerializeField] private Image iconB;
    [SerializeField] private Image iconC;
    [SerializeField] private Sprite[] activeIcons;
    [SerializeField] private Sprite[] passiveIcons;
    [SerializeField] private LevelUpStatOptionSO[] statOptions;

    private static readonly Color NewTagColor = new Color(1f, 0.85f, 0.2f, 1f);
    private static readonly Color LevelTagColor = new Color(0.75f, 0.85f, 1f, 1f);

    private Option[] currentOptions;

    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);

        optionButtonA.onClick.AddListener(() => Choose(0));
        optionButtonB.onClick.AddListener(() => Choose(1));
        optionButtonC.onClick.AddListener(() => Choose(2));
    }

    public void Show()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        currentOptions = BuildOptions(skills, health, passives);
        ShowOptions();
    }

    private void ShowOptions()
    {
        bool alreadyOpen = panel.activeSelf;

        SetRow(titleA, levelA, descA, iconA, currentOptions[0]);
        SetRow(titleB, levelB, descB, iconB, currentOptions[1]);
        SetRow(titleC, levelC, descC, iconC, currentOptions[2]);

        panel.SetActive(true);
        if (!alreadyOpen) ModalPause.Push();
    }

    private static void SetRow(TMP_Text title, TMP_Text level, TMP_Text desc, Image icon, Option option)
    {
        title.text = option.Title;
        desc.text = option.Description;
        SetIcon(icon, option.Icon);

        if (level != null)
        {
            if (option.IsNew)
            {
                level.text = "신규!";
                level.color = NewTagColor;
            }
            else if (!string.IsNullOrEmpty(option.LevelText))
            {
                level.text = option.LevelText;
                level.color = LevelTagColor;
            }
            else
            {
                level.text = "";
            }
        }
    }

    private static void SetIcon(Image image, Sprite sprite)
    {
        image.enabled = sprite != null;
        image.sprite = sprite;
    }

    private Option[] BuildOptions(PlayerSkills skills, PlayerHealth health, PlayerPassives passives)
    {
        List<Option> candidates = new List<Option>();

        if (!skills.HasMaxSkills)
        {
            foreach (ActiveSkillId id in new[] { ActiveSkillId.Whirlwind, ActiveSkillId.Orb, ActiveSkillId.Lightning, ActiveSkillId.EagleDrop })
            {
                if (skills.HasSkill(id)) continue;
                ActiveSkillId captured = id;
                candidates.Add(new Option
                {
                    Title = PlayerSkills.GetActiveSkillName(captured),
                    IsNew = true,
                    Description = GetActiveSkillDescription(captured),
                    Icon = GetIcon(activeIcons, (int)captured),
                    Apply = () => skills.AcquireSkill(captured),
                });
            }
        }

        if (!passives.HasMaxPassives)
        {
            foreach (PassiveSkillId id in new[] { PassiveSkillId.Strength, PassiveSkillId.Health, PassiveSkillId.Knowledge, PassiveSkillId.Assassinate, PassiveSkillId.Refresh })
            {
                if (passives.HasPassive(id)) continue;
                PassiveSkillId captured = id;
                candidates.Add(new Option
                {
                    Title = PlayerSkills.GetPassiveSkillName(captured),
                    IsNew = true,
                    Description = GetPassiveSkillDescription(captured),
                    Icon = GetIcon(passiveIcons, (int)captured),
                    Apply = () => passives.AcquirePassive(captured),
                });
            }
        }

        foreach (EquippedSkill equipped in skills.EquippedSkills)
        {
            if (!skills.CanUpgradeSkill(equipped)) continue;
            EquippedSkill captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetActiveSkillName(captured.Id),
                LevelText = "레벨: " + (captured.Level + 1),
                Description = PlayerSkills.DescribeUpgradeEffect(captured.Id, captured.Level + 1),
                Icon = GetIcon(activeIcons, (int)captured.Id),
                Apply = () => skills.UpgradeSkillLevel(captured.Id),
            });
        }

        Shuffle(candidates);
        List<Option> options = candidates.Take(3).ToList();

        int statIndex = 0;
        while (options.Count < 3 && statOptions != null && statOptions.Length > 0)
        {
            LevelUpStatOptionSO so = statOptions[statIndex % statOptions.Length];
            options.Add(new Option { Title = so.title, Description = so.description, Apply = () => ApplyStatEffect(so, skills, health) });
            statIndex++;
        }

        return options.ToArray();
    }

    public void ShowTreasureReward()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();

        List<EquippedSkill> ready = skills.EquippedSkills.Where(s =>
            s.Level >= 5 && s.TotalEvolutionTier < s.Level / 5 && s.TotalEvolutionTier < 4 && skills.CanEvolveAnyPath(s)).ToList();

        if (ready.Count == 0)
        {
            Show();
            return;
        }

        if (ready.Count == 1)
        {
            EvolutionTreeUI.Instance.Show(skills, ready[0]);
            return;
        }

        Shuffle(ready);
        List<Option> pickOptions = ready.Take(3).Select(s => new Option
        {
            Title = PlayerSkills.GetActiveSkillName(s.Id),
            LevelText = "진화 가능",
            Description = "어떤 스킬을 먼저 진화시킬지 선택하세요",
            Icon = GetIcon(activeIcons, (int)s.Id),
            Apply = () => EvolutionTreeUI.Instance.Show(skills, s),
        }).ToList();

        while (pickOptions.Count < 3)
            pickOptions.Add(new Option { Title = "체력 강화", Description = "최대 체력 +20", Apply = () => FindAnyObjectByType<PlayerHealth>().IncreaseMaxHealth(20) });

        currentOptions = pickOptions.ToArray();
        ShowOptions();
    }

    private static void ApplyStatEffect(LevelUpStatOptionSO so, PlayerSkills skills, PlayerHealth health)
    {
        switch (so.effect)
        {
            case LevelUpStatEffect.BasicAttackDamageFlat:
                skills.UpgradeSkillDamage(ActiveSkillId.BasicAttack, so.value);
                break;
            case LevelUpStatEffect.BasicAttackCooldownPercent:
                skills.UpgradeSkillCooldown(ActiveSkillId.BasicAttack, 1f - so.value / 100f);
                break;
            case LevelUpStatEffect.MaxHealthFlat:
                health.IncreaseMaxHealth((int)so.value);
                break;
            case LevelUpStatEffect.XpMultiplierPercent:
                PlayerExperience.Instance.IncreaseXPMultiplier(so.value / 100f);
                break;
        }
    }

    private static Sprite GetIcon(Sprite[] icons, int index) =>
        icons != null && index >= 0 && index < icons.Length ? icons[index] : null;

    private static string GetActiveSkillDescription(ActiveSkillId id) => id switch
    {
        ActiveSkillId.Whirlwind => "적을 자동으로 추적하는 회오리를 소환(지속시간 4초). 닿아있는 동안 지속 피해를 주며, 피해를 주는 동안 이동 속도가 느려짐",
        ActiveSkillId.Orb => "전방으로 관통하며 나아가는 오브를 소환. 닿아있는 모든 적에게 지속 피해를 주고 느려지게 함",
        ActiveSkillId.Lightning => "6초간 공격 피해를 입는 모든 적들에게 30% 확률로 낙뢰가 떨어져 피해",
        ActiveSkillId.EagleDrop => "화면 전체에 독수리를 1초 간격으로 3회 투하해 모든 적에게 피해",
        _ => "",
    };

    private static string GetPassiveSkillDescription(PassiveSkillId id) => id switch
    {
        PassiveSkillId.Strength => "피해량 10% 증가",
        PassiveSkillId.Health => "최대 체력 20 증가",
        PassiveSkillId.Knowledge => "경험치 획득량 10% 증가",
        PassiveSkillId.Assassinate => "모든 피해가 15% 확률로 3배 피해",
        PassiveSkillId.Refresh => "스킬 사용 시 5% 확률로 쿨타임 초기화",
        _ => "",
    };

    private void Choose(int index)
    {
        currentOptions[index].Apply?.Invoke();
        Close();
    }

    private void Close()
    {
        ModalPause.Pop();
        if (panelTransition != null) panelTransition.Hide();
        else panel.SetActive(false);
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

}
