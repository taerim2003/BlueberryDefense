using UnityEngine;

// 판 시작 시 선택된 맵(RunConfig.Map, 없으면 직렬화된 defaultMap)을 씬에 적용한다.
// MetaRunApplier의 형제로 GameManager 오브젝트에 붙는다. Awake에서 적용 —
// EnemySpawner.Start(물량 세팅)·GameManager.Update 전에 스테이지테이블/로스터가 준비돼야 하므로.
public class RunBootstrap : MonoBehaviour
{
    [SerializeField] private MapDefinition defaultMap; // RunConfig.Map이 null일 때(씬 단독 실행) 사용

    private AudioSource bgmSource;
    private AudioLowPassFilter bgmLowPass;
    private float muffleBlend; // 0 = 평소, 1 = 클리어/게임오버 화면에서 "옆방" 소리

    private void Awake()
    {
        RunConfig.HasPlayedThisSession = true; // 타이틀로 돌아갔을 때 TitleBgm이 복귀곡을 틀 근거
        ApplyCharacterVisuals();
        ApplyMap();
        TutorialHint.TryShow();
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
        TrimCameraForShake();

        // 배경 — 컷이 2장 이상이면 돌리고(BackgroundAnimator가 첫 컷을 바로 깐다), 아니면 기존처럼 정지 한 장.
        GameObject bg = GameObject.Find("Background");
        if (bg != null)
        {
            SpriteRenderer sr = bg.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                if (map.backgroundFrames != null && map.backgroundFrames.Length > 1)
                    bg.AddComponent<BackgroundAnimator>().Init(map.backgroundFrames, map.backgroundFrameSeconds);
                else if (map.background != null)
                    sr.sprite = map.background;
            }
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
            VolumeSettings.RegisterBgm(bgmSource); // 옵션의 배경음 볼륨을 재생 전에 반영

            // 클리어/게임오버 화면에서 곡을 뒤로 물리기 위한 필터. 판이 끝나도 곡은 계속 돌고
            // 씬 전환도 없으므로, 새 소스를 만들 필요 없이 이 하나에 필터만 걸면 된다.
            bgmLowPass = gameObject.AddComponent<AudioLowPassFilter>();
            bgmLowPass.cutoffFrequency = BgmMuffle.OpenCutoff;

            bgmSource.Play();
        }
    }

    // 엔딩 연출이 곡을 끄고(보스 처치 직후) 다시 켠다("Game Cleared!"). 다시 켤 땐 처음부터.
    public void SetBgmPlaying(bool playing)
    {
        if (bgmSource == null) return;
        if (playing) bgmSource.Play();
        else bgmSource.Stop();
    }

    // 판이 끝나면(승패 무관) BGM을 "옆방에서 들리는" 소리로 물린다 — 타이틀 화면의 패널과 같은 처리.
    // ⚠️ 이 시점엔 Time.timeScale이 0이므로 BgmMuffle이 unscaledDeltaTime으로 보간한다.
    private void Update()
    {
        if (bgmSource == null) return;

        GameManager gm = GameManager.Instance;
        bool ended = gm != null && (gm.IsGameOver || gm.IsGameClear);

        muffleBlend = BgmMuffle.Advance(muffleBlend, ended);
        BgmMuffle.Apply(bgmSource, bgmLowPass, muffleBlend);
    }

    // 🔴 화면을 배경보다 **조금 좁게** 잡는다(2026-09-27 사용자: 휘두르기 셰이크 때 배경 밖이 보인다).
    //    배경 그림이 화면과 **정확히 같은 크기**(오차 0)라서 — 농장 400x225 · 해변 480x270 · 우주 576x324 @PPU18 —
    //    ScreenShake가 x·y로 최대 0.12유닛 흔들면 그만큼 사방이 빈다.
    // 🔴 **세로가 빡빡한 쪽이다**(ortho가 세로를 고정하므로). 실측:
    //    농장 ortho 6.25 · 배경 반높이 6.25 → 2%면 여유 0.125로 흔들림 0.12와 거의 같다(슬랙 0.005).
    //    3%면 여유가 농장 0.19 · 해변 0.23 · 우주 0.27로 넉넉해진다 — 그래서 0.97이다.
    // ⚠️ ortho만 줄인다. Widen(플레이어·스포너 좌표)에 이 값을 곱하면 안 된다 — 레인 위치가 틀어진다.
    // ⚠️ PPU 정수 배율이 깨져 도트가 약간 뭉갤 수 있다. 정석은 배경을 조금 크게 다시 그리는 것이다.
    private const float ShakeTrim = 0.97f;
    private static void TrimCameraForShake()
    {
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic) cam.orthographicSize *= ShakeTrim;
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
