using System.Runtime.InteropServices;

namespace v2rayN.Desktop.Common;

/// <summary>
/// V2pn: what a right click on the tray icon opens on Windows. Avalonia's plain menu gives way to the tray
/// panel (Views/TrayPanel), and the window is kept next to the click.
///
/// The placement: Avalonia's tray menu window (TrayPopupRoot)
/// re-runs its positioner on every layout pass and anchors it at the window's own current position,
/// which the previous pass already moved. When a later pass comes back taller, the menu flips up a
/// second time and hangs a whole menu-height above the taskbar. Here the click point is taken once,
/// and after every layout pass the menu is put back against it, inside the screen's working area.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class TrayMenuPlacement
{
    private const string TrayWindowType = "TrayPopupRoot";

    public static void Install()
    {
        // Avalonia builds the tray window with its menu as the content and shows it at once; the panel takes
        // the menu's place before the first layout pass, so the plain menu is never drawn.
        ContentControl.ContentProperty.Changed.AddClassHandler<Window>((window, e) =>
        {
            if (window.GetType().Name == TrayWindowType && e.NewValue is MenuFlyoutPresenter)
            {
                UsePanel(window);
            }
        });
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => OnOpened(window));
    }

    private static Views.TrayPanel? _panel;
    private static Views.MainWindow? _panelOwner;

    private static void UsePanel(Window window)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: Views.MainWindow main })
            {
                return;
            }
            // One panel for the whole run: built once, it opens as fast as the plain menu did.
            if (_panel == null || _panelOwner != main)
            {
                _panel = new Views.TrayPanel(main, window.Close);
                _panelOwner = main;
            }
            _panel.MoveTo(window, window.Close);
            window.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    window.Close();
                }
            };
        }
        catch (Exception ex)
        {
            // The plain menu stays: it still does everything the panel does.
            Logging.SaveLog(nameof(TrayMenuPlacement), ex);
        }
    }

    private static void OnOpened(Window window)
    {
        if (window.GetType().Name != TrayWindowType || !GetCursorPos(out var cursor))
        {
            return;
        }

        var click = new PixelPoint(cursor.X, cursor.Y);
        void Place(object? sender, EventArgs e) => PlaceAt(window, click);
        window.LayoutUpdated += Place;
        window.Closed += (_, _) => window.LayoutUpdated -= Place;
        PlaceAt(window, click);
    }

    private static void PlaceAt(Window window, PixelPoint click)
    {
        try
        {
            var size = PixelSize.FromSize(window.FrameSize ?? window.ClientSize, window.RenderScaling);
            var screen = window.Screens.ScreenFromPoint(click) ?? window.Screens.Primary;
            if (size.Width <= 0 || size.Height <= 0 || screen is null)
            {
                return;
            }
            var area = screen.WorkingArea;

            // Like the shell's own tray menus: above the click and to its right, flipped when that
            // leaves the working area, so a taskbar on any edge keeps the menu beside it.
            var x = click.X + size.Width <= area.Right ? click.X : click.X - size.Width;
            var y = click.Y - size.Height >= area.Y ? click.Y - size.Height : click.Y;
            x = Math.Max(area.X, Math.Min(x, area.Right - size.Width));
            y = Math.Max(area.Y, Math.Min(y, area.Bottom - size.Height));

            // Background mode (tools/sandbox.ps1) keeps it off the screen like every other window.
            var target = OffscreenMode.Place(new PixelPoint(x, y));
            if (window.Position != target)
            {
                window.Position = target;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(TrayMenuPlacement), ex);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);
}
