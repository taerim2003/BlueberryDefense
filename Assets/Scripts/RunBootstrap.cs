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
        ApplyCharacterVisuals();
        ApplyMap();
    }

    // 캐릭터 외형만 여기서 적용(스탯=시작스킬/체력은 플레이어 컴포넌트가 자기 Awake에서 직접 읽음).
    // 스프라이트/애니메이터가 null이면 스킵 → 프리팹 기본 유지(기본 캐릭터=현행과 동일).
    private void ApplyCharacterVisuals()
    {
        CharacterDefinition character = RunConfig.Character;
        if (character == null) return;

        PlayerSkills player = FindAnyObjectByType<PlayerSkills>();
        if (player == null) return;

        if (character.sprite != null)
        {
            SpriteRenderer sr = player.GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sr.sprite = character.sprite;
        }
        if (character.animatorController != null)
        {
            Animator anim = player.GetComponentInChildren<Animator>();
            if (anim != null) anim.runtimeAnimatorController = character.animatorController;
        }
    }

    private void ApplyMap()
    {
        MapDefinition map = RunConfig.Map != null ? RunConfig.Map : defaultMap;
        if (map == null) return;

        // 스테이지 테이블 (기본맵이면 GameManager에 이미 물린 것과 동일)
        GameManager gm = FindAnyObjectByType<GameManager>();
        if (gm != null && map.stageTable != null) gm.SetStageTable(map.stageTable);

        // 적 로스터·스폰 파라미터 (EnemySpawner.Start 전에 세팅)
        EnemySpawner spawner = FindAnyObjectByType<EnemySpawner>();
        if (spawner != null) spawner.ActiveMap = map;

        ApplyFieldScale(map.fieldScale, spawner);

        // 배경
        GameObject bg = GameObject.Find("Background");
        if (bg != null && map.background != null)
        {
            SpriteRenderer sr = bg.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sprite = map.background;
        }

        // 카메라 들어올리기 = 화면 안에서 레인 내리기. 배경은 카메라의 자식이 아니라서 **같이** 옮겨야
        // 화면을 계속 정확히 덮는다(둘 다 y=0 중심이라는 전제로 fieldScale이 계산돼 있다).
        if (!Mathf.Approximately(map.cameraYLift, 0f))
        {
            Camera cam = Camera.main;
            if (cam != null) cam.transform.position += Vector3.up * map.cameraYLift;
            if (bg != null) bg.transform.position += Vector3.up * map.cameraYLift;
        }

        // 배경 스프라이트는 자기 px/PPU만큼 커지므로(480×270@PPU18 = 26.67×15유닛) 별도 스케일링이 필요 없다.

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

    // 필드 확장 = 카메라 줌아웃 + 절대 좌표를 같은 비율로 벌리기. 캐릭터/적 스케일은 손대지 않는다.
    // 카메라 기준으로 계산되는 것들(UFO 등장·호버 고도, 종이비행기 강하 시작 높이, 의성어 클램프)은 자동으로 따라온다.
    // 중간 소환 구간(BalanceConstants.AmbushBand*)만 절대 좌표라 EnemySpawner가 따로 곱한다.
    private void ApplyFieldScale(float scale, EnemySpawner spawner)
    {
        if (scale <= 0f || Mathf.Approximately(scale, 1f)) return;

        Camera cam = Camera.main;
        if (cam != null && cam.orthographic) cam.orthographicSize *= scale;

        PlayerSkills player = FindAnyObjectByType<PlayerSkills>();
        if (player != null) Widen(player.transform, scale);
        if (spawner != null) Widen(spawner.transform, scale);
    }

    // x·y만 벌린다(z는 정렬용이라 유지).
    private static void Widen(Transform t, float scale)
    {
        Vector3 p = t.position;
        t.position = new Vector3(p.x * scale, p.y * scale, p.z);
    }
}
