using UnityEngine;

// 캐릭터 1종의 데이터. RunConfig.Character로 선택되어 판 시작 시 씬에 적용된다(MapDefinition과 대칭).
// 적용 방식: 스탯(시작스킬·기본체력)은 플레이어 컴포넌트가 자기 Awake에서 직접 읽고(순서 안전),
//           외형(스프라이트·애니메이터)은 RunBootstrap이 얹는다(null이면 프리팹 기본 유지 → 현재와 동일).
// 후보 풀이 비어 있으면 "전체 허용"으로 해석 → 기본 캐릭터는 풀을 비워 현행 후보와 동일.
[CreateAssetMenu(fileName = "CharacterDefinition", menuName = "BlueberryDefense/Character Definition")]
public class CharacterDefinition : ScriptableObject
{
    [Header("선택 화면")]
    public string displayName;            // 캐릭터 선택 카드 이름
    [TextArea] public string description; // 카드 설명 (선택)
    public Sprite portrait;               // 선택 화면 초상화 (선택)

    [Header("외형 (null이면 프리팹 기본 유지)")]
    public Sprite sprite;                                // 인게임 플레이어 스프라이트
    public RuntimeAnimatorController animatorController; // 인게임 애니메이터

    [Header("기본 스탯")]
    public int baseHealth = 100;

    // ── 해금 조건 ──────────────────────────────────────────────────────────────
    // 두 조건을 **모두** 충족해야 열린다(누적 정수 + 특정 맵 클리어). 이후 캐릭터도 같은 구조를 재사용.
    // ⚠️ unlockedFromStart 기본값이 true인 이유: 이 필드가 없던 시절 임포트된 기존 에셋(딸기)이
    //    직렬화 캐시의 옛 값을 쓰기 때문에, 기본값은 반드시 "기존 동작(전부 해금)"과 같은 쪽이어야 한다.
    //    새로 잠글 캐릭터만 명시적으로 false로 둘 것.
    [Header("해금 조건 (unlockedFromStart면 무시)")]
    public bool unlockedFromStart = true;
    public int requiredEssenceEarned;         // 지금까지 벌어들인 정수 총합(SkillTreeSave.EssenceEarned — 써도 안 줄어듦)
    public MapDefinition requiredClearMap;    // null이면 맵 조건 없음
    public int requiredClearAscension = 1;    // 그 맵에서 클리어해야 하는 승천 등급

    public bool IsUnlocked =>
        unlockedFromStart ||
        (SkillTreeSave.EssenceEarned >= requiredEssenceEarned && ClearConditionMet);

    private bool ClearConditionMet =>
        requiredClearMap == null || MapClearSave.HasCleared(requiredClearMap.name, requiredClearAscension);

    // 잠금 UI에 그대로 띄우는 진행도 두 줄. 충족한 줄에는 체크가 붙는다.
    public string[] UnlockProgressLines()
    {
        if (unlockedFromStart) return new string[0];

        var lines = new System.Collections.Generic.List<string>();
        if (requiredEssenceEarned > 0)
        {
            int have = Mathf.Min(SkillTreeSave.EssenceEarned, requiredEssenceEarned);
            bool ok = SkillTreeSave.EssenceEarned >= requiredEssenceEarned;
            lines.Add((ok ? "✔ " : "• ") + $"정수 {have} / {requiredEssenceEarned}");
        }
        if (requiredClearMap != null)
        {
            string mapName = string.IsNullOrEmpty(requiredClearMap.displayName)
                ? requiredClearMap.name : requiredClearMap.displayName;
            lines.Add((ClearConditionMet ? "✔ " : "• ") + $"{mapName} 승천{requiredClearAscension} 클리어");
        }
        return lines.ToArray();
    }

    [Header("시작 구성")]
    public ActiveSkillId startingSkill = ActiveSkillId.BasicAttack;
    public ActiveSkillId[] allowedActivePool;   // 레벨업 신규 스킬 후보. 비면 전체 허용
    public PassiveSkillId[] allowedPassivePool; // 레벨업 신규 패시브 후보. 비면 전체 허용

    // 후보 풀 판정 헬퍼(빈 배열 = 전체 허용).
    public bool AllowsActive(ActiveSkillId id) =>
        allowedActivePool == null || allowedActivePool.Length == 0 || System.Array.IndexOf(allowedActivePool, id) >= 0;
    public bool AllowsPassive(PassiveSkillId id) =>
        allowedPassivePool == null || allowedPassivePool.Length == 0 || System.Array.IndexOf(allowedPassivePool, id) >= 0;
}
