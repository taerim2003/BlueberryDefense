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

    [Header("표시")]
    public Sprite background;
    public AudioClip bgm; // null이면 무음(현재 상태). RunBootstrap이 있으면 루프 재생

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
    public GameObject bossEnemyPrefab;
}
