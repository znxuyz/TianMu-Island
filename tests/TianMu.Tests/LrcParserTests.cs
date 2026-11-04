using TianMu.Modules.Music.Lyrics;

namespace TianMu.Tests;

public class LrcParserTests
{
    [Fact]
    public void Parses_common_timestamp_formats()
    {
        var lines = LrcParser.Parse("""
            [ti:晴天]
            [ar:周杰倫]
            [00:01.5]一
            [00:02.25]二
            [00:03.125]三
            [00:04]四
            [01:05:50]五
            """);

        Assert.Equal(5, lines.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), lines[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(2250), lines[1].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(3125), lines[2].Time);
        Assert.Equal(TimeSpan.FromSeconds(4), lines[3].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(65500), lines[4].Time);
        Assert.Equal("五", lines[4].Text);
    }

    [Fact]
    public void Multiple_tags_on_one_line_are_expanded_and_sorted()
    {
        var lines = LrcParser.Parse("[00:10.00][00:30.00]副歌\n[00:20.00]主歌");

        Assert.Equal(new[] { "副歌", "主歌", "副歌" }, lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.FromSeconds(30), lines[2].Time);
    }

    [Fact]
    public void Offset_tag_shifts_lines_earlier()
    {
        var lines = LrcParser.Parse("[offset:500]\n[00:02.00]a");

        Assert.Equal(TimeSpan.FromMilliseconds(1500), lines[0].Time);
    }

    [Fact]
    public void Handles_crlf_and_empty_input()
    {
        Assert.Empty(LrcParser.Parse(null));
        Assert.Empty(LrcParser.Parse("   "));

        var lines = LrcParser.Parse("[00:01.00]a\r\n[00:02.00]b\r\n");
        Assert.Equal(new[] { "a", "b" }, lines.Select(l => l.Text));
    }

    [Theory]
    [InlineData(0.5, -1)]
    [InlineData(1.0, 0)]
    [InlineData(2.5, 1)]
    [InlineData(99, 2)]
    public void FindIndex_returns_line_at_position(double seconds, int expected)
    {
        var lines = LrcParser.Parse("[00:01.00]a\n[00:02.00]b\n[00:03.00]c");

        Assert.Equal(expected, LrcParser.FindIndex(lines, TimeSpan.FromSeconds(seconds)));
    }
}
