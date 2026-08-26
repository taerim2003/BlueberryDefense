using UnityEngine;

// 짧은 스프라이트 시퀀스(예: 4프레임)를 재생하는 경량 VFX 컴포넌트. ObjectPool로 스폰/디스폰되며,
// 활성화될 때마다 첫 프레임부터 재생한다. 비루프면 마지막 프레임에서 정지(디스폰은 호출측 타이머가 담당).
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFlipbook : MonoBehaviour
{
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float fps = 16f;
    [SerializeField] private bool loop = false;
    [SerializeField] private bool despawnOnFinish = true; // 비루프 재생이 끝나면 풀로 자동 반환 — 마지막 프레임이 얼어붙어 남는 잔상 방지

    private SpriteRenderer sr;
    private float timer;
    private bool finished;

    private void Awake() => sr = GetComponent<SpriteRenderer>();

    private void OnEnable()
    {
        timer = 0f;
        finished = false;
        if (frames != null && frames.Length > 0) sr.sprite = frames[0];
    }

    // 런타임에 프레임을 갈아끼우고 처음부터 재생한다.
    // 프리팹에 미리 박아둘 수 없는 경우(진화한 화살처럼 진화 티어에 따라 그림이 갈리는 것)에 쓴다.
    // ⚠️ loop=true로 부르면 `despawnOnFinish`는 영영 안 걸린다 — 풀이 아닌 Instantiate 오브젝트엔 그쪽이 맞다.
    public void Play(Sprite[] newFrames, float newFps, bool newLoop)
    {
        frames = newFrames;
        fps = newFps;
        loop = newLoop;
        timer = 0f;
        finished = false;
        if (sr == null) sr = GetComponent<SpriteRenderer>(); // AddComponent 직후엔 Awake가 아직 안 돌았을 수 있다
        if (frames != null && frames.Length > 0) sr.sprite = frames[0];
    }

    private void Update()
    {
        if (frames == null || frames.Length == 0) return;
        timer += Time.deltaTime;
        int frame = Mathf.FloorToInt(timer * fps);

        if (loop)
        {
            sr.sprite = frames[frame % frames.Length];
            return;
        }

        // 비루프: 마지막 프레임까지 재생한 뒤(= timer*fps가 프레임 수를 넘어서면) 자동 소멸.
        if (frame >= frames.Length)
        {
            if (!finished)
            {
                finished = true;
                if (despawnOnFinish) ObjectPool.Instance.Despawn(gameObject);
            }
            return;
        }
        sr.sprite = frames[frame];
    }
}
