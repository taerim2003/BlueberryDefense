using System;
using UnityEngine;

// 중간 소환 예고 마커 — 지정한 바닥 위치에 표적 원을 깔고, 커지며 점점 빠르게 깜빡이다가
// 시간이 다 차면 onFire를 호출하고 스스로 사라진다.
// "예고 없이 부대가 튀어나오면 대응이 불가능하다"는 게 이 메커닉의 유일한 위험이라, 마커가 그 대응 시간을 만든다.
// EnemySpawner가 런타임에 생성한다 — 씬/프리팹 배선 없음.
//
// ⚠️ 스프라이트는 런타임 생성 임시 디스크(도트 아님). 전용 마커 도트를 그리면 BuildSprite만 갈아끼우면 된다.
public class AmbushMarker : MonoBehaviour
{
    private const float Radius = 1.1f;         // 다 자랐을 때의 월드 반지름
    private const float FlatY = 0.4f;          // 바닥에 누운 타원으로 보이게 y를 눌러주는 비율
    private const float StartScaleRatio = 0.3f; // 등장 순간 크기(다 자란 것 대비)
    private const float BlinkStartHz = 2f;      // 깜빡임 시작 주파수
    private const float BlinkEndHz = 10f;       // 소환 직전 주파수 — 빨라지는 것만으로 "곧 터진다"가 읽힌다
    // 밝은 잔디·흙 배경 위에 얹히므로 최소 알파가 낮으면 깜빡임의 절반이 배경에 묻힌다(한 번 이 함정에 빠졌음).
    private const float AlphaMin = 0.45f;
    private const float AlphaMax = 0.95f;
    private const int SortingOrder = -50;       // 배경(-100)보다 앞, 적(1~180)·플레이어(0)보다 뒤 = 바닥에 그려진 것처럼
    private static readonly Color EarlyColor = new Color(0.85f, 0.15f, 0.2f);
    private static readonly Color LateColor = new Color(1f, 0.5f, 0.15f);

    private static Sprite cachedSprite;

    private SpriteRenderer sr;
    private float duration;
    private float timer;
    private Action onFire;

    public static AmbushMarker Spawn(Vector3 pos, float duration, Action onFire)
    {
        GameObject go = new GameObject("AmbushMarker");
        go.transform.position = pos;
        AmbushMarker m = go.AddComponent<AmbushMarker>();
        m.duration = duration;
        m.onFire = onFire;
        m.sr = go.AddComponent<SpriteRenderer>();
        m.sr.sprite = GetSprite();
        m.sr.sortingOrder = SortingOrder;
        return m;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);

        float scale = Radius * Mathf.Lerp(StartScaleRatio, 1f, Mathf.SmoothStep(0f, 1f, t));
        transform.localScale = new Vector3(scale, scale * FlatY, 1f);

        // 주파수가 t를 따라 오르므로 위상은 적분해서 누적한다 — 현재 hz를 timer에 그대로 곱하면 위상이 뒤로 튄다.
        // hz(t) = start + (end-start)·t, t = timer/duration  →  ∫ = start·timer + (end-start)·timer²/(2·duration)
        float phase = BlinkStartHz * timer + (BlinkEndHz - BlinkStartHz) * timer * timer / (2f * duration);
        float blink = Mathf.InverseLerp(-1f, 1f, Mathf.Sin(phase * Mathf.PI * 2f));
        Color c = Color.Lerp(EarlyColor, LateColor, t);
        c.a = Mathf.Lerp(AlphaMin, AlphaMax, blink) * Mathf.Lerp(0.6f, 1f, t);
        sr.color = c;

        if (timer >= duration)
        {
            Action fire = onFire;
            onFire = null; // 파괴 지연으로 Update가 한 번 더 돌더라도 두 번 터지지 않게
            Destroy(gameObject);
            fire?.Invoke();
        }
    }

    // 가장자리로 갈수록 진해지는 표적 원. PPU 32·Point 필터라 다른 도트와 같은 픽셀 밀도로 보인다.
    private static Sprite GetSprite()
    {
        if (cachedSprite != null) return cachedSprite;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var px = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                // 테두리 링을 굵고 진하게, 안쪽은 옅게 — 꽉 찬 원보다 "표적"으로 읽힌다.
                float a = d > 1f ? 0f : (d > 0.72f ? 1f : Mathf.Lerp(0.22f, 0.5f, d / 0.72f));
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();

        cachedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
        return cachedSprite;
    }
}
