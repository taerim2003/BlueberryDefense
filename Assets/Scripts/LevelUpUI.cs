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
        public bool UnlocksEvolution; // 다음 레벨이 5의 배수 → 진화 해금 가능 레벨업
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
    [SerializeField] private Button rerollButton;   // 스킬트리 리롤 해금 시 노출
    [SerializeField] private TMP_Text rerollLabel;

    private static readonly Color NewTagColor = new Color(1f, 0.85f, 0.2f, 1f);
    private static readonly Color LevelTagColor = new Color(0.75f, 0.85f, 1f, 1f);
    private static readonly Color EvolveTagColor = new Color(1f, 0.55f, 0.1f, 1f); // 진화 가능 강조(주황)

    private Outline[] optionOutlines;

    private const int EssenceReward = 10; // 레벨업할 게 없을 때 대체로 지급하는 정수량

    private Option[] currentOptions;
    private int rerollsRemaining;  // 게임당 남은 리롤 횟수
    private bool rerollable;        // 이번 모달이 리롤 가능한가(진화 선택 모달은 불가)

    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);

        optionButtonA.onClick.AddListener(() => Choose(0));
        optionButtonB.onClick.AddListener(() => Choose(1));
        optionButtonC.onClick.AddListener(() => Choose(2));
        if (rerollButton != null) rerollButton.onClick.AddListener(OnReroll);

        // 진화 가능 레벨업 강조용 셀 아웃라인(기본 꺼짐)
        optionOutlines = new[] { MakeOutline(optionButtonA), MakeOutline(optionButtonB), MakeOutline(optionButtonC) };
    }

    private static Outline MakeOutline(Button btn)
    {
        if (btn == null) return null;
        Outline o = btn.GetComponent<Outline>();
        if (o == null) o = btn.gameObject.AddComponent<Outline>();
        o.effectColor = EvolveTagColor;
        o.effectDistance = new Vector2(5f, 5f);
        o.enabled = false;
        return o;
    }

    // 판 시작 시 MetaRunApplier가 호출 — 스킬트리 리롤 노드 해금 수만큼 리롤 부여
    public void InitRerolls(int count) => rerollsRemaining = count;

    public void Show()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        rerollable = true;
        currentOptions = BuildOptions(skills, passives);
        ShowOptions();
    }

    private void OnReroll()
    {
        if (!rerollable || rerollsRemaining <= 0) return;
        rerollsRemaining--;
        Show(); // 후보 재구성 + 재셔플 (rerollable 다시 true)
    }

    private void ShowOptions()
    {
        bool alreadyOpen = panel.activeSelf;

        // 선택지는 1~3개로 가변 — 남는 슬롯의 옵션 카드(버튼)는 통째로 숨긴다.
        int count = currentOptions.Length;
        SetSlot(0, optionButtonA, titleA, levelA, descA, iconA, count > 0 ? currentOptions[0] : null);
        SetSlot(1, optionButtonB, titleB, levelB, descB, iconB, count > 1 ? currentOptions[1] : null);
        SetSlot(2, optionButtonC, titleC, levelC, descC, iconC, count > 2 ? currentOptions[2] : null);

        UpdateRerollButton();

        panel.SetActive(true);
        if (!alreadyOpen) ModalPause.Push();
    }

    private void SetSlot(int index, Button button, TMP_Text title, TMP_Text level, TMP_Text desc, Image icon, Option option)
    {
        if (button != null) button.gameObject.SetActive(option != null);
        if (optionOutlines != null && optionOutlines[index] != null)
            optionOutlines[index].enabled = option != null && option.UnlocksEvolution;
        if (option != null) SetRow(title, level, desc, icon, option);
    }

    private void UpdateRerollButton()
    {
        if (rerollButton == null) return;
        bool show = rerollable && rerollsRemaining > 0;
        rerollButton.gameObject.SetActive(show);
        if (show && rerollLabel != null) rerollLabel.text = $"다시 뽑기 ({rerollsRemaining})";
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
            else if (option.UnlocksEvolution)
            {
                level.text = "진화 가능! " + option.LevelText;
                level.color = EvolveTagColor;
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

    private Option[] BuildOptions(PlayerSkills skills, PlayerPassives passives)
    {
        List<Option> candidates = new List<Option>();
        CharacterDefinition character = RunConfig.Character; // 후보 풀 게이팅(null이면 전체 허용 = 현행)

        if (!skills.HasMaxSkills)
        {
            foreach (ActiveSkillId id in new[] { ActiveSkillId.Whirlwind, ActiveSkillId.Orb, ActiveSkillId.Lightning, ActiveSkillId.EagleDrop, ActiveSkillId.Sniping, ActiveSkillId.Homing, ActiveSkillId.Shotgun, ActiveSkillId.Rewind })
            {
                if (skills.HasSkill(id)) continue;
                if (character != null && !character.AllowsActive(id)) continue;
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
                if (character != null && !character.AllowsPassive(id)) continue;
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
                UnlocksEvolution = (captured.Level + 1) % 5 == 0,
                Description = PlayerSkills.DescribeUpgradeEffect(captured, captured.Level + 1),
                Icon = GetIcon(activeIcons, (int)captured.Id),
                Apply = () => skills.UpgradeSkillLevel(captured.Id),
            });
        }

        foreach (EquippedPassive equipped in passives.EquippedPassives)
        {
            if (!passives.CanUpgradePassive(equipped)) continue;
            EquippedPassive captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetPassiveSkillName(captured.Id),
                LevelText = "레벨: " + (captured.Level + 1),
                UnlocksEvolution = (captured.Level + 1) % 5 == 0,
                Description = PlayerPassives.DescribePassiveLevelEffect(captured.Id),
                Icon = GetIcon(passiveIcons, (int)captured.Id),
                Apply = () => passives.UpgradePassiveLevel(captured.Id),
            });
        }

        Shuffle(candidates);
        List<Option> options = candidates.Take(3).ToList();

        // 레벨업 가능한 후보가 3개보다 적으면 '정수 +10' 선택지를 하나만 끼운다.
        // 후보가 0~1개면 선택지 자체가 1~2개만 뜬다.
        if (options.Count < 3)
            options.Add(EssenceOption());

        return options.ToArray();
    }

    // 레벨업할 스킬이 부족할 때 자리를 메우는 대체 보상: 이번 판 정수 +10
    private static Option EssenceOption() => new Option
    {
        Title = "정수 획득",
        Description = "정수 +" + EssenceReward,
        Apply = () => MetaRun.Collect(EssenceReward),
    };

    public void ShowTreasureReward()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        List<EquippedSkill> readySkills = skills.EquippedSkills.Where(s =>
            s.Level >= 5 && s.TotalEvolutionTier < s.Level / 5 && s.TotalEvolutionTier < 4 && skills.CanEvolveAnyPath(s)).ToList();
        List<EquippedPassive> readyPassives = passives.EquippedPassives.Where(p =>
            p.Level >= 5 && p.TotalEvolutionTier < p.Level / 5 && p.TotalEvolutionTier < 4 && passives.CanEvolveAnyPath(p)).ToList();

        if (readySkills.Count == 0 && readyPassives.Count == 0)
        {
            Show();
            return;
        }

        if (readySkills.Count + readyPassives.Count == 1)
        {
            if (readySkills.Count == 1) EvolutionTreeUI.Instance.Show(skills, readySkills[0]);
            else EvolutionTreeUI.Instance.Show(passives, readyPassives[0]);
            return;
        }

        List<Option> pickOptions = new List<Option>();
        foreach (EquippedSkill s in readySkills)
        {
            EquippedSkill captured = s;
            pickOptions.Add(new Option
            {
                Title = PlayerSkills.GetActiveSkillName(captured.Id),
                LevelText = "진화 가능",
                Description = "어떤 스킬을 먼저 진화시킬지 선택하세요",
                Icon = GetIcon(activeIcons, (int)captured.Id),
                Apply = () => EvolutionTreeUI.Instance.Show(skills, captured),
            });
        }
        foreach (EquippedPassive p in readyPassives)
        {
            EquippedPassive captured = p;
            pickOptions.Add(new Option
            {
                Title = PlayerSkills.GetPassiveSkillName(captured.Id),
                LevelText = "진화 가능",
                Description = "어떤 패시브를 먼저 진화시킬지 선택하세요",
                Icon = GetIcon(passiveIcons, (int)captured.Id),
                Apply = () => EvolutionTreeUI.Instance.Show(passives, captured),
            });
        }

        Shuffle(pickOptions);
        pickOptions = pickOptions.Take(3).ToList();

        while (pickOptions.Count < 3)
            pickOptions.Add(new Option { Title = "체력 강화", Description = "최대 체력 +20", Apply = () => FindAnyObjectByType<PlayerHealth>().IncreaseMaxHealth(20) });

        rerollable = false; // 진화 대상 선택 모달은 리롤 불가
        currentOptions = pickOptions.ToArray();
        ShowOptions();
    }

    private static Sprite GetIcon(Sprite[] icons, int index) =>
        icons != null && index >= 0 && index < icons.Length ? icons[index] : null;

    // 다른 UI(일시정지 요약 등)가 스킬 아이콘을 재사용할 수 있도록 노출.
    public Sprite GetActiveIcon(ActiveSkillId id) => GetIcon(activeIcons, (int)id);
    public Sprite GetPassiveIcon(PassiveSkillId id) => GetIcon(passiveIcons, (int)id);

    private static string GetActiveSkillDescription(ActiveSkillId id) => id switch
    {
        ActiveSkillId.Whirlwind => "적을 자동으로 추적하는 회오리를 소환(지속시간 4초). 닿아있는 동안 지속 피해를 주며, 피해를 주는 동안 이동 속도가 느려짐",
        ActiveSkillId.Orb => "전방으로 관통하며 나아가는 오브를 소환. 닿아있는 모든 적에게 지속 피해를 주고 느려지게 함",
        ActiveSkillId.Lightning => "6초간 공격 피해를 입는 모든 적들에게 30% 확률로 낙뢰가 떨어져 피해",
        ActiveSkillId.EagleDrop => "화면 전체에 독수리를 1초 간격으로 3회 투하해 모든 적에게 피해",
        ActiveSkillId.Sniping => "가장 체력이 높은 적을 5회 저격해 큰 피해를 줌",
        ActiveSkillId.Homing => "적을 추적하는 미사일 5개를 발사. 사용할수록 미사일이 강해짐(이번 판 한정)",
        ActiveSkillId.Shotgun => "5초 동안 모든 스킬의 공격 횟수가 1회 증가",
        ActiveSkillId.Rewind => "다른 모든 스킬의 재사용 대기시간을 1초 앞당김. 레벨업할수록 더 크게 되감음",
        _ => "",
    };

    private static string GetPassiveSkillDescription(PassiveSkillId id) => PlayerPassives.DescribePassiveAcquire(id);

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
