using UnityEngine;

public class DamageNumber : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float lifetime = 0.8f;

    private static readonly Color CritColor = new Color(1f, 0.15f, 0.15f, 1f);

    private TMPro.TextMeshPro text;
    private float timer;
    private Color startColor;
    private Color baseColor;

    private void Awake()
    {
        text = GetComponent<TMPro.TextMeshPro>();
        startColor = text.color;
    }

    public void Init(float damage, bool isCrit = false)
    {
        text.text = Mathf.RoundToInt(damage) + (isCrit ? "!" : "");
        baseColor = isCrit ? CritColor : startColor;
        timer = 0f;
    }

    private void Update()
    {
        transform.Translate(Vector3.up * moveSpeed * Time.deltaTime);
        timer += Time.deltaTime;

        Color c = baseColor;
        c.a = Mathf.Lerp(1f, 0f, timer / lifetime);
        text.color = c;

        if (timer >= lifetime) ObjectPool.Instance.Despawn(gameObject);
    }
}
