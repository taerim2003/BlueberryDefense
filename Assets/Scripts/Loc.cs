using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

// 번역 조회의 단일 창구. 게임 코드는 Unity Localization API를 직접 부르지 않고 전부 여기를 지난다.
//
// 왜 래퍼를 두나 — 호출부가 백 곳이 넘어서 API가 바뀌거나 캐싱 전략을 손볼 때 여기 한 곳만 고치면 된다.
// 그리고 StringDatabase.GetLocalizedString()은 호출마다 테이블을 찾아 들어가므로
// HUD처럼 매 프레임 도는 자리에서 쓰면 비싸다 — 테이블을 잡아두고 엔트리만 읽는다.
//
// 🧰 번역 시스템 소유권:
//  - 🔴 **TSV가 원본**(`Assets/Localization/*_ko.tsv`·`*_en.tsv`). 표(`Tables/Game`)를 손으로 편집하면 되돌릴 수 없다.
//    적재는 `Window > Blueberry Defense > 번역 - 모든 TSV를 표에 적재` 하나로 끝난다(파일명 접미사가 곧 로케일).
//    ⚠️ 그 도구는 추가·갱신만 한다 — TSV에서 키를 지워도 표에는 남는다(안 읽으면 무해).
//  - 🔴 **표시 문구를 SO에서 직접 읽지 말 것.** `SkillNode.Name` 같은 프로퍼티가 `Loc.TOr` 창구다.
//    UI 문구를 `const string`으로 두지 말 것.
//  - 씬 TMP는 `LocalizedTmp`로 키에 묶되, **런타임에 코드가 값을 덮어쓰는 TMP에는 붙이지 말 것**(서로 싸운다).
public static class Loc
{
    public const string Table = "Game";

    private static StringTable table;         // 지금 언어의 표
    private static StringTable fallbackTable; // 한국어 표 — 영어가 덜 채워진 자리를 메운다
    private static Locale tableLocale;
    private static bool initialized;

    // 개발 언어 = 한국어. 번역이 비면 언제나 여기로 떨어진다.
    public const string SourceLocale = "ko";

    // 언어가 바뀌면 화면에 이미 그려진 글자를 다시 그려야 한다.
    // 런타임에 글자만 다시 채우는 화면(OptionsMenu·PauseMenu)은 이걸 구독해 스스로 갱신하고,
    // 씬에 박힌 TMP는 LocalizedTmp가 이걸 구독해 스스로 갱신한다.
    public static event System.Action LocaleChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    private static void OnLocaleChanged(Locale l)
    {
        Invalidate();            // 안 비우면 이전 언어가 계속 나온다
        LocaleChanged?.Invoke();
    }

    // 표 자체가 바뀌었을 때(에디터에서 번역을 다시 밀어넣은 직후) 캐시를 버린다.
    public static void Invalidate() { table = null; fallbackTable = null; tableLocale = null; }

    // 🔴 LocalizationSettings 초기화는 **지연**이다. 첫 조회가 초기화 전에 들어오면 표를 못 찾아
    //    화면에 키('skill.name.Whirlwind')가 그대로 뜬다. 한 번만 끝까지 기다린다.
    private static void EnsureInit()
    {
        // 플래그 하나로 막으면, 제공자가 어떤 이유로든 비워졌을 때(에디터에서 에셋을 저장하면 실제로 비워진다)
        // 다시는 초기화하지 않아 영영 복구가 안 된다. "비어 있으면 다시 시도"를 같이 본다.
        if (initialized && LocalizationSettings.AvailableLocales.Locales.Count > 0) return;
        LocalizationSettings.InitializationOperation.WaitForCompletion();
        Invalidate();
        initialized = true;
    }

    private static void Refresh()
    {
        EnsureInit();
        Locale sel = LocalizationSettings.SelectedLocale;
        if (table != null && tableLocale == sel) return;

        table = LocalizationSettings.StringDatabase.GetTable(Table);
        tableLocale = sel;

        // 폴백 표는 언어와 무관하게 늘 한국어.
        // ⚠️ StringDatabase.GetLocalizedString 경로였다면 FallbackLocale 메타데이터가 알아서 해주지만,
        //    그 경로는 호출마다 표를 찾아 들어가서 HUD처럼 매 프레임 도는 자리엔 못 쓴다.
        //    표를 잡아두는 대신 폴백을 여기서 직접 처리한다.
        if (fallbackTable == null || fallbackTable.LocaleIdentifier.Code != SourceLocale)
            fallbackTable = LocalizationSettings.StringDatabase.GetTable(Table, LocaleFor(SourceLocale));
    }

    private static Locale LocaleFor(string code)
    {
        foreach (Locale l in LocalizationSettings.AvailableLocales.Locales)
            if (l.Identifier.Code == code) return l;
        return null;
    }

    private static string Raw(StringTable t, string key)
    {
        if (t == null) return null;
        StringTableEntry e = t.GetEntry(key);
        if (e == null) return null;
        string v = e.LocalizedValue;
        return string.IsNullOrEmpty(v) ? null : v;
    }

    // 키가 없으면 키를 그대로 돌려준다 — 화면에 'evo.name.Orb.0.1'이 뜨면 누락이 눈에 보인다.
    // (빈 문자열로 삼키면 "왜 글자가 안 나오지"로 바뀌어 원인을 못 찾는다.)
    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        Refresh();
        return Raw(table, key) ?? Raw(fallbackTable, key) ?? key;
    }

    // 수치가 끼는 문장 — 번역문은 "{0}% 증가" 꼴로 저장하고 인자를 나중에 채운다.
    // ⚠️ 어순이 언어마다 달라서 문장을 쪼개 이어붙이면 안 된다. 반드시 통문장 + 자리표시자.
    public static string F(string key, params object[] args)
    {
        string fmt = T(key);
        if (args == null || args.Length == 0) return fmt;
        try { return string.Format(fmt, args); }
        catch (System.FormatException) { return fmt; }   // 번역문의 자리표시자가 깨져도 게임은 굴러가야 한다
    }

    // 키가 있는지 (진화명처럼 "정의된 조합만 있는" 표를 조회할 때)
    public static bool Has(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        Refresh();
        return Raw(table, key) != null || Raw(fallbackTable, key) != null;
    }

    // 표에 없으면 폴백 문자열을 쓴다 (enum 이름 등 번역 대상이 아닌 자리)
    public static string TOr(string key, string fallback) => Has(key) ? T(key) : fallback;

    // ── 언어 선택 (설정 패널이 쓴다) ──
    // ⚠️ 여기도 EnsureInit이 필요하다. 초기화 전에 읽으면 목록이 **빈 채로** 돌아와
    //    언어 선택 행이 '?'로 뜨고 ◀▶가 아무것도 안 한다(T()가 먼저 불린 덕에 가려지기 쉬운 자리).
    public static IList<Locale> Locales
    {
        get { EnsureInit(); return LocalizationSettings.AvailableLocales.Locales; }
    }

    public static int CurrentIndex
    {
        get
        {
            IList<Locale> ls = Locales;
            for (int i = 0; i < ls.Count; i++) if (ls[i] == LocalizationSettings.SelectedLocale) return i;
            return 0;
        }
    }

    public static void SetLocale(int index)
    {
        IList<Locale> ls = Locales;
        if (index < 0 || index >= ls.Count) return;
        LocalizationSettings.SelectedLocale = ls[index];
        PlayerPrefs.SetString(PrefKey, ls[index].Identifier.Code);
        PlayerPrefs.Save();
    }

    public const string PrefKey = "loc.locale";

    // 저장된 언어 복원. Unity Localization의 기본 LocaleSelector는 시스템 언어를 보는데,
    // 사용자가 고른 값이 있으면 그게 이긴다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreSaved()
    {
        string code = PlayerPrefs.GetString(PrefKey, "");
        if (string.IsNullOrEmpty(code)) return;   // 저장된 게 없으면 시스템 언어 선택기에 맡긴다
        foreach (Locale l in Locales)
            if (l.Identifier.Code == code) { LocalizationSettings.SelectedLocale = l; return; }
    }
}
