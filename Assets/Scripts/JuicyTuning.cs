using System.Reflection;
using UnityEngine;

// UI 손맛의 단일 소스. "빠르고 통통 튀게" — 짧은 시간 + 큰 스케일 대비.
//
// 값이 두 군데로 갈리기 때문에 여기 모았다:
//  · 씬·프리팹에 배치된 버튼 → 인스펙터에 직렬화된 JuicyButton 필드. 에디터 도구
//    (`Window > Blueberry Defense > UI 스킨 적용`)가 Apply()로 이 상수를 덮는다.
//  · 코드가 직접 도는 트윈 → 상수만 읽어 쓴다(SkillTreeUI의 노드 호버).
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

}
