using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 씬 UI와 코드가 같은 옷(폰트·머티리얼·강조색)을 쓰게 하는 창구.
// 스프라이트·색의 원본은 에디터 도구 `Window > Blueberry Defense > UI 스킨 적용`(UISkinApply)의 표이고,
// 이 에셋은 그것을 런타임에서 집을 수 있게 담아둔 것이다.
//
// 🔴 에셋이 있어야 옷이 입혀진다: `Window > Blueberry Defense > UI 스킨 에셋 만들기`로
//    `Assets/Resources/UISkin.asset`을 굽는다(경로·파일명이 곧 배선 — SfxLibrary와 같은 방식).
//    없으면 조용히 무시하고 호출측이 이미 칠해둔 모양이 남는다.
//
// ⚠️ 판·바·박스 스프라이트를 런타임에 입히던 API는 전부 없앴다 — 설정·일시정지·컬렉션이
//    프리팹 주도로 바뀌면서 읽는 코드가 사라졌다. 새 UI는 CLAUDE.md §5-1대로 씬/프리팹에서
//    Simple + Set Native Size로 짓는다. 여기 남은 건 글자(Text)·강조색(Highlight)뿐이다.
public class UISkin : ScriptableObject
{
    [Header("바탕 스프라이트")]
    public Sprite panel;    // 큰 판
    public Sprite bar;      // 가로 바·버튼
    public Sprite box;      // 정사각 박스
    public Sprite barWide;  // 아주 넓은 바 — bar를 원본보다 늘리지 않고 쓰기 위한 한 단계 위
    public Sprite iconBox;  // 아이콘 한 칸
    public Sprite bigBox;   // 개큰네모 721x289 — 여러 행을 묶는 그룹 상자

    [Header("게이지 3겹 (바탕 → 채움 → 테두리 순으로 겹친다)")]
    public Sprite gaugeTrack;  // 체력바_색칠  387x101
    public Sprite gaugeFill;   // 체력바_내용물
    public Sprite gaugeOuter;  // 체력바_투명 — 채움 위에 덮어 테두리를 살린다

    [Header("색")]
    public Color skin = new Color(0.420f, 0.482f, 0.910f, 1f);      // #6B7BE8
    public Color dim = new Color(0.031f, 0.020f, 0.051f, 0.8f);     // #08050D
    public Color highlight = new Color(1f, 0.878f, 0.302f, 1f);     // #FFE04D

    [Header("글자")]
    public TMP_FontAsset pixelFont;   // 제목·버튼·수치
    public TMP_FontAsset bodyFont;    // 설명문
    public Material pixelBig, pixelSmall;
    public Material bodyBig, bodySmall;

    public const int SmallCut = 23;   // 이 크기 미만이면 Small 머티리얼

    private static UISkin cached;
    private static bool searched;

    public static UISkin Instance
    {
        get
        {
            if (cached == null && !searched)
            {
                searched = true;
                cached = Resources.Load<UISkin>("UISkin");
            }
            return cached;
        }
    }

    // 선택 표시용 강조색(#FFE04D). 카드 뒤에 까는 노란 테를 켤 때 쓴다 — 에셋이 없어도 같은 노랑으로 폴백한다.
    public static Color Highlight => Instance != null ? Instance.highlight : new Color(1f, 0.878f, 0.302f, 1f);

    // Highlight의 짝 — 꺼진 상태. 오브젝트를 껐다 켜는 대신 알파만 0으로 두면 레이아웃이 안 흔들린다.
    public static readonly Color Transparent = new Color(1f, 1f, 1f, 0f);

    public static void Text(TMP_Text t, bool body)
    {
        var s = Instance;
        if (s == null || t == null) return;

        var font = body ? s.bodyFont : s.pixelFont;
        if (font != null) t.font = font;

        Material mat = t.fontSize >= SmallCut
            ? (body ? s.bodyBig : s.pixelBig)
            : (body ? s.bodySmall : s.pixelSmall);
        if (mat != null) t.fontSharedMaterial = mat;
    }

    // 9-slice 모서리는 원본 픽셀 크기로 그려진다 — 칸이 좁으면 모서리끼리 만나 뭉갠다.
    // 테두리 합이 칸의 60%를 넘지 않도록 배율을 키운다(배율↑ = 모서리가 작게 그려짐).
    // ⚠️ 지금 이걸 부르는 건 에디터 도구(UISkinApply)뿐이다. 런타임에서 다시 쓸 일이 생기면
    //    두 벌로 만들지 말고 이 함수를 부를 것 — 씬과 런타임의 테두리 두께가 갈리면 안 된다.
    public const float BorderBudget = 0.6f;

    public static void FitSlice(Image img)
    {
        img.type = Image.Type.Sliced;
        img.preserveAspect = false;

        Vector4 b = img.sprite != null ? img.sprite.border : Vector4.zero;
        if (b == Vector4.zero) { img.type = Image.Type.Simple; img.pixelsPerUnitMultiplier = 1f; return; }

        Vector2 size = img.rectTransform.rect.size;
        float mx = size.x > 1f ? (b.x + b.z) / (BorderBudget * size.x) : 1f;
        float my = size.y > 1f ? (b.y + b.w) / (BorderBudget * size.y) : 1f;
        img.pixelsPerUnitMultiplier = Mathf.Max(1f, mx, my);
    }
}
