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
    public int TotalEvolutionTier => PathTier[0] + PathTier[1] + PathTier[2];

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
    public static float HealthDamagePerHp = 0f; // 건강 연계 path1: 최대체력 1당 피해량 배율 보너스
    // 피격 시 받은 피해의 이 배수를 전체 적에게 되돌려준다.
    // ⚠️ 예전엔 건강 path2("가시 갑주")가 이걸 켰지만, 2026-08-06 명세에서 **방어 path2**로 옮겨졌다
    //    (건강 path2는 하트 드랍으로 교체). 소비처는 HandleDamageTaken 한 곳뿐이라 필드는 그대로 쓴다.
    public static float HealthRetaliationMultiplier = 0f;
    public static float BasicAttackDamageMultiplierBonus = 0f; // 힘 연계 path2: 기본공격 전용 추가 피해 배율
    public static int EagleDropCastXpBonus = 0; // 지식 연계 path2: 독수리 투하 시전마다 즉시 획득하는 경험치
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
    // 암살 연계 path2(스나이핑): 이 쿨타임 이상인 스킬은 치명타 확률 100%(상한 무시). 0=미보유.
    public static float AssassinateSlowSkillCritCooldown = 0f;

    [SerializeField] private PassiveProgression[] progressions; // 패시브별 기본값+레벨업당 상승값(Tier A). 미할당 패시브는 코드 기본값 폴백(=현행)

    private static System.Collections.Generic.Dictionary<PassiveSkillId, PassiveProgression> progressionLookup;

    private readonly List<EquippedPassive> equippedPassives = new List<EquippedPassive>();
    private PlayerSkills skills;
    private PlayerHealth health;

    private float regenTimer;
    private float regenAmount;
    private float regenInterval;

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
        HealthDamagePerHp = 0f;
        HealthRetaliationMultiplier = 0f;
        DamageReduction = 0f;
        AccelCooldownReduction = 0f;
        DefenseAutoSwingDamageMult = 0f;
        AccelCooldownCutOnHit = 0f;
        HeartDropMultiplier = 1f;
        AssassinateSlowSkillCritCooldown = 0f;
        BasicAttackDamageMultiplierBonus = 0f;
        EagleDropCastXpBonus = 0;
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

    public static float BaseValue(PassiveSkillId id)
    {
        PassiveProgression p = Prog(id);
        return p != null ? p.baseValue : PassiveProgression.DefaultBaseValue(id);
    }

    public static float PerLevelBonus(PassiveSkillId id)
    {
        PassiveProgression p = Prog(id);
        return p != null ? p.perLevelBonus : PassiveProgression.DefaultPerLevelBonus(id);
    }

    private void Update()
    {
        if (regenInterval <= 0f || regenAmount <= 0f || health == null) return;

        regenTimer += Time.deltaTime;
        if (regenTimer >= regenInterval)
        {
            regenTimer = 0f;
            health.Heal(Mathf.RoundToInt(regenAmount));
        }
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
        return isCrit ? damage * AssassinateCritMultiplier : damage;
    }

    public void AcquirePassive(PassiveSkillId id)
    {
        if (HasMaxPassives || HasPassive(id)) return;

        equippedPassives.Add(new EquippedPassive { Id = id });
        ApplyPassiveValue(id, BaseValue(id)); // 획득 = 기본값 적용
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
        ApplyPassiveValue(passive.Id, PerLevelBonus(passive.Id)); // 레벨업 = 레벨당 상승값 적용
    }

    // 패시브별 대상 스탯에 값을 더한다(획득=기본값·레벨업=상승값 공통 경로).
    private void ApplyPassiveValue(PassiveSkillId id, float amount)
    {
        switch (id)
        {
            case PassiveSkillId.Strength:
                skills.IncreaseDamageMultiplier(amount);
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
            PassiveSkillId.Strength => $"피해량 {Pct(v)}% 증가",
            PassiveSkillId.Health => $"최대 체력 {v:0} 증가",
            PassiveSkillId.Knowledge => $"경험치 획득량 {Pct(v)}% 증가",
            PassiveSkillId.Assassinate => $"치명타 확률 {Pct(v)}%p 증가",
            PassiveSkillId.Refresh => $"재사용 초기화 확률 {Pct(v)}%p 증가",
            PassiveSkillId.Defense => $"받는 피해 {Pct(v)}%p 감소",
            PassiveSkillId.Accel => $"모든 스킬 쿨타임 {Pct(v)}%p 감소",
            _ => "",
        };
    }

    // 획득(신규) 카드 설명 — 기본값(SO) 반영
    public static string DescribePassiveAcquire(PassiveSkillId id)
    {
        float b = BaseValue(id);
        return id switch
        {
            PassiveSkillId.Strength => $"피해량 {Pct(b)}% 증가",
            PassiveSkillId.Health => $"최대 체력 {b:0} 증가",
            PassiveSkillId.Knowledge => $"경험치 획득량 {Pct(b)}% 증가",
            PassiveSkillId.Assassinate => $"모든 피해가 {Pct(b)}% 확률로 3배 피해",
            PassiveSkillId.Refresh => $"스킬 사용 시 {Pct(b)}% 확률로 쿨타임 초기화",
            PassiveSkillId.Defense => $"받는 피해 {Pct(b)}% 감소",
            PassiveSkillId.Accel => $"모든 스킬 쿨타임 {Pct(b)}% 감소",
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
                if (skills != null) lines.Add($"전체 피해량 +{Pct(skills.PassiveDamageMultiplier - 1f)}%");
                if (BasicAttackDamageMultiplierBonus > 0f) lines.Add($"화살 쏘기 피해량 +{Pct(BasicAttackDamageMultiplierBonus)}%");
                break;

            case PassiveSkillId.Health:
                if (health != null) lines.Add($"최대 체력 {health.MaxHealth}");
                if (regenInterval > 0f && regenAmount > 0f) lines.Add($"{regenInterval:0.#}초마다 체력 {regenAmount:0.#} 재생");
                if (HealthDamagePerHp > 0f) lines.Add($"최대 체력 1당 피해량 +{Pct(HealthDamagePerHp)}%");
                if (HeartDropMultiplier > 1f) lines.Add($"체력 회복 드랍률 x{HeartDropMultiplier:0.#}");
                break;

            case PassiveSkillId.Knowledge:
                if (PlayerExperience.Instance != null) lines.Add($"경험치 획득량 +{Pct(PlayerExperience.Instance.XpMultiplier - 1f)}%");
                if (EnemySpawner.ExtraTreasureChance > 0f) lines.Add($"블루베리가 {Pct(EnemySpawner.ExtraTreasureChance)}% 확률로 보물상자로 등장");
                if (EagleDropCastXpBonus > 0) lines.Add($"독수리 투하 시전마다 경험치 +{EagleDropCastXpBonus}");
                break;

            case PassiveSkillId.Assassinate:
                lines.Add($"치명타 확률 {Pct(AssassinateCritChance)}%");
                lines.Add($"치명타 피해 배율 x{AssassinateCritMultiplier:0.##}");
                if (AssassinateSlowSkillCritCooldown > 0f) lines.Add($"재사용 {AssassinateSlowSkillCritCooldown:0.#}초 이상 스킬은 항상 치명타");
                if (AssassinateKillXpMultiplier > 1f) lines.Add($"치명타 처치 시 경험치 x{AssassinateKillXpMultiplier:0.##}");
                break;

            case PassiveSkillId.Refresh:
                lines.Add($"스킬 사용 시 {Pct(RefreshChance)}% 확률로 쿨타임 초기화");
                if (RefreshHealOnResetAmount > 0f) lines.Add($"초기화될 때마다 체력 {RefreshHealOnResetAmount:0.#} 회복");
                if (BuffSkillCooldownMult < 1f) lines.Add($"버프류 스킬(산탄·낙뢰) 쿨타임 -{Pct(1f - BuffSkillCooldownMult)}%");
                if (RefreshLightningCooldownProcChance > 0f) lines.Add($"낙뢰 발동 시 {Pct(RefreshLightningCooldownProcChance)}% 확률로 전체 쿨타임 -0.5초");
                break;

            case PassiveSkillId.Defense:
                lines.Add($"받는 피해 -{Pct(DamageReduction)}%");
                if (health != null && DefenseAutoSwingDamageMult > 0f) lines.Add($"피격 시 휘두르기 자동 발동 (피해 {Pct(DefenseAutoSwingDamageMult)}%)");
                if (HealthRetaliationMultiplier > 0f) lines.Add($"피격 시 받은 피해의 {Pct(HealthRetaliationMultiplier)}%를 전체 적에게");
                break;

            case PassiveSkillId.Accel:
                lines.Add($"모든 스킬 쿨타임 -{Pct(AccelCooldownReduction)}%");
                if (RefreshChance > 0f) lines.Add($"스킬 사용 시 {Pct(RefreshChance)}% 확률로 쿨타임 초기화");
                if (AccelCooldownCutOnHit > 0f) lines.Add($"피격 시 전체 쿨타임 -{AccelCooldownCutOnHit:0.#}초");
                break;
        }
        return lines;
    }

    // ── 진화 (2루트 × 2티어, 진화 아이템으로만 열림) — 액티브 스킬과 동일 규칙 ──
    // ⚠️ 폐지된 리프레쉬는 획득 경로가 없어 여기 닿지 않는다. 방어·가속은 2026-08-06에 진화가 설계돼
    //    "설계 없는 패시브를 목록에서 빼던 가드"(HasEvolutionDesign)가 필요 없어져 사라졌다.
    //    새 패시브를 또 만들 거면 진화까지 같이 만들 것 — 안 그러면 이름도 효과도 그대로인 빈 진화가 뜬다.
    public bool CanEvolve(EquippedPassive passive)
    {
        if (passive == null || passive.EvolutionStage >= EvolutionRoutes.MaxStage) return false;
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
        if (!IsRouteUnlocked(id, route)) return; // 연계 스킬 미보유

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

        // 패시브의 "기본 스탯 도약" = 레벨업 1회분을 한 번 더 얹는 것(스킬의 피해 ×1.5에 해당).
        ApplyPassiveLevelEffect(passive);
        passive.Level = 1;
    }

    private void ApplyPassivePathTierEffect(EquippedPassive passive, int path, int newTier)
    {
        switch (passive.Id, path, newTier)
        {
            // 힘
            case (PassiveSkillId.Strength, 0, 1): skills.IncreaseDamageMultiplier(0.08f); break;
            case (PassiveSkillId.Strength, 0, 2): skills.IncreaseDamageMultiplier(0.15f); break;
            case (PassiveSkillId.Strength, 0, 3): skills.IncreaseDamageMultiplier(0.22f); break;
            case (PassiveSkillId.Strength, 1, 1): AssassinateCritMultiplier += 0.15f; break;
            case (PassiveSkillId.Strength, 1, 2): AssassinateCritMultiplier += 0.35f; break;
            case (PassiveSkillId.Strength, 1, 3): AssassinateCritMultiplier += 0.5f; break;
            case (PassiveSkillId.Strength, 2, 1): BasicAttackDamageMultiplierBonus += 0.10f; break;
            case (PassiveSkillId.Strength, 2, 2): BasicAttackDamageMultiplierBonus += 0.175f; break;
            case (PassiveSkillId.Strength, 2, 3): BasicAttackDamageMultiplierBonus += 0.25f; break;

            // 건강
            case (PassiveSkillId.Health, 0, 1): regenAmount = 2f; regenInterval = 5f; break;
            case (PassiveSkillId.Health, 0, 2): regenAmount = 5f; regenInterval = 5f; break;
            case (PassiveSkillId.Health, 0, 3): regenAmount = 8f; regenInterval = 3f; break;
            case (PassiveSkillId.Health, 1, 1): HealthDamagePerHp += 0.0005f; break;
            case (PassiveSkillId.Health, 1, 2): HealthDamagePerHp += 0.0005f; break;
            case (PassiveSkillId.Health, 1, 3): HealthDamagePerHp += 0.0005f; break;
            // 건강 path2(오브 연계) — "가시 갑주"(피해 반사)에서 **하트 드랍**으로 교체(2026-08-06 명세).
            // 반사는 방어 path2로 옮겨갔다. 1차는 T1+T2를 순서대로 밟으므로 최종값만 의미가 있다(=5배).
            case (PassiveSkillId.Health, 2, 1): HeartDropMultiplier = 3f; break;
            case (PassiveSkillId.Health, 2, 2): HeartDropMultiplier = 5f; break;
            case (PassiveSkillId.Health, 2, 3): HeartDropMultiplier = 10f; break;

            // 지식
            case (PassiveSkillId.Knowledge, 0, 1): PlayerExperience.Instance.IncreaseXPMultiplier(0.08f); break;
            case (PassiveSkillId.Knowledge, 0, 2): PlayerExperience.Instance.IncreaseXPMultiplier(0.15f); break;
            case (PassiveSkillId.Knowledge, 0, 3): PlayerExperience.Instance.IncreaseXPMultiplier(0.22f); break;
            case (PassiveSkillId.Knowledge, 1, 1): EnemySpawner.ExtraTreasureChance += 0.005f; break;
            case (PassiveSkillId.Knowledge, 1, 2): EnemySpawner.ExtraTreasureChance += 0.005f; break;
            case (PassiveSkillId.Knowledge, 1, 3): EnemySpawner.ExtraTreasureChance += 0.005f; break;
            case (PassiveSkillId.Knowledge, 2, 1): EagleDropCastXpBonus += 3; break;
            case (PassiveSkillId.Knowledge, 2, 2): EagleDropCastXpBonus += 3; break;
            case (PassiveSkillId.Knowledge, 2, 3): EagleDropCastXpBonus += 4; break;

            // 암살
            case (PassiveSkillId.Assassinate, 0, 1): AssassinateCritChance += 0.08f; break;
            case (PassiveSkillId.Assassinate, 0, 2): AssassinateCritChance += 0.15f; break;
            case (PassiveSkillId.Assassinate, 0, 3): AssassinateCritChance += 0.22f; break;
            case (PassiveSkillId.Assassinate, 1, 1): AssassinateKillXpMultiplier += 0.25f; break;
            case (PassiveSkillId.Assassinate, 1, 2): AssassinateKillXpMultiplier += 0.75f; break;
            case (PassiveSkillId.Assassinate, 1, 3): AssassinateKillXpMultiplier += 0.5f; break;
            // 암살 path2(스나이핑 연계) — "폭풍 암살"(회오리 전용 치명타)에서 **긴 쿨 스킬 확정 치명타**로 교체(2026-08-06 명세).
            // 치명타 배율 +0.6은 기본 3배 대비 +20%다. 2차는 임계 쿨을 낮춰 해당 스킬을 늘린다.
            case (PassiveSkillId.Assassinate, 2, 1): AssassinateSlowSkillCritCooldown = 5f; break;
            case (PassiveSkillId.Assassinate, 2, 2): AssassinateCritMultiplier += 0.6f; break;
            case (PassiveSkillId.Assassinate, 2, 3): AssassinateSlowSkillCritCooldown = 3f; AssassinateCritMultiplier += 0.6f; break;

            // 리프레쉬
            case (PassiveSkillId.Refresh, 0, 1): RefreshChance += 0.03f; break;
            case (PassiveSkillId.Refresh, 0, 2): RefreshChance += 0.02f; break;
            case (PassiveSkillId.Refresh, 0, 3): RefreshChance += 0.03f; break;
            case (PassiveSkillId.Refresh, 1, 1): RefreshHealOnResetAmount += 2f; break;
            case (PassiveSkillId.Refresh, 1, 2): RefreshHealOnResetAmount += 2f; break;
            case (PassiveSkillId.Refresh, 1, 3): RefreshHealOnResetAmount += 2f; break;
            case (PassiveSkillId.Refresh, 2, 1): BuffSkillCooldownMult = 0.9f; break; // 버프류 스킬 쿨타임 10% 감소
            case (PassiveSkillId.Refresh, 2, 2): RefreshLightningCooldownProcChance += 0.05f; break;
            case (PassiveSkillId.Refresh, 2, 3): RefreshLightningCooldownProcChance += 0.03f; break;

            // 방어 (2026-08-06 신설) — R0=path1(휘두르기 연계) / R1=path2(건강 연계)
            // path1: 맞으면 반사적으로 휘두른다. 휘두르기 보유가 루트 조건이라 스킬이 없을 일은 없다.
            case (PassiveSkillId.Defense, 1, 1): DefenseAutoSwingDamageMult = 0.6f; break;
            case (PassiveSkillId.Defense, 1, 2): DefenseAutoSwingDamageMult = 1f; break;
            case (PassiveSkillId.Defense, 1, 3): DefenseAutoSwingDamageMult = 2f; break;
            // path2: 최대체력 +200%와 피해 3배 반사. 최대체력은 **현재 최대치 기준 배수**라 T1에 한 번만 얹는다
            // (T1·T2에 나눠 걸면 순차 적용이라 100→200→400으로 +300%가 된다).
            case (PassiveSkillId.Defense, 2, 1): if (health != null) health.IncreaseMaxHealth(health.MaxHealth * 2); break;
            case (PassiveSkillId.Defense, 2, 2): HealthRetaliationMultiplier += 3f; break;
            case (PassiveSkillId.Defense, 2, 3):
                if (health != null) health.IncreaseMaxHealth(health.MaxHealth / 2); // 2차: 다시 +50%
                HealthRetaliationMultiplier += 2f;                                   // 반사 총 5배
                break;

            // 가속 (2026-08-06 신설) — R0=path1(되감기 연계) / R1=path2(방어 연계)
            // path1: 폐지된 리프레쉬의 "쿨타임 초기화"를 그대로 물려받는다(PlayerSkills.TryUseSkill이 소비).
            case (PassiveSkillId.Accel, 1, 1): RefreshChance += 0.10f; break;
            case (PassiveSkillId.Accel, 1, 2): RefreshChance += 0.08f; break;
            case (PassiveSkillId.Accel, 1, 3): RefreshChance += 0.10f; break;
            // path2: 맞을 때마다 전체 쿨타임이 앞당겨진다.
            case (PassiveSkillId.Accel, 2, 1): AccelCooldownCutOnHit += 0.5f; break;
            case (PassiveSkillId.Accel, 2, 3): AccelCooldownCutOnHit += 0.5f; break;
        }
    }


    // 루트 잠금 조건은 EvolutionRoutes.RoutePrereq가 (패시브, 루트)별로 단독 소유한다(2026-08-06 개편).

    // 🔴 액티브 진화와 같은 규칙 — **수치를 쓰지 않는다.** 그림만 보고 호기심으로 고르게 한다.
    //    실제 수치는 ApplyPassivePathTierEffect와 소비처에 있다.
    public static string DescribePathEffect(PassiveSkillId id, int path, int tier) => (id, path, tier) switch
    {
        (PassiveSkillId.Strength, 0, 1) => "주먹이 무거워진다",
        (PassiveSkillId.Strength, 0, 2) => "더 무거워진다",
        (PassiveSkillId.Strength, 0, 3) => "휘두르는 것마다 부서진다",
        (PassiveSkillId.Strength, 1, 1) => "제대로 들어간 한 방이 더 깊다",
        (PassiveSkillId.Strength, 1, 2) => "훨씬 더 깊다",
        (PassiveSkillId.Strength, 1, 3) => "제대로 맞으면 남는 게 없다",
        (PassiveSkillId.Strength, 2, 1) => "손에 든 것부터 강해진다",
        (PassiveSkillId.Strength, 2, 2) => "더 강해진다",
        (PassiveSkillId.Strength, 2, 3) => "맨손이 무기가 된다",

        (PassiveSkillId.Health, 0, 1) => "상처가 알아서 아문다",
        (PassiveSkillId.Health, 0, 2) => "더 빨리 아문다",
        (PassiveSkillId.Health, 0, 3) => "다치는 속도를 앞지른다",
        (PassiveSkillId.Health, 1, 1) => "버틸수록 세진다",
        (PassiveSkillId.Health, 1, 2) => "더 세진다",
        (PassiveSkillId.Health, 1, 3) => "두꺼운 몸이 그대로 힘이 된다",
        (PassiveSkillId.Health, 2, 1) => "쓰러진 자리에 먹을 게 남는다",
        (PassiveSkillId.Health, 2, 2) => "훨씬 자주 남는다",
        (PassiveSkillId.Health, 2, 3) => "발밑이 늘 붉다",

        (PassiveSkillId.Knowledge, 0, 1) => "보고 배우는 게 빠르다",
        (PassiveSkillId.Knowledge, 0, 2) => "더 빠르다",
        (PassiveSkillId.Knowledge, 0, 3) => "한 번 보면 안다",
        (PassiveSkillId.Knowledge, 1, 1) => "가끔 상자를 짊어진 놈이 섞인다",
        (PassiveSkillId.Knowledge, 1, 2) => "더 자주 섞인다",
        (PassiveSkillId.Knowledge, 1, 3) => "눈에 띄게 자주 섞인다",
        (PassiveSkillId.Knowledge, 2, 1) => "독수리가 날면 뭔가 남는다",
        (PassiveSkillId.Knowledge, 2, 2) => "더 많이 남는다",
        (PassiveSkillId.Knowledge, 2, 3) => "날 때마다 두둑하다",

        (PassiveSkillId.Assassinate, 0, 1) => "급소가 자꾸 눈에 들어온다",
        (PassiveSkillId.Assassinate, 0, 2) => "더 자주 보인다",
        (PassiveSkillId.Assassinate, 0, 3) => "어디를 봐도 급소다",
        (PassiveSkillId.Assassinate, 1, 1) => "깔끔하게 끝낸 값을 받는다",
        (PassiveSkillId.Assassinate, 1, 2) => "값이 오른다",
        (PassiveSkillId.Assassinate, 1, 3) => "끝낼수록 배가 부르다",
        (PassiveSkillId.Assassinate, 2, 1) => "묵직한 것일수록 급소만 노린다",
        (PassiveSkillId.Assassinate, 2, 2) => "그 한 방이 더 깊다",
        (PassiveSkillId.Assassinate, 2, 3) => "웬만한 건 전부 급소로 들어간다",

        (PassiveSkillId.Refresh, 0, 1) => "가끔 방금 쓴 게 다시 준비된다",
        (PassiveSkillId.Refresh, 0, 2) => "더 자주 그런다",
        (PassiveSkillId.Refresh, 0, 3) => "기다리는 일이 드물어진다",
        (PassiveSkillId.Refresh, 1, 1) => "다시 채워질 때마다 숨이 트인다",
        (PassiveSkillId.Refresh, 1, 2) => "더 깊게 트인다",
        (PassiveSkillId.Refresh, 1, 3) => "채워질수록 멀쩡해진다",
        (PassiveSkillId.Refresh, 2, 1) => "몸을 데우는 것들이 빨리 돌아온다",
        (PassiveSkillId.Refresh, 2, 2) => "번개가 치면 전부 앞당겨진다",
        (PassiveSkillId.Refresh, 2, 3) => "번개가 더 자주 편을 든다",

        (PassiveSkillId.Defense, 1, 1) => "맞으면 몸이 먼저 반응한다",
        (PassiveSkillId.Defense, 1, 2) => "그 반사가 무거워진다",
        (PassiveSkillId.Defense, 1, 3) => "맞는 순간이 곧 반격이다",
        (PassiveSkillId.Defense, 2, 1) => "몸집이 통째로 불어난다",
        (PassiveSkillId.Defense, 2, 2) => "때린 만큼 그대로 돌아간다",
        (PassiveSkillId.Defense, 2, 3) => "건드린 쪽이 먼저 무너진다",

        (PassiveSkillId.Accel, 1, 1) => "가끔 방금 쓴 게 다시 준비된다",
        (PassiveSkillId.Accel, 1, 2) => "더 자주 그런다",
        (PassiveSkillId.Accel, 1, 3) => "기다리는 일이 드물어진다",
        (PassiveSkillId.Accel, 2, 1) => "맞을수록 손이 빨라진다",
        (PassiveSkillId.Accel, 2, 2) => "더 빨라진다",
        (PassiveSkillId.Accel, 2, 3) => "맞는 것이 곧 재촉이 된다",

        _ => "",
    };

    // 진화 카드에 붙는 짧은 제목. 기능명이 아니라 별명이다.
    public static string GetPathTierTitle(PassiveSkillId id, int path, int tier) => (id, path, tier) switch
    {
        (PassiveSkillId.Strength, 0, 1) => "무거운 주먹",
        (PassiveSkillId.Strength, 0, 2) => "더 무거운 주먹",
        (PassiveSkillId.Strength, 0, 3) => "휘두르면 부서진다",
        (PassiveSkillId.Strength, 1, 1) => "깊게 들어간다",
        (PassiveSkillId.Strength, 1, 2) => "더 깊게",
        (PassiveSkillId.Strength, 1, 3) => "남는 게 없다",
        (PassiveSkillId.Strength, 2, 1) => "손에 든 것부터",
        (PassiveSkillId.Strength, 2, 2) => "더 단단히",
        (PassiveSkillId.Strength, 2, 3) => "맨손이 무기다",

        (PassiveSkillId.Health, 0, 1) => "알아서 아문다",
        (PassiveSkillId.Health, 0, 2) => "빨리 아문다",
        (PassiveSkillId.Health, 0, 3) => "다치는 속도를 앞지른다",
        (PassiveSkillId.Health, 1, 1) => "버틸수록 세진다",
        (PassiveSkillId.Health, 1, 2) => "더 세진다",
        (PassiveSkillId.Health, 1, 3) => "두꺼운 몸이 힘이다",
        (PassiveSkillId.Health, 2, 1) => "먹을 게 남는다",
        (PassiveSkillId.Health, 2, 2) => "자주 남는다",
        (PassiveSkillId.Health, 2, 3) => "발밑이 붉다",

        (PassiveSkillId.Knowledge, 0, 1) => "빨리 배운다",
        (PassiveSkillId.Knowledge, 0, 2) => "더 빨리",
        (PassiveSkillId.Knowledge, 0, 3) => "한 번 보면 안다",
        (PassiveSkillId.Knowledge, 1, 1) => "상자를 진 놈",
        (PassiveSkillId.Knowledge, 1, 2) => "더 자주 섞인다",
        (PassiveSkillId.Knowledge, 1, 3) => "눈에 띄게 자주",
        (PassiveSkillId.Knowledge, 2, 1) => "독수리가 남기는 것",
        (PassiveSkillId.Knowledge, 2, 2) => "더 많이 남는다",
        (PassiveSkillId.Knowledge, 2, 3) => "날 때마다 두둑하게",

        (PassiveSkillId.Assassinate, 0, 1) => "급소가 보인다",
        (PassiveSkillId.Assassinate, 0, 2) => "더 자주 보인다",
        (PassiveSkillId.Assassinate, 0, 3) => "어디를 봐도 급소",
        (PassiveSkillId.Assassinate, 1, 1) => "깔끔한 값",
        (PassiveSkillId.Assassinate, 1, 2) => "오르는 값",
        (PassiveSkillId.Assassinate, 1, 3) => "끝낼수록 배부르다",
        (PassiveSkillId.Assassinate, 2, 1) => "묵직한 급소",
        (PassiveSkillId.Assassinate, 2, 2) => "더 깊게",
        (PassiveSkillId.Assassinate, 2, 3) => "전부 급소로",

        (PassiveSkillId.Refresh, 0, 1) => "다시 준비된다",
        (PassiveSkillId.Refresh, 0, 2) => "더 자주",
        (PassiveSkillId.Refresh, 0, 3) => "기다릴 일이 없다",
        (PassiveSkillId.Refresh, 1, 1) => "숨이 트인다",
        (PassiveSkillId.Refresh, 1, 2) => "더 깊게",
        (PassiveSkillId.Refresh, 1, 3) => "채워질수록 멀쩡해진다",
        (PassiveSkillId.Refresh, 2, 1) => "빨리 돌아온다",
        (PassiveSkillId.Refresh, 2, 2) => "번개가 앞당긴다",
        (PassiveSkillId.Refresh, 2, 3) => "번개가 편을 든다",

        (PassiveSkillId.Defense, 1, 1) => "몸이 먼저 반응한다",
        (PassiveSkillId.Defense, 1, 2) => "무거운 반사",
        (PassiveSkillId.Defense, 1, 3) => "맞는 순간이 반격",
        (PassiveSkillId.Defense, 2, 1) => "통째로 불어난다",
        (PassiveSkillId.Defense, 2, 2) => "그대로 돌아간다",
        (PassiveSkillId.Defense, 2, 3) => "건드린 쪽이 무너진다",

        (PassiveSkillId.Accel, 1, 1) => "다시 준비된다",
        (PassiveSkillId.Accel, 1, 2) => "더 자주",
        (PassiveSkillId.Accel, 1, 3) => "기다릴 일이 없다",
        (PassiveSkillId.Accel, 2, 1) => "맞을수록 빨라진다",
        (PassiveSkillId.Accel, 2, 2) => "더 빨라진다",
        (PassiveSkillId.Accel, 2, 3) => "맞는 것이 재촉",

        _ => "",
    };
}
