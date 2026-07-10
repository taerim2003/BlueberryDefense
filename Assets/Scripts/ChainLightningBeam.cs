using UnityEngine;

// 두 지점을 잇는 지그재그 번개 선을 그린 뒤 사라짐 (워크래프트식 체인 라이트닝).
[RequireComponent(typeof(LineRenderer))]
public class ChainLightningBeam : MonoBehaviour
{
    [SerializeField] private float lifetime = 0.35f;
    [SerializeField] private int segments = 6;
    [SerializeField] private float jitter = 0.15f;

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
        SetAlpha(Mathf.Lerp(1f, 0f, timer / lifetime));
    }

    private void SetAlpha(float alpha)
    {
        Color c = baseColor;
        c.a = alpha;
        line.startColor = c;
        line.endColor = c;
    }
}
