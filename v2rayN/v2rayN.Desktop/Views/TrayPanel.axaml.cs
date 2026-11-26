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
/// V2pn: what the tray icon opens (on Windows TrayMenuPlacement puts it into Avalonia's tray window in place of the
/// menu; on a Mac MainWindow.Tray.cs shows it under the menu bar). It stays open while the connection, the mode or
/// the route changes, so the result is seen where it was asked for; whatever needs the window opens the window and
/// closes the panel.
/// </summary>
public partial class TrayPanel : UserControl
{
    /// <summary>Servers beside "Fastest" in the two columns of cards.</summary>
    internal const int QuickServers = 3;

    private readonly MainWindow? _main;
    private Action _close;
    private bool _sync;
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
        btnServer.Click += (_, _) => Leave(m => m.TrayShowServers());
        lstMode.SelectionChanged += (_, _) =>
        {
            if (!_sync && lstMode.SelectedIndex >= 0)
            {
                _main?.TraySetMode(lstMode.SelectedIndex == 1);
            }
        };
        lstRoute.SelectionChanged += async (_, _) =>
        {
            if (_sync || _main == null)
            {
                return;
            }
            if (lstRoute.SelectedItem == itemRouteRussia)
            {
                await _main.SelectPresetFromTrayAsync(RoutingPresetKind.RussiaDirect);
            }
            else if (lstRoute.SelectedItem == itemRouteBlocked)
            {
                await _main.SelectPresetFromTrayAsync(RoutingPresetKind.BlockedOnly);
            }
            else if (lstRoute.SelectedItem == itemRouteAll)
            {
                await _main.SelectPresetFromTrayAsync(RoutingPresetKind.All);
            }
        };

        btnPaste.Click += (_, _) => Leave(m => { _ = m.TrayPasteAsync(); });
        btnUpdate.Click += (_, _) => Leave(m => m.TrayUpdateSubscriptions(viaVpn: false));
        btnUpdateVpn.Click += (_, _) => Leave(m => m.TrayUpdateSubscriptions(viaVpn: true));
        btnTerminal.Click += (_, _) =>
        {
            // Copied without leaving: the icon turns green and says so, and the panel closes a moment later.
            _main?.TrayCopyProxyCommand();
            btnTerminal.Classes.Add("done");
            ToolTip.SetTip(btnTerminal, L.TrayCopied);
            AutomationPropertiesName(btnTerminal, L.TrayCopied);
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
        btnTerminal.Classes.Remove("done");
        ToolTip.SetTip(btnTerminal, L.TrayCopyProxyCmd);
        AutomationPropertiesName(btnTerminal, L.TrayCopyProxyCmd);
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
                panelState.Classes.Set(c, c == v.Kind);
            }
            txtState.Text = v.Connected && v.Delay > 0
                ? $"{v.StateLabel.ToUpper(CultureInfo.CurrentUICulture)} · {L.Ms(v.Delay)}"
                : v.StateLabel.ToUpper(CultureInfo.CurrentUICulture);

            imgFlag.Source = v.Flag;
            borderFlag.IsVisible = v.Flag != null;
            borderGlobe.IsVisible = v.Flag == null;
            txtTitle.Text = v.Title;
            txtSubtitle.Text = v.Subtitle;
            txtSubtitle.IsVisible = !string.IsNullOrEmpty(v.Subtitle);
            AutomationPropertiesName(btnServer, string.Join(", ", new[] { v.Title, v.Subtitle, v.StateLabel }.Where(t => !string.IsNullOrEmpty(t))));

            var action = !v.Connected ? L.Connect : v.Busy ? L.StateConnecting : L.Disconnect;
            ToolTip.SetTip(btnConnect, action);
            AutomationPropertiesName(btnConnect, action);
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

            var custom = v.Route == null && !string.IsNullOrEmpty(v.CustomRoute);
            itemRouteCustom.IsVisible = custom;
            txtRouteCustom.Text = v.CustomRoute;
            ToolTip.SetTip(itemRouteCustom, v.CustomRoute);
            AutomationPropertiesName(itemRouteCustom, v.CustomRoute ?? string.Empty);
            lstRoute.SelectedItem = v.Route switch
            {
                RoutingPresetKind.RussiaDirect => itemRouteRussia,
                RoutingPresetKind.BlockedOnly => itemRouteBlocked,
                RoutingPresetKind.All => itemRouteAll,
                _ => custom ? itemRouteCustom : null,
            };
        }
        finally
        {
            _sync = false;
        }
    }

    private void FillServers(TrayView v)
    {
        gridServers.IsVisible = v.HasServers;
        txtNoServers.IsVisible = !v.HasServers;
        // Rebuilt only when something in it changed; a new ping every few seconds must not flicker the cards.
        var stamp = string.Join("|", v.Servers.Select(s => $"{s.IndexId}:{s.Delay}:{s.Active}:{s.Name}")) + $"|{v.HasServers}";
        if (stamp == _shownServers)
        {
            return;
        }
        _shownServers = stamp;
        gridServers.Children.Clear();
        if (!v.HasServers)
        {
            return;
        }
        gridServers.Children.Add(Card(Icon("SemiIconBolt"), L.TrayFastest, 0, false, () => _main?.TrayFastestAsync() ?? Task.CompletedTask));
        foreach (var s in v.Servers.Take(QuickServers))
        {
            var id = s.IndexId;
            gridServers.Children.Add(Card(FlagOf(s), s.Name, s.Delay, s.Active, () => _main?.TrayConnectToAsync(id) ?? Task.CompletedTask));
        }
    }

    private Button Card(Control lead, string name, int delay, bool active, Func<Task> click)
    {
        // No third column without a ping: its spacing alone would cut "Fastest" short.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(delay != 0 ? "Auto,*,Auto" : "Auto,*"), ColumnSpacing = 7 };
        grid.Children.Add(lead);
        var text = new TextBlock { Text = name, Classes = { "tname" } };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (delay != 0)
        {
            var ping = new TextBlock
            {
                Text = delay > 0 ? L.Ms(delay) : L.Timeout,
                Classes = { "tping", delay switch { > 0 and <= 300 => "good", > 300 and <= 800 => "mid", _ => "bad" } },
            };
            Grid.SetColumn(ping, 2);
            grid.Children.Add(ping);
        }
        var card = new Button
        {
            Content = grid,
            Classes = { "tcard" },
            Theme = this.FindResource("V2SideRow") as ControlTheme,
        };
        card.Classes.Set("active", active);
        ToolTip.SetTip(card, name);
        AutomationPropertiesName(card, delay > 0 ? $"{name}, {L.Ms(delay)}" : name);
        card.Click += async (_, _) => await click();
        return card;
    }

    private Control FlagOf(TrayServer s)
    {
        if (s.Flag == null)
        {
            return Icon("SemiIconGlobeStroke");
        }
        return new Border { Classes = { "rowflag" }, Child = new Image { Source = s.Flag, Stretch = Stretch.UniformToFill } };
    }

    private static PathIcon Icon(string key)
    {
        var icon = new PathIcon { Classes = { "rowicon" } };
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable(key));
        return icon;
    }

    private static void AutomationPropertiesName(Control control, string name) =>
        Avalonia.Automation.AutomationProperties.SetName(control, name);
}
