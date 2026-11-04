namespace TianMu.Modules.Music.Media;

/// <summary>某一瞬間系統媒體的狀態快照。</summary>
public sealed record MediaSnapshot(
    string SourceApp,
    string Title,
    string Artist,
    string Album,
    byte[]? Thumbnail,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionUpdatedAt,
    double PlaybackRate)
{
    /// <summary>用來判斷「是否換歌」的鍵。</summary>
    public string TrackKey => $"{SourceApp}\u0001{Title}\u0001{Artist}";

    /// <summary>推算此刻的播放位置（播放中會隨時間前進）。</summary>
    public TimeSpan EstimatePosition(DateTimeOffset now)
    {
        var pos = Position;
        if (IsPlaying)
        {
            var elapsed = now - PositionUpdatedAt;
            if (elapsed > TimeSpan.Zero)
                pos += TimeSpan.FromTicks((long)(elapsed.Ticks * PlaybackRate));
        }

        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && pos > Duration) pos = Duration;
        return pos;
    }
}
