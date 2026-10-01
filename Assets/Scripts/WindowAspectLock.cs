#if UNITY_STANDALONE_WIN && !UNITY_EDITOR && !BOT_QA
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

// 빌드 창을 사용자가 자유롭게 늘이고 줄이되 **화면 비율은 16:9로 고정**한다.
// Unity엔 창 비율을 묶는 설정이 없어서(PlayerSettings는 '크기 조절 가능'까지만) Win32 창 프로시저를 가로챈다.
// - WM_SIZING: 테두리를 끄는 동안 — 잡은 변·모서리에 맞춰 반대 축을 따라오게 한다.
// - WM_WINDOWPOSCHANGING: 최대화·스냅·SetResolution — 제안된 사각형 안에 16:9로 맞춰 가운데 둔다.
// 전체화면(테두리 없음)과 모니터를 덮는 크기는 건드리지 않는다 — 16:10 모니터의 전체화면을 줄이면 안 된다.
// ⚠️ 콜백은 Unity API를 부르지 않는다(창 스레드에서 불릴 수 있다). Win32만 쓴다.
// QA 빌드(BOT_QA)는 뺀다 — BotChaos가 640×480으로 일부러 비율을 깨서 UI를 시험한다.
public static class WindowAspectLock
{
    private const int AspectW = 16, AspectH = 9;

    private const int GWLP_WNDPROC = -4, GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const uint WM_SIZING = 0x0214, WM_WINDOWPOSCHANGING = 0x0046;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002;
    private const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4,
                      WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int idx, IntPtr v);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern IntPtr SetWindowLong32(IntPtr hWnd, int idx, IntPtr v);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int idx);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern IntPtr GetWindowLong32(IntPtr hWnd, int idx);
    [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRectExForDpi(ref RECT r, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

    private static IntPtr hwnd, prevProc;
    private static WndProc proc; // GC가 대리자를 거둬 가면 창이 죽는다 — static으로 붙잡아 둔다.

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (hwnd != IntPtr.Zero) return;
        hwnd = FindUnityWindow();
        if (hwnd == IntPtr.Zero) return; // -batchmode 등 창이 없는 실행

        proc = Hook;
        prevProc = SetLong(GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(proc));
    }

    private static uint pid;

    private static IntPtr FindUnityWindow()
    {
        pid = (uint)Process.GetCurrentProcess().Id;
        EnumWindows(MatchWindow, IntPtr.Zero);
        return foundWindow;
    }

    private static IntPtr foundWindow;

    [AOT.MonoPInvokeCallback(typeof(EnumWindowsProc))]
    private static bool MatchWindow(IntPtr h, IntPtr _)
    {
        GetWindowThreadProcessId(h, out uint p);
        if (p != pid) return true;
        var sb = new StringBuilder(64);
        GetClassName(h, sb, sb.Capacity);
        if (sb.ToString() != "UnityWndClass") return true;
        foundWindow = h;
        return false;
    }

    [AOT.MonoPInvokeCallback(typeof(WndProc))]
    private static IntPtr Hook(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
    {
        IntPtr result = CallWindowProc(prevProc, h, msg, wParam, lParam);

        if (msg == WM_SIZING && Windowed())
        {
            var r = Marshal.PtrToStructure<RECT>(lParam);
            OnSizing(ref r, wParam.ToInt32());
            Marshal.StructureToPtr(r, lParam, false);
            return (IntPtr)1;
        }
        if (msg == WM_WINDOWPOSCHANGING && Windowed())
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if (OnPosChanging(ref wp)) Marshal.StructureToPtr(wp, lParam, false);
        }
        return result;
    }

    // 테두리 있는 창 모드인가. 테두리 없는 전체화면 창은 WS_CAPTION이 없다.
    private static bool Windowed() => (Style() & WS_CAPTION) != 0 && !IsIconic(hwnd);

    // 창 사각형 − 클라이언트 사각형 = 테두리·제목줄 두께. 지금 스타일과 DPI로 계산한다
    // (GetWindowRect − GetClientRect는 전체화면→창 전환 중엔 옛 모양을 재서 틀린다).
    private static void Frame(out int fw, out int fh)
    {
        var r = new RECT();
        AdjustWindowRectExForDpi(ref r, Style(), false, (uint)GetLong(GWL_EXSTYLE).ToInt64(), GetDpiForWindow(hwnd));
        fw = r.right - r.left;
        fh = r.bottom - r.top;
    }

    private static void OnSizing(ref RECT r, int edge)
    {
        Frame(out int fw, out int fh);
        int cw = r.right - r.left - fw, ch = r.bottom - r.top - fh;

        if (edge == WMSZ_TOP || edge == WMSZ_BOTTOM)
        {
            // 위·아래 변: 높이가 주인 — 너비를 오른쪽으로 따라오게.
            r.right = r.left + Mathf.RoundToInt(ch * AspectW / (float)AspectH) + fw;
            return;
        }
        // 좌·우 변과 네 모서리: 너비가 주인 — 높이는 잡은 쪽(위/아래)으로 따라온다.
        int newH = Mathf.RoundToInt(cw * AspectH / (float)AspectW) + fh;
        if (edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT) r.top = r.bottom - newH;
        else r.bottom = r.top + newH;
    }

    // 최대화·스냅 등 끌기가 아닌 크기 변경: 제안된 사각형 안에 16:9로 맞추고 그 가운데에 둔다.
    private static bool OnPosChanging(ref WINDOWPOS wp)
    {
        if ((wp.flags & SWP_NOSIZE) != 0) return false;
        if (CoversMonitor(wp)) return false;

        Frame(out int fw, out int fh);
        int cw = wp.cx - fw, ch = wp.cy - fh;
        if (cw <= 0 || ch <= 0) return false;
        if (Mathf.Abs(cw * AspectH - ch * AspectW) <= AspectW) return false; // 이미 맞음(끌기 중엔 WM_SIZING이 맞춰 놓았다)

        int nw = cw, nh = ch;
        if (cw * AspectH > ch * AspectW) nw = Mathf.RoundToInt(ch * AspectW / (float)AspectH);
        else nh = Mathf.RoundToInt(cw * AspectH / (float)AspectW);

        if ((wp.flags & SWP_NOMOVE) == 0)
        {
            wp.x += (cw - nw) / 2;
            wp.y += (ch - nh) / 2;
        }
        wp.cx = nw + fw;
        wp.cy = nh + fh;
        return true;
    }

    // 모니터 전체를 덮는 크기면 전체화면 전환 중이다 — 손대지 않는다.
    private static bool CoversMonitor(WINDOWPOS wp)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2 /*MONITOR_DEFAULTTONEAREST*/), ref mi)) return false;
        return wp.cx >= mi.rcMonitor.right - mi.rcMonitor.left && wp.cy >= mi.rcMonitor.bottom - mi.rcMonitor.top;
    }

    private static uint Style() => (uint)GetLong(GWL_STYLE).ToInt64();
    private static IntPtr GetLong(int idx) => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, idx) : GetWindowLong32(hwnd, idx);
    private static IntPtr SetLong(int idx, IntPtr v) => IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, idx, v) : SetWindowLong32(hwnd, idx, v);
}
#endif
