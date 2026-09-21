using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 진화 아이콘(Icon_*R1/R2.png · 2차 Icon_*R1_2/R2_2.png)을 임포트 정규화하고 씬의 LevelUpUI에 배선한다.
// Window > Blueberry Defense > 진화 아이콘 배선
//
// 아이콘 파일명 규칙: R1 = 루트0, R2 = 루트1. 액티브 2차 전용 그림은 뒤에 `_2`가 붙는다.
// 배열 인덱스는 LevelUpUI와 같은 규칙 — (int)id * 2 + route. 2차 그림이 없는 칸은 비워 두면 1차 그림으로 떨어진다.
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
        "GrapeBomb",   // GrapeToss
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

    // 규칙 파일명(Icon_{name}R{n}.png) 대신 **작가가 둔 자리 그대로** 쓰는 그림들. 폴더째 올라온 것이라 옮기지 않는다.
    // 키 = (int)id * 2 + route.
    private static readonly Dictionary<int, string> ActiveOverrides = new Dictionary<int, string>
    {
        { (int)ActiveSkillId.Lightning * 2 + 1, SpriteDir + "/LightningRod/Icon_Tesla.png" },   // 피뢰침(1차) 다시 그린 것
    };

    private static readonly Dictionary<int, string> ActiveStage2Overrides = new Dictionary<int, string>
    {
        { (int)ActiveSkillId.Lightning * 2 + 1, SpriteDir + "/LightningRod/Icon_Jeus.png" },    // 제우스의 은총
        { (int)ActiveSkillId.Shotgun * 2 + 1,   SpriteDir + "/FIRE!!!/Icon_FullBurst.png" },    // 초강력 섬멸용 전탄발사
        // 2026-09-19: 아래 둘은 **그림이 있는데 규칙 경로에 없어서** 빈 칸으로 남아 있던 것들이다.
        { (int)ActiveSkillId.Homing * 2 + 0,    SpriteDir + "/SuperMissile/Icon_SuperMissile.png" }, // 초강력 슈퍼 로켓
        // ⚠️ 루트에도 같은 이름(Icon_SwingR1_2.png)의 **옛 그림**이 있다 — 규칙 경로가 그걸 집으므로 여기서 덮는다.
        { (int)ActiveSkillId.Swing * 2 + 0,     SpriteDir + "/로얄팔라딘의망치/Icon_SwingR1_2.png" }, // 로열 팔라딘의 망치
    };

    [MenuItem("Window/Blueberry Defense/진화 아이콘 배선")]
    private static void Run() => Debug.Log(RunAndReport());

    // 메뉴는 로그로 보고, 자동화(MCP)는 반환값으로 받는다.
    public static string RunAndReport()
    {
        var log = new StringBuilder();

        int fixedCount = NormalizeImporters(log);
        AssetDatabase.Refresh();

        Sprite[] active = Collect(ActiveFileNames, 1, ActiveOverrides, log);
        Sprite[] active2 = Collect(ActiveFileNames, 2, ActiveStage2Overrides, log);
        Sprite[] passive = Collect(PassiveFileNames, 1, null, log);

        string wired = Wire(active, active2, passive, log);

        log.Insert(0, $"[진화 아이콘] 임포트 정규화 {fixedCount}장 · 액티브 {active.Count(s => s != null)}/{active.Length} · " +
                      $"액티브 2차 {active2.Count(s => s != null)}/{active2.Length} ·패시브 {passive.Count(s => s != null)}/12 · {wired}\n");
        return log.ToString();
    }

    // 도트 아이콘 임포트 설정을 기존 원본 아이콘(Icon_BasicAttack 등)과 같게 맞춘다.
    // 새 PNG는 기본값(Bilinear·PPU100·압축)으로 들어와서 그냥 두면 흐릿하고 뭉갠 채로 뜬다.
    private static int NormalizeImporters(StringBuilder log)
    {
        int changed = 0;

        // 🔴 Single 전환은 **새로 들어온 그림에만** 한다. 기존 32장은 Multiple인 채로 씬에 배선돼 있어서
        //    바꾸면 서브에셋 ID가 달라져 참조가 깨진다(그 32장은 조각이 하나뿐이라 Multiple이어도 무해했다).
        var newArt = new HashSet<string>(ActiveOverrides.Values.Concat(ActiveStage2Overrides.Values));
        foreach (string name in ActiveFileNames)
            for (int r = 0; r < 2; r++) newArt.Add(PathFor(name, r, 2));

        foreach (string path in EveryIconPath())
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { log.AppendLine($"  ⚠ 임포터 없음: {path}"); continue; }

            bool dirty = false;

            if (newArt.Contains(path) && importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }

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
        var paths = new List<string>();
        foreach (string name in ActiveFileNames)
            for (int r = 0; r < 2; r++)
            {
                paths.Add(PathFor(name, r, 1));
                paths.Add(PathFor(name, r, 2));
            }

        foreach (string name in PassiveFileNames)
        {
            if (name == null) continue;
            for (int r = 0; r < 2; r++) paths.Add(PathFor(name, r, 1));
        }

        paths.AddRange(ActiveOverrides.Values);
        paths.AddRange(ActiveStage2Overrides.Values);
        return paths.Distinct().Where(System.IO.File.Exists);
    }

    private static string PathFor(string name, int route, int stage) =>
        $"{SpriteDir}/Icon_{name}R{route + 1}{(stage >= 2 ? "_2" : "")}.png";

    // 인덱스 = enum값 * 2 + route. 빠진 그림은 null로 남겨 둔다(런타임이 원본·1차 아이콘으로 떨어진다).
    // 2차는 아직 안 그린 칸이 많아서 "못 찾음"을 찍지 않는다 — 찍으면 진짜 누락이 묻힌다.
    private static Sprite[] Collect(string[] fileNames, int stage, Dictionary<int, string> overrides, StringBuilder log)
    {
        var result = new Sprite[fileNames.Length * 2];

        for (int i = 0; i < fileNames.Length; i++)
        {
            if (fileNames[i] == null) continue;
            for (int r = 0; r < 2; r++)
            {
                int index = i * 2 + r;
                string path = overrides != null && overrides.TryGetValue(index, out string o) ? o : PathFor(fileNames[i], r, stage);
                Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                if (sprite == null)
                {
                    if (stage < 2) log.AppendLine($"  ⚠ 스프라이트를 못 찾음: {path}");
                    continue;
                }
                result[index] = sprite;
            }
        }

        return result;
    }

    private static string Wire(Sprite[] active, Sprite[] active2, Sprite[] passive, StringBuilder log)
    {
        LevelUpUI ui = Object.FindAnyObjectByType<LevelUpUI>(FindObjectsInactive.Include);
        if (ui == null)
        {
            log.AppendLine("  ⚠ 열린 씬에 LevelUpUI가 없다 — Battle 씬을 열고 다시 실행할 것. (아이콘 임포트만 끝났다)");
            return "배선 실패";
        }

        var so = new SerializedObject(ui);
        Assign(so.FindProperty("activeEvoIcons"), active);
        Assign(so.FindProperty("activeEvo2Icons"), active2);
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
