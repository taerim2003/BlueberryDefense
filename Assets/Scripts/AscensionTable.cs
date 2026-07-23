using UnityEngine;

// 승천(난이도 등급) 배율표. 승천 레벨 1 = 기본(현재 난이도), 2·3… = 더 어려움.
// 적 체력·이동속도·데미지(난이도)와 정수 획득량(보상)을 조절한다. 인스펙터/Balance Dashboard에서 수치 편집.
// RunConfig.AscensionLevel로 이번 판 등급을 선택하고, EnemySpawner가 스폰 시 난이도 배율을,
// MetaRunApplier가 판 시작 시 정수 배율을 적용한다.
[System.Serializable]
public class AscensionTier
{
    public float hpMult = 1f;      // 적 최대체력 배율
    public float speedMult = 1f;   // 적 이동속도 배율
    public float damageMult = 1f;  // 적 피해 배율
    public float essenceMult = 1f; // 정수 획득량 배율 — 어려운 만큼 보상도 커진다
}

[CreateAssetMenu(fileName = "AscensionTable", menuName = "BlueberryDefense/Ascension Table")]
public class AscensionTable : ScriptableObject
{
    // tiers[0]=승천 1(기본, 전부 ×1), [1]=승천 2, [2]=승천 3 … 배열을 늘리면 상위 승천도 추가된다.
    public AscensionTier[] tiers =
    {
        new AscensionTier { hpMult = 1f,    speedMult = 1f,   damageMult = 1f,    essenceMult = 1f },    // 승천 1 (현재 난이도)
        new AscensionTier { hpMult = 1.35f, speedMult = 1.1f, damageMult = 1.25f, essenceMult = 1.25f }, // 승천 2
        new AscensionTier { hpMult = 1.8f,  speedMult = 1.2f, damageMult = 1.5f,  essenceMult = 1.5f },  // 승천 3
    };

    // 존재하는 최고 승천 레벨(1-based).
    public int MaxLevel => Mathf.Max(1, tiers != null ? tiers.Length : 1);

    // 승천 레벨(1-based)의 배율. 범위 밖은 클램프.
    public AscensionTier Get(int level)
    {
        if (tiers == null || tiers.Length == 0) return new AscensionTier();
        return tiers[Mathf.Clamp(level - 1, 0, tiers.Length - 1)];
    }

    // 에셋 미할당 시 폴백(위 기본 tiers 그대로) — SampleScene 단독 실행도 동작.
    private static AscensionTable defaultInstance;
    public static AscensionTable Default
    {
        get
        {
            if (defaultInstance == null) defaultInstance = CreateInstance<AscensionTable>();
            return defaultInstance;
        }
    }
}

// 승천 해금 진행도(판을 넘어 유지). 클리어하면 다음 승천이 열린다 — StS식 루프.
public static class AscensionSave
{
    private const string Key = "ascension.unlocked";

    // 지금까지 해금된 최고 승천 레벨(최소 1).
    public static int Unlocked => Mathf.Max(1, PlayerPrefs.GetInt(Key, 1));

    public static void UnlockUpTo(int level)
    {
        if (level > Unlocked) { PlayerPrefs.SetInt(Key, level); PlayerPrefs.Save(); }
    }

    public static void Reset() { PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save(); }
}
