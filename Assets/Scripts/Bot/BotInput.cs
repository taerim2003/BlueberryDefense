// 봇 플레이테스트(Tools/BotPlaytest, `balance` 스킬)가 게임 안을 관측·조종하는 창구.
//
// 🔴 **평소 게임에는 아무 영향이 없어야 한다.** 필드는 전부 기본값(false/null)이고, 호출부는 `?.Invoke` 한 줄뿐이다.
//    값을 세우는 쪽은 `BotPilot`/`BotRecorder`뿐이고 둘 다 `UNITY_EDITOR || BOT_QA`라 릴리스 빌드에서는 영원히 비어 있다.
//    이 파일만 `#if` 밖에 둔 이유: PlayerSkills·Enemy·EndingSequence의 호출부가 릴리스 빌드에서도 컴파일돼야 해서.
//
// 훅을 새로 걸 때도 **게임 로직을 바꾸지 말고 관측만** 한다 — 봇이 재는 값이 사람이 하는 게임과 달라지면 측정이 무의미하다.
public static class BotInput
{
    // QWER 꾹 누르기. PlayerSkills.Update가 키 입력과 OR로 본다(Game 뷰 포커스와 무관하게 동작해야 무인 실행이 된다).
    public static bool HoldSkills;

    // 스킬별 발사 허가(사용자 지시 2026-09-30 — 전 스킬 전역). BotCastPolicy가 봇 판 동안만 설치한다.
    // null이면 무조건 발사 = 종전 동작. HoldSkills가 켜진 프레임에만 읽히므로 사람 플레이에는 절대 닿지 않는다.
    public static System.Func<EquippedSkill, bool> CastGate;

    // 충돌 이펙트의 프레임당 상한을 QA에서 바꿔 끼우기 위한 값(0이면 게임 기본값). 같은 장면을 상한별로 찍어 비교한다.
    public static int ImpactVfxPerFrameOverride;

    // 렉 원인 분리용 대조 스위치(QA 빌드의 perf 실험에서만 켠다 — 기본값 false라 평소 게임은 그대로다).
    // 광역기가 한 프레임에 적 수백을 때릴 때 타격마다 붙는 것들을 하나씩 꺼 보고 렉이 사라지는지 본다.
    public static bool SuppressDamageNumbers;
    public static bool SuppressHitParticles;

    // 광활한 우주 어려움 클리어 때 엔딩 연출을 건너뛴다(밸런스 측정은 판 결과만 필요하다 — 엔딩에 걸려 멈추면 안 된다).
    // QA의 chaos 인스턴스는 끈다 — 엔딩 경로도 QA 대상이다.
    public static bool SkipEnding;

    // 스킬 시전 확정 직후(TryUseSkill). baseCd = 감소율 곱하기 전 쿨, cdMult = 스킬트리·패시브 감소율.
    public static System.Action<EquippedSkill, float, float> OnCast;

    // 적이 플레이어를 박치기한 순간(플레이어 피해의 유일한 경로). 피해량은 방어 적용 전.
    public static System.Action<Enemy, int> OnPlayerHit;

    // 적이 피해를 받은 순간. source는 DamageMeter와 같은 규칙(낙뢰 발동은 Lightning), hpBefore는 이 타격 직전 체력.
    // isCrit = 치명타 여부, preEnemy = 적 쪽 배율(취약·비행 적·보스 추가 피해)을 곱하기 전의 피해 — 순수 스킬 피해 계산용.
    public static System.Action<Enemy, ActiveSkillId?, float, float, bool, float> OnEnemyDamaged;

    // 스킬 피해를 계산한 순간(PlayerSkills.ComputeBaseDamage). buffMult = 계산된 피해 ÷ 스킬 자체 공격력 —
    // 패시브·스킬트리 피해 배율, 되감기 강화, 낙뢰 중첩 같은 외부 강화를 모두 곱한 값이다. 순수 스킬 피해 = 피해 ÷ 이 값.
    public static System.Action<ActiveSkillId, float> OnBaseDamage;

    // 적 사망(isDead가 세워지는 순간 1회). 막타 스킬은 직전 OnEnemyDamaged의 source로 판정한다.
    public static System.Action<Enemy> OnEnemyKilled;

    // UFO(캐리어)가 투하를 마치고 화면 위로 빠져나간 순간 — 처치가 아니다.
    public static System.Action<Enemy> OnEnemyEscaped;

    // 감속·기절(multiplier 0) / 넉백이 걸린 순간.
    public static System.Action<Enemy, float, float> OnSlow;
    public static System.Action<Enemy, float> OnKnockback;

    // ── 패시브 성능 측정용(BotRecorder의 passives[] — 각 패시브가 "없었다면 잃었을 것"을 분리한다) ──

    // 스킬 피해를 계산한 순간(PlayerSkills.ComputeBaseDamage, OnBaseDamage 바로 옆).
    // pool = 덧셈 피해 배율 풀 전체(힘·스킬트리 공격력·Q 전용·힘 2배 강화 합), strength = 그중 힘 패시브 몫,
    // accelBonus = 스킬트리 "가속: 쿨 4초 이하 +30%"가 이 계산에 붙었으면 그 비율(가속 보유 조건이라 가속 몫), 아니면 0.
    public static System.Action<ActiveSkillId, float, float, float> OnDamageShares;

    // 스킬의 치명타 확률을 정한 순간(PlayerSkills.GetCritChance). chance = 상한을 씌우기 전 합, cap = 적용된 상한.
    public static System.Action<ActiveSkillId, float, float> OnCritChance;

    // 치명타를 굴린 순간(PlayerPassives.ApplyCrit). 바로 뒤의 OnEnemyDamaged가 이 굴림의 결과다.
    public static System.Action<float> OnCritRoll;

    // 플레이어 체력이 실제로 깎여 피격 반응 패시브(가시 갑주·고통 가속·망치 반격)가 돌기 **직전**(PlayerPassives.HandleDamageTaken).
    public static System.Action<int> OnPlayerDamageTaken;

    // 하트를 획득하기 직전(HeartPickup). heal = 2배 강화를 곱하기 전 회복량.
    public static System.Action<PlayerHealth, int> OnHeartPickup;

    // 처치 경험치가 정해진 순간(Enemy 사망 처리). xpValue = 배율 전 기본 경험치.
    public static System.Action<Enemy, int, bool, ActiveSkillId?> OnKillXp;

    // 경험치가 적립되는 순간(PlayerExperience.AddXP). 실제 적립 = amount × mult × stageFactor(반올림).
    public static System.Action<int, float, float> OnXpAdded;
}
