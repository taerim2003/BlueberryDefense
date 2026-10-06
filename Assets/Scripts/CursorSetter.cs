using UnityEngine;

// 손그림 마우스 커서로 바꾼다. Cursor.SetCursor는 전역이라 한 번만 걸면 씬을 넘어가도 유지된다.
// (타이틀 씬에 붙여 두면 게임 씬까지 그대로 따라간다.)
//
// ⚠️ 기본 커서로 되돌리는 건 OnDestroy가 아니라 Application.quitting에서 한다.
//    이 컴포넌트는 타이틀 씬의 Controllers에만 붙어 있어서 게임 씬으로 넘어갈 때 같이 파괴된다 —
//    OnDestroy에서 되돌리면 "게임 시작하면 커서가 기본으로 돌아가는" 증상이 된다.
//
// ⚠️ 커서를 **키우는 코드는 없다**(2026-09-29에 걷어냈다). 2026-09-19 실측 — Windows가 하드웨어 커서를 32x32로 줄이므로
//    텍스처를 정수 배로 확대해 넘겨도 화면 크기는 그대로였다(SM_CXCURSOR=32 · 256x256을 넘겼는데 GetIconInfo 비트맵이 32x32).
//    실제로 키우려면 CursorMode.ForceSoftware로 가야 한다 — 쓸지는 미결정(HANDOFF 참고).
public class CursorSetter : MonoBehaviour
{
    [SerializeField] private Texture2D cursorTexture;
    [SerializeField] private Vector2 hotspot = new Vector2(6f, 20f); // 화살표 뾰족한 끝(텍스처 좌상단 기준 px)

    private void Awake()
    {
        if (cursorTexture == null) return;

        Cursor.SetCursor(cursorTexture, hotspot, CursorMode.Auto);

        Application.quitting -= Restore; // 타이틀로 되돌아와 Awake가 다시 돌아도 중복 구독하지 않게
        Application.quitting += Restore;
    }

    // 에디터에서 플레이를 멈췄을 때 기본 커서로 돌려놓지 않으면 그대로 남는다.
    private static void Restore()
    {
        Application.quitting -= Restore;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
}
