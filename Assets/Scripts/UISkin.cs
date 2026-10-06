using UnityEngine;
using UnityEngine.UI;

// 씬 UI와 코드가 같은 옷(강조색·선택 테 머티리얼)을 쓰게 하는 창구.
// 색의 원본은 에디터 도구 `Window > Blueberry Defense > UI 스킨 적용`(UISkinApply)의 표이고,
// 이 에셋은 그것을 런타임에서 집을 수 있게 담아둔 것이다.
//
// 🔴 에셋이 있어야 옷이 입혀진다: `Window > Blueberry Defense > UI 스킨 에셋 만들기`로
//    `Assets/Resources/UISkin.asset`을 굽는다(경로·파일명이 곧 배선 — SfxLibrary와 같은 방식).
//    없으면 조용히 무시하고 호출측이 이미 칠해둔 모양이 남는다.
//
// ⚠️ 판·바·박스 스프라이트와 폰트·머티리얼을 런타임에 입히던 API·필드는 전부 없앴다(2026-09-08 · 2026-09-29) —
//    설정·일시정지·컬렉션이 프리팹 주도로 바뀌면서 읽는 코드가 사라졌다. 새 UI는 CLAUDE.md §5-1대로 씬/프리팹에서
//    Simple + Set Native Size로 짓는다. 여기 남은 건 강조색(Highlight)·선택 테(SelectOutline)뿐이다.
public class UISkin : ScriptableObject
{
    [Header("색")]
    public Color highlight = new Color(1f, 0.878f, 0.302f, 1f);     // #FFE04D

    // 선택 테를 그리는 머티리얼(UI/SelectOutline). 맵 선택 카드의 `SelectGlow`가 쓰던 것과 **같은 에셋**이고,
    // UIFocusGroup이 키보드 포커스 테를 만들 때 여기서 가져간다 — 화면마다 배선하지 않으려고 강조색 옆에 뒀다.
    // 비어 있으면 테 없이 JuicyButton의 선택 연출만 나간다(진화 창 노드는 Juicy가 없어 표시가 사라진다).
    public Material selectOutline;

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

    // 키보드/패드 포커스 테의 머티리얼. UIFocusGroup이 SelectGlow를 만들 때 쓴다.
    public static Material SelectOutline => Instance != null ? Instance.selectOutline : null;

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
