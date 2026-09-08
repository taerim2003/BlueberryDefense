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
    // ↓ 아래는 뒤에만 추가할 것 — 기존 Prog_* 에셋이 이 enum을 정수로 직렬화해 두어서 중간에 끼우면 값이 밀린다.
    TickRate,        // 반복 타격 간격 배율(작을수록 자주 때림) — 회오리 피해 주기, 독수리 투하 간격
    MaxTargets,      // 동시에 상대하는 적 수 — 오브 동시 타격 수, 스나이핑 저격 대상 수
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

    // 🔴 아래 네 축은 **0이면 "이 스킬은 이 축을 안 쓴다"** 는 뜻이라 코드·프리팹 기본값을 그대로 둔다.
    //    그래서 기존 에셋(값이 없는 상태)은 지금과 100% 같게 돈다 — 태리미가 값을 넣는 순간부터 에셋이 이긴다.
    //    ⚠️ `baseDuration`은 **스킬의 주 지속시간 하나**만 가리킨다. 산탄처럼 지속이 둘인 스킬
    //    (버프 7초 · 전탄발사 2초)은 **버프 쪽**만 여기서 정한다 — 한 값으로 둘을 움직이면 반대 방향 요구가 충돌한다.
    [Header("시작값 — 축을 안 쓰면 0(코드 기본값 유지)")]
    public float baseDuration = 0f;   // 지속시간(초): 회오리 소용돌이 수명 · 산탄 버프 · 낙뢰 폭풍
    public int baseHits = 0;          // 한 캐스트가 때리는 횟수(0이면 기본공격=3, 그 외=1)
    public int basePierce = 0;        // 관통 횟수 시작값
    public int baseProjectiles = 0;   // 추가 투사체 시작값

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
    // 캐릭터 주력기(딸기 화살 쏘기·파인애플 휘두르기)는 매 판 쓰는 기본 딜링이라 후반 과성장을 억제한다.
    public static float DefaultDamageMult(ActiveSkillId id) =>
        id == ActiveSkillId.BasicAttack || id == ActiveSkillId.Swing ? 1.13f : 1.2f;

    // 🔴 아래 두 표(DefaultBaseCooldown / DefaultBaseDamage)는 **Prog_* 에셋이 없는 스킬에만** 쓰인다.
    //    PlayerSkills 가 `p != null ? p.baseCooldown : Default...` 로 읽으므로, 에셋이 있는 스킬은
    //    여기 값을 고쳐도 게임이 안 본다. 2026-09-03 현재 스킬 11종 모두 에셋이 있으므로 **이 표는 전부 가려져 있다.**
    //    밸런스를 고칠 곳은 `Assets/Data/Skills/Prog_*.asset` 이다(CLAUDE.md "밸런스 수치는 에셋이 정답").
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
        ActiveSkillId.Swing => 1.8f,
        ActiveSkillId.GrapeToss => 6f, // 안개가 4초 깔려 있어 쿨이 짧으면 화면이 안개로 덮인다
        _ => 1f,
    };

    public static float DefaultBaseDamage(ActiveSkillId id) => id switch
    {
        ActiveSkillId.BasicAttack => 16f,
        ActiveSkillId.Whirlwind => 6f, // 8/24 플레이스루: 소폭 과함 → 7에서 -1
        ActiveSkillId.Orb => 7f,
        ActiveSkillId.Lightning => 0f, // 실제 피해는 LightningStorm.ProcDamage
        ActiveSkillId.EagleDrop => 13f, // 8/24 플레이스루: 기본 데미지가 낮다 → 11에서 +2
        ActiveSkillId.Sniping => 18f,
        ActiveSkillId.Homing => 8f,
        ActiveSkillId.Shotgun => 12f,
        ActiveSkillId.Rewind => 0f,
        ActiveSkillId.Swing => 20f,
        ActiveSkillId.GrapeToss => 4f, // 틱당 피해 — 기본 6틱 × 알 3개라 총량은 이 값의 몇 배가 된다
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
        // 포도: 안개 범위와 던지는 알 수를 번갈아 올린다(기획안의 레벨업 축 — 범위·피해·알 수).
        ActiveSkillId.GrapeToss => occurrence % 2 == 0
            ? Step(SkillStat.Scale, StatOp.Add, 0.08f)
            : Step(SkillStat.ProjectileCount, StatOp.Add, 1f),
        _ => Step(SkillStat.Scale, StatOp.Add, 0.05f), // Orb·Sniping·Homing·Shotgun
    };

    private static LevelUpStep Step(SkillStat s, StatOp o, float a) =>
        new LevelUpStep { stat = s, op = o, amount = a };
}
