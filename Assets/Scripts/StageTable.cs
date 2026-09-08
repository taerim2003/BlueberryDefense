using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class StageData
{
    public int stageNumber = 1;
    public int spawnCount = 20;   // 이 스테이지에 스폰할 총 적 수(물량 기반 클리어). 쿼터 소진 + 잔몹 0 → 클리어
    public float duration = 45f;  // (레거시) 물량 기반 전환으로 미사용 — 참고용으로만 남김
    // 적 1마리 사이의 간격. burstSize가 2 이상이면 **무리 안에서의 간격**이 된다(무리끼리의 간격은 burstRest).
    public float spawnInterval = 1.5f;

    // ── 웨이브(무리) 스폰 ──
    // BTD6식 리듬. burstSize 마리를 spawnInterval 간격으로 연달아 쏟은 뒤 burstRest만큼 정문 스폰을 멈춘다.
    // 0 또는 1이면 기존 방식(균일 간격으로 꾸준히) 그대로 — 스테이지마다 성격을 갈라 쓰라고 데이터로 뒀다.
    // ⚠️ 무리로 만들 땐 spawnInterval을 확 낮춰야(0.06~0.15) "좌라락" 쏟아진다. 안 낮추면 그냥 느린 스폰에 쉼표만 찍힌 꼴.
    public int burstSize = 0;
    public float burstRest = 0f;

    public float eliteChance = 0f;
    // 벽 스테이지(능력시험) 표식. 0보다 크면 이 스테이지에 엘리트가 **확정으로** 그 수만큼 나온다.
    // ⚠️ 이름과 달리 지금은 아이템을 안 떨군다 — 진화 경로가 보물상자로 통합됐다(세션25). 남은 역할은 "확정 엘리트 수".
    public int evolutionItemDrops = 0;
    // 중간 소환 횟수(0=없음). 진화 엘리트와 같은 방식으로 스테이지 물량을 균등 분할한 지점마다 1회씩,
    // 화면 안(BalanceConstants.AmbushBand*)에 예고 마커를 띄운 뒤 부대를 꽂는다. 부대원 수도 물량 쿼터에 포함된다.
    public int ambushCount = 0;
    // 중간 소환 1회에 **동시에** 튀어나오는 부대 수(1 = 기존처럼 한 부대씩).
    // ambushCount를 올리면 예고(2.5초)마다 정문 스폰이 멈춰 판이 늘어지는데, 이쪽은 예고 한 번에 여러 부대를
    // 한꺼번에 쏟으므로 늘어짐 없이 게릴라 물량만 는다. 소환 구간을 등분해 부대끼리 안 겹치게 배치한다.
    // 이 필드가 없던 기존 에셋(StageTable_Coast 등)은 여기 초기값 1로 읽힌다 — 확인함(세션25). 즉 현행 동작 그대로다.
    // 다만 인스펙터에서 0을 넣을 수 있으므로 읽는 쪽은 Max(1, ...)로 받는다.
    public int ambushSquads = 1;
    public float paperPlaneChance = 0f;
    public float ufoChance = 0f;
    public float shieldChance = 0f; // 방패 블루베리(관통·오브 차단) 스폰 확률
    public float riderChance = 0f;  // 라이더 블루베리(지상 고속 돌진·저HP) 스폰 확률
    public float hopperChance = 0f; // 콩콩이 블루베리(지상을 높이 뛰며 전진 — 공중에 뜬 동안 지상 히트박스를 피함) 스폰 확률
    public float surferChance = 0f; // 서핑 블루베리(라이더보다 빠른 최고속·최저HP — 무리로 몰려나오라고 만든 적) 스폰 확률
    public float airshipChance = 0f; // 해적 비행선(느리고 체력 많은 공중 엘리트 — 격추되면 선원이 쏟아진다) 스폰 확률
    public float enemyHpMultiplier = 1f;
    public float enemySpeedMultiplier = 1f;
    public float enemyDamageMultiplier = 1f;
}

[CreateAssetMenu(fileName = "StageTable", menuName = "BlueberryDefense/Stage Table")]
public class StageTable : ScriptableObject
{
    public StageData[] stages;

    // ── 저작 범위 밖 자동 생성 (지금은 안전망) ──
    // 승천 최대치가 25스테이지(AscensionTable)인데 저작 엔트리가 25개라 **정상 플레이에선 여기 안 온다.**
    // 승천을 4단계 이상으로 늘리거나 저작 엔트리를 지우면 그때부터 이 경로가 산다.
    // 그 경우 마지막 저작 엔트리에서 출발해 스테이지당 아래 비율로 계속 키운다.
    // ⚠️ 여기서 생성된 스테이지는 벽 스테이지가 아니라 전부 마지막 엔트리의 성격을 복사한다.
    // ⚠️ 예전엔 GetStage가 범위 밖을 마지막 엔트리로 **클램프**해서 난이도가 15스테이지에 눌러앉았다.
    // ⚠️ ScalingTable.hpStepBonus는 7칸(=18스테이지)에서 클램프되므로, 그 뒤 난이도는 전적으로 여기가 끈다.
    // 🔴 현재 성장률은 **의도적으로 가파르다**(사용자 결정, 세션17). 승천3 최종이 기준(승천1 15스테이지)의 약 11.8배.
    //    되돌리지 말 것 — 버그가 아니라 "일단 세게 잡고 플레이로 깎는다"는 선택이다.
    //    ⚠️ 참고: 만렙 10 + 진화 2회로 플레이어 파워엔 상한이 있어서, 판이 길어져도 플레이어는 그 이상 안 세진다
    //    (늘어나는 건 스킬트리뿐). 못 깨겠으면 제일 먼저 내릴 손잡이가 여기다. 완만안은 1.08(=승천3 2.16배).
    [Header("저작 범위 밖 자동 생성 (승천으로 길어진 판)")]
    public float extendedHpGrowth = 1.28f;      // 스테이지당 체력 배율 성장
    public float extendedDamageGrowth = 1.03f;
    public float extendedSpeedGrowth = 1.01f;
    public int extendedSpawnCountStep = 4;      // 스테이지당 물량 증가
    public float extendedSpawnIntervalMin = 0.16f;
    public int extendedEvolutionItemEvery = 4;  // 확장 구간에서 이 간격마다 진화 아이템 1개(보스 스테이지와 안 겹치게 4)

    // 생성 결과 캐시 — GetStage는 EnemySpawner.Update에서 매 프레임 불린다. 매번 new 하면 GC가 갈린다.
    private Dictionary<int, StageData> extendedCache;

    private void OnEnable() => extendedCache = null;
    private void OnValidate() => extendedCache = null; // 인스펙터에서 성장률을 만지면 즉시 반영

    public StageData GetStage(int stageNumber)
    {
        if (stages == null || stages.Length == 0) return null;
        if (stageNumber <= stages.Length) return stages[Mathf.Max(0, stageNumber - 1)];
        return GetExtended(stageNumber);
    }

    private StageData GetExtended(int stageNumber)
    {
        if (extendedCache == null) extendedCache = new Dictionary<int, StageData>();
        if (extendedCache.TryGetValue(stageNumber, out StageData cached)) return cached;

        StageData last = stages[stages.Length - 1];
        int n = stageNumber - stages.Length; // 저작 범위를 몇 칸 넘었나(1부터)
        StageData s = new StageData
        {
            stageNumber = stageNumber,
            spawnCount = last.spawnCount + extendedSpawnCountStep * n,
            duration = last.duration,
            spawnInterval = Mathf.Max(extendedSpawnIntervalMin, last.spawnInterval),
            burstSize = last.burstSize,
            burstRest = last.burstRest,
            eliteChance = last.eliteChance,
            // 판이 길어진 만큼 확정 엘리트도 늘어난다(승천1=+1개, 2=+2, 3=+3).
            evolutionItemDrops = (extendedEvolutionItemEvery > 0 && n % extendedEvolutionItemEvery == 0) ? 1 : 0,
            ambushCount = last.ambushCount,
            ambushSquads = last.ambushSquads,
            paperPlaneChance = last.paperPlaneChance,
            ufoChance = last.ufoChance,
            shieldChance = last.shieldChance,
            riderChance = last.riderChance,
            hopperChance = last.hopperChance,
            surferChance = last.surferChance,
            enemyHpMultiplier = last.enemyHpMultiplier * Mathf.Pow(extendedHpGrowth, n),
            enemySpeedMultiplier = last.enemySpeedMultiplier * Mathf.Pow(extendedSpeedGrowth, n),
            enemyDamageMultiplier = last.enemyDamageMultiplier * Mathf.Pow(extendedDamageGrowth, n),
        };
        extendedCache[stageNumber] = s;
        return s;
    }
}
