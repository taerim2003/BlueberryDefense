using UnityEngine;

// 레벨업 시 어떤 스탯이 올라가는지(EquippedSkill의 어느 필드).
public enum SkillStat
{
    Damage,          // 피해
    Cooldown,        // 재사용 대기시간 (GlobalCooldown 하한 적용)
    ProjectileSpeed, // 투사체 속도 배율
    Pierce,          // 관통 횟수(정수)
    ProjectileCount, // 추가 투사체(정수)
    ProcChance,      // 발동 확률 보너스
    Duration,        // 지속시간(회오리)
    Scale,           // 크기
    RewindAmount,    // 되감기 시간
}

// 스탯을 올리는 방식. Multiply=현재값×amount, Add=현재값+amount.
public enum StatOp { Add, Multiply }

// 한 레벨업이 적용하는 단일 스탯 변화.
[System.Serializable]
public class LevelUpStep
{
    public SkillStat stat;
    public StatOp op = StatOp.Add;
    public float amount;
}

// 스킬 1종의 밸런스 데이터: 시작값 + 레벨별 강화 커브(Tier A, 완전 데이터화).
// levels[0]=1→2레벨, [1]=2→3레벨 … 배열 범위 밖 레벨은 DefaultStep(코드 규칙)으로 폴백 → 미저작 구간도 현행과 동일.
// 과거 level%3 순환 + occurrence%N 3번째 슬롯 스케줄(코드 Tier B)을 명시적 배열로 대체한다.
[CreateAssetMenu(fileName = "SkillProgression", menuName = "BlueberryDefense/Skill Progression")]
public class SkillProgression : ScriptableObject
{
    public ActiveSkillId skill;

    [Header("시작값")]
    public float baseCooldown = 1f;
    public float baseDamage = 6f;

    [Header("레벨업 커브 (levels[0]=1→2레벨, [1]=2→3레벨 … 범위 밖=기본 규칙 폴백)")]
    public LevelUpStep[] levels;

    // 목표 레벨(2 이상)에 적용할 스텝. 에셋에 없으면 코드 기본 규칙으로 폴백.
    public LevelUpStep StepForLevel(int level)
    {
        int idx = level - 2;
        if (levels != null && idx >= 0 && idx < levels.Length) return levels[idx];
        return DefaultStep(skill, level);
    }

    // ── 현행 재현 규칙 (폴백 + 에셋 생성의 단일 진실원) ─────────────────────
    // 리팩토링 전 PlayerSkills의 레벨업 로직·기본 수치를 그대로 옮긴 것(현행 재현의 기준).
    public const float DefaultCooldownMult = 0.95f;
    public static float DefaultDamageMult(ActiveSkillId id) =>
        id == ActiveSkillId.BasicAttack ? 1.13f : 1.2f; // 기본공격은 후반 과성장 억제

    public static float DefaultBaseCooldown(ActiveSkillId id) => id switch
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
    };

    public static float DefaultBaseDamage(ActiveSkillId id) => id switch
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
    };

    // 리팩토링 전 레벨업 규칙: 되감기=홀짝 교차 / 그 외=level%3 순환(1피해·2쿨·0 3번째슬롯).
    public static LevelUpStep DefaultStep(ActiveSkillId id, int level)
    {
        if (id == ActiveSkillId.Rewind)
            return level % 2 == 0
                ? Step(SkillStat.RewindAmount, StatOp.Add, 0.15f)  // 짝수 레벨: 되감기 시간 +0.15
                : Step(SkillStat.Cooldown, StatOp.Add, -0.15f);    // 홀수 레벨: 쿨타임 -0.15

        switch (level % 3)
        {
            case 1: return Step(SkillStat.Damage, StatOp.Multiply, DefaultDamageMult(id));
            case 2: return Step(SkillStat.Cooldown, StatOp.Multiply, DefaultCooldownMult);
            default: return DefaultThirdStep(id, level / 3 - 1); // occurrence = 지금까지의 %3==0 횟수 - 1
        }
    }

    // 리팩토링 전 3번째 슬롯(occurrence 기준 순환) 고유 강화.
    private static LevelUpStep DefaultThirdStep(ActiveSkillId id, int occurrence) => id switch
    {
        ActiveSkillId.BasicAttack => (occurrence % 4) switch
        {
            1 => Step(SkillStat.Pierce, StatOp.Add, 1f),
            3 => Step(SkillStat.ProjectileCount, StatOp.Add, 1f),
            _ => Step(SkillStat.ProjectileSpeed, StatOp.Add, 0.1f), // 0·2: 저성능 배치 완화
        },
        ActiveSkillId.Lightning => Step(SkillStat.ProcChance, StatOp.Add, 0.03f),
        ActiveSkillId.Whirlwind => occurrence % 2 == 0
            ? Step(SkillStat.Duration, StatOp.Add, 0.5f)
            : Step(SkillStat.Scale, StatOp.Add, 0.05f),
        ActiveSkillId.EagleDrop => Step(SkillStat.Cooldown, StatOp.Multiply, 0.95f),
        _ => Step(SkillStat.Scale, StatOp.Add, 0.05f), // Orb·Sniping·Homing·Shotgun
    };

    private static LevelUpStep Step(SkillStat s, StatOp o, float a) =>
        new LevelUpStep { stat = s, op = o, amount = a };
}
