using UnityEngine;

public enum LevelUpStatEffect
{
    BasicAttackDamageFlat,
    BasicAttackCooldownPercent,
    MaxHealthFlat,
    XpMultiplierPercent,
}

// 레벨업 시 뜨는 범용 스탯 강화 선택지. 에셋을 새로 만들어 배열에 추가하면
// 코드 수정 없이 레벨업 선택지 풀에 새 옵션이 생긴다.
[CreateAssetMenu(fileName = "LevelUpStatOption", menuName = "Blueberry Defense/LevelUp Stat Option")]
public class LevelUpStatOptionSO : ScriptableObject
{
    public string title;
    [TextArea] public string description;
    public LevelUpStatEffect effect;
    public float value;
}
