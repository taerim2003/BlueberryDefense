using UnityEngine;

[System.Serializable]
public class StageData
{
    public int stageNumber = 1;
    public int spawnCount = 20;   // 이 스테이지에 스폰할 총 적 수(물량 기반 클리어). 쿼터 소진 + 잔몹 0 → 클리어
    public float duration = 45f;  // (레거시) 물량 기반 전환으로 미사용 — 참고용으로만 남김
    public float spawnInterval = 1.5f;
    public float eliteChance = 0f;
    public float paperPlaneChance = 0f;
    public float ufoChance = 0f;
    public float shieldChance = 0f; // 방패 블루베리(관통·오브 차단) 스폰 확률
    public float riderChance = 0f;  // 라이더 블루베리(지상 고속 돌진·저HP) 스폰 확률
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
