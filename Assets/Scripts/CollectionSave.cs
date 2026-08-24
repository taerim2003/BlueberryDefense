using UnityEngine;

// 컬렉션(도감) 발견 기록 — "한 번이라도 얻어 본 스킬 / 한 번이라도 달성한 진화".
// 판을 넘어 남아야 하므로 PlayerPrefs에 CSV 한 줄로 담는다(정수·승천 해금과 같은 저장소).
//
// 캐시를 안 두는 게 의도다. 설정의 "세이브 초기화"가 PlayerPrefs.DeleteAll()로 통째로 비우는데,
// 캐시가 있으면 그 뒤에도 옛 기록이 화면에 남는다. 조회는 메뉴에서만 일어나므로 매번 읽어도 싸다.
public static class CollectionSave
{
    private const string Key = "Collection.Discovered";

    // 토큰: 스킬 = "A:Whirlwind" / "P:Strength", 진화 = 거기에 ":{루트}:{티어}"를 붙인다.
    private static string ActiveToken(ActiveSkillId id) => "A:" + id;
    private static string PassiveToken(PassiveSkillId id) => "P:" + id;

    public static void DiscoverActive(ActiveSkillId id) => Add(ActiveToken(id));
    public static void DiscoverPassive(PassiveSkillId id) => Add(PassiveToken(id));
    public static void DiscoverActiveEvo(ActiveSkillId id, int route, int stage) => Add($"{ActiveToken(id)}:{route}:{stage}");
    public static void DiscoverPassiveEvo(PassiveSkillId id, int route, int stage) => Add($"{PassiveToken(id)}:{route}:{stage}");

    public static bool HasActive(ActiveSkillId id) => Has(ActiveToken(id));
    public static bool HasPassive(PassiveSkillId id) => Has(PassiveToken(id));
    public static bool HasActiveEvo(ActiveSkillId id, int route, int stage) => Has($"{ActiveToken(id)}:{route}:{stage}");
    public static bool HasPassiveEvo(PassiveSkillId id, int route, int stage) => Has($"{PassiveToken(id)}:{route}:{stage}");

    // 토큰끼리 접두사가 겹치므로("A:Whirlwind"는 "A:Whirlwind:0:1"의 접두사) 구분자째로 찾는다.
    private static bool Has(string token) => Csv().Contains("," + token + ",");

    private static void Add(string token)
    {
        string csv = Csv();
        if (csv.Contains("," + token + ",")) return;
        PlayerPrefs.SetString(Key, csv.Trim(',') + (csv.Length > 2 ? "," : "") + token);
        PlayerPrefs.Save();
    }

    // 앞뒤에 구분자를 붙인 형태로 돌려준다 — 첫 항목·마지막 항목도 같은 규칙으로 찾히게.
    private static string Csv() => "," + PlayerPrefs.GetString(Key, "") + ",";
}
