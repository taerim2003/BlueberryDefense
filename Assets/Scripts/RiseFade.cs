using UnityEngine;

// 머리 위에 뜨는 표식(되감기 표식·뇌운)의 등·퇴장 연출.
// 아래에서 쓱 올라오며 나타나고, 사라질 때도 위로 쓱 빠지며 흐려진다.
//
// 위치는 transform이 아니라 Anchor를 기준으로 잡는다 — 뇌운처럼 플레이어를 매 프레임 따라다녀야 하는 쪽은
// 외부(StormCloudAura)가 Anchor만 갱신하면 되고, 떠오르는 오프셋은 이 컴포넌트가 단독으로 소유한다.
// (둘 다 transform.position에 쓰면 서로 덮어써서 연출이 사라진다.)
[RequireComponent(typeof(SpriteRenderer))]
public class RiseFade : MonoBehaviour
{
    [SerializeField] private float riseInDistance = 0.35f;  // 등장할 때 아래에서 올라오는 거리
    [SerializeField] private float riseInDuration = 0.12f;
    [SerializeField] private float riseOutDistance = 0.55f; // 퇴장할 때 위로 빠지는 거리
    [SerializeField] private float riseOutDuration = 0.22f;
    [SerializeField] private float lifetime = 0.3f;         // autoExit일 때 등장~퇴장 시작까지
    [SerializeField] private bool autoExit = true;          // 뇌운처럼 계속 떠 있어야 하면 false

    public Vector3 Anchor { get; set; }

    private SpriteRenderer sr;
    private float timer;
    private float exitTimer;
    private bool exiting;

    private void Awake() => sr = GetComponent<SpriteRenderer>();

    // ObjectPool 재사용은 Awake가 다시 돌지 않는다 — 스폰마다 초기화가 필요한 값은 전부 여기서 되돌린다.
    private void OnEnable()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        Anchor = transform.position;
        timer = 0f;
        exitTimer = 0f;
        exiting = false;
        SetAlpha(0f);
    }

    // 외부에서 "이제 사라져라"를 알린다(뇌운은 낙뢰 스택이 줄 때). 퇴장이 끝나면 스스로 풀에 반납한다.
    public void BeginExit()
    {
        if (exiting) return;
        exiting = true;
        exitTimer = 0f;
    }

    private void Update()
    {
        float yOffset;

        if (!exiting)
        {
            timer += Time.deltaTime;
            float t = riseInDuration <= 0f ? 1f : Mathf.Clamp01(timer / riseInDuration);
            yOffset = Mathf.Lerp(-riseInDistance, 0f, EaseOut(t));
            SetAlpha(t);
            if (autoExit && timer >= lifetime) BeginExit();
        }
        else
        {
            exitTimer += Time.deltaTime;
            float t = riseOutDuration <= 0f ? 1f : Mathf.Clamp01(exitTimer / riseOutDuration);
            yOffset = Mathf.Lerp(0f, riseOutDistance, EaseOut(t));
            SetAlpha(1f - t);
            if (t >= 1f)
            {
                ObjectPool.Instance.Despawn(gameObject);
                return;
            }
        }

        transform.position = Anchor + Vector3.up * yOffset;
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    private void SetAlpha(float a)
    {
        Color c = sr.color;
        c.a = Mathf.Clamp01(a);
        sr.color = c;
    }
}
