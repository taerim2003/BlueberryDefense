using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class LevelUpUI : MonoBehaviour
{
    public static LevelUpUI Instance { get; private set; }

    private class Option
    {
        public string Title;
        public string LevelText;
        public bool IsNew;
        public bool IsEvolution;      // 진화 아이템 모달의 카드 — 주황 아웃라인 + "진화!" 태그
        public string Description;
        public Sprite Icon;
        public System.Action Apply;
        // 진화 연쇄(레벨업 후 진화창)·보물상자 에스컬레이션이 대상 스킬/패시브를 식별하기 위한 정보
        public ActiveSkillId? SkillId;
        public PassiveSkillId? PassiveId;
        public bool IsEssence;
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

    [SerializeField] private TMP_Text headerText;        // 패널 제목(일반=레벨 업, 보물=보물 획득)
    [SerializeField] private GameObject[] treasureDecor; // 보물 모드에서만 켜지는 장식(정수 비·보물상자)

    private static readonly Color TreasureHeaderColor = new Color(1f, 0.82f, 0.2f, 1f);
    private const string TreasureHeader = "보물 획득!";
    private const string EvolutionHeader = "진화!";
    private const string EvolutionFallbackHeader = "진화할 스킬이 없다 — 대신 레벨업";
    private const int EvolutionFallbackLevels = 3; // 진화 대상이 없을 때 주는 대체 레벨업 수
    private string headerDefaultText;
    private Color headerDefaultColor;

    private static readonly Color NewTagColor = new Color(1f, 0.85f, 0.2f, 1f);
    private static readonly Color LevelTagColor = new Color(0.75f, 0.85f, 1f, 1f);
    private static readonly Color EvolveTagColor = new Color(1f, 0.55f, 0.1f, 1f); // 진화 가능 강조(주황)

    private Outline[] optionOutlines;

    private const int EssenceReward = 10; // 레벨업할 게 없을 때 대체로 지급하는 정수량

    private Option[] currentOptions;
    private int rerollsRemaining;  // 게임당 남은 리롤 횟수
    private bool rerollable;        // 이번 모달이 리롤 가능한가(진화 선택 모달은 불가)
    private bool treasureMode;      // 이번 모달이 보물상자 에스컬레이션 보상인가
    private bool evolutionMode;     // 이번 모달이 진화 아이템 보상인가

    // 모달이 열려 있는 동안 들어온 레벨업/보물상자 요청 — 닫힐 때 하나씩 이어서 띄운다.
    // (보물상자 블루베리 2마리를 연달아 먹으면 두 번째 보상이 첫 번째를 덮어써 사라지던 문제)
    private bool isOpen;
    private int pendingLevelUps;
    private int pendingTreasures;
    private int pendingEvolutions;

    private Button[] optionButtons;
    private TMP_Text[] optionLevelTexts;

    // 보물상자 에스컬레이션: 0.3초 간격으로 다음 레벨(최대 4)까지 강화될 확률
    private const float TreasureEscalateChance = 0.6f;
    private const int TreasureMaxLevels = 4;

    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);

        if (headerText != null)
        {
            headerDefaultText = headerText.text;
            headerDefaultColor = headerText.color;
        }
        SetTreasureDecor(false);

        optionButtonA.onClick.AddListener(() => Choose(0));
        optionButtonB.onClick.AddListener(() => Choose(1));
        optionButtonC.onClick.AddListener(() => Choose(2));
        if (rerollButton != null) rerollButton.onClick.AddListener(OnReroll);

        // 진화 가능 레벨업 강조용 셀 아웃라인(기본 꺼짐)
        optionOutlines = new[] { MakeOutline(optionButtonA), MakeOutline(optionButtonB), MakeOutline(optionButtonC) };

        optionButtons = new[] { optionButtonA, optionButtonB, optionButtonC };
        optionLevelTexts = new[] { levelA, levelB, levelC };
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
        if (isOpen) { pendingLevelUps++; return; }
        ShowLevelUp();
    }

    private void ShowLevelUp()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        rerollable = true;
        treasureMode = false;
        SetTreasureDecor(false);
        currentOptions = BuildOptions(skills, passives);
        ShowOptions();
    }

    // 보물 모드 장식(정수 비·보물상자)과 패널 제목을 일반/보물에 맞게 전환.
    // 배열 앞쪽부터 켜지므로 보물상자를 정수 비보다 먼저 두면 정수 비 OnEnable에서 상자 연출을 안전하게 건다.
    private void SetTreasureDecor(bool on)
    {
        if (treasureDecor != null)
            foreach (GameObject go in treasureDecor)
                if (go != null) go.SetActive(on);

        if (headerText != null)
        {
            headerText.text = on ? TreasureHeader : headerDefaultText;
            headerText.color = on ? TreasureHeaderColor : headerDefaultColor;
        }
    }

    // 밀려 있던 보상을 하나 이어서 연다(보물상자 우선). 닫힘 연출(UITransition.Hide)이 끝난 뒤에
    // 열어야 연출이 새 모달을 다시 꺼버리지 않는다.
    private void QueueNextPending()
    {
        if (pendingEvolutions <= 0 && pendingTreasures <= 0 && pendingLevelUps <= 0) return;
        StartCoroutine(ShowNextPendingWhenClosed());
    }

    private IEnumerator ShowNextPendingWhenClosed()
    {
        yield return new WaitWhile(() => panel.activeSelf);

        if (pendingEvolutions > 0) { pendingEvolutions--; ShowEvolution(); }
        else if (pendingTreasures > 0) { pendingTreasures--; ShowTreasure(); }
        else if (pendingLevelUps > 0) { pendingLevelUps--; ShowLevelUp(); }
    }

    private void OnReroll()
    {
        if (!rerollable || rerollsRemaining <= 0) return;
        rerollsRemaining--;
        ShowLevelUp(); // 후보 재구성 + 재셔플 (이미 열려 있으므로 대기열을 거치지 않는다)
    }

    private void ShowOptions()
    {
        bool alreadyOpen = isOpen;

        // 선택지는 1~3개로 가변 — 남는 슬롯의 옵션 카드(버튼)는 통째로 숨긴다.
        // 슬롯은 씬에 y=+190/0/-190으로 고정 배치돼 있어서, 1개짜리는 가운데 슬롯에 넣어야 덩그러니 위에 붙지 않는다.
        Option[] slots = new Option[3];
        for (int i = 0; i < currentOptions.Length && i < slots.Length; i++)
            slots[SlotForOption(i)] = currentOptions[i];

        SetSlot(0, optionButtonA, titleA, levelA, descA, iconA, slots[0]);
        SetSlot(1, optionButtonB, titleB, levelB, descB, iconB, slots[1]);
        SetSlot(2, optionButtonC, titleC, levelC, descC, iconC, slots[2]);

        UpdateRerollButton();

        // 닫힘 연출이 아직 돌고 있으면 panel.activeSelf가 true라 SetActive(true)로는 다시 열리지 않는다
        // (연출이 끝나며 패널을 꺼버려 '보이지 않는 모달 + timeScale 0' 상태가 됨) — Show()로 연출을 되돌린다.
        if (panelTransition != null) panelTransition.Show();
        else panel.SetActive(true);
        if (!alreadyOpen) ModalPause.Push();
        isOpen = true;
    }

    private void SetSlot(int index, Button button, TMP_Text title, TMP_Text level, TMP_Text desc, Image icon, Option option)
    {
        if (button != null)
        {
            button.gameObject.SetActive(option != null);
            button.interactable = true; // 보물 에스컬레이션에서 껐던 상호작용 복구
        }
        if (optionOutlines != null && optionOutlines[index] != null)
            optionOutlines[index].enabled = option != null && option.IsEvolution;
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
            else if (option.IsEvolution)
            {
                level.text = "진화! " + option.LevelText;
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
                if (!MetaBonuses.SkillUnlockedForRun(id)) continue; // 스킬트리 해금 게이팅
                ActiveSkillId captured = id;
                candidates.Add(new Option
                {
                    Title = PlayerSkills.GetActiveSkillTitleWithTags(captured),
                    IsNew = true,
                    Description = GetActiveSkillDescription(captured),
                    Icon = GetIcon(activeIcons, (int)captured),
                    Apply = () => skills.AcquireSkill(captured),
                    SkillId = captured,
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
                    Title = PlayerSkills.GetPassiveSkillTitleWithTags(captured),
                    IsNew = true,
                    Description = GetPassiveSkillDescription(captured),
                    Icon = GetIcon(passiveIcons, (int)captured),
                    Apply = () => passives.AcquirePassive(captured),
                    PassiveId = captured,
                });
            }
        }

        foreach (EquippedSkill equipped in skills.EquippedSkills)
        {
            if (!skills.CanUpgradeSkill(equipped)) continue;
            EquippedSkill captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetActiveSkillTitleWithTags(captured),
                LevelText = "레벨: " + (captured.Level + 1),
                Description = PlayerSkills.DescribeUpgradeEffect(captured, captured.Level + 1),
                Icon = GetIcon(activeIcons, (int)captured.Id),
                Apply = () => skills.UpgradeSkillLevel(captured.Id),
                SkillId = captured.Id,
            });
        }

        foreach (EquippedPassive equipped in passives.EquippedPassives)
        {
            if (!passives.CanUpgradePassive(equipped)) continue;
            EquippedPassive captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetPassiveSkillTitleWithTags(captured),
                LevelText = "레벨: " + (captured.Level + 1),
                Description = PlayerPassives.DescribePassiveLevelEffect(captured.Id),
                Icon = GetIcon(passiveIcons, (int)captured.Id),
                Apply = () => passives.UpgradePassiveLevel(captured.Id),
                PassiveId = captured.Id,
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
        IsEssence = true,
    };

    // 보물상자 블루베리 보상: 일반 레벨업과 동일한 3지선다를 제시하되, 하나를 고르면
    // 0.3초 간격으로 보상이 랜덤하게 강화(최대 4업)되는 에스컬레이션 연출로 파워 스파이크를 준다.
    // (진화는 더 이상 보물상자가 아니라 '진화 가능 레벨 도달' 시 즉시 열린다.)
    public void ShowTreasureReward()
    {
        // 보물상자 블루베리는 경험치가 커서 Enemy.Die가 ShowTreasureReward보다 먼저 AddXP를 호출하면
        // 같은 프레임에 '일반 레벨업' 모달이 먼저 열려 버린다. 그 상태로 대기열에 넣으면 플레이어는
        // 에스컬레이션 없는 평범한 카드부터 고르게 된다(= 보물 보너스가 안 터지는 것처럼 보임).
        // 그래서 아직 아무것도 안 고른 레벨업 모달은 뒤로 미루고 보물 보상을 먼저 띄운다.
        if (isOpen)
        {
            if (treasureMode) { pendingTreasures++; return; } // 보물 모달이 이미 떠 있으면 순서대로
            pendingLevelUps++;
        }
        ShowTreasure();
    }

    private void ShowTreasure()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        rerollable = false;   // 보물상자 보상은 리롤 불가
        treasureMode = true;
        evolutionMode = false;
        SetTreasureDecor(true);
        currentOptions = BuildOptions(skills, passives);
        ShowOptions();
    }

    // ── 진화 아이템 보상 ────────────────────────────────────────────────────
    // 벽 스테이지 엘리트가 떨군 진화 아이템을 먹으면 열린다. 진화 가능한 스킬/패시브 중
    // 3개를 제시하고, 고르면 진화 트리(루트 선택)로 이어진다.
    // 진화 가능한 게 하나도 없으면 대신 "고른 스킬 3레벨업"을 준다(아이템이 버려지지 않도록).
    public void ShowEvolutionReward()
    {
        if (isOpen) { pendingEvolutions++; return; }
        ShowEvolution();
    }

    private void ShowEvolution()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        rerollable = false;   // 진화 선택은 리롤 불가
        treasureMode = false;
        SetTreasureDecor(false);
        currentOptions = BuildEvolutionOptions(skills, passives);
        evolutionMode = true;
        if (headerText != null)
        {
            headerText.text = currentOptions.Length > 0 && currentOptions[0].IsEvolution ? EvolutionHeader : EvolutionFallbackHeader;
            headerText.color = EvolveTagColor;
        }
        ShowOptions();
    }

    private Option[] BuildEvolutionOptions(PlayerSkills skills, PlayerPassives passives)
    {
        List<Option> candidates = new List<Option>();

        foreach (EquippedSkill equipped in skills.EquippedSkills)
        {
            if (!skills.CanEvolve(equipped)) continue;
            EquippedSkill captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetActiveSkillTitleWithTags(captured),
                LevelText = (captured.EvolutionStage + 1) + "차 진화",
                IsEvolution = true,
                Description = DescribeEvolutionChoice(skills, captured),
                Icon = GetIcon(activeIcons, (int)captured.Id),
                SkillId = captured.Id,
            });
        }

        foreach (EquippedPassive equipped in passives.EquippedPassives)
        {
            if (!passives.CanEvolve(equipped)) continue;
            EquippedPassive captured = equipped;
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetPassiveSkillTitleWithTags(captured),
                LevelText = (captured.EvolutionStage + 1) + "차 진화",
                IsEvolution = true,
                Description = DescribeEvolutionChoice(passives, captured),
                Icon = GetIcon(passiveIcons, (int)captured.Id),
                PassiveId = captured.Id,
            });
        }

        // 진화 대상이 없으면 대체 보상(레벨업)인데, 이건 고를 게 없는 보상이라 3지선다로 낼 이유가 없다.
        // 랜덤으로 하나만 뽑아 가운데에 한 장 띄우고 누르게 한다.
        if (candidates.Count == 0)
        {
            List<Option> fallback = BuildEvolutionFallbackOptions(skills, passives);
            Shuffle(fallback);
            return fallback.Take(1).ToArray();
        }

        Shuffle(candidates);
        return candidates.Take(3).ToArray();
    }

    // 진화 대상이 없을 때의 대체 보상: 아무 스킬/패시브 하나를 골라 3레벨업.
    // 만렙에 걸린 대상은 올릴 자리가 없으니 후보에서 빼고, 남은 여유분만큼만 표기한다.
    private List<Option> BuildEvolutionFallbackOptions(PlayerSkills skills, PlayerPassives passives)
    {
        List<Option> candidates = new List<Option>();

        foreach (EquippedSkill equipped in skills.EquippedSkills)
        {
            if (!skills.CanUpgradeSkill(equipped)) continue;
            EquippedSkill captured = equipped;
            int target = Mathf.Min(captured.Level + EvolutionFallbackLevels, BalanceConstants.MaxSkillLevel);
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetActiveSkillTitleWithTags(captured),
                LevelText = $"레벨: {captured.Level} → {target}",
                Description = $"{target - captured.Level}레벨 즉시 상승",
                Icon = GetIcon(activeIcons, (int)captured.Id),
                Apply = () => { for (int i = 0; i < EvolutionFallbackLevels; i++) skills.UpgradeSkillLevel(captured.Id); },
                SkillId = captured.Id,
            });
        }

        foreach (EquippedPassive equipped in passives.EquippedPassives)
        {
            if (!passives.CanUpgradePassive(equipped)) continue;
            EquippedPassive captured = equipped;
            int target = Mathf.Min(captured.Level + EvolutionFallbackLevels, BalanceConstants.MaxSkillLevel);
            candidates.Add(new Option
            {
                Title = PlayerSkills.GetPassiveSkillTitleWithTags(captured),
                LevelText = $"레벨: {captured.Level} → {target}",
                Description = $"{target - captured.Level}레벨 즉시 상승",
                Icon = GetIcon(passiveIcons, (int)captured.Id),
                Apply = () => { for (int i = 0; i < EvolutionFallbackLevels; i++) passives.UpgradePassiveLevel(captured.Id); },
                PassiveId = captured.Id,
            });
        }

        if (candidates.Count == 0) candidates.Add(EssenceOption());
        return candidates;
    }

    // 1차 진화 카드는 "고를 수 있는" 루트만 나열한다 — 연계 스킬이 없어 잠긴 루트는 이름조차 안 보여준다.
    private static string DescribeEvolutionChoice(PlayerSkills skills, EquippedSkill s)
    {
        if (s.EvolutionStage > 0) return $"→ {EvolutionRoutes.EvolvedName(s.Id, s.Route, 2)}";
        string[] names = PlayerSkills.SelectableRoutes(s)
            .Where(r => skills.IsRouteUnlocked(s.Id, r))
            .Select(r => EvolutionRoutes.EvolvedName(s.Id, r, 1))
            .ToArray();
        return "루트 선택: " + string.Join(" / ", names);
    }

    private static string DescribeEvolutionChoice(PlayerPassives passives, EquippedPassive p)
    {
        if (p.EvolutionStage > 0) return $"→ {EvolutionRoutes.EvolvedName(p.Id, p.Route, 2)}";
        string[] names = PlayerPassives.SelectableRoutes(p)
            .Where(r => passives.IsRouteUnlocked(p.Id, r))
            .Select(r => EvolutionRoutes.EvolvedName(p.Id, r, 1))
            .ToArray();
        return "루트 선택: " + string.Join(" / ", names);
    }

    // 진화 카드를 고른 뒤: 진화면 트리 창으로, 대체 보상이면 곧바로 적용.
    private IEnumerator ResolveEvolutionChoice(Option opt)
    {
        yield return new WaitWhile(() => panel.activeSelf); // 닫힘 연출이 끝나야 다음 모달이 안 꺼진다

        if (opt.IsEvolution)
        {
            PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
            PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();
            bool done = false;

            if (opt.SkillId.HasValue)
            {
                EquippedSkill s = skills.EquippedSkills.FirstOrDefault(x => x.Id == opt.SkillId.Value);
                if (s != null) EvolutionTreeUI.Instance.Show(skills, s, () => done = true); else done = true;
            }
            else if (opt.PassiveId.HasValue)
            {
                EquippedPassive p = passives.GetPassive(opt.PassiveId.Value);
                if (p != null) EvolutionTreeUI.Instance.Show(passives, p, () => done = true); else done = true;
            }
            else done = true;

            yield return new WaitUntil(() => done);
        }
        else
        {
            opt.Apply?.Invoke();
        }

        QueueNextPending();
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

    // 선택지가 1개뿐일 때만 슬롯과 옵션 인덱스가 어긋난다(옵션 0 → 가운데 슬롯 1).
    private int SlotForOption(int optionIndex) => currentOptions.Length == 1 ? 1 : optionIndex;
    private int OptionForSlot(int slot) => currentOptions.Length == 1 ? 0 : slot;

    private void Choose(int slot)
    {
        int index = OptionForSlot(slot);
        if (index < 0 || index >= currentOptions.Length) return;
        Option opt = currentOptions[index];

        if (treasureMode)
        {
            treasureMode = false;
            StartCoroutine(TreasureEscalateRoutine(slot, opt)); // 셀 연출은 슬롯 기준
            return;
        }

        if (evolutionMode)
        {
            evolutionMode = false;
            Close();
            StartCoroutine(ResolveEvolutionChoice(opt));
            return;
        }

        opt.Apply?.Invoke();
        Close();
        QueueNextPending();
    }

    // 보물상자 에스컬레이션: 고른 선택지 셀만 남기고, 0.3초 간격으로 랜덤하게 레벨업 수치를 강화(최대 4업).
    // 강화될 때마다 바운스 + "Bonus!". 확정되면 보상을 적용하고(5배수 넘으면 진화창 연쇄) 모달을 닫는다.
    private IEnumerator TreasureEscalateRoutine(int index, Option opt)
    {
        // 고른 셀만 남기고 나머지 숨김 + 중복 클릭 방지
        for (int i = 0; i < optionButtons.Length; i++)
        {
            if (optionButtons[i] == null) continue;
            optionButtons[i].interactable = false;
            if (i != index) optionButtons[i].gameObject.SetActive(false);
        }

        RectTransform cell = (RectTransform)optionButtons[index].transform;
        int levels = 1;
        while (levels < TreasureMaxLevels)
        {
            yield return new WaitForSecondsRealtime(0.3f);
            if (Random.value >= TreasureEscalateChance) break;

            levels++;
            cell.DOKill();
            cell.localScale = Vector3.one;
            cell.DOPunchScale(Vector3.one * 0.35f, 0.3f, 8, 0.6f).SetUpdate(true);
            if (optionLevelTexts[index] != null)
            {
                optionLevelTexts[index].text = $"Bonus!  {levels}업!";
                optionLevelTexts[index].color = EvolveTagColor;
            }
        }

        yield return new WaitForSecondsRealtime(0.5f); // 결과 여운

        yield return ApplyTreasureReward(opt, levels);

        // 보물 모달 종료 (ShowOptions에서 Push한 참조 1개 해제)
        isOpen = false;
        ModalPause.Pop();
        if (panelTransition != null) panelTransition.Hide();
        else panel.SetActive(false);

        QueueNextPending();
    }

    // 고른 보상을 levels만큼 부여. 정수는 배수로, 그 외에는 첫 적용(획득/1레벨업) 후 나머지를 연쇄 레벨업.
    private IEnumerator ApplyTreasureReward(Option opt, int levels)
    {
        if (opt.IsEssence)
        {
            MetaRun.Collect(EssenceReward * levels);
            yield break;
        }

        opt.Apply?.Invoke(); // 신규 스킬 획득 또는 첫 레벨업

        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();
        if (opt.SkillId.HasValue)
            yield return GrantSkillLevels(skills, opt.SkillId.Value, levels - 1);
        else if (opt.PassiveId.HasValue)
            yield return GrantPassiveLevels(passives, opt.PassiveId.Value, levels - 1);
    }

    // 스킬/패시브를 count번 레벨업. (진화는 아이템 전용이 되어 더 이상 레벨업 도중에 끼어들지 않는다)
    private static IEnumerator GrantSkillLevels(PlayerSkills skills, ActiveSkillId id, int count)
    {
        for (int i = 0; i < count; i++) skills.UpgradeSkillLevel(id);
        yield break;
    }

    private static IEnumerator GrantPassiveLevels(PlayerPassives passives, PassiveSkillId id, int count)
    {
        for (int i = 0; i < count; i++) passives.UpgradePassiveLevel(id);
        yield break;
    }

    private void Close()
    {
        isOpen = false;
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
