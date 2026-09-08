using UnityEngine;

// 마지막으로 고른 캐릭터(판을 넘어 유지). 한 판 끝나고 Title 씬으로 돌아와도 그대로 남아 있어야 한다.
//
// 인덱스가 아니라 **에셋 이름**을 저장한다 — 로스터(CharacterSelectUI.characters) 순서가 바뀌거나
// 캐릭터가 중간에 추가돼도 선택이 엉뚱한 캐릭터로 옮겨가지 않게. (MapClearSave와 같은 이유·같은 방식.)
// ⚠️ 캐릭터 에셋 파일명을 바꾸면 그 선택 기록이 끊긴다(→ 첫 캐릭터로 폴백).
public static class CharacterSave
{
    private const string Key = "select.character";

    public static string Selected => PlayerPrefs.GetString(Key, "");

    public static void Save(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return;
        PlayerPrefs.SetString(Key, characterId);
        PlayerPrefs.Save();
    }
}
