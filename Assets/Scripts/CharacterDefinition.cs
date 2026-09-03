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

    // 표시 문구는 표에서 읽는다. 키는 **에셋 이름**에서 파생 — MapDefinition과 같은 규칙.
    public string Name => Loc.TOr("char.name." + name, string.IsNullOrEmpty(displayName) ? name : displayName);
    public string Desc => Loc.TOr("char.desc." + name, description);

    [Header("외형 (null이면 프리팹 기본 유지)")]
    public Sprite sprite;                                // 인게임 플레이어 스프라이트
    public RuntimeAnimatorController animatorController; // 인게임 애니메이터

    // 선택 화면에서 카드를 누르면 이 프레임들을 한 번 훑고 portrait로 돌아온다.
    // 인게임 공격 클립과 같은 그림을 쓰지만, 카드는 Animator가 아니라 Image라서 배열로 따로 들고 있다.
    [Header("카드 클릭 시 공격 모션 (비면 재생 안 함)")]
    public Sprite[] attackFrames;
    public float attackFrameSeconds = 0.09f;

    // 공격 프레임의 캔버스가 초상화보다 크면(충격파까지 담느라) preserveAspect가 가로에 맞추면서
    // 몸통이 확 쪼그라든다 — 파인애플이 150x60, 초상화는 38x47이라 세로가 절반 이하로 눌렸다.
    // 재생하는 동안만 썸네일을 이만큼 키우고 밀어서 **몸통이 초상화와 같은 크기·같은 자리**에 오게 한다.
    // 오프셋은 썸네일 표시 크기 대비 비율이라 카드 크기가 바뀌어도 따라간다.
    // 두 캔버스가 같은 캐릭터(딸기)는 1과 0 그대로 두면 된다.
    public float attackFrameScale = 1f;
    public Vector2 attackFrameOffset = Vector2.zero;

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
            lines.Add((ok ? "✓ " : "• ") + Loc.F("unlock.essence", have, requiredEssenceEarned));
        }
        if (requiredClearMap != null)
        {
            lines.Add((ClearConditionMet ? "✓ " : "• ") + Loc.F("unlock.clearMap", requiredClearMap.Name, AscensionTable.DifficultyName(requiredClearAscension)));
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
