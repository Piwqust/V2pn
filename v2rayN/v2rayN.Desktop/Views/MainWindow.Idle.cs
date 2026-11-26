using Avalonia.Controls.Notifications;

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
        if (_pingTimer == null || (change.Property != WindowStateProperty && change.Property != IsVisibleProperty))
        {
            return;
        }
        var seen = IsVisible && WindowState != WindowState.Minimized;
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
