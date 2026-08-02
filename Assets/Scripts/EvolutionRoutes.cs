// ── 진화 재설계 (2026-07-29): 3경로 × 3티어 → 2루트 × 2티어 ──────────────────────
//
// 진화는 더 이상 "레벨 5의 배수 도달"로 열리지 않는다. 벽 스테이지 엘리트가 떨구는
// **진화 아이템**을 먹어야 열리고, 그때 진화 가능한 스킬 중 하나를 골라 진화시킨다.
//
// ⚠️ 저장 구조는 기존 PathTier[3]을 그대로 쓴다.
//    PlayerSkills/PlayerPassives의 Fire*/TryUseSkill이 `skill.PathTier[n] >= t`를 115군데에서
//    직접 읽고 있어서, 저장 형식을 바꾸면 진화 효과 전체를 다시 짜야 한다. 대신 여기서
//    "새 좌표 → 기존 좌표"만 번역한다:
//
//      새 루트 r(0/1)  → 기존 path 인덱스   (스킬마다 다름. RoutePath 표 참고)
//      새 티어 1       → 기존 T1 + T2 동시 적용 → PathTier[path] = 2
//      새 티어 2       → 기존 T3 적용          → PathTier[path] = 3
//
//    한 번의 진화가 옛 티어 2개를 한꺼번에 주므로 진화 1회의 체감이 예전의 2배가 된다(의도).
//    각 스킬에서 버리는 나머지 1개 path는 "레벨업 축과 겹치거나 밋밋한 것"을 골랐다.
public static class EvolutionRoutes
{
    public const int MaxStage = 2;             // 스킬당 진화 횟수 상한
    // 진화 요구 레벨 = 만렙. 1차·2차 모두 같다 — 진화하면 표시 레벨이 1로 리셋되므로
    // "만렙 찍고 진화, 다시 만렙 찍고 진화"가 된다. (누적 레벨 TotalLevel이 아니라 **표시 레벨**이 기준)
    public const int RequiredLevel = BalanceConstants.MaxSkillLevel;

    // 진화 시 즉시 붙는 기본 스탯 도약. 레벨 표시가 1로 리셋되는 대신 이만큼 세져서
    // "약해진 게 아니라 다른 스킬이 됐다"가 눈에 보이게 한다.
    public const float EvolveDamageMult = 1.5f;
    public const float EvolveCooldownMult = 0.9f;

    // ── 루트 → 기존 path 매핑 ────────────────────────────────────────────────
    // 버린 path와 이유:
    //   기본공격 0(관통·피해)   — 레벨업이 이미 관통/투사체/피해를 담당
    //   회오리   1(순수 쿨감)   — 밋밋함
    //   오브     0(공중 추가피해) — 히트박스 판정 도입으로 의미 축소
    //   낙뢰     2(회오리 연계)  — 회오리 없으면 통째로 죽는 칸
    //   독수리   0(투하 횟수)   — 레벨업 축(TickRate/ProjectileCount)과 중복
    //   스나이핑 0(타겟 수)     — 레벨업 축(MaxTargets)으로 이전됨
    //   호밍     0(미사일 수)   — 레벨업 축(ProjectileCount)과 중복
    //   산탄     0(타수)        — 레벨업 축과 중복
    //   되감기   0(되감기 시간) — 레벨업 축(RewindAmount)과 중복
    //   휘두르기 0(사거리·크기) — 레벨업 축(Scale)과 중복
    public static int RoutePath(ActiveSkillId id, int route) => (id, route) switch
    {
        (ActiveSkillId.BasicAttack, 0) => 1, (ActiveSkillId.BasicAttack, _) => 2,
        (ActiveSkillId.Whirlwind, 0) => 0,   (ActiveSkillId.Whirlwind, _) => 2,
        (ActiveSkillId.Orb, 0) => 1,         (ActiveSkillId.Orb, _) => 2,
        (ActiveSkillId.Lightning, 0) => 0,   (ActiveSkillId.Lightning, _) => 1,
        (ActiveSkillId.EagleDrop, 0) => 1,   (ActiveSkillId.EagleDrop, _) => 2,
        (ActiveSkillId.Sniping, 0) => 1,     (ActiveSkillId.Sniping, _) => 2,
        (ActiveSkillId.Homing, 0) => 1,      (ActiveSkillId.Homing, _) => 2,
        (ActiveSkillId.Shotgun, 0) => 1,     (ActiveSkillId.Shotgun, _) => 2,
        (ActiveSkillId.Rewind, 0) => 1,      (ActiveSkillId.Rewind, _) => 2,
        (ActiveSkillId.Swing, 0) => 1,       (ActiveSkillId.Swing, _) => 2,
        _ => route == 0 ? 0 : 1,
    };

    // 패시브는 전부 path0이 "같은 스탯 더 주기"(레벨업과 완전 중복)라 1·2를 루트로 쓴다.
    public static int RoutePath(PassiveSkillId id, int route) => route == 0 ? 1 : 2;

    // ── 루트 잠금(연계 조건) ─────────────────────────────────────────────────
    // 기존 path 좌표계가 그대로 조건이 된다: path1 = 패시브 연계, path2 = 액티브 연계, path0 = 무의존.
    // 연계 대상을 **보유**해야 그 루트가 열린다(레벨 조건은 없음 — 아이템이 희소해서 레벨까지 걸면
    // 아이템이 버려지는 판이 생긴다). 두 루트가 다 잠긴 스킬은 진화 목록에 아예 안 뜬다.
    public static PassiveSkillId? RoutePassivePrereq(ActiveSkillId id, int route) =>
        RoutePath(id, route) == 1 ? PlayerSkills.GetPassivePrereq(id) : null;

    public static ActiveSkillId? RouteActivePrereq(ActiveSkillId id, int route) =>
        RoutePath(id, route) == 2 ? PlayerSkills.GetActivePrereq(id) : null;

    public static PassiveSkillId? RoutePassivePrereq(PassiveSkillId id, int route) =>
        RoutePath(id, route) == 1 ? PlayerPassives.GetPassivePrereq(id) : null;

    public static ActiveSkillId? RouteActivePrereq(PassiveSkillId id, int route) =>
        RoutePath(id, route) == 2 ? PlayerPassives.GetActivePrereq(id) : null;

    // 잠금 사유 표기용 — 필요한 연계 스킬 이름. 조건이 없으면 null.
    public static string RoutePrereqName(ActiveSkillId id, int route)
    {
        PassiveSkillId? p = RoutePassivePrereq(id, route);
        if (p.HasValue) return PlayerSkills.GetPassiveSkillName(p.Value);
        ActiveSkillId? a = RouteActivePrereq(id, route);
        return a.HasValue ? PlayerSkills.GetActiveSkillName(a.Value) : null;
    }

    public static string RoutePrereqName(PassiveSkillId id, int route)
    {
        PassiveSkillId? p = RoutePassivePrereq(id, route);
        if (p.HasValue) return PlayerSkills.GetPassiveSkillName(p.Value);
        ActiveSkillId? a = RouteActivePrereq(id, route);
        return a.HasValue ? PlayerSkills.GetActiveSkillName(a.Value) : null;
    }

    // 새 티어(1/2)가 도달시키는 기존 PathTier 값. 1 → 2(T1+T2), 2 → 3(T3).
    public static int TargetPathTier(int newTier) => newTier == 1 ? 2 : 3;

    // 새 티어를 적용할 때 순서대로 적용해야 하는 기존 티어들.
    public static int[] LegacyTiersFor(int newTier) => newTier == 1 ? new[] { 1, 2 } : new[] { 3 };

    // ── 진화 후 이름 ─────────────────────────────────────────────────────────
    // 진화하면 스킬 이름이 바뀌고 레벨 표시가 1로 돌아간다("다른 스킬이 됐다"는 연출).
    public static string EvolvedName(ActiveSkillId id, int route, int stage) => (id, route, stage) switch
    {
        (ActiveSkillId.BasicAttack, 0, 1) => "암살 사격",
        (ActiveSkillId.BasicAttack, 0, 2) => "처형 사격",
        (ActiveSkillId.BasicAttack, 1, 1) => "매사냥",
        (ActiveSkillId.BasicAttack, 1, 2) => "군집 매사냥",

        (ActiveSkillId.Whirlwind, 0, 1) => "회오리 무리",
        (ActiveSkillId.Whirlwind, 0, 2) => "폭풍 군단",
        (ActiveSkillId.Whirlwind, 1, 1) => "대회오리",
        (ActiveSkillId.Whirlwind, 1, 2) => "재앙의 눈",

        (ActiveSkillId.Orb, 0, 1) => "대형 오브",
        (ActiveSkillId.Orb, 0, 2) => "빙결 오브",
        (ActiveSkillId.Orb, 1, 1) => "오브 제단",
        (ActiveSkillId.Orb, 1, 2) => "심판의 제단",

        (ActiveSkillId.Lightning, 0, 1) => "연쇄 낙뢰",
        (ActiveSkillId.Lightning, 0, 2) => "폭풍우",
        (ActiveSkillId.Lightning, 1, 1) => "전도 낙뢰",
        (ActiveSkillId.Lightning, 1, 2) => "천둥 그물",

        (ActiveSkillId.EagleDrop, 0, 1) => "포식 독수리",
        (ActiveSkillId.EagleDrop, 0, 2) => "흡혈 군단",
        (ActiveSkillId.EagleDrop, 1, 1) => "급강하 폭격",
        (ActiveSkillId.EagleDrop, 1, 2) => "회오리 폭격",

        (ActiveSkillId.Sniping, 0, 1) => "관통 저격",
        (ActiveSkillId.Sniping, 0, 2) => "몰살 저격",
        (ActiveSkillId.Sniping, 1, 1) => "자동 조준",
        (ActiveSkillId.Sniping, 1, 2) => "감시탑",

        (ActiveSkillId.Homing, 0, 1) => "작렬 미사일",
        (ActiveSkillId.Homing, 0, 2) => "융단 폭격",
        (ActiveSkillId.Homing, 1, 1) => "성장형 미사일",
        (ActiveSkillId.Homing, 1, 2) => "무한 성장 미사일",

        (ActiveSkillId.Shotgun, 0, 1) => "집중 산탄",
        (ActiveSkillId.Shotgun, 0, 2) => "일점사 산탄",
        (ActiveSkillId.Shotgun, 1, 1) => "광역 산탄",
        (ActiveSkillId.Shotgun, 1, 2) => "제압 산탄",

        (ActiveSkillId.Rewind, 0, 1) => "충전 되감기",
        (ActiveSkillId.Rewind, 0, 2) => "과부하 되감기",
        (ActiveSkillId.Rewind, 1, 1) => "가속 되감기",
        (ActiveSkillId.Rewind, 1, 2) => "시간 붕괴",

        (ActiveSkillId.Swing, 0, 1) => "쓸어치기",
        (ActiveSkillId.Swing, 0, 2) => "박살내기",
        (ActiveSkillId.Swing, 1, 1) => "지진파",
        (ActiveSkillId.Swing, 1, 2) => "대지 균열",

        _ => PlayerSkills.GetActiveSkillName(id),
    };

    public static string EvolvedName(PassiveSkillId id, int route, int stage) => (id, route, stage) switch
    {
        (PassiveSkillId.Strength, 0, 1) => "치명의 힘",
        (PassiveSkillId.Strength, 0, 2) => "파괴의 힘",
        (PassiveSkillId.Strength, 1, 1) => "완력",
        (PassiveSkillId.Strength, 1, 2) => "괴력",

        (PassiveSkillId.Health, 0, 1) => "강건함",
        (PassiveSkillId.Health, 0, 2) => "불굴",
        (PassiveSkillId.Health, 1, 1) => "가시 갑주",
        (PassiveSkillId.Health, 1, 2) => "복수의 갑주",

        (PassiveSkillId.Knowledge, 0, 1) => "보물 탐지",
        (PassiveSkillId.Knowledge, 0, 2) => "보물 감정",
        (PassiveSkillId.Knowledge, 1, 1) => "전투 통찰",
        (PassiveSkillId.Knowledge, 1, 2) => "전장의 현자",

        (PassiveSkillId.Assassinate, 0, 1) => "사냥꾼",
        (PassiveSkillId.Assassinate, 0, 2) => "학살자",
        (PassiveSkillId.Assassinate, 1, 1) => "폭풍 암살",
        (PassiveSkillId.Assassinate, 1, 2) => "회오리 학살",

        (PassiveSkillId.Refresh, 0, 1) => "재생 순환",
        (PassiveSkillId.Refresh, 0, 2) => "생명 순환",
        (PassiveSkillId.Refresh, 1, 1) => "가속 순환",
        (PassiveSkillId.Refresh, 1, 2) => "무한 순환",

        _ => PlayerSkills.GetPassiveSkillName(id),
    };
}
