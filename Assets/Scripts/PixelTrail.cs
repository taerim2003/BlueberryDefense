using UnityEngine;

// 기존 TrailRenderer를 끄고, 날아간 경로에 픽셀 도트를 남기는 ParticleSystem으로 대체한다.
// MonoBehaviour를 붙이기만 하면 ParticleSystem 추가·설정을 자동으로 처리한다.
[DisallowMultipleComponent]
public class PixelTrail : MonoBehaviour
{
    [SerializeField] private Gradient trailGradient;
    [SerializeField] private AnimationCurve sizeCurve;
    [SerializeField] private float dotSize = 0.10f;      // 월드 유닛 (~3px @ PPU32)
    [SerializeField] private float dotLifetime = 0.35f;  // 초
    [SerializeField] private float dotsPerUnit = 8f;     // 이동 거리당 파티클 수

    private void Reset()
    {
        trailGradient = BuildDefaultGradient();
        sizeCurve     = AnimationCurve.Linear(0f, 1f, 1f, 0f);
    }

    private void Awake()
    {
        var tr = GetComponent<TrailRenderer>();
        if (tr != null) tr.enabled = false;

        var ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime  = dotLifetime;
        main.startSize      = dotSize;
        main.startSpeed     = 0f;
        main.startColor     = Color.white;
        main.loop           = true;
        main.playOnAwake    = false;
        main.maxParticles   = 300;

        var em = ps.emission;
        em.rateOverTime     = 0f;
        em.rateOverDistance = dotsPerUnit;

        var shape = ps.shape;
        shape.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            (sizeCurve != null && sizeCurve.keys.Length > 0)
                ? sizeCurve
                : AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = (trailGradient != null && trailGradient.colorKeys.Length > 0)
            ? trailGradient
            : BuildDefaultGradient();

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode   = ParticleSystemRenderMode.Billboard;
        rend.sharedMaterial = DotMaterial();
        rend.sortingOrder = 399; // 미사일 스프라이트(400) 바로 아래

        ps.Play();
    }

    private static Gradient BuildDefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.08f, 0.02f), 0f),    // 빨강
                new GradientColorKey(new Color(1f, 0.50f, 0.05f), 0.45f), // 주황
                new GradientColorKey(new Color(1f, 0.88f, 0.20f), 0.85f), // 노랑
                new GradientColorKey(new Color(1f, 0.88f, 0.20f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(1f,   0f),
                new GradientAlphaKey(0.9f, 0.7f),
                new GradientAlphaKey(0f,   1f),
            }
        );
        return g;
    }

    // 전 인스턴스가 한 재질을 공유한다. 예전엔 Awake마다 Shader.Find + 텍스처 + 재질을 새로 만들었는데,
    // 호밍 미사일은 한 번에 수십 발이 Instantiate되고 **파괴돼도 재질·텍스처는 안 지워져** 판 내내 쌓였다.
    // 재질이 제각각이라 꼬리끼리 배칭도 안 됐다. 색은 파티클 colorOverLifetime이 정하므로 공유해도 그림이 같다.
    private static Material dotMaterial;

    private static Material DotMaterial()
    {
        if (dotMaterial != null) return dotMaterial; // 씬 전환의 UnloadUnusedAssets가 지웠으면 다시 만든다

        // 1×1 흰색 픽셀 + Point 필터 → 화면에서 선명한 정사각형 도트
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Sprites/Default");

        dotMaterial = new Material(shader) { mainTexture = tex };
        return dotMaterial;
    }
}
