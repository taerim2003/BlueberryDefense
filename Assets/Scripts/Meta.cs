using System.Collections.Generic;
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
    public static bool HomingMissileGrowth = false;     // 호밍: 10회 사용마다 미사일 +1 (스킬트리 해금 시에만)
    public static bool WhirlwindCooldownBonus = false;  // 회오리는 쿨타임 감소 효과를 1.5배로 받음
    public static float AccelCooldownBonus = 0f;        // 가속 패시브 보유 시 추가되는 쿨타임 감소 가산(0~1)
    public static float ThunderCooldownPerStrike = 0f;  // 낙뢰 1회 타격마다 낙뢰 쿨타임 감소(초)
    public static bool SnipingExtraTarget = false;      // 스나이핑 저격 타겟 +1
    public static bool RewindSlowAll = false;           // 되감기 사용 시 모든 적 둔화
    public static bool ShotgunCloseBonus = false;       // 산탄 버프 받은 공격이 근거리 적에게 +2타
    public static int ArrowStartLevel = 1;              // 기본공격(화살) 시작 레벨
    public static int SwingStartLevel = 1;              // 기본공격(휘두르기) 시작 레벨 — 파인애플용
    public static int RerollCount = 0;                  // 레벨업 선택지 리롤 가능 횟수(게임당)

    // 스킬 해금 게이팅(스킬트리): Gated=트리에 해금 노드가 있는 스킬 / TreeUnlocked=그중 실제 해금된 것.
    // MetaRunApplier가 판 시작 시 채움. 비어 있으면(트리 미연결) 게이팅 안 함 = 현행.
    public static readonly HashSet<ActiveSkillId> GatedSkills = new();
    public static readonly HashSet<ActiveSkillId> TreeUnlockedSkills = new();

    // 인게임 레벨업 카드에 이 스킬을 후보로 띄워도 되는지: 게이팅 대상이 아니거나 이미 해금됐으면 OK.
    public static bool SkillUnlockedForRun(ActiveSkillId id) =>
        !GatedSkills.Contains(id) || TreeUnlockedSkills.Contains(id);

    public static void Reset()
    {
        GatedSkills.Clear();
        TreeUnlockedSkills.Clear();
        CooldownMult = 1f;
        DurationMult = 1f;
        CritBonus = 0f;
        CurrencyMult = 1f;
        RegenPer5s = 0;
        FlyDamageBonus = 0f;
        EagleFlyDamageBonus = 0f;
        HealDropChanceBonus = 0f;
        OrbCanHitFlying = false;
        HomingMissileGrowth = false;
        WhirlwindCooldownBonus = false;
        AccelCooldownBonus = 0f;
        ThunderCooldownPerStrike = 0f;
        SnipingExtraTarget = false;
        RewindSlowAll = false;
        ShotgunCloseBonus = false;
        ArrowStartLevel = 1;
        SwingStartLevel = 1;
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
        public bool HomingGrowth;    // 호밍 10회 사용마다 미사일 +1
        public bool WhirlwindCdBonus;// 회오리 쿨감 1.5배
        public float AccelCdPct;     // 가속 패시브 추가 쿨감(%p)
        public float ThunderCdPerStrike; // 낙뢰 타격당 쿨감(초)
        public int ArrowStartLevel;  // 화살 시작 레벨(0=미설정)
        public int SwingStartLevel;  // 휘두르기 시작 레벨(0=미설정)
        public int RerollCount;      // 레벨업 리롤 횟수(게임당)
        public bool SnipingExtraTarget; // 스나이핑 타겟 +1
        public bool RewindSlow;      // 되감기 시 모든 적 둔화
        public bool ShotgunClose;    // 산탄 버프 근거리 +2타
    }

    public static Totals Compute()
    {
        var t = new Totals();
        // 스탯 노드(Normal)는 노드 레벨(lv)만큼 효과가 누적된다. Gate/ActiveSkill 토글 노드는 lv가 항상 1이라 ×1.
        foreach (var kv in SkillTreeSave.Levels())
        {
            string id = kv.Key;
            int lv = kv.Value;
            switch (id)
            {
                // 공격력
                case "atk_1": t.DamagePct += 5f * lv; break;
                case "atk_2": t.DamagePct += 8f * lv; break;
                case "atk_3": t.DamagePct += 10f * lv; break;
                // 체력
                case "hp_1": t.HpAdd += 10 * lv; break;
                case "hp_2": t.HpAdd += 15 * lv; break;
                // 치명타
                case "gate_crit": t.CritPct += 10f; break;
                case "crit_1": t.CritPct += 3f * lv; break;
                case "crit_2": t.CritPct += 5f * lv; break;
                // 경험치
                case "exp_1": t.XpPct += 5f * lv; break;
                case "exp_2": t.XpPct += 8f * lv; break;
                // 정수 획득
                case "gold_1": t.CurrencyPct += 10f * lv; break;
                case "gold_2": t.CurrencyPct += 15f * lv; break;
                case "gold_3": t.CurrencyPct += 25f * lv; break;
                // 쿨타임 감소
                case "gate_cooldown": t.CdReducePct += 10f; break;
                case "cool_1": t.CdReducePct += 3f * lv; break;
                case "cool_2": t.CdReducePct += 5f * lv; break;
                // 비행 추가피해
                case "gate_fly": t.FlyDmgPct += 20f; break;
                case "fly_1": t.FlyDmgPct += 5f * lv; break;
                case "fly_2": t.FlyDmgPct += 10f * lv; break;
                case "eagle_fly": t.EagleFlyDmgPct += 30f; break;
                // 체력회복 드랍(루트) — 레벨당 +4%
                case "root_hp": t.HealDropPct += 4f * lv; break;
                // 스킬 개별강화(토글, maxLevel 1)
                case "orb_BasicFly": t.OrbFly = true; break;
                case "Homing_MissileNum": t.HomingGrowth = true; break;
                case "tornado_CoolDownBonus": t.WhirlwindCdBonus = true; break;
                case "Sniping_TwoTarget": t.SnipingExtraTarget = true; break;
                case "Rewind_Slow": t.RewindSlow = true; break;
                case "Shotgun_CloseBonus": t.ShotgunClose = true; break;
                // 리프레쉬 폐지(2026-08-06)로 가속 패시브에 재배선. 노드 id·에셋 텍스트는 트리 쪽이라 그대로다.
                // ⚠️ 확률 5%p/레벨을 쿨감 5%p/레벨로 그대로 옮기면 과하다(maxLevel 5 → 25%p) → 2%p/레벨로 낮춤.
                case "refresh_bonus": t.AccelCdPct += 2f * lv; break;
                case "thunder_Cooldown": t.ThunderCdPerStrike += 0.01f; break;
                case "arrow_StartLev": t.ArrowStartLevel = 3; break;
                case "swing_StartLev": t.SwingStartLevel = 3; break;
                // 레벨업 리롤: New_Reroll(해금)=첫 리롤 +1, reroll_1/reroll_2(리롤 I)=레벨당 추가
                case "New_Reroll": t.RerollCount += 1; break;
                case "reroll_1": t.RerollCount += 1 * lv; break;
                case "reroll_2": t.RerollCount += 1 * lv; break;
            }
        }
        return t;
    }
}
