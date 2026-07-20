using UnityEngine;

// 판 시작 시 선택된 맵(RunConfig.Map, 없으면 직렬화된 defaultMap)을 씬에 적용한다.
// MetaRunApplier의 형제로 GameManager 오브젝트에 붙는다. Awake에서 적용 —
// EnemySpawner.Start(물량 세팅)·GameManager.Update 전에 스테이지테이블/로스터가 준비돼야 하므로.
public class RunBootstrap : MonoBehaviour
{
    [SerializeField] private MapDefinition defaultMap; // RunConfig.Map이 null일 때(씬 단독 실행) 사용

    private AudioSource bgmSource;

    private void Awake()
    {
        MapDefinition map = RunConfig.Map != null ? RunConfig.Map : defaultMap;
        if (map == null) return;

        // 스테이지 테이블 (기본맵이면 GameManager에 이미 물린 것과 동일)
        GameManager gm = FindAnyObjectByType<GameManager>();
        if (gm != null && map.stageTable != null) gm.SetStageTable(map.stageTable);

        // 적 로스터·스폰 파라미터 (EnemySpawner.Start 전에 세팅)
        EnemySpawner spawner = FindAnyObjectByType<EnemySpawner>();
        if (spawner != null) spawner.ActiveMap = map;

        // 배경
        GameObject bg = GameObject.Find("Background");
        if (bg != null && map.background != null)
        {
            SpriteRenderer sr = bg.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sprite = map.background;
        }

        // BGM (최소 재생 — clip 없으면 무음으로 현재와 동일)
        if (map.bgm != null)
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.clip = map.bgm;
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            bgmSource.Play();
        }
    }
}
