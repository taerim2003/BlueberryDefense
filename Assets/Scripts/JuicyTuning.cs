using System.Reflection;
using UnityEngine;

// UI 손맛의 단일 소스. "빠르고 통통 튀게" — 짧은 시간 + 큰 스케일 대비.
//
// 값이 두 군데로 갈리기 때문에 여기 모았다:
//  · 씬에 배치된 버튼 → 인스펙터에 직렬화된 JuicyButton 필드 (에디터 스크립트가 이 상수로 덮는다)
//  · 런타임에 만드는 버튼(OptionsMenu·PauseMenu) → AddComponent 직후 Apply()로 주입
//
// JuicyButton의 파라미터는 전부 private SerializeField라 코드에서 직접 대입할 수 없다.
// 외부 패키지(JuicyUI)는 건드리지 않기로 했으므로 리플렉션으로 주입한다.
public static class JuicyTuning
{
    // 🔴 값의 기준은 **캐릭터 선택 화면의 버튼**이다(세션41, 사용자 지정).
    //    예전엔 여기 값이 인게임 세트(1.15/1.3/0.88 + 흔들림)라 화면마다 손맛이 갈렸다.
    public const float HoverScale = 1.06f;
    public const float SquashX = 1f;
    public const float SquashDuration = 0.02f;
    public const float HoverDuration = 0.09f;
    public const float HoverReturnDuration = 0.1f;
    public const float PressScale = 0.95f;
    public const float PressDuration = 0.03f;

    // 비호버·비선택은 작고 어둡게(세션38). 선택 화면이 쓰는 값 그대로.
    public const float IdleScale = 0.92f;
    public const float DimDuration = 0.15f;

    // 호버할 때 살짝 기울었다가 돌아오는 각도. **선택 화면은 이걸 안 쓴다** — 흔들림을 끈다.
    // (JuicyButton은 visualRoot가 있어야 회전을 켠다. 값은 남겨두되 shakeOnHover=false로 잠근다.)
    public const float ShakeStrength = 7f;
    public const bool ShakeOnHover = false;

    // 패널 열고 닫힘(UITransition.duration). 딤이 반투명한 전체화면 패널은 페이드만 쓴다.
    public const float PanelDuration = 0.13f;      // 타이틀 전체화면 패널(페이드)
    public const float ModalDuration = 0.2f;       // 안쪽 창이 팝하는 인게임 모달

    private static readonly string[] FieldNames =
        { "hoverScale", "squashX", "squashDuration", "hoverDuration", "hoverReturnDuration",
          "pressScale", "pressDuration", "idleScale", "dimDuration" };

    private static readonly float[] Values =
        { HoverScale, SquashX, SquashDuration, HoverDuration, HoverReturnDuration,
          PressScale, PressDuration, IdleScale, DimDuration };

    public static void Apply(JuicyButton button)
    {
        if (button == null) return;
        var type = typeof(JuicyButton);
        for (int i = 0; i < FieldNames.Length; i++)
        {
            var f = type.GetField(FieldNames[i], BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(button, Values[i]);
        }

        // visualRoot는 자기 자신 — 선택 화면 버튼들이 그렇게 돼 있다.
        // ⚠️ `PointerInsideBaseRect`가 RestScale(idle 크기)로 판정하는 것과 짝이다(세션38). 지우지 말 것.
        Set(type, button, "visualRoot", button.GetComponent<RectTransform>());
        Set(type, button, "shakeOnHover", ShakeOnHover);
        Set(type, button, "shakeStrength", ShakeStrength);
        Set(type, button, "idleDim", true);
    }

    private static void Set(System.Type type, JuicyButton button, string field, object value)
    {
        var f = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) f.SetValue(button, value);
    }

    // 런타임 생성 버튼용 — AddComponent + 손맛 주입을 한 번에.
    public static JuicyButton Attach(GameObject go)
    {
        var jb = go.AddComponent<JuicyButton>();
        Apply(jb);
        return jb;
    }

    // JuicyButton은 스케일을 pivot 기준으로 키운다. pivot이 끝(0 또는 1)에 있으면 버튼이
    // 중앙이 아니라 한쪽 끝을 축으로 자라 보인다. 배치가 끝난 뒤 pivot만 중앙으로 옮긴다.
    // ⚠️ 배치 함수(Bottom/Anchored 등)가 pivot을 덮으므로 반드시 **배치 다음에** 부를 것.
    public static void CenterPivot(GameObject go)
    {
        if (go == null) return;
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) return;

        Vector2 delta = new Vector2(0.5f, 0.5f) - rt.pivot;
        if (delta == Vector2.zero) return;

        // anchorMin==anchorMax(한 점 앵커)일 때만 pivot이 화면 위치를 정한다 —
        // 그 경우에만 anchoredPosition으로 되밀어 보이는 위치를 유지한다.
        // stretch 앵커·레이아웃 자식은 pivot이 위치에 관여하지 않으므로 보정하지 않는다.
        bool pointAnchor = rt.anchorMin == rt.anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        if (pointAnchor)
            rt.anchoredPosition += new Vector2(delta.x * rt.rect.width, delta.y * rt.rect.height);
    }
}
