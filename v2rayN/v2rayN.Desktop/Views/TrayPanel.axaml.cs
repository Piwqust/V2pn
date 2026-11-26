using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>The panel's picture of the app: what MainWindow knows, taken in one go (MainWindow.Tray.cs).</summary>
internal sealed record TrayView(
    string Kind,
    string StateLabel,
    bool Connected,
    bool Busy,
    bool HasServers,
    Bitmap? Flag,
    string Title,
    string? Subtitle,
    int Delay,
    bool Tun,
    bool NeedsAdmin,
    ESysProxyType Method,
    bool PacAvailable,
    RoutingPresetKind? Route,
    string? CustomRoute,
    IReadOnlyList<TrayServer> Servers);

internal sealed record TrayServer(string IndexId, string Name, Bitmap? Flag, int Delay, bool Active);

/// <summary>
/// V2pn: what a right click on the tray icon opens on Windows (TrayPanelHost puts it into Avalonia's tray
/// window in place of the menu). It stays open while the connection, the mode or the route changes, so the
/// result is seen where it was asked for; whatever needs the window opens the window and closes the panel.
/// </summary>
public partial class TrayPanel : UserControl
{
    internal const int QuickServers = 3;

    private static readonly ESysProxyType[] WindowsMethods = [ESysProxyType.ForcedChange, ESysProxyType.Unchanged, ESysProxyType.Pac];

    private readonly MainWindow? _main;
    private Action _close;
    private bool _sync;
    private ESysProxyType[] _methods = [];
    private string? _shownServers;

    public TrayPanel() : this(null, () => { })
    {
    }

    internal TrayPanel(MainWindow? main, Action close)
    {
        _main = main;
        _close = close;
        InitializeComponent();

        btnConnect.Click += (_, _) => _main?.ToggleConnectionFromTray();
        btnAllServers.Click += (_, _) => Leave(m => m.TrayShowServers());
        lstMode.SelectionChanged += (_, _) =>
        {
            if (!_sync && lstMode.SelectedIndex >= 0)
            {
                _main?.TraySetMode(lstMode.SelectedIndex == 1);
            }
        };
        lstMethod.SelectionChanged += (_, _) =>
        {
            if (!_sync && lstMethod.SelectedIndex >= 0 && lstMethod.SelectedIndex < _methods.Length)
            {
                _main?.TraySetMethod(_methods[lstMethod.SelectedIndex]);
            }
        };
        btnRouteRussia.Click += async (_, _) => await Route(RoutingPresetKind.RussiaDirect);
        btnRouteBlocked.Click += async (_, _) => await Route(RoutingPresetKind.BlockedOnly);
        btnRouteAll.Click += async (_, _) => await Route(RoutingPresetKind.All);
        btnRouteCustom.Click += (_, _) => Leave(m => m.TrayShowRoutes());

        btnPaste.Click += (_, _) => Leave(m => { _ = m.TrayPasteAsync(); });
        btnUpdate.Click += (_, _) => Leave(m => m.TrayUpdateSubscriptions(viaVpn: false));
        btnUpdateVpn.Click += (_, _) => Leave(m => m.TrayUpdateSubscriptions(viaVpn: true));
        btnTerminal.Click += (_, _) =>
        {
            // Copied without leaving: the tile says so, and the panel closes a moment later.
            _main?.TrayCopyProxyCommand();
            txtTerminal.Text = L.TrayTileCopied;
            DispatcherTimer.RunOnce(_close, TimeSpan.FromMilliseconds(700));
        };
        btnSettings.Click += (_, _) => Leave(m => m.TrayOpenSettings());
        btnOpenWide.Click += (_, _) => Leave(m => m.ShowHideWindow(true));
        btnQuit.Click += (_, _) => Leave(static m => { _ = MainWindow.TrayQuitAsync(); });

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _close();
            }
        };
    }

    /// <summary>The window the panel is in now: the same panel moves into each new tray window, already built and styled.</summary>
    internal void MoveTo(Window window, Action close)
    {
        if (Parent is ContentControl old && old != window)
        {
            old.Content = null;
        }
        _close = close;
        txtTerminal.Text = L.TrayTileTerminal;
        window.Content = this;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_main != null)
        {
            _main.TrayChanged += Refresh;
            Refresh();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_main != null)
        {
            _main.TrayChanged -= Refresh;
        }
    }

    // Closes the panel first: what follows opens the window, which takes the focus anyway.
    private void Leave(Action<MainWindow> action)
    {
        _close();
        if (_main is { } main)
        {
            Dispatcher.UIThread.Post(() => action(main));
        }
    }

    private async Task Route(RoutingPresetKind kind)
    {
        if (_main != null)
        {
            await _main.SelectPresetFromTrayAsync(kind);
        }
    }

    internal void Refresh()
    {
        if (_main?.TrayViewNow() is not { } v)
        {
            return;
        }
        _sync = true;
        try
        {
            foreach (var c in new[] { "safe", "unsafe", "warn", "checking" })
            {
                pillState.Classes.Set(c, c == v.Kind);
            }
            txtState.Text = v.StateLabel.ToUpper(CultureInfo.CurrentUICulture);
            AutomationPropertiesName(pillState, v.StateLabel);

            imgFlag.Source = v.Flag;
            borderFlag.IsVisible = v.Flag != null;
            borderGlobe.IsVisible = v.Flag == null;
            txtTitle.Text = v.Title;
            txtSubtitle.Text = v.Subtitle;
            txtSubtitle.IsVisible = !string.IsNullOrEmpty(v.Subtitle);
            SetPing(pillPing, txtPing, v.Connected ? v.Delay : 0);

            txtConnect.Text = !v.Connected ? L.Connect : v.Busy ? L.StateConnecting : L.Disconnect;
            AutomationPropertiesName(btnConnect, txtConnect.Text);
            btnConnect.Classes.Set("accent", !v.Connected);
            btnConnect.Classes.Set("disconnect", v.Connected);
            btnConnect.Classes.Set("safe", v.Connected && v.Kind == "safe");
            btnConnect.Classes.Set("warn", v.Connected && v.Kind == "warn");
            btnConnect.IsEnabled = v.HasServers;

            FillServers(v);

            lstMode.SelectedIndex = v.Tun ? 1 : 0;
            badgeAdmin.IsVisible = v.NeedsAdmin;
            ToolTip.SetTip(itemProxy, L.ModeProxyTip);
            ToolTip.SetTip(itemVpn, v.NeedsAdmin ? $"{L.ModeVpnTip} {L.ModeVpnNeedsAdmin}" : L.ModeVpnTip);
            FillMethods(v);

            var custom = v.Route == null && !string.IsNullOrEmpty(v.CustomRoute);
            btnRouteRussia.Classes.Set("active", v.Route == RoutingPresetKind.RussiaDirect);
            btnRouteBlocked.Classes.Set("active", v.Route == RoutingPresetKind.BlockedOnly);
            btnRouteAll.Classes.Set("active", v.Route == RoutingPresetKind.All);
            btnRouteCustom.IsVisible = custom;
            txtRouteCustom.Text = v.CustomRoute;
            AutomationPropertiesName(btnRouteCustom, v.CustomRoute ?? string.Empty);
        }
        finally
        {
            _sync = false;
        }
    }

    // In Proxy mode, how the system proxy is set: the choice from Settings → General, one click away.
    private void FillMethods(TrayView v)
    {
        var methods = v.PacAvailable ? WindowsMethods : WindowsMethods[..2];
        if (!methods.SequenceEqual(_methods))
        {
            _methods = methods;
            lstMethod.ItemsSource = methods.Select(m => new ListBoxItem
            {
                Content = m switch
                {
                    ESysProxyType.Pac => L.TrayMethodPac,
                    ESysProxyType.Unchanged => L.TrayMethodPort,
                    _ => L.TrayMethodSystem,
                },
                [ToolTip.TipProperty] = m switch
                {
                    ESysProxyType.Pac => L.ProxyMethodPac,
                    ESysProxyType.Unchanged => L.ProxyModeKeep,
                    _ => L.ProxyMethodSystem,
                },
            }).ToList();
        }
        lstMethod.SelectedIndex = Array.IndexOf(_methods, v.Method);
        lstMethod.IsVisible = !v.Tun;
    }

    private void FillServers(TrayView v)
    {
        panelServers.IsVisible = true;
        // Rebuilt only when something in it changed; a new ping every few seconds must not flicker the rows.
        var stamp = string.Join("|", v.Servers.Select(s => $"{s.IndexId}:{s.Delay}:{s.Active}")) + $"|{v.HasServers}";
        if (stamp == _shownServers)
        {
            return;
        }
        _shownServers = stamp;
        listServers.Children.Clear();
        if (!v.HasServers)
        {
            btnAllServers.IsVisible = false;
            listServers.Children.Add(new TextBlock
            {
                Text = L.TrayNoServers,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 2, 4, 4),
                Classes = { "muted", "small" },
            });
            return;
        }
        btnAllServers.IsVisible = true;
        listServers.Children.Add(Row(Icon("SemiIconBolt"), L.Fastest, 0, false, () => _main?.TrayFastestAsync() ?? Task.CompletedTask));
        foreach (var s in v.Servers.Take(QuickServers))
        {
            var id = s.IndexId;
            listServers.Children.Add(Row(FlagOf(s), s.Name, s.Delay, s.Active, () => _main?.TrayConnectToAsync(id) ?? Task.CompletedTask));
        }
    }

    private Button Row(Control lead, string name, int delay, bool active, Func<Task> click)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(lead);
        var text = new TextBlock { Text = name, Classes = { "tname" } };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (delay != 0)
        {
            var pill = new Border { Classes = { "pill" } };
            var label = new TextBlock();
            pill.Child = label;
            SetPing(pill, label, delay);
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);
        }
        var check = Icon("SemiIconTickCircle");
        check.Classes.Add("check");
        Grid.SetColumn(check, 3);
        grid.Children.Add(check);

        var row = new Button
        {
            Content = grid,
            Classes = { "trow" },
            Theme = this.FindResource("V2SideRow") as ControlTheme,
        };
        row.Classes.Set("active", active);
        AutomationPropertiesName(row, delay > 0 ? $"{name}, {L.Ms(delay)}" : name);
        row.Click += async (_, _) => await click();
        return row;
    }

    private Control FlagOf(TrayServer s)
    {
        if (s.Flag == null)
        {
            var globe = Icon("SemiIconGlobeStroke");
            globe.Classes.Add("rowicon");
            return globe;
        }
        return new Border { Classes = { "rowflag" }, Child = new Image { Source = s.Flag, Stretch = Stretch.UniformToFill } };
    }

    private PathIcon Icon(string key)
    {
        var icon = new PathIcon();
        if (key == "SemiIconBolt")
        {
            icon.Classes.Add("rowicon");
        }
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable(key));
        return icon;
    }

    private static void SetPing(Border pill, TextBlock text, int delay)
    {
        var tier = delay switch
        {
            0 => "none",
            > 0 and <= 300 => "good",
            > 300 and <= 800 => "mid",
            _ => "bad",
        };
        foreach (var c in new[] { "good", "mid", "bad" })
        {
            pill.Classes.Set(c, c == tier);
        }
        pill.IsVisible = delay != 0;
        text.Text = delay > 0 ? L.Ms(delay) : delay < 0 ? L.Timeout : string.Empty;
    }

    private static void AutomationPropertiesName(Control control, string name) =>
        Avalonia.Automation.AutomationProperties.SetName(control, name);
}
