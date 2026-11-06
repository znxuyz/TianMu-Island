using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace TianMu.Modules.Sense.Watchers;

/// <summary>一次取樣的結果。</summary>
/// <param name="Cpu">CPU 使用率 0~1。</param>
/// <param name="Memory">記憶體使用率 0~1。</param>
/// <param name="DownBytesPerSecond">下載速度。</param>
/// <param name="UpBytesPerSecond">上傳速度。</param>
public sealed record SystemStats(double Cpu, double Memory, long DownBytesPerSecond, long UpBytesPerSecond);

/// <summary>
/// 讀取 CPU、記憶體與網路流量。全部是 Windows 內建的輕量 API，每次取樣不到一毫秒。
/// 第一次取樣只是建立基準，從第二次開始才有數值。
/// </summary>
public sealed class SystemSampler
{
    private (ulong Idle, ulong Kernel, ulong User)? _lastCpu;
    private (long Received, long Sent, DateTime At)? _lastNet;

    public SystemStats? Sample()
    {
        DateTime now = DateTime.UtcNow;

        double cpu = 0;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var times = (idle.Value, kernel.Value, user.Value);
            if (_lastCpu is { } prev) cpu = SystemMath.CpuLoad(prev, times);
            _lastCpu = times;
        }

        double memory = 0;
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref status)) memory = status.dwMemoryLoad / 100.0;

        var (received, sent) = NetworkTotals();
        long down = 0, up = 0;
        if (_lastNet is { } last)
        {
            double seconds = (now - last.At).TotalSeconds;
            down = SystemMath.Rate(last.Received, received, seconds);
            up = SystemMath.Rate(last.Sent, sent, seconds);
        }
        bool first = _lastNet is null;
        _lastNet = (received, sent, now);

        return first ? null : new SystemStats(cpu, memory, down, up);
    }

    /// <summary>所有實體網路介面（排除迴路與通道）累計收發的位元組數。</summary>
    private static (long Received, long Sent) NetworkTotals()
    {
        long received = 0, sent = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = nic.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
        }
        catch { /* 介面剛好在變動，下次再算 */ }
        return (received, sent);
    }

    // ── Win32 ─────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}

/// <summary>取樣的計算（純數學，方便測試）。</summary>
public static class SystemMath
{
    /// <summary>
    /// 由兩次 GetSystemTimes 算出 CPU 使用率。注意 kernel 時間已經包含 idle。
    /// </summary>
    public static double CpuLoad((ulong Idle, ulong Kernel, ulong User) before, (ulong Idle, ulong Kernel, ulong User) after)
    {
        if (after.Idle < before.Idle || after.Kernel < before.Kernel || after.User < before.User) return 0;
        double idle = after.Idle - before.Idle;
        double total = (after.Kernel - before.Kernel) + (after.User - before.User);
        return total <= 0 ? 0 : Math.Clamp(1 - idle / total, 0, 1);
    }

    /// <summary>每秒位元組數；計數器歸零（網卡重新連線）時回傳 0。</summary>
    public static long Rate(long before, long after, double seconds) =>
        seconds <= 0 || after < before ? 0 : (long)((after - before) / seconds);

    /// <summary>「0 B/s」「512 KB/s」「3.4 MB/s」。</summary>
    public static string SpeedText(long bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024} KB/s";
        double mb = bytesPerSecond / 1024.0 / 1024.0;
        return (mb < 10 ? mb.ToString("0.0", CultureInfo.InvariantCulture) : mb.ToString("0", CultureInfo.InvariantCulture)) + " MB/s";
    }
}
