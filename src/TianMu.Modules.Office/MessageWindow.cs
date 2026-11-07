using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TianMu.Modules.Office;

/// <summary>
/// 看不見的訊息視窗：接收「剪貼簿有變動」與全域快捷鍵（必須在 UI 執行緒建立）。
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int WM_HOTKEY = 0x0312;
    private static readonly nint HWND_MESSAGE = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _hotkeys = [];
    private bool _listening;

    public event Action? ClipboardChanged;

    public MessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("TianMuOffice") { ParentWindow = HWND_MESSAGE, Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    public bool ListenClipboard() => _listening = AddClipboardFormatListener(_source.Handle);

    /// <summary>註冊快捷鍵；被別的程式佔用時回傳 false。</summary>
    public bool RegisterHotkey(int id, uint modifiers, uint key, Action action)
    {
        if (!RegisterHotKey(_source.Handle, id, modifiers | 0x4000 /* MOD_NOREPEAT */, key)) return false;
        _hotkeys[id] = action;
        return true;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            ClipboardChanged?.Invoke();
            handled = true;
        }
        else if (msg == WM_HOTKEY && _hotkeys.TryGetValue((int)wParam, out var action))
        {
            action();
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        if (_listening) RemoveClipboardFormatListener(_source.Handle);
        foreach (int id in _hotkeys.Keys) UnregisterHotKey(_source.Handle, id);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
