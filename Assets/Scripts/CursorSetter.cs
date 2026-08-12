using UnityEngine;

// 손그림 마우스 커서로 바꾼다. Cursor.SetCursor는 전역이라 한 번만 걸면 씬을 넘어가도 유지된다.
// (타이틀 씬에 붙여 두면 게임 씬까지 그대로 따라간다.)
public class CursorSetter : MonoBehaviour
{
    [SerializeField] private Texture2D cursorTexture;
    [SerializeField] private Vector2 hotspot = new Vector2(6f, 20f); // 화살표 뾰족한 끝(텍스처 좌상단 기준 px)

    // 커서는 텍스처 원본 크기 그대로 그려진다 — 키우려면 그림 자체를 확대해 넘겨야 한다.
    // 도트가 뭉개지지 않게 **정수 배수**로만 늘린다(최근접 이웃).
    [SerializeField, Range(1, 4)] private int pixelScale = 2;

    private Texture2D scaled;

    private void Awake()
    {
        if (cursorTexture == null) return;

        Texture2D tex = cursorTexture;
        Vector2 spot = hotspot;
        if (pixelScale > 1)
        {
            scaled = Upscale(cursorTexture, pixelScale);
            if (scaled != null) { tex = scaled; spot = hotspot * pixelScale; }
        }
        Cursor.SetCursor(tex, spot, CursorMode.Auto);
    }

    private void OnDestroy()
    {
        // 에디터에서 플레이를 멈췄을 때 기본 커서로 돌려놓지 않으면 그대로 남는다.
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        if (scaled != null) Destroy(scaled);
    }

    // 최근접 이웃 확대. 임포트 설정에서 Read/Write가 꺼져 있으면 픽셀을 못 읽으므로 원본을 그대로 쓴다.
    private static Texture2D Upscale(Texture2D src, int scale)
    {
        if (!src.isReadable) return null;

        var srcPixels = src.GetPixels32();
        int w = src.width, h = src.height;
        var dst = new Texture2D(w * scale, h * scale, TextureFormat.RGBA32, false);
        var dstPixels = new Color32[w * scale * h * scale];
        for (int y = 0; y < h * scale; y++)
            for (int x = 0; x < w * scale; x++)
                dstPixels[y * w * scale + x] = srcPixels[(y / scale) * w + (x / scale)];
        dst.SetPixels32(dstPixels);
        dst.filterMode = FilterMode.Point;
        dst.Apply();
        return dst;
    }
}
