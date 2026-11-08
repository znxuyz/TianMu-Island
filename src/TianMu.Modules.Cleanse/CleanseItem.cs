namespace TianMu.Modules.Cleanse;

/// <summary>掃描找到的一項可能可以清理的東西。</summary>
public sealed class CleanseItem
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required CleanseKind Kind { get; init; }
    public CleanseMode Mode { get; init; } = CleanseMode.Whole;
    public required CleanseTier Tier { get; init; }
    public long Bytes { get; init; }
    public string Reason { get; init; } = "";
    public IReadOnlyList<string> AppsRunning { get; init; } = [];

    /// <summary>掃描當下的最後修改時間（UTC）。使用者檔案清理前會再比對一次，被動過就不清。</summary>
    public DateTime? WrittenAtUtc { get; init; }

    /// <summary>計算大小時有地方進不去或算太久，實際可能更大。</summary>
    public bool Partial { get; init; }

    public string? RuleId { get; init; }
}

/// <summary>一次掃描的結果。</summary>
public sealed record ScanResult(string Volume, long FreeBytes, long TotalBytes, IReadOnlyList<CleanseItem> Items, DateTime ScannedAt);

/// <summary>清理一項的結果。</summary>
public enum CleanOutcome { Deleted, Recycled, Partial, Skipped, Gone, Failed }

public sealed record CleanResult(CleanseItem Item, CleanOutcome Outcome, long FreedBytes, string Message);
