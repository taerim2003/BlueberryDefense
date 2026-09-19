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
    // 🔴 2026-09-19부터 **비용과 무관하다** — 표시·그룹용으로만 남겼다(지우면 84노드 YAML의 tier 값이 날아간다).
    public int tier = 1;

    // 이 노드의 1레벨 정수 가격. **이 값이 곧 가격이다**(2026-09-19 사용자 — 등비 공식 폐기).
    // 🔴 규칙: 모든 간선에서 자식 가격 ≥ 부모 가격. 단 자식이 해금류(SkillUnlock/SpecialUnlock)면 예외 —
    //    해금은 즉시 강해지지 않으므로 싸게 둬 빨리 찍게 유도한다(그 할인은 이 값에 이미 녹아 있다).
    // 검사: node Tools/SkillTree/verify-costs.js  (위반 0건이어야 한다)
    public int cost = 20;

    // Normal 노드가 올리는 스탯 축과 그 크기. SkillEffects.Compute가 이 둘을 그대로 읽는다.
    public MetaUpgradeId effect = MetaUpgradeId.Attack;
    public float perLevel = 5f;
    public int maxLevel = 5;

    public List<string> prereqIds = new List<string>();
    public Vector2 editorPos = new Vector2(200, 200);

    // 표시 문구는 표에서 읽는다. 키는 노드 id에서 파생 — 노드를 추가하면 키도 저절로 는다.
    // 표에 없으면 에셋에 적힌 값이 그대로 나오므로, 번역이 덜 채워져도 화면이 비지 않는다.
    public string Name => Loc.TOr("tree.name." + id, displayName);
    public string Desc => Loc.TOr("tree.desc." + id, description);
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

    // 노드 만렙: 스탯 노드(Normal)만 여러 레벨(에셋 maxLevel), 스킬 해금/강화는 1회 개방
    public static int MaxLevelOf(SkillNode n) =>
        n.type == SkillNodeType.Normal ? Mathf.Max(1, n.maxLevel) : 1;

    // 저장된 레벨이 에셋 만렙보다 높으면 만렙으로 본다 — 에셋에서 만렙을 줄인 뒤 남은 옛 세이브용.
    // 효과·표시·지불액이 전부 이 값을 쓴다. 그래서 넘친 레벨에 냈던 정수는 Spent가 안 세어 **자동으로 돌려준다.**
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
    //      이제 가격은 노드가 직접 갖는다(SkillNode.cost) — 84노드가 서로 다른 값이다.
    //   ⚠️ Max(1,…)은 안전망이다. cost가 안 적힌 옛/백업 에셋이 0으로 읽혀 노드가 공짜가 되는 걸 막는다.
    public static int CostOf(SkillNode n) => Mathf.Max(1, n.cost);

    // 레벨당 비용 성장 배율(레벨이 오를수록 비싸짐 — 레벨제 Normal 노드용)
    private const float LevelCostGrowth = 1.5f;

    // 다음 레벨(현재 level → level+1) 구매 비용. 1레벨(cur 0)=등급 비용, 이후 1.5배씩.
    public static int NextLevelCost(SkillTreeData tree, SkillNode n) =>
        Mathf.RoundToInt(CostOf(n) * Mathf.Pow(LevelCostGrowth, LevelOf(n.id)));

    // ── available 정수 = earned − Σ(해금된 노드에 지불한 정수) ──
    public static int AvailableEssence(SkillTreeData tree) => EssenceEarned - Spent(tree);

    private static int Spent(SkillTreeData tree)
    {
        if (tree == null) return 0;
        int sum = 0;
        foreach (var kv in Levels())
        {
            SkillNode n = tree.Find(kv.Key);
            if (n == null) continue;
            int baseCost = CostOf(n);
            for (int L = 0; L < EffectiveLevel(n, kv.Value); L++)
                sum += Mathf.RoundToInt(baseCost * Mathf.Pow(LevelCostGrowth, L));
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
    // 첫 레벨(cur 0) 구매엔 선행조건이 필요하고, 이미 보유(레벨업)면 만렙 미만 + 자원만 확인.
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
