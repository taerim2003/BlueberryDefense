using UnityEngine;

// 맵 1종의 데이터(배경·BGM·스테이지 구성·적 로스터). RunConfig.Map으로 선택되어 RunBootstrap이 씬에 적용.
// "맵 = 적 로스터·스폰 파라미터·배경까지 다름"을 단일 SO로 표현한다. 기본 BlueberryField 에셋은 현재 씬 값 그대로.
[CreateAssetMenu(fileName = "MapDefinition", menuName = "BlueberryDefense/Map Definition")]
public class MapDefinition : ScriptableObject
{
    [Header("선택 화면")]
    public string displayName;            // 맵 선택 카드에 표시되는 이름 (예: "블루베리 밭")
    [TextArea] public string description; // 카드 하단/툴팁 설명 (선택)

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
