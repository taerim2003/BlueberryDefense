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
    public const float HoverScale = 1.15f;
    public const float SquashX = 1.3f;
    public const float SquashDuration = 0.05f;
    public const float HoverDuration = 0.08f;
    public const float PressScale = 0.88f;
    public const float PressDuration = 0.04f;

    // 호버할 때 살짝 기울었다가 돌아오는 각도. JuicyButton은 visualRoot가 있어야 회전을 켠다
    // (자식을 돌려야 히트박스가 안 틀어진다는 이유) — 우리는 래퍼를 새로 끼우는 대신
    // visualRoot에 **버튼 자신의 RectTransform**을 넣어 켠다. 기울기가 squash 동안만이고
    // 곧바로 원각도로 돌아와서, 히트박스가 기운 채로 머무르지 않는다.
    public const float ShakeStrength = 7f;

    // 패널 열고 닫힘(UITransition.duration). 딤이 반투명한 전체화면 패널은 페이드만 쓴다.
    public const float PanelDuration = 0.13f;      // 타이틀 전체화면 패널(페이드)
    public const float ModalDuration = 0.2f;       // 안쪽 창이 팝하는 인게임 모달

    private static readonly string[] FieldNames =
        { "hoverScale", "squashX", "squashDuration", "hoverDuration", "pressScale", "pressDuration" };

    private static readonly float[] Values =
        { HoverScale, SquashX, SquashDuration, HoverDuration, PressScale, PressDuration };

    public static void Apply(JuicyButton button)
    {
        if (button == null) return;
        var type = typeof(JuicyButton);
        for (int i = 0; i < FieldNames.Length; i++)
        {
            var f = type.GetField(FieldNames[i], BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(button, Values[i]);
        }

        // 회전(비틀기)은 visualRoot가 있어야 켜진다 — 자기 자신을 넣어 구조 변경 없이 활성화.
        Set(type, button, "visualRoot", button.GetComponent<RectTransform>());
        Set(type, button, "shakeOnHover", true);
        Set(type, button, "shakeStrength", ShakeStrength);
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
}
