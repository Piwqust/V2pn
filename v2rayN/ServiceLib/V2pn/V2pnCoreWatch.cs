namespace ServiceLib;

public enum EV2pnCoreState
{
    Stopped,
    Starting,
    Running,
    Restarting,
    Failed,
}

/// <summary>Why V2pn switched itself off.</summary>
public enum EV2pnFailure
{
    None,

    /// <summary>The core did not start: no core file, a config it rejects, a busy port.</summary>
    StartFailed,

    /// <summary>The core started and then kept exiting, restart after restart.</summary>
    KeepsStopping,
}

/// <summary>
/// What the core process is actually doing. v2rayN shows the mode the user picked, so a core that never
/// started or died later still reads as connected while Windows sends traffic to a port nobody serves.
/// V2pn watches the process: a core that exits on its own is restarted with growing pauses, and when that
/// does not help V2pn switches off, which clears its own system proxy, and says why.
/// </summary>
public static class V2pnCoreWatch
{
    public static readonly EventChannel<EV2pnCoreState> StateChanged = new();

    /// <summary>The core exited without CoreStop asking for it. Carries the exit code.</summary>
    public static readonly EventChannel<int> CoreExited = new();

    public static V2pnRestartPolicy Restarts { get; } = new();

    public static EV2pnCoreState State { get; private set; }

    /// <summary>Kept after V2pn switches off, until the next connection attempt.</summary>
    public static EV2pnFailure Failure { get; private set; }

    /// <summary>The core's own words about the failure, already in the app language, when it gave any.</summary>
    public static string? FailureDetail { get; private set; }

    private static string? _lastProblem;

    /// <summary>The last message of a failed start, so the failure can be explained later.</summary>
    public static void NoteProblem(string? message)
    {
        if (message.IsNotEmpty())
        {
            _lastProblem = message;
        }
    }

    public static void MarkStarting()
    {
        _lastProblem = null;
        Failure = EV2pnFailure.None;
        FailureDetail = null;
        // A restart after a crash keeps saying "reconnecting" until the core is up.
        if (State != EV2pnCoreState.Restarting)
        {
            Restarts.Reset();
            Set(EV2pnCoreState.Starting);
        }
    }

    public static void MarkRunning()
    {
        Restarts.Started(DateTime.UtcNow);
        Set(EV2pnCoreState.Running);
    }

    public static void MarkRestarting() => Set(EV2pnCoreState.Restarting);

    /// <summary>Off, by the user or after a failure; the failure stays readable.</summary>
    public static void MarkStopped() => Set(EV2pnCoreState.Stopped);

    public static void MarkFailed(EV2pnFailure failure, string? detail = null)
    {
        Failure = failure;
        FailureDetail = detail.IsNotEmpty() ? detail : _lastProblem;
        Set(EV2pnCoreState.Failed);
    }

    private static void Set(EV2pnCoreState state)
    {
        State = state;
        StateChanged.Publish(state);
    }
}

/// <summary>
/// How long to wait before restarting a core that died, and when to stop trying. A core that ran for a
/// minute counts as healthy again, so one crash a day never adds up to "keeps stopping".
/// </summary>
public sealed class V2pnRestartPolicy
{
    private static readonly int[] _delaysSeconds = [1, 2, 5, 15, 30];
    private static readonly TimeSpan _stableAfter = TimeSpan.FromSeconds(60);

    private int _attempts;
    private DateTime? _startedUtc;

    public int MaxAttempts => _delaysSeconds.Length;

    /// <summary>The core is up.</summary>
    public void Started(DateTime nowUtc) => _startedUtc = nowUtc;

    public void Reset()
    {
        _attempts = 0;
        _startedUtc = null;
    }

    /// <summary>The pause before the next restart, or null when it is time to give up.</summary>
    public TimeSpan? NextDelay(DateTime nowUtc)
    {
        if (_startedUtc is { } started && nowUtc - started >= _stableAfter)
        {
            _attempts = 0;
        }
        _startedUtc = null;
        if (_attempts >= _delaysSeconds.Length)
        {
            return null;
        }
        return TimeSpan.FromSeconds(_delaysSeconds[_attempts++]);
    }
}
