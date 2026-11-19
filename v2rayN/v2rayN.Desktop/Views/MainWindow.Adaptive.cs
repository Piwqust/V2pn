using Avalonia.Automation;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// The main window follows its own width, the way a web page does:
/// <list type="bullet">
/// <item>Full: the server list beside the map, the tiles (update, DNS, settings) at the foot of the list,
/// the status card at the bottom.</item>
/// <item>NoRail: the same, named for when the tiles still sat on the map; the two differ no more.</item>
/// <item>Stacked: one column, the state, the server, Connect, the mode and the route on top and the
/// server list under them. No map, no status card and no Appearance button; the route gets a row of its
/// own under the mode. Too short for a useful list, the window keeps only the top part.</item>
/// </list>
/// Dragging the edge is enough. The button in the title bar jumps between a mini size and the last full
/// one; each is remembered.
/// </summary>
public partial class MainWindow
{
    private enum BodyTier { Full, NoRail, Stacked }

    // The map's width at which the tiles still clear the header (108 px a side) and the first-run card.
    // At the old minimum window width, 1000 px, it was 658 px.
    private const double FullStageMin = 650;
    // Below this the header's mode and route bar and the first-run card no longer fit beside the list.
    private const double TwoColumnStageMin = 470;
    // The body's margins and the gap between the list and the map.
    private const double BodyChrome = 30;

    private const double MiniWidth = 400;
    private const double MiniHeight = 640;
    private const double MiniMinWidth = 360;
    // A floor only: the one-column window's real minimum is its top part, measured (text size, density).
    private const double MiniMinHeight = 300;
    // The list needs room for about three rows; with less, the window is only the state and Connect.
    private const double StackedListMin = 150;
    private const double StackedStageBottom = 12;
    // Connect and the mode bar in a wide one-column window: as wide as Connect gets anyway.
    private const double StackedColumnMax = 520;
    // The widest in-window dialog (Appearance, 700 px) with its frame.
    private const double RoomyDialogWidth = 720;

    // Where the title-bar button returns to. "MainWindow" itself is what V2pn reopens with.
    private const string MiniKey = "MainWindow.Mini";
    private const string FullKey = "MainWindow.Full";

    private BodyTier _tier;
    private bool _laidOut;
    private bool _placed;
    // The height that shows the whole top part (or the whole first-run card) in one column.
    private double _stackedMinHeight;

    // The two-column values from the XAML, put back when the window widens again.
    private double _twoColumnMinHeight;
    private Thickness _headerMargin;
    private Thickness _headlineMargin;
    private Thickness _welcomeMargin;
    private double _welcomeWidth;
    private Avalonia.Layout.HorizontalAlignment _headerAlignment;
    private Avalonia.Layout.HorizontalAlignment _welcomeAlignment;

    private void InitAdaptive()
    {
        _twoColumnMinHeight = MinHeight;
        _headerMargin = panelHeader.Margin;
        _headlineMargin = panelHeadline.Margin;
        _welcomeMargin = scrollWelcome.Margin;
        _welcomeAlignment = scrollWelcome.HorizontalAlignment;
        _welcomeWidth = cardWelcome.Width;
        _headerAlignment = panelHeader.HorizontalAlignment;
        // Until WindowBase has applied the saved size, a short one-column window must not be stretched
        // to the two-column height.
        MinWidth = MiniMinWidth;
        MinHeight = MiniMinHeight;

        btnMini.Click += (_, _) => _ = SnapAsync();
        menuMiniWindow.Click += (_, _) => _ = SnapAsync();
        gridBody.SizeChanged += (_, _) => UpdateStackedRows();
        panelHeader.SizeChanged += (_, _) => UpdateStackedRows();
        cardWelcome.GetObservable(IsVisibleProperty).Subscribe(shown =>
        {
            scrollWelcome.IsVisible = shown;
            UpdateStackedRows();
        });
        pageOverlay.GetObservable(IsVisibleProperty).Subscribe(_ => UpdateStackedRows());
        Loaded += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _placed = true;
            ApplyWindowLimits();
        }, DispatcherPriority.Background);
    }

    // The client width is right even before the first layout and while the window is hidden in the tray.
    private double CurrentWidth() => ClientSize.Width > 0 ? ClientSize.Width : Bounds.Width;

    private BodyTier TierFor(double windowWidth)
    {
        if (windowWidth <= 0)
        {
            return BodyTier.Full;
        }
        var stage = windowWidth - BodyChrome - SidebarPixels(windowWidth);
        return stage >= FullStageMin ? BodyTier.Full
            : stage >= TwoColumnStageMin ? BodyTier.NoRail
            : BodyTier.Stacked;
    }

    // Re-arranges only when the width crosses into another layout or changes the sidebar's width.
    private void FollowWidth()
    {
        if (!_laidOut)
        {
            return;
        }
        var width = CurrentWidth();
        if (TierFor(width) != _tier || (_tier != BodyTier.Stacked && SidebarPixels(width) != _sidebarPixels))
        {
            ApplyLayout();
        }
    }

    // What the three layouts arrange differently. ApplyLayout then sets the sidebar's side and width.
    private void ArrangeForWidth()
    {
        _tier = TierFor(CurrentWidth());
        _laidOut = true;
        var stacked = _tier == BodyTier.Stacked;
        Classes.Set("stacked", stacked);
        if (stacked)
        {
            gridBody.ColumnDefinitions = new ColumnDefinitions("*");
            gridBody.RowDefinitions = new RowDefinitions("Auto,*");
            Grid.SetColumn(borderSidebar, 0);
            Grid.SetColumn(borderStage, 0);
            Grid.SetRow(borderStage, 0);
            Grid.SetRow(borderSidebar, 1);
            borderSidebar.Margin = new Thickness(0, 8, 0, 0);
        }
        else
        {
            gridBody.RowDefinitions = new RowDefinitions();
            Grid.SetRow(borderStage, 0);
            Grid.SetRow(borderSidebar, 0);
            borderSidebar.IsVisible = true;
        }
        panelHeadline.Margin = _tier == BodyTier.Full ? _headlineMargin : default;
        panelHeader.Margin = stacked ? new Thickness(12, 12, 12, 0) : _headerMargin;
        Tighten(stacked);
        // Connect and the mode bar span the column instead of shrinking to the bar's own width.
        var span = stacked ? Avalonia.Layout.HorizontalAlignment.Stretch : _headerAlignment;
        panelHeader.HorizontalAlignment = span;
        panelHeader.MaxWidth = stacked ? StackedColumnMax : double.PositiveInfinity;
        panelConnect.HorizontalAlignment = span;
        barConnection.HorizontalAlignment = span;
        ArrangeBar(stacked);
        cardWelcome.Width = stacked ? double.NaN : _welcomeWidth;
        scrollWelcome.Margin = stacked ? new Thickness(12, 56, 12, 12) : _welcomeMargin;
        scrollWelcome.HorizontalAlignment = stacked ? Avalonia.Layout.HorizontalAlignment.Stretch : _welcomeAlignment;
        // The title bar of a narrow window has no room for it, and the Appearance page is wider anyway.
        conTheme.IsVisible = !stacked;
        RefreshUpdateBadge();
        RefreshSnapButton();
        // V2pn: the partner's logo, background and plan follow the layout too (MainWindow.Edition.cs).
        ArrangeEdition();
        ApplyWindowLimits();
        UpdateStackedRows();
    }

    // The mini window is short on height, so its top part and the list's head close up: a smaller flag
    // and title, a lower Connect, tighter gaps. The bar's captions go too (V2pnStyles.axaml, .stacked):
    // the icons and the route's name say what each half is. The XAML holds the full-size values.
    private void Tighten(bool stacked)
    {
        pillState.Margin = stacked ? default : new Thickness(0, 0, 0, 4);
        borderHeaderFlag.Width = stacked ? 32 : 40;
        borderHeaderFlag.Height = stacked ? 24 : 30;
        borderHeaderFlag.Margin = stacked ? new Thickness(0, 0, 10, 0) : new Thickness(0, 0, 12, 0);
        panelConnect.Margin = stacked ? new Thickness(0, 8, 0, 0) : new Thickness(0, 12, 0, 0);
        panelConnect.Spacing = stacked ? 8 : 12;
        btnConnect.Height = stacked ? 44 : 50;
        btnConnect.CornerRadius = new CornerRadius(stacked ? 22 : 25);
        gridBar.RowSpacing = stacked ? 4 : 10;
        gridSearch.Margin = stacked ? new Thickness(10, 10, 10, 6) : new Thickness(12, 12, 12, 8);
    }

    // Mode and route side by side, or the route under the mode: side by side they need about 440 px.
    private void ArrangeBar(bool stacked)
    {
        gridBar.ColumnDefinitions = stacked ? new ColumnDefinitions("*") : new ColumnDefinitions("Auto,Auto,*");
        gridBar.RowDefinitions = stacked ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        sepBar.IsVisible = !stacked;
        Grid.SetColumn(panelRoute, stacked ? 0 : 2);
        Grid.SetRow(panelRoute, stacked ? 1 : 0);
    }

    // A two-column window shorter than its XAML minimum would push the status card into the header; a
    // one-column one must keep its top part whole.
    private void ApplyWindowLimits()
    {
        var min = !_placed ? MiniMinHeight
            : _tier == BodyTier.Stacked ? Math.Max(MiniMinHeight, _stackedMinHeight)
            : _twoColumnMinHeight;
        if (MinHeight != min)
        {
            MinHeight = min;
        }
    }

    // The button shows where it leads: arrows in for the mini size, arrows out for the full one.
    private void RefreshSnapButton()
    {
        var stacked = _tier == BodyTier.Stacked;
        var label = stacked ? L.FullWindow : L.MiniWindow;
        var icon = this.FindResource(stacked ? "SemiIconExpand" : "SemiIconShrink") as Geometry;
        iconMini.Data = icon;
        iconMenuMini.Data = icon;
        ToolTip.SetTip(btnMini, label);
        AutomationProperties.SetName(btnMini, label);
        menuMiniWindow.Header = label;
    }

    // The list takes what the top part leaves. The first-run card and the pages (log, table) take the
    // whole window, and so does the top part when too little would be left for the list.
    private void UpdateStackedRows()
    {
        if (_tier != BodyTier.Stacked || gridBody.RowDefinitions.Count != 2)
        {
            return;
        }
        var stage = Math.Ceiling(panelHeader.Margin.Top + panelHeader.Bounds.Height + StackedStageBottom);
        var room = gridBody.Bounds.Height - stage - borderSidebar.Margin.Top;
        // The first-run card scrolls, so only the top part has to fit.
        if (panelHeader.Bounds.Height > 0)
        {
            _stackedMinHeight = Math.Ceiling(gridBody.Bounds.Y + stage + gridBody.Margin.Bottom);
            ApplyWindowLimits();
        }
        var list = !cardWelcome.IsVisible && !pageOverlay.IsVisible && room >= StackedListMin;
        borderSidebar.IsVisible = list;
        var top = list ? new GridLength(stage) : GridLength.Star;
        var bottom = list ? GridLength.Star : new GridLength(0);
        if (gridBody.RowDefinitions[0].Height != top)
        {
            gridBody.RowDefinitions[0].Height = top;
        }
        if (gridBody.RowDefinitions[1].Height != bottom)
        {
            gridBody.RowDefinitions[1].Height = bottom;
        }
    }

    /// <summary>
    /// The title-bar button: from any wider layout to the mini size, from the one-column layout back to
    /// the last wider size. The size being left is saved first, so the next press brings it back.
    /// </summary>
    private async Task SnapAsync()
    {
        try
        {
            var toMini = _tier != BodyTier.Stacked;
            StorageUI(toMini ? FullKey : MiniKey);
            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
            }
            var saved = ConfigTextHandler.GetWindowSizeItem(toMini ? MiniKey : FullKey);
            var size = saved != null ? new Size(saved.Width, saved.Height) : default;
            // A size saved before the sidebar got wider or narrower may now fall in the other layout.
            if (saved == null || (TierFor(size.Width) == BodyTier.Stacked) != toMini)
            {
                (size, saved) = (toMini ? new Size(MiniWidth, MiniHeight) : new Size(1280, 800), null);
            }
            // The short mini height must get through the two-column minimum still in force.
            MinHeight = MiniMinHeight;
            PlaceWindow(size, saved is { X: { } x, Y: { } y } ? new PixelPoint(x, y) : null);
            if (!toMini && saved?.IsMaximized == true)
            {
                WindowState = WindowState.Maximized;
            }
            _ = Motion.Enter(gridBody, dy: 0, ms: 160);
            await ConfigTextHandler.SaveConfig();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn window size", ex);
        }
    }

    // Sizes the window on the screen it is on: at the saved position when there is one, else around the
    // centre of the window it replaces. It never ends up past the edge of the screen.
    private void PlaceWindow(Size size, PixelPoint? saved)
    {
        var screen = (saved is { } point ? Screens.ScreenFromPoint(point) : null)
            ?? Screens.ScreenFromWindow(this)
            ?? Screens.Primary;
        if (screen == null)
        {
            Width = size.Width;
            Height = size.Height;
            return;
        }
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var area = screen.WorkingArea;
        var outer = FrameSize ?? ClientSize;
        var frame = outer - ClientSize;
        var width = Math.Min(size.Width, (area.Width / scaling) - frame.Width);
        var height = Math.Min(size.Height, (area.Height / scaling) - frame.Height);
        var newWidth = (width + frame.Width) * scaling;
        var newHeight = (height + frame.Height) * scaling;

        double x;
        double y;
        if (saved is { } at)
        {
            (x, y) = (at.X, at.Y);
        }
        else
        {
            x = Position.X + (((outer.Width * scaling) - newWidth) / 2);
            y = Position.Y + (((outer.Height * scaling) - newHeight) / 2);
        }
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - newWidth));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - newHeight));

        Width = width;
        Height = height;
        Position = Common.OffscreenMode.Place(new PixelPoint((int)x, (int)y));
        _normalSize = new Size(width, height);
        _normalPosition = Position;
    }

    /// <summary>
    /// The in-window dialogs (updates, backup, a QR code) are wider than a narrow window: the full size
    /// opens for them, and the narrow one comes back when they close.
    /// </summary>
    internal async Task<object?> ShowRoomyDialogAsync(object content)
    {
        var widened = _tier == BodyTier.Stacked && CurrentWidth() < RoomyDialogWidth;
        if (widened)
        {
            await SnapAsync();
        }
        try
        {
            return await DialogHost.Show(content);
        }
        finally
        {
            if (widened && _tier != BodyTier.Stacked)
            {
                await SnapAsync();
            }
        }
    }

    internal static Task<object?> ShowRoomyDialog(object content) =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow main }
            ? main.ShowRoomyDialogAsync(content)
            : DialogHost.Show(content);
}
