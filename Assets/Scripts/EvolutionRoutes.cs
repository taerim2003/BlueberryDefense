// ── 진화 재설계 (2026-07-29): 3경로 × 3티어 → 2루트 × 2티어 ──────────────────────
//
// 진화는 더 이상 "레벨 5의 배수 도달"로 열리지 않는다. 벽 스테이지 엘리트가 떨구는
// **진화 아이템**을 먹어야 열리고, 그때 진화 가능한 스킬 중 하나를 골라 진화시킨다.
//
// ⚠️ 저장 구조는 기존 PathTier[3]을 그대로 쓴다.
//    PlayerSkills/PlayerPassives의 Fire*/TryUseSkill이 `skill.PathTier[n] >= t`를 수십 군데에서
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
    public const int MaxStage = 2;             // 액티브 스킬당 진화 횟수 상한
    // 🔴 **패시브는 2차 진화를 만들지 않는다**(2026-09-08 사용자 결정). 진화 창에서 2차 칸 자체가 안 뜬다.
    //    되돌리려면 이 상수를 2로 올리기만 하면 된다 — UI·문구 수확·게이트가 전부 여기서 갈린다.
    public const int MaxPassiveStage = 1;

    public static int MaxStageFor(ActiveSkillId id) => MaxStage;
    public static int MaxStageFor(PassiveSkillId id) => MaxPassiveStage;
    // 진화 요구 레벨 = 만렙. 1차·2차 모두 같다 — 진화하면 표시 레벨이 1로 리셋되므로
    // "만렙 찍고 진화, 다시 만렙 찍고 진화"가 된다. (누적 레벨 TotalLevel이 아니라 **표시 레벨**이 기준)
    public const int RequiredLevel = BalanceConstants.MaxSkillLevel;

    // 진화 시 즉시 붙는 기본 스탯 도약. 레벨 표시가 1로 리셋되는 대신 이만큼 세져서
    // "약해진 게 아니라 다른 스킬이 됐다"가 눈에 보이게 한다.
    public const float EvolveDamageMult = 1.5f;
    // 🔴 진화는 **쿨을 줄이지 않는다**(2026-09-07 사용자 결정 — 진화 스킬 쿨이 전부 너무 짧았다).
    //    도약은 피해(EvolveDamageMult)로만 준다. 1로 두는 이유는 이 값을 지우면 위 규칙이 안 보여서다.
    public const float EvolveCooldownMult = 1f;

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
        (ActiveSkillId.GrapeToss, 0) => 1,   (ActiveSkillId.GrapeToss, _) => 2,
        _ => route == 0 ? 0 : 1,
    };

    // 패시브는 전부 path0이 "같은 스탯 더 주기"(레벨업과 완전 중복)라 1·2를 루트로 쓴다.
    public static int RoutePath(PassiveSkillId id, int route) => route == 0 ? 1 : 2;

    // ── 루트 잠금(연계 조건) ─────────────────────────────────────────────────
    // 연계 대상을 **보유**해야 그 루트가 열린다(레벨 조건은 없음 — 아이템이 희소해서 레벨까지 걸면
    // 아이템이 버려지는 판이 생긴다). 두 루트가 다 잠긴 스킬은 진화 목록에 아예 안 뜬다.
    //
    // ⚠️ 조건은 **RoutePath와 무관하다**(2026-08-06 개편). 예전엔 path 좌표가 곧 조건이었지만
    //    (path1=패시브 연계 / path2=액티브 연계 / path0=무조건) 새 조건표는 그 틀을 셋 다 깬다:
    //      · 한 스킬의 두 루트가 **둘 다 액티브** 조건(회오리·독수리·산탄) 또는 **둘 다 패시브**(되감기)
    //      · R0이 액티브 · R1이 패시브인 **반대 배치**(낙뢰·스나이핑·호밍·가속·방어)
    //      · 옛 path0(무조건) 자리에도 조건이 붙는다 — 이제 **조건 없는 루트는 없다**
    //    RoutePath는 진화 **효과** 매핑에만 계속 쓰인다. 조건은 아래 표가 단독 소유다.
    //    (원본: 노션 "스킬 데이터 시트 > NEW 진화조건 표")
    private static (PassiveSkillId?, ActiveSkillId?) Need(PassiveSkillId p) => (p, null);
    private static (PassiveSkillId?, ActiveSkillId?) Need(ActiveSkillId a) => (null, a);

    public static (PassiveSkillId? Passive, ActiveSkillId? Active) RoutePrereq(ActiveSkillId id, int route) => (id, route) switch
    {
        (ActiveSkillId.BasicAttack, 0) => Need(PassiveSkillId.Assassinate),
        (ActiveSkillId.BasicAttack, _) => Need(ActiveSkillId.EagleDrop),

        (ActiveSkillId.Whirlwind, 0) => Need(ActiveSkillId.BasicAttack),
        (ActiveSkillId.Whirlwind, _) => Need(ActiveSkillId.Orb),

        (ActiveSkillId.Orb, 0) => Need(PassiveSkillId.Knowledge),
        (ActiveSkillId.Orb, _) => Need(ActiveSkillId.Homing),

        (ActiveSkillId.Lightning, 0) => Need(ActiveSkillId.Rewind),
        (ActiveSkillId.Lightning, _) => Need(PassiveSkillId.Strength),

        (ActiveSkillId.EagleDrop, 0) => Need(ActiveSkillId.Shotgun),
        (ActiveSkillId.EagleDrop, _) => Need(ActiveSkillId.Whirlwind),

        (ActiveSkillId.Sniping, 0) => Need(ActiveSkillId.EagleDrop),
        (ActiveSkillId.Sniping, _) => Need(PassiveSkillId.Defense),

        (ActiveSkillId.Homing, 0) => Need(ActiveSkillId.Shotgun),
        (ActiveSkillId.Homing, _) => Need(PassiveSkillId.Accel),

        (ActiveSkillId.Shotgun, 0) => Need(ActiveSkillId.Sniping),
        (ActiveSkillId.Shotgun, _) => Need(ActiveSkillId.Swing),

        (ActiveSkillId.Rewind, 0) => Need(PassiveSkillId.Strength),
        (ActiveSkillId.Rewind, _) => Need(PassiveSkillId.Accel),

        (ActiveSkillId.Swing, 0) => Need(PassiveSkillId.Health),
        (ActiveSkillId.Swing, _) => Need(ActiveSkillId.Lightning),

        // 포도 — R0 생화학(건강) / R1 찌릿찌릿(낙뢰). 기획안 그대로.
        (ActiveSkillId.GrapeToss, 0) => Need(PassiveSkillId.Health),
        (ActiveSkillId.GrapeToss, _) => Need(ActiveSkillId.Lightning),

        _ => (null, null),
    };

    public static (PassiveSkillId? Passive, ActiveSkillId? Active) RoutePrereq(PassiveSkillId id, int route) => (id, route) switch
    {
        (PassiveSkillId.Strength, 0) => Need(PassiveSkillId.Assassinate),
        (PassiveSkillId.Strength, _) => Need(ActiveSkillId.Shotgun),

        (PassiveSkillId.Health, 0) => Need(PassiveSkillId.Strength),
        (PassiveSkillId.Health, _) => Need(ActiveSkillId.Orb),

        (PassiveSkillId.Knowledge, 0) => Need(PassiveSkillId.Accel),
        (PassiveSkillId.Knowledge, _) => Need(ActiveSkillId.Homing),

        (PassiveSkillId.Assassinate, 0) => Need(PassiveSkillId.Knowledge),
        (PassiveSkillId.Assassinate, _) => Need(ActiveSkillId.Sniping),

        (PassiveSkillId.Defense, 0) => Need(ActiveSkillId.Swing),
        (PassiveSkillId.Defense, _) => Need(PassiveSkillId.Health),

        (PassiveSkillId.Accel, 0) => Need(ActiveSkillId.Rewind),
        (PassiveSkillId.Accel, _) => Need(PassiveSkillId.Defense),

        // Refresh는 폐지돼 조건표에 없다(레벨업 후보에서 빠져 획득 자체가 안 된다).
        _ => (null, null),
    };

    public static PassiveSkillId? RoutePassivePrereq(ActiveSkillId id, int route) => RoutePrereq(id, route).Passive;
    public static ActiveSkillId? RouteActivePrereq(ActiveSkillId id, int route) => RoutePrereq(id, route).Active;
    public static PassiveSkillId? RoutePassivePrereq(PassiveSkillId id, int route) => RoutePrereq(id, route).Passive;
    public static ActiveSkillId? RouteActivePrereq(PassiveSkillId id, int route) => RoutePrereq(id, route).Active;

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

    // ── 2차 진화의 열쇠 (2026-09-08 신설) ───────────────────────────────────
    // 🔴 **2차는 "다른 진화체 하나를 이미 만들어 뒀을 것"을 요구한다.** 1차의 루트 조건(§RoutePrereq)이
    //    "그 스킬을 **보유**"인 것과 달리, 여기는 **그 스킬이 그 루트로 1차 진화까지 돼 있어야** 한다.
    //    고른 기준은 오직 **2차가 하는 일과 어울리는가**다 — 치명타로 터지는 진화엔 암살을,
    //    비처럼 쏟아지는 진화엔 화살비를 건다. (원본: 노션 「스킬 데이터 시트」 > 2차 진화 열쇠)
    // ⚠️ 열쇠는 **1차만** 요구한다. 2차를 열쇠로 걸면 서로가 서로를 기다리는 판이 나온다.
    // 🔴 **열쇠에 캐릭터 전용 스킬을 걸지 말 것.** 레벨업 후보 풀은 8종뿐이고(LevelUpUI)
    //    `BasicAttack`·`Swing`·`GrapeToss`는 각각 딸기·파인애플·포도의 **시작 스킬이라 다른 캐릭터가 못 얻는다.**
    //    걸면 그 2차가 한 캐릭터 전용이 되거나(아예 도달 불가가 되기도 한다) 조용히 죽는다 — 화면엔 그냥 잠긴 칸으로 보인다.
    //    같은 이유로 **열쇠의 루트 조건**도 봐야 한다: `Whirlwind R0`(←화살)·`Shotgun R1`(←휘두르기)·
    //    `Defense R0`(←휘두르기)은 그 자체가 캐릭터 전용이라 열쇠로 못 쓴다.
    //    (2026-09-08: 처음 짤 때 여섯 칸이 여기 걸렸다. 「로열 팔라딘의 망치」는 파인애플이 포도알을 못 얻어 **영영 불가**였다.)
    private static (PassiveSkillId?, ActiveSkillId?, int) Key(PassiveSkillId p, int route) => (p, null, route);
    private static (PassiveSkillId?, ActiveSkillId?, int) Key(ActiveSkillId a, int route) => (null, a, route);

    public static (PassiveSkillId? Passive, ActiveSkillId? Active, int Route) Stage2Prereq(ActiveSkillId id, int route) => (id, route) switch
    {
        // 추격 화살은 **치명타로 맞힐 때만** 나간다 → 치명타 킬을 보상하는 진화가 열쇠
        (ActiveSkillId.BasicAttack, 0) => Key(PassiveSkillId.Assassinate, 0),  // 현상금
        // 하늘을 덮는 화살비 ← 하늘에서 떨어지는 것끼리
        (ActiveSkillId.BasicAttack, _) => Key(ActiveSkillId.EagleDrop, 0),     // 폭탄 독수리

        // 회오리를 끊임없이 뽑는 **설치물** ← 설치물끼리
        (ActiveSkillId.Whirlwind, 0) => Key(ActiveSkillId.Lightning, 1),       // 피뢰침
        // 맞은 적을 **취약**하게 만든다 ← 적을 물렁하게 만드는 오브
        (ActiveSkillId.Whirlwind, _) => Key(ActiveSkillId.Orb, 0),             // 강력한 마력

        // 주위를 **끌어당기는** 초대형 오브 ← 빨아들이는 거대 소용돌이
        (ActiveSkillId.Orb, 0) => Key(ActiveSkillId.Whirlwind, 1),             // 대회오리
        // **수많은** 오브를 다룬다 ← 수많은 소형 발사체
        (ActiveSkillId.Orb, _) => Key(ActiveSkillId.Homing, 1),                // 소형 미사일 다발

        // 20스택까지 **축적**한다 ← 충전해서 한 번에 터뜨리는 진화
        (ActiveSkillId.Lightning, 0) => Key(ActiveSkillId.Rewind, 0),          // 충전 되감기
        // 제우스의 은총 = 압도적인 힘
        (ActiveSkillId.Lightning, _) => Key(PassiveSkillId.Strength, 0),       // 불타는 근육

        // 거대한 **폭발** 한 방 ← 폭발을 다루는 진화
        (ActiveSkillId.EagleDrop, 0) => Key(ActiveSkillId.Homing, 0),          // 묵직한 탄두
        // **끊임없이 쏟아진다** ← 수많은 것이 하늘에서 쏟아지는 진화
        (ActiveSkillId.EagleDrop, _) => Key(ActiveSkillId.Homing, 1),          // 소형 미사일 다발

        // 독수리 특공대를 **지휘**한다 ← 독수리를 먼저 길들여 뒀을 것
        (ActiveSkillId.Sniping, 0) => Key(ActiveSkillId.EagleDrop, 0),         // 폭탄 독수리
        // **피해를 입으면** 몸이 알아서 움직인다 ← 맞을 때마다 반응하는 진화
        (ActiveSkillId.Sniping, _) => Key(PassiveSkillId.Accel, 1),            // 고통 가속

        // **가장 강한 적**에게 한 발 ← 최강 적을 조준하는 진화
        (ActiveSkillId.Homing, 0) => Key(ActiveSkillId.Sniping, 0),            // 독수리 저격
        // 세상을 뒤덮는 물량 ← 작은 것을 여러 개 뿌리는 진화
        (ActiveSkillId.Homing, _) => Key(ActiveSkillId.Orb, 1),                // 추적 오브

        // 공격 **대신** 버프를 건다 ← 스킬을 강화하는 진화
        (ActiveSkillId.Shotgun, 0) => Key(PassiveSkillId.Strength, 1),         // 생활 근육
        // **무조건 치명타** ← 치명타 상한을 뚫어 둔 진화
        (ActiveSkillId.Shotgun, _) => Key(PassiveSkillId.Assassinate, 1),      // 필중 암살

        // **다음 공격을 강하게** 만든다 ← 공격에 타수를 얹어 주는 진화(같은 결의 버프)
        (ActiveSkillId.Rewind, 0) => Key(ActiveSkillId.Shotgun, 0),            // 보너스 탄환 장착
        // 쿨타임이 **사라진다** ← 쿨을 초기화하는 진화
        (ActiveSkillId.Rewind, _) => Key(PassiveSkillId.Accel, 0),             // 리프레쉬

        // 맞은 적이 **오래 못 일어난다** ← 발을 땅에 붙여 두는 진화
        (ActiveSkillId.Swing, 0) => Key(ActiveSkillId.Whirlwind, 1),           // 대회오리
        // 땅을 타고 번지는 충격이 커진다 ← 땅에 꽂아 두는 진화
        (ActiveSkillId.Swing, _) => Key(ActiveSkillId.Lightning, 1),           // 피뢰침

        // 쓰러진 자리에서 **터져 번진다** ← 착탄 자리를 터뜨리는 진화
        (ActiveSkillId.GrapeToss, 0) => Key(ActiveSkillId.EagleDrop, 0),       // 폭탄 독수리
        // 찌릿찌릿 = 번개 ← 번개를 쌓아 두는 진화
        (ActiveSkillId.GrapeToss, _) => Key(ActiveSkillId.Lightning, 0),       // 뇌운 축적

        _ => (null, null, 0),
    };

    // 잠금 사유 표기용 — 열쇠 **진화체의 이름**을 준다("화살비를 먼저 만들 것"). 조건이 없으면 null.
    public static string Stage2PrereqName(ActiveSkillId id, int route)
    {
        var (p, a, keyRoute) = Stage2Prereq(id, route);
        if (p.HasValue) return EvolvedName(p.Value, keyRoute, 1);
        return a.HasValue ? EvolvedName(a.Value, keyRoute, 1) : null;
    }

    // 새 티어(1/2)가 도달시키는 기존 PathTier 값. 1 → 2(T1+T2), 2 → 3(T3).
    public static int TargetPathTier(int newTier) => newTier == 1 ? 2 : 3;

    // 새 티어를 적용할 때 순서대로 적용해야 하는 기존 티어들.
    public static int[] LegacyTiersFor(int newTier) => newTier == 1 ? new[] { 1, 2 } : new[] { 3 };

    // ── 진화 후 이름 ─────────────────────────────────────────────────────────
    // 진화하면 스킬 이름이 바뀌고 레벨 표시가 1로 돌아간다("다른 스킬이 됐다"는 연출).
    // 이름 문자열은 번역 표(`Assets/Localization/Tables/Game`)가 소유한다 — 키는 (스킬, 루트, 티어)에서 파생.
    // 정의가 없는 조합은 원래 스킬 이름으로 떨어진다(옛 `_ =>` 분기와 같은 동작).
    public static string EvolvedName(ActiveSkillId id, int route, int stage) =>
        Loc.TOr($"evo.name.{id}.{route}.{stage}", PlayerSkills.GetActiveSkillName(id));

    public static string EvolvedName(PassiveSkillId id, int route, int stage) =>
        Loc.TOr($"evo.name.{id}.{route}.{stage}", PlayerSkills.GetPassiveSkillName(id));
}
