using UnityEngine;

// 스킬 아이콘을 씬 배선 없이 집는 창구. 타이틀 씬의 컬렉션 패널은 인게임 UI(LevelUpUI)에
// 접근할 수 없어서, 같은 그림을 Resources에서 읽는다.
//
// 🔴 에셋이 있어야 그림이 뜬다: `Window > Blueberry Defense > 스킬 아이콘 라이브러리 굽기`로
//    `Assets/Resources/SkillIconLibrary.asset`을 굽는다(경로·파일명이 곧 배선 — UISkin·SfxLibrary와 같은 방식).
//    파일명 표는 굽는 도구(SkillIconLibraryBake)가 단독으로 소유한다.
public class SkillIconLibrary : ScriptableObject
{
    public Sprite[] active;       // index = (int)ActiveSkillId
    public Sprite[] passive;      // index = (int)PassiveSkillId
    public Sprite[] activeEvo;    // index = (int)ActiveSkillId * 2 + route
    public Sprite[] passiveEvo;   // index = (int)PassiveSkillId * 2 + route
    public Sprite essence;        // 정수 픽업 그림 — 스킬트리 「부유」 노드
    public Sprite critDamage;     // 스킬트리 「치명타 피해」 노드
    public Sprite skilltree;      // 스킬트리 루트 「스킬트리 해금!」 노드
    public Sprite reroll;         // 스킬트리 「리롤 해금」·「리롤」 노드
    public Sprite[] evolution;    // 스킬트리 진화 해금 노드 — 0 = 1차, 1 = 2차
    public Sprite[] level;        // 스킬트리 단계 숫자(32×32 우측 하단) — index 0 = I
    public Sprite[] upgrade;      // 스킬트리 강화 별(32×32 우측 하단) — 0 = 은별(첫 강화), 1 = 금별(다음 강화)

    private static SkillIconLibrary cached;
    private static bool searched;

    public static SkillIconLibrary Instance
    {
        get
        {
            if (cached == null && !searched)
            {
                searched = true;
                cached = Resources.Load<SkillIconLibrary>("SkillIconLibrary");
            }
            return cached;
        }
    }

    public static Sprite Active(ActiveSkillId id) => Pick(Instance != null ? Instance.active : null, (int)id);
    public static Sprite Passive(PassiveSkillId id) => Pick(Instance != null ? Instance.passive : null, (int)id);
    public static Sprite ActiveEvo(ActiveSkillId id, int route) => Pick(Instance != null ? Instance.activeEvo : null, (int)id * 2 + route);
    public static Sprite PassiveEvo(PassiveSkillId id, int route) => Pick(Instance != null ? Instance.passiveEvo : null, (int)id * 2 + route);

    public static Sprite Essence() => Instance != null ? Instance.essence : null;
    public static Sprite CritDamage() => Instance != null ? Instance.critDamage : null;
    public static Sprite Skilltree() => Instance != null ? Instance.skilltree : null;
    public static Sprite Reroll() => Instance != null ? Instance.reroll : null;
    public static Sprite Evolution(int order) => Pick(Instance != null ? Instance.evolution : null, order - 1);
    public static Sprite Level(int stage) => Pick(Instance != null ? Instance.level : null, stage - 1);
    public static Sprite Upgrade(int rank) => Pick(Instance != null ? Instance.upgrade : null, rank);

    private static Sprite Pick(Sprite[] arr, int i) => arr != null && i >= 0 && i < arr.Length ? arr[i] : null;
}
