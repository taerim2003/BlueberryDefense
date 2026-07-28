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
        public bool UnlocksEvolution; // 다음 레벨이 5의 배수 → 진화 해금 가능 레벨업
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

    // 모달이 열려 있는 동안 들어온 레벨업/보물상자 요청 — 닫힐 때 하나씩 이어서 띄운다.
    // (보물상자 블루베리 2마리를 연달아 먹으면 두 번째 보상이 첫 번째를 덮어써 사라지던 문제)
    private bool isOpen;
    private int pendingLevelUps;
    private int pendingTreasures;

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
        if (pendingTreasures <= 0 && pendingLevelUps <= 0) return;
        StartCoroutine(ShowNextPendingWhenClosed());
    }

    private IEnumerator ShowNextPendingWhenClosed()
    {
        yield return new WaitWhile(() => panel.activeSelf);

        if (pendingTreasures > 0) { pendingTreasures--; ShowTreasure(); }
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
        int count = currentOptions.Length;
        SetSlot(0, optionButtonA, titleA, levelA, descA, iconA, count > 0 ? currentOptions[0] : null);
        SetSlot(1, optionButtonB, titleB, levelB, descB, iconB, count > 1 ? currentOptions[1] : null);
        SetSlot(2, optionButtonC, titleC, levelC, descC, iconC, count > 2 ? currentOptions[2] : null);

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
                Title = PlayerSkills.GetActiveSkillTitleWithTags(captured.Id),
                LevelText = "레벨: " + (captured.Level + 1),
                UnlocksEvolution = (captured.Level + 1) % 5 == 0,
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
                Title = PlayerSkills.GetPassiveSkillTitleWithTags(captured.Id),
                LevelText = "레벨: " + (captured.Level + 1),
                UnlocksEvolution = (captured.Level + 1) % 5 == 0,
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
        SetTreasureDecor(true);
        currentOptions = BuildOptions(skills, passives);
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
        Option opt = currentOptions[index];

        if (treasureMode)
        {
            treasureMode = false;
            StartCoroutine(TreasureEscalateRoutine(index, opt));
            return;
        }

        opt.Apply?.Invoke();
        Close();
        // 진화창이 열리면 그게 닫힌 뒤에, 아니면 곧바로 밀려 있던 보상을 이어서 연다.
        if (!(opt.UnlocksEvolution && OpenEvolutionAfterLevelUp(opt, QueueNextPending)))
            QueueNextPending();
    }

    // (2A) 레벨업으로 5의 배수 레벨(진화 해금 레벨)에 도달하면 곧바로 진화창을 연다. 열었으면 true.
    private bool OpenEvolutionAfterLevelUp(Option opt, System.Action closed)
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        if (opt.SkillId.HasValue)
        {
            EquippedSkill s = skills.EquippedSkills.FirstOrDefault(x => x.Id == opt.SkillId.Value);
            if (s != null && !skills.CanUpgradeSkill(s) && s.TotalEvolutionTier < 4 && skills.CanEvolveAnyPath(s))
            {
                EvolutionTreeUI.Instance.Show(skills, s, closed);
                return true;
            }
        }
        else if (opt.PassiveId.HasValue)
        {
            EquippedPassive p = passives.GetPassive(opt.PassiveId.Value);
            if (p != null && !passives.CanUpgradePassive(p) && p.TotalEvolutionTier < 4 && passives.CanEvolveAnyPath(p))
            {
                EvolutionTreeUI.Instance.Show(passives, p, closed);
                return true;
            }
        }

        return false;
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

    // 스킬을 count번 레벨업. 5배수 게이트에 걸리면 진화창을 열고 닫힐 때까지 대기 후 계속.
    private IEnumerator GrantSkillLevels(PlayerSkills skills, ActiveSkillId id, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return ResolveSkillEvolution(skills, id);
            EquippedSkill s = skills.EquippedSkills.FirstOrDefault(x => x.Id == id);
            if (s == null || !skills.CanUpgradeSkill(s)) break;
            skills.UpgradeSkillLevel(id);
        }
        yield return ResolveSkillEvolution(skills, id); // 마지막 레벨이 5배수면 진화창
    }

    private IEnumerator GrantPassiveLevels(PlayerPassives passives, PassiveSkillId id, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return ResolvePassiveEvolution(passives, id);
            EquippedPassive p = passives.GetPassive(id);
            if (p == null || !passives.CanUpgradePassive(p)) break;
            passives.UpgradePassiveLevel(id);
        }
        yield return ResolvePassiveEvolution(passives, id);
    }

    private IEnumerator ResolveSkillEvolution(PlayerSkills skills, ActiveSkillId id)
    {
        while (true)
        {
            EquippedSkill s = skills.EquippedSkills.FirstOrDefault(x => x.Id == id);
            if (s == null || skills.CanUpgradeSkill(s)) yield break;      // 게이트 없음
            if (s.TotalEvolutionTier >= 4 || !skills.CanEvolveAnyPath(s)) yield break; // 더 진화 불가
            bool done = false;
            EvolutionTreeUI.Instance.Show(skills, s, () => done = true);
            yield return new WaitUntil(() => done);
        }
    }

    private IEnumerator ResolvePassiveEvolution(PlayerPassives passives, PassiveSkillId id)
    {
        while (true)
        {
            EquippedPassive p = passives.GetPassive(id);
            if (p == null || passives.CanUpgradePassive(p)) yield break;
            if (p.TotalEvolutionTier >= 4 || !passives.CanEvolveAnyPath(p)) yield break;
            bool done = false;
            EvolutionTreeUI.Instance.Show(passives, p, () => done = true);
            yield return new WaitUntil(() => done);
        }
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
