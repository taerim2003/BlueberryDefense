using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// 아웃게임(메타 프로그레션) 런타임 계층.
// 인게임에서 번 정수(태양빛)를 저장하고(→ SkillTreeSave), 스킬트리 해금 효과를
// 다음 판 시작 시 인게임 스탯에 반영한다(→ SkillEffects/MetaRunApplier).
// MonoBehaviour 없음 — 순수 static/data. 씬 배치 컴포넌트는 MetaRunApplier가 담당.
// ─────────────────────────────────────────────────────────────────────────────

// 스킬트리 노드 효과 분류(SkillNode.effect 필드 + 에디터 드롭다운에서 사용).
public enum MetaUpgradeId
{
    Attack,   // 공격력
    Health,   // 체력
    Regen,    // 회복
    Cooldown, // 쿨타임
    Duration, // 지속시간
    Xp,       // 경험
    Wealth,   // 부유(정수 획득)
    Crit,     // 치명타
}

// 판 시작 시 MetaRunApplier가 저장값을 읽어 세팅하는 인게임 런타임 보너스.
// 기본값 = 무효과. 전투 코드가 매 발동/계산 때 읽는다.
public static class MetaBonuses
{
    public static float CooldownMult = 1f; // 스킬 쿨타임 배율 (<1이면 감소)
    public static float DurationMult = 1f; // 스킬 지속시간 배율 (>1이면 증가)
    public static float CritBonus = 0f;    // 전역 치명타 확률 가산(0~1)
    public static float CurrencyMult = 1f; // 정수 획득 배율
    public static int RegenPer5s = 0;      // 5초마다 회복량

    public static float FlyDamageBonus = 0f;      // 비행 적 추가 피해 배율 가산(전역, 0~)
    public static float EagleFlyDamageBonus = 0f; // 독수리 투하의 비행 적 추가 피해(전역 위에 더해짐)
    public static float HealDropChanceBonus = 0f; // 적 처치 시 하트 드랍 확률 가산(0~1)

    public static bool OrbCanHitFlying = false;         // 기본 오브도 비행 적 타격 가능
    public static bool WhirlwindCooldownBonus = false;  // 회오리는 쿨타임 감소 효과를 1.5배로 받음
    public static float RefreshChanceBonus = 0f;        // 리프레시(재사용 초기화) 확률 가산(0~1)
    public static float ThunderCooldownPerStrike = 0f;  // 낙뢰 1회 타격마다 낙뢰 쿨타임 감소(초)
    public static int ArrowStartLevel = 1;              // 기본공격(화살) 시작 레벨
    public static int RerollCount = 0;                  // 레벨업 선택지 리롤 가능 횟수(게임당)

    public static void Reset()
    {
        CooldownMult = 1f;
        DurationMult = 1f;
        CritBonus = 0f;
        CurrencyMult = 1f;
        RegenPer5s = 0;
        FlyDamageBonus = 0f;
        EagleFlyDamageBonus = 0f;
        HealDropChanceBonus = 0f;
        OrbCanHitFlying = false;
        WhirlwindCooldownBonus = false;
        RefreshChanceBonus = 0f;
        ThunderCooldownPerStrike = 0f;
        ArrowStartLevel = 1;
        RerollCount = 0;
    }
}

// 이번 판에서 모은 정수(판 종료 시 SkillTreeSave.AddEssence로 적립).
public static class MetaRun
{
    public static int RunCurrency;

    public static void Reset() => RunCurrency = 0;

    // 정수 픽업이 플레이어에게 흡수될 때 호출 — 부유(정수 획득) 배율이 여기서 적용된다.
    public static void Collect(int baseAmount)
    {
        RunCurrency += Mathf.Max(1, Mathf.RoundToInt(baseAmount * MetaBonuses.CurrencyMult));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 스킬트리 해금집합 → 인게임 효과. 노드 id 기준 코드 레지스트리(에셋 effect 필드는 신뢰 안 함).
// 이번 단계에선 단순스탯 노드만 실제 값 부여, 나머지(신규 범용·스킬 개별강화)는 스텁(0).
// MetaRunApplier가 판 시작 시 호출한다. 상세 매핑은 SKILLTREE_DESIGN.md §4.
// ─────────────────────────────────────────────────────────────────────────────
public static class SkillEffects
{
    // %는 정수 퍼센트값으로 누적(예: DamagePct=13 → 피해 +13%). HpAdd는 정수 체력.
    public struct Totals
    {
        public float DamagePct;
        public int HpAdd;
        public float CritPct;
        public float XpPct;
        public float CurrencyPct;
        public float CdReducePct;
        public float FlyDmgPct;      // 비행 추가피해(전역)
        public float EagleFlyDmgPct; // 독수리 비행 추가피해
        public float HealDropPct;    // 하트 드랍 확률 가산
        public bool OrbFly;          // 오브 비행 타격 가능
        public bool WhirlwindCdBonus;// 회오리 쿨감 1.5배
        public float RefreshPct;     // 리프레시 확률 가산
        public float ThunderCdPerStrike; // 낙뢰 타격당 쿨감(초)
        public int ArrowStartLevel;  // 화살 시작 레벨(0=미설정)
        public int RerollCount;      // 레벨업 리롤 횟수(게임당)
    }

    public static Totals Compute()
    {
        var t = new Totals();
        foreach (string id in SkillTreeSave.UnlockedIds())
        {
            switch (id)
            {
                // 공격력
                case "atk_1": t.DamagePct += 5f; break;
                case "atk_2": t.DamagePct += 8f; break;
                case "atk_3": t.DamagePct += 10f; break;
                // 체력
                case "hp_1": t.HpAdd += 10; break;
                case "hp_2": t.HpAdd += 15; break;
                // 치명타
                case "gate_crit": t.CritPct += 10f; break;
                case "crit_1": t.CritPct += 3f; break;
                case "crit_2": t.CritPct += 5f; break;
                // 경험치
                case "exp_1": t.XpPct += 5f; break;
                case "exp_2": t.XpPct += 8f; break;
                // 정수 획득
                case "gold_1": t.CurrencyPct += 10f; break;
                case "gold_2": t.CurrencyPct += 15f; break;
                case "gold_3": t.CurrencyPct += 25f; break;
                // 쿨타임 감소
                case "gate_cooldown": t.CdReducePct += 10f; break;
                case "cool_1": t.CdReducePct += 3f; break;
                case "cool_2": t.CdReducePct += 5f; break;
                // 비행 추가피해
                case "gate_fly": t.FlyDmgPct += 20f; break;
                case "fly_1": t.FlyDmgPct += 5f; break;
                case "fly_2": t.FlyDmgPct += 10f; break;
                case "eagle_fly": t.EagleFlyDmgPct += 30f; break;
                // 체력회복 드랍(루트) — 이 노드 해금 시에만 하트 드랍 발동, 약 1%
                case "root_hp": t.HealDropPct += 1f; break;
                // 스킬 개별강화
                case "orb_BasicFly": t.OrbFly = true; break;
                case "tornado_CoolDownBonus": t.WhirlwindCdBonus = true; break;
                case "refresh_bonus": t.RefreshPct += 5f; break;
                case "thunder_Cooldown": t.ThunderCdPerStrike += 0.1f; break;
                case "arrow_StartLev": t.ArrowStartLevel = 3; break;
                // 레벨업 리롤 (reroll_2는 reroll_1 선행이라 둘 다 +1)
                case "reroll_1": t.RerollCount += 1; break;
                case "reroll_2": t.RerollCount += 1; break;
            }
        }
        return t;
    }
}
