using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 번역 1단계 — 기존 하드코딩 테이블에서 (키, 한국어)를 뽑아낸다.
//
// 🔴 텍스트 파싱이 아니라 **함수를 실제로 호출해서** 뽑는다.
//    정규식으로 소스를 긁으면 "switch에 있지만 도달 불가능한 항목"과 "게임이 실제로 쓰는 폴백"을
//    구분할 수 없다. enum 조합을 전부 돌며 호출하면 게임이 쓰는 것과 1:1이 보장된다.
//    → 소스 정규식으로 뽑은 목록(scratchpad/loc_from_source.tsv)과 대조하는 것이 검증이다.
//
// ⚠️ **보간 문자열 테이블은 여기서 뽑지 않는다.** DescribePassiveAcquire처럼 $"...{Pct(b)}%..."인 것은
//    호출하면 수치가 이미 박힌 문장이 나와 자리표시자 {0}을 만들 수 없다. 그건 소스에서 변환해 넣는다.
//
// 키는 손으로 짓지 않고 enum 이름에서 파생한다 — 항목이 늘면 키도 저절로 늘어난다.
public static class LocHarvest
{
    public const string OutPath = "Assets/Localization/harvest_ko.tsv";

    [MenuItem("Window/Blueberry Defense/번역 - 하드코딩 테이블 수확")]
    public static void RunMenu() { Debug.Log(Run()); }

    public static string Run()
    {
        var rows = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>();
        var log = new StringBuilder();

        void Add(string key, string ko)
        {
            if (string.IsNullOrEmpty(ko)) return;              // 빈 칸 = 그 조합이 정의되지 않은 것
            if (!seen.Add(key)) { log.AppendLine("DUP KEY: " + key); return; }
            rows.Add(new KeyValuePair<string, string>(key, ko));
        }

        // ── 액티브 스킬 ──
        foreach (ActiveSkillId id in Enum.GetValues(typeof(ActiveSkillId)))
        {
            string n = PlayerSkills.GetActiveSkillName(id);
            if (n != id.ToString()) Add("skill.name." + id, n);   // 폴백(id.ToString())은 번역 대상이 아니다

            Add("skill.desc." + id, LevelUpUI.GetActiveSkillDescription(id));

            for (int path = 0; path <= 2; path++)
                for (int tier = 1; tier <= 3; tier++)
                {
                    Add($"evo.active.desc.{id}.{path}.{tier}", PlayerSkills.DescribePathEffect(id, path, tier));
                    Add($"evo.active.title.{id}.{path}.{tier}", PlayerSkills.GetPathTierTitle(id, path, tier));
                }

            for (int route = 0; route <= 1; route++)
                for (int stage = 1; stage <= 2; stage++)
                {
                    string e = EvolutionRoutes.EvolvedName(id, route, stage);
                    if (e != n) Add($"evo.name.{id}.{route}.{stage}", e);   // 원래 이름 그대로면 = 미정의(폴백)
                }
        }

        // ── 패시브 ──
        foreach (PassiveSkillId id in Enum.GetValues(typeof(PassiveSkillId)))
        {
            string n = PlayerSkills.GetPassiveSkillName(id);
            if (n != id.ToString()) Add("passive.name." + id, n);

            for (int path = 0; path <= 2; path++)
                for (int tier = 1; tier <= 3; tier++)
                {
                    Add($"evo.passive.desc.{id}.{path}.{tier}", PlayerPassives.DescribePathEffect(id, path, tier));
                    Add($"evo.passive.title.{id}.{path}.{tier}", PlayerPassives.GetPathTierTitle(id, path, tier));
                }

            for (int route = 0; route <= 1; route++)
                for (int stage = 1; stage <= 2; stage++)
                {
                    string e = EvolutionRoutes.EvolvedName(id, route, stage);
                    if (e != n) Add($"evo.name.{id}.{route}.{stage}", e);
                }
        }

        // ── 스킬 종류 배지 ──
        foreach (SkillCategory c in Enum.GetValues(typeof(SkillCategory)))
            Add("skill.cat." + c, PlayerSkills.GetSkillCategoryLabel(c));

        // ── 쓰기 (UTF-8 BOM — PowerShell 5.1이 안 깨뜨리게) ──
        var outLines = new List<string> { "KEY\tKO" };
        foreach (var kv in rows) outLines.Add(kv.Key + "\t" + kv.Value.Replace("\t", " ").Replace("\n", "\\n"));

        Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
        File.WriteAllLines(OutPath, outLines, new UTF8Encoding(true));
        AssetDatabase.Refresh();

        log.AppendLine("수확 " + rows.Count + "건 -> " + OutPath);

        // 접두사별 집계 — 소스 파싱 결과와 눈으로 대조하기 위한 것
        var byPrefix = new SortedDictionary<string, int>();
        foreach (var kv in rows)
        {
            string[] p = kv.Key.Split('.');
            string pre = p.Length >= 2 ? p[0] + "." + p[1] : p[0];
            byPrefix.TryGetValue(pre, out int c);
            byPrefix[pre] = c + 1;
        }
        foreach (var kv in byPrefix) log.AppendLine("  " + kv.Key.PadRight(22) + kv.Value);
        return log.ToString();
    }
}
