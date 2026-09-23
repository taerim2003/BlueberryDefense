#if UNITY_EDITOR || BOT_QA
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

// 봇의 스킬트리·세이브 조작. 구매는 전부 게임과 같은 `SkillTreeSave.CanUpgrade/TryUpgrade`를 거친다(클릭과 같은 판정).
public static class BotTree
{
    private static SkillTreeData tree;

    public static SkillTreeData Tree
    {
        get
        {
            if (tree != null) return tree;
            // MetaRunApplier가 배선한 것과 같은 에셋. 백업(MainSkillTree_Backup)을 집지 않게 이름으로 고정한다.
#if UNITY_EDITOR
            tree = AssetDatabase.LoadAssetAtPath<SkillTreeData>("Assets/SkillTree/MainSkillTree.asset");
#else
            tree = LoadByName<SkillTreeData>("MainSkillTree");
#endif
            if (tree == null) Debug.LogError("[Bot] MainSkillTree 에셋을 못 찾음");
            return tree;
        }
    }

    public static T LoadByName<T>(string assetName) where T : Object
    {
#if UNITY_EDITOR
        foreach (string guid in AssetDatabase.FindAssets(assetName + " t:" + typeof(T).Name))
        {
            T a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null && a.name == assetName) return a;
        }
        return null;
#else
        // 빌드엔 AssetDatabase가 없다. 씬(MetaRunApplier·CharacterSelectUI·MapSelectUI)이 참조하는 에셋은
        // 타이틀이 뜨면 이미 메모리에 있으니 로드된 것 중에서 이름으로 찾는다.
        foreach (T a in Resources.FindObjectsOfTypeAll<T>())
            if (a != null && a.name == assetName) return a;
        return null;
#endif
    }

    // 전 노드를 만렙까지 사는 데 드는 총 정수.
    public static int TotalCost()
    {
        int sum = 0;
        foreach (SkillNode n in Tree.nodes)
            for (int L = 0; L < SkillTreeSave.MaxLevelOf(n); L++)
                sum += Mathf.RoundToInt(SkillTreeSave.CostOf(n) * Mathf.Pow(1.5f, L));
        return sum;
    }

    public static bool AllMaxed() =>
        Tree.nodes.All(n => SkillTreeSave.EffectiveLevel(n, SkillTreeSave.LevelOf(n.id)) >= SkillTreeSave.MaxLevelOf(n));

    public static int OwnedLevels() => Tree.nodes.Sum(n => SkillTreeSave.EffectiveLevel(n, SkillTreeSave.LevelOf(n.id)));
    public static int TotalLevels() => Tree.nodes.Sum(n => SkillTreeSave.MaxLevelOf(n));

    public static int Spent() => SkillTreeSave.EssenceEarned - SkillTreeSave.AvailableEssence(Tree);

    // 사용자 정책: 살 수 있는 노드를 **비용이 낮은 것부터** 더 못 살 때까지. 동가는 무작위.
    public static List<object> BuyCheapestFirst(System.Random rng)
    {
        var bought = new List<object>();
        for (int guard = 0; guard < 2000; guard++)
        {
            List<SkillNode> buyable = Tree.nodes.Where(n => SkillTreeSave.CanUpgrade(Tree, n.id)).ToList();
            if (buyable.Count == 0) break;
            int min = buyable.Min(n => SkillTreeSave.NextLevelCost(Tree, n));
            List<SkillNode> cheapest = buyable.Where(n => SkillTreeSave.NextLevelCost(Tree, n) == min).ToList();
            SkillNode pick = cheapest[rng.Next(cheapest.Count)];
            if (!SkillTreeSave.TryUpgrade(Tree, pick.id)) break;
            var d = BotJson.Obj();
            d["id"] = pick.id; d["level"] = SkillTreeSave.LevelOf(pick.id); d["cost"] = min;
            bought.Add(d);
        }
        return bought;
    }

    // 빈 봇 세이브. 파일을 지우지 않고 비운 채 저장한다(파일이 없으면 Migrate가 레지스트리 옛 값을 끌어온다).
    public static void ResetSave()
    {
        SaveStore.DeleteAll();
        SaveStore.Save();
    }

    // probe용 합성 세이브: **보유 노드 레벨 비율**이 ratio에 닿을 때까지 싼 것부터 산 트리 + 맵·캐릭터 전부 해금.
    // 🔴 비용 비율로 정하지 말 것 — 가격이 대역마다 4배라 "비용 30%"가 노드 레벨 89%였다(2026-09-18 실측).
    // 같은 ratio면 같은 트리가 나오도록 고정 시드로 산다(정주행의 운과 무관한 기준점).
    public static void BuildReferenceSave(float ratio, string[] mapNames)
    {
        ResetSave();
        SkillTreeSave.AddEssence(TotalCost());
        if (ratio >= 1f) BuyCheapestFirst(new System.Random(7));
        else
        {
            int target = Mathf.CeilToInt(TotalLevels() * ratio);
            var rng = new System.Random(7);
            // 상한이 있어야 한다 — TryUpgrade가 성공해도 OwnedLevels가 안 오르는 조합이면
            // 이 루프가 메인 스레드를 영원히 잡고, 그러면 Update도 안 돌아 봇 워치독조차 못 짖는다.
            int guard = 0;
            while (OwnedLevels() < target)
            {
                if (++guard > 5000)
                {
                    Debug.LogError("[Bot] BuildReferenceSave 루프 상한 도달 — ratio=" + ratio
                        + " target=" + target + " owned=" + OwnedLevels());
                    break;
                }
                List<SkillNode> buyable = Tree.nodes.Where(n => SkillTreeSave.CanUpgrade(Tree, n.id)).ToList();
                if (buyable.Count == 0) break;
                int min = buyable.Min(n => SkillTreeSave.NextLevelCost(Tree, n));
                List<SkillNode> cheapest = buyable.Where(n => SkillTreeSave.NextLevelCost(Tree, n) == min).ToList();
                if (!SkillTreeSave.TryUpgrade(Tree, cheapest[rng.Next(cheapest.Count)].id)) break;
            }
        }
        // 캐릭터 해금 조건(누적 정수)만 채운다 — 남은 정수는 probe에서 안 쓰므로 트리에 영향 없음.
        int earned = SkillTreeSave.EssenceEarned;
        if (earned < 2000) SkillTreeSave.AddEssence(2000 - earned);
        foreach (string m in mapNames) MapClearSave.RecordClear(m, 3);
        AscensionSave.UnlockUpTo(3);
    }
}
#endif
