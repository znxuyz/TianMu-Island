using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace TianMu.Modules.Sense.Watchers;

/// <summary>某一顆磁碟此刻的樣子。</summary>
public sealed record DriveSnapshot(string Name, string Label, long FreeBytes, long TotalBytes)
{
    /// <summary>「E:」。</summary>
    public string Letter => Name.TrimEnd('\\');
}

/// <summary>磁碟清單的差異（純邏輯，方便測試）。</summary>
public static class DriveDiff
{
    public static (List<DriveSnapshot> Added, List<DriveSnapshot> Removed) Compare(
        IReadOnlyDictionary<string, DriveSnapshot> before,
        IReadOnlyDictionary<string, DriveSnapshot> after)
    {
        var added = after.Where(kv => !before.ContainsKey(kv.Key)).Select(kv => kv.Value).OrderBy(d => d.Name).ToList();
        var removed = before.Where(kv => !after.ContainsKey(kv.Key)).Select(kv => kv.Value).OrderBy(d => d.Name).ToList();
        return (added, removed);
    }

    /// <summary>「12.3 GB」、「850 MB」。</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        string format = value >= 100 || unit == 0 ? "0" : "0.0";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + units[unit];
    }
}

/// <summary>
/// 每 2 秒比對一次磁碟清單，偵測隨身碟、行動硬碟的插入與移除。
/// 啟動時已存在的磁碟不會觸發通知；網路磁碟與光碟機不列入。
/// </summary>
internal sealed class DriveWatcher : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Timer _timer;
    private Dictionary<string, DriveSnapshot>? _known;
    private int _busy;

    public DriveWatcher()
    {
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, Interval);
    }

    /// <summary>磁碟插入（在背景執行緒觸發）。</summary>
    public event Action<DriveSnapshot>? Arrived;

    /// <summary>磁碟移除（在背景執行緒觸發）。</summary>
    public event Action<DriveSnapshot>? Removed;

    private void Poll()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var now = Scan();
            if (_known is not null)
            {
                var (added, removed) = DriveDiff.Compare(_known, now);
                foreach (var d in added) Arrived?.Invoke(d);
                foreach (var d in removed) Removed?.Invoke(d);
            }
            _known = now;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[靈識感應] 掃描磁碟失敗：{ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private static Dictionary<string, DriveSnapshot> Scan()
    {
        var result = new Dictionary<string, DriveSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Removable or DriveType.Fixed)) continue;
            if (!drive.IsReady) continue;

            string label;
            long free, total;
            try
            {
                label = drive.VolumeLabel;
                free = drive.AvailableFreeSpace;
                total = drive.TotalSize;
            }
            catch (IOException)
            {
                continue; // 正在拔除
            }

            result[drive.Name] = new DriveSnapshot(drive.Name, label, free, total);
        }
        return result;
    }

    public void Dispose() => _timer.Dispose();
}
