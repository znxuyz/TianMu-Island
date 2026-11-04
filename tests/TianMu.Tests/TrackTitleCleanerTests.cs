using TianMu.Modules.Music.Media;

namespace TianMu.Tests;

public class TrackTitleCleanerTests
{
    private const string Chrome = "chrome";

    [Fact]
    public void Bilingual_title_in_lenticular_brackets()
    {
        var id = TrackTitleCleaner.Clean("周杰倫 Jay Chou【晴天 Sunny Day】Official MV", "周杰倫 Jay Chou", Chrome);

        Assert.Equal("晴天 Sunny Day", id.Title);
        Assert.Equal("周杰倫 Jay Chou", id.Artist);
        Assert.Contains(("周杰倫", "晴天"), id.SearchCandidates);
        Assert.Contains(("Jay Chou", "Sunny Day"), id.SearchCandidates);
    }

    [Fact]
    public void Artist_dash_title_with_noise_parentheses()
    {
        var id = TrackTitleCleaner.Clean("Taylor Swift - Shake It Off (Official Video)", "TaylorSwiftVEVO", "msedge");

        Assert.Equal("Shake It Off", id.Title);
        Assert.Equal("Taylor Swift", id.Artist);
        Assert.Equal(("Taylor Swift", "Shake It Off"), id.SearchCandidates[0]);
    }

    [Fact]
    public void Square_bracket_noise_is_removed()
    {
        var id = TrackTitleCleaner.Clean("Ed Sheeran - Shape of You [Official Lyric Video]", "Ed Sheeran", Chrome);

        Assert.Equal("Shape of You", id.Title);
        Assert.Equal("Ed Sheeran", id.Artist);
    }

    [Fact]
    public void Topic_channel_suffix_is_stripped()
    {
        var id = TrackTitleCleaner.Clean("Lemon", "Kenshi Yonezu - Topic", Chrome);

        Assert.Equal("Lemon", id.Title);
        Assert.Equal("Kenshi Yonezu", id.Artist);
    }

    [Fact]
    public void Japanese_corner_brackets_hold_song_name()
    {
        var id = TrackTitleCleaner.Clean("YOASOBI「アイドル」 Official Music Video", "Ayase / YOASOBI", Chrome);

        Assert.Equal("アイドル", id.Title);
        Assert.Equal("YOASOBI", id.Artist);
    }

    [Fact]
    public void Leading_mv_tag_is_removed()
    {
        var id = TrackTitleCleaner.Clean("【MV】米津玄師 - Lemon", "米津玄師", Chrome);

        Assert.Equal("Lemon", id.Title);
        Assert.Equal("米津玄師", id.Artist);
    }

    [Fact]
    public void Featuring_is_removed_from_title()
    {
        var id = TrackTitleCleaner.Clean("Calvin Harris - This Is What You Came For (Official Video) ft. Rihanna", "CalvinHarrisVEVO", Chrome);

        Assert.Equal("This Is What You Came For", id.Title);
        Assert.Equal("Calvin Harris", id.Artist);
    }

    [Fact]
    public void Native_player_metadata_is_kept()
    {
        var id = TrackTitleCleaner.Clean("Blinding Lights", "The Weeknd", "Spotify.exe");

        Assert.Equal("Blinding Lights", id.Title);
        Assert.Equal("The Weeknd", id.Artist);
        Assert.Equal(("The Weeknd", "Blinding Lights"), id.SearchCandidates[0]);
    }

    [Fact]
    public void Native_player_remaster_suffix_gets_extra_candidate()
    {
        var id = TrackTitleCleaner.Clean("Come Together - Remastered 2009", "The Beatles", "Spotify.exe");

        Assert.Equal("Come Together - Remastered 2009", id.Title);
        Assert.Contains(("The Beatles", "Come Together"), id.SearchCandidates);
    }

    [Fact]
    public void Plain_title_without_artist_hint_uses_channel()
    {
        var id = TrackTitleCleaner.Clean("告白氣球", "周杰倫", Chrome);

        Assert.Equal("告白氣球", id.Title);
        Assert.Equal("周杰倫", id.Artist);
    }

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("MSEdge", true)]
    [InlineData("308046B0AF4A39CB", true)]
    [InlineData("Spotify.exe", false)]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", false)]
    public void Browser_detection(string aumid, bool expected) =>
        Assert.Equal(expected, TrackTitleCleaner.IsBrowser(aumid));
}
