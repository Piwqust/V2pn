namespace v2rayN.Desktop.Base;

public class WindowBase<TViewModel> : ReactiveWindow<TViewModel> where TViewModel : class
{
    public WindowBase()
    {
        // Everything but the main window is a v2rayN dialog; they share the V2pn dialog styling.
        if (this is not Views.MainWindow)
        {
            Classes.Add("dialog");
            Common.DialogChrome.Attach(this);
            Common.MacMenus.AttachBasic(this); // V2pn
        }
        Common.OffscreenMode.Attach(this); // V2pn
        Loaded += OnLoaded;
        Loaded += (s, e) =>
        {
            if (Owner != null && !ShowInTaskbar)
            {
                CanMinimize = false;
            }
        };
    }

    private void ReactiveWindowBase_Closed(object? sender, EventArgs e)
    {
        throw new NotImplementedException();
    }

    protected virtual void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Classes.Contains("dialog"))
        {
            Common.SettingsKit.FollowTitles(this);
        }
        try
        {
            var sizeItem = ConfigTextHandler.GetWindowSizeItem(GetType().Name);
            // V2pn: the main window also fits the screen on its first launch, before anything is saved.
            var isMain = this is Views.MainWindow;
            if (sizeItem is null && !isMain)
            {
                return;
            }

            // The main window reopens on the monitor it was left on, while that monitor is still there.
            var saved = isMain ? SavedScreen(sizeItem) : null;
            var screen = saved ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null)
            {
                return;
            }
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var workingArea = screen.WorkingArea;

            double width;
            double height;
            if (sizeItem is null)
            {
                // The design size, kept clear of the screen edges and the taskbar (1280x800 does not fit
                // a 1920x1080 laptop at 150%).
                width = Math.Min(Width, (workingArea.Width / scaling) - 32);
                height = Math.Min(Height, (workingArea.Height / scaling) - 32);
            }
            else
            {
                // Dialogs grew to fit the V2pn card frame; a width saved by v2rayN must not squeeze them back.
                // The main window is held by MinWidth instead: it reopens as narrow as it was left, and its
                // layout follows that width (MainWindow.Adaptive.cs).
                var designWidth = isMain || double.IsNaN(Width) ? 0 : Width;
                width = Math.Min(Math.Max(sizeItem.Width, designWidth), workingArea.Width / scaling);
                height = Math.Min(sizeItem.Height, workingArea.Height / scaling);
            }

            Width = width;
            Height = height;

            var frameDiff = (FrameSize ?? ClientSize) - ClientSize;
            var totalWidth = (width + frameDiff.Width) * scaling;
            var totalHeight = (height + frameDiff.Height) * scaling;

            var x = workingArea.X + ((workingArea.Width - totalWidth) / 2);
            var y = workingArea.Y + ((workingArea.Height - totalHeight) / 2);
            if (saved is not null && sizeItem is { X: { } sx, Y: { } sy })
            {
                x = Math.Clamp(sx, workingArea.X, Math.Max(workingArea.X, workingArea.X + workingArea.Width - totalWidth));
                y = Math.Clamp(sy, workingArea.Y, Math.Max(workingArea.Y, workingArea.Y + workingArea.Height - totalHeight));
            }
            Position = Common.OffscreenMode.Place(new PixelPoint((int)x, (int)y));
        }
        catch { }
    }

    // The screen that shows the saved window's title strip, or null (never saved, or that monitor is gone).
    private Screen? SavedScreen(WindowSizeItem? sizeItem)
    {
        if (sizeItem is not { X: { } x, Y: { } y })
        {
            return null;
        }
        foreach (var screen in Screens.All)
        {
            var strip = new PixelRect(x, y, Math.Max(1, (int)(sizeItem.Width * screen.Scaling)), (int)(40 * screen.Scaling));
            var visible = screen.WorkingArea.Intersect(strip);
            if (visible.Width >= 100 && visible.Height >= 10)
            {
                return screen;
            }
        }
        return null;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // The main window saves its normal bounds itself (MainWindow.StorageUI); Width here may be the maximized size.
        if (this is Views.MainWindow)
        {
            return;
        }
        try
        {
            ConfigTextHandler.SaveWindowSizeItem(GetType().Name, Width, Height);
        }
        catch { }
    }
}
