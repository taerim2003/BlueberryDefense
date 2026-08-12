using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

// 번역 2단계 — TSV(KEY\tVALUE)를 StringTable에 적재한다.
//
// 표를 손으로 편집하지 않고 파일에서 밀어넣는 이유: 항목이 600개라 Localization Tables 창에서
// 손으로 넣으면 오타가 섞이고, 무엇보다 **되돌릴 수가 없다.** 파일이 원본이면 언제든 다시 밀 수 있다.
//
// ⚠️ 값이 비어 있는 줄은 건너뛴다 — 빈 값을 넣으면 폴백(en→ko)이 안 돌고 화면이 빈다.
public static class LocTableImport
{
    public const string Collection = "Game";

    [MenuItem("Window/Blueberry Defense/번역 - 수확본을 ko 테이블에 적재")]
    public static void ImportHarvestMenu() { Debug.Log(Import(LocHarvest.OutPath, "ko")); }

    public static string Import(string tsvPath, string localeCode)
    {
        var log = new StringBuilder();
        if (!File.Exists(tsvPath)) return "TSV 없음: " + tsvPath;

        var col = LocalizationEditorSettings.GetStringTableCollection(Collection);
        if (col == null) return "컬렉션 없음: " + Collection;

        StringTable table = null;
        foreach (var t in col.StringTables)
            if (t.LocaleIdentifier.Code == localeCode) { table = t; break; }
        if (table == null) return "테이블 없음: " + localeCode;

        var shared = col.SharedData;
        int added = 0, updated = 0, skipped = 0;

        foreach (string line in File.ReadAllLines(tsvPath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            string key = line.Substring(0, tab).Trim();
            string val = line.Substring(tab + 1).Replace("\\n", "\n");
            if (key == "KEY") continue;                       // 헤더
            if (string.IsNullOrEmpty(val)) { skipped++; continue; }

            if (shared.GetEntry(key) == null) shared.AddKey(key);

            var entry = table.GetEntry(key);
            if (entry == null) { table.AddEntry(key, val); added++; }
            else if (entry.LocalizedValue != val) { entry.Value = val; updated++; }
        }

        EditorUtility.SetDirty(shared);
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();

        log.AppendLine($"[{localeCode}] 신규 {added} · 갱신 {updated} · 빈값건너뜀 {skipped}");

        // ── 되읽어 확인 ── 에셋을 코드로 만들면 반드시 되읽는다
        var reload = AssetDatabase.LoadAssetAtPath<StringTable>(AssetDatabase.GetAssetPath(table));
        log.AppendLine("되읽기 entries=" + (reload != null ? reload.Count.ToString() : "NULL"));
        return log.ToString();
    }

    // 표 상태 리포트 — 언어별 채움 정도와 누락 키
    public static string Report(int sampleMissing = 8)
    {
        var col = LocalizationEditorSettings.GetStringTableCollection(Collection);
        if (col == null) return "컬렉션 없음";
        var sb = new StringBuilder();
        var shared = col.SharedData;
        sb.AppendLine("shared keys = " + shared.Entries.Count);

        foreach (var t in col.StringTables)
        {
            int filled = 0;
            var missing = new List<string>();
            foreach (var se in shared.Entries)
            {
                var e = t.GetEntry(se.Id);
                if (e != null && !string.IsNullOrEmpty(e.LocalizedValue)) filled++;
                else if (missing.Count < sampleMissing) missing.Add(se.Key);
            }
            sb.AppendLine($"  {t.LocaleIdentifier.Code}: {filled}/{shared.Entries.Count}"
                        + (missing.Count > 0 ? "  누락 예: " + string.Join(", ", missing) : ""));
        }
        return sb.ToString();
    }
}
