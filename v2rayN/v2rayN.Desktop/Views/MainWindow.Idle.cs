namespace v2rayN.Desktop.Views;

/// <summary>
/// V2pn spends most of its life minimized or in the tray. While nobody can see the window, its
/// once-a-second work for the eye (the active server's ping, the status clock) waits, and it
/// catches up the moment the window is shown again.
/// </summary>
public partial class MainWindow
{
    private bool _seen = true;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
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
            RefreshActivePing();
            _pingTimer.Start();
        }
        else
        {
            _pingTimer.Stop();
        }
        _statusView?.SetSeen(seen);
    }
}
