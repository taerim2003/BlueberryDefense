using UnityEngine;

// 패시브 1종의 밸런스 데이터: 획득 시 기본값 + 레벨업당 상승값(선형 진행, Tier A).
// 값이 어떤 스탯(피해배율·체력·경험치·치명타·초기화확률)을 뜻하는지는 PassiveSkillId별로 PlayerPassives가 해석한다.
// 진화 트리(path/tier)는 이 SO 범위 밖 — PlayerPassives에 하드코딩 유지.
[CreateAssetMenu(fileName = "PassiveProgression", menuName = "BlueberryDefense/Passive Progression")]
public class PassiveProgression : ScriptableObject
{
    public PassiveSkillId passive;

    [Header("시작값 / 레벨업당 상승값")]
    public float baseValue;     // 획득(레벨1) 시 적용
    public float perLevelBonus; // 레벨업마다 추가 적용

    // 코드 기본값(에셋 미할당 시 폴백 = 현행 재현). 단일 진실원.
    public static float DefaultBaseValue(PassiveSkillId id) => id switch
    {
        PassiveSkillId.Strength => 0.07f,   // 피해 배율
        PassiveSkillId.Health => 20f,       // 최대 체력
        PassiveSkillId.Knowledge => 0.08f,  // 경험치 배율
        PassiveSkillId.Assassinate => 0.15f, // 치명타 확률
        PassiveSkillId.Refresh => 0.10f,    // 재사용 초기화 확률
        PassiveSkillId.Defense => 0.06f,    // 받는 피해 감소 비율
        PassiveSkillId.Accel => 0.05f,      // 전 스킬 쿨타임 감소 비율
        _ => 0f,
    };

    public static float DefaultPerLevelBonus(PassiveSkillId id) => id switch
    {
        PassiveSkillId.Strength => 0.07f,
        PassiveSkillId.Health => 20f,
        PassiveSkillId.Knowledge => 0.08f,
        PassiveSkillId.Assassinate => 0.04f,
        PassiveSkillId.Refresh => 0.02f,
        PassiveSkillId.Defense => 0.04f,
        PassiveSkillId.Accel => 0.025f,     // 만렙(10) 누적 = 0.05 + 0.025×9 = 27.5%
        _ => 0f,
    };
}
