using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// 아웃게임(메타 프로그레션) 런타임 계층.
// 인게임에서 번 정수(태양빛)를 저장하고(→ SkillTreeSave), 스킬트리 해금 효과를
// 다음 판 시작 시 인게임 스탯에 반영한다(→ SkillEffects/MetaRunApplier).
// MonoBehaviour 없음 — 순수 static/data. 씬 배치 컴포넌트는 MetaRunApplier가 담당.
// ─────────────────────────────────────────────────────────────────────────────

// 스킬트리 **일반(Normal) 노드**의 효과 축. 노드가 어느 스탯을 올리는지는 이 값이 정하고,
// 크기는 SkillNode.perLevel(레벨당)이 정한다 — 즉 일반 노드는 코드를 안 고치고 에디터에서 얼마든지 늘릴 수 있다.
// 🔴 **끝에만 추가할 것.** 에셋이 정수로 직렬화해서 중간에 끼우면 기존 노드의 축이 통째로 밀린다.
public enum MetaUpgradeId
{
    Attack,   // 공격력 — 모든 피해 +N%
    Health,   // 체력 — 최대 체력 +N
    Regen,    // 회복 — 5초마다 +N (트리에 노드가 생기면 그때부터 동작)
    Cooldown, // 쿨타임 — 모든 쿨 -N%
    Duration, // 지속시간 — 스킬 지속 +N%
    Xp,       // 경험 — 경험치 획득 +N%
    Wealth,   // 부유 — 정수 획득 +N%
    Crit,     // 치명타 — 치명타 확률 +N%p
    // ↓ 2026-09-03 스킬트리 재설계(칸반 「스킬트리 재설계」)에서 추가한 축.
    FlyDamage,  // 비행 추가피해 — 비행 적에게 +N%
    CritDamage, // 치명타 피해 — 치명타 배율에 +N%p(기본 배율 3.0에 더해진다)
    BossDamage, // 보스 추가피해 — 보스에게 +N%
    Reroll,     // 리롤 횟수 — 게임당 리롤 +N회(레벨당)
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
    public static float WhirlwindFlyDamageBonus = 0f; // 회오리의 비행 적 추가 피해(전역 위에 더해짐)
    public static float BossDamageBonus = 0f;     // 보스 적 추가 피해 배율 가산(0~)
    public static float CritDamageBonus = 0f;     // 치명타 피해 배율 가산 — PlayerPassives.AssassinateCritMultiplier에 더해진다
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

    // ── 2026-09-03 스킬트리 재설계: 스킬 강화(SkillEnhance) 노드가 켜는 것들 ──
    // 액티브
    public static int ArrowExtraPierce = 0;         // 화살: 기본 관통 +N
    public static float SwingKnockbackMult = 1f;    // 휘두르기: 넉백 배율
    public static bool OrbSlowBoost = false;        // 오브: 기본 둔화 강화(감속률·지속 둘 다)
    public static int OrbExtraTargets = 0;          // 오브: 붙잡는 적 수 +N("관통 +3")
    public static int EagleExtraDrops = 0;          // 독수리 투하: 투하 횟수 +N
    public static bool ThunderStackable = false;    // 번개: 낙뢰 버프 중첩(스택당 피해 증가) 개방
    public static int ShotgunExtraBonusHit = 0;     // 산탄: 타수 버프가 주는 타수 +N
    public static float ShotgunCritBonus = 0f;      // 산탄: 이 스킬 전용 치명타 확률 가산
    public static float SnipingCritBonus = 0f;      // 스나이핑: 이 스킬 전용 치명타 확률 가산
    public static float HomingCooldownCut = 0f;     // 호밍: 기본 쿨타임 -N초(감소율보다 먼저 빠진다)
    public static bool RewindSkipsGlobalCooldown = false; // 되감기: 전역 쿨타임을 트리거하지 않음

    // 패시브 — "기본값 +N" 은 획득 시 적용되는 baseValue에 얹힌다(PlayerPassives.BaseValue가 읽는다).
    public static float PassiveBaseStrength = 0f;   // 힘: 기본 피해 배율 +N
    public static float PassiveBaseHealth = 0f;     // 건강: 기본 최대체력 +N
    public static float PassiveBaseKnowledge = 0f;  // 지식: 기본 경험치 배율 +N
    public static float PassiveBaseAssassinate = 0f;// 암살: 기본 치명타 확률 +N
    public static float PassiveBaseDefense = 0f;    // 방어: 기본 받는 피해 감소 +N
    public static float PassiveBaseAccel = 0f;      // 가속: 기본 쿨타임 감소 +N

    public static bool HealItemDouble = false;      // 건강: 체력회복템 회복량 2배
    public static bool StrengthSlowSkillDouble = false; // 힘: 쿨 5초 이상 스킬에 힘 피해 배율을 2배로
    public static bool AssassinFullCritExtraHit = false;// 암살: 치명타 확률 100%인 스킬은 타수 +1
    public static bool DefenseRevive = false;       // 방어: 사망 시 1회 부활
    public static bool AccelFastSkillDamage = false;// 가속: 쿨 4초 이하 스킬 피해 +30%
    public static bool ShowEvolutionHint = false;   // 지식: 레벨업 카드에 진화 조건 표시

    // ── 기타 기능 해금(SpecialUnlock) ──
    // 🔴 기본값 true. 트리에 해당 게이트 노드가 **없으면 게이팅 자체를 안 한다**(스킬 해금 게이팅과 같은 원칙) —
    //    노드를 아직 안 만든 트리에서 진화가 통째로 막히는 사고를 막는다. 노드가 있으면 SkillEffects가 false로 내린다.
    public static bool EvolutionUnlocked = true;    // 1차 진화 개방
    public static bool Evolution2Unlocked = true;   // 2차 진화 개방

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
        WhirlwindFlyDamageBonus = 0f;
        BossDamageBonus = 0f;
        CritDamageBonus = 0f;
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

        ArrowExtraPierce = 0;
        SwingKnockbackMult = 1f;
        OrbSlowBoost = false;
        OrbExtraTargets = 0;
        EagleExtraDrops = 0;
        ThunderStackable = false;
        ShotgunExtraBonusHit = 0;
        ShotgunCritBonus = 0f;
        SnipingCritBonus = 0f;
        HomingCooldownCut = 0f;
        RewindSkipsGlobalCooldown = false;

        PassiveBaseStrength = 0f;
        PassiveBaseHealth = 0f;
        PassiveBaseKnowledge = 0f;
        PassiveBaseAssassinate = 0f;
        PassiveBaseDefense = 0f;
        PassiveBaseAccel = 0f;

        HealItemDouble = false;
        StrengthSlowSkillDouble = false;
        AssassinFullCritExtraHit = false;
        DefenseRevive = false;
        AccelFastSkillDamage = false;
        ShowEvolutionHint = false;

        EvolutionUnlocked = true;
        Evolution2Unlocked = true;
    }

    // 패시브 획득 시 기본값에 얹히는 스킬트리 보너스(PlayerPassives.BaseValue의 창구).
    public static float PassiveBaseBonus(PassiveSkillId id) => id switch
    {
        PassiveSkillId.Strength => PassiveBaseStrength,
        PassiveSkillId.Health => PassiveBaseHealth,
        PassiveSkillId.Knowledge => PassiveBaseKnowledge,
        PassiveSkillId.Assassinate => PassiveBaseAssassinate,
        PassiveSkillId.Defense => PassiveBaseDefense,
        PassiveSkillId.Accel => PassiveBaseAccel,
        _ => 0f,
    };
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
// 스킬트리 해금집합 → 인게임 효과. MetaRunApplier가 판 시작 시 한 번 호출한다.
//
// 🔴 노드 종류에 따라 효과를 정하는 주체가 다르다:
//   Normal(일반)  = **에셋이 정한다.** SkillNode.effect(축) × perLevel(레벨당 크기) × 노드 레벨.
//                   → 공격력·체력·치명타·치명타 피해·경험치·정수·쿨타임·비행·보스·리롤 노드를
//                     에디터에서 몇 개를 만들든 코드를 안 고쳐도 된다(재설계 목표 100개 대응).
//   그 외(해금·강화·기타) = **코드가 정한다.** 노드 하나하나가 고유 동작이라 아래 id 스위치가 진실원.
//                   → 에디터에서 노드를 만들 때 id를 여기 적힌 것과 **똑같이** 쳐야 효과가 붙는다.
//
// ⚠️ Normal 노드는 tree 에셋을 뒤져야 축을 알 수 있다 — tree가 null이면 일반 노드는 전부 무효다.
// ─────────────────────────────────────────────────────────────────────────────
public static class SkillEffects
{
    // %는 정수 퍼센트값으로 누적(예: DamagePct=13 → 피해 +13%). HpAdd/RegenPer5s는 정수.
    public struct Totals
    {
        // ── 일반 노드가 채우는 축(에셋 주도) ──
        public float DamagePct;
        public int HpAdd;
        public int RegenPer5s;
        public float CritPct;
        public float CritDmgPct;     // 치명타 피해 배율 가산(%p — 100이면 배율 +1.0)
        public float XpPct;
        public float CurrencyPct;
        public float CdReducePct;
        public float DurationPct;
        public float FlyDmgPct;      // 비행 추가피해(전역)
        public float BossDmgPct;     // 보스 추가피해
        public int RerollCount;      // 레벨업 리롤 횟수(게임당)

        // ── 강화/해금 노드가 채우는 것(코드 주도) ──
        public float EagleFlyDmgPct;     // 독수리 비행 추가피해
        public float WhirlwindFlyDmgPct; // 회오리 비행 추가피해
        public float HealDropPct;        // 하트 드랍 확률 가산
        public bool OrbFly;              // 오브 비행 타격 가능
        public bool HomingGrowth;        // 호밍 10회 사용마다 미사일 +1
        public bool WhirlwindCdBonus;    // 회오리 쿨감 1.5배
        public float AccelCdPct;         // 가속 패시브 추가 쿨감(%p)
        public float ThunderCdPerStrike; // 낙뢰 타격당 쿨감(초)
        public int ArrowStartLevel;      // 화살 시작 레벨(0=미설정)
        public int SwingStartLevel;      // 휘두르기 시작 레벨(0=미설정)
        public bool SnipingExtraTarget;  // 스나이핑 타겟 +1
        public bool RewindSlow;          // 되감기 시 모든 적 둔화
        public bool ShotgunClose;        // 산탄 버프 근거리 +2타

        public int ArrowPierce;          // 화살 기본 관통 +N
        public float SwingKnockbackMult; // 휘두르기 넉백 배율(1=기본)
        public bool OrbSlowBoost;        // 오브 기본 둔화 강화
        public int OrbTargets;           // 오브 붙잡는 적 수 +N
        public int EagleDrops;           // 독수리 투하 횟수 +N
        public bool ThunderStack;        // 낙뢰 버프 중첩 개방
        public int ShotgunBonusHit;      // 산탄 타수 버프 +N
        public float ShotgunCritPct;     // 산탄 전용 치명타 확률(%p)
        public float SnipingCritPct;     // 스나이핑 전용 치명타 확률(%p)
        public float HomingCdCut;        // 호밍 기본 쿨 -N초
        public bool RewindNoGcd;         // 되감기가 전역 쿨타임을 안 건다

        public float PassiveStrength;    // 힘 기본 피해 배율 +
        public float PassiveHealth;      // 건강 기본 최대체력 +
        public float PassiveKnowledge;   // 지식 기본 경험치 배율 +
        public float PassiveAssassinate; // 암살 기본 치명타 확률 +
        public float PassiveDefense;     // 방어 기본 피해 감소 +
        public float PassiveAccel;       // 가속 기본 쿨감 +

        public bool HealItemDouble;
        public bool StrengthSlowSkillDouble;
        public bool AssassinFullCritExtraHit;
        public bool DefenseRevive;
        public bool AccelFastSkillDamage;
        public bool ShowEvolutionHint;

        public bool EvolutionUnlocked;
        public bool Evolution2Unlocked;
    }

    // ── 기타 기능 해금 노드의 id (SpecialUnlock) ──
    // 트리에 이 id의 노드가 **없으면 게이팅을 안 한다** — 노드를 아직 안 만든 트리에서
    // 진화가 통째로 막히는 사고를 막기 위한 원칙(스킬 해금 게이팅과 같다).
    public const string EvolutionNodeId = "New_Evolution";   // 1차 진화 개방
    public const string Evolution2NodeId = "New_Evolution2"; // 2차 진화 개방

    public static Totals Compute(SkillTreeData tree)
    {
        var t = new Totals();
        t.SwingKnockbackMult = 1f;

        // 게이트 노드가 트리에 없으면 잠그지 않는다(위 원칙). 있으면 해금 여부가 곧 개방 여부.
        t.EvolutionUnlocked = tree == null || tree.Find(EvolutionNodeId) == null;
        t.Evolution2Unlocked = tree == null || tree.Find(Evolution2NodeId) == null;

        foreach (var kv in SkillTreeSave.Levels())
        {
            string id = kv.Key;
            int lv = kv.Value;
            SkillNode node = tree != null ? tree.Find(id) : null;

            // ① 일반 노드 — 축과 크기를 에셋이 들고 있다. 코드는 축을 스탯에 꽂아 주기만 한다.
            if (node != null && node.type == SkillNodeType.Normal)
            {
                AddNormal(ref t, node.effect, node.perLevel * lv);
                continue;
            }

            // ② 그 외 — 노드마다 고유 동작이라 id가 곧 계약이다.
            //    (SkillUnlock 노드는 여기 없다 — 카드 풀 게이팅은 SkillTreeSave.UnlockedSkills가 따로 본다.)
            switch (id)
            {
                // ── 기타 기능 해금 ──
                case EvolutionNodeId: t.EvolutionUnlocked = true; break;
                case Evolution2NodeId: t.Evolution2Unlocked = true; break;
                case "New_Reroll": t.RerollCount += 1; break;

                // ── 화살 쏘기(기본공격) ──
                case "arrow_StartLev": t.ArrowStartLevel = 3; break;
                case "arrow_Pierce": t.ArrowPierce += 1; break;

                // ── 휘두르기 ──
                case "swing_StartLev": t.SwingStartLevel = 3; break;
                case "swing_Knockback": t.SwingKnockbackMult = 1.5f; break;

                // ── 오브 ──
                // ⚠️ 기본 오브는 **이미 둔화를 건다**(Orb.cs slowMultiplier 0.65 / 2초). 그래서 이 노드는
                //    "둔화를 켠다"가 아니라 **둔화를 강화**한다(지식 루트 T1과 같은 크기: 감속 +0.1, 지속 +0.5초).
                case "orb_BasicSlow": t.OrbSlowBoost = true; break;
                case "orb_Pierce": t.OrbTargets += 3; break;
                case "orb_BasicFly": t.OrbFly = true; break; // 구 트리 노드 — 재설계 목록엔 없지만 살려 둔다

                // ── 독수리 투하 ──
                case "eagle_DropNum": t.EagleDrops += 1; break;
                case "eagle_fly": t.EagleFlyDmgPct += 30f; break;

                // ── 번개 ──
                case "thunder_Stack": t.ThunderStack = true; break;
                case "thunder_Cooldown": t.ThunderCdPerStrike += 0.01f; break;

                // ── 산탄 ──
                case "shotgun_BonusHit": t.ShotgunBonusHit += 1; break;
                case "shotgun_Crit": t.ShotgunCritPct += 30f; break;
                case "Shotgun_CloseBonus": t.ShotgunClose = true; break; // 구 트리 노드

                // ── 스나이핑 ──
                case "Sniping_TwoTarget": t.SnipingExtraTarget = true; break;
                case "sniping_Crit": t.SnipingCritPct += 30f; break;

                // ── 회오리 ──
                case "tornado_CoolDownBonus": t.WhirlwindCdBonus = true; break;
                case "tornado_Fly": t.WhirlwindFlyDmgPct += 30f; break;

                // ── 호밍 ──
                case "Homing_MissileNum": t.HomingGrowth = true; break;
                case "homing_Cooldown": t.HomingCdCut += 1f; break;

                // ── 되감기 ──
                case "rewind_NoGcd": t.RewindNoGcd = true; break;
                case "Rewind_Slow": t.RewindSlow = true; break;

                // ── 패시브 강화 ──
                case "health_BaseHp": t.PassiveHealth += 30f; break;
                case "health_HealItem": t.HealItemDouble = true; break;
                case "strength_BaseDmg": t.PassiveStrength += 0.05f; break;
                case "strength_SlowSkill": t.StrengthSlowSkillDouble = true; break;
                case "assassin_BaseCrit": t.PassiveAssassinate += 0.10f; break;
                case "assassin_FullCritHit": t.AssassinFullCritExtraHit = true; break;
                case "defense_BaseReduce": t.PassiveDefense += 0.10f; break;
                case "defense_Revive": t.DefenseRevive = true; break;
                case "accel_BaseCool": t.PassiveAccel += 0.05f; break;
                case "accel_FastSkillDmg": t.AccelFastSkillDamage = true; break;
                case "knowledge_BaseXp": t.PassiveKnowledge += 0.10f; break;
                case "knowledge_EvoHint": t.ShowEvolutionHint = true; break;

                // 구 트리 잔재 — 하트 드랍(루트)·가속 쿨감. 재설계 목록엔 없지만 노드가 남아 있으면 계속 동작한다.
                case "root_hp": t.HealDropPct += 4f * lv; break;
                case "refresh_bonus": t.AccelCdPct += 2f * lv; break;
            }
        }
        return t;
    }

    // 일반 노드의 효과 축 → 누적 스탯. amount 는 이미 (레벨당 × 노드 레벨)로 곱해져 들어온다.
    private static void AddNormal(ref Totals t, MetaUpgradeId effect, float amount)
    {
        switch (effect)
        {
            case MetaUpgradeId.Attack: t.DamagePct += amount; break;
            case MetaUpgradeId.Health: t.HpAdd += Mathf.RoundToInt(amount); break;
            case MetaUpgradeId.Regen: t.RegenPer5s += Mathf.RoundToInt(amount); break;
            case MetaUpgradeId.Cooldown: t.CdReducePct += amount; break;
            case MetaUpgradeId.Duration: t.DurationPct += amount; break;
            case MetaUpgradeId.Xp: t.XpPct += amount; break;
            case MetaUpgradeId.Wealth: t.CurrencyPct += amount; break;
            case MetaUpgradeId.Crit: t.CritPct += amount; break;
            case MetaUpgradeId.FlyDamage: t.FlyDmgPct += amount; break;
            case MetaUpgradeId.CritDamage: t.CritDmgPct += amount; break;
            case MetaUpgradeId.BossDamage: t.BossDmgPct += amount; break;
            case MetaUpgradeId.Reroll: t.RerollCount += Mathf.RoundToInt(amount); break;
        }
    }
}
