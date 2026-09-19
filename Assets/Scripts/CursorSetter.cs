using UnityEngine;

// 손그림 마우스 커서로 바꾼다. Cursor.SetCursor는 전역이라 한 번만 걸면 씬을 넘어가도 유지된다.
// (타이틀 씬에 붙여 두면 게임 씬까지 그대로 따라간다.)
//
// ⚠️ 기본 커서로 되돌리는 건 OnDestroy가 아니라 Application.quitting에서 한다.
//    이 컴포넌트는 타이틀 씬의 Controllers에만 붙어 있어서 게임 씬으로 넘어갈 때 같이 파괴된다 —
//    OnDestroy에서 되돌리면 "게임 시작하면 커서가 기본으로 돌아가는" 증상이 된다.
public class CursorSetter : MonoBehaviour
{
    [SerializeField] private Texture2D cursorTexture;
    [SerializeField] private Vector2 hotspot = new Vector2(6f, 20f); // 화살표 뾰족한 끝(텍스처 좌상단 기준 px)

    // 도트가 뭉개지지 않게 **정수 배수**로만 늘린다(최근접 이웃).
    // ⚠️ 2026-09-19 실측 — **이 값은 화면 커서 크기를 바꾸지 못한다.** Windows가 하드웨어 커서를 32x32로 줄인다
    //    (SM_CXCURSOR=32 · 256x256을 넘겼는데 GetIconInfo 비트맵이 32x32 · hotspot도 44,48 -> 6,6으로 축소).
    //    실제로 키우려면 CursorMode.ForceSoftware로 가야 한다 — 쓸지는 미결정(HANDOFF 참고).
    [SerializeField, Range(1, 6)] private int pixelScale = 3;

    // 확대 결과는 씬을 넘어 살아남아야 한다 — SetCursor는 텍스처를 참조로 들고 있어서,
    // 씬 전환 때 파괴하면 커서가 다시 그려지는 시점에 깨진다. static으로 들고 재사용한다.
    private static Texture2D scaled;
    private static Texture2D scaledSource;
    private static int scaledScale;

    private void Awake()
    {
        if (cursorTexture == null) return;

        Texture2D tex = cursorTexture;
        Vector2 spot = hotspot;
        if (pixelScale > 1)
        {
            if (scaled == null || scaledSource != cursorTexture || scaledScale != pixelScale)
            {
                if (scaled != null) Destroy(scaled);
                scaled = Upscale(cursorTexture, pixelScale);
                scaledSource = cursorTexture;
                scaledScale = pixelScale;
            }
            if (scaled != null) { tex = scaled; spot = hotspot * pixelScale; }
        }
        Cursor.SetCursor(tex, spot, CursorMode.Auto);

        Application.quitting -= Restore; // 타이틀로 되돌아와 Awake가 다시 돌아도 중복 구독하지 않게
        Application.quitting += Restore;
    }

    // 에디터에서 플레이를 멈췄을 때 기본 커서로 돌려놓지 않으면 그대로 남는다.
    private static void Restore()
    {
        Application.quitting -= Restore;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        if (scaled != null) { Destroy(scaled); scaled = null; }
        scaledSource = null;
        scaledScale = 0;
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
        dst.hideFlags = HideFlags.HideAndDontSave; // 씬 언로드 때 UnloadUnusedAssets에 쓸려 나가지 않게
        dst.filterMode = FilterMode.Point;
        dst.Apply();
        return dst;
    }
}
