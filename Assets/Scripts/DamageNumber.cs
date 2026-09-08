using UnityEngine;

// 🔴 숫자는 **태어난 자리에 월드 고정**된다 — 적이 움직여도 따라가지 않고, 그 자리에서 위로 떠오르며 사라진다
//    (2026-09-07 사용자 결정. 이전의 "적이 든 큐가 매 프레임 자리를 정해 준다"는 2026-09-02 명세를 뒤집은 것).
//    "새 데미지는 아래, 기존은 위로"는 큐 없이도 유지된다 — 먼저 뜬 숫자가 이미 위로 올라가 있기 때문이다.
// ⚠️ 큐를 되살리지 말 것. 풀에서 재사용된 숫자를 옛 주인과 새 주인이 동시에 잡아당겨
//    숫자가 엉뚱한 적으로 튀는 버그가 거기서 났다.
public class DamageNumber : MonoBehaviour
{
    // ⚠️ moveSpeed는 연타 간격을 정하는 값이기도 하다 — 앞 숫자가 `Enemy.DamageNumberStaggerDelay` 동안
    //    올라간 거리가 곧 두 숫자 사이 간격이다(3.2 × 0.18 ≈ 0.58유닛. 글리프 높이가 0.62~1.0이라 이보다 좁히면 겹친다).
    [SerializeField] private float moveSpeed = 3.2f;   // 위로 떠오르는 속도
    [SerializeField] private float lifetime = 0.7f;    // 보이기 시작한 뒤 이만큼 살고 사라진다

    private const float FadeOutTime = 0.25f;   // 수명의 마지막 이만큼 동안 페이드아웃

    // 일반 데미지의 그라데이션은 프리팹에서 잡고, 치명타만 여기서 빨강으로 갈아끼운다.
    // 윗색까지 빨강 계열로 밀어야 구분된다 — 밝은 윗색을 쓰면 흰 테두리에 가려 일반과 안 갈린다.
    private static readonly TMPro.VertexGradient CritGradient = new TMPro.VertexGradient(
        new Color(1f, 0.50f, 0.38f, 1f), new Color(1f, 0.50f, 0.38f, 1f),
        new Color(0.72f, 0.02f, 0.10f, 1f), new Color(0.72f, 0.02f, 0.10f, 1f));

    // 피해가 클수록 크고 진하게. 작은 타격을 확 줄이는 게 목적이라 기준선(ScaleAtLow)이 1보다 한참 아래다.
    // 로그 보간이라 한 자릿수부터 세 자릿수까지 한 곡선으로 덮인다(선형이면 초반 구간이 전부 뭉갠다).
    private const float DamageAtMinSize = 3f;    // 이 이하 = 가장 작게
    private const float DamageAtMaxSize = 150f;  // 이 이상 = 가장 크게
    private const float ScaleAtLow = 0.7f;
    private const float ScaleAtHigh = 1.15f;

    // 뜨는 순간의 "보잉" — 작게 튀어나와 한 번 넘겼다가 제자리로. 진폭도 피해가 클수록 커진다.
    // (최종 크기 차등과 별개다. 크기는 얼마나 아픈지, 보잉 진폭은 얼마나 세게 꽂혔는지를 말한다.)
    // DOTween을 안 쓰는 이유: 이 오브젝트는 풀링돼 초당 수십 번 뜬다 — 트윈을 매번 만들면
    // 할당이 쌓이고, 반납된 오브젝트에 트윈이 남아 스케일을 건드린다.
    private const float PopTime = 0.2f;
    private const float PopRise = 0.35f;    // 이 지점에서 최대까지 부풀고, 나머지 구간에 제자리로 돌아온다
    private const float PopAtLow = 0.25f;
    private const float PopAtHigh = 0.7f;

    // 큰 피해 쪽 색. 아랫색만 확 진하게 밀고 윗색은 거의 그대로 둔다 — 윗색까지 어둡게 하면
    // 흰 테두리 안에서 글자가 뭉개져 오히려 안 읽힌다(치명타 빨강과도 헷갈린다).
    private static readonly Color DeepTop = new Color(1f, 0.90f, 0.52f, 1f);
    private static readonly Color DeepBottom = new Color(0.94f, 0.26f, 0.02f, 1f);

    private TMPro.TextMeshPro text;
    private float timer;
    private TMPro.VertexGradient startGradient;
    private TMPro.VertexGradient baseGradient;
    private Vector3 baseScale;
    private Vector3 endScale;  // 보잉이 끝난 뒤 눌러앉을 크기(피해량에 따른 차등이 여기 들어 있다)
    private float popAmount;

    private void Awake()
    {
        text = GetComponent<TMPro.TextMeshPro>();
        startGradient = text.colorGradient;
        baseScale = transform.localScale; // 풀에서 재사용되므로 원본 스케일을 여기서 한 번만 잡아둔다
    }

    // offset은 뜨는 자리(세로는 머리 위 높이, 가로는 Enemy가 흔들어 넘긴다).
    // delay는 같은 공격의 서브히트를 시간차로 띄우기 위한 것 — 그동안 투명하게 제자리에서 기다린다.
    public void Init(float damage, bool isCrit = false, Vector3 offset = default, float delay = 0f)
    {
        text.text = Mathf.RoundToInt(damage) + (isCrit ? "!" : "");

        float t = Mathf.InverseLerp(Mathf.Log(DamageAtMinSize), Mathf.Log(DamageAtMaxSize),
                                    Mathf.Log(Mathf.Max(damage, 1f)));
        endScale = baseScale * Mathf.Lerp(ScaleAtLow, ScaleAtHigh, t);
        popAmount = Mathf.Lerp(PopAtLow, PopAtHigh, t);
        // 치명타는 이미 전용 빨강이라 색은 그대로 두고 크기 차등만 받는다.
        baseGradient = isCrit ? CritGradient : DeepenGradient(startGradient, t);

        timer = -delay;

        // 첫 프레임을 Update에 맡기면 한 프레임 동안 최종 크기로 떠 보인다 — 여기서 0 지점을 직접 찍는다.
        // 딜레이가 걸려 있어도 같은 자리에서 시작한다 — 보잉은 숫자가 보이기 시작할 때 돈다.
        transform.localScale = endScale * PopScale(0f);
        transform.position += offset;
        // 딜레이 중엔 투명하게 대기
        TMPro.VertexGradient g = baseGradient;
        if (delay > 0f) { g.topLeft.a = 0f; g.topRight.a = 0f; g.bottomLeft.a = 0f; g.bottomRight.a = 0f; }
        text.colorGradient = g;
    }

    private static TMPro.VertexGradient DeepenGradient(TMPro.VertexGradient g, float t) =>
        new TMPro.VertexGradient(
            Color.Lerp(g.topLeft, DeepTop, t), Color.Lerp(g.topRight, DeepTop, t),
            Color.Lerp(g.bottomLeft, DeepBottom, t), Color.Lerp(g.bottomRight, DeepBottom, t));

    // 0 → (작게) → 최대 → 1 로 수렴하는 크기 배율. p는 PopTime 기준 진행도.
    private float PopScale(float p)
    {
        if (p >= 1f) return 1f;
        return p < PopRise
            ? Mathf.Lerp(1f - popAmount * 0.8f, 1f + popAmount, Mathf.SmoothStep(0f, 1f, p / PopRise))
            : Mathf.Lerp(1f + popAmount, 1f, Mathf.SmoothStep(0f, 1f, (p - PopRise) / (1f - PopRise)));
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer < 0f) return;   // 서브히트 지연 중 — Init이 찍어 둔 자리·크기·투명 상태 그대로 대기

        transform.localScale = endScale * PopScale(timer / PopTime);
        transform.position += Vector3.up * (moveSpeed * Time.deltaTime);

        // 수명의 마지막 FadeOutTime 동안만 흐려진다.
        float a = Mathf.InverseLerp(lifetime, lifetime - FadeOutTime, timer);

        // 그라데이션을 쓰면 text.color로는 알파가 먹지 않아 네 꼭짓점을 직접 낮춘다.
        TMPro.VertexGradient g = baseGradient;
        g.topLeft.a = a; g.topRight.a = a; g.bottomLeft.a = a; g.bottomRight.a = a;
        text.colorGradient = g;

        if (timer >= lifetime) ObjectPool.Instance.Despawn(gameObject);
    }
}
