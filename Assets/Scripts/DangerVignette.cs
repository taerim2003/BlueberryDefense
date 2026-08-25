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

    private const float PulseMinAlpha = 0.12f;
    private const float PulseMaxAlpha = 0.5f;
    private const float PulseHalfCycle = 0.55f;

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

    // 중앙은 완전히 투명하고 **네 변 전체**가 짙어지는 한 장.
    // ⚠️ 원형 거리(√(dx²+dy²)/√2)로 구우면 알파 1.0에 닿는 건 네 귀퉁이뿐이고 변 한가운데는 0.45에서 멈춘다
    //    — "가장자리 점멸"이 아니라 "귀퉁이 점멸"이 된다(실제로 그렇게 구웠다가 알파를 재서 잡았다).
    //    그래서 초타원(p=4) 거리를 쓴다: 변 한가운데에서 정확히 1이 되고 모서리는 넘겨서 클램프된다.
    private static Sprite bakedVignette;
    private static Sprite Baked()
    {
        if (bakedVignette != null) return bakedVignette;

        const int size = 128;
        const float inner = 0.55f; // 여기까지는 완전 투명 — 화면 한가운데(플레이 영역)를 가리면 안 된다
        const float power = 4f;    // 클수록 사각형에 가깝다(2면 원, ∞면 정사각)
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // 중심(0) → 각 변(1)으로 정규화한 거리
                float dx = Mathf.Abs(x / (size - 1f) * 2f - 1f);
                float dy = Mathf.Abs(y / (size - 1f) * 2f - 1f);
                float d = Mathf.Pow(Mathf.Pow(dx, power) + Mathf.Pow(dy, power), 1f / power);
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, 1f, d)));
            }
        tex.SetPixels(px);
        tex.Apply();

        bakedVignette = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return bakedVignette;
    }
}
