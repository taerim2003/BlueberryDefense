using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// 판을 넘어 남는 모든 저장값(정수·스킬트리·승천·맵 클리어·도감·캐릭터 선택·음량·언어)의 단일 저장소.
// PlayerPrefs와 같은 모양의 API라 호출부는 `PlayerPrefs.` → `SaveStore.`만 다르다.
//
// 🔴 **PlayerPrefs로 되돌리지 말 것 — Steam Cloud가 끊긴다.** PlayerPrefs는 Windows에서 레지스트리이고,
//    Steam Auto-Cloud는 **파일만** 동기화한다. Steamworks의 Auto-Cloud 경로가 이 파일을 가리킨다:
//    루트 `WinAppDataLocalLow` · 하위 `taerimgames/BlueberryDefense` · 패턴 `save.json`
//    → ProjectSettings의 companyName/productName을 바꾸면 폴더가 바뀌어 클라우드 세이브가 끊긴다.
//
// 에디터는 `save_editor.json`을 쓴다 — 에디터 테스트가 빌드 세이브(와 그 클라우드 사본)를 덮지 않게.
// 같은 폴더에 Unity가 Player.log도 쓰므로 Steam 패턴은 `*`이 아니라 파일 이름 하나로 건다.
public static class SaveStore
{
#if UNITY_EDITOR
    private const string FileName = "save_editor.json";
#else
    private const string FileName = "save.json";
#endif

    [Serializable]
    private class Data
    {
        public List<string> keys = new List<string>();
        public List<string> values = new List<string>();
    }

    private static readonly Dictionary<string, string> map = new Dictionary<string, string>();
    private static bool loaded;

#if UNITY_EDITOR
    // 봇 플레이테스트 전용 세이브 분리(`save_<name>.json`). 봇이 사용자의 에디터 세이브를 오염시키지 않게 한다.
    // 에디터에서만 존재 — 빌드의 `save.json`(Steam Cloud)은 이 경로에 절대 닿지 않는다.
    // 부르면 메모리 캐시를 비워 다음 접근에 새 파일을 읽는다. null이면 기본 파일로 돌아간다.
    private static string profileFileName;

    public static void UseProfile(string name)
    {
        profileFileName = string.IsNullOrEmpty(name) ? null : "save_" + name + ".json";
        map.Clear();
        loaded = false;
    }

    private static string FilePath => Path.Combine(Application.persistentDataPath, profileFileName ?? FileName);
#else
    private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);
#endif

    public static int GetInt(string key, int fallback = 0)
    {
        EnsureLoaded();
        return map.TryGetValue(key, out string s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
    }

    public static float GetFloat(string key, float fallback = 0f)
    {
        EnsureLoaded();
        return map.TryGetValue(key, out string s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }

    public static string GetString(string key, string fallback = "")
    {
        EnsureLoaded();
        return map.TryGetValue(key, out string s) ? s : fallback;
    }

    public static void SetInt(string key, int value) { EnsureLoaded(); map[key] = value.ToString(CultureInfo.InvariantCulture); }
    public static void SetFloat(string key, float value) { EnsureLoaded(); map[key] = value.ToString("R", CultureInfo.InvariantCulture); }
    public static void SetString(string key, string value) { EnsureLoaded(); map[key] = value ?? ""; }
    public static void DeleteKey(string key) { EnsureLoaded(); map.Remove(key); }

    // 파일을 지우지 않고 **빈 상태로 저장**할 것(호출부가 Save()를 부른다) —
    // 파일이 없으면 다음 실행에 Migrate()가 옛 레지스트리 값을 되살린다.
    public static void DeleteAll() { EnsureLoaded(); map.Clear(); }

    public static void Save()
    {
        EnsureLoaded();
        var data = new Data();
        foreach (var kv in map) { data.keys.Add(kv.Key); data.values.Add(kv.Value); }

        // 임시 파일에 다 쓴 뒤 교체한다 — 쓰는 도중 꺼져도 원본이 반쯤 쓰인 채로 남지 않게.
        string path = FilePath;
        string tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SaveStore] 저장 실패: " + path + "\n" + e);
        }
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        string path = FilePath;
        if (!File.Exists(path)) { Migrate(); return; }

        try
        {
            Data data = JsonUtility.FromJson<Data>(File.ReadAllText(path));
            if (data == null || data.keys.Count != data.values.Count) throw new FormatException("keys/values 길이 불일치");
            for (int i = 0; i < data.keys.Count; i++) map[data.keys[i]] = data.values[i];
        }
        catch (Exception e)
        {
            // 빈 상태로 시작하되 원본은 남겨 둔다 — 다음 Save()가 덮어쓰면 복구할 거리가 사라진다.
            map.Clear();
            try { File.Copy(path, path + ".corrupt", true); } catch (Exception) { }
            Debug.LogWarning("[SaveStore] 세이브 파일을 읽지 못해 빈 상태로 시작합니다(원본은 .corrupt로 보관): " + path + "\n" + e);
        }
    }

    // 세이브 파일이 없을 때 한 번만: 예전 빌드가 PlayerPrefs(레지스트리)에 남긴 값을 옮긴다.
    // PlayerPrefs는 키 열거 API가 없어서 키를 박아 둔다. 레지스트리 쪽은 지우지 않는다(되돌릴 여지).
    // 맵 키는 Assets/Data/Map_*.asset 에셋 이름 기준(MapClearSave 참고).
    private static readonly string[] LegacyIntKeys =
    {
        "meta.currency", "ascension.unlocked",
        "clear.map.Map_BlueberryField", "clear.map.Map_Wide15", "clear.map.Map_Wide20",
    };
    private static readonly string[] LegacyStringKeys =
    {
        "skilltree.current", "select.character", "Collection.Discovered", "loc.locale",
    };
    private static readonly string[] LegacyFloatKeys =
    {
        "option.volume.master", "option.volume.bgm", "option.volume.sfx",
    };

    private static void Migrate()
    {
        foreach (string k in LegacyIntKeys) if (PlayerPrefs.HasKey(k)) SetInt(k, PlayerPrefs.GetInt(k));
        foreach (string k in LegacyStringKeys) if (PlayerPrefs.HasKey(k)) SetString(k, PlayerPrefs.GetString(k));
        foreach (string k in LegacyFloatKeys) if (PlayerPrefs.HasKey(k)) SetFloat(k, PlayerPrefs.GetFloat(k));
        Save(); // 옮길 게 없어도 파일을 만든다 — 다음 실행부터는 마이그레이션이 다시 돌지 않게.
    }
}
