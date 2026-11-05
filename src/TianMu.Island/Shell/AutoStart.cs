using System.Diagnostics;
using Microsoft.Win32;

namespace TianMu.Island.Shell;

/// <summary>
/// 開機自動啟動：寫入目前使用者的 Run 登錄機碼（不需要系統管理員權限）。
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TianMu-Island";

    /// <summary>目前執行檔的完整路徑。</summary>
    public static string? ExecutablePath => Environment.ProcessPath;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>開啟或關閉開機啟動；成功回傳 true。</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                string? exe = ExecutablePath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, $"\"{exe}\" --autostart");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[天幕] 設定開機啟動失敗：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// exe 被搬移後，登錄檔裡的舊路徑會失效；啟動時呼叫此方法把路徑更新成目前位置。
    /// </summary>
    public static void RefreshPathIfEnabled()
    {
        if (IsEnabled()) SetEnabled(true);
    }
}
