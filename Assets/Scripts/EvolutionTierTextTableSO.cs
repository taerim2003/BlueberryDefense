using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class EvolutionTierTextEntry
{
    public ActiveSkillId skillId;
    [Range(0, 2)] public int path;
    [Range(1, 3)] public int tier;
    public string title;
    [TextArea] public string description;
}

// 진화 트리 각 path/tier 칸의 제목·설명 텍스트를 코드 수정 없이 에디터에서 덮어쓰기 위한 테이블.
// 실제 수치/기믹 로직(PlayerSkills의 Fire*/ApplyPathTierEffect)은 그대로 코드에 남아있고,
// 여기서는 표시 텍스트만 override한다. 항목이 없으면 기존 하드코딩 텍스트로 폴백.
[CreateAssetMenu(fileName = "EvolutionTierTextTable", menuName = "Blueberry Defense/Evolution Tier Text Table")]
public class EvolutionTierTextTableSO : ScriptableObject
{
    public List<EvolutionTierTextEntry> entries = new List<EvolutionTierTextEntry>();

    public bool TryGet(ActiveSkillId id, int path, int tier, out EvolutionTierTextEntry entry)
    {
        foreach (EvolutionTierTextEntry e in entries)
        {
            if (e.skillId == id && e.path == path && e.tier == tier)
            {
                entry = e;
                return true;
            }
        }
        entry = null;
        return false;
    }
}
