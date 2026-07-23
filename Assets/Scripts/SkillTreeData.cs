using System.Collections.Generic;
using UnityEngine;

// 노드 스킬트리의 데이터(에셋). 커스텀 에디터 창(SkillTreeEditorWindow)에서 편집하고,
// 런타임 인게임 트리 UI가 같은 에셋을 읽는다. 노드 위치(editorPos)는 인게임 레이아웃으로도 재사용.

// 노드 4종:
//   Normal        = 일반 스탯 노드(레벨제 가능, 효과는 id 기준 SkillEffects 레지스트리)
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

    // 노드 비용 "등급"(1,2,3…). 노드마다 정수 비용을 직접 치지 않고 등급만 지정한다.
    // 실제 정수 비용은 SkillTreeSave.TierCost가 등급→비용 선형변환으로 계산(공식 상수만 바꾸면 전체 밸런싱).
    // 인게임엔 계산된 정수만 보이고 등급은 노출 안 함.
    public int tier = 1;

    // 효과(스탯 반영) 메모 — 실제 효과는 id 기준 SkillEffects 레지스트리(effect 필드는 신뢰 안 함).
    public bool hasEffect = true;
    public MetaUpgradeId effect = MetaUpgradeId.Attack;
    public float perLevel = 5f;
    public int maxLevel = 5;

    public List<string> prereqIds = new List<string>();
    public Vector2 editorPos = new Vector2(200, 200);

    public float TotalAt(int level) => perLevel * level;
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
    public static int EssenceEarned => PlayerPrefs.GetInt(EssenceKey, 0);

    public static void AddEssence(int amount) => Add(EssenceKey, amount);

    private static void Add(string key, int amount)
    {
        if (amount <= 0) return;
        PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + amount);
        PlayerPrefs.Save();
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

    // 인게임 카드 풀 게이팅 판정: 게이팅 대상이 아니거나(=트리에 unlock 노드 없음) 이미 해금됐으면 사용 가능.
    public static bool SkillAvailable(SkillTreeData tree, ActiveSkillId id) =>
        !GatedSkills(tree).Contains(id) || UnlockedSkills(tree).Contains(id);

    // ── 노드 비용(정수) ── 등급(tier) 기반 선형변환.
    //   tier 0 = 1정수 고정(극초반 해금용). tier 1 = TierCostBase, 이후 등급마다 +TierCostStep (등속).
    //   예) base 30·step 10 → 0=1, 1=30, 2=40, 3=50 … 밸런싱은 이 두 상수만 조정하면 전체 등급에 반영된다.
    public const int TierCostBase = 30;
    public const int TierCostStep = 10;

    public static int TierCost(int tier) => tier <= 0 ? 1 : TierCostBase + (tier - 1) * TierCostStep;

    // 노드의 기본(1레벨) 비용 = 등급 비용.
    public static int CostOf(SkillNode n) => TierCost(n.tier);

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
            for (int L = 0; L < kv.Value; L++)
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
        PlayerPrefs.DeleteKey(EssenceKey);
        PlayerPrefs.DeleteKey(CurrentKey);
        PlayerPrefs.Save();
    }

    // ── 내부 CSV 직렬화 (id:level,id:level) ──
    private static Dictionary<string, int> ReadLevels(string key)
    {
        var map = new Dictionary<string, int>();
        string csv = PlayerPrefs.GetString(key, "");
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
        PlayerPrefs.SetString(key, string.Join(",", parts));
        PlayerPrefs.Save();
    }
}
