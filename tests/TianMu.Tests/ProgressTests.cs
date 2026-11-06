using TianMu.Modules.Sense.Watchers;

namespace TianMu.Tests;

public class TaskbarProgressTests
{
    [Fact]
    public void Picks_the_running_progress_closest_to_done()
    {
        var picked = TaskbarProgressReading.Pick(
        [
            ("檔案總管 - 1 個執行中的視窗", 30, 100),
            ("Google Chrome - 2 running windows", 0.62, 1),
            ("設定", 0, 100),          // 沒在跑
        ]);

        Assert.NotNull(picked);
        Assert.Equal("Google Chrome", picked.App);
        Assert.Equal(0.62, picked.Fraction, 3);
    }

    [Fact]
    public void Empty_and_finished_bars_are_not_progress()
    {
        Assert.Null(TaskbarProgressReading.Pick([("A", 0, 100), ("B", 100, 100), ("C", 5, 0), ("D", double.NaN, 100)]));
        Assert.Null(TaskbarProgressReading.Pick([]));
    }

    [Theory]
    [InlineData("Microsoft Edge - 3 個執行中的視窗", "Microsoft Edge")]
    [InlineData("Steam", "Steam")]
    [InlineData("", "工作列")]
    [InlineData("A very long application name that keeps going", "A very long appli…")]
    public void Cleans_taskbar_button_names(string raw, string expected) =>
        Assert.Equal(expected, TaskbarProgressReading.CleanName(raw));
}

public class SystemMathTests
{
    [Fact]
    public void Cpu_load_uses_kernel_time_that_includes_idle()
    {
        // 這段時間：idle 600、kernel 800（含 idle）、user 200 → 忙碌 400 / 總共 1000
        Assert.Equal(0.4, SystemMath.CpuLoad((1000, 2000, 500), (1600, 2800, 700)), 6);
        Assert.Equal(0, SystemMath.CpuLoad((10, 10, 10), (10, 10, 10)));
        Assert.Equal(0, SystemMath.CpuLoad((100, 100, 100), (50, 50, 50))); // 計數器重置
    }

    [Fact]
    public void Network_rate_is_per_second_and_survives_counter_reset()
    {
        Assert.Equal(512, SystemMath.Rate(1000, 2024, 2));
        Assert.Equal(0, SystemMath.Rate(5000, 10, 2));
        Assert.Equal(0, SystemMath.Rate(0, 100, 0));
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(2048, "2 KB/s")]
    [InlineData(3_565_158, "3.4 MB/s")]
    [InlineData(52_428_800, "50 MB/s")]
    public void Speed_text(long bps, string expected) => Assert.Equal(expected, SystemMath.SpeedText(bps));
}
