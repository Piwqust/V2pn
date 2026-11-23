using v2rayN.Desktop.Common;
using v2rayN.Desktop.Converters;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

// V2pn on a Mac: the menu bar (File, Edit, Connection, Window, Help; the app menu is App.axaml.cs), the status
// item's menu and the Dock menu. They say and do what the window does, through the window's own methods, so the
// three can never disagree with it.
//
// The parts that follow the data (the state line, the servers, the route) are put together when macOS is about to
// show a menu (NativeMenu.NeedsUpdate), not on every change. macOS also asks before it looks a ⌘ key up in the menu
// bar, so the work is skipped while nothing a menu shows has changed (MacMenuStamp).
public partial class MainWindow
{
    private const int MacMenuCountries = 40;
    private const int MacMenuServersPerCountry = 30;
    private const int MacMenuRecent = 5;

    private NativeMenu? _macConnectionMenu;
    private NativeMenu? _macStatusMenu;
    private NativeMenu? _macDockMenu;
    private string? _macConnectionStamp;
    private string? _macStatusStamp;
    private string? _macDockStamp;
    private bool _macDockQueued;

    // The height of V2pn's own title bar (the first row of MainWindow.axaml).
    private const double MacTitleBarHeight = 40;

    /// <summary>
    /// The close, minimize and zoom buttons sit in the middle of V2pn's title bar, on one line with the menu button
    /// and the name, and the menu button starts clear of them. In full screen macOS hides them, so the menu button
    /// goes back to the edge.
    /// </summary>
    private void InitMacWindowButtons()
    {
        void Place()
        {
            var fullScreen = WindowState == WindowState.FullScreen;
            btnAppMenu.Margin = new Thickness(fullScreen ? 8 : 78, 0, 2, 0);
            if (!fullScreen && IsVisible)
            {
                MacAppUtils.CenterWindowButtons(this, MacTitleBarHeight);
            }
        }
        // AppKit lays the buttons out again when it resizes the window, shows it, gives it a new title or brings it
        // back from full screen (an animation of most of a second), so they are placed once more after it has.
        void PlaceSoon()
        {
            Place();
            Dispatcher.UIThread.Post(Place, DispatcherPriority.Background);
        }
        Opened += (_, _) => PlaceSoon();
        Activated += (_, _) => PlaceSoon();
        Deactivated += (_, _) => PlaceSoon();
        Resized += (_, _) => PlaceSoon();
        this.GetObservable(WindowStateProperty).Subscribe(_ =>
        {
            PlaceSoon();
            DispatcherTimer.RunOnce(Place, TimeSpan.FromMilliseconds(900));
        });
        this.GetObservable(IsVisibleProperty).Subscribe(_ => PlaceSoon());
        // The title carries the connection state (RefreshTray), and a new title lays the title bar out again.
        this.GetObservable(TitleProperty).Subscribe(_ => PlaceSoon());
        Place();
    }

    private void InitMacMenus()
    {
        if (!OperatingSystem.IsMacOS() || Design.IsDesignMode)
        {
            return;
        }
        _macConnectionMenu = new NativeMenu();
        _macConnectionMenu.NeedsUpdate += (_, _) => FillMacConnectionMenu();
        var bar = new NativeMenu();
        bar.Items.Add(MacMenus.Submenu(L.MacFile, MacMenus.Menu(
            MacMenus.Item(L.MacAddFromClipboard, () => MacInWindow(AddServerViaClipboardAsync)),
            MacMenus.Item(L.MacAddSubscription, () => MacInWindow(() => ViewModel?.ProfilesViewModel.AddSubCmd.Execute().Subscribe()), new KeyGesture(Key.N, MacMenus.Cmd)),
            MacMenus.Item(L.MacQrFromImage, () => MacInWindow(() => ViewModel?.AddServerViaImageCmd.Execute().Subscribe())),
            MacMenus.Separator(),
            MacMenus.Item(L.MacCloseWindow, () => MacMenus.ActiveWindow()?.Close(), new KeyGesture(Key.W, MacMenus.Cmd)))));
        bar.Items.Add(MacMenus.Submenu(L.MacEdit, MacMenus.EditMenu(() => MacInWindow(() =>
        {
            txtSearch.Focus();
            txtSearch.SelectAll();
        }))));
        bar.Items.Add(MacMenus.Submenu(L.Connection, _macConnectionMenu));
        bar.Items.Add(MacMenus.Submenu(L.MacWindow, MacMenus.Menu(
            MacMenus.Item(L.MacMinimize, () => MacMenus.ActiveWindow()?.WindowState = WindowState.Minimized, new KeyGesture(Key.M, MacMenus.Cmd)),
            MacMenus.Item(L.MacZoom, () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized),
            MacMenus.Separator(),
            MacMenus.Item(L.MiniWindow, () => MacInWindow(() => { _ = SnapAsync(); })),
            MacMenus.Item(L.MenuLog, () => MacInWindow(() => ShowPage(L.MenuLog, MsgPage))),
            MacMenus.Separator(),
            MacMenus.Item(L.MacMainWindow, () => ShowHideWindow(true), new KeyGesture(Key.D0, MacMenus.Cmd)))));
        bar.Items.Add(MacMenus.Submenu(L.MacHelp, MacMenus.Menu(
            MacMenus.Item(L.MacDocs, () => ProcUtils.ProcessStart(V2pnInfo.DocsUrl)),
            MacMenus.Item(L.HelpSite, () => ProcUtils.ProcessStart(V2pnInfo.SiteUrl)),
            MacMenus.Item(L.MacWhatsNew, () => ProcUtils.ProcessStart(V2pnInfo.ReleasesUrl)),
            MacMenus.Separator(),
            MacMenus.Item(L.DiagMenu, () => MacInWindow(ShowDiagnosticsAsync)),
            MacMenus.Item(L.HelpReport, () => ProcUtils.ProcessStart($"{V2pnInfo.RepoUrl}/issues")))));
        NativeMenu.SetMenu(this, bar);
        FillMacConnectionMenu();
    }

    /// <summary>The status item's menu and the Dock's, which belong to the app (App.axaml.cs hands them over).</summary>
    internal void MacAdoptAppMenus(NativeMenu statusMenu, NativeMenu dockMenu)
    {
        _macStatusMenu = statusMenu;
        _macStatusMenu.NeedsUpdate += (_, _) => FillMacStatusMenu();
        _macDockMenu = dockMenu;
        _macDockMenu.NeedsUpdate += (_, _) => FillMacDockMenu();
        FillMacStatusMenu();
        FillMacDockMenu();
    }

    #region What the app menu and the menus' items do

    /// <summary>
    /// An action that happens in the window: the window comes up first, as it does for the same item of the tray
    /// menu on Windows. While an in-window dialog is open nothing starts behind it.
    /// </summary>
    private void MacInWindow(Action action)
    {
        ShowHideWindow(true);
        if (!IsDialogOpen())
        {
            action();
        }
    }

    private void MacInWindow(Func<Task> action) => MacInWindow(() => { _ = MacGuardAsync(action); });

    private static async Task MacGuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(MacMenus), ex);
        }
    }

    internal void MacOpenSettings() => MacInWindow(() => ViewModel?.OptionSettingCmd.Execute().Subscribe());

    internal void MacCheckForUpdates() => MacInWindow(() => MenuCheckUpdate_Click(null, new RoutedEventArgs()));

    /// <summary>The system's About panel: the version, the installed cores, where to read more and the licences.</summary>
    internal async void MacShowAbout()
    {
        try
        {
            var cores = await V2pnCores.InstalledSummaryAsync();
            var lines = new List<object[]> { new object[] { L.MacAboutTagline } };
            if (cores.IsNotEmpty())
            {
                lines.Add([string.Format(L.MacAboutCores, cores)]);
            }
            lines.Add([string.Empty]);
            lines.Add([
                new V2pnCredits.Link(L.MacAboutSite, V2pnInfo.SiteUrl), "  ·  ",
                new V2pnCredits.Link(L.MacAboutDocs, V2pnInfo.DocsUrl), "  ·  ",
                new V2pnCredits.Link(L.MacWhatsNew, V2pnInfo.ReleasesUrl),
            ]);
            lines.Add([
                new V2pnCredits.Link(L.MacAboutSource, V2pnInfo.RepoUrl), "  ·  ",
                new V2pnCredits.Link(L.MacAboutLicense, $"{V2pnInfo.RepoUrl}/blob/main/LICENSE"),
            ]);
            lines.Add([string.Empty]);
            lines.Add([new V2pnCredits.Link(L.MacAboutBasedOn, $"{Global.GithubUrl}/2dust/v2rayN")]);
            lines.Add([
                $"{L.MacAboutThanks} ",
                new V2pnCredits.Link("Xray", "https://github.com/XTLS/Xray-core"), ", ",
                new V2pnCredits.Link("sing-box", "https://github.com/SagerNet/sing-box"), ", ",
                new V2pnCredits.Link("Avalonia", "https://github.com/AvaloniaUI/Avalonia"), ", ",
                new V2pnCredits.Link("Semi Design", "https://github.com/irihitech/Semi.Avalonia"), ", ",
                new V2pnCredits.Link("Lucide", "https://lucide.dev"), ", ",
                new V2pnCredits.Link("Inter", "https://rsms.me/inter/"),
            ]);
            MacAppUtils.ShowAboutPanel(V2pnInfo.DisplayVersion, V2pnCredits.Rtf(lines));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(MacShowAbout), ex);
        }
    }

    private async Task MacSetModeAsync(bool tun)
    {
        // VPN mode asks for the administrator's password in the window.
        if (tun && ViewModel?.StatusBarViewModel is { } status && !status.AllowEnableTun())
        {
            ShowHideWindow(true);
        }
        // The window's own switch: it asks for the rights, saves the choice and reconnects when connected.
        lstMode.SelectedIndex = tun ? 1 : 0;
        await Task.CompletedTask;
    }

    private async Task MacSelectRouteAsync(string id)
    {
        if (RoutingPresets.KindOf(id) is { } kind)
        {
            await SelectPresetFromTrayAsync(kind);
            return;
        }
        await ActivateRoutingAsync(id, sourcesChanged: false);
        RefreshRouteUi();
    }

    private void MacShowCountries() => MacInWindow(() => SelectTab(SidebarTab.Countries));

    #endregion What the app menu and the menus' items do

    #region The menus

    /// <summary>The menu bar's Connection menu.</summary>
    private void FillMacConnectionMenu()
    {
        if (_macConnectionMenu == null || !MacStampChanged(ref _macConnectionStamp))
        {
            return;
        }
        var status = ViewModel?.StatusBarViewModel;
        var items = new List<NativeMenuItemBase>
        {
            MacMenus.Label(MacStateLine()),
            MacMenus.Item(MacConnectText(), ToggleConnectionFromTray),
            MacMenus.Separator(),
            MacMenus.Submenu(L.MacServers, MacServersMenu()),
            MacMenus.Submenu(L.MacMode, MacModeMenu()),
            MacMenus.Submenu(L.MacRoute, MacRouteMenu()),
            MacMenus.Submenu(L.MacSubscriptions, MacSubscriptionsMenu()),
            MacMenus.Separator(),
        };
        if (status != null)
        {
            items.Add(MacMenus.Item(L.CheckConnection, async () => await status.TestServerAvailability()));
        }
        items.Add(MacMenus.Item(L.MenuRestartConnection, async () =>
        {
            if (ViewModel is { } vm)
            {
                await vm.Reload();
            }
        }));
        items.Add(MacMenus.Item(L.TestPingAll, PingAllAsync, new KeyGesture(Key.R, MacMenus.Cmd | KeyModifiers.Shift)));
        MacMenus.Fill(_macConnectionMenu, items);
    }

    /// <summary>The status item: the state, the everyday actions, then what the window is for.</summary>
    private void FillMacStatusMenu()
    {
        if (_macStatusMenu == null || !MacStampChanged(ref _macStatusStamp))
        {
            return;
        }
        var status = ViewModel?.StatusBarViewModel;
        var items = new List<NativeMenuItemBase>
        {
            MacMenus.Label(MacStateLine()),
            MacMenus.Item(MacConnectText(), ToggleConnectionFromTray),
            MacMenus.Separator(),
            MacMenus.Submenu(L.MacServers, MacServersMenu()),
            MacMenus.Submenu(L.MacMode, MacModeMenu()),
            MacMenus.Submenu(L.MacRoute, MacRouteMenu()),
            MacMenus.Submenu(L.MacSubscriptions, MacSubscriptionsMenu()),
            MacMenus.Separator(),
            MacMenus.Item(L.MacAddFromClipboard, () => MacInWindow(AddServerViaClipboardAsync)),
        };
        if (status != null)
        {
            items.Add(MacMenus.Item(L.TrayCopyProxyCmd, () => status.CopyProxyCmdToClipboardCmd.Execute().Subscribe()));
        }
        items.Add(MacMenus.Separator());
        items.Add(MacMenus.Item(L.TrayOpen, () => ShowHideWindow(true)));
        items.Add(MacMenus.Item(L.MacSettings, MacOpenSettings));
        items.Add(MacMenus.Separator());
        items.Add(MacMenus.Item(L.MacQuit, MacQuit));
        MacMenus.Fill(_macStatusMenu, items);
    }

    /// <summary>The Dock menu, above what the Dock adds itself: connect or disconnect, and the servers used last.</summary>
    private void FillMacDockMenu()
    {
        if (_macDockMenu == null || !MacStampChanged(ref _macDockStamp))
        {
            return;
        }
        var items = new List<NativeMenuItemBase>
        {
            MacMenus.Label(MacStateLine()),
            MacMenus.Item(MacConnectText(), ToggleConnectionFromTray),
            MacMenus.Separator(),
        };
        items.AddRange((_sidebar?.MenuRecent(MacMenuRecent) ?? []).Select(m => MacServerItem(m, null)));
        MacMenus.Fill(_macDockMenu, items);
    }

    /// <summary>
    /// The Dock keeps its own copy of the menu and may not ask before it shows it, so this one follows the state as
    /// it changes (RefreshTray, and the list when it is rebuilt).
    /// </summary>
    private void QueueMacDockMenu()
    {
        if (_macDockMenu == null || _macDockQueued)
        {
            return;
        }
        _macDockQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _macDockQueued = false;
            FillMacDockMenu();
        }, DispatcherPriority.Background);
    }

    private string MacStateLine()
    {
        if (_trayState is not { } tray || tray.Label.IsNullOrEmpty())
        {
            return L.AppName;
        }
        return tray.State == ProtectionState.Unprotected ? tray.Label : $"{tray.Label} · {Shorten(_serverName ?? L.NoServer, 40)}";
    }

    private string MacConnectText() => CurrentState() == ProtectionState.Unprotected ? L.Connect : L.Disconnect;

    private NativeMenu MacServersMenu()
    {
        var items = new List<NativeMenuItemBase>();
        var sidebar = _sidebar;
        var countries = sidebar?.MenuCountries() ?? [];
        if (sidebar == null || countries.Count == 0)
        {
            items.Add(MacMenus.Label(L.MacNoServers));
            items.Add(MacMenus.Separator());
            items.Add(MacMenus.Item(L.MacAddSubscription, () => MacInWindow(() => ViewModel?.ProfilesViewModel.AddSubCmd.Execute().Subscribe())));
            return MacMenus.Menu([.. items]);
        }
        items.Add(MacMenus.Item(L.Fastest, RecheckFastestAsync));
        var favorites = sidebar.MenuFavorites();
        if (favorites.Count > 0)
        {
            items.Add(MacMenus.Separator());
            items.Add(MacMenus.Label(L.TabFavorites));
            items.AddRange(favorites.Take(MacMenuServersPerCountry).Select(m => MacServerItem(m, null)));
        }
        var favoriteIds = favorites.Select(m => m.IndexId).ToHashSet();
        var recent = sidebar.MenuRecent(MacMenuRecent + favoriteIds.Count).Where(m => !favoriteIds.Contains(m.IndexId)).Take(MacMenuRecent).ToList();
        if (recent.Count > 0)
        {
            items.Add(MacMenus.Separator());
            items.Add(MacMenus.Label(L.Recent));
            items.AddRange(recent.Select(m => MacServerItem(m, null)));
        }
        items.Add(MacMenus.Separator());
        items.Add(MacMenus.Label(L.TabCountries));
        foreach (var (code, name, servers) in countries.Take(MacMenuCountries))
        {
            var flag = V2pnConverters.Flag(code);
            if (servers.Count == 1)
            {
                // One server: the country itself connects to it.
                var only = servers[0];
                items.Add(MacMenus.Choice(name, only.IndexId == _config.IndexId, () => ConnectToAsync(only.IndexId), flag, MacServerName(only, null)));
                continue;
            }
            var inside = servers.Take(MacMenuServersPerCountry).Select(m => (NativeMenuItemBase)MacServerItem(m, code)).ToList();
            if (servers.Count > MacMenuServersPerCountry)
            {
                inside.Add(MacMenus.Separator());
                inside.Add(MacMenus.Item(string.Format(L.MacMoreServers, servers.Count - MacMenuServersPerCountry), MacShowCountries));
            }
            var country = MacMenus.Submenu(name, MacMenus.Menu([.. inside]), flag);
            // The tick on the country says where the server in use is, without opening every submenu.
            if (servers.Any(m => m.IndexId == _config.IndexId))
            {
                country.ToggleType = MenuItemToggleType.CheckBox;
                country.IsChecked = true;
            }
            items.Add(country);
        }
        if (countries.Count > MacMenuCountries)
        {
            items.Add(MacMenus.Separator());
            items.Add(MacMenus.Item(string.Format(L.MacMoreServers, countries.Skip(MacMenuCountries).Sum(c => c.Items.Count)), MacShowCountries));
        }
        return MacMenus.Menu([.. items]);
    }

    private NativeMenuItem MacServerItem(ProfileItemModel model, string? countryCode) =>
        MacMenus.Choice(MacServerName(model, countryCode), model.IndexId == _config.IndexId, () => ConnectToAsync(model.IndexId));

    // Under a country the name loses the country; elsewhere it stays whole. A known ping follows it.
    private static string MacServerName(ProfileItemModel model, string? countryCode)
    {
        var name = ServerNames.TrimLeadingSymbols(CountryResolver.ServerName(model));
        if (countryCode.IsNotEmpty())
        {
            name = CountryResolver.WithoutCountry(name, countryCode);
        }
        name = Shorten(name, 48);
        return model.Delay > 0 ? $"{name}  ·  {L.Ms(model.Delay)}" : name;
    }

    private NativeMenu MacModeMenu()
    {
        var tun = lstMode.SelectedIndex == 1;
        return MacMenus.Menu(
            MacMenus.Choice(L.ModeProxy, !tun, () => MacSetModeAsync(false), tip: L.ModeProxyTip),
            MacMenus.Choice(L.ModeVpn, tun, () => MacSetModeAsync(true), tip: L.ModeVpnTip));
    }

    private NativeMenu MacRouteMenu()
    {
        var status = ViewModel?.StatusBarViewModel;
        var active = status?.SelectedRouting?.Id;
        var kind = RoutingPresets.KindOf(active);
        var items = new List<NativeMenuItemBase>
        {
            MacMenus.Choice(L.RouteRussiaDirect, kind == RoutingPresetKind.RussiaDirect, () => MacSelectRouteAsync(RoutingPresets.Id(RoutingPresetKind.RussiaDirect))),
            MacMenus.Choice(L.RouteBlockedOnly, kind == RoutingPresetKind.BlockedOnly, () => MacSelectRouteAsync(RoutingPresets.Id(RoutingPresetKind.BlockedOnly))),
            MacMenus.Choice(L.RouteAll, kind == RoutingPresetKind.All, () => MacSelectRouteAsync(RoutingPresets.Id(RoutingPresetKind.All))),
        };
        // The person's own routes, as the window's route menu lists them; a partner's route stays in its tab.
        var custom = status?.RoutingItems.Where(t => !RoutingPresets.IsPreset(t.Id) && !V2pnPartnerRoutes.IsPartnerRoute(t.Id)).ToList() ?? [];
        if (custom.Count > 0)
        {
            items.Add(MacMenus.Separator());
            items.AddRange(custom.Take(MacMenuServersPerCountry).Select(t =>
                MacMenus.Choice(Shorten(RoutingPresets.DisplayName(t.Remarks), 48), kind == null && t.Id == active, () => MacSelectRouteAsync(t.Id))));
        }
        items.Add(MacMenus.Separator());
        items.Add(MacMenus.Item(L.MacRouteSettings, () => MacInWindow(() => ViewModel?.RoutingSettingCmd.Execute().Subscribe())));
        return MacMenus.Menu([.. items]);
    }

    private NativeMenu MacSubscriptionsMenu()
    {
        var vm = ViewModel;
        var items = new List<NativeMenuItemBase>();
        var subs = _sidebar?.MenuSubscriptions() ?? [];
        if (subs.Count > 0)
        {
            items.Add(MacMenus.Item(L.UpdateAll, () => vm?.SubUpdateCmd.Execute().Subscribe()));
            items.Add(MacMenus.Item(L.TraySubUpdateViaVpn, () => vm?.SubUpdateViaProxyCmd.Execute().Subscribe()));
            if (subs.Count > 1)
            {
                items.Add(MacMenus.Separator());
                items.AddRange(subs.Take(MacMenuServersPerCountry).Select(sub =>
                    MacMenus.Item(string.Format(L.MacUpdateSub, Shorten(SideSubRow.NameOf(sub), 40)), () => UpdateSubscription(sub.Id))));
            }
            items.Add(MacMenus.Separator());
        }
        items.Add(MacMenus.Item(L.MacAddSubscription, () => MacInWindow(() => vm?.ProfilesViewModel.AddSubCmd.Execute().Subscribe())));
        items.Add(MacMenus.Item(L.MacAllSubscriptions, () => MacInWindow(() => SelectTab(SidebarTab.Subscriptions))));
        return MacMenus.Menu([.. items]);
    }

    /// <summary>⌘Q and the status item's Quit: the same quit the Dock asks for, which stops the core first (OnClosing).</summary>
    private static void MacQuit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown();
        }
    }

    #endregion The menus

    #region Notifications

    private string? _macNotedVerdict;
    private DateTime _macNotifiedAt;

    /// <summary>
    /// A system notification when the connection is lost, or comes back after a loss, while the window is not in
    /// front: in the menu bar V2pn has only its icon to say so. Only settled verdicts count ("checking" is on the
    /// way to one). Switching off by hand is not a loss, and connecting by hand is not a recovery.
    /// </summary>
    private void MacNoteVerdict(string kind, string label, bool lost)
    {
        if (!OperatingSystem.IsMacOS() || kind == "checking")
        {
            return;
        }
        var verdict = kind == "safe" ? "safe" : lost ? "lost" : "off";
        var before = _macNotedVerdict;
        _macNotedVerdict = verdict;
        // The window in front already shows it. A window can stay "active" for Avalonia while another app is in
        // front, so the app itself is asked.
        if (before == null || before == verdict || (IsVisible && WindowState != WindowState.Minimized && MacAppUtils.IsAppActive()))
        {
            return;
        }
        var text = $"{label} · {Shorten(_serverName ?? L.NoServer, 40)}";
        if (before == "safe" && verdict == "lost")
        {
            // A server that answers every other check must not fill the notification list.
            if (DateTime.UtcNow - _macNotifiedAt < TimeSpan.FromSeconds(30))
            {
                _macNotedVerdict = before;
                return;
            }
            _macNotifiedAt = DateTime.UtcNow;
            Logging.SaveLog($"V2pn: connection lost, notification {(MacAppUtils.Notify(L.MacNotifyLost, text) ? "posted" : "not available")}");
        }
        else if (before == "lost" && verdict == "safe")
        {
            Logging.SaveLog($"V2pn: connection restored, notification {(MacAppUtils.Notify(L.MacNotifyBack, text) ? "posted" : "not available")}");
        }
    }

    #endregion Notifications

    /// <summary>
    /// Everything the menus show, as one text: the state, the mode, the route, the servers with their pings, the
    /// subscriptions. True when it differs from <paramref name="stamp"/>, which then takes the new one.
    /// </summary>
    private bool MacStampChanged(ref string? stamp)
    {
        var sb = new StringBuilder();
        sb.Append(MacStateLine()).Append('|').Append(MacConnectText()).Append('|').Append(lstMode.SelectedIndex)
            .Append('|').Append(ViewModel?.StatusBarViewModel.SelectedRouting?.Id).Append('|').Append(_config.IndexId)
            .Append('|').Append(Editions.Partner?.Id).Append('|').Append(CultureInfo.CurrentUICulture.Name);
        if (ViewModel?.StatusBarViewModel is { } status)
        {
            foreach (var route in status.RoutingItems)
            {
                sb.Append('|').Append(route.Id).Append(':').Append(route.Remarks);
            }
        }
        if (_sidebar is { } sidebar)
        {
            foreach (var (code, _, servers) in sidebar.MenuCountries())
            {
                sb.Append('|').Append(code);
                foreach (var m in servers)
                {
                    sb.Append(',').Append(m.IndexId).Append(':').Append(m.Remarks).Append(':').Append(m.Delay);
                }
            }
            sb.Append("|f").AppendJoin(',', sidebar.MenuFavorites().Select(m => m.IndexId));
            sb.Append("|r").AppendJoin(',', sidebar.MenuRecent(MacMenuRecent * 2).Select(m => m.IndexId));
            sb.Append("|s").AppendJoin(',', sidebar.MenuSubscriptions().Select(s => $"{s.Id}:{s.Remarks}"));
        }
        var now = sb.ToString();
        if (now == stamp)
        {
            return false;
        }
        stamp = now;
        return true;
    }
}
