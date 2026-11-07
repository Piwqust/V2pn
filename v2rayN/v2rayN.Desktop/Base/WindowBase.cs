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
        }
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
            var sizeItem = ConfigHandler.GetWindowSizeItem(AppManager.Instance.Config, GetType().Name);
            if (sizeItem is null)
            {
                return;
            }

            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var workingArea = screen.WorkingArea;

            // Dialogs grew to fit the V2pn card frame; a width saved by v2rayN must not squeeze them back.
            var designWidth = double.IsNaN(Width) ? 0 : Width;
            var width = Math.Min(Math.Max(sizeItem.Width, designWidth), workingArea.Width / scaling);
            var height = Math.Min(sizeItem.Height, workingArea.Height / scaling);

            Width = width;
            Height = height;

            var frameDiff = (FrameSize ?? ClientSize) - ClientSize;
            var totalWidth = (width + frameDiff.Width) * scaling;
            var totalHeight = (height + frameDiff.Height) * scaling;

            var x = workingArea.X + ((workingArea.Width - totalWidth) / 2);
            var y = workingArea.Y + ((workingArea.Height - totalHeight) / 2);
            Position = new PixelPoint((int)x, (int)y);
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try
        {
            ConfigHandler.SaveWindowSizeItem(AppManager.Instance.Config, GetType().Name, Width, Height);
        }
        catch { }
    }
}
