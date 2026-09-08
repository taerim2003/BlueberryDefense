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

            // 🔴 좌표는 화면 그대로 — 루트 0/1 × 차수 1~MaxStage(2026-09-08 개편).
            //    legacy path·tier(1~3)로 훑던 옛 루프는 화면에 안 뜨는 조합까지 84줄이나 뱉었다.
            for (int route = 0; route <= 1; route++)
                for (int tier = 1; tier <= EvolutionRoutes.MaxStageFor(id); tier++)
                {
                    Add($"evo.active.desc.{id}.{route}.{tier}", PlayerSkills.DescribePathEffect(id, route, tier));
                    string e = EvolutionRoutes.EvolvedName(id, route, tier);
                    if (e != n) Add($"evo.name.{id}.{route}.{tier}", e);   // 원래 이름 그대로면 = 미정의(폴백)
                }
        }

        // ── 패시브 ──
        foreach (PassiveSkillId id in Enum.GetValues(typeof(PassiveSkillId)))
        {
            string n = PlayerSkills.GetPassiveSkillName(id);
            if (n != id.ToString()) Add("passive.name." + id, n);

            // 패시브는 2차 진화가 없다 — MaxStageFor가 1을 준다(2026-09-08 사용자 결정).
            for (int route = 0; route <= 1; route++)
                for (int tier = 1; tier <= EvolutionRoutes.MaxStageFor(id); tier++)
                {
                    Add($"evo.passive.desc.{id}.{route}.{tier}", PlayerPassives.DescribePathEffect(id, route, tier));
                    string e = EvolutionRoutes.EvolvedName(id, route, tier);
                    if (e != n) Add($"evo.name.{id}.{route}.{tier}", e);
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

    // ── 에셋에 적힌 표시 문구 수확 ──────────────────────────────────────────────
    // 위 Run()은 **코드의 스위치 테이블**을 뽑는다. 문구가 에셋에 적힌 것(스킬트리 노드·캐릭터·맵)은
    // 여기서 뽑는다. 키는 노드 id / **에셋 파일 이름**에서 파생 — 에셋이 늘면 키도 저절로 는다.
    // ⚠️ 조회 쪽(SkillNode.Name/Desc·CharacterDefinition.Name/Desc·MapDefinition.Name/Desc)이
    //    Loc.TOr 폴백이라, 표에 없어도 에셋의 값이 그대로 나온다. 여기 빠져도 화면이 비지는 않는다.
    public const string AssetOutPath = "Assets/Localization/assets_ko.tsv";

    [MenuItem("Window/Blueberry Defense/번역 - 에셋 표시문구 수확")]
    public static void RunAssetsMenu() { Debug.Log(RunAssets()); }

    public static string RunAssets()
    {
        var rows = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>();
        var log = new StringBuilder();

        void Add(string key, string ko)
        {
            if (string.IsNullOrWhiteSpace(ko)) return;
            if (!seen.Add(key)) { log.AppendLine("DUP KEY: " + key); return; }
            rows.Add(new KeyValuePair<string, string>(key, ko));
        }

        int trees = 0, nodes = 0, chars = 0, maps = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:SkillTreeData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // 백업본(MainSkillTree_Backup)까지 긁으면 죽은 노드 키가 표에 섞인다 — 현행 에셋만.
            if (path.Contains("_Backup")) { log.AppendLine("skip " + path); continue; }
            var tree = AssetDatabase.LoadAssetAtPath<SkillTreeData>(path);
            if (tree == null || tree.nodes == null) continue;
            trees++;
            foreach (var n in tree.nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.id)) continue;
                nodes++;
                Add("tree.name." + n.id, n.displayName);
                Add("tree.desc." + n.id, n.description);
            }
        }

        foreach (string guid in AssetDatabase.FindAssets("t:CharacterDefinition"))
        {
            var c = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (c == null) continue;
            chars++;
            Add("char.name." + c.name, c.displayName);
            Add("char.desc." + c.name, c.description);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:MapDefinition"))
        {
            var m = AssetDatabase.LoadAssetAtPath<MapDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (m == null) continue;
            maps++;
            Add("map.name." + m.name, m.displayName);
            Add("map.desc." + m.name, m.description);
        }

        var outLines = new List<string> { "KEY\tKO" };
        foreach (var kv in rows) outLines.Add(kv.Key + "\t" + kv.Value.Replace("\t", " ").Replace("\n", "\\n"));

        Directory.CreateDirectory(Path.GetDirectoryName(AssetOutPath));
        File.WriteAllLines(AssetOutPath, outLines, new UTF8Encoding(true));
        AssetDatabase.Refresh();

        log.AppendLine($"에셋 수확 {rows.Count}건 -> {AssetOutPath}");
        log.AppendLine($"  트리 {trees}개 / 노드 {nodes}개 · 캐릭터 {chars} · 맵 {maps}");
        return log.ToString();
    }
}
