using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 런타임에 코드로 짓는 UI(설정·일시정지)가 씬 UI와 같은 옷을 입게 하는 창구.
// 씬 쪽은 에디터 도구 `Window > Blueberry Defense > UI 스킨 적용`이 같은 값을 밀어 넣는다 —
// 스프라이트·색은 그쪽 표(UISkinApply)가 원본이고, 이 에셋은 그것을 런타임에서 집을 수 있게 담아둔 것이다.
//
// 🔴 에셋이 있어야 옷이 입혀진다: `Window > Blueberry Defense > UI 스킨 에셋 만들기`로
//    `Assets/Resources/UISkin.asset`을 굽는다(경로·파일명이 곧 배선 — SfxLibrary와 같은 방식).
//    없으면 조용히 무시하고 예전 프리미티브 모양으로 그린다.
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

    // ── 적용 ── 에셋이 없으면 아무것도 안 한다(호출측이 이미 칠해둔 프리미티브 색이 남는다).
    public static void Panel(Image img) => Apply(img, s => s.panel);
    public static void Bar(Image img) => Apply(img, s => s.bar);
    public static void Box(Image img) => Apply(img, s => s.box);
    public static void BarWide(Image img) => Apply(img, s => s.barWide);
    public static void IconBox(Image img) => Apply(img, s => s.iconBox);
    public static void BigBox(Image img) => Apply(img, s => s.bigBox);

    // 🔴 CLAUDE.md §5-1 — 판은 **원본 크기 그대로 Simple**로 쓴다.
    //    위의 Apply/FitSlice 경로는 Sliced로 늘려 쓰던 시절 것이라 테두리·아래 그림자가 칸 크기에 따라
    //    두꺼워진다. 새로 만드는 UI는 이쪽을 쓸 것. 칸 크기는 그림이 정한다(SetNativeSize).
    public static void Native(Image img, Sprite sp)
    {
        if (img == null || sp == null) return;
        img.sprite = sp;
        img.type = Image.Type.Simple;
        img.preserveAspect = true;
        img.pixelsPerUnitMultiplier = 1f;
        img.SetNativeSize();
    }

    public static void GaugeTrack(Image img) { var s = Instance; if (s != null) Native(img, s.gaugeTrack); }
    public static void GaugeOuter(Image img) { var s = Instance; if (s != null) Native(img, s.gaugeOuter); }
    public static Sprite GaugeFillSprite => Instance != null ? Instance.gaugeFill : null;
    public static Sprite GaugeTrackSprite => Instance != null ? Instance.gaugeTrack : null;

    private static void Apply(Image img, System.Func<UISkin, Sprite> pick)
    {
        var s = Instance;
        if (s == null || img == null) return;
        Sprite sprite = pick(s);
        if (sprite == null) return;
        img.sprite = sprite;
        img.color = s.skin;
        Fit(img);
    }

    // 🔴 CLAUDE.md §5-1 — 판은 Simple + Preserve Aspect로 쓴다.
    // 늘이거나 줄여도 그림 **전체가 균일하게** 스케일되므로 아래 그림자 두께가 원본 비율로 남는다.
    // (Sliced는 테두리를 원본 픽셀 크기로 그려서, 칸이 작아질수록 그림자만 상대적으로 두꺼워진다.)
    // 칸의 가로세로 비가 그림과 다르면 PA가 남는 쪽을 비우므로, 칸 비율을 그림에 맞춰 두는 게 좋다.
    private static void Fit(Image img)
    {
        img.type = Image.Type.Simple;
        img.preserveAspect = true;
        img.pixelsPerUnitMultiplier = 1f;
    }

    // 색을 따로 주고 싶을 때(위험 버튼처럼) — 스프라이트만 스킨을 쓰고 색은 호출측 것을 지킨다.
    public static void BarTinted(Image img, Color tint) => Tinted(img, Instance != null ? Instance.bar : null, tint);
    public static void BoxTinted(Image img, Color tint) => Tinted(img, Instance != null ? Instance.box : null, tint);

    private static void Tinted(Image img, Sprite sprite, Color tint)
    {
        if (img == null || sprite == null) return;
        img.sprite = sprite;
        img.color = tint;
        Fit(img);
    }

    public static void Dim(Image img, float alpha)
    {
        var s = Instance;
        if (s == null || img == null) return;
        img.color = new Color(s.dim.r, s.dim.g, s.dim.b, alpha);
    }

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

    // 코드로 지은 UI는 "스프라이트를 입힌 시점"엔 아직 크기가 안 정해진 경우가 많다
    // (버튼을 만든 뒤에 호출측이 Bottom/Anchored로 크기를 준다). 그래서 다 짓고 나서 한 번 더 재단한다.
    // ⚠️ 이걸 빼면 작은 버튼의 테두리가 칸을 다 먹어 안이 안 보인다.
    // 겸사겸사 가로 바/정사각 스프라이트도 최종 비율을 보고 다시 고른다 — 정사각 칸에 가로 바를 넣으면
    // 손그림 테두리가 찌그러진다(해상도 ◀▶ 버튼처럼 52×56인 것들).
    public const float SquareAspect = 1.6f;

    public static void Refit(GameObject root)
    {
        var s = Instance;
        if (root == null || s == null) return;

        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            if (img.sprite == null || img.type != Image.Type.Sliced) continue;
            if (img.sprite == s.bar || img.sprite == s.box)
            {
                Vector2 size = img.rectTransform.rect.size;
                if (size.y > 1f) img.sprite = size.x / size.y < SquareAspect ? s.box : s.bar;
            }
            FitSlice(img);
        }
    }

    // 9-slice 모서리는 원본 픽셀 크기로 그려진다 — 칸이 좁으면 모서리끼리 만나 뭉갠다.
    // 테두리 합이 칸의 60%를 넘지 않도록 배율을 키운다(배율↑ = 모서리가 작게 그려짐).
    // ⚠️ 에디터 도구(UISkinApply)도 이 함수를 쓴다. 두 벌로 만들면 씬과 런타임의 테두리 두께가 갈린다.
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
