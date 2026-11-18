using System.Net.NetworkInformation;

namespace ServiceLib;

/// <summary>
/// V2pn: after the computer wakes or its network changes (Wi-Fi to a hotspot, a cable back), the running
/// connection is checked again. A burst of events (an adapter going down, two addresses coming up) ends in
/// one check, made once the network has been quiet for a moment. Wake-up is seen as a gap in a timer that
/// ticks every few seconds: timers do not run while the computer sleeps, on any system.
/// </summary>
public sealed class V2pnNetworkWatch : IDisposable
{
    public static readonly TimeSpan DefaultQuiet = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WakeGap = TimeSpan.FromSeconds(30);

    private readonly Func<string, Task> _settled;
    private readonly TimeSpan _quiet;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private string _reason = string.Empty;
    private Timer? _timer;
    private DateTime _lastTick;
    private bool _subscribed;

    /// <param name="settled">Runs once per burst, with the first reason of the burst.</param>
    public V2pnNetworkWatch(Func<string, Task> settled, TimeSpan? quiet = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _settled = settled;
        _quiet = quiet ?? DefaultQuiet;
        _delay = delay ?? Task.Delay;
        _lastTick = DateTime.UtcNow;
    }

    public void Start()
    {
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        _subscribed = true;
        _lastTick = DateTime.UtcNow;
        _timer = new Timer(_ => CheckForWake(DateTime.UtcNow), null, Tick, Tick);
    }

    private void OnAddressChanged(object? sender, EventArgs e) => Signal("network address changed");

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        Signal(e.IsAvailable ? "network is back" : "network lost");

    /// <summary>Called by the tick timer; a gap far longer than the tick means the computer slept.</summary>
    internal void CheckForWake(DateTime now)
    {
        var gap = now - _lastTick;
        _lastTick = now;
        if (gap > WakeGap)
        {
            Signal($"woke after {gap.TotalMinutes:0} min");
        }
    }

    /// <summary>Starts or restarts the quiet period; the check runs when no new event came during it.</summary>
    public void Signal(string reason)
    {
        CancellationTokenSource next;
        lock (_gate)
        {
            if (_pending == null)
            {
                _reason = reason;
            }
            _pending?.Cancel();
            _pending?.Dispose();
            next = _pending = new CancellationTokenSource();
        }
        _ = RunAfterQuietAsync(next);
    }

    private async Task RunAfterQuietAsync(CancellationTokenSource cts)
    {
        try
        {
            await _delay(_quiet, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        string reason;
        lock (_gate)
        {
            if (!ReferenceEquals(_pending, cts) || cts.IsCancellationRequested)
            {
                return;
            }
            _pending = null;
            reason = _reason;
        }

        // A check still running from the previous burst finishes first; this one then reflects the newest network.
        await _running.WaitAsync();
        try
        {
            await _settled(reason);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnNetworkWatch), ex);
        }
        finally
        {
            _running.Release();
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            NetworkChange.NetworkAddressChanged -= OnAddressChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        }
        _timer?.Dispose();
        lock (_gate)
        {
            _pending?.Cancel();
        }
    }
}
