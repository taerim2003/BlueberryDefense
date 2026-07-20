using UnityEngine;

// 액티브 스킬 9종의 기본 밸런스 수치(Tier A). PlayerSkills가 참조한다.
// 담는 것: 기본 쿨타임·기본 피해 + 레벨업 배율(피해 %3==1, 쿨감 %3==2).
// 담지 않는 것(코드 잔류, Tier B): 3번째 슬롯 순환 강화·되감기 홀짝 교차 — 값이 아니라 스케줄 모양이라서.
// 미할당 시 SkillTable.Default(코드 기본값=현행)로 폴백 → SampleScene 단독 실행도 현재와 동일.
[CreateAssetMenu(fileName = "SkillTable", menuName = "BlueberryDefense/Skill Table")]
public class SkillTable : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public ActiveSkillId id;
        public float baseCooldown = 1f;
        public float baseDamage = 6f;
        public float levelDamageMultiplier = 1.2f;    // 레벨 %3==1: 피해 배율
        public float levelCooldownMultiplier = 0.95f; // 레벨 %3==2: 쿨타임 배율
    }

    public Entry[] skills;

    // id로 항목 조회. 에셋에 없으면 코드 기본값(DefaultEntry)으로 폴백.
    public Entry Get(ActiveSkillId id)
    {
        if (skills != null)
            foreach (var e in skills)
                if (e.id == id) return e;
        return DefaultEntry(id);
    }

    // 현행 하드코딩 값 그대로 — 폴백/기본 에셋의 단일 진실원(GetDefaultCooldown/Damage에서 이관).
    // 낙뢰 피해는 LightningStorm.ProcDamage에서 오므로 여기 baseDamage는 미사용(0).
    public static Entry DefaultEntry(ActiveSkillId id) => new Entry
    {
        id = id,
        baseCooldown = id switch
        {
            ActiveSkillId.BasicAttack => 1.5f,
            ActiveSkillId.Whirlwind => 5f,
            ActiveSkillId.Orb => 7f,
            ActiveSkillId.Lightning => 12f,
            ActiveSkillId.EagleDrop => 15f,
            ActiveSkillId.Sniping => 5f,
            ActiveSkillId.Homing => 8f,
            ActiveSkillId.Shotgun => 14f,
            ActiveSkillId.Rewind => 6f,
            _ => 1f,
        },
        baseDamage = id switch
        {
            ActiveSkillId.BasicAttack => 16f,
            ActiveSkillId.Whirlwind => 7f,
            ActiveSkillId.Orb => 7f,
            ActiveSkillId.Lightning => 0f, // 실제 피해는 LightningStorm.ProcDamage
            ActiveSkillId.EagleDrop => 11f,
            ActiveSkillId.Sniping => 18f,
            ActiveSkillId.Homing => 8f,
            ActiveSkillId.Shotgun => 12f,
            ActiveSkillId.Rewind => 0f,
            _ => 6f,
        },
        levelDamageMultiplier = id == ActiveSkillId.BasicAttack ? 1.13f : 1.2f, // 기본공격은 후반 과성장 억제
        levelCooldownMultiplier = 0.95f,
    };

    private static SkillTable defaultInstance;

    // SerializeField 미할당 시 폴백 — 9종 전부 DefaultEntry로 채워 현재 동작과 동일.
    public static SkillTable Default
    {
        get
        {
            if (defaultInstance == null)
            {
                defaultInstance = CreateInstance<SkillTable>();
                var ids = (ActiveSkillId[])System.Enum.GetValues(typeof(ActiveSkillId));
                defaultInstance.skills = new Entry[ids.Length];
                for (int i = 0; i < ids.Length; i++)
                    defaultInstance.skills[i] = DefaultEntry(ids[i]);
            }
            return defaultInstance;
        }
    }
}
