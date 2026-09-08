using UnityEngine;

// 승천(난이도 등급) 배율표. 승천 레벨 1 = 기본(현재 난이도), 2·3… = 더 어려움.
// 적 체력·이동속도·데미지(난이도)와 정수 획득량(보상)을 조절한다. 인스펙터/Balance Dashboard에서 수치 편집.
// RunConfig.AscensionLevel로 이번 판 등급을 선택하고, EnemySpawner가 스폰 시 난이도 배율을,
// MetaRunApplier가 판 시작 시 정수 배율을 적용한다.
[System.Serializable]
public class AscensionTier
{
    public float hpMult = 1f;
    public float speedMult = 1f;
    public float damageMult = 1f;
    public float essenceMult = 1f; // 정수 획득량 배율 — 어려운 만큼 보상도 커진다
    // 이 승천의 마지막 스테이지(=보스가 나오는 판). 승천이 오를수록 **판 자체가 길어진다**
    // (스테이지당 물량이 아니라 스테이지 개수). 0 이하면 DefaultFinalStage로 폴백.
    public int finalStage = 15;
}

[CreateAssetMenu(fileName = "AscensionTable", menuName = "BlueberryDefense/Ascension Table")]
public class AscensionTable : ScriptableObject
{
    // tiers[0]=승천 1(기본, 전부 ×1), [1]=승천 2, [2]=승천 3 … 배열을 늘리면 상위 승천도 추가된다.
    public AscensionTier[] tiers =
    {
        new AscensionTier { hpMult = 1f,    speedMult = 1f,   damageMult = 1f,    essenceMult = 1f,    finalStage = 15 }, // 승천 1 (현재 난이도)
        new AscensionTier { hpMult = 1.35f, speedMult = 1.1f, damageMult = 1.25f, essenceMult = 1.25f, finalStage = 20 }, // 승천 2
        new AscensionTier { hpMult = 1.8f,  speedMult = 1.2f, damageMult = 1.5f,  essenceMult = 1.5f,  finalStage = 25 }, // 승천 3
    };

    // finalStage가 직렬화되지 않은 옛 에셋(값 0)을 위한 폴백.
    private const int DefaultFinalStage = 15;

    // 존재하는 최고 승천 레벨(1-based).
    public int MaxLevel => Mathf.Max(1, tiers != null ? tiers.Length : 1);

    // 화면에 보이는 난이도 이름. 등급이 셋뿐이라 그대로 쉬움/보통/어려움으로 부르고,
    // 등급이 늘면 이름이 모자라므로 "어려움 +N"으로 이어 붙인다.
    // 🔴 UI에 등급 **숫자**를 그대로 쓰지 말 것 — "승천"이라는 말은 화면에서 폐지됐다(맵/캐릭터 해금 조건 포함).
    private const int DifficultyNameCount = 3;

    public static string DifficultyName(int level)
    {
        int i = Mathf.Clamp(level, 1, int.MaxValue) - 1;
        if (i < DifficultyNameCount) return Loc.T("ui.difficulty." + i);
        return Loc.F("ui.difficulty.plus", Loc.T("ui.difficulty." + (DifficultyNameCount - 1)), i - DifficultyNameCount + 1);
    }

    // 승천 레벨(1-based)의 배율. 범위 밖은 클램프.
    public AscensionTier Get(int level)
    {
        if (tiers == null || tiers.Length == 0) return new AscensionTier();
        return tiers[Mathf.Clamp(level - 1, 0, tiers.Length - 1)];
    }

    // 이 승천의 최종 스테이지(클리어 판정). GameManager가 본다.
    public int FinalStageFor(int level)
    {
        int s = Get(level).finalStage;
        return s > 0 ? s : DefaultFinalStage;
    }

    // 보스 판 = 어느 승천이든 최종 판으로 삼는 스테이지 전부(기본 15·20·25).
    // **이번 판의 최종이 아니어도** 보스가 선다 — 20판짜리 승천이라도 15판엔 보스전이 있어야 한다.
    // EnemySpawner의 보스 등장 판정이 이걸 본다(클리어 판정은 FinalStageFor 그대로).
    public bool IsBossStage(int stage)
    {
        if (tiers == null || tiers.Length == 0) return stage == DefaultFinalStage;
        foreach (AscensionTier t in tiers)
            if ((t.finalStage > 0 ? t.finalStage : DefaultFinalStage) == stage) return true;
        return false;
    }

    // 에셋 미할당 시 폴백(위 기본 tiers 그대로) — Battle 씬 단독 실행도 동작.
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
