using System.IO;
using System.Text;
using TianMu.Modules.Convert.Engines;
using TianMu.Modules.Convert.Furnace;

namespace TianMu.Tests;

public class FurnacePlannerTests
{
    private static string[] Ids(params string[] files) => FurnacePlanner.OptionsFor(files, mediaReady: true).Select(o => o.Id).ToArray();

    [Fact]
    public void Photos_offer_image_formats_without_their_own_format()
    {
        var ids = Ids(@"C:\a\trip.jpg", @"C:\a\beach.JPEG");
        Assert.Contains("img:webp", ids);
        Assert.Contains("img:pdf", ids);
        Assert.Contains("img:shrink", ids);
        Assert.DoesNotContain("img:jpg", ids);        // JPG 轉 JPG 沒意義
        Assert.True(ids.Length <= 8);
    }

    [Fact]
    public void Each_kind_gets_its_own_ring()
    {
        Assert.Contains("pdf:merge", Ids("a.pdf", "b.pdf"));
        Assert.DoesNotContain("pdf:merge", Ids("a.pdf"));
        Assert.Contains("av:mp3", Ids("clip.mp4"));
        Assert.DoesNotContain("au:mp3", Ids("song.mp3"));
        Assert.Contains("au:flac", Ids("song.mp3"));
        Assert.Equal(new[] { "sub:srt", "sub:vtt", "txt:utf8" }, Ids("movie.srt"));
        Assert.Equal(new[] { "zip:extract" }, Ids("bundle.zip"));
        Assert.Equal(new[] { "zip:pack" }, Ids("photo.jpg", "notes.pdf"));   // 混在一起只能打包
        Assert.Empty(Ids());
    }

    [Fact]
    public void Media_options_warn_before_the_first_download()
    {
        var options = FurnacePlanner.OptionsFor(["clip.mov"], mediaReady: false);
        Assert.All(options, o => Assert.Contains("首次需下載", o.Caption));
    }

    [Fact]
    public void Output_names_follow_the_source_and_never_overwrite()
    {
        Assert.Equal("trip.webp", OutputNames.For(@"C:\x\trip.jpg", "webp"));
        Assert.Equal("trip-灰階.jpg", OutputNames.For("trip.jpg", ".jpg", "灰階"));

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine("d", "trip.webp"), Path.Combine("d", "trip (2).webp") };
        Assert.Equal(Path.Combine("d", "trip (3).webp"), OutputNames.Unique("d", "trip.webp", taken.Contains));
        Assert.Equal(Path.Combine("d", "new.webp"), OutputNames.Unique("d", "new.webp", taken.Contains));
    }
}

public class TextEngineTests
{
    [Fact]
    public void Big5_text_is_read_correctly()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] big5 = Encoding.GetEncoding(950).GetBytes("天幕島化形爐");
        Assert.Equal("天幕島化形爐", TextEngine.Decode(big5));
        Assert.Equal("天幕", TextEngine.Decode(Encoding.UTF8.GetBytes("天幕")));
        Assert.Equal("天幕", TextEngine.Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("天幕")]));
    }

    [Fact]
    public void Srt_becomes_webvtt()
    {
        const string srt = "1\r\n00:00:01,000 --> 00:00:02,500\r\n你好\r\n\r\n2\r\n00:00:03,000 --> 00:00:04,000\r\n再見\r\n";
        string vtt = TextEngine.SrtToVtt(srt);
        Assert.StartsWith("WEBVTT\n\n", vtt);
        Assert.Contains("00:00:01.000 --> 00:00:02.500", vtt);
        Assert.Contains("你好", vtt);
    }

    [Fact]
    public void Webvtt_becomes_numbered_srt()
    {
        const string vtt = "WEBVTT\n\nNOTE 這是註解\n\n01:02.500 --> 01:04.000 align:start\n第一句\n\ncue-2\n00:01:05.000 --> 00:01:06.000\n第二句\n第二行\n";
        string srt = TextEngine.VttToSrt(vtt);
        Assert.Equal("1\n00:01:02,500 --> 00:01:04,000\n第一句\n\n2\n00:01:05,000 --> 00:01:06,000\n第二句\n第二行\n\n", srt);
    }

    [Fact]
    public void Ffmpeg_duration_is_read_from_its_log()
    {
        Assert.Equal(62.33 * 1_000_000, MediaArgs.ParseDuration("  Duration: 00:01:02.33, start: 0.000000, bitrate: 128 kb/s")!.Value, 0);
        Assert.Null(MediaArgs.ParseDuration("Stream #0:0: Audio"));
        Assert.Equal("mp4", MediaArgs.For("720p", "a.mov").Ext);
        Assert.Equal("720p", MediaArgs.For("720p", "a.mov").Suffix);
    }
}
