using System;
using System.Text;
using UnityEditor;
using UnityEngine;

// `Assets/Resources/SkillIconLibrary.asset`을 굽는다 — 타이틀 씬의 컬렉션 패널이 스킬 아이콘을
// 씬 배선 없이 집을 수 있게. 아이콘 PNG를 추가·개명하면 여기 표를 고치고 다시 돌린다.
//
// ⚠️ enum 이름과 파일명이 안 맞는 것들이 있다(회오리=Tornado, 산탄=Scatter, 힘=Power, 방어=Defence,
//    암살=Assasinate[원본 철자], 기본공격의 진화만 Arrow). 그림을 열어봐도 안 갈리므로 표로 고정한다.
public static class SkillIconLibraryBake
{
    private const string AssetPath = "Assets/Resources/SkillIconLibrary.asset";
    private const string SpriteDir = "Assets/Sprites/";
    private const int LevelCount = 6;

    // 원본 아이콘 파일명. 진화 아이콘은 여기에 R1(루트0)·R2(루트1)를 붙인 이름이다.
    private static string ActiveBase(ActiveSkillId id) => id switch
    {
        ActiveSkillId.BasicAttack => "Icon_BasicAttack",
        ActiveSkillId.Whirlwind => "Icon_Tornado",
        ActiveSkillId.Orb => "Icon_Orb",
        ActiveSkillId.Lightning => "Icon_Thunder",
        ActiveSkillId.EagleDrop => "Icon_EagleStrike",
        ActiveSkillId.Sniping => "Icon_Sniping",
        ActiveSkillId.Homing => "Icon_Homing",
        ActiveSkillId.Shotgun => "Icon_Scatter",
        ActiveSkillId.Rewind => "Icon_Rewind",
        ActiveSkillId.Swing => "Icon_Swing",
        ActiveSkillId.GrapeToss => "Icon_GrapeBomb", // 포도 독성 포도알 — 진화 아이콘은 R1/R2
        _ => null,
    };

    // 기본공격만 진화 그림의 파일명이 다르다(Icon_BasicAttack → Icon_ArrowR1/R2).
    private static string ActiveEvoBase(ActiveSkillId id) =>
        id == ActiveSkillId.BasicAttack ? "Icon_Arrow" : ActiveBase(id);

    private static string PassiveBase(PassiveSkillId id) => id switch
    {
        PassiveSkillId.Strength => "Icon_Power",
        PassiveSkillId.Health => "Icon_Health",
        PassiveSkillId.Knowledge => "Icon_Knowledge",
        PassiveSkillId.Assassinate => "Icon_Assasinate",
        PassiveSkillId.Defense => "Icon_Defence",
        PassiveSkillId.Accel => "Icon_Accel",
        _ => null, // Refresh(폐지) — 그림 없음. 칸은 비워 두되 인덱스는 밀지 않는다.
    };

    [MenuItem("Window/Blueberry Defense/스킬 아이콘 라이브러리 굽기")]
    public static void BakeMenu() { Debug.Log(Bake()); }

    public static string Bake()
    {
        var actives = (ActiveSkillId[])Enum.GetValues(typeof(ActiveSkillId));
        var passives = (PassiveSkillId[])Enum.GetValues(typeof(PassiveSkillId));

        var lib = AssetDatabase.LoadAssetAtPath<SkillIconLibrary>(AssetPath);
        bool created = lib == null;
        if (created) lib = ScriptableObject.CreateInstance<SkillIconLibrary>();

        lib.active = new Sprite[actives.Length];
        lib.passive = new Sprite[passives.Length];
        lib.activeEvo = new Sprite[actives.Length * 2];
        lib.passiveEvo = new Sprite[passives.Length * 2];

        var missing = new StringBuilder();
        int filled = 0;

        foreach (ActiveSkillId id in actives)
        {
            filled += Assign(lib.active, (int)id, ActiveBase(id), missing);
            for (int route = 0; route < 2; route++)
                filled += Assign(lib.activeEvo, (int)id * 2 + route, Suffix(ActiveEvoBase(id), route), missing);
        }

        foreach (PassiveSkillId id in passives)
        {
            filled += Assign(lib.passive, (int)id, PassiveBase(id), missing);
            for (int route = 0; route < 2; route++)
                filled += Assign(lib.passiveEvo, (int)id * 2 + route, Suffix(PassiveBase(id), route), missing);
        }

        // 스킬트리 「부유」 노드용 — 정수 픽업 그림을 그대로 쓴다. Multiple로 잘려 있어 첫 조각을 집는다.
        lib.essence = FirstSprite("태양정수", missing);
        lib.critDamage = FirstSprite("Icon_CritDMG", missing);
        lib.fly = FirstSprite("Icon_Fly", missing);
        lib.boss = FirstSprite("Icon_Boss", missing);
        lib.skilltree = FirstSprite("Icon_Skilltree", missing);
        lib.reroll = FirstSprite("Icon_Reroll", missing);
        filled += (lib.essence != null ? 1 : 0) + (lib.critDamage != null ? 1 : 0) + (lib.fly != null ? 1 : 0) + (lib.boss != null ? 1 : 0) + (lib.skilltree != null ? 1 : 0) + (lib.reroll != null ? 1 : 0);

        // 진화 해금 1차·2차. ⚠️ Icon_Evolution2는 1차와 같은 그림(복사본)이라 2차는 Icon_Evolution3이다.
        lib.evolution = new Sprite[2];
        filled += Assign(lib.evolution, 0, "Icon_Evolution", missing);
        filled += Assign(lib.evolution, 1, "Icon_Evolution3", missing);

        // 스킬트리 단계 숫자 I~VI — Icon_Level1~6 (Level7은 V와 같은 그림이라 안 쓴다)
        lib.level = new Sprite[LevelCount];
        for (int i = 0; i < LevelCount; i++)
            filled += Assign(lib.level, i, "Icon_Level" + (i + 1), missing);

        // 스킬트리 강화 별 — Icon_SkillUpgrade1(은) · 2(금)
        lib.upgrade = new Sprite[2];
        for (int i = 0; i < 2; i++)
            filled += Assign(lib.upgrade, i, "Icon_SkillUpgrade" + (i + 1), missing);

        if (created) AssetDatabase.CreateAsset(lib, AssetPath);
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 되읽어서 확인 — 코드로 만든 에셋의 스프라이트 대입이 조용히 무시된 전례가 있다.
        var reread = AssetDatabase.LoadAssetAtPath<SkillIconLibrary>(AssetPath);
        int rereadFilled = 0;
        foreach (var arr in new[] { reread.active, reread.passive, reread.activeEvo, reread.passiveEvo, reread.level, reread.upgrade, reread.evolution })
            foreach (var s in arr) if (s != null) rereadFilled++;
        rereadFilled += (reread.essence != null ? 1 : 0) + (reread.critDamage != null ? 1 : 0) + (reread.fly != null ? 1 : 0) + (reread.boss != null ? 1 : 0) + (reread.skilltree != null ? 1 : 0) + (reread.reroll != null ? 1 : 0);

        return $"SkillIconLibrary {(created ? "생성" : "갱신")}: {AssetPath}\n" +
               $"  칸 {lib.active.Length + lib.passive.Length + lib.activeEvo.Length + lib.passiveEvo.Length + lib.level.Length + lib.upgrade.Length + lib.evolution.Length + 6}개 중 " +
               $"채움 {filled}개 (되읽기 {rereadFilled}개)\n" +
               (missing.Length == 0 ? "  빈 칸 없음" : "  빈 칸:\n" + missing);
    }

    private static Sprite FirstSprite(string fileName, StringBuilder missing)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(SpriteDir + fileName + ".png"))
            if (o is Sprite s) return s;
        missing.AppendLine($"    [{fileName}] 스프라이트 없음");
        return null;
    }

    private static string Suffix(string base_, int route) =>
        string.IsNullOrEmpty(base_) ? null : base_ + (route == 0 ? "R1" : "R2");

    private static int Assign(Sprite[] arr, int index, string fileName, StringBuilder missing)
    {
        if (string.IsNullOrEmpty(fileName)) return 0;

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + fileName + ".png");
        if (sprite == null)
        {
            // Multiple로 잘린 PNG는 메인 에셋이 Sprite가 아니라 여기서 null이 된다 — 파일명 오타와 구분해 적어 둔다.
            bool exists = AssetDatabase.LoadAssetAtPath<Texture2D>(SpriteDir + fileName + ".png") != null;
            missing.AppendLine($"    [{index}] {fileName} — {(exists ? "파일은 있으나 Sprite가 아님(Multiple 슬라이스?)" : "파일 없음")}");
            return 0;
        }

        arr[index] = sprite;
        return 1;
    }
}
