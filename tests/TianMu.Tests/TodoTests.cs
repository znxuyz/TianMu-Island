using System.IO;
using TianMu.Core;
using TianMu.Island;
using TianMu.Modules.Todo;

namespace TianMu.Tests;

public class TodoTimeTests
{
    // 2026/11/5（四）14:20
    private static readonly DateTime Now = new(2026, 11, 5, 14, 20, 0);

    [Theory]
    [InlineData("30分", 2026, 11, 5, 14, 50)]
    [InlineData("30 分鐘", 2026, 11, 5, 14, 50)]
    [InlineData("45m", 2026, 11, 5, 15, 5)]
    [InlineData("2小時", 2026, 11, 5, 16, 20)]
    [InlineData("1.5h", 2026, 11, 5, 15, 50)]
    [InlineData("15:30", 2026, 11, 5, 15, 30)]
    [InlineData("１５：３０", 2026, 11, 5, 15, 30)]
    [InlineData("9:00", 2026, 11, 6, 9, 0)]          // 已經過了 → 明天
    [InlineData("下午3點", 2026, 11, 5, 15, 0)]
    [InlineData("下午 3 點半", 2026, 11, 5, 15, 30)]
    [InlineData("晚上8點15分", 2026, 11, 5, 20, 15)]
    [InlineData("今晚8點", 2026, 11, 5, 20, 0)]
    [InlineData("明早9點", 2026, 11, 6, 9, 0)]
    [InlineData("明天 14:00", 2026, 11, 6, 14, 0)]
    [InlineData("後天", 2026, 11, 7, 9, 0)]
    [InlineData("11/20 9:00", 2026, 11, 20, 9, 0)]
    [InlineData("11/2 9:00", 2027, 11, 2, 9, 0)]     // 日期已過 → 明年
    [InlineData("2027/1/3 10:05", 2027, 1, 3, 10, 5)]
    [InlineData("8pm", 2026, 11, 5, 20, 0)]
    public void Parses_common_phrases(string input, int y, int mo, int d, int h, int mi) =>
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), TodoTime.Parse(input, Now));

    [Theory]
    [InlineData("")]
    [InlineData("等一下")]
    [InlineData("25:00")]
    [InlineData("13/1 9:00")]
    [InlineData("2/30")]
    [InlineData("0分")]
    public void Rejects_what_it_cannot_read(string input) => Assert.Null(TodoTime.Parse(input, Now));

    [Fact]
    public void Describes_relative_days()
    {
        Assert.Equal("今天 15:30", TodoTime.Describe(new DateTime(2026, 11, 5, 15, 30, 0), Now));
        Assert.Equal("明天 09:00", TodoTime.Describe(new DateTime(2026, 11, 6, 9, 0, 0), Now));
        Assert.Equal("11/9（一）08:05", TodoTime.Describe(new DateTime(2026, 11, 9, 8, 5, 0), Now));
    }
}

public class TodoStoreTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 14, 20, 0);

    [Fact]
    public void Due_items_are_reported_once_and_in_order()
    {
        var store = new TodoStore();
        var later = store.Add("開會", Now.AddMinutes(-1));
        var first = store.Add("繳費", Now.AddMinutes(-5));
        store.Add("明天的事", Now.AddDays(1));

        var due = store.Due(Now);
        Assert.Equal(new[] { first.Id, later.Id }, due.Select(i => i.Id));

        first.Notified = true;
        Assert.Equal(new[] { later.Id }, store.Due(Now).Select(i => i.Id));
    }

    [Fact]
    public void Snooze_moves_the_reminder_and_rearms_it()
    {
        var store = new TodoStore();
        var item = store.Add("回電話", Now);
        item.Notified = true;

        Assert.True(store.Snooze(item.Id, TimeSpan.FromMinutes(10), Now));
        Assert.Equal(Now.AddMinutes(10), item.DueAt);
        Assert.Empty(store.Due(Now));
        Assert.Single(store.Due(Now.AddMinutes(10)));
    }

    [Fact]
    public void Completed_items_leave_the_list_and_are_pruned_after_a_day()
    {
        var store = new TodoStore();
        var item = store.Add("寄信", Now);
        Assert.True(store.Complete(item.Id, Now));
        Assert.Empty(store.Pending);
        Assert.Empty(store.Due(Now));

        Assert.Equal(0, store.Prune(Now.AddHours(1)));
        Assert.Equal(1, store.Prune(Now.AddDays(2)));
    }

    [Fact]
    public void Survives_a_round_trip_to_disk()
    {
        string file = Path.Combine(Path.GetTempPath(), $"tianmu-todo-{Guid.NewGuid():N}.json");
        try
        {
            var store = new TodoStore();
            store.Add("  記得喝水  ", Now);
            store.Save(file);

            var loaded = TodoStore.Load(file);
            Assert.Equal("記得喝水", Assert.Single(loaded.Items).Text);
            Assert.Equal(Now, loaded.Items[0].DueAt);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Corrupt_file_starts_empty()
    {
        string file = Path.Combine(Path.GetTempPath(), $"tianmu-todo-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, "{ not json");
        try { Assert.Empty(TodoStore.Load(file).Items); }
        finally { File.Delete(file); }
    }
}

public class GlowTests
{
    [Fact]
    public void Glow_color_follows_the_activity()
    {
        Assert.Equal("MiaoJinBrush", IslandWindow.GlowKeyFor(new IslandActivity("m", "t", Priority: ActivityPriority.Normal)));
        Assert.Equal("ZhuShaBrush", IslandWindow.GlowKeyFor(new IslandActivity("m", "t", Priority: ActivityPriority.Critical)));
        Assert.Equal("TianQingBrush", IslandWindow.GlowKeyFor(new IslandActivity("m", "t") { GlowKey = "TianQingBrush" }));
        Assert.Null(IslandWindow.GlowKeyFor(new IslandActivity("m", "t") { GlowKey = "" }));
    }
}
