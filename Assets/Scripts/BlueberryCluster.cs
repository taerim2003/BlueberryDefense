using System.Collections;
using UnityEngine;

// 엔딩 보스 "블루베리 군집체"(모든 블루베리의 결집체)의 연출. 전투는 같은 오브젝트의 Enemy가 맡는다 —
// 그래서 모든 스킬이 평소처럼 조준·타격한다. 여기선 굴러 들어오기 · 맞을 때 흔들림 · 블루베리 파편만 한다.
// 스폰·사망 후 처리는 EndingSequence가 한다(사망하면 Enemy가 풀로 반납돼 이 컴포넌트도 멈춘다).
[RequireComponent(typeof(Enemy))]
public class BlueberryCluster : MonoBehaviour
{
    [SerializeField] private Sprite chunkSprite;            // 튀는 파편 = 기본 블루베리 그림
    [SerializeField] private float chunkScale = 1.5f;       // 프로젝트 픽셀 규칙(1.5배) — 필드의 기본 블루베리와 같은 크기
    [SerializeField] private float chunksPerSecondMax = 45f; // 다단히트 스킬에 파편이 화면을 덮지 않게
    [SerializeField] private int chunksPerHitMin = 3;        // 한 번 맞을 때 튀는 알 수
    [SerializeField] private int chunksPerHitMax = 5;
    [SerializeField] private float shakeAmount = 0.08f;     // 피격 흔들림 진폭(유닛)
    [SerializeField] private float shakeDuration = 0.12f;

    private Enemy enemy;
    private SpriteRenderer body;
    private BoxCollider2D box;
    private float lastHealth = -1f;
    private float chunkBudget;
    private float shakeTimer;
    private Vector3 restPos;
    private bool rolling;

    // 몸통 반지름 = 판정 상자 기준. 그림은 가장자리 블루베리가 삐져나와 있어 그림 크기보다 작다.
    public float Radius => box != null ? box.size.y * 0.5f * Mathf.Abs(transform.localScale.y) : 1f;
    public Sprite ChunkSprite => chunkSprite;

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
        body = GetComponent<SpriteRenderer>();
        box = GetComponent<BoxCollider2D>();
    }

    private void OnEnable()
    {
        lastHealth = -1f;
        shakeTimer = 0f;
        rolling = false;
    }

    // 화면 밖에서 굴러 들어온다. 도착하면 똑바로 선다(총 회전이 360의 배수가 되게 되감는다).
    public IEnumerator RollIn(float fromX, float toX, float duration)
    {
        rolling = true;
        SetShadowVisible(false); // 그림자는 몸의 자식이라 같이 돌아 버린다 — 구르는 동안만 숨긴다
        Vector3 p = transform.position;
        float circumference = 2f * Mathf.PI * Mathf.Max(0.1f, Radius);
        int turns = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(toX - fromX) / circumference));

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = 1f - (1f - t / duration) * (1f - t / duration); // 감속하며 멈춘다
            p.x = Mathf.Lerp(fromX, toX, k);
            transform.position = p;
            transform.rotation = Quaternion.Euler(0f, 0f, -360f * turns * k); // 오른쪽으로 구르면 시계 방향
            yield return null;
        }

        p.x = toX;
        transform.position = p;
        transform.rotation = Quaternion.identity;
        restPos = p;
        SetShadowVisible(true);
        rolling = false;
    }

    private void Update()
    {
        if (enemy == null || rolling) return;

        float hp = enemy.CurrentHealth;
        if (lastHealth < 0f) { lastHealth = hp; restPos = transform.position; }

        chunkBudget = Mathf.Min(chunkBudget + chunksPerSecondMax * Time.deltaTime, chunksPerHitMax * 2f);
        if (hp < lastHealth)
        {
            shakeTimer = shakeDuration;
            int n = Mathf.Min(Random.Range(chunksPerHitMin, chunksPerHitMax + 1), Mathf.FloorToInt(chunkBudget));
            if (chunkSprite != null)
                for (int i = 0; i < n; i++) { chunkBudget -= 1f; SpawnChunk(false); }
        }
        lastHealth = hp;

        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            float a = shakeAmount * Mathf.Clamp01(shakeTimer / shakeDuration);
            transform.position = restPos + new Vector3(Random.Range(-a, a), Random.Range(-a, a) * 0.5f, 0f);
            if (shakeTimer <= 0f) transform.position = restPos;
        }
    }

    // 몸 둘레의 한 점에서 바깥·위로 튀어 떨어지는 블루베리 한 알.
    private void SpawnChunk(bool burst)
    {
        Vector2 dir = Random.insideUnitCircle.normalized;
        if (!burst && dir.y < 0f) dir.y = -dir.y; // 평소 피격은 위쪽 반원에서만 — 땅 밑으로 튀면 안 보인다
        Vector3 from = restPos + (Vector3)(dir * Radius * 0.85f);
        float speed = burst ? Random.Range(6f, 13f) : Random.Range(3f, 6f);
        Vector2 vel = dir * speed + Vector2.up * (burst ? 3f : 4f);
        BlueberryChunk.Spawn(chunkSprite, from, vel, chunkScale, body);
    }

    // 사망 순간 사방으로 터지는 파편. Enemy가 곧 풀로 반납되므로 EndingSequence가 부른다.
    public void Burst(int count)
    {
        restPos = transform.position;
        for (int i = 0; i < count; i++) SpawnChunk(true);
    }

    private void SetShadowVisible(bool visible)
    {
        Transform s = transform.Find("Shadow");
        if (s != null && s.TryGetComponent(out SpriteRenderer sr)) sr.enabled = visible;
    }
}

// 파편 한 알 — 포물선으로 날다 사라진다. 런타임 AddComponent 전용이라 BlueberryCluster 파일에 둔다.
public class BlueberryChunk : MonoBehaviour
{
    private const float Gravity = 22f;
    private const float Life = 0.9f;

    private Vector2 velocity;
    private float spin;
    private float age;
    private SpriteRenderer sr;

    public static void Spawn(Sprite sprite, Vector3 pos, Vector2 velocity, float scale, SpriteRenderer sortRef)
    {
        var go = new GameObject("BlueberryChunk");
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * scale;
        var c = go.AddComponent<BlueberryChunk>();
        c.sr = go.AddComponent<SpriteRenderer>();
        c.sr.sprite = sprite;
        if (sortRef != null)
        {
            c.sr.sortingLayerID = sortRef.sortingLayerID;
            c.sr.sortingOrder = sortRef.sortingOrder + 1;
        }
        c.velocity = velocity;
        c.spin = Random.Range(-540f, 540f);
    }

    private void Update()
    {
        age += Time.deltaTime;
        velocity.y -= Gravity * Time.deltaTime;
        transform.position += (Vector3)(velocity * Time.deltaTime);
        transform.Rotate(0f, 0f, spin * Time.deltaTime);
        float fade = Mathf.Clamp01((Life - age) / (Life * 0.35f));
        sr.color = new Color(1f, 1f, 1f, fade);
        if (age >= Life) Destroy(gameObject);
    }
}
