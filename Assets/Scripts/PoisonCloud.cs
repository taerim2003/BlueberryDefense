using System.Collections.Generic;
using UnityEngine;

// 포도의 독성 안개 — 포도알이 터진 자리에 깔려 범위 안의 적을 계속 중독시킨다.
//
// 전용 그림이 아직 없어 **원형 스프라이트를 코드로 만들어 여러 장 겹친다**(사용자 결정: 프리미티브 보라 연기).
// 픽셀 아트라 가장자리는 일부러 하드하게 자른다 — 부드럽게 하면 다른 이펙트와 재질이 안 맞는다.
// 적(sortingOrder 1~180)보다 **앞**에 깔고, 대신 알파 상한을 걸어 뒤의 적이 비쳐 보이게 한다(사용자 결정 2026-09-02).
public class PoisonCloud : MonoBehaviour
{
    // 🔴 안개가 커질 때 **덩이를 키우지 않고 개수를 늘린다**(사용자 지시 2026-09-27).
    //    예전엔 덩이 크기에 radius를 곱해서, 넓어질수록 같은 그림이 확대돼 뭉개져 보였다.
    //    이제 덩이는 늘 같은 크기고, 넓이(반경²)에 비례해 개수가 는다.
    private const int PuffCountAtBase = 7;            // 기본 반경(PlayerSkills.GrapeCloudRadius)에서의 덩이 수
    private const int PuffCountMax = 18;              // 상한 — 파티클 수가 곧 프레임 비용이라 여기서 끊는다
    private const float PuffCenterSize = 1.875f;      // 가운데 덩이 지름(유닛). 기본 반경 1.5 × 1.25 = 예전 그대로
    private const float PuffSizeMin = 1.08f;          // 둘레 덩이 지름 하한 (1.5 × 0.72)
    private const float PuffSizeMax = 1.5f;           // 둘레 덩이 지름 상한 (1.5 × 1.0)
    private const int PuffTexSize = 32;
    private const int SortingOrder = 250;     // 적(1~180)보다 앞, 만화 효과(600)·미사일(400)보다 뒤
    private const float MaxAlpha = 0.33f;     // 덩이 여러 장이 겹치므로 장당 알파를 낮춰야 뒤의 적이 보인다
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

        // 덩이 수는 **넓이**를 따라간다 — 반경이 √2배면 개수가 2배다. 기본 반경에서는 예전과 같은 7장.
        float sizeRatio = radius / PlayerSkills.GrapeCloudRadius;
        int puffCount = Mathf.Clamp(Mathf.RoundToInt(PuffCountAtBase * sizeRatio * sizeRatio),
                                    PuffCountAtBase, PuffCountMax);

        for (int i = 0; i < puffCount; i++)
        {
            // 가운데 한 덩이 + 둘레에 흩어진 덩이들 = 뭉게뭉게한 실루엣.
            // 🔴 황금각 나선으로 원판을 고르게 메운다 — 한 겹 링에 늘어놓으면 개수가 늘수록
            //    둘레만 빽빽해지고 가운데가 빈다. sqrt는 바깥으로 갈수록 링 간격을 좁혀 밀도를 맞춘다.
            float angle = i * 2.39996f;
            float dist = i == 0 ? 0f : radius * 0.62f * Mathf.Sqrt(i / (float)(puffCount - 1));
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

            // 🔴 radius를 곱하지 않는다 — 덩이 크기는 안개가 넓어져도 그대로다(개수로 채운다).
            float scale = i == 0 ? PuffCenterSize : Random.Range(PuffSizeMin, PuffSizeMax);
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
        alpha *= MaxAlpha;

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
        using (Enemy.GetSnapshot(out List<Enemy> enemies))
            foreach (Enemy e in enemies)
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
