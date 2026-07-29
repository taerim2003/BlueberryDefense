using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class StageData
{
    public int stageNumber = 1;
    public int spawnCount = 20;   // 이 스테이지에 스폰할 총 적 수(물량 기반 클리어). 쿼터 소진 + 잔몹 0 → 클리어
    public float duration = 45f;  // (레거시) 물량 기반 전환으로 미사용 — 참고용으로만 남김
    public float spawnInterval = 1.5f;
    public float eliteChance = 0f;
    // 벽 스테이지(능력시험) 표식. 0보다 크면 이 스테이지에 엘리트가 **확정으로** 그 수만큼 나오고,
    // 그 엘리트를 잡으면 진화 아이템을 떨군다. 진화는 이 아이템으로만 열린다(§EvolutionRoutes).
    public int evolutionItemDrops = 0;
    // 중간 소환 횟수(0=없음). 진화 엘리트와 같은 방식으로 스테이지 물량을 균등 분할한 지점마다 1회씩,
    // 화면 안(BalanceConstants.AmbushBand*)에 예고 마커를 띄운 뒤 부대를 꽂는다. 부대원 수도 물량 쿼터에 포함된다.
    public int ambushCount = 0;
    public float paperPlaneChance = 0f;
    public float ufoChance = 0f;
    public float shieldChance = 0f; // 방패 블루베리(관통·오브 차단) 스폰 확률
    public float riderChance = 0f;  // 라이더 블루베리(지상 고속 돌진·저HP) 스폰 확률
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
            eliteChance = last.eliteChance,
            // 판이 길어진 만큼 진화 기회도 늘어난다(승천1=+1개, 2=+2, 3=+3).
            evolutionItemDrops = (extendedEvolutionItemEvery > 0 && n % extendedEvolutionItemEvery == 0) ? 1 : 0,
            ambushCount = last.ambushCount,
            paperPlaneChance = last.paperPlaneChance,
            ufoChance = last.ufoChance,
            shieldChance = last.shieldChance,
            riderChance = last.riderChance,
            enemyHpMultiplier = last.enemyHpMultiplier * Mathf.Pow(extendedHpGrowth, n),
            enemySpeedMultiplier = last.enemySpeedMultiplier * Mathf.Pow(extendedSpeedGrowth, n),
            enemyDamageMultiplier = last.enemyDamageMultiplier * Mathf.Pow(extendedDamageGrowth, n),
        };
        extendedCache[stageNumber] = s;
        return s;
    }
}
