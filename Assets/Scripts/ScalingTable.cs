using UnityEngine;

// 후반 난이도 스케일링과 XP 커브를 담는 전역 SO(맵별 스왑 아님 — 판 전체에 하나).
// EnemySpawner(3스테이지마다 붙는 체력/이속 배율)와 PlayerExperience(XP 커브·후반 감쇠)가 읽는다.
// 값은 여기(Tier A), 흐름(스텝 인덱스 = stage/3, 감쇠 lerp 형태)은 코드에 남긴다(Tier B).
[CreateAssetMenu(fileName = "ScalingTable", menuName = "BlueberryDefense/Scaling Table")]
public class ScalingTable : ScriptableObject
{
    [Header("후반 배율 (3스테이지마다 스텝 상승 — 인덱스는 코드에서 stage/3)")]
    public float[] hpStepBonus = { 0f, 0.06f, 0.16f, 0.30f, 0.48f, 0.72f, 1.0f };
    public float[] speedStepBonus = { 0f, 0.025f, 0.06f, 0.11f, 0.17f, 0.24f, 0.33f };

    [Header("XP 커브")]
    public int xpToNextLevelBase = 18;     // 1→2레벨 필요 XP
    public int xpToNextLevelPerLevel = 9;  // 레벨당 필요 XP 증가

    [Header("후반 XP 감쇠 (스테이지1=최대 → 기준스테이지=최소, 선형)")]
    public float xpFactorMax = 1f;
    public float xpFactorMin = 0.5f;
    public int xpDecayReferenceStage = 20;

    private static ScalingTable defaultInstance;

    // SerializeField 미할당 시 폴백 — 위 필드 초기값 그대로라 현재 동작과 동일.
    public static ScalingTable Default
    {
        get
        {
            if (defaultInstance == null) defaultInstance = CreateInstance<ScalingTable>();
            return defaultInstance;
        }
    }

    public float HpStepBonusAt(int step) => hpStepBonus[Mathf.Clamp(step, 0, hpStepBonus.Length - 1)];
    public float SpeedStepBonusAt(int step) => speedStepBonus[Mathf.Clamp(step, 0, speedStepBonus.Length - 1)];

    // 스테이지별 XP 획득 배율: stage1=Max → referenceStage=Min으로 선형 감소, 이후 Min 유지.
    public float XpStageFactor(int stage)
    {
        int s = Mathf.Clamp(stage, 1, xpDecayReferenceStage);
        return Mathf.Lerp(xpFactorMax, xpFactorMin, (s - 1) / (float)(xpDecayReferenceStage - 1));
    }
}
