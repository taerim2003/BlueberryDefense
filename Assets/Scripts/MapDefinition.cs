using UnityEngine;

// 맵 1종의 데이터(배경·BGM·스테이지 구성·적 로스터). RunConfig.Map으로 선택되어 RunBootstrap이 씬에 적용.
// "맵 = 적 로스터·스폰 파라미터·배경까지 다름"을 단일 SO로 표현한다. 기본 BlueberryField 에셋은 현재 씬 값 그대로.
[CreateAssetMenu(fileName = "MapDefinition", menuName = "BlueberryDefense/Map Definition")]
public class MapDefinition : ScriptableObject
{
    [Header("선택 화면")]
    public string displayName;            // 맵 선택 카드에 표시되는 이름 (예: "블루베리 밭")
    [TextArea] public string description; // 카드 하단/툴팁 설명 (선택)

    [Header("필드 크기")]
    // 이 맵의 플레이 영역 배율(1 = 씬 기본 = 17.78×10유닛). RunBootstrap이 판 시작 시 카메라 ortho와
    // 절대 좌표(플레이어·스포너 위치)에 곱한다. **캐릭터·적의 월드 크기는 안 건드린다** —
    // 도트가 비정수 배율로 뭉개지기 때문. 필드가 넓어진 만큼 화면에서 작아 보이는 게 의도한 결과다.
    // ⚠️ background 그림도 같은 배율로 넓어야 한다(PPU 18 기준 1배=320×180px, 1.5배=480×270, 2배=640×360).
    //    안 그러면 넓어진 화면의 가장자리가 빈다.
    public float fieldScale = 1f;

    // 카메라와 배경을 이만큼 위로 올린다(월드 유닛) = 화면 안에서 **지상 레인이 그만큼 아래로 내려간다.**
    // fieldScale은 y를 비율로만 벌려서 화면이 세로로 커져도 레인이 화면 한가운데 근처에 머문다 —
    // 넓은 맵에서 "레인이 너무 높다"는 문제를 이 값으로 따로 해결한다.
    // ⚠️ **플레이어·적의 월드 좌표는 일부러 안 건드린다.** 절대 y를 쓰는 것들(회오리 착지선 groundY=0 등)이
    //    그대로 맞아떨어져야 하기 때문. 대신 카메라 기준으로 계산되는 것(UFO 호버 고도·종이비행기 강하 높이)은
    //    자동으로 같이 올라가서 하늘 공간이 그만큼 넓어진다 — 이것도 노린 결과다.
    public float cameraYLift = 0f;

    // 게릴라(중간 소환) 구간과 UFO 등장 범위를 이만큼 **플레이어 쪽(+x)으로** 민다(월드 유닛). 0 = 전 맵 공통 구간 그대로.
    // 2026-10-06 사용자: 우주는 농장과 달리 앞에서 나와도 대응할 시간이 있다 → 우주만 2.
    // (9/21의 "구간은 전 맵 동일"을 우주에 한해 푼 것이다 — 기준 구간은 BalanceConstants.AmbushBand*.)
    public float ambushShiftX = 0f;

    [Header("표시")]
    public Sprite background;
    // 배경을 여러 컷으로 돌리고 싶을 때만 채운다(2장 이상이어야 동작). 비어 있으면 위 background로 정지 표시.
    // 컷은 전부 같은 픽셀 규격이어야 한다 — 크기가 다르면 컷이 바뀔 때 화면 덮는 범위가 튄다.
    public Sprite[] backgroundFrames;
    public float backgroundFrameSeconds = 0.4f;
    public AudioClip bgm; // null이면 무음(현재 상태). RunBootstrap이 있으면 루프 재생

    // ── 해금 조건 ──────────────────────────────────────────────────────────────
    // 앞 맵을 특정 승천까지 깨야 다음 맵이 열린다(StS식 진행). CharacterDefinition과 **같은 구조·같은 이유**로 만든다.
    // ⚠️ unlockedFromStart 기본값이 true인 것도 같은 이유다: 이 필드가 없던 시절 임포트된 기존 에셋이
    //    직렬화 캐시의 옛 값을 쓰기 때문에, 기본값은 반드시 "기존 동작(전부 해금)"과 같은 쪽이어야 한다.
    //    새로 잠글 맵만 명시적으로 false로 둘 것.
    // ⚠️ 판정 근거인 MapClearSave는 **세션26에 생겨서 그 이전 클리어가 소급되지 않는다** —
    //    이미 게임을 깬 세이브에서도 첫 맵부터 다시 깨야 다음 맵이 열린다(치트 창에 해금 버튼을 둔 이유).
    [Header("해금 조건 (unlockedFromStart면 무시)")]
    public bool unlockedFromStart = true;
    public MapDefinition requiredClearMap;    // null이면 맵 조건 없음
    public int requiredClearAscension = 2;    // 그 맵에서 클리어해야 하는 승천 등급

    public bool IsUnlocked => unlockedFromStart || ClearConditionMet;

    private bool ClearConditionMet =>
        requiredClearMap == null || MapClearSave.HasCleared(requiredClearMap.name, requiredClearAscension);

    // 잠긴 카드에 그대로 띄우는 조건 문구. 없으면 빈 문자열.
    public string UnlockConditionText()
    {
        if (unlockedFromStart || requiredClearMap == null) return "";
        return Loc.F("unlock.clearMap", requiredClearMap.Name, AscensionTable.DifficultyName(requiredClearAscension));
    }

    // 표시 문구는 표에서 읽는다. 키는 **에셋 이름**에서 파생 — displayName은 표시용이라 바뀔 수 있다(MapClearSave와 같은 이유).
    public string Name => Loc.TOr("map.name." + name, string.IsNullOrEmpty(displayName) ? name : displayName);
    public string Desc => Loc.TOr("map.desc." + name, description);

    [Header("스테이지 구성")]
    public StageTable stageTable;
    public int bossStage = 15;
    public float spawnInterval = 1.5f; // StageData 없을 때 폴백 간격
    public int defaultSpawnCount = 20; // StageData 없을 때 폴백 물량

    [Header("적 로스터")]
    public GameObject enemyPrefab;
    public GameObject treasureEnemyPrefab;
    public GameObject eliteEnemyPrefab;
    public GameObject paperPlaneEnemyPrefab;
    public GameObject ufoEnemyPrefab;
    public GameObject shieldEnemyPrefab;
    public GameObject riderEnemyPrefab;
    public GameObject hopperEnemyPrefab;
    public GameObject surferEnemyPrefab;
    public GameObject airshipEnemyPrefab;   // 해적 비행선(엘리트 공중) — 이 프리팹을 꽂은 맵에만 등장한다
    // 🔴 **보스는 승천 티어마다 오브젝트가 따로다**(2026-09-21 사용자: "보스 그냥 보통/어려움이랑 오브젝트 분리해"
    //    → "보통이랑 어려움 보스도 분할해"). 같은 오브젝트를 공유하면 한 티어를 조율할 때 다른 티어가 같이 움직인다 —
    //    실제로 농장 보통의 보스 체력을 −30% 했을 때 어려움 보스까지 같이 약해졌다.
    //    배선: Easy = 승천 1 · **bossEnemyPrefab = 승천 2(보통)** · Hard = 승천 3.
    //    ⚠️ Easy/Hard가 **비어 있으면 bossEnemyPrefab을 그대로 쓴다** — 안 꽂은 맵은 종전과 완전히 같이 동작한다.
    public GameObject bossEnemyPrefab;
    public GameObject bossEnemyPrefabEasy;
    public GameObject bossEnemyPrefabHard;

    // 🔴 보스를 잡을 때 흩뿌려지는 적들(2026-09-27 사용자: "UFO는 나오지 않도록"). **맵마다 달라야 한다** —
    //    보스 프리팹 3종은 세 맵이 **공유**하므로 프리팹의 deathSpawnPrefabs를 고치면 세 맵이 같이 움직인다.
    //    비어 있으면 보스 프리팹의 deathSpawnPrefabs로 떨어진다(= 안 꽂은 맵은 종전과 같다).
    // ⚠️ 마리마다 이 배열에서 **균등 추첨**한다(Enemy.SpawnDeathBurst) — 특정 종류의 비중을 낮추려면
    //    흔하게 낼 쪽(일반 블루베리)을 여러 칸에 넣어 가중치를 만든다.
    // 🔴 캐리어(UFO)를 넣지 말 것: 분출로 생기면 화면 위로 상승 퇴장하는데 그동안 Enemy.Active에 남아
    //    GameManager의 "잔몹 0" 클리어 조건이 안 채워진다 → 판이 끝나지 않는다.
    public GameObject[] bossDeathSpawnPrefabs;

    // 🔴 맵마다 보스 생김새를 다르게 한다(2026-09-29 사용자: "해변 맵의 보스는 생김새가 달랐으면 좋겠어서 그렸어").
    //    bossDeathSpawnPrefabs와 **같은 이유**로 맵 쪽에 둔다 — 보스 프리팹 3종은 세 맵이 공유하므로
    //    프리팹의 그림을 바꾸면 세 맵이 같이 바뀐다. 비어 있으면 프리팹 그림 그대로(= 안 꽂은 맵은 종전과 같다).
    // ⚠️ 그림만 덮어쓴다 — 콜라이더는 아래 bossColliderSize로 따로 맞춘다. 그림 크기가 크게 다르면
    //    실루엣과 피격 범위가 어긋나므로 캡처로 한 번 볼 것.
    public Sprite[] bossSpriteFrames;

    // 보스 BoxCollider2D 크기(로컬). 같은 이유로 맵 쪽에 둔다.
    // (0,0)이면 프리팹 크기·중심 그대로. 2026-09-30 사용자: 크라켄 히트박스를 "스프라이트 크기랑 비슷하게".
    public Vector2 bossColliderSize;
    // 보스 BoxCollider2D 중심(로컬). **bossColliderSize가 (0,0)이 아닐 때만** 같이 적용된다 — (0,0)도 유효한 중심이라
    // 크기 칸이 스위치 역할을 한다. 프리팹 중심은 (0, −0.4)이고, 그림 중심에 맞추려면 (0,0)이다.
    public Vector2 bossColliderOffset;

    // 보스 정지선을 이만큼 더 앞(왼쪽)에서 잡는다(2026-09-30 사용자: 베리크루저가 "너무 가까워. 좀 뒤에 서있어야").
    // 정지선(플레이어 x − ContactStopDistance)은 적 **중심** 기준이라, 그림이 큰 보스는 몸통이 플레이어를 덮는다. 0이면 종전.
    public float bossHoldbackX;

    // 0보다 크면 보스가 이동 중 위아래로 출렁인다(2026-09-30 사용자: "날아다니는 거니까"). 멈추면 가라앉는다.
    // 강하 유닛(diveBob)의 출렁임 곡선을 그대로 빌린다 — 참고로 우주 종이비행기는 진폭 1.5 · 속도 1.
    public float bossFloatBobAmplitude;
    public float bossFloatBobSpeed = 1f;

    // 이 맵의 정수 획득 기본 배율. 스킬트리(부유)·승천 배율 위에 곱한다(MetaRunApplier). 1이면 보너스 없음.
    // 2026-10-01 사용자: 해안가 1.3 · 우주 1.6.
    public float essenceMult = 1f;

    // 보스 체력 배율(층·승천 배율 위에 곱한다, EnemySpawner의 보스 슬롯에서만). bossSpriteFrames와 같은 이유로 맵 쪽에 둔다 —
    // 보스 정의의 maxHealth를 바꾸면 세 맵이 같이 바뀐다. 1이면 종전. 2026-10-01 사용자: 우주 2.
    public float bossHpMultiplier = 1f;
}
