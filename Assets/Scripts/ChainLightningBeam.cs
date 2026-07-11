using UnityEngine;

// 두 지점을 잇는 지그재그 번개 선을 그린 뒤 사라짐 (워크래프트식 체인 라이트닝).
[RequireComponent(typeof(LineRenderer))]
public class ChainLightningBeam : MonoBehaviour
{
    [SerializeField] private float lifetime = 0.6f;
    [SerializeField] private int segments = 8;
    [SerializeField] private float jitter = 0.25f;

    private LineRenderer line;
    private float timer;
    private Color baseColor;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        baseColor = line.startColor;
    }

    public void Init(Vector3 from, Vector3 to)
    {
        line.positionCount = segments + 1;
        Vector3 dir = to - from;
        Vector3 perpendicular = Vector3.Cross(dir, Vector3.forward).normalized;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector3 point = Vector3.Lerp(from, to, t);
            if (i > 0 && i < segments)
                point += perpendicular * Random.Range(-jitter, jitter);
            line.SetPosition(i, point);
        }

        timer = 0f;
        line.enabled = true;
        SetAlpha(1f);
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = timer / lifetime;
        // 처음부터 서서히 흐려지면 눈에 띄기도 전에 옅어져 보이므로, 절반은 완전히 밝게 유지하다가 후반에만 페이드
        float alpha = t < 0.5f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.5f) / 0.5f);
        SetAlpha(alpha);
    }

    private void SetAlpha(float alpha)
    {
        Color c = baseColor;
        c.a = alpha;
        line.startColor = c;
        line.endColor = c;
    }
}
