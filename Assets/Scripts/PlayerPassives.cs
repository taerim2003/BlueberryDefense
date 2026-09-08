using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum PassiveSkillId
{
    Strength,
    Health,
    Knowledge,
    Assassinate,
    // 🚫 폐지(2026-08-06) — 진화 조건표 개편에서 빠지고 역할이 Accel(가속)로 넘어갔다.
    //    레벨업 후보 배열(LevelUpUI)에서만 뺐고 enum·에셋은 남긴다: 정수 직렬화라 지우면 뒤 값이 밀린다.
    //    ⚠️ 그래서 아래 switch들의 Refresh 분기는 **죽은 코드가 아니다** — 에디터 치트 창(CheatWindow)이
    //       enum 전체를 순회해 획득 버튼을 만들기 때문에 그쪽으로는 여전히 도달한다. 지우지 말 것.
    Refresh,
    // ↓ 아래는 뒤에만 추가할 것 — Passive_* 에셋이 이 enum을 정수로 직렬화해 두어서 중간에 끼우면 값이 밀린다.
    Defense,
    Accel,
}

public class EquippedPassive
{
    public PassiveSkillId Id;
    public int Level = 1;

    // 진화 효과 저장소(§EvolutionRoutes — 루트/티어를 여기로 번역해 넣는다).
    public readonly int[] PathTier = new int[3];

    public int TotalLevel = 1;      // 진화 리셋과 무관한 누적 레벨 — 표시용(진화 게이트는 표시 레벨 Level을 본다)
    public int EvolutionStage = 0;  // 0=미진화, 1=1차, 2=2차(최종)
    public int Route = -1;

    public string DisplayName =>
        EvolutionStage > 0 ? EvolutionRoutes.EvolvedName(Id, Route, EvolutionStage) : PlayerSkills.GetPassiveSkillName(Id);
}

public class PlayerPassives : MonoBehaviour
{
    private const int MaxPassives = 4;

    // 아래 값들은 진화/레벨업으로 계속 갱신되는 정적 상태 — 플레이어가 한 명뿐이라 LightningStorm과 같은 방식으로 관리한다.
    // 치명타 확률·재사용 초기화 확률의 '기본값'은 PassiveProgression(SO)이 소유 → 획득 시 적용하므로 0에서 시작하고 Awake에서 판마다 리셋.
    public static float AssassinateCritChance = 0f;
    public static float AssassinateCritMultiplier = 3f;
    public static float RefreshChance = 0f;
    public static float AssassinateKillXpMultiplier = 1f; // 암살 연계 path1: 치명타 처치 시 경험치 배율
    public static float RefreshHealOnResetAmount = 0f; // 리프레쉬 연계 path1: 쿨타임 초기화시 회복량
    public static float RefreshLightningCooldownProcChance = 0f; // 리프레쉬 연계 path3 T2+: 낙뢰 발동시 전체 쿨타임 감소 확률
    public static float BuffSkillCooldownMult = 1f; // 리프레쉬 연계 path3 T1: 버프류 스킬(산탄·낙뢰) 쿨타임 감소 배율
    // 피격 시 받은 피해의 이 배수를 전체 적에게 되돌려준다.
    // ⚠️ 예전엔 건강 path2("가시 갑주")가 이걸 켰지만, 2026-08-06 명세에서 **방어 path2**로 옮겨졌다
    //    (건강 path2는 하트 드랍으로 교체). 소비처는 HandleDamageTaken 한 곳뿐이라 필드는 그대로 쓴다.
    public static float HealthRetaliationMultiplier = 0f;
    // 힘 연계 path2("완력/괴력"): **Q키에 할당된 스킬** 전용 추가 피해 배율.
    // ⚠️ 예전엔 기본공격(화살 쏘기) 고정이었는데 2026-08-07 명세대로 슬롯 기준으로 바꿨다.
    //    Q는 슬롯 이름이 아니라 **가장 먼저 얻은 스킬**에 붙는다(PlayerSkills.AcquireSkill) —
    //    화살 쏘기로 시작하지 않는 캐릭터(파인애플=휘두르기)에선 대상이 달라진다.
    public static float FirstSlotDamageMultiplierBonus = 0f;
    // 지식 R1 「전투 통찰」: **호밍 미사일로 처치한** 적의 경험치 배율. Enemy가 처치 시 읽는다.
    // ⚠️ 예전엔 "독수리 투하 시전마다 즉시 XP"(EagleDropCastXpBonus)였다 — 2026-09-08 문구 개정으로 통째로 교체.
    public static float HomingKillXpMultiplier = 1f;
    // 방어: 받는 피해 감소 비율(0~1). PlayerHealth.TakeDamage가 읽는다.
    // 건강(최대체력)과 역할이 다르다 — 이쪽은 들어오는 피해 자체를 깎는다.
    public static float DamageReduction = 0f;
    // 가속: 전 스킬 쿨타임 감소 비율(0~1). 방어(DamageReduction)와 같은 가산 방식이다.
    // PlayerSkills가 쿨을 걸 때 (1 - 이 값)을 곱한다 — 스킬트리 MetaBonuses.CooldownMult와 같은 축이라
    // 둘이 곱해져 들어가고, 최종 하한은 GlobalCooldown(0.4초).
    public static float AccelCooldownReduction = 0f;
    // 방어 연계 path1(휘두르기): 피격 시 휘두르기를 쿨과 무관하게 자동 발동. 값은 본체 피해 대비 배율(0=미보유).
    public static float DefenseAutoSwingDamageMult = 0f;
    // 가속 연계 path2(방어): 피격할 때마다 모든 스킬 쿨타임을 이 초만큼 앞당긴다(0=미보유).
    public static float AccelCooldownCutOnHit = 0f;
    // 건강 연계 path2(오브): 하트(체력회복) 드랍 확률 배율. Enemy가 처치 시 읽는다.
    public static float HeartDropMultiplier = 1f;
    // 암살 R1 「필중 암살」: 치명타 확률 **상한**을 이 값으로 연다(0=미보유 → PlayerSkills.MaxCritChance 70%가 그대로).
    // ⚠️ 예전엔 "긴 쿨 스킬은 확정 치명타"(AssassinateSlowSkillCritCooldown)였다 — 2026-09-08 문구 개정으로 교체.
    public static float CritChanceCapOverride = 0f;

    // ── 스킬트리 패시브 강화 중 **패시브 스탯 밖에서 동작하는 것** (2026-09-03 재설계) ──
    // 트리 노드를 찍었어도 **그 패시브를 실제로 얻어야** 켜진다(액티브 강화가 그 스킬을 얻어야만
    // 의미가 있는 것과 같은 원칙). 스위치는 AcquirePassive에서 올라간다.
    public static bool HealItemDouble = false;   // 건강: 체력회복템 회복량 2배 — HeartPickup이 읽는다
    public static bool ReviveOnce = false;       // 방어: 사망 시 1회 부활 — PlayerHealth가 읽고 소비한다
    public static bool FullCritExtraHit = false; // 암살: 치명타 100% 스킬은 타수 +1 — Enemy.TakeSkillHit가 읽는다
    public static bool ShowEvolutionHint = false;// 지식: 레벨업 카드에 진화 조건 표시 — LevelUpUI가 읽는다

    [SerializeField] private PassiveProgression[] progressions; // 패시브별 기본값+레벨업당 상승값(Tier A). 미할당 패시브는 코드 기본값 폴백(=현행)

    private static System.Collections.Generic.Dictionary<PassiveSkillId, PassiveProgression> progressionLookup;

    private readonly List<EquippedPassive> equippedPassives = new List<EquippedPassive>();
    private PlayerSkills skills;
    private PlayerHealth health;

    public bool HasMaxPassives => equippedPassives.Count >= MaxPassives;
    public IReadOnlyList<EquippedPassive> EquippedPassives => equippedPassives;

    public bool HasPassive(PassiveSkillId id) => equippedPassives.Any(p => p.Id == id);
    public EquippedPassive GetPassive(PassiveSkillId id) => equippedPassives.FirstOrDefault(p => p.Id == id);

    private void Awake()
    {
        skills = GetComponent<PlayerSkills>();
        health = GetComponent<PlayerHealth>();
        if (health != null) health.OnDamageTaken += HandleDamageTaken;
        LightningStorm.OnProc += HandleLightningProc;

        BuildProgressionLookup();   // 정적 조회맵 승격 — AcquirePassive 전에 세팅
        ResetRunState();            // 판마다 리셋 — 기본값은 획득 시 SO에서 채워짐
    }

    private void OnDestroy()
    {
        // 구독을 풀지 않으면 판이 바뀔 때마다 죽은 인스턴스의 핸들러가 계속 쌓인다.
        if (health != null) health.OnDamageTaken -= HandleDamageTaken;
        LightningStorm.OnProc -= HandleLightningProc;
    }

    // 이 판에서만 유효한 static 효과 전부 초기화. 진화로 붙는 값이 대부분이라
    // 하나라도 빠지면 다음 판에 지난 판의 진화 효과가 남는다(RunState에서 호출).
    public static void ResetRunState()
    {
        AssassinateCritChance = 0f;
        AssassinateCritMultiplier = 3f;
        RefreshChance = 0f;
        AssassinateKillXpMultiplier = 1f;
        RefreshHealOnResetAmount = 0f;
        RefreshLightningCooldownProcChance = 0f;
        BuffSkillCooldownMult = 1f;
        HealthRetaliationMultiplier = 0f;
        DamageReduction = 0f;
        AccelCooldownReduction = 0f;
        DefenseAutoSwingDamageMult = 0f;
        AccelCooldownCutOnHit = 0f;
        HeartDropMultiplier = 1f;
        CritChanceCapOverride = 0f;
        FirstSlotDamageMultiplierBonus = 0f;
        HomingKillXpMultiplier = 1f;
        HealItemDouble = false;
        ReviveOnce = false;
        FullCritExtraHit = false;
        ShowEvolutionHint = false;
    }

    // 직렬화된 progressions[]를 id→SO 조회맵으로 승격. 미포함 패시브는 조회 실패 → 코드 기본값 폴백.
    private void BuildProgressionLookup()
    {
        progressionLookup = new System.Collections.Generic.Dictionary<PassiveSkillId, PassiveProgression>();
        if (progressions == null) return;
        foreach (var p in progressions)
            if (p != null) progressionLookup[p.passive] = p;
    }

    private static PassiveProgression Prog(PassiveSkillId id) =>
        progressionLookup != null && progressionLookup.TryGetValue(id, out var p) ? p : null;

    // 스킬트리 패시브 강화("기본 최대체력 +30" 등)는 **획득 시 값**에만 얹힌다 —
    // 레벨업 상승값(PerLevelBonus)은 건드리지 않는다. 그래서 레벨이 올라도 보너스는 한 번만 들어간다.
    public static float BaseValue(PassiveSkillId id)
    {
        PassiveProgression p = Prog(id);
        float b = p != null ? p.baseValue : PassiveProgression.DefaultBaseValue(id);
        return b + MetaBonuses.PassiveBaseBonus(id);
    }

    public static float PerLevelBonus(PassiveSkillId id)
    {
        PassiveProgression p = Prog(id);
        return p != null ? p.perLevelBonus : PassiveProgression.DefaultPerLevelBonus(id);
    }

    // 피격 반응 3종이 전부 여기 모인다(전부 "맞을 때마다" 발동하는 진화라 트리거가 같다).
    // ⚠️ 이 콜백은 오버힐(보호막)이 흡수하고 **남은 피해가 실제로 체력을 깎을 때만** 온다 —
    //    보호막으로 다 막은 타격은 반사도 자동 휘두르기도 안 나간다(의도, PlayerHealth.TakeDamage 참고).
    private void HandleDamageTaken(int amount)
    {
        // 방어 path2(건강 연계): 받은 피해의 배수를 전체 적에게 되돌려준다.
        if (HealthRetaliationMultiplier > 0f)
        {
            float damage = amount * HealthRetaliationMultiplier;
            foreach (Enemy enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                enemy.TakeDamage(damage);
        }

        // 가속 path2(방어 연계): 맞을 때마다 모든 스킬이 그만큼 빨리 돌아온다.
        if (AccelCooldownCutOnHit > 0f && skills != null)
            skills.ReduceAllCooldowns(AccelCooldownCutOnHit);

        // 방어 path1(휘두르기 연계): 맞으면 반사적으로 휘두른다.
        if (DefenseAutoSwingDamageMult > 0f && skills != null)
            skills.TriggerAutoSwing(DefenseAutoSwingDamageMult);
    }

    private void HandleLightningProc()
    {
        if (RefreshLightningCooldownProcChance <= 0f || skills == null) return;
        if (Random.value < RefreshLightningCooldownProcChance)
            skills.ReduceAllCooldowns(0.5f);
    }

    // 타격 기준 치명타: 각 데미지 이벤트(투사체 명중, 회오리/오브 틱 등)마다 개별적으로 굴린다.
    public static float ApplyCrit(float damage, float critChance, out bool isCrit)
    {
        isCrit = critChance > 0f && Random.value < critChance;
        // 스킬트리 "치명타 피해" 노드는 여기서 더한다 — AssassinateCritMultiplier 자체를 올리면
        // ResetRunState가 판마다 3f로 되돌려 놓아서 적용 순서에 따라 사라진다.
        return isCrit ? damage * (AssassinateCritMultiplier + MetaBonuses.CritDamageBonus) : damage;
    }

    public void AcquirePassive(PassiveSkillId id)
    {
        if (HasMaxPassives || HasPassive(id)) return;

        equippedPassives.Add(new EquippedPassive { Id = id });
        ApplyPassiveValue(id, BaseValue(id));
        ApplyTreeEnhancements(id);            // 스킬트리 패시브 강화 중 스탯 밖에서 도는 것들
        CollectionSave.DiscoverPassive(id);   // 컬렉션(도감) 발견 기록 — 판을 넘어 남는다
    }

    // 스킬트리 강화는 노드를 찍는 것만으론 안 켜진다 — 그 패시브를 얻는 순간 켜진다.
    private static void ApplyTreeEnhancements(PassiveSkillId id)
    {
        switch (id)
        {
            case PassiveSkillId.Health: if (MetaBonuses.HealItemDouble) HealItemDouble = true; break;
            case PassiveSkillId.Defense: if (MetaBonuses.DefenseRevive) ReviveOnce = true; break;
            case PassiveSkillId.Assassinate: if (MetaBonuses.AssassinFullCritExtraHit) FullCritExtraHit = true; break;
            case PassiveSkillId.Knowledge: if (MetaBonuses.ShowEvolutionHint) ShowEvolutionHint = true; break;
        }
    }

    // 만렙(MaxSkillLevel)에 닿으면 레벨업 후보에서 빠진다 — 진화해야 Lv.1로 리셋되어 다시 큰다(액티브와 동일).
    public bool CanUpgradePassive(EquippedPassive passive) => passive != null && passive.Level < BalanceConstants.MaxSkillLevel;

    public void UpgradePassiveLevel(PassiveSkillId id)
    {
        EquippedPassive passive = GetPassive(id);
        if (passive == null || passive.Level >= BalanceConstants.MaxSkillLevel) return;

        passive.Level++;
        passive.TotalLevel++;
        ApplyPassiveLevelEffect(passive);
    }

    private void ApplyPassiveLevelEffect(EquippedPassive passive)
    {
        ApplyPassiveValue(passive.Id, PerLevelBonus(passive.Id));
        ApplyEvolvedLevelBonus(passive);   // 진화 루트가 레벨마다 더 주는 몫(§ApplyEvolvedLevelBonus)
    }

    // 패시브별 대상 스탯에 값을 더한다(획득=기본값·레벨업=상승값 공통 경로).
    private void ApplyPassiveValue(PassiveSkillId id, float amount)
    {
        switch (id)
        {
            case PassiveSkillId.Strength:
                skills.IncreaseStrengthDamage(amount); // 힘의 몫은 따로 기억된다(스킬트리 "힘" 강화가 그 몫만 2배로 쓴다)
                break;
            case PassiveSkillId.Health:
                health.IncreaseMaxHealth(Mathf.RoundToInt(amount));
                break;
            case PassiveSkillId.Knowledge:
                PlayerExperience.Instance.IncreaseXPMultiplier(amount);
                break;
            case PassiveSkillId.Assassinate:
                AssassinateCritChance += amount;
                break;
            case PassiveSkillId.Refresh:
                RefreshChance += amount;
                break;
            case PassiveSkillId.Defense:
                DamageReduction += amount;
                break;
            case PassiveSkillId.Accel:
                AccelCooldownReduction += amount;
                break;
        }
    }

    // 레벨업 카드 설명 — 레벨당 상승값(SO) 반영
    public static string DescribePassiveLevelEffect(PassiveSkillId id)
    {
        float v = PerLevelBonus(id);
        return id switch
        {
            PassiveSkillId.Strength => Loc.F("passive.lvl.Strength", Pct(v)),
            PassiveSkillId.Health => Loc.F("passive.lvl.Health", v.ToString("0")),
            PassiveSkillId.Knowledge => Loc.F("passive.lvl.Knowledge", Pct(v)),
            PassiveSkillId.Assassinate => Loc.F("passive.lvl.Assassinate", Pct(v)),
            PassiveSkillId.Refresh => Loc.F("passive.lvl.Refresh", Pct(v)),
            PassiveSkillId.Defense => Loc.F("passive.lvl.Defense", Pct(v)),
            PassiveSkillId.Accel => Loc.F("passive.lvl.Accel", Pct(v)),
            _ => "",
        };
    }

    // 획득(신규) 카드 설명 — 기본값(SO) 반영
    public static string DescribePassiveAcquire(PassiveSkillId id)
    {
        float b = BaseValue(id);
        return id switch
        {
            PassiveSkillId.Strength => Loc.F("passive.acq.Strength", Pct(b)),
            PassiveSkillId.Health => Loc.F("passive.acq.Health", b.ToString("0")),
            PassiveSkillId.Knowledge => Loc.F("passive.acq.Knowledge", Pct(b)),
            PassiveSkillId.Assassinate => Loc.F("passive.acq.Assassinate", Pct(b)),
            PassiveSkillId.Refresh => Loc.F("passive.acq.Refresh", Pct(b)),
            PassiveSkillId.Defense => Loc.F("passive.acq.Defense", Pct(b)),
            PassiveSkillId.Accel => Loc.F("passive.acq.Accel", Pct(b)),
            _ => "",
        };
    }

    private static string Pct(float f) => (f * 100f).ToString("0.#");

    // 일시정지(ESC) 요약용: 이 패시브가 **지금 실제로** 얼마나 적용되고 있는지.
    // 레벨업 누적분과 진화로 붙은 보정이 이미 반영된 현재 수치를 그대로 읽어 보여준다.
    public List<string> DescribeCurrentEffect(EquippedPassive p)
    {
        var lines = new List<string>();
        switch (p.Id)
        {
            case PassiveSkillId.Strength:
                if (skills != null) lines.Add(Loc.F("passive.cur.Strength.dmg", Pct(skills.PassiveDamageMultiplier - 1f)));
                if (FirstSlotDamageMultiplierBonus > 0f)
                {
                    // 대상이 캐릭터·획득 순서마다 달라서 이름을 박아 두면 틀린다 — 지금 Q에 있는 스킬을 그때그때 읽는다.
                    string qSkill = skills != null && skills.EquippedSkills.Count > 0 ? skills.EquippedSkills[0].DisplayName : Loc.T("passive.cur.Strength.qfallback");
                    lines.Add(Loc.F("passive.cur.Strength.q", qSkill, Pct(FirstSlotDamageMultiplierBonus)));
                }
                break;

            case PassiveSkillId.Health:
                if (health != null) lines.Add(Loc.F("passive.cur.Health.max", health.MaxHealth));
                if (HeartDropMultiplier > 1f) lines.Add(Loc.F("passive.cur.Health.heartDrop", HeartDropMultiplier.ToString("0.#")));
                break;

            case PassiveSkillId.Knowledge:
                if (PlayerExperience.Instance != null) lines.Add(Loc.F("passive.cur.Knowledge.xp", Pct(PlayerExperience.Instance.XpMultiplier - 1f)));
                if (EnemySpawner.ExtraTreasureChance > 0f) lines.Add(Loc.F("passive.cur.Knowledge.treasure", Pct(EnemySpawner.ExtraTreasureChance)));
                if (HomingKillXpMultiplier > 1f) lines.Add(Loc.F("passive.cur.Knowledge.homingXp", HomingKillXpMultiplier.ToString("0.##")));
                break;

            case PassiveSkillId.Assassinate:
                lines.Add(Loc.F("passive.cur.Assassinate.chance", Pct(AssassinateCritChance)));
                lines.Add(Loc.F("passive.cur.Assassinate.mult", (AssassinateCritMultiplier + MetaBonuses.CritDamageBonus).ToString("0.##")));
                if (CritChanceCapOverride > 0f) lines.Add(Loc.F("passive.cur.Assassinate.critCap", Pct(CritChanceCapOverride)));
                if (AssassinateKillXpMultiplier > 1f) lines.Add(Loc.F("passive.cur.Assassinate.killXp", AssassinateKillXpMultiplier.ToString("0.##")));
                break;

            case PassiveSkillId.Refresh:
                lines.Add(Loc.F("passive.cur.Refresh.reset", Pct(RefreshChance)));
                if (RefreshHealOnResetAmount > 0f) lines.Add(Loc.F("passive.cur.Refresh.heal", RefreshHealOnResetAmount.ToString("0.#")));
                if (BuffSkillCooldownMult < 1f) lines.Add(Loc.F("passive.cur.Refresh.buffCd", Pct(1f - BuffSkillCooldownMult)));
                if (RefreshLightningCooldownProcChance > 0f) lines.Add(Loc.F("passive.cur.Refresh.lightningCd", Pct(RefreshLightningCooldownProcChance)));
                break;

            case PassiveSkillId.Defense:
                lines.Add(Loc.F("passive.cur.Defense.reduce", Pct(DamageReduction)));
                if (health != null && DefenseAutoSwingDamageMult > 0f) lines.Add(Loc.F("passive.cur.Defense.autoSwing", Pct(DefenseAutoSwingDamageMult)));
                if (HealthRetaliationMultiplier > 0f) lines.Add(Loc.F("passive.cur.Defense.retaliate", Pct(HealthRetaliationMultiplier)));
                break;

            case PassiveSkillId.Accel:
                lines.Add(Loc.F("passive.cur.Accel.cd", Pct(AccelCooldownReduction)));
                if (RefreshChance > 0f) lines.Add(Loc.F("passive.cur.Refresh.reset", Pct(RefreshChance)));
                if (AccelCooldownCutOnHit > 0f) lines.Add(Loc.F("passive.cur.Accel.cutOnHit", AccelCooldownCutOnHit.ToString("0.#")));
                break;
        }
        return lines;
    }

    // ── 진화 (2루트 × 2티어, 진화 아이템으로만 열림) — 액티브 스킬과 동일 규칙 ──
    // ⚠️ 폐지된 리프레쉬는 게임 안에선 획득 경로가 없어 여기 닿지 않는다(치트 창으로만 온다). 방어·가속은 2026-08-06에 진화가 설계돼
    //    "설계 없는 패시브를 목록에서 빼던 가드"(HasEvolutionDesign)가 필요 없어져 사라졌다.
    //    새 패시브를 또 만들 거면 진화까지 같이 만들 것 — 안 그러면 이름도 효과도 그대로인 빈 진화가 뜬다.
    public bool CanEvolve(EquippedPassive passive)
    {
        if (passive == null || passive.EvolutionStage >= EvolutionRoutes.MaxStageFor(passive.Id)) return false;
        // 스킬트리 "진화 해금" / "2차 진화 해금". 트리에 그 노드가 없으면 둘 다 true라 게이팅이 없다.
        if (!(passive.EvolutionStage == 0 ? MetaBonuses.EvolutionUnlocked : MetaBonuses.Evolution2Unlocked)) return false;
        if (passive.Level < EvolutionRoutes.RequiredLevel) return false;
        return SelectableRoutes(passive).Any(r => IsRouteUnlocked(passive.Id, r));
    }

    // 루트 잠금: 연계 대상(패시브/액티브)을 보유해야 그 루트를 고를 수 있다(§EvolutionRoutes).
    public bool IsRouteUnlocked(PassiveSkillId id, int route)
    {
        PassiveSkillId? p = EvolutionRoutes.RoutePassivePrereq(id, route);
        if (p.HasValue && !HasPassive(p.Value)) return false;
        ActiveSkillId? a = EvolutionRoutes.RouteActivePrereq(id, route);
        return !a.HasValue || (skills != null && skills.HasSkill(a.Value));
    }

    public static int[] SelectableRoutes(EquippedPassive passive) =>
        passive.EvolutionStage == 0 ? new[] { 0, 1 } : new[] { passive.Route };

    public void EvolvePassive(PassiveSkillId id, int route)
    {
        EquippedPassive passive = GetPassive(id);
        if (passive == null || !CanEvolve(passive)) return;
        if (passive.EvolutionStage > 0 && route != passive.Route) return;
        if (!IsRouteUnlocked(id, route)) return;

        int newTier = passive.EvolutionStage + 1;
        int path = EvolutionRoutes.RoutePath(id, route);

        foreach (int legacyTier in EvolutionRoutes.LegacyTiersFor(newTier))
        {
            passive.PathTier[path] = legacyTier;
            ApplyPassivePathTierEffect(passive, path, legacyTier);
        }
        passive.PathTier[path] = EvolutionRoutes.TargetPathTier(newTier);

        passive.Route = route;
        passive.EvolutionStage = newTier;
        CollectionSave.DiscoverPassiveEvo(id, route, newTier); // 컬렉션(도감) 발견 기록

        // 패시브의 "기본 스탯 도약" = 레벨업 1회분을 한 번 더 얹는 것(스킬의 피해 ×1.5에 해당).
        ApplyPassiveLevelEffect(passive);
        passive.Level = 1;
    }

    // 🔴 **패시브는 1차 진화뿐이다**(2026-09-08 사용자 결정) — 그래서 여기 도달하는 newTier는 1·2뿐이고
    //    옛 T3(2차) 분기는 전부 걷어냈다. 1차 한 번이 T1+T2를 순서대로 밟으므로 **최종값만 의미가 있다.**
    //    ⚠️ 되살리려면 EvolutionRoutes.MaxPassiveStage를 2로 올리고 T3 분기를 다시 채워야 한다.
    // 레벨마다 더 커지는 몫은 여기가 아니라 ApplyEvolvedLevelBonus가 맡는다.
    private void ApplyPassivePathTierEffect(EquippedPassive passive, int path, int newTier)
    {
        switch (passive.Id, path, newTier)
        {
            // 힘  (path0은 버려진 루트 — 패시브는 R0=path1 / R1=path2다. EvolutionRoutes.RoutePath 참고)
            // R0 「불타는 근육」 — 치명타 피해가 증가한다
            case (PassiveSkillId.Strength, 1, 1): AssassinateCritMultiplier += 0.15f; break;
            case (PassiveSkillId.Strength, 1, 2): AssassinateCritMultiplier += 0.35f; break;
            // R1 「생활 근육」 — Q 스킬이 강해진다
            case (PassiveSkillId.Strength, 2, 1): FirstSlotDamageMultiplierBonus += 0.10f; break;
            case (PassiveSkillId.Strength, 2, 2): FirstSlotDamageMultiplierBonus += 0.175f; break;

            // 건강 R0 「강건함」 — **체력이 더욱 빠르게 증가한다**(2026-09-08 문구 개정).
            // 즉시 최대체력 +50%, 이후 레벨마다 +30%(ApplyEvolvedLevelBonus). 만렙이면 아주 두꺼워진다.
            // ⚠️ 예전엔 "최대체력 1당 피해"(HealthDamagePerHp)였다 — 피해 축이라 문구와 정반대였다.
            //    최대체력은 **현재 최대치 기준 배수**라 T1에 한 번만 얹는다(T1·T2에 나눠 걸면 순차 적용되어 두 번 곱해진다).
            case (PassiveSkillId.Health, 1, 1):
                if (health != null) health.IncreaseMaxHealth(Mathf.RoundToInt(health.MaxHealth * HealthSturdyBase));
                break;
            // 건강 R1 「풍요」 — 체력 회복 아이템이 **3배** 자주 나온다. 레벨마다 더 잦아져 만렙에 5~6배.
            // ⚠️ 예전엔 1차 최종값이 5배였다(T1 3 → T2 5). 문구가 "3배"로 확정돼 T2 덮어쓰기를 걷어냈다.
            case (PassiveSkillId.Health, 2, 1): HeartDropMultiplier = HeartDropBase; break;

            // 지식 R0 「보물 탐지」 — 보물상자 블루베리 등장 확률
            case (PassiveSkillId.Knowledge, 1, 1): EnemySpawner.ExtraTreasureChance += 0.005f; break;
            case (PassiveSkillId.Knowledge, 1, 2): EnemySpawner.ExtraTreasureChance += 0.005f; break;
            // 지식 R1 「전투 통찰」 — **호밍 미사일로 처리한 적**이 추가 경험치를 남긴다(2026-09-08 문구 개정).
            // ⚠️ 예전엔 "독수리 투하 시전마다 XP"였다. 루트 조건이 호밍인데 대상이 독수리라 어긋나 있었다.
            case (PassiveSkillId.Knowledge, 2, 1): HomingKillXpMultiplier = HomingKillXpBase; break;

            // 암살 R0 「현상금」 — 치명타로 죽은 적이 추가 경험치를 남긴다
            case (PassiveSkillId.Assassinate, 1, 1): AssassinateKillXpMultiplier += 0.25f; break;
            case (PassiveSkillId.Assassinate, 1, 2): AssassinateKillXpMultiplier += 0.75f; break;
            // 암살 R1 「필중 암살」 — 치명타 확률 **상한이 100%로** 열린다(2026-09-08 문구 개정).
            // 상한만 여는 게 아니라 레벨마다 확률 자체도 오른다(ApplyEvolvedLevelBonus) — 안 그러면 상한에 닿을 길이 없다.
            // ⚠️ 예전엔 "쿨 5초 이상 스킬은 확정 치명타"였다.
            case (PassiveSkillId.Assassinate, 2, 1): CritChanceCapOverride = 1f; break;
            case (PassiveSkillId.Assassinate, 2, 2): AssassinateCritMultiplier += 0.6f; break;

            // 리프레쉬 — 폐지된 패시브(치트 창으로만 도달). 진화 설계가 없어 옛 값을 그대로 둔다.
            case (PassiveSkillId.Refresh, 1, 1): RefreshHealOnResetAmount += 2f; break;
            case (PassiveSkillId.Refresh, 1, 2): RefreshHealOnResetAmount += 2f; break;
            case (PassiveSkillId.Refresh, 2, 1): BuffSkillCooldownMult = 0.9f; break;
            case (PassiveSkillId.Refresh, 2, 2): RefreshLightningCooldownProcChance += 0.05f; break;

            // 방어 R0 「망치 반격」 — 맞으면 반사적으로 휘두른다. 휘두르기 보유가 루트 조건이라 스킬이 없을 일은 없다.
            case (PassiveSkillId.Defense, 1, 1): DefenseAutoSwingDamageMult = 0.6f; break;
            case (PassiveSkillId.Defense, 1, 2): DefenseAutoSwingDamageMult = 1f; break;
            // 방어 R1 「가시 갑주」 — **반사만** 준다. 받은 피해의 10배를 되돌리고 레벨마다 30%씩 늘어난다.
            // ⚠️ 예전엔 최대체력 +200%가 같이 붙어 있었다 — 문구가 반사만 말하므로 걷어냈다(2026-09-08 사용자 지시).
            case (PassiveSkillId.Defense, 2, 1): HealthRetaliationMultiplier = DefenseThornsBase; break;

            // 가속 R0 「리프레쉬」 — 폐지된 리프레쉬의 "쿨타임 초기화"를 물려받는다(PlayerSkills.TryUseSkill이 소비).
            case (PassiveSkillId.Accel, 1, 1): RefreshChance += 0.10f; break;
            case (PassiveSkillId.Accel, 1, 2): RefreshChance += 0.08f; break;
            // 가속 R1 「고통 가속」 — 맞을 때마다 전체 쿨타임이 앞당겨진다.
            case (PassiveSkillId.Accel, 2, 1): AccelCooldownCutOnHit += 0.5f; break;
        }
    }

    // ── 진화 루트가 레벨업마다 더 주는 몫 ────────────────────────────────────
    // 🔴 진화한 패시브는 레벨업이 **원래 축 + 루트 전용 축** 둘을 올린다(2026-09-08 사용자 지시).
    //    "다 찍으면 엄청난 체력을 가질 수 있게" 같은 요구가 여기서 만들어진다.
    //    루트를 안 탄 패시브(EvolutionStage 0)는 걸리지 않는다.
    // ⚠️ 진화 직후에도 한 번 불린다(EvolvePassive가 ApplyPassiveLevelEffect를 부른다) — 그게 진화의 "도약" 몫이다.
    private const float HealthSturdyBase = 0.5f;       // 건강 R0: 진화 즉시 최대체력 +50%
    private const float HealthSturdyPerLevel = 0.3f;   //          레벨마다 최대체력 +30%(현재 최대치 기준 = 복리)
    private const float HeartDropBase = 3f;            // 건강 R1: 회복템 3배
    private const float HeartDropPerLevel = 0.3f;      //          진화 시 1회 + 레벨업 9회 = 만렙 6.0배
    private const float HomingKillXpBase = 2f;         // 지식 R1: 호밍 처치 경험치 2배
    private const float HomingKillXpPerLevel = 0.15f;  //          진화 시 1회 + 레벨업 9회 = 만렙 3.5배
    private const float AssassinCertainCritPerLevel = 0.03f; // 암살 R1: 레벨마다 치명타 확률 +3%p(상한이 100%로 열려 있다)
    private const float DefenseThornsBase = 10f;       // 방어 R1: 받은 피해의 10배 반사
    private const float DefenseThornsPerLevel = 0.3f;  //          레벨마다 기본값의 30%씩(진화 1회 포함 만렙 40배)

    private void ApplyEvolvedLevelBonus(EquippedPassive p)
    {
        if (p.EvolutionStage <= 0) return;
        switch (p.Id, p.Route)
        {
            case (PassiveSkillId.Health, 0):
                if (health != null) health.IncreaseMaxHealth(Mathf.RoundToInt(health.MaxHealth * HealthSturdyPerLevel));
                break;
            case (PassiveSkillId.Health, 1):
                HeartDropMultiplier += HeartDropPerLevel;
                break;
            case (PassiveSkillId.Knowledge, 1):
                HomingKillXpMultiplier += HomingKillXpPerLevel;
                break;
            case (PassiveSkillId.Assassinate, 1):
                AssassinateCritChance += AssassinCertainCritPerLevel;
                break;
            case (PassiveSkillId.Defense, 1):
                HealthRetaliationMultiplier += DefenseThornsBase * DefenseThornsPerLevel;
                break;
        }
    }


    // 루트 잠금 조건은 EvolutionRoutes.RoutePrereq가 (패시브, 루트)별로 단독 소유한다(2026-08-06 개편).

    // 🔴 액티브 진화와 같은 규칙 — **수치를 쓰지 않는다.** 그림만 보고 호기심으로 고르게 한다.
    //    실제 수치는 ApplyPassivePathTierEffect와 소비처에 있다.
    // 문장은 번역 표(`Assets/Localization/Tables/Game`)가 소유한다.
    // 🔴 키 좌표는 화면 그대로 — 루트 0/1 × 차수(패시브는 1차뿐이다). PlayerSkills 쪽과 같은 규칙.
    // 정의가 없는 조합은 빈 문자열 — 옛 `_ => ""` 분기와 같다(표에 키가 없으면 Has가 false다).
    public static string DescribePathEffect(PassiveSkillId id, int route, int tier) =>
        Loc.TOr($"evo.passive.desc.{id}.{route}.{tier}", "");
}
