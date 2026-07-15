using System.Collections.Generic;
using UnityEngine;

// 노드 스킬트리의 데이터(에셋). 커스텀 에디터 창(SkillTreeEditorWindow)에서 편집하고,
// 런타임 인게임 트리 UI가 같은 에셋을 읽는다. 노드 위치(editorPos)는 인게임 레이아웃으로도 재사용.

// ActiveSkill = 루트 끝에 배치하는 "특정 액티브 스킬 강화" 노드 (enum 끝에 추가 — 기존 저장값 유지)
public enum SkillNodeType { Normal, Gate, ActiveSkill }

[System.Serializable]
public class SkillNode
{
    public string id = "node";
    public string displayName = "새 노드";
    [TextArea] public string description = "";

    public SkillNodeType type = SkillNodeType.Normal;

    // 효과(스탯 반영) — Gate 노드나 순수 분기 노드는 hasEffect=false
    public bool hasEffect = true;
    public MetaUpgradeId effect = MetaUpgradeId.Attack;
    public float perLevel = 5f;
    public int maxLevel = 5;

    // 일반 노드 비용(정수), Gate 노드 비용(태양 결정)
    public int cost = 50;
    public float costGrowth = 1.5f;
    public int gateCost = 1;

    public List<string> prereqIds = new List<string>();
    public Vector2 editorPos = new Vector2(200, 200);

    public int CostForLevel(int level) => Mathf.RoundToInt(cost * Mathf.Pow(costGrowth, level));
    public float TotalAt(int level) => perLevel * level;
}

[CreateAssetMenu(fileName = "SkillTreeData", menuName = "Blueberry Defense/Skill Tree Data")]
public class SkillTreeData : ScriptableObject
{
    public List<SkillNode> nodes = new List<SkillNode>();

    public SkillNode Find(string id) => nodes.Find(n => n.id == id);
}

// 노드가 소비하는 자원 종류. Normal→정수, Gate→태양결정, ActiveSkill→가루.
public enum SkillResource { Essence, Crystal, Powder }

// ─────────────────────────────────────────────────────────────────────────────
// 스킬트리 저장/진행 상태. 모든 노드는 일회성 개방(레벨 없음).
//   정수(essence)   = 인게임 획득, 일반 노드 비용(루트에 가까울수록 싸고 가장자리로 갈수록 비쌈)
//   태양결정(crystal) = 스테이지15/20 클리어, 게이트 노드 비용(node.gateCost)
//   가루(powder)    = 스테이지 클리어마다 +1, ActiveSkill(스킬강화) 노드 비용(개당 1)
// available = earned − Σ(해금된 노드 비용). earned는 단조증가라 유효했던 빌드는 항상 재구매 가능.
// 상세 규칙은 SKILLTREE_DESIGN.md 참고.
// ─────────────────────────────────────────────────────────────────────────────
public static class SkillTreeSave
{
    private const string EssenceKey = "meta.currency"; // 기존 정수 키 재사용(인게임 적립분 이어짐)
    private const string CrystalKey = "meta.crystal";
    private const string PowderKey = "meta.powder";
    private const string CurrentKey = "skilltree.current";
    private const string BuildPrefix = "skilltree.build.";
    public const int BuildSlots = 5;

    // ── 자원 earned 총량 ──
    public static int EssenceEarned => PlayerPrefs.GetInt(EssenceKey, 0);
    public static int CrystalEarned => PlayerPrefs.GetInt(CrystalKey, 0);
    public static int PowderEarned => PlayerPrefs.GetInt(PowderKey, 0);

    public static void AddEssence(int amount) => Add(EssenceKey, amount);
    public static void AddCrystal(int amount) => Add(CrystalKey, amount);
    public static void AddPowder(int amount) => Add(PowderKey, amount);

    private static void Add(string key, int amount)
    {
        if (amount <= 0) return;
        PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + amount);
        PlayerPrefs.Save();
    }

    // ── 현재 해금 집합 ──
    public static HashSet<string> UnlockedIds() => ReadSet(CurrentKey);

    public static bool IsUnlocked(string id) => UnlockedIds().Contains(id);

    // ── 노드 비용/자원 종류 ──
    public static SkillResource ResourceOf(SkillNode n) =>
        n.type == SkillNodeType.Gate ? SkillResource.Crystal :
        n.type == SkillNodeType.ActiveSkill ? SkillResource.Powder : SkillResource.Essence;

    public static int CostOf(SkillTreeData tree, SkillNode n) =>
        n.type == SkillNodeType.Gate ? n.gateCost :
        n.type == SkillNodeType.ActiveSkill ? 1 :
        EssenceCost(tree, n);

    // 정수 비용: 루트로부터의 깊이가 깊을수록 비쌈. 30 · 1.4^depth
    public static int EssenceCost(SkillTreeData tree, SkillNode n) =>
        Mathf.RoundToInt(30f * Mathf.Pow(1.4f, Depth(tree, n.id)));

    // 루트로부터 최단 선행 거리(루트=0). 순수 데이터라 UI/저장 양쪽에서 씀.
    public static int Depth(SkillTreeData tree, string id) => DepthRec(tree, id, new HashSet<string>());

    private static int DepthRec(SkillTreeData tree, string id, HashSet<string> visiting)
    {
        SkillNode n = tree != null ? tree.Find(id) : null;
        if (n == null || n.prereqIds.Count == 0) return 0;
        if (!visiting.Add(id)) return 0; // 사이클 가드
        int min = int.MaxValue;
        foreach (string p in n.prereqIds) min = Mathf.Min(min, DepthRec(tree, p, visiting) + 1);
        visiting.Remove(id);
        return min == int.MaxValue ? 0 : min;
    }

    // ── available 자원 ──
    public static int AvailableEssence(SkillTreeData tree) => EssenceEarned - Spent(tree, SkillResource.Essence);
    public static int AvailableCrystal(SkillTreeData tree) => CrystalEarned - Spent(tree, SkillResource.Crystal);
    public static int AvailablePowder(SkillTreeData tree) => PowderEarned - Spent(tree, SkillResource.Powder);

    public static int Available(SkillTreeData tree, SkillResource res) => res switch
    {
        SkillResource.Crystal => AvailableCrystal(tree),
        SkillResource.Powder => AvailablePowder(tree),
        _ => AvailableEssence(tree),
    };

    private static int Spent(SkillTreeData tree, SkillResource res)
    {
        if (tree == null) return 0;
        int sum = 0;
        foreach (string id in UnlockedIds())
        {
            SkillNode n = tree.Find(id);
            if (n == null || ResourceOf(n) != res) continue;
            sum += CostOf(tree, n);
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

    // ── 해금 가능 여부 / 실행 ──
    public static bool CanUnlock(SkillTreeData tree, string id)
    {
        if (tree == null) return false;
        SkillNode node = tree.Find(id);
        if (node == null) return false;

        HashSet<string> unlocked = UnlockedIds();
        if (unlocked.Contains(id) || !PrereqMet(tree, node, unlocked)) return false;

        return Available(tree, ResourceOf(node)) >= CostOf(tree, node);
    }

    public static bool TryUnlock(SkillTreeData tree, string id)
    {
        if (!CanUnlock(tree, id)) return false;
        HashSet<string> set = UnlockedIds();
        set.Add(id);
        WriteSet(CurrentKey, set);
        return true;
    }

    // ── 환불: 노드 + 그 노드에서 파생되는 모든 해금된 자식 노드를 함께 해제(자원 자동 환급) ──
    public static bool RefundNode(SkillTreeData tree, string id)
    {
        if (tree == null) return false;
        HashSet<string> set = UnlockedIds();
        if (!set.Contains(id)) return false;

        var toRemove = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(id);
        while (stack.Count > 0)
        {
            string cur = stack.Pop();
            if (!toRemove.Add(cur)) continue;
            foreach (SkillNode n in tree.nodes)
                if (set.Contains(n.id) && !toRemove.Contains(n.id) && n.prereqIds.Contains(cur))
                    stack.Push(n.id);
        }
        set.ExceptWith(toRemove);
        WriteSet(CurrentKey, set);
        return true;
    }

    // ── 리스펙(리셋): 현재 해금 초기화 → available 자동 환급 ──
    public static void Respec()
    {
        PlayerPrefs.DeleteKey(CurrentKey);
        PlayerPrefs.Save();
    }

    // ── 빌드셋 슬롯(1~BuildSlots) ──
    public static bool BuildEmpty(int slot) => ReadSet(BuildKey(slot)).Count == 0;

    public static void SaveBuild(int slot) => WriteSet(BuildKey(slot), UnlockedIds());

    // 로드: 현재를 슬롯 내용으로 교체. earned 단조증가라 항상 afford 가능.
    public static void LoadBuild(int slot) => WriteSet(CurrentKey, ReadSet(BuildKey(slot)));

    // ── 치트/디버그: 전체 초기화 ──
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(EssenceKey);
        PlayerPrefs.DeleteKey(CrystalKey);
        PlayerPrefs.DeleteKey(PowderKey);
        PlayerPrefs.DeleteKey(CurrentKey);
        for (int i = 1; i <= BuildSlots; i++) PlayerPrefs.DeleteKey(BuildKey(i));
        PlayerPrefs.Save();
    }

    // ── 내부 CSV 직렬화 ──
    private static string BuildKey(int slot) => BuildPrefix + slot;

    private static HashSet<string> ReadSet(string key)
    {
        var set = new HashSet<string>();
        string csv = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(csv)) return set;
        foreach (string s in csv.Split(','))
            if (!string.IsNullOrEmpty(s)) set.Add(s);
        return set;
    }

    private static void WriteSet(string key, HashSet<string> set)
    {
        PlayerPrefs.SetString(key, string.Join(",", set));
        PlayerPrefs.Save();
    }
}
