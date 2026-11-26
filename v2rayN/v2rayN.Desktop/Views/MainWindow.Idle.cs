using Avalonia.Controls.Notifications;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// V2pn spends most of its life minimized or in the tray. While nobody can see the window, its
/// once-a-second work for the eye (the active server's ping, the status clock) waits, and it
/// catches up the moment the window is shown again. Toasts raised meanwhile wait too.
/// </summary>
public partial class MainWindow
{
    private const int MaxPendingNotices = 3;

    private bool _seen = true;
    private readonly Queue<(object Content, NotificationType Type, TimeSpan Ttl, DateTime At)> _pendingNotices = new();

    // News like "Connecting · Germany" is stale a moment later; a warning or an error is worth reading late.
    private static readonly TimeSpan _noticeFreshFor = TimeSpan.FromSeconds(15);

    // The state a minimized or hidden window comes back in, and the bounds it restores to when maximized.
    private WindowState _restoreState = WindowState.Normal;
    private PixelPoint? _normalPosition;
    private Size? _normalSize;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
        {
            // The caption buttons dim in the background, like a native title bar.
            Classes.Set("inactive", !IsActive);
        }
        else if (change.Property == WindowStateProperty)
        {
            OnWindowStateChanged();
        }
        else if (change.Property == ClientSizeProperty)
        {
            QueueNormalBounds();
            FollowWidth();
        }

        // Properties are set during InitializeComponent, before the timers exist.
        if (_pingTimer != null && (change.Property == WindowStateProperty || change.Property == IsVisibleProperty))
        {
            UpdateSeen();
        }
    }

    private void UpdateSeen()
    {
        // Drawn in the tray (the warm-up below), the window is on no screen.
        var seen = IsVisible && WindowState != WindowState.Minimized && !_warming;
        if (seen == _seen)
        {
            return;
        }
        _seen = seen;
        if (seen)
        {
            DebugTraceSeen();
            RefreshActivePing();
            _pingTimer.Start();
            // After the first layout of the shown window, so the toasts land above the status card.
            Dispatcher.UIThread.Post(ReplayNotices, DispatcherPriority.Background);
        }
        else
        {
            _pingTimer.Stop();
        }
        _statusView?.SetSeen(seen);
    }

    #region Warm-up

    // A start in the tray leaves a window that was never drawn, and the first click on the tray icon paid for
    // its whole first layout and frame: about half a second, where every later click takes a few hundredths.
    // So once the start has settled, the window is drawn one time where nobody sees it: cloaked (Windows
    // composes it and shows nothing), not activated, without a taskbar button. Then it is hidden again.
    private bool _warming;
    // Drawn once already, or shown by the person before its turn came.
    private bool _warmUpDone;
    private (bool Activated, bool Taskbar, WindowState Restore) _beforeWarmUp;

    // The start's own work comes first: the server list, the connection, the tray.
    private static readonly TimeSpan _warmUpAfter = TimeSpan.FromSeconds(3);
    // A frame that does not come (no display, a remote session) must not keep the cloaked window up.
    private static readonly TimeSpan _warmUpLimit = TimeSpan.FromSeconds(3);

    private async Task WarmUpInTrayAsync()
    {
        if (!Utils.IsWindows() || _warmUpDone)
        {
            return;
        }
        try
        {
            await Task.Delay(_warmUpAfter);
            if (ViewModel?.Initialized is { } loaded)
            {
                await loaded;
            }
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (_warmUpDone || _blCloseByUser || IsVisible || TryGetPlatformHandle()?.Handle is not { } hwnd || !WindowsShell.Cloak(hwnd, true))
            {
                return;
            }
            _beforeWarmUp = (ShowActivated, ShowInTaskbar, _restoreState);
            _warming = true;
            var began = System.Diagnostics.Stopwatch.GetTimestamp();
            ShowActivated = false;
            ShowInTaskbar = false;
            // In its normal size: Windows activates a window it maximizes.
            WindowState = WindowState.Normal;
            Show();
            var drawn = await DrawnAsync(_warmUpLimit);
            DebugPerfLine(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss.fff} warm-up took={System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalMilliseconds:0} drawn={drawn}"));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(WarmUpInTrayAsync), ex);
        }
        finally
        {
            EndWarmUp(false);
        }
    }

    /// <summary>
    /// Ends the drawing in the tray: the window goes back into hiding, or, when the person asked for it
    /// meanwhile (<paramref name="reveal"/>), stays and comes into view.
    /// </summary>
    private void EndWarmUp(bool reveal)
    {
        _warmUpDone = true;
        if (!_warming)
        {
            return;
        }
        try
        {
            if (!reveal)
            {
                Hide();
            }
            _warming = false;
            ShowInTaskbar = _beforeWarmUp.Taskbar;
            ShowActivated = _beforeWarmUp.Activated;
            // Hidden, the state is only remembered: the next show opens the window in it, in one step.
            WindowState = _beforeWarmUp.Restore;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(EndWarmUp), ex);
        }
        finally
        {
            _warming = false;
            if (TryGetPlatformHandle()?.Handle is { } hwnd)
            {
                WindowsShell.Cloak(hwnd, false);
            }
            UpdateSeen();
        }
    }

    /// <summary>Until the window, as it is laid out now, has been drawn, and what loads after the first frame too.</summary>
    private async Task<bool> DrawnAsync(TimeSpan limit)
    {
        var timeout = Task.Delay(limit);
        for (var frame = 0; frame < 2; frame++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if (Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(this)?.Compositor is not { } compositor
                || await Task.WhenAny(compositor.RequestCompositionBatchCommitAsync().Rendered, timeout) == timeout)
            {
                return false;
            }
        }
        return true;
    }

    #endregion Warm-up

    /// <summary>
    /// Every toast goes through here. While the window is hidden or minimized its toasts would time out
    /// unseen, so the last few wait and appear when it is shown.
    /// </summary>
    private void ShowNotice(object content, NotificationType type, TimeSpan ttl)
    {
        if (_manager == null)
        {
            return;
        }
        if (!_seen)
        {
            if (_pendingNotices.Count == MaxPendingNotices)
            {
                _pendingNotices.Dequeue();
            }
            _pendingNotices.Enqueue((content, type, ttl, DateTime.Now));
            return;
        }
        _manager.Show(content, type, ttl);
    }

    private void ReplayNotices()
    {
        while (_seen && _manager != null && _pendingNotices.Count > 0)
        {
            var (content, type, ttl, at) = _pendingNotices.Dequeue();
            var news = type is NotificationType.Information or NotificationType.Success;
            if (news && DateTime.Now - at > _noticeFreshFor)
            {
                continue;
            }
            _manager.Show(content, type, ttl);
        }
    }

    // Maximize shows the restore glyph and says "Restore"; a minimized window comes back as it was.
    private void OnWindowStateChanged()
    {
        if (WindowState is WindowState.Normal or WindowState.Maximized)
        {
            _restoreState = WindowState;
        }
        if (iconMaximize == null)
        {
            return;
        }
        var maximized = WindowState == WindowState.Maximized;
        iconMaximize.Data = this.FindResource(maximized ? "SemiIconCopy" : "SemiIconMaximize") as Geometry;
        Avalonia.Automation.AutomationProperties.SetName(btnMaximize, maximized ? L.Restore : L.Maximize);
        QueueNormalBounds();
    }

    // Read after the state settles: Windows reports the new size before the new state, and a maximized
    // size must not be remembered as the normal one.
    private void QueueNormalBounds()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible && WindowState == WindowState.Normal && ClientSize.Width > 0)
            {
                _normalPosition = Position;
                _normalSize = ClientSize;
            }
        }, DispatcherPriority.Background);
    }
}
