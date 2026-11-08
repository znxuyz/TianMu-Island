using System.Runtime.InteropServices;

namespace TianMu.Modules.Cleanse;

/// <summary>資源回收筒：查詢佔用、清空（Windows Shell API）。</summary>
internal static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(nint hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;

    /// <summary>把檔案或資料夾移到資源回收筒（可以從回收筒找回來）。不跳任何視窗。</summary>
    public static bool Send(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0",            // 清單要以兩個 \0 結尾（字串封送會再補一個）
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    /// <summary>所有磁碟的資源回收筒合計（位元組、項目數）；查不到時回傳 (0, 0)。</summary>
    public static (long Bytes, long Count) Query()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
        }
        catch { return (0, 0); }
    }

    /// <summary>清空所有磁碟的資源回收筒（不跳確認框；確認已在審查畫面做過）。</summary>
    public static bool Empty()
    {
        try
        {
            int hr = SHEmptyRecycleBin(0, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            return hr == 0 || hr == unchecked((int)0x8000FFFF); // 已經是空的時會回 E_UNEXPECTED
        }
        catch { return false; }
    }
}
