using System.IO;
using TianMu.Modules.Cleanse;

namespace TianMu.Tests;

public sealed class CleanseTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "tianmu-cleanse-" + Guid.NewGuid().ToString("N"));

    public CleanseTests() => Directory.CreateDirectory(_home);

    public void Dispose()
    {
        try { Directory.Delete(_home, true); } catch { }
    }

    private string Make(string relative, int bytes = 0)
    {
        string path = Path.Combine(_home, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    // ── 等級判斷 ──

    [Fact]
    public void User_files_are_never_ticked_by_default()
    {
        var now = DateTime.Now;
        var (tier, _) = CleanseJudge.Judge(CleanseKind.LargeFile, CleanseTier.Primary, @"C:\Users\a\Downloads\x.mp4", "", false, false, [], now.AddDays(-40), now);
        Assert.Equal(CleanseTier.Optional, tier);
        (tier, _) = CleanseJudge.Judge(CleanseKind.LargeFolder, CleanseTier.Primary, @"C:\Users\a\Videos\old", "", false, false, [], null, now);
        Assert.Equal(CleanseTier.Optional, tier);
    }

    [Fact]
    public void Protected_and_repositories_are_kept()
    {
        var now = DateTime.Now;
        Assert.Equal(CleanseTier.Keep, CleanseJudge.Judge(CleanseKind.KnownCache, CleanseTier.Primary, "x", "", true, false, [], null, now).Tier);
        Assert.Equal(CleanseTier.Keep, CleanseJudge.Judge(CleanseKind.LargeFolder, CleanseTier.Optional, "x", "", false, true, [], null, now).Tier);
    }

    [Fact]
    public void Reasons_mention_age_installers_and_running_apps()
    {
        var now = new DateTime(2026, 11, 8);
        var (_, reason) = CleanseJudge.Judge(CleanseKind.LargeFile, CleanseTier.Optional, @"C:\Users\a\Downloads\setup.exe", "", false, false, [], now.AddDays(-90), now);
        Assert.Contains("安裝檔", reason);
        Assert.Contains("90 天", reason);
        (_, reason) = CleanseJudge.Judge(CleanseKind.KnownCache, CleanseTier.Primary, "x", "網頁快取", false, false, ["chrome"], null, now);
        Assert.Contains("chrome 還開著", reason);
    }

    [Fact]
    public void Only_the_deepest_big_folder_is_listed()
    {
        var picked = CleanseJudge.Deepest([(@"C:\u\Videos", 5L), (@"C:\u\Videos\trip", 4L), (@"C:\u\Music", 2L)]);
        Assert.Equal(2, picked.Count);
        Assert.Contains(picked, p => p.Path == @"C:\u\Videos\trip");
        Assert.DoesNotContain(picked, p => p.Path == @"C:\u\Videos");
    }

    [Fact]
    public void Sizes_read_naturally()
    {
        Assert.Equal("1.5 GB", CleanseJudge.Size(3 * CleanseRules.GB / 2));
        Assert.Equal("300 MB", CleanseJudge.Size(300 * CleanseRules.MB));
    }

    // ── 路徑 ──

    [Fact]
    public void Wildcards_expand_to_every_profile()
    {
        Make(@"Browser\Profile 1\Cache\a.bin");
        Make(@"Browser\Default\Cache\b.bin");
        Make(@"Browser\Default\Other\c.bin");
        var found = PathRules.Resolve(@"~\Browser\*\Cache", _home);
        Assert.Equal(2, found.Count);
        Assert.All(found, p => Assert.EndsWith("Cache", p));
    }

    [Fact]
    public void Home_root_and_secrets_are_protected()
    {
        Assert.True(PathRules.IsProtected(_home, _home));
        Assert.True(PathRules.IsProtected(Path.Combine(_home, ".ssh", "id_ed25519"), _home));
        Assert.True(PathRules.IsProtected(Path.GetPathRoot(_home)!, _home));
        Assert.False(PathRules.IsProtected(Path.Combine(_home, "Downloads", "big.iso"), _home));
        Assert.True(PathRules.IsUnder(Path.Combine(_home, "a", "b"), Path.Combine(_home, "a")));
        Assert.False(PathRules.IsUnder(Path.Combine(_home, "ab"), Path.Combine(_home, "a")));
    }

    // ── 掃描與清理 ──

    [Fact]
    public void Scan_finds_known_caches_and_big_user_files()
    {
        Make(@"AppCache\one.bin", 3 * 1024 * 1024);
        var rules = new[] { new CacheRule("test", "測試快取", [@"~\AppCache"], CleanseMode.Contents, CleanseTier.Primary, "測試", MinMB: 1) };
        var result = new DiskScanner(_home, () => new HashSet<string>()).Scan(null, CancellationToken.None, rules, includeLarge: false, includeRecycleBin: false);
        var item = Assert.Single(result.Items);
        Assert.Equal(CleanseTier.Primary, item.Tier);
        Assert.Equal(3 * 1024 * 1024, item.Bytes);
        Assert.Equal("c001", item.Id);
    }

    [Fact]
    public void Cleaning_contents_keeps_the_folder_and_skips_kept_items()
    {
        string a = Make(@"AppCache\one.bin", 1024);
        Make(@"AppCache\sub\two.bin", 1024);
        var item = new CleanseItem
        {
            Id = "c001", Path = Path.GetDirectoryName(a)!, Name = "快取", Kind = CleanseKind.KnownCache,
            Mode = CleanseMode.Contents, Tier = CleanseTier.Primary, Bytes = 2048,
        };
        var r = DiskCleaner.Clean(item, optionalToRecycleBin: true, CancellationToken.None, _home);
        Assert.Equal(CleanOutcome.Deleted, r.Outcome);
        Assert.True(Directory.Exists(item.Path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(item.Path));

        var keep = new CleanseItem { Id = "c002", Path = item.Path, Name = "x", Kind = CleanseKind.LargeFolder, Tier = CleanseTier.Keep };
        Assert.Equal(CleanOutcome.Skipped, DiskCleaner.Clean(keep, true, CancellationToken.None, _home).Outcome);
    }

    [Fact]
    public void Files_changed_after_the_scan_are_left_alone()
    {
        string f = Make(@"Downloads\movie.mkv", 1024);
        var item = new CleanseItem
        {
            Id = "c001", Path = f, Name = "movie.mkv", Kind = CleanseKind.LargeFile, Tier = CleanseTier.Optional,
            WrittenAtUtc = File.GetLastWriteTimeUtc(f).AddMinutes(-10),
        };
        var r = DiskCleaner.Clean(item, optionalToRecycleBin: false, CancellationToken.None, _home);
        Assert.Equal(CleanOutcome.Skipped, r.Outcome);
        Assert.True(File.Exists(f));
    }

    [Fact]
    public void Missing_items_are_reported_as_gone()
    {
        var item = new CleanseItem { Id = "c001", Path = Path.Combine(_home, "nope"), Name = "x", Kind = CleanseKind.KnownCache, Tier = CleanseTier.Primary };
        Assert.Equal(CleanOutcome.Gone, DiskCleaner.Clean(item, true, CancellationToken.None, _home).Outcome);
    }

    [Fact]
    public void Low_space_alert_respects_threshold_and_cooldown()
    {
        var now = DateTime.Now;
        long gb = CleanseRules.GB;
        Assert.True(TianMu.Modules.Cleanse.CleanseModule.ShouldAlert(10 * gb, 500 * gb, 15 * gb, DateTime.MinValue, now, TimeSpan.FromHours(6)));
        Assert.False(TianMu.Modules.Cleanse.CleanseModule.ShouldAlert(20 * gb, 500 * gb, 15 * gb, DateTime.MinValue, now, TimeSpan.FromHours(6)));
        Assert.False(TianMu.Modules.Cleanse.CleanseModule.ShouldAlert(10 * gb, 500 * gb, 15 * gb, now.AddHours(-1), now, TimeSpan.FromHours(6)));
    }

    [Fact]
    public void Every_known_rule_is_well_formed()
    {
        Assert.Equal(CleanseRules.Known.Count, CleanseRules.Known.Select(r => r.Id).Distinct().Count());
        Assert.All(CleanseRules.Known, r =>
        {
            Assert.NotEmpty(r.Paths);
            Assert.False(string.IsNullOrWhiteSpace(r.Impact));
            Assert.All(r.Paths, p => Assert.True(p.StartsWith('%') || p.StartsWith('~'), p));
        });
    }
}
