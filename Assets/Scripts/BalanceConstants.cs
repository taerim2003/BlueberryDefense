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
    public const int OrbBaseTargets = 2;      // 오브가 동시에 갈아버리는 적 수
    public const int HomingBaseMissiles = 3;  // 호밍 미사일 수(시작값 하향 5→3)
    public const int EagleBaseDrops = 2;      // 독수리 투하 횟수(시작값 하향 3→2)
    public const int ShotgunBasePellets = 3;      // 산탄 알 수(신규)
    public const float ShotgunSpreadDegrees = 22f; // 산탄 부채꼴 반각 — 알이 늘수록 같은 각도 안이 촘촘해진다
    public const float FlyingArrowSpawnRaise = 0.65f; // 비행 타격 진화 시 발사점 상승

    // ── 적 접촉 모델: "닿으면 한 방 주고 자폭" → "플레이어 앞에 줄 서서 계속 박치기" ──
    // 적은 더 이상 스스로 사라지지 않는다. 죽여야만 없어지고, 그 대신 1회 피해가 훨씬 약하다.
    public const float ContactStopDistance = 1.35f;   // 플레이어로부터 이 x거리에서 멈춰 선다(돌진할 여유를 두고 물러서 있음)
    public const float HeadbuttInterval = 1f;         // 박치기 주기(초)
    public const float HeadbuttDamageScale = 0.35f;   // EnemyDefinition.damage에 곱해지는 1회 피해 배율
    public const float HeadbuttLungeDistance = 0.5f;  // 박치기할 때 앞으로 튀어나가는 거리
    public const float HeadbuttLungeDuration = 0.26f; // 나갔다 제자리로 돌아오는 총 시간(피해는 최전방 도달 순간)
    public const float HeadbuttLungeTilt = 18f;       // 돌진하며 앞으로 기우는 각도(도) — 튀어나간 만큼 같이 기울었다 돌아온다
    // 앞 적과의 **중심 간 x거리**. 적 스프라이트 폭이 약 1.1유닛(콜라이더 0.75 × scale 1.5)이라
    // 이 값이 그보다 훨씬 작아야 서로 깊게 겹쳐서 "두께 있는 무리"로 보인다(줄 서 있는 것처럼 보이면 이 값을 더 줄일 것).
    public const float EnemyStackSpacing = 0.042f;
    public const float EnemyStackSearchRadius = 1.2f; // 앞 적 **후보 탐색** 반경(정지 판정은 중심거리로 함 — 이 값은 판정에 안 쓰임)
    public const float EnemySpawnYJitter = 0.065f;    // 스폰 시 y를 이만큼 랜덤하게 흔든다 — 줄이 자로 잰 듯 정렬되지 않게(EnemyLaneTolerance보다 훨씬 작아야 레인이 안 갈라짐)
    public const float EnemyLaneTolerance = 0.6f;     // y가 이보다 벌어지면 다른 레인(지상/공중)이라 서로 안 막음
}
