using System.Collections.Generic;
using UnityEngine;

// ⚠️ **UFO가 떨구는 투하물은 물량 쿼터에 안 잡힌다.** UFO 벽 스테이지(13·22)가 게임에서 제일 무거운 판인 이유고,
//    프레임이 떨어진다는 신고가 오면 여기부터 의심할 것. 쿼터를 올려 잡으면 그 두 판이 먼저 터진다.
public class EnemySpawner : MonoBehaviour
{
    // 적 로스터·스폰 파라미터는 MapDefinition이 소유한다. RunBootstrap이 판 시작 시 ActiveMap을 세팅(Start 전).
    // fallbackMap = 씬 단독 실행 시(RunBootstrap 없거나 RunConfig.Map null) 사용할 기본 맵.
    [SerializeField] private MapDefinition fallbackMap;
    [SerializeField] private ScalingTable scaling;       // 후반 체력/이속 스텝 배율(전역). 미할당 시 기본값 폴백
    [SerializeField] private AscensionTable ascension;   // 승천(난이도 등급) 배율표. 미할당 시 기본값 폴백

    public MapDefinition ActiveMap { get; set; }
    private MapDefinition Map => ActiveMap != null ? ActiveMap : fallbackMap;

    // 3스테이지마다 추가로 붙는 체력/이속 배율 — 처음엔 거의 안 느껴지다가 갈수록 증가폭이 커짐(후반일수록 스텝당 증가폭 자체가 커짐)
    private ScalingTable Scaling => scaling != null ? scaling : ScalingTable.Default;
    private AscensionTable Ascension => ascension != null ? ascension : AscensionTable.Default;

    // 지식 연계 path1: 블루베리 스폰 시 이 확률로 보물상자 블루베리로 대체
    public static float ExtraTreasureChance = 0f;

    private const float TreasureDelay = 2.5f; // 잔몹을 다 잡은 뒤 이 시간만큼 텀을 두고 보물상자 등장. 2026-09-20 사용자 "늦게 나오는 감" → 5에서 절반으로

    private float timer;
    private float treasureDelayTimer;
    private int treasureSpawnedForStage = 0;
    private int stageBeingCounted = -1;
    private bool bossSpawnedThisStage;
    private int evolutionElitesSpawnedThisStage; // 벽 스테이지 확정 엘리트를 몇 마리 내보냈나
    private int ambushesTriggeredThisStage;      // 중간 소환을 몇 번 예고했나
    private readonly List<AmbushMarker> pendingAmbushes = new List<AmbushMarker>(); // 예고 중인 마커들(하나라도 있으면 정문 스폰 정지)
    private int spawnedInBurst;                  // 현재 무리에서 몇 마리 내보냈나(웨이브 스폰. burstSize=0/1이면 안 쓰임)

    // 물량 기반 스폰 진행 상태 — GameManager가 스테이지 클리어 판정에 참조한다.
    public int SpawnedThisStage { get; private set; }
    public int SpawnTarget { get; private set; }
    public bool StageSpawnComplete => SpawnedThisStage >= SpawnTarget;

    // 첫 프레임에 GameManager.Update가 EnemySpawner.Update보다 먼저 돌면 SpawnTarget이 0이라
    // StageSpawnComplete가 참이 되어 1스테이지가 즉시 넘어가버린다(첫 판이 2스테이지에서 시작).
    // Start(모든 Awake 이후·첫 Update 이전)에서 현재 스테이지 물량을 미리 셋업해 방지.
    private void Start()
    {
        BeginStageCount(GameManager.Instance);
    }

    private void BeginStageCount(GameManager gm)
    {
        StageData stage = gm != null ? gm.CurrentStageData : null;
        stageBeingCounted = gm != null ? gm.CurrentStage : 1;
        SpawnedThisStage = 0;
        SpawnTarget = stage != null ? stage.spawnCount : Map.defaultSpawnCount;
        treasureSpawnedForStage = 0;
        bossSpawnedThisStage = false;
        evolutionElitesSpawnedThisStage = 0;
        ambushesTriggeredThisStage = 0;
        spawnedInBurst = 0;
        // 예고 중에 스테이지가 넘어가면 마커는 다음 판에 부대를 쏟아낸다 — 판이 바뀌는 즉시 취소.
        foreach (AmbushMarker marker in pendingAmbushes)
            if (marker != null) Destroy(marker.gameObject);
        pendingAmbushes.Clear();
        timer = 0f;
        treasureDelayTimer = 0f;
    }

    private void Update()
    {
        GameManager gm = GameManager.Instance;
        StageData stage = gm != null ? gm.CurrentStageData : null;
        int currentStage = gm != null ? gm.CurrentStage : 1;
        MapDefinition map = Map;

        // 스테이지가 바뀌면(전환 텀 진입 시점 포함) 이 스테이지의 물량 카운트를 리셋 — 정지 체크보다 먼저 돌아야 함
        if (currentStage != stageBeingCounted)
        {
            BeginStageCount(gm);
        }

        if (gm != null && gm.IsSpawningPaused) return; // 스테이지 전환 텀: 스폰 정지
        if (StageSpawnComplete) return;                // 이 스테이지 물량 다 스폰함 — 잔몹 처리는 GameManager가 대기
        if (pendingAmbushes.Count > 0) return;          // 중간 소환 예고 중: 정문 스폰을 멈춰 마커에 시선을 몰아준다

        const int treasureCount = 1; // 스테이지 종료 보물상자 블루베리는 모든 스테이지에서 1마리로 통일
        // 보스는 **승천표가 최종 판으로 삼는 스테이지 전부**(15·20·25)에 나온다 — 이번 판의 최종이 아니어도.
        // (20판 승천에서도 15판은 보스전이다.) MapDefinition.bossStage는 GameManager가 없는 씬 단독 실행용 폴백.
        bool isBossStage = map.bossEnemyPrefab != null &&
            (gm != null ? Ascension.IsBossStage(currentStage) : currentStage == map.bossStage);
        // 이 판을 깨면 게임이 끝나는가(GameManager.AdvanceStage의 클리어 조건과 같은 식).
        // 최종 판의 보상은 쓸 데가 없으므로 상자도 진화 아이템도 건너뛴다.
        bool isFinalStage = gm != null && currentStage >= gm.FinalStage;
        // 보스 판이어도 **뒤에 판이 남아 있으면** 보상을 준다 — 다음 판을 준비할 자원이 필요하다.
        bool bossStageWithRewards = isBossStage && !isFinalStage;
        bool treasureStage = map.treasureEnemyPrefab != null && (!isBossStage || bossStageWithRewards);

        // 물량 꼬리에서 예약해 둬야 하는 칸 수. 보상 있는 보스 판은 상자 + 보스로 2칸이 필요하다.
        // 🔴 중간 소환도 이 예약분을 침범하면 안 된다(TriggerAmbush에 같은 값을 넘긴다) —
        //    넘기면 StageSpawnComplete가 먼저 참이 되어 상자/보스가 영영 안 나온다.
        int reservedTail = (isBossStage ? 1 : 0) + (treasureStage ? treasureCount : 0);

        // 스테이지 종료 보물상자: 잔몹을 **다 잡은 뒤** TreasureDelay만큼 텀을 두고 등장.
        // 보스 판에서는 상자가 먼저 나오고(=보스 직전 파워 스파이크) 상자까지 잡으면 보스가 등장한다.
        if (treasureStage && treasureSpawnedForStage != currentStage && SpawnedThisStage >= SpawnTarget - reservedTail)
        {
            // 🔴 **잔몹이 다 죽은 뒤에** 상자가 나온다(2026-09-20 사용자).
            //    종전 조건은 "마지막 몹이 **스폰**된 뒤 5초"라, 적이 잔뜩 살아 있는데도 상자가 떴다.
            if (Enemy.Active.Count > 0) { treasureDelayTimer = 0f; return; }
            treasureDelayTimer += Time.deltaTime;
            if (treasureDelayTimer < TreasureDelay) return;
            SpawnEnemies(map.treasureEnemyPrefab, treasureCount, stage, currentStage);
            treasureSpawnedForStage = currentStage;
            return;
        }

        float interval = stage != null ? stage.spawnInterval : map.spawnInterval;
        float eliteChance = stage != null ? stage.eliteChance : 0f;
        float paperPlaneChance = stage != null ? stage.paperPlaneChance : 0f;
        float ufoChance = stage != null ? stage.ufoChance : 0f;
        float shieldChance = stage != null ? stage.shieldChance : 0f;
        float riderChance = stage != null ? stage.riderChance : 0f;
        float hopperChance = stage != null ? stage.hopperChance : 0f;
        float surferChance = stage != null ? stage.surferChance : 0f;
        float airshipChance = stage != null ? stage.airshipChance : 0f;

        // 웨이브(무리) 스폰: 무리를 다 쏟았으면 spawnInterval 대신 burstRest만큼 쉰다.
        // burstSize가 0/1이면 resting이 영원히 false라 기존의 균일 간격 스폰과 완전히 동일하게 동작한다.
        int burstSize = stage != null ? stage.burstSize : 0;
        bool resting = burstSize > 1 && spawnedInBurst >= burstSize;
        float wait = resting ? stage.burstRest : interval;

        timer += Time.deltaTime;
        if (timer < wait) return;

        timer = 0f;
        if (resting) spawnedInBurst = 0; // 휴식 끝 — 다음 무리 시작

        // 벽 스테이지 진화 엘리트: 확률이 아니라 확정으로, 스테이지 물량을 균등 분할한 지점마다 1마리씩.
        // (2마리면 33%·66% 지점) 이 엘리트만 진화 아이템을 떨군다.
        int evolutionDrops = stage != null ? stage.evolutionItemDrops : 0;
        // 보상 있는 보스 판은 진화 아이템 1개를 보장한다(테이블에 이미 있으면 그대로) — 판 중반 지점에 나온다.
        if (bossStageWithRewards) evolutionDrops = Mathf.Max(1, evolutionDrops);
        if (evolutionDrops > 0 && evolutionElitesSpawnedThisStage < evolutionDrops && map.eliteEnemyPrefab != null)
        {
            int threshold = SpawnTarget * (evolutionElitesSpawnedThisStage + 1) / (evolutionDrops + 1);
            if (SpawnedThisStage >= threshold)
            {
                evolutionElitesSpawnedThisStage++;
                // 진화는 보물상자로 통합됐다(세션25) — 엘리트는 벽 스테이지의 난이도 요소로 그대로 남기고 아이템은 안 떨군다.
                // ⚠️ 필드명(`evolutionItemDrops`)만 낡았다. 0으로 만들면 벽 스테이지 엘리트까지 사라진다.
                SpawnEnemies(map.eliteEnemyPrefab, 1, stage, currentStage);
                return;
            }
        }

        // 중간 소환: 진화 엘리트와 같은 균등 분할 지점마다 1회. 예고 마커를 띄우고 그 시간 동안 정문 스폰은 멈춘다.
        int ambushes = stage != null ? stage.ambushCount : 0;
        if (ambushes > 0 && ambushesTriggeredThisStage < ambushes)
        {
            int threshold = SpawnTarget * (ambushesTriggeredThisStage + 1) / (ambushes + 1);
            if (SpawnedThisStage >= threshold)
            {
                ambushesTriggeredThisStage++;
                TriggerAmbush(stage, currentStage, reservedTail);
                return;
            }
        }

        GameObject prefabToSpawn = map.enemyPrefab;
        bool spawnAsBoss = false;
        // 보스 블루베리: 보스 스테이지의 마지막 물량으로 1회 등장(그 뒤 잔몹 + 분출 블루베리까지 잡아야 클리어).
        // 🔴 상자까지 **먹고 난 빈 화면**에서 등장한다(2026-09-20 사용자). 안 그러면 상자와 보스가 같은 순간에 떠서
        //    "상자 먼저 = 보스 직전 파워 스파이크"라는 이 구간의 의도가 사라진다.
        if (isBossStage && !bossSpawnedThisStage && SpawnedThisStage >= SpawnTarget - 1 && Enemy.Active.Count == 0)
        {
            // 승천 티어별 전용 보스. 꽂혀 있으면 그걸 쓰고, 비어 있으면 bossEnemyPrefab으로 떨어진다
            // (= 안 꽂은 맵은 종전과 완전히 같다. MapDefinition 주석 참고).
            prefabToSpawn = map.bossEnemyPrefab;
            if (RunConfig.AscensionLevel <= 1)
            {
                if (map.bossEnemyPrefabEasy != null) prefabToSpawn = map.bossEnemyPrefabEasy;
            }
            else if (RunConfig.AscensionLevel >= 3)
            {
                if (map.bossEnemyPrefabHard != null) prefabToSpawn = map.bossEnemyPrefabHard;
            }
            bossSpawnedThisStage = true;
            spawnAsBoss = true;
        }
        // 🔴 **적어 놓은 값이 곧 실제 비율이다**(2026-09-20 사용자). 주사위를 한 번만 굴려 누적 가중치로 고른다.
        //    예전엔 종류마다 따로 굴려 걸리면 멈추는 else-if 사슬이라, 뒤쪽 종류가 "앞에서 아무도 안 걸릴 확률"만큼
        //    깎였다 — 해변 16층에서 콩콩이 0.40이 실제 12.5%, 서핑 0.42가 7.9%로 나오던 이유다.
        //    합이 1 미만이면 남는 몫은 기본 블루베리, 1을 넘으면 비율을 유지한 채 정규화한다.
        else
        {
            prefabToSpawn = PickByWeight(map, stage,
                (map.treasureEnemyPrefab != null ? ExtraTreasureChance : 0f),
                eliteChance, paperPlaneChance, ufoChance, shieldChance,
                riderChance, hopperChance, surferChance, airshipChance) ?? prefabToSpawn;
        }

        SpawnEnemies(prefabToSpawn, 1, stage, currentStage, spawnAsBoss);
    }

    // 화면 안 빈 구간에 예고 마커를 띄운다. 실제 부대는 마커가 다 찬 뒤 콜백에서 나온다.
    // ambushSquads가 2 이상이면 마커를 그 수만큼 **한꺼번에** 띄운다 — 예고 시간은 한 번치(2.5초)만 쓰면서
    // 게릴라 물량은 배로 는다. ambushCount를 올려 늘어지게 만드는 것과 여기가 갈린다.
    private void TriggerAmbush(StageData stage, int currentStage, int reservedTail)
    {
        // 물량 꼬리의 reservedTail칸은 보물상자·보스 몫이라 절대 침범하면 안 된다
        // (일반 판 1칸 = 상자 / 보상 있는 보스 판 2칸 = 상자+보스 / 최종 보스 판 1칸 = 보스).
        // 여길 넘기면 StageSpawnComplete가 먼저 참이 되어 보물상자/보스가 영영 안 나온다.
        // ⚠️ 부대들이 동시에 예약되는데 SpawnedThisStage는 콜백에서야 오르므로, 남은 칸은 여기서 직접 깎아 나간다.
        int room = SpawnTarget - reservedTail - SpawnedThisStage;
        if (room <= 0) return;

        SfxPlayer.Play(SfxId.WaveWarning); // 마커가 뜨는 순간 — 예고 2.5초 동안 정문 스폰이 멈추므로 시선을 끌 곳이다

        // 소환 구간은 절대 좌표라 맵 필드 배율만큼 같이 벌려야 한다(넓은 맵에서 화면 왼쪽에만 몰리지 않게).
        // 부대원이 흩어지는 폭(AmbushSquadSpreadX)은 적 크기 기준이라 안 곱한다.
        float fieldScale = Map != null ? Map.fieldScale : 1f;
        float bandMin = BalanceConstants.AmbushBandMinX;   // 🔴 fieldScale을 곱하지 않는다(2026-09-21) — 곱하면 우주에서 -12.6이 되어 정문(-9)보다 뒤에 매복이 생긴다. MaxX도 같은 이유로 안 곱한다
        // 🔴 상한은 **플레이어 기준**이라 맵 배율을 곱하지 않는다(2026-09-19 사용자).
        // 곱하면 넓은 맵일수록 박치기선에 붙는다 — 우주(fieldScale 1.8)에서 4.14까지 와서
        // 박치기선(5.36)과 1.22유닛밖에 안 떨어졌다(이 상수 주석이 요구하는 최소 1.5 위반).
        float bandMax = BalanceConstants.AmbushBandMaxX;

        // 인스펙터에서 0이 들어와도 한 부대는 나오게 막아 둔다(필드가 없던 옛 에셋은 초기값 1로 읽히므로 무관).
        int squads = Mathf.Max(1, stage != null ? stage.ambushSquads : 1);
        float slot = (bandMax - bandMin) / squads; // 부대끼리 겹치지 않게 소환 구간을 등분해 한 칸씩 맡긴다

        // 중간 소환 부대 자체가 하나의 무리다 — 쏟은 직후 곧바로 휴식이 걸리게 무리 정원을 채워 둔다.
        if (stage != null && stage.burstSize > 1) spawnedInBurst = stage.burstSize;

        for (int i = 0; i < squads && room > 0; i++)
        {
            int squad = Mathf.Min(Random.Range(BalanceConstants.AmbushSquadMin, BalanceConstants.AmbushSquadMax + 1), room);
            if (squad <= 0) break;
            room -= squad;

            Vector3 center = new Vector3(
                Random.Range(bandMin + slot * i, bandMin + slot * (i + 1)),
                transform.position.y, // 스포너 y = 레인 기준선
                0f);

            AmbushMarker marker = null;
            marker = AmbushMarker.Spawn(center, BalanceConstants.AmbushWarnDuration, () =>
            {
                pendingAmbushes.Remove(marker);
                SpawnAmbushSquad(center, squad, stage, currentStage);
            });
            if (marker != null) pendingAmbushes.Add(marker);
        }
    }

    // 마커 자리에서 부대가 팝콘처럼 튀어올랐다 레인에 착지한다. 튀는 동안은 Enemy가 무적이라
    // 소환 순간 이미 깔려 있던 광역기에 "안 나온 것처럼" 즉사하지 않는다.
    private void SpawnAmbushSquad(Vector3 center, int count, StageData stage, int currentStage)
    {
        MapDefinition map = Map;
        float laneBaselineY = transform.position.y;
        for (int i = 0; i < count; i++)
        {
            GameObject prefab = PickAmbushPrefab(stage, map);
            if (prefab == null) continue;
            Vector3 pos = center + Vector3.right * Random.Range(-BalanceConstants.AmbushSquadSpreadX, BalanceConstants.AmbushSquadSpreadX);
            Enemy enemy = Enemy.Spawn(prefab, pos);
            if (enemy == null) continue;
            ApplyStageScaling(enemy, stage, currentStage);
            enemy.PopIn(Random.Range(4f, 7f), Random.Range(-1.5f, 1.5f), laneBaselineY + enemy.SpawnYOffset);
        }
        SpawnedThisStage += count;
    }

    // 주사위 한 번으로 종류를 고른다 — 적어 놓은 확률이 그대로 실제 비율이 된다.
    // 합이 1보다 작으면 남는 몫이 기본 블루베리, 1보다 크면 비율을 유지한 채 정규화한다.
    // ⚠️ 프리팹이 안 배선된 종류(우주의 서핑 등)는 가중치에서 아예 빠진다 — 그 몫이 다른 종류로 가지 않고
    //    기본 블루베리로 간다(예전 사슬도 같은 동작이었다).
    private GameObject PickByWeight(MapDefinition map, StageData stage,
        float treasure, float elite, float paper, float ufo, float shield,
        float rider, float hopper, float surfer, float airship)
    {
        var prefabs = new GameObject[] {
            map.treasureEnemyPrefab, map.eliteEnemyPrefab, map.paperPlaneEnemyPrefab, map.ufoEnemyPrefab,
            map.shieldEnemyPrefab, map.riderEnemyPrefab, map.hopperEnemyPrefab, map.surferEnemyPrefab, map.airshipEnemyPrefab };
        var weights = new float[] { treasure, elite, paper, ufo, shield, rider, hopper, surfer, airship };

        float total = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            if (prefabs[i] == null || weights[i] <= 0f) { weights[i] = 0f; continue; }
            total += weights[i];
        }
        if (total <= 0f) return null;                       // 전부 0 → 호출부가 기본 블루베리를 쓴다

        // 합이 1을 넘으면 정규화한다(비율은 그대로, 기본 블루베리 몫은 0).
        float span = Mathf.Max(1f, total);
        float roll = Random.value * span;
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0f) continue;
            roll -= weights[i];
            if (roll < 0f) return prefabs[i];
        }
        return null;                                        // 남은 몫 = 기본 블루베리
    }

    // 중간 소환은 그 스테이지의 **지상** 로스터를 다시 굴린다.
    // 제외: 종이비행기(상공 강하)·UFO(캐리어 하강)·서핑(바다에서 강하)은 자기 전용 등장 연출이 있어 땅에서 튀어나오면 안 된다.
    //       **방패·라이더**는 대응 시간이 0이 되어 빠졌다(2026-09-20 사용자) — 아래 주석.
    // 남은 후보: 엘리트 · 콩콩이 · 기본 블루베리.
    private GameObject PickAmbushPrefab(StageData stage, MapDefinition map)
    {
        if (stage != null)
        {
            if (map.eliteEnemyPrefab != null && Random.value < stage.eliteChance) return map.eliteEnemyPrefab;
            // 🔴 방패·라이더는 중간 소환에서 제외한다(2026-09-20 사용자).
            //    방패는 코앞에서 튀어나오면 관통 없이는 손도 못 대고, 라이더는 이속 4.5라
            //    등장하자마자 덮친다 — 둘 다 "예고 2.5초 뒤 눈앞에서 튀어나온다"는 매복 연출과 겹쳐 대응 시간이 0이 된다.
            //    정문으로 걸어올 때는 그대로 나온다(stage.shieldChance·riderChance는 그쪽에서만 쓰인다).
            if (map.hopperEnemyPrefab != null && Random.value < stage.hopperChance) return map.hopperEnemyPrefab;
        }
        return map.enemyPrefab;
    }

    private void ApplyStageScaling(Enemy enemy, StageData stage, int currentStage)
    {
        if (stage == null) return;
        int step = currentStage / 3;
        AscensionTier asc = Ascension.Get(RunConfig.AscensionLevel); // 승천 등급 배율(체력·이속·데미지)
        float hpMult = stage.enemyHpMultiplier * (1f + Scaling.HpStepBonusAt(step)) * asc.hpMult;
        float speedMult = stage.enemySpeedMultiplier * (1f + Scaling.SpeedStepBonusAt(step)) * asc.speedMult;
        enemy.ApplyStageMultipliers(hpMult, speedMult, stage.enemyDamageMultiplier * asc.damageMult);
    }

    private void SpawnEnemies(GameObject prefab, int count, StageData stage, int currentStage, bool isBoss = false)
    {
        for (int i = 0; i < count; i++)
        {
            // 2마리 이상 동시 스폰 시 겹치지 않게 뒤쪽(왼쪽)으로 살짝 벌려 단일 대열 유지
            Vector3 spawnPos = transform.position + Vector3.left * (1.2f * i);
            Enemy enemy = Enemy.Spawn(prefab, spawnPos);
            if (enemy != null)
            {
                if (isBoss) enemy.MarkAsBoss(); // 군중제어 감쇄 대상 — 프리팹이 아니라 이 슬롯으로 스폰됐는지가 기준
                ApplyStageScaling(enemy, stage, currentStage);
            }
        }

        SpawnedThisStage += count;
        spawnedInBurst += count; // 웨이브 회계 — 이 한 곳만 지나가면 정문 스폰 전부가 무리에 잡힌다
    }
}
