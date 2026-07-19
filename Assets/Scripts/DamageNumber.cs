using UnityEngine;

public class DamageNumber : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 1.2f;
    [SerializeField] private float lifetime = 0.85f;

    private static readonly Color CritColor = new Color(1f, 0.15f, 0.15f, 1f);

    private TMPro.TextMeshPro text;
    private float timer;
    private Color startColor;
    private Color baseColor;
    private Vector3 spawnPos; // 뜬 자리(월드 고정) — 적/투사체가 이동해도 여기서 위로만 올라간다

    private void Awake()
    {
        text = GetComponent<TMPro.TextMeshPro>();
        startColor = text.color;
    }

    // offset은 같은 공격의 서브히트를 세로로 정렬해 쌓기 위한 고정 오프셋(가로는 0으로 완전 정렬).
    public void Init(float damage, bool isCrit = false, Vector3 offset = default)
    {
        text.text = Mathf.RoundToInt(damage) + (isCrit ? "!" : "");
        baseColor = isCrit ? CritColor : startColor;
        timer = 0f;
        spawnPos = transform.position + offset;
        transform.position = spawnPos;
        text.color = baseColor;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        transform.position = spawnPos + Vector3.up * (moveSpeed * timer);

        Color c = baseColor;
        c.a = Mathf.Lerp(1f, 0f, timer / lifetime);
        text.color = c;

        if (timer >= lifetime) ObjectPool.Instance.Despawn(gameObject);
    }
}
