using UnityEngine;
using UnityEngine.UI;

// 잠긴 카드 한가운데에 얹는 자물쇠. 캐릭터 선택과 맵 선택이 같은 것을 쓴다
// (8/24 플레이스루: 맵은 실루엣만 있고 자물쇠가 없어 "왜 안 눌리는지"가 안 보였다).
// 전용 그림이 있으면 그걸 쓰고, 없으면 코드로 굽는다 — 도트가 나오면 각 UI의 lockIcon에 꽂기만 하면 된다.
public static class LockBadge
{
    public static void Add(GameObject card, Sprite icon)
    {
        var go = new GameObject("Lock", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(card.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        // 🔴 카드 rect 중앙 ≠ 판 그림 중앙이다. 카드에는 아래쪽에 이름표(`NameBox`)가 딸려 있어
        //    rect가 판보다 아래로 길다 — 캐릭터 카드는 판이 y=+63, 맵 카드는 y=+54에 있다.
        //    rect 중앙(0)에 두면 그만큼 자물쇠가 판 아래로 내려가 붙는다(8/27 빌드 QA "너무 아래에 위치").
        rt.anchoredPosition = new Vector2(0f, PlateCenterY(card.transform));
        rt.sizeDelta = new Vector2(64f, 64f);

        var img = go.GetComponent<Image>();
        img.sprite = icon != null ? icon : Baked();
        img.raycastTarget = false;
        rt.SetAsLastSibling(); // 썸네일 위에 오도록
    }

    // 카드 안에서 "판"으로 볼 자식의 세로 중심(카드 기준). 캐릭터·맵 카드가 둘 다 `Bg`를 쓴다.
    // 못 찾으면 0 — 예전 동작(카드 rect 중앙) 그대로라 새 카드 구조가 와도 자물쇠가 사라지지는 않는다.
    private static float PlateCenterY(Transform card)
    {
        var plate = card.Find("Bg") as RectTransform;
        return plate != null ? plate.anchoredPosition.y : 0f;
    }

    // 16x16 자물쇠. 한 장만 구워 모든 잠긴 카드가 공유한다.
    private static Sprite bakedLock;

    private static Sprite Baked()
    {
        if (bakedLock != null) return bakedLock;

        string[] art =
        {
            "................",
            "................",
            ".....######.....",
            "....##....##....",
            "....##....##....",
            "....##....##....",
            "..############..",
            "..############..",
            "..#####..#####..",
            "..####....####..",
            "..#####..#####..",
            "..############..",
            "..############..",
            "..############..",
            "................",
            "................",
        };

        // 🔴 밝은 색이어야 한다 — 잠긴 카드는 **검은 실루엣**이라, 어두운 자물쇠는 통째로 묻혀 안 보인다
        //    (파인애플 카드가 그랬다. 초상화가 없는 3번 슬롯에서만 우연히 보였다).
        var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var body = new Color(0.93f, 0.92f, 0.98f, 1f);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                tex.SetPixel(x, 15 - y, art[y][x] == '#' ? body : Color.clear); // 배열은 위에서부터, 텍스처는 아래에서부터
        tex.Apply();

        bakedLock = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
        return bakedLock;
    }
}
