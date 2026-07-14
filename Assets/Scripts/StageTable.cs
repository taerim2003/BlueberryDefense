using UnityEngine;

[System.Serializable]
public class StageData
{
    public int stageNumber = 1;
    public float duration = 45f;
    public float spawnInterval = 1.5f;
    public float eliteChance = 0f;
    public float paperPlaneChance = 0f;
    public float ufoChance = 0f;
    public float shieldChance = 0f; // 방패 블루베리(관통·오브 차단) 스폰 확률
    public float enemyHpMultiplier = 1f;
    public float enemySpeedMultiplier = 1f;
    public float enemyDamageMultiplier = 1f;
}

[CreateAssetMenu(fileName = "StageTable", menuName = "BlueberryDefense/Stage Table")]
public class StageTable : ScriptableObject
{
    public StageData[] stages;

    public StageData GetStage(int stageNumber)
    {
        if (stages == null || stages.Length == 0) return null;
        int index = Mathf.Clamp(stageNumber - 1, 0, stages.Length - 1);
        return stages[index];
    }
}
