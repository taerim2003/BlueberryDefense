using UnityEngine;

public class DamageNumber : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float lifetime = 0.8f;

    private static readonly Color CritColor = new Color(1f, 0.15f, 0.15f, 1f);

    private TMPro.TextMeshPro text;
    private float timer;
    private float delay;      // 멀티히트 순차 표시용: 이 시간 전엔 숨김
    private Color startColor;
    private Color baseColor;
    private Vector3 spawnPos;

    private void Awake()
    {
        text = GetComponent<TMPro.TextMeshPro>();
        startColor = text.color;
    }

    public void Init(float damage, bool isCrit = false, Vector3 offset = default, float delay = 0f)
    {
        text.text = Mathf.RoundToInt(damage) + (isCrit ? "!" : "");
        baseColor = isCrit ? CritColor : startColor;
        timer = 0f;
        this.delay = delay;
        spawnPos = transform.position + offset;
        transform.position = spawnPos;

        // 딜레이가 있으면 등장 전까지 숨긴다
        Color c = baseColor;
        c.a = delay > 0f ? 0f : 1f;
        text.color = c;
    }

    private void Update()
    {
        timer += Time.deltaTime;

        // 등장 대기(멀티히트 순차 표시): 아직 숨김
        if (timer < delay)
            return;

        float t = timer - delay;
        transform.position = spawnPos + Vector3.up * (moveSpeed * t);

        Color c = baseColor;
        c.a = Mathf.Lerp(1f, 0f, t / lifetime);
        text.color = c;

        if (t >= lifetime) ObjectPool.Instance.Despawn(gameObject);
    }
}
