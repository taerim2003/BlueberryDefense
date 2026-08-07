using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 진화 아이콘(Icon_*R1/R2.png) 32장을 임포트 정규화하고 씬의 LevelUpUI에 배선한다.
// Window > Blueberry Defense > 진화 아이콘 배선
//
// 아이콘 파일명 규칙: R1 = 루트0, R2 = 루트1 (1차/2차 티어는 같은 그림을 쓴다).
// 배열 인덱스는 LevelUpUI와 같은 규칙 — (int)id * 2 + route.
public static class EvolutionIconWiring
{
    private const string SpriteDir = "Assets/Sprites";

    // enum 값 → 파일명 조각. 그림 파일명이 enum 이름과 다른 것들이 있다(BasicAttack=Arrow, Shotgun=Scatter 등).
    private static readonly string[] ActiveFileNames =
    {
        "Arrow",       // BasicAttack
        "Tornado",     // Whirlwind
        "Orb",
        "Thunder",     // Lightning
        "EagleStrike", // EagleDrop
        "Sniping",
        "Homing",
        "Scatter",     // Shotgun
        "Rewind",
        "Swing",
    };

    // Refresh(인덱스 4)는 폐지돼 그림이 없다. 자리는 비운 채 세어야 뒤(Defense·Accel)가 안 밀린다.
    private static readonly string[] PassiveFileNames =
    {
        "Power",       // Strength
        "Health",
        "Knowledge",
        "Assasinate",  // Assassinate (파일명 철자 그대로)
        null,          // Refresh — 폐지
        "Defence",     // Defense
        "Accel",
    };

    [MenuItem("Window/Blueberry Defense/진화 아이콘 배선")]
    private static void Run() => Debug.Log(RunAndReport());

    // 메뉴는 로그로 보고, 자동화(MCP)는 반환값으로 받는다.
    public static string RunAndReport()
    {
        var log = new StringBuilder();

        int fixedCount = NormalizeImporters(log);
        AssetDatabase.Refresh();

        Sprite[] active = Collect(ActiveFileNames, log);
        Sprite[] passive = Collect(PassiveFileNames, log);

        string wired = Wire(active, passive, log);

        log.Insert(0, $"[진화 아이콘] 임포트 정규화 {fixedCount}장 · 액티브 {active.Count(s => s != null)}/20 · " +
                      $"패시브 {passive.Count(s => s != null)}/12 · {wired}\n");
        return log.ToString();
    }

    // 도트 아이콘 임포트 설정을 기존 원본 아이콘(Icon_BasicAttack 등)과 같게 맞춘다.
    // 새 PNG는 기본값(Bilinear·PPU100·압축)으로 들어와서 그냥 두면 흐릿하고 뭉갠 채로 뜬다.
    private static int NormalizeImporters(StringBuilder log)
    {
        int changed = 0;

        foreach (string path in EveryIconPath())
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { log.AppendLine($"  ⚠ 임포터 없음: {path}"); continue; }

            bool dirty = false;

            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; dirty = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }

            // Unity 6에서 spritePixelsToUnit은 TextureImporterSettings 경유로 써야 안전하다.
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (!Mathf.Approximately(settings.spritePixelsPerUnit, 32f))
            {
                settings.spritePixelsPerUnit = 32f;
                importer.SetTextureSettings(settings);
                dirty = true;
            }

            // 플랫폼 오버라이드(Standalone)도 압축이 켜져 있으면 빌드에서만 뭉갠다.
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            if (standalone.overridden && standalone.textureCompression != TextureImporterCompression.Uncompressed)
            {
                standalone.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SetPlatformTextureSettings(standalone);
                dirty = true;
            }

            if (!dirty) continue;
            importer.SaveAndReimport();
            changed++;
        }

        return changed;
    }

    private static IEnumerable<string> EveryIconPath()
    {
        foreach (string name in ActiveFileNames)
            for (int r = 0; r < 2; r++)
            {
                string p = PathFor(name, r);
                if (System.IO.File.Exists(p)) yield return p;
            }

        foreach (string name in PassiveFileNames)
        {
            if (name == null) continue;
            for (int r = 0; r < 2; r++)
            {
                string p = PathFor(name, r);
                if (System.IO.File.Exists(p)) yield return p;
            }
        }
    }

    private static string PathFor(string name, int route) => $"{SpriteDir}/Icon_{name}R{route + 1}.png";

    // 인덱스 = enum값 * 2 + route. 빠진 그림은 null로 남겨 둔다(런타임이 원본 아이콘으로 떨어진다).
    private static Sprite[] Collect(string[] fileNames, StringBuilder log)
    {
        var result = new Sprite[fileNames.Length * 2];

        for (int i = 0; i < fileNames.Length; i++)
        {
            if (fileNames[i] == null) continue;
            for (int r = 0; r < 2; r++)
            {
                string path = PathFor(fileNames[i], r);
                Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                if (sprite == null)
                {
                    log.AppendLine($"  ⚠ 스프라이트를 못 찾음: {path}");
                    continue;
                }
                result[i * 2 + r] = sprite;
            }
        }

        return result;
    }

    private static string Wire(Sprite[] active, Sprite[] passive, StringBuilder log)
    {
        LevelUpUI ui = Object.FindAnyObjectByType<LevelUpUI>(FindObjectsInactive.Include);
        if (ui == null)
        {
            log.AppendLine("  ⚠ 열린 씬에 LevelUpUI가 없다 — SampleScene을 열고 다시 실행할 것. (아이콘 임포트만 끝났다)");
            return "배선 실패";
        }

        var so = new SerializedObject(ui);
        Assign(so.FindProperty("activeEvoIcons"), active);
        Assign(so.FindProperty("passiveEvoIcons"), passive);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);

        log.AppendLine($"  ✔ {ui.gameObject.scene.name}의 LevelUpUI에 배선함 — 씬 저장(Ctrl+S)은 직접 할 것");
        return "배선 완료(미저장)";
    }

    private static void Assign(SerializedProperty array, Sprite[] sprites)
    {
        if (array == null) return;
        array.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
    }
}
