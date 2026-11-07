using TianMu.Modules.Office;

namespace TianMu.Tests;

public class ClipHistoryTests
{
    private static ClipEntry Text(string s) => new() { Kind = ClipKind.Text, Text = s };

    [Fact]
    public void Keeps_only_the_latest_ten()
    {
        var h = new ClipHistory();
        for (int i = 1; i <= 13; i++) h.Add(Text($"第 {i} 筆"));
        Assert.Equal(ClipHistory.Capacity, h.Items.Count);
        Assert.Equal("第 13 筆", h.Items[0].Text);
        Assert.Equal("第 4 筆", h.Items[^1].Text);
    }

    [Fact]
    public void Copying_the_same_thing_again_moves_it_to_the_top()
    {
        var h = new ClipHistory();
        h.Add(Text("甲"));
        h.Add(Text("乙"));
        Assert.True(h.Add(Text("甲")));
        Assert.Equal(new[] { "甲", "乙" }, h.Items.Select(i => i.Text));
        Assert.False(h.Add(Text("甲")));        // 已經是最新的
        Assert.False(h.Add(Text("   ")));       // 空白不記
    }

    [Fact]
    public void Pinned_items_survive_being_pushed_out_and_clearing()
    {
        var h = new ClipHistory();
        h.Add(Text("重要"));
        h.TogglePin(h.Items[0].Id);
        for (int i = 0; i < 20; i++) h.Add(Text($"雜 {i}"));
        Assert.Contains(h.Items, e => e.Text == "重要" && e.Pinned);
        Assert.Equal(ClipHistory.Capacity, h.Items.Count);

        h.ClearUnpinned();
        Assert.Equal("重要", Assert.Single(h.Items).Text);
    }

    [Fact]
    public void Preview_collapses_whitespace_and_truncates()
    {
        Assert.Equal("第一行 第二行", ClipHistory.Preview("  第一行\r\n\t第二行  "));
        Assert.Equal(new string('字', 10) + "…", ClipHistory.Preview(new string('字', 30), max: 10));
    }
}

public class OcrTextTests
{
    [Fact]
    public void Spaces_between_chinese_characters_are_removed_but_english_keeps_them()
    {
        Assert.Equal("天幕島 TianMu Island 很好用。", OcrText.Join(["天 幕 島 TianMu Island 很 好 用 。"]));
        Assert.Equal("第一行" + Environment.NewLine + "Second line", OcrText.Join(["第 一 行", " Second line ", "  "]));
    }

    [Fact]
    public void Counts_chinese_characters_and_english_words()
    {
        Assert.Equal(5, OcrText.CountWords("天幕島 hello world"));
        Assert.Equal(0, OcrText.CountWords("  "));
    }
}
