using UnityEngine;

public class HitParticle : MonoBehaviour
{
    [SerializeField] private float gravity = 14f;
    [SerializeField] private float fadeInDuration = 0.05f;
    [SerializeField] private float offScreenY = -6f;

    // 동시에 떠 있을 수 있는 파편 수. 타격 1회에 2~9개가 나오는데 후반엔 타격이 초당 수백 번이라,
    // 상한이 없으면 수천 개가 동시에 떠서(2026-09-18 봇 실측 3,872개) 이 Update만으로 프레임을 먹었다.
    // 상한에 닿으면 새 타격의 파편만 생략된다 — 이미 뜬 것은 그대로 떨어진다(Enemy.SpawnHitParticles).
    // 2000 = 사용자 결정(2026-09-18). 에디터가 빌드보다 느리므로 에디터 기준으로 줄이지 않는다.
    public const int MaxLive = 2000;
    public static int Live { get; private set; }

    private SpriteRenderer sr;
    private Vector2 velocity;
    private Vector3 position;
    private Color startColor;
    private float timer;
    private bool fadingIn;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        // 원본 색은 한 번만 잡는다. Init에서 sr.color를 읽으면 페이드인 도중 반납된 개체의 반투명이 원본으로 굳는다.
        startColor = sr.color;
    }

    private void OnEnable() => Live++;
    private void OnDisable() => Live--;

    public void Init(Sprite sprite, Vector2 initialVelocity)
    {
        sr.sprite = sprite;
        Color c = startColor;
        c.a = 0f;
        sr.color = c;
        velocity = initialVelocity;
        position = transform.position;
        timer = 0f;
        fadingIn = true;
    }

    private void Update()
    {
        velocity.y -= gravity * Time.deltaTime;
        position += (Vector3)(velocity * Time.deltaTime);
        transform.position = position;
        timer += Time.deltaTime;

        // 색은 페이드인 구간에만 쓴다 — 수천 개가 매 프레임 같은 색을 다시 넣던 게 비용이었다.
        // 구간이 끝나는 프레임에 원본 알파를 한 번 넣고 멈춘다.
        if (fadingIn)
        {
            float k = Mathf.Clamp01(timer / fadeInDuration);
            Color c = startColor;
            c.a = startColor.a * k;
            sr.color = c;
            if (k >= 1f) fadingIn = false;
        }

        if (position.y < offScreenY) ObjectPool.Instance.Despawn(gameObject);
    }
}
