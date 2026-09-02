using System.Collections.Generic;
using UnityEngine;

// 포도의 독성 안개 — 포도알이 터진 자리에 깔려 범위 안의 적을 계속 중독시킨다.
//
// 전용 그림이 아직 없어 **원형 스프라이트를 코드로 만들어 여러 장 겹친다**(사용자 결정: 프리미티브 보라 연기).
// 픽셀 아트라 가장자리는 일부러 하드하게 자른다 — 부드럽게 하면 다른 이펙트와 재질이 안 맞는다.
// 적(sortingOrder 100+)보다 뒤에 깔아서 안개가 적을 가리지 않게 한다.
public class PoisonCloud : MonoBehaviour
{
    private const int PuffCount = 7;
    private const int PuffTexSize = 32;
    private const int SortingOrder = 50;      // 배경(-100)보다 앞, 적(100+)보다 뒤 — 낙뢰 기둥과 같은 층
    private const float ReapplyInterval = 0.2f; // 적 탐색 주기. 매 프레임 FindObjects는 안개 여러 개면 비싸다
    private const float FadeInTime = 0.15f;
    private const float FadeOutTime = 0.4f;

    private static Sprite puffSprite;

    private float radius;
    private float lifetime;
    private float poisonDamage;
    private float poisonDuration;
    private float poisonInterval;

    private float age;
    private float nextApply;
    private readonly List<SpriteRenderer> puffs = new List<SpriteRenderer>();
    private readonly List<Vector3> puffHome = new List<Vector3>();
    private readonly List<float> puffPhase = new List<float>();
    private readonly List<float> puffBaseScale = new List<float>();

    public void Init(float radius, float lifetime, float poisonDamage, float poisonDuration, float poisonInterval, Color color)
    {
        this.radius = radius;
        this.lifetime = lifetime;
        this.poisonDamage = poisonDamage;
        this.poisonDuration = poisonDuration;
        this.poisonInterval = poisonInterval;
        age = 0f;
        nextApply = 0f;

        BuildPuffs(color);
    }

    private void BuildPuffs(Color color)
    {
        if (puffSprite == null) puffSprite = CreateCircleSprite();

        for (int i = 0; i < PuffCount; i++)
        {
            // 가운데 한 덩이 + 둘레에 흩어진 덩이들 = 뭉게뭉게한 실루엣.
            float angle = (i - 1) / (float)(PuffCount - 1) * Mathf.PI * 2f;
            float dist = i == 0 ? 0f : radius * Random.Range(0.35f, 0.62f);
            Vector3 home = i == 0
                ? Vector3.zero
                : new Vector3(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist * 0.55f, 0f); // 세로로 눌러 바닥에 깔린 느낌

            GameObject puff = new GameObject("Puff", typeof(SpriteRenderer));
            puff.transform.SetParent(transform, false);
            puff.transform.localPosition = home;

            SpriteRenderer sr = puff.GetComponent<SpriteRenderer>();
            sr.sprite = puffSprite;
            // 덩이마다 명도를 살짝 달리해야 한 장의 원판으로 안 보인다.
            float shade = Random.Range(0.82f, 1.12f);
            sr.color = new Color(color.r * shade, color.g * shade, color.b * shade, 0f); // 알파는 페이드인이 올린다
            sr.sortingOrder = SortingOrder + i;

            float scale = radius * (i == 0 ? 1.25f : Random.Range(0.72f, 1.0f));
            puff.transform.localScale = Vector3.one * scale;

            puffs.Add(sr);
            puffHome.Add(home);
            puffPhase.Add(Random.Range(0f, Mathf.PI * 2f));
            puffBaseScale.Add(scale);
        }
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= lifetime) { Destroy(gameObject); return; }

        // 들어올 때 부풀고 사라질 때 옅어진다 — 그냥 켜고 끄면 중독 범위가 언제 생겼는지 안 보인다.
        float alpha = 1f;
        if (age < FadeInTime) alpha = age / FadeInTime;
        else if (age > lifetime - FadeOutTime) alpha = Mathf.Max(0f, (lifetime - age) / FadeOutTime);

        for (int i = 0; i < puffs.Count; i++)
        {
            SpriteRenderer sr = puffs[i];
            if (sr == null) continue;

            Color c = sr.color;
            c.a = alpha;
            sr.color = c;

            // 아주 느린 부유 + 맥동. 값이 크면 "연기"가 아니라 "튀는 공"이 된다.
            float t = age * 1.3f + puffPhase[i];
            sr.transform.localPosition = puffHome[i] + new Vector3(Mathf.Sin(t) * 0.06f, Mathf.Cos(t * 0.8f) * 0.05f, 0f);
            float grow = age < FadeInTime ? Mathf.Lerp(0.55f, 1f, age / FadeInTime) : 1f;
            sr.transform.localScale = Vector3.one * (puffBaseScale[i] * grow * (1f + Mathf.Sin(t * 0.7f) * 0.04f));
        }

        nextApply -= Time.deltaTime;
        if (nextApply > 0f) return;
        nextApply = ReapplyInterval;

        // 안개 안에 있는 동안 계속 다시 걸어 준다 — 나가면 남은 지속시간만큼만 아프다.
        foreach (Enemy e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
        {
            if (e == null || !e.IsAlive) continue;
            if (Vector2.Distance(e.transform.position, transform.position) > radius) continue;
            e.ApplyPoison(poisonDamage, poisonDuration, poisonInterval);
        }
    }

    // 하드 엣지 원. 안티앨리어싱을 넣으면 도트 이펙트들과 재질이 안 맞는다.
    private static Sprite CreateCircleSprite()
    {
        Texture2D tex = new Texture2D(PuffTexSize, PuffTexSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;

        float r = PuffTexSize * 0.5f;
        Color[] px = new Color[PuffTexSize * PuffTexSize];
        for (int y = 0; y < PuffTexSize; y++)
            for (int x = 0; x < PuffTexSize; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                px[y * PuffTexSize + x] = (dx * dx + dy * dy) <= r * r ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();

        // pixelsPerUnit = 텍스처 크기 → localScale 1이 곧 지름 1유닛이 된다(반지름 계산이 그대로 먹는다).
        return Sprite.Create(tex, new Rect(0f, 0f, PuffTexSize, PuffTexSize), new Vector2(0.5f, 0.5f), PuffTexSize);
    }
}
