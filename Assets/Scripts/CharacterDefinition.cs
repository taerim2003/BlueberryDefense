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
