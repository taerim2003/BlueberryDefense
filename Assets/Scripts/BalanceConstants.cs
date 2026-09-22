// 여러 시스템에 걸치는 전역 스킬 상수 집결지(Tier B — 로직 모양이라 SO로 빼지 않고 코드에 모음).
// 값은 여기서 편집하면 참조처(주로 PlayerSkills)가 별칭 const로 물려받아 전파된다.
// 스킬 기본 수치·레벨업 커브는 SkillProgression(SO), 캐릭터/맵별 데이터는 각 Definition(SO)을 볼 것.
public static class BalanceConstants
{
    public const float GlobalCooldown = 0.4f;       // 모든 스킬 공통 최소 간격 + 쿨타임 하한
    // 스킬/패시브 표시 레벨 상한 = 만렙. 여기 도달해야 진화가 열리고, 진화하면 Lv.1로 리셋되어 다시 이 값까지 큰다.
    // (레벨업 커브 Prog_*가 정확히 10레벨까지만 저작돼 있다 — 이 값을 올리면 11+가 옛 폴백 커브를 탄다)
    public const int MaxSkillLevel = 10;
    public const float OrbAltarCooldown = 15f;      // 오브 제단(Orb path2 T2+) 쿨타임
    public const float MaxCritChance = 0.7f;        // 치명타 확률 상한(상시 100% 크리 방지)
    public const int BasicAttackBaseHits = 3;       // 기본공격 기본 멀티히트 수
    // 기본공격 추가 발사체는 한 줄로 동시에 나가지 않고 "두두두둑" 연사된다 — 여러 발이 나간다는 게 눈에 보이게.
    // 세로 오프셋은 순수 연출(화살 히트박스가 세로 2.4유닛이라 명중 판정엔 영향 없음).
    public const float BasicAttackBurstInterval = 0.07f; // 추가 발사 간격(초)
    public const float BasicAttackBurstYOffset = 0.13f;  // 발사마다 위·아래로 번갈아 벌어지는 폭
    public const int SnipingBaseShots = 3;          // 스나이핑 타겟당 저격 횟수(레벨업으로 +, 시작값 하향 5→3)
    public const float SnipingShotInterval = 0.08f; // 스나이핑 저격 간격

    // ── 레벨업 성장축의 "시작값" ──
    // 레벨업이 눈에 보이려면 시작이 낮아야 한다(2→5마리가 5→8마리보다 훨씬 크게 느껴짐).
    // 오브가 사라지기 전까지 붙잡을 수 있는 총 적 수(소모성 예산). 이게 곧 체감상 "오브 관통력"이다 —
    // 예산을 다 쓰면 오브가 그 자리에서 사라져 무리를 끝까지 뚫지 못한다.
    // 8/24 플레이스루: 기본 오브 관통력이 낮다 → 4에서 6으로.
    // 2026-09-20 사용자: 오브는 관통이 충분해야 파워가 난다 → 6에서 10으로.
    //   레벨업(Prog_Orb의 관통 대상 +5 × 3단계)과 합쳐 만렙 25가 된다.
    public const int OrbBaseTargets = 10;
    public const int HomingBaseMissiles = 3;  // 호밍 미사일 수(시작값. 레벨업이 +1/+2로 붙어 만렙에 12발이 된다)
    public const int EagleBaseDrops = 3;      // 독수리 투하 횟수. 3→2→3, 9/14 5로(대신 Prog_EagleDrop.baseDamage 6→3, 9/17 버프로 5), 9/21 3으로(레벨업 +1×3 → 10렙 6회)
    public const int ShotgunBasePellets = 12;     // 산탄 알 수. 9/14 3→6, 9/17 12
    public const float ShotgunSpreadDegrees = 22f; // 산탄 부채꼴 반각 — 알이 늘수록 같은 각도 안이 촘촘해진다

    // ── 적 접촉 모델: "닿으면 한 방 주고 자폭" → "플레이어 앞에 줄 서서 계속 박치기" ──
    // 적은 더 이상 스스로 사라지지 않는다. 죽여야만 없어지고, 그 대신 1회 피해가 훨씬 약하다.
    // 플레이어로부터 이 x거리에서 멈춰 선다(돌진할 여유를 두고 물러서 있음). 딸기 한 칸(약 1.2유닛)만큼 더 물러서 있게 조정.
    // 2026-09-20 사용자: 박치기선을 아주 조금만 플레이어 쪽으로 → 2.55에서 2.3으로(박치기선 5.36 → 5.61).
    public const float ContactStopDistance = 2.3f;
    public const float HeadbuttInterval = 1f;         // 박치기 주기(초)
    // 도착 직후 첫 박치기까지의 뜸(초). 2026-09-20 사용자: "오자마자 박치기 하지 말고 0.5초 기다렸다".
    // Enemy가 headbuttTimer를 (HeadbuttInterval − 이 값)으로 채운 채 시작한다 — 0이면 도착 즉시 때린다.
    public const float HeadbuttFirstDelay = 0.5f;
    public const float HeadbuttDamageScale = 0.35f;   // EnemyDefinition.damage에 곱해지는 1회 피해 배율
    // ⚠️ 돌진 거리는 ContactStopDistance와 한 세트다 — 대기 위치를 뒤로 물리면 이만큼 더 튀어나가야
    //    최전방에서 실제로 플레이어에 닿는 그림이 된다(안 늘리면 허공을 향해 박치기한다).
    //    현재: 2.55에서 서서 1.4 튀어나가 최근접 1.15 (물리기 전 0.85와 같은 급).
    public const float HeadbuttLungeDistance = 1.4f;  // 박치기할 때 앞으로 튀어나가는 거리
    public const float HeadbuttLungeDuration = 0.32f; // 나갔다 제자리로 돌아오는 총 시간(피해는 최전방 도달 순간)
    public const float HeadbuttLungeTilt = 18f;       // 돌진하며 앞으로 기우는 각도(도) — 튀어나간 만큼 같이 기울었다 돌아온다
    // 앞 적과의 **중심 간 x거리**. 적 스프라이트 폭이 약 1.1유닛(콜라이더 0.75 × scale 1.5)이라
    // 이 값이 그보다 훨씬 작아야 서로 깊게 겹쳐서 "두께 있는 무리"로 보인다(줄 서 있는 것처럼 보이면 이 값을 더 줄일 것).
    public const float EnemyStackSpacing = 0.042f;
    public const float EnemyStackSearchRadius = 1.2f; // 앞 적 **후보 탐색** 반경(정지 판정은 중심거리로 함 — 이 값은 판정에 안 쓰임)
    public const float EnemySpawnYJitter = 0.065f;    // 스폰 시 y를 이만큼 랜덤하게 흔든다 — 줄이 자로 잰 듯 정렬되지 않게(EnemyLaneTolerance보다 훨씬 작아야 레인이 안 갈라짐)
    public const float EnemyLaneTolerance = 0.6f;     // y가 이보다 벌어지면 다른 레인(지상/공중)이라 서로 안 막음

    // ── 정수 수입의 스테이지 감쇠 ──
    // 🔴 **뒤 스테이지일수록 정수를 덜 준다**(2026-09-21 사용자 — BTD 시리즈처럼 스테이지가 갈수록 돈 배율이 준다).
    //    이유는 실측이다: 어려움 티어가 판당 62,983정수를 벌어 **쉬움의 13.5배**였다(표에 적힌 배수는 1.5배).
    //    최종층이 길어 뒤 스테이지를 많이 밟는 것이 곱으로 쌓인 것이고, 그래서 어려움 4.5판이면 풀트리
    //    (280,687정수)를 다 샀다 — "정주행 뒤 목표가 전부 1판"의 뿌리가 여기였다.
    //    🔴 **초반은 거의 평평하고 뒤에서 가파르다**(2026-09-21 사용자 — "초반 영향 없게").
    //    배율 = 1 − (1 − Min) × t^Power,  t = (층−1) / (FalloffStage−1)
    //    Power가 1이면 직선, 클수록 뒤로 쏠린다. 3이면 10층 ×0.96 · 15층 ×0.84 · 20층 ×0.60 · 25층 ×0.20.
    public const float StageEssenceMin = 0.2f;
    public const int StageEssenceFalloffStage = 25;
    public const float StageEssenceFalloffPower = 3f;

    // ── 중간 소환(ambush) ──
    // 적이 화면 왼쪽 등장 지점(x=-9)에서 다 죽어 x=-5~+6.5가 빈 땅이 되는 문제의 해법.
    // 화면 안 빈 구간에 예고 마커를 띄우고, 시간이 차면 그 자리에서 부대가 튀어나온다(전투를 플레이어 쪽으로 당김).
    public const float AmbushWarnDuration = 2.5f;     // 마커가 떠 있는 시간 — 예고 없이 튀어나오면 대응 불가라 반드시 필요
    // 🔴 **구간은 −7 ~ −1.8, 전 맵 동일하다**(2026-09-21 사용자: "주인공 쪽 맵 절반에는 게릴라가 안 나와야").
    //    둘 다 fieldScale을 곱하지 않는다(EnemySpawner) — 곱하면 넓은 맵에서 하한이 −12.6까지 가서
    //    정문 스폰(x=−9, 씬에 고정이고 맵 배율과 무관)보다 뒤에 매복이 생긴다(= 그냥 걸어오는 적이 된다).
    //    상한 −1.8의 근거: 정문(−9)과 박치기선(플레이어 7.91 − ContactStopDistance 2.3 = 5.61)의 중간이 −1.7이다.
    //    ⚠️ ContactStopDistance를 만지면 그 중간값이 움직이므로 여기도 같이 본다.
    //    이력: −3~3.8 → (9/17) −4.5~2.3 → (9/19) 상한 1.3 → (9/21) −7~−1.8.
    public const float AmbushBandMinX = -7f;
    public const float AmbushBandMaxX = -1.8f;
    public const int AmbushSquadMin = 4;
    public const int AmbushSquadMax = 6;
    public const float AmbushSquadSpreadX = 1.1f;     // 부대원이 마커 중심에서 좌우로 흩어지는 폭
}
