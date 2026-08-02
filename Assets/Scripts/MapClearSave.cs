using UnityEngine;

// 맵별 클리어 진행도(판을 넘어 유지). "어느 맵을 어느 승천까지 깼는가"를 맵마다 따로 기록한다.
//
// 왜 AscensionSave와 별도인가: AscensionSave는 전역 단일 키("ascension.unlocked")라
// **어느 맵에서 깼는지가 남지 않는다.** 캐릭터 해금 조건("농장 승천1 클리어")처럼
// 맵을 특정하는 조건은 그걸로 판정할 수 없어서 맵별 기록을 따로 둔다.
//
// 키는 MapDefinition의 **에셋 이름**을 쓴다(displayName은 표시용이라 바뀔 수 있다).
// ⚠️ 맵 에셋 파일명을 바꾸면 그 맵의 클리어 기록이 끊긴다.
public static class MapClearSave
{
    private static string Key(string mapId) => "clear.map." + mapId;

    // 이 맵에서 클리어한 최고 승천 등급(한 번도 못 깼으면 0).
    public static int ClearedAscension(string mapId) =>
        string.IsNullOrEmpty(mapId) ? 0 : PlayerPrefs.GetInt(Key(mapId), 0);

    public static bool HasCleared(string mapId, int ascension) =>
        ClearedAscension(mapId) >= ascension;

    public static void RecordClear(string mapId, int ascension)
    {
        if (string.IsNullOrEmpty(mapId)) return; // 맵 미선택(SampleScene 단독 실행)이면 기록할 곳이 없다
        if (ascension <= ClearedAscension(mapId)) return;
        PlayerPrefs.SetInt(Key(mapId), ascension);
        PlayerPrefs.Save();
    }

    public static void Reset(string mapId)
    {
        if (string.IsNullOrEmpty(mapId)) return;
        PlayerPrefs.DeleteKey(Key(mapId));
        PlayerPrefs.Save();
    }
}
