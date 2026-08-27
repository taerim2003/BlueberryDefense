using UnityEngine;

public class DamageNumber : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 1.2f;
    [SerializeField] private float lifetime = 0.85f;

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
    private Vector3 spawnPos; // 뜬 자리(월드 고정) — 적/투사체가 이동해도 여기서 위로만 올라간다

    private void Awake()
    {
        text = GetComponent<TMPro.TextMeshPro>();
        startGradient = text.colorGradient;
        baseScale = transform.localScale; // 풀에서 재사용되므로 원본 스케일을 여기서 한 번만 잡아둔다
    }

    // offset은 같은 공격의 서브히트를 쌓기 위한 오프셋(세로는 고정 간격, 가로는 Enemy가 흔들어 넘긴다).
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
        spawnPos = transform.position + offset;
        transform.position = spawnPos;
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
        float visible = Mathf.Max(0f, timer); // 딜레이 중엔 위치 고정, 알파 0 유지
        transform.position = spawnPos + Vector3.up * (moveSpeed * visible);
        transform.localScale = endScale * PopScale(visible / PopTime); // 보잉도 딜레이가 끝나야 시작한다

        // 그라데이션을 쓰면 text.color로는 알파가 먹지 않아 네 꼭짓점을 직접 낮춘다.
        float a = timer < 0f ? 0f : Mathf.Lerp(1f, 0f, timer / lifetime);
        TMPro.VertexGradient g = baseGradient;
        g.topLeft.a = a; g.topRight.a = a; g.bottomLeft.a = a; g.bottomRight.a = a;
        text.colorGradient = g;

        if (timer >= lifetime) ObjectPool.Instance.Despawn(gameObject);
    }
}
