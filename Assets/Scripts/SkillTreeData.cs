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
