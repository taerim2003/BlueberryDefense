using System.Collections.Generic;
using UnityEngine;

// 노드 스킬트리의 데이터(에셋). 커스텀 에디터 창(SkillTreeEditorWindow)에서 편집하고,
// 런타임 인게임 트리 UI가 같은 에셋을 읽는다. 노드 위치(editorPos)는 인게임 레이아웃으로도 재사용.

// 노드 4종:
//   Normal        = 일반 스탯 노드(레벨제 가능). **어느 스탯인지는 노드의 effect 필드가 정한다** —
//                   코드를 안 고치고 에디터에서 축을 고를 수 있다(SkillEffects.Compute의 AddNormal 경로).
//   SkillUnlock   = 스킬 해금 노드. 해금해야 그 스킬(node.skill)이 인게임 레벨업 카드 풀에 등장
//   SkillEnhance  = 스킬 강화 노드. 스킬별 고유 강화(오브 비행타격·낙뢰 쿨감 등, id 기준 SkillEffects)
//   SpecialUnlock = 특수(기능) 해금 노드. 스킬이 아닌 기능류(리롤 등) 1회 개방. 효과는 id 기준 SkillEffects.
// (enum 순서 유지 — 기존 저장/직렬화값 흔들지 않도록 끝에만 추가할 것)
public enum SkillNodeType { Normal, SkillUnlock, SkillEnhance, SpecialUnlock }

[System.Serializable]
public class SkillNode
{
    public string id = "node";
    public string displayName = "새 노드";
    [TextArea] public string description = "";

    public SkillNodeType type = SkillNodeType.Normal;

    // SkillUnlock/SkillEnhance 노드가 대상으로 삼는 액티브 스킬.
    // SkillUnlock: 이 스킬을 레벨업 카드 풀에 해금. SkillEnhance: 어느 스킬 강화인지(표시용, 효과는 id 레지스트리).
    public ActiveSkillId skill = ActiveSkillId.BasicAttack;

    // 노드 "대역"(0=루트, 1~6). 일반 노드 이름의 숫자 I~VI와 같다.
    // 🔴 2026-09-19부터 **비용과 무관하다** — 표시·그룹용으로만 남겼다(지우면 96노드 YAML의 tier 값이 날아간다).
    public int tier = 1;

    // 이 노드의 1레벨 정수 가격. **이 값이 곧 가격이다**(2026-09-19 사용자 — 등비 공식 폐기).
    // 🔴 규칙: 모든 간선에서 자식 가격 ≥ 부모 가격. 단 자식이 해금류(SkillUnlock/SpecialUnlock)면 예외 —
    //    해금은 즉시 강해지지 않으므로 싸게 둬 빨리 찍게 유도한다(그 할인은 이 값에 이미 녹아 있다).
    // 검사: node Tools/SkillTree/verify-costs.js  (위반 0건이어야 한다)
    public int cost = 20;

    // Normal 노드가 올리는 스탯 축과 그 크기. SkillEffects.Compute가 이 둘을 그대로 읽는다.
    public MetaUpgradeId effect = MetaUpgradeId.Attack;
    // 🔴 이 노드를 사면 얹히는 양. **레벨제 폐지(2026-09-27) 후로는 "한 번에 주는 전부"다** —
    //    이름은 옛 흔적이라 남겨 두었다(에셋 96개의 YAML 키를 바꾸지 않으려고).
    public float perLevel = 5f;

    public List<string> prereqIds = new List<string>();
    public Vector2 editorPos = new Vector2(200, 200);

    // 표시 문구는 표에서 읽는다. 키는 노드 id에서 파생 — 노드를 추가하면 키도 저절로 는다.
    // 표에 없으면 에셋에 적힌 값이 그대로 나오므로, 번역이 덜 채워져도 화면이 비지 않는다.
    public string Name => Loc.TOr("tree.name." + id, displayName);
    // 🔴 일반 노드의 설명은 노드별 문구가 아니라 **효과 축 문장 + perLevel**로 짓는다(`tree.effect.{effect}` = "모든 피해가 {0}% 증가").
    //    노드별로 숫자를 적어 두면 밸런스 조정으로 perLevel이 바뀔 때 문구만 낡는다(2026-09-22 실측 47개 중 11개가 틀린 숫자를 보여줬다).
    public string Desc => type == SkillNodeType.Normal && Loc.Has("tree.effect." + effect)
        ? Loc.F("tree.effect." + effect, perLevel)
        : Loc.TOr("tree.desc." + id, description);
}

[CreateAssetMenu(fileName = "SkillTreeData", menuName = "Blueberry Defense/Skill Tree Data")]
public class SkillTreeData : ScriptableObject
{
    public List<SkillNode> nodes = new List<SkillNode>();

    public SkillNode Find(string id) => nodes.Find(n => n.id == id);
}

// ─────────────────────────────────────────────────────────────────────────────
// 스킬트리 저장/진행 상태. **자원 1개(정수)** · **되돌리기 불가**(환불/리스펙/빌드셋 없음).
//   정수(essence) = 인게임 획득. 모든 노드 비용을 정수로 지불.
// available = earned − Σ(해금된 노드 비용). earned는 단조증가.
// 승천(난이도 등급)은 별도 시스템 — 이 트리는 판을 넘어 영구히 유지되는 성장.
// ─────────────────────────────────────────────────────────────────────────────
public static class SkillTreeSave
{
    private const string EssenceKey = "meta.currency"; // 기존 정수 키 재사용(인게임 적립분 이어짐)
    private const string CurrentKey = "skilltree.current";

    // ── 정수 earned 총량 ──
    public static int EssenceEarned => SaveStore.GetInt(EssenceKey, 0);

    public static void AddEssence(int amount) => Add(EssenceKey, amount);

    private static void Add(string key, int amount)
    {
        if (amount <= 0) return;
        SaveStore.SetInt(key, SaveStore.GetInt(key, 0) + amount);
        SaveStore.Save();
    }

    // ── 노드별 레벨 (id→level, level≥1이면 보유). 저장 포맷 CSV "id:level,id:level" ──
    // 구버전 저장값(콜론 없는 순수 id)은 level 1로 흡수(마이그레이션).
    public static Dictionary<string, int> Levels() => ReadLevels(CurrentKey);

    public static int LevelOf(string id) => Levels().TryGetValue(id, out int lv) ? lv : 0;

    // 토폴로지/게이팅 코드가 그대로 쓰도록 "보유(level≥1) id 집합"을 파생 제공
    public static HashSet<string> UnlockedIds()
    {
        var set = new HashSet<string>();
        foreach (var kv in Levels()) if (kv.Value >= 1) set.Add(kv.Key);
        return set;
    }

    public static bool IsUnlocked(string id) => LevelOf(id) >= 1;

    // 🔴 **레벨제는 폐지됐다 — 모든 노드가 "구매/미구매" 2상태다**(사용자 결정 2026-09-27).
    //    예전엔 Normal 노드만 에셋의 maxLevel까지 여러 번 찍을 수 있었다. 폐지하면서
    //    노드 에셋을 **등가 변환**했다: 가격 = 옛 1~만렙 누적 비용의 합, 효과 = perLevel × 옛 만렙.
    //    그래서 트리 전체 비용(201,938)도 만렙 총효과도 그대로다.
    // 함수를 남겨 두는 이유: 호출부(업적·봇·UI)가 "다 찍었나"를 이걸로 묻는데, 1을 돌려주면 전부 그대로 맞는다.
    public static int MaxLevelOf(SkillNode n) => 1;

    // 저장된 레벨이 1보다 크면 1로 본다 — 레벨제 시절 세이브(2~N레벨)를 "구매함"으로 읽는 창구.
    // 효과·표시·지불액이 전부 이 값을 쓴다(가격 소급분은 MigrateFlattenedCosts가 따로 보정한다).
    public static int EffectiveLevel(SkillNode n, int savedLevel) => Mathf.Min(savedLevel, MaxLevelOf(n));

    // ── 스킬 해금 게이팅 ──
    // 트리에 SkillUnlock 노드로 등록된 스킬(=게이팅 대상). 여기 없는 스킬은 게이팅 안 함(캐릭터 풀 그대로).
    public static HashSet<ActiveSkillId> GatedSkills(SkillTreeData tree)
    {
        var set = new HashSet<ActiveSkillId>();
        if (tree == null) return set;
        foreach (SkillNode n in tree.nodes)
            if (n.type == SkillNodeType.SkillUnlock) set.Add(n.skill);
        return set;
    }

    // 현재 해금된 SkillUnlock 노드가 열어준 스킬 집합.
    public static HashSet<ActiveSkillId> UnlockedSkills(SkillTreeData tree)
    {
        var set = new HashSet<ActiveSkillId>();
        if (tree == null) return set;
        foreach (var kv in Levels())
        {
            SkillNode n = tree.Find(kv.Key);
            if (n != null && n.type == SkillNodeType.SkillUnlock) set.Add(n.skill);
        }
        return set;
    }

    // ── 노드 비용(정수) ──
    //   🔴 대역(tier) 등비 공식은 **폐기됐다**(2026-09-19 사용자: "3.2배 등비 공식 아예 버려").
    //      예전엔 20·64·205·655·2097·6711 여섯 값뿐이라 **같은 대역 노드가 전부 같은 가격**이었고
    //      (tier 1에 13개 노드가 전부 20정수) "8 → 15 → 20" 같은 촘촘한 간격을 만들 수가 없었다.
    //      이제 가격은 노드가 직접 갖는다(SkillNode.cost) — 96노드가 서로 다른 값이다. 검사는 `node Tools/SkillTree/verify-costs.js`.
    //   ⚠️ Max(1,…)은 안전망이다. cost가 안 적힌 옛/백업 에셋이 0으로 읽혀 노드가 공짜가 되는 걸 막는다.
    public static int CostOf(SkillNode n) => Mathf.Max(1, n.cost);

    // 구매 비용. 레벨제가 폐지돼 노드당 **한 번**만 낸다 — 곧 노드가 가진 값 그대로다.
    // (예전엔 레벨마다 1.5배씩 비싸졌다. 그 누적분은 노드 cost에 합쳐 넣었다 — MaxLevelOf 주석 참고.)
    public static int NextLevelCost(SkillTreeData tree, SkillNode n) => CostOf(n);

    // ── available 정수 = earned − Σ(해금된 노드에 지불한 정수) ──
    public static int AvailableEssence(SkillTreeData tree)
    {
        if (!flattenChecked) { flattenChecked = true; MigrateFlattenedCosts(tree); MigrateLate20Costs(tree); MigrateOct07Costs(tree); }
        return EssenceEarned - Spent(tree);
    }

    // 🔴 레벨제 폐지(2026-09-27)로 노드 가격을 전부 2.9319배 했다. 그러면 **이미 산 노드**의 지출액도
    //    소급해서 같이 오르기 때문에 available(= earned − spent)이 음수가 된다 —
    //    실제로 기존 세이브에서 화면에 `-19,356 Essence`가 찍혔다.
    //    산 노드가 옛 가격으로 냈던 몫과 새 가격의 **차액만큼 earned에 얹어**, 가진 정수를 그대로 보존한다.
    //    (플레이어는 같은 노드로 이제 만렙 몫을 받으므로 값을 더 내라고 할 이유가 없다.)
    // 한 번만 돈다 — 세이브 키로 표시하므로 두 번 얹히지 않는다.
    private const string FlattenMigrationKey = "skilltree.flatten.v1";
    private const float FlattenCostScale = 2.9319f;   // 변환 때 쓴 배율과 같아야 한다
    private static bool flattenChecked;               // 도메인 리로드마다 풀린다 — 위 키가 진짜 방어선이다

    private static void MigrateFlattenedCosts(SkillTreeData tree)
    {
        if (tree == null || SaveStore.GetInt(FlattenMigrationKey, 0) == 1) return;

        int delta = 0;
        foreach (var kv in Levels())
        {
            SkillNode n = tree.Find(kv.Key);
            if (n == null || EffectiveLevel(n, kv.Value) <= 0) continue;
            // 극후반 인상분은 MigrateLate20Costs가, 10/7 인상분은 MigrateOct07Costs가 따로 얹는다 — 여기서 새 가격을 쓰면 두 번 얹힌다.
            // 두 표에 다 있는 노드(극후반 중 5,000 이하 4개)는 더 옛 가격인 Late20 쪽을 쓴다.
            int now = Late20OldCosts.TryGetValue(n.id, out int late) ? late
                    : Oct07OldCosts.TryGetValue(n.id, out int pre) ? pre : CostOf(n);
            int old = Mathf.Max(1, Mathf.RoundToInt(now / FlattenCostScale));
            delta += now - old;
        }
        if (delta > 0) SaveStore.SetInt(EssenceKey, SaveStore.GetInt(EssenceKey, 0) + delta);
        SaveStore.SetInt(FlattenMigrationKey, 1);
        SaveStore.Save();
    }

    // 🔴 가격 상위 20개 노드의 가격을 올렸다(2026-10-06 사용자 — 가격순 n번째에 (50000/3686)^(n/20)배,
    //    가장 비싼 strength_SlowSkill이 50,000정수). 트리 총비용 114,380 → 393,378.
    //    위 flatten과 같은 문제다: 이미 산 노드의 지출액이 소급해서 올라 보유 정수가 음수가 된다.
    //    산 노드의 차액만큼 earned에 얹어 가진 정수를 그대로 보존한다. 한 번만 돈다.
    private const string Late20MigrationKey = "skilltree.late20.v1";
    // 인상 **전** 가격. 에셋의 새 가격과 짝이다 — 이 노드들의 가격을 또 바꾸면 여기가 아니라 새 보정을 만든다.
    private static readonly Dictionary<string, int> Late20OldCosts = new Dictionary<string, int>
    {
        { "knowledge_EvoHint", 2509 }, { "cool_5", 2541 }, { "exp_5", 2604 }, { "hp_5", 2635 },
        { "assassin_FullCritHit", 2667 }, { "health_HealItem", 2698 }, { "regen_1", 2713 }, { "heal_1", 2736 },
        { "swing_Knockback", 2758 }, { "heal_2", 2846 }, { "boss_4", 2871 }, { "crit_4", 3224 }, { "hp_6", 3344 },
        { "heal_3", 3375 }, { "regen_2", 3383 }, { "atk_6", 3432 }, { "accel_FastSkillDmg", 3518 },
        { "critdmg_5", 3537 }, { "defense_Revive", 3603 }, { "strength_SlowSkill", 3686 },
    };

    private static void MigrateLate20Costs(SkillTreeData tree)
    {
        if (tree == null || SaveStore.GetInt(Late20MigrationKey, 0) == 1) return;

        int delta = 0;
        foreach (var kv in Levels())
        {
            SkillNode n = tree.Find(kv.Key);
            if (n == null || EffectiveLevel(n, kv.Value) <= 0) continue;
            // 10/7에 또 오른 4개는 10/6 가격까지만 여기서 얹는다 — 그 뒤 몫은 MigrateOct07Costs가 얹는다(CostOf를 쓰면 두 번 얹힌다).
            if (!Late20OldCosts.TryGetValue(n.id, out int old)) continue;
            int after = Oct07OldCosts.TryGetValue(n.id, out int oct) ? oct : CostOf(n);
            delta += Mathf.Max(0, after - old);
        }
        if (delta > 0) SaveStore.SetInt(EssenceKey, SaveStore.GetInt(EssenceKey, 0) + delta);
        SaveStore.SetInt(Late20MigrationKey, 1);
        SaveStore.Save();
    }

    // 🔴 극후반 20개를 뺀 앞 75노드의 가격을 올렸다(2026-10-07 사용자 — 756정수 이하 53개는 1.3배,
    //    880~2,476정수 22개는 가격이 오를수록 배율이 1.3 → 1.0으로 줄어 극후반 첫 노드(2,858)에 이어진다).
    //    같은 날 1,000~5,000정수 구간을 가파르게 했다 — 1,110에서 5,118(고정) 직전까지 한 칸 6%씩 오르는 선을 긋고
    //    그보다 싼 16노드를 선까지 올렸다(2,117~2,547에 몰려 한 번에 사지던 덩어리를 편다. 극후반 중 5,000 이하 4개 포함).
    //    트리 총비용 393,378 → 410,483. 위 둘과 같은 문제라 산 노드의 차액만큼 earned에 얹는다. 한 번만 돈다.
    private const string Oct07MigrationKey = "skilltree.cost1007.v1";
    // 인상 **전** 가격. 에셋의 새 가격과 짝이다 — 이 노드들의 가격을 또 바꾸면 여기가 아니라 새 보정을 만든다.
    private static readonly Dictionary<string, int> Oct07OldCosts = new Dictionary<string, int>
    {
        { "Root_Skilltree", 3 }, { "New_Orb", 7 }, { "atk_1", 9 }, { "New_Lightning", 18 }, { "hp_1", 21 },
        { "crit_1", 26 }, { "gold_1", 27 }, { "fly_1", 32 }, { "thunder_Stack", 45 }, { "New_Evolution", 50 },
        { "New_Reroll", 53 }, { "exp_1", 55 }, { "critdmg_1", 56 }, { "cool_1", 59 }, { "reroll_2", 65 },
        { "tornado_CoolDownBonus", 73 }, { "New_Homing", 88 }, { "critdmg_2", 94 }, { "orb_BasicSlow", 150 },
        { "fly_2", 154 }, { "Sniping_TwoTarget", 160 }, { "atk_2", 182 }, { "New_Rewind", 188 },
        { "arrow_StartLev", 200 }, { "crit_2", 205 }, { "cool_2", 213 }, { "reroll_3", 229 },
        { "eagle_DropNum", 230 }, { "gold_2", 230 }, { "rewind_NoGcd", 243 }, { "Homing_MissileNum", 270 },
        { "exp_2", 279 }, { "reroll_4", 290 }, { "New_Shotgun", 300 }, { "hp_2", 320 }, { "shotgun_BonusHit", 330 },
        { "swing_StartLev", 350 }, { "atk_3", 358 }, { "gold_3", 387 }, { "hp_3", 419 }, { "knowledge_BaseXp", 449 },
        { "assassin_BaseCrit", 481 }, { "boss_1", 500 }, { "grape_StartLev", 505 }, { "critdmg_3", 510 },
        { "boss_2", 520 }, { "health_BaseHp", 542 }, { "cool_3", 570 }, { "exp_3", 620 }, { "accel_BaseCool", 695 },
        { "boss_3", 720 }, { "defense_BaseReduce", 727 }, { "strength_BaseDmg", 756 }, { "atk_4", 880 },
        { "cool_4", 962 }, { "exp_4", 1026 }, { "arrow_Pierce", 1074 }, { "hp_4", 1311 }, { "shotgun_Crit", 1364 },
        { "gold_4", 1466 }, { "tornado_Fly", 1514 }, { "grape_CloudDuration", 1790 }, { "eagle_fly", 1857 },
        { "homing_Cooldown", 1913 }, { "orb_Pierce", 1967 }, { "Rewind_Slow", 2021 }, { "sniping_Crit", 2076 },
        { "thunder_Cooldown", 2126 }, { "crit_3", 2127 }, { "reroll_5", 2178 }, { "fly_3", 2275 },
        { "critdmg_4", 2333 }, { "atk_5", 2445 }, { "New_Evolution2", 2456 }, { "gold_5", 2476 },
        // 극후반 20개 중 5,000정수 이하 4개 — 여기 값은 10/6 인상 **뒤** 가격이다(그 앞 가격은 Late20OldCosts).
        { "knowledge_EvoHint", 2858 }, { "cool_5", 3298 }, { "exp_5", 3850 }, { "hp_5", 4439 },
    };

    private static void MigrateOct07Costs(SkillTreeData tree)
    {
        if (tree == null || SaveStore.GetInt(Oct07MigrationKey, 0) == 1) return;

        int delta = 0;
        foreach (var kv in Levels())
        {
            SkillNode n = tree.Find(kv.Key);
            if (n == null || EffectiveLevel(n, kv.Value) <= 0) continue;
            if (Oct07OldCosts.TryGetValue(n.id, out int old)) delta += Mathf.Max(0, CostOf(n) - old);
        }
        if (delta > 0) SaveStore.SetInt(EssenceKey, SaveStore.GetInt(EssenceKey, 0) + delta);
        SaveStore.SetInt(Oct07MigrationKey, 1);
        SaveStore.Save();
    }

    private static int Spent(SkillTreeData tree)
    {
        if (tree == null) return 0;
        int sum = 0;
        foreach (var kv in Levels())
        {
            SkillNode n = tree.Find(kv.Key);
            if (n == null) continue;
            // EffectiveLevel은 0 아니면 1이다 — 산 노드만 한 번 센다.
            // 🔴 옛 세이브에 레벨 2 이상이 남아 있어도 1로 눌리므로, 초과 레벨에 냈던 정수는 여기서 **자동으로 돌아온다.**
            sum += CostOf(n) * EffectiveLevel(n, kv.Value);
        }
        return sum;
    }

    // ── 선행조건 충족 여부(자원 무관) ──
    public static bool PrereqMet(SkillTreeData tree, SkillNode node, HashSet<string> unlocked = null)
    {
        unlocked ??= UnlockedIds();
        foreach (string pre in node.prereqIds)
            if (!unlocked.Contains(pre)) return false;
        return true;
    }

    // ── 업그레이드(구매) 가능 여부 / 실행 ──
    // 미보유(cur 0) 구매엔 선행조건 + 자원이 필요하다. 이미 산 노드는 MaxLevelOf가 1이라 항상 false — 레벨업 구매 경로는 없다.
    public static bool CanUpgrade(SkillTreeData tree, string id)
    {
        if (tree == null) return false;
        SkillNode node = tree.Find(id);
        if (node == null) return false;

        int cur = LevelOf(id);
        if (cur >= MaxLevelOf(node)) return false;
        if (cur == 0 && !PrereqMet(tree, node)) return false;

        return AvailableEssence(tree) >= NextLevelCost(tree, node);
    }

    public static bool TryUpgrade(SkillTreeData tree, string id)
    {
        if (!CanUpgrade(tree, id)) return false;
        var levels = Levels();
        levels.TryGetValue(id, out int cur);
        levels[id] = cur + 1;
        WriteLevels(CurrentKey, levels);
        Achievements.SyncTree(tree);
        return true;
    }

    // ── 치트/디버그: 전체 초기화 (정상 플레이에는 되돌리기 없음) ──
    public static void ResetAll()
    {
        SaveStore.DeleteKey(EssenceKey);
        SaveStore.DeleteKey(CurrentKey);
        SaveStore.Save();
    }

    // ── 내부 CSV 직렬화 (id:level,id:level) ──
    private static Dictionary<string, int> ReadLevels(string key)
    {
        var map = new Dictionary<string, int>();
        string csv = SaveStore.GetString(key, "");
        if (string.IsNullOrEmpty(csv)) return map;
        foreach (string tok in csv.Split(','))
        {
            if (string.IsNullOrEmpty(tok)) continue;
            int colon = tok.IndexOf(':');
            if (colon < 0) { map[tok] = 1; continue; } // 구버전 순수 id → level 1
            string id = tok.Substring(0, colon);
            if (int.TryParse(tok.Substring(colon + 1), out int lv) && lv >= 1) map[id] = lv;
        }
        return map;
    }

    private static void WriteLevels(string key, Dictionary<string, int> map)
    {
        var parts = new List<string>();
        foreach (var kv in map) if (kv.Value >= 1) parts.Add(kv.Key + ":" + kv.Value);
        SaveStore.SetString(key, string.Join(",", parts));
        SaveStore.Save();
    }
}
