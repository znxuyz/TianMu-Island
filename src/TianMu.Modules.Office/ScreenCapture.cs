using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace TianMu.Modules.Office;

/// <summary>擷取游標所在的那一個螢幕（實際像素，用 GDI）。</summary>
internal static class ScreenCapture
{
    public sealed record Shot(BitmapSource Image, Int32Rect Bounds);

    public static Shot? CaptureMonitorUnderCursor()
    {
        if (!GetCursorPos(out var cursor)) return null;
        nint monitor = MonitorFromPoint(cursor, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;
        var r = info.rcMonitor;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;

        nint screenDc = GetDC(0);
        nint memDc = CreateCompatibleDC(screenDc);
        nint bitmap = CreateCompatibleBitmap(screenDc, w, h);
        nint old = SelectObject(memDc, bitmap);
        try
        {
            BitBlt(memDc, 0, 0, w, h, screenDc, r.Left, r.Top, 0x00CC0020 /* SRCCOPY */ | 0x40000000 /* CAPTUREBLT */);
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return new Shot(image, new Int32Rect(r.Left, r.Top, w, h));
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(0, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint dest, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
