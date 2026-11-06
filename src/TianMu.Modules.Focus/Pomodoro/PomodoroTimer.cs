namespace TianMu.Modules.Focus.Pomodoro;

public enum PomodoroPhase
{
    Idle,
    Focus,
    ShortBreak,
    LongBreak,
}

/// <summary>番茄鐘時長設定。</summary>
public sealed record PomodoroConfig(TimeSpan Focus, TimeSpan ShortBreak, TimeSpan LongBreak, int RoundsBeforeLongBreak)
{
    public static PomodoroConfig Default { get; } =
        new(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), 4);
}

public enum PomodoroEventKind
{
    /// <summary>一回專注完成（應累積修為），接著進入休息。</summary>
    FocusCompleted,
    /// <summary>休息結束，回到待命。</summary>
    BreakCompleted,
}

public readonly record struct PomodoroEvent(PomodoroEventKind Kind, TimeSpan FocusedTime, PomodoroPhase NextPhase);

/// <summary>
/// 番茄鐘狀態機。以「結束時刻」計時（而不是每秒減一），電腦卡頓或睡眠也不會跑錯。
/// 所有方法都傳入現在時間，方便測試。
/// </summary>
public sealed class PomodoroTimer(PomodoroConfig config)
{
    private DateTime? _endsAt;
    private TimeSpan _pausedRemaining;

    /// <summary>時長設定；改變後從下一個階段開始生效。</summary>
    public PomodoroConfig Config { get; set; } = config;
    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Idle;
    public bool IsPaused { get; private set; }

    /// <summary>本輪（到下一次長休息前）已完成的專注回合數。</summary>
    public int CompletedInCycle { get; private set; }

    /// <summary>目前是第幾回（從 1 起算）。</summary>
    public int CurrentRound => CompletedInCycle + (Phase == PomodoroPhase.Focus ? 1 : 0);

    public bool IsRunning => Phase != PomodoroPhase.Idle;
    public bool IsBreak => Phase is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak;

    public TimeSpan PhaseDuration => Phase switch
    {
        PomodoroPhase.Focus => Config.Focus,
        PomodoroPhase.ShortBreak => Config.ShortBreak,
        PomodoroPhase.LongBreak => Config.LongBreak,
        _ => TimeSpan.Zero,
    };

    public TimeSpan Remaining(DateTime now)
    {
        if (!IsRunning) return TimeSpan.Zero;
        if (IsPaused) return _pausedRemaining;
        var left = _endsAt!.Value - now;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>本階段進度（0~1）。</summary>
    public double Progress(DateTime now)
    {
        var total = PhaseDuration;
        if (total <= TimeSpan.Zero) return 0;
        return Math.Clamp(1 - Remaining(now) / total, 0, 1);
    }

    /// <summary>已專注的時間（只在專注階段有意義）。</summary>
    public TimeSpan FocusedSoFar(DateTime now) =>
        Phase == PomodoroPhase.Focus ? PhaseDuration - Remaining(now) : TimeSpan.Zero;

    public void StartFocus(DateTime now) => Begin(PomodoroPhase.Focus, now);

    public void Pause(DateTime now)
    {
        if (!IsRunning || IsPaused) return;
        _pausedRemaining = Remaining(now);
        IsPaused = true;
    }

    public void Resume(DateTime now)
    {
        if (!IsRunning || !IsPaused) return;
        _endsAt = now + _pausedRemaining;
        IsPaused = false;
    }

    /// <summary>散功：放棄這一回。回傳已專注的時間（依然計入修為，不懲罰）。</summary>
    public TimeSpan Abandon(DateTime now)
    {
        var focused = FocusedSoFar(now);
        Stop();
        return focused;
    }

    /// <summary>跳過休息，直接回到待命。</summary>
    public void SkipBreak()
    {
        if (IsBreak) Stop();
    }

    /// <summary>每隔一小段時間呼叫；時間到時推進階段並回傳事件。</summary>
    public PomodoroEvent? Tick(DateTime now)
    {
        if (!IsRunning || IsPaused || now < _endsAt) return null;

        if (Phase == PomodoroPhase.Focus)
        {
            CompletedInCycle++;
            var next = CompletedInCycle >= Config.RoundsBeforeLongBreak ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
            var focused = Config.Focus;
            Begin(next, now);
            return new PomodoroEvent(PomodoroEventKind.FocusCompleted, focused, next);
        }

        // 休息結束
        if (Phase == PomodoroPhase.LongBreak) CompletedInCycle = 0;
        Stop();
        return new PomodoroEvent(PomodoroEventKind.BreakCompleted, TimeSpan.Zero, PomodoroPhase.Idle);
    }

    private void Begin(PomodoroPhase phase, DateTime now)
    {
        Phase = phase;
        IsPaused = false;
        _endsAt = now + PhaseDuration;
    }

    private void Stop()
    {
        Phase = PomodoroPhase.Idle;
        IsPaused = false;
        _endsAt = null;
    }
}
