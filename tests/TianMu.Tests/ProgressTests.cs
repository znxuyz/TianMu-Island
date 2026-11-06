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
