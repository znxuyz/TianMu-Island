using System.IO;
using System.Text.Json;
using TianMu.Island.Settings;
using TianMu.Island.Theme;
using TianMu.Island.Time;
using TianMu.Island.Update;

namespace TianMu.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tianmu-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Defaults_when_no_file()
    {
        var store = new SettingsStore(_dir);
        var s = store.Snapshot();

        Assert.False(s.StartWithWindows);
        Assert.True(s.AutoCheckUpdates);
        Assert.True(s.IsModuleEnabled("anything"));
    }

    [Fact]
    public void Update_persists_and_reloads()
    {
        var store = new SettingsStore(_dir);
        store.Update(s =>
        {
            s.StartWithWindows = true;
            s.GetModule("tianmu.music").Enabled = false;
            s.GetModule("tianmu.music").Options["lyrics"] = false;
        });

        var reloaded = new SettingsStore(_dir).Snapshot();
        Assert.True(reloaded.StartWithWindows);
        Assert.False(reloaded.IsModuleEnabled("tianmu.music"));
        Assert.False(new SettingsStore(_dir).ForModule("tianmu.music").GetBool("lyrics", true));
    }

    [Fact]
    public void Module_option_falls_back_to_default()
    {
        var store = new SettingsStore(_dir);
        Assert.True(store.ForModule("x").GetBool("missing", true));
        Assert.False(store.ForModule("x").GetBool("missing", false));
    }

    [Fact]
    public void Snapshot_is_a_copy()
    {
        var store = new SettingsStore(_dir);
        store.Snapshot().StartWithWindows = true;
        Assert.False(store.Snapshot().StartWithWindows);
    }

    [Fact]
    public void Corrupted_file_falls_back_and_keeps_backup()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ this is not json");

        var store = new SettingsStore(_dir);

        Assert.True(store.Snapshot().AutoCheckUpdates);
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json.broken")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}

public class UpdateServiceTests
{
    private const string Prefix = "https://github.com/znxuyz/TianMu-Island/releases/download/";

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("0.3.0+abc123", "0.3.0")]
    [InlineData("V2.0.1-beta", "2.0.1")]
    [InlineData("1.2", "1.2.0")]
    public void ParseVersion_accepts_common_forms(string input, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateService.ParseVersion(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public void ParseVersion_rejects_garbage(string? input) =>
        Assert.Null(UpdateService.ParseVersion(input));

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\TianMuIsland-win-x64.exe", @"C:\Users\me\Downloads\", true)]
    [InlineData(@"C:\Users\me\Desktop\TianMuIsland.exe", @"C:\Users\me\Desktop\", true)]
    [InlineData(@"D:\src\TianMu-Island\src\TianMu.Island\bin\Debug\net10.0-windows\TianMuIsland.exe",
                @"D:\src\TianMu-Island\src\TianMu.Island\bin\Debug\net10.0-windows\", false)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", @"C:\Program Files\dotnet\", false)]
    [InlineData(null, @"C:\", false)]
    public void Release_downloads_can_self_update(string? exe, string dir, bool expected) =>
        Assert.Equal(expected, UpdateService.IsSelfUpdatable(exe, dir));

    [Fact]
    public void ParseChecksum_reads_first_token()
    {
        string hash = new string('a', 64);
        Assert.Equal(hash, UpdateService.ParseChecksum($"{hash.ToUpperInvariant()}  TianMuIsland-win-x64.exe\n"));
        Assert.Equal(hash, UpdateService.ParseChecksum($"{hash} *file.exe"));
        Assert.Null(UpdateService.ParseChecksum("not-a-hash file.exe"));
        Assert.Null(UpdateService.ParseChecksum(""));
    }

    [Theory]
    [InlineData(Prefix + "v1.0.0/TianMuIsland-win-x64.exe", true)]
    [InlineData("https://github.com/someone-else/TianMu-Island/releases/download/v1/x.exe", false)]
    [InlineData("http://github.com/znxuyz/TianMu-Island/releases/download/v1/x.exe", false)]
    [InlineData(Prefix + "../../evil/x.exe", false)]
    public void Only_this_repository_is_trusted(string url, bool allowed) =>
        Assert.Equal(allowed, UpdateService.IsAllowedDownloadUrl(url));

    private static JsonElement Release(string tag, bool prerelease = false, bool withChecksum = true, string? foreignAsset = null)
    {
        var assets = new List<object>
        {
            new { name = "TianMuIsland-win-x64.exe", browser_download_url = foreignAsset ?? Prefix + tag + "/TianMuIsland-win-x64.exe" },
        };
        if (withChecksum)
            assets.Add(new { name = "TianMuIsland-win-x64.exe.sha256", browser_download_url = Prefix + tag + "/TianMuIsland-win-x64.exe.sha256" });

        var json = JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft = false,
            prerelease,
            html_url = "https://github.com/znxuyz/TianMu-Island/releases/tag/" + tag,
            body = "notes",
            assets,
        });
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public void ParseRelease_reads_assets()
    {
        var info = UpdateService.ParseRelease(Release("v9.1.0"), "TianMuIsland-win-x64.exe");

        Assert.NotNull(info);
        Assert.Equal(new Version(9, 1, 0), info!.Version);
        Assert.EndsWith("TianMuIsland-win-x64.exe", info.AssetUrl);
        Assert.EndsWith(".sha256", info.ChecksumUrl);
    }

    [Fact]
    public void ParseRelease_refuses_without_checksum() =>
        Assert.Null(UpdateService.ParseRelease(Release("v9.1.0", withChecksum: false), "TianMuIsland-win-x64.exe"));

    [Fact]
    public void ParseRelease_ignores_prerelease() =>
        Assert.Null(UpdateService.ParseRelease(Release("v9.1.0", prerelease: true), "TianMuIsland-win-x64.exe"));

    [Fact]
    public void ParseRelease_ignores_foreign_download_urls() =>
        Assert.Null(UpdateService.ParseRelease(
            Release("v9.1.0", foreignAsset: "https://evil.example.com/TianMuIsland-win-x64.exe"), "TianMuIsland-win-x64.exe"));
}

public class LunarTests
{
    [Theory]
    [InlineData(1, "初一")]
    [InlineData(10, "初十")]
    [InlineData(11, "十一")]
    [InlineData(19, "十九")]
    [InlineData(20, "二十")]
    [InlineData(21, "廿一")]
    [InlineData(29, "廿九")]
    [InlineData(30, "三十")]
    public void DayName(int day, string expected) => Assert.Equal(expected, Lunar.DayName(day));

    [Fact]
    public void MonthNames_use_traditional_forms()
    {
        Assert.Equal("正", Lunar.MonthName(1));
        Assert.Equal("冬", Lunar.MonthName(11));
        Assert.Equal("臘", Lunar.MonthName(12));
    }

    [Fact]
    public void Lunar_new_year_2026() => Assert.Equal("正月初一", Lunar.MonthDay(new DateTime(2026, 2, 17)));

    [Fact]
    public void Leap_month_2025() => Assert.Equal("閏六月初一", Lunar.MonthDay(new DateTime(2025, 7, 25)));

    [Fact]
    public void GanZhi_year_changes_at_lunar_new_year()
    {
        Assert.Equal("乙巳", Lunar.GanZhiYear(new DateTime(2026, 2, 16)));
        Assert.Equal("丙午", Lunar.GanZhiYear(new DateTime(2026, 2, 17)));
    }

    [Theory]
    [InlineData(23, "子")]
    [InlineData(0, "子")]
    [InlineData(1, "丑")]
    [InlineData(14, "未")]
    [InlineData(22, "亥")]
    public void Shichen(int hour, string expected) =>
        Assert.Equal(expected, Lunar.Shichen(new DateTime(2026, 1, 1, hour, 30, 0)));
}

public class TypoTests
{
    [Fact]
    public void Inserts_thin_space_between_cjk_characters()
    {
        Assert.Equal("一 般", Typo.Space("一般"));
        Assert.Equal("天 幕 島", Typo.Space("天幕島"));
    }

    [Fact]
    public void Leaves_latin_words_intact() => Assert.Equal("v0.3.0", Typo.Space("v0.3.0"));
}
