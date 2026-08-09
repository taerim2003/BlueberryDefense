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

    private TMPro.TextMeshPro text;
    private float timer;
    private TMPro.VertexGradient startGradient;
    private TMPro.VertexGradient baseGradient;
    private Vector3 spawnPos; // 뜬 자리(월드 고정) — 적/투사체가 이동해도 여기서 위로만 올라간다

    private void Awake()
    {
        text = GetComponent<TMPro.TextMeshPro>();
        startGradient = text.colorGradient;
    }

    // offset은 같은 공격의 서브히트를 세로로 정렬해 쌓기 위한 고정 오프셋(가로는 0으로 완전 정렬).
    public void Init(float damage, bool isCrit = false, Vector3 offset = default)
    {
        text.text = Mathf.RoundToInt(damage) + (isCrit ? "!" : "");
        baseGradient = isCrit ? CritGradient : startGradient;
        timer = 0f;
        spawnPos = transform.position + offset;
        transform.position = spawnPos;
        text.colorGradient = baseGradient;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        transform.position = spawnPos + Vector3.up * (moveSpeed * timer);

        // 그라데이션을 쓰면 text.color로는 알파가 먹지 않아 네 꼭짓점을 직접 낮춘다.
        float a = Mathf.Lerp(1f, 0f, timer / lifetime);
        TMPro.VertexGradient g = baseGradient;
        g.topLeft.a = a; g.topRight.a = a; g.bottomLeft.a = a; g.bottomRight.a = a;
        text.colorGradient = g;

        if (timer >= lifetime) ObjectPool.Instance.Despawn(gameObject);
    }
}
