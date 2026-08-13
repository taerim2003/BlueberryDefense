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
        return Loc.F("unlock.clearMap", requiredClearMap.Name, requiredClearAscension);
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
    public GameObject bossEnemyPrefab;
}
