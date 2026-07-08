using UnityEngine;

public class HitParticle : MonoBehaviour
{
    [SerializeField] private float gravity = 14f;
    [SerializeField] private float fadeInDuration = 0.05f;
    [SerializeField] private float offScreenY = -6f;

    private SpriteRenderer sr;
    private Vector2 velocity;
    private Color startColor;
    private float timer;

    public void Init(Sprite sprite, Vector2 initialVelocity)
    {
        sr = GetComponent<SpriteRenderer>();
        sr.sprite = sprite;
        startColor = sr.color;
        Color c = startColor;
        c.a = 0f;
        sr.color = c;
        velocity = initialVelocity;
        timer = 0f;
    }

    private void Update()
    {
        velocity.y -= gravity * Time.deltaTime;
        transform.position += (Vector3)(velocity * Time.deltaTime);
        timer += Time.deltaTime;

        Color c = startColor;
        c.a = startColor.a * Mathf.Clamp01(timer / fadeInDuration);
        sr.color = c;

        if (transform.position.y < offScreenY) ObjectPool.Instance.Despawn(gameObject);
    }
}
