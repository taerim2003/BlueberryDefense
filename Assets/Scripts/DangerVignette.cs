using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// 체력이 위험 구간에 들어가면 화면 가장자리가 붉게 점멸한다.
// 씬에 없는 요소라 HUDController가 런타임에 만든다(스테이지 배너·정수 텍스트와 같은 방식).
//
// 그림은 코드로 굽는다 — 부드러운 그라데이션 한 장뿐이라 아트를 기다릴 이유가 없고,
// 사용자 아트가 나오면 vignetteSprite 대입 한 줄만 갈아끼우면 된다.
public class DangerVignette : MonoBehaviour
{
    public const float DangerRatio = 0.3f;   // 최대 체력 대비 이 아래로 떨어지면 켜진다

    // 2026-09-14 사용자 요청: 너무 세서 눈이 아프다 → 가장자리에만 약하게, 느리게.
    // (예전 값: 알파 0.12~0.5 · 반주기 0.55초 · 화면 중심에서 55% 지점부터 붉어짐)
    private const float PulseMinAlpha = 0.05f;
    private const float PulseMaxAlpha = 0.22f;
    private const float PulseHalfCycle = 0.9f;
    private const float BandHeightRatio = 0.1f; // 붉은 띠 두께 = 화면 높이의 10%(1080p에서 108px). 네 변 모두 같은 두께

    private static readonly Color DangerColor = new Color(0.85f, 0.05f, 0.08f);

    private Image image;
    private Tween pulse;
    private bool on;

    public static DangerVignette Create(Canvas canvas)
    {
        var go = new GameObject("DangerVignette", typeof(RectTransform), typeof(Image));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling(); // HUD 위에 덮는다

        var v = go.AddComponent<DangerVignette>();
        v.image = go.GetComponent<Image>();
        v.image.sprite = Baked();
        v.image.raycastTarget = false; // 화면 전체를 덮으므로 이걸 빼먹으면 HUD 클릭이 통째로 막힌다
        v.image.color = new Color(DangerColor.r, DangerColor.g, DangerColor.b, 0f);
        return v;
    }

    public void SetDanger(bool danger)
    {
        if (danger == on) return;
        on = danger;

        pulse?.Kill();
        if (!danger)
        {
            image.color = new Color(DangerColor.r, DangerColor.g, DangerColor.b, 0f);
            return;
        }

        image.color = new Color(DangerColor.r, DangerColor.g, DangerColor.b, PulseMinAlpha);
        // 레벨업 등으로 timeScale=0이 되어도 계속 뛰게 unscaled로 돈다(멈추면 "굳은 붉은 화면"이 된다).
        pulse = image.DOFade(PulseMaxAlpha, PulseHalfCycle)
                     .SetLoops(-1, LoopType.Yoyo)
                     .SetEase(Ease.InOutSine)
                     .SetUpdate(true);
    }

    private void OnDestroy() => pulse?.Kill();

    // 중앙은 완전히 투명하고 **네 변에서 같은 두께**만큼만 짙어지는 한 장.
    // 🔴 정사각형으로 구워 화면에 늘리면 좌우 띠가 위아래보다 16/9배 두꺼워진다 — 그래서 화면 비율대로 굽고,
    //    거리는 "가장 가까운 변까지의 픽셀 거리 ÷ 높이"로 잰다(좌우·상하가 같은 자로 재진다).
    // ⚠️ 원형·초타원 거리로 굽던 옛 방식은 띠가 화면 안쪽 45%까지 들어와 플레이 영역을 덮었다.
    private static Sprite bakedVignette;
    private static Sprite Baked()
    {
        if (bakedVignette != null) return bakedVignette;

        const int width = 256;
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        int height = Mathf.Max(16, Mathf.RoundToInt(width / aspect));
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[width * height];
        float band = BandHeightRatio * height;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float edge = Mathf.Min(Mathf.Min(x, width - 1 - x), Mathf.Min(y, height - 1 - y)); // 가장 가까운 변까지(px)
                float t = 1f - Mathf.Clamp01(edge / band);   // 변 = 1, 띠 안쪽 끝 = 0
                px[y * width + x] = new Color(1f, 1f, 1f, t * t); // 제곱 — 안쪽으로 갈수록 빨리 옅어진다
            }
        tex.SetPixels(px);
        tex.Apply();

        bakedVignette = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        return bakedVignette;
    }
}
