using System.Runtime.InteropServices;

namespace TianMu.Island.Interop;

/// <summary>
/// Win32 符咒（P/Invoke）集中處。所有直接呼叫 user32.dll 的程式碼都放這裡。
/// </summary>
internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;

    public const long WS_EX_TRANSPARENT = 0x00000020;   // 滑鼠完全穿透
    public const long WS_EX_TOOLWINDOW = 0x00000080;    // 不出現在 Alt+Tab
    public const long WS_EX_LAYERED = 0x00080000;       // 分層視窗（透明背景）
    public const long WS_EX_NOACTIVATE = 0x08000000;    // 點擊時不搶走焦點

    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_DPICHANGED = 0x02E0;

    public static readonly nint HWND_TOPMOST = -1;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    /// <summary>Windows 11：讓自訂邊框的視窗也有系統圓角與深色外框。舊版 Windows 會安靜地忽略。</summary>
    public static void ApplyModernFrame(nint hwnd)
    {
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        int dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    public static long GetExStyle(nint hwnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE) : GetWindowLong32(hwnd, GWL_EXSTYLE);

    public static void SetExStyle(nint hwnd, long style)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_EXSTYLE, (nint)style);
        else SetWindowLong32(hwnd, GWL_EXSTYLE, unchecked((int)style));
    }

    /// <summary>加上或移除指定的延伸樣式旗標。</summary>
    public static void ToggleExStyle(nint hwnd, long flag, bool enable)
    {
        long style = GetExStyle(hwnd);
        SetExStyle(hwnd, enable ? style | flag : style & ~flag);
    }

    /// <summary>重新宣告置頂（某些全螢幕程式或工作列會把我們擠下去）。</summary>
    public static void ReassertTopmost(nint hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
}
