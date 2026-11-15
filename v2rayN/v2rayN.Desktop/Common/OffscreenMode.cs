namespace v2rayN.Desktop.Common;

/// <summary>
/// V2pn: with V2PN_BACKGROUND=1 (set by tools/sandbox.ps1) every window opens far off the screen,
/// without taking focus or a taskbar button, so test runs never flash in front of the owner.
/// The windows still render, so tools/shot.ps1 and UI Automation keep working.
/// </summary>
internal static class OffscreenMode
{
    // Far left of any real monitor layout, yet well inside the coordinate range Windows accepts.
    private const int ParkX = -20000;

    public static bool IsOn { get; } = Environment.GetEnvironmentVariable("V2PN_BACKGROUND") == "1";

    /// <summary>The position to give a window the app is about to place: the park spot in background mode.</summary>
    // Fixing a position after the move is too late: the window shows there for a frame.
    public static PixelPoint Place(PixelPoint wanted) => IsOn ? new PixelPoint(ParkX, 0) : wanted;

    public static void Attach(Window window)
    {
        if (!IsOn)
        {
            return;
        }
        Park(window);
        // Window XAML (WindowStartupLocation="CenterScreen" and the like) loads after the base constructor.
        window.Initialized += (_, _) => Park(window);
        // The app re-centres and clamps its windows to a screen after load and on layout changes.
        window.PositionChanged += (_, e) =>
        {
            if (e.Point.X > ParkX / 2)
            {
                window.Position = new PixelPoint(ParkX, 0);
            }
        };
        // A maximized or full-screen window would cover the owner's screen wherever it is parked.
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty
                && window.WindowState is WindowState.Maximized or WindowState.FullScreen)
            {
                window.WindowState = WindowState.Normal;
            }
        };
    }

    private static void Park(Window window)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(ParkX, 0);
    }
}
