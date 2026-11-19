using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using DialogHostAvalonia;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Converters;
using v2rayN.Desktop.Manager;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

public partial class MainWindow : WindowBase<MainWindowViewModel>
{
    private static Config _config;
    private readonly WindowNotificationManager? _manager;
    private readonly DispatcherTimer _pingTimer;
    // Counts connected time for server statistics; runs while the window is hidden in the tray too.
    private readonly DispatcherTimer _usageTimer;
    private int _usageTicks;
    private CheckUpdateView? _checkUpdateView;
    private BackupAndRestoreView? _backupAndRestoreView;
    private bool _blCloseByUser = false;
    // "Start in the tray": decided once, from how this launch started (AutoStartupHandler.StartsHidden).
    private readonly bool _startHidden;
    private DateTime _deactivatedAt;
    private bool _closeAsking;
    private bool _shutdownCleaning;

    private StatusBarView? _statusView;
    private SidebarViewModel? _sidebar;
    private MsgView? _msgView;
    private ProfilesView? _profilesView;
    private ClashConnectionsView? _clashConnectionsView;
    private ClashProxiesView? _clashProxiesView;

    // The log, the server table and the Clash pages are built the first time they open: they bind to their
    // view models only while shown, and building all four at start cost about a third of a second.
    private MsgView? MsgPage => ViewModel is { } vm ? _msgView ??= new MsgView { DataContext = vm.MsgViewModel } : null;

    private ProfilesView? TablePage => ViewModel is { } vm
        ? _profilesView ??= new ProfilesView { DataContext = vm.ProfilesViewModel, MainViewModel = vm }
        : null;

    private ClashProxiesView? ClashProxiesPage => ViewModel is { } vm
        ? _clashProxiesView ??= new ClashProxiesView { DataContext = vm.ClashProxiesViewModel }
        : null;

    private ClashConnectionsView? ClashConnectionsPage => ViewModel is { } vm
        ? _clashConnectionsView ??= new ClashConnectionsView { DataContext = vm.ClashConnectionsViewModel }
        : null;

    private string? _activeIndexId;
    private bool _hasServer;

    public MainWindow()
    {
        InitializeComponent();
        // Labels from the first frame: the real ones arrive with the first refresh and the route presets.
        SetConnectLabel(L.Connect);
        txtRoute.Text = L.RouteTitle;

        _config = ConfigTextHandler.Config;
        InitAdaptive();
        // Above the status card and clear of the feature rail, over the empty part of the map.
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this))
        {
            MaxItems = 3,
            Position = NotificationPosition.BottomRight,
            Margin = new Thickness(0, 0, 20, 196),
        };
        _pingTimer = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) => RefreshActivePing());
        _usageTimer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) => TickUsage());
        _usageTimer.Start();
        // Toasts sit just above the status card, or near the bottom when it is hidden (Appearance, welcome, pages).
        contentStatusBarView.SizeChanged += (_, _) => UpdateNoticeMargin();
        contentStatusBarView.GetObservable(IsVisibleProperty).Subscribe(_ => UpdateNoticeMargin());
        panelStage.GetObservable(IsVisibleProperty).Subscribe(_ => UpdateNoticeMargin());
        mapView.SizeChanged += (_, _) => UpdateMapFocus();
        panelHeader.SizeChanged += (_, _) => UpdateMapFocus();
        contentStatusBarView.SizeChanged += (_, _) => UpdateMapFocus();
        contentStatusBarView.GetObservable(IsVisibleProperty).Subscribe(_ => UpdateMapFocus());
        // A tray click deactivates the window before the click arrives; see ShowHideWindow.
        Deactivated += (_, _) => _deactivatedAt = DateTime.UtcNow;
        PositionChanged += (_, _) => QueueNormalBounds();

        KeyDown += MainWindow_KeyDown;
        menuSettingsSetUWP.Click += MenuSettingsSetUWP_Click;
        menuPromotion.Click += MenuPromotion_Click;
        menuCheckUpdate.Click += MenuCheckUpdate_Click;
        btnNewUpdate.Click += MenuCheckUpdate_Click;
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;
        menuClose.Click += MenuClose_Click;
        menuAddServerViaClipboard.Click += async (_, _) => await AddServerViaClipboardAsync();
        btnConnect.Click += BtnConnect_Click;
        btnWelcomePaste.Click += async (_, _) => await AddServerViaClipboardAsync();
        InitImport();
        InitEdition();
        InitPartnerActions();
        InitScene();
        InitProviderLogin();
        InitDebug();
        btnWelcomeSub.Click += (_, _) => ViewModel?.ProfilesViewModel.AddSubCmd.Execute().Subscribe();
        btnWelcomeQr.Click += (_, _) => ViewModel?.AddServerViaScanCmd.Execute().Subscribe();
        btnWelcomeQr.IsVisible = Utils.IsWindows();
        if (!Utils.IsWindows())
        {
            // Without the screen scan the remaining button takes the whole row instead of half of it.
            Grid.SetColumnSpan(btnWelcomeSub, 3);
        }
        btnWelcomeRouteRu.Click += async (_, _) => await SelectPresetAsync(RoutingPresetKind.RussiaDirect);
        btnWelcomeRouteAll.Click += async (_, _) => await SelectPresetAsync(RoutingPresetKind.All);
        SubEditView.SubscriptionAdded += subId => _ = UpdateAddedSubscriptionAsync(subId);
        menuRegionalPresetsRussia.Icon = new Image { Source = V2pnConverters.Flag("RU"), Width = 16, Height = 12 };
        menuRegionalPresetsIran.Icon = new Image { Source = V2pnConverters.Flag("IR"), Width = 16, Height = 12 };
        if (OperatingSystem.IsMacOS())
        {
            // The Mac's own window buttons, top left, instead of V2pn's on the right; the menu button
            // moves clear of them.
            WindowDecorations = WindowDecorations.Full;
            btnMinimize.IsVisible = btnMaximize.IsVisible = btnCloseWindow.IsVisible = false;
            btnAppMenu.Margin = new Thickness(78, 0, 2, 0);
        }
        btnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        btnMaximize.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        btnCloseWindow.Click += (_, _) => Close();
        btnPageBack.Click += (_, _) => ClosePage();
        menuPingAll.Click += async (_, _) => await PingAllAsync();
        menuPageLog.Click += (_, _) => ShowPage(L.MenuLog, MsgPage);
        menuPageTable.Click += (_, _) => ShowPage(L.MenuTable, TablePage);
        menuPageProxies.Click += (_, _) => ShowPage(L.MenuClashProxies, ClashProxiesPage);
        menuPageConnections.Click += (_, _) => ShowPage(L.MenuClashConnections, ClashConnectionsPage);
        lstTabs.SelectionChanged += LstTabs_SelectionChanged;
        // Right-click and drag change the list in place (MainWindow.SidebarMenu.cs, MainWindow.SidebarDrag.cs).
        InitSidebarMenus();
        InitSidebarDrag();
        lstRows.KeyDown += LstRows_KeyDown;
        lstRows.SelectionChanged += (_, _) =>
        {
            // A rebuild clears the selection first; only a real row updates the remembered one.
            if (lstRows.SelectedItem is SideRow row && KeyOf(row) is { } key)
            {
                _lastRowKey = key;
            }
        };
        // Focus goes back to the row after a rebuild only for keyboard users; a click must not leave a ring.
        lstRows.AddHandler(KeyDownEvent, (_, _) => _rowsByKeyboard = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        lstRows.AddHandler(PointerPressedEvent, (_, _) => _rowsByKeyboard = false, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Headers without actions and hint text are skipped by the arrow keys. Containers are recycled,
        // so the value is set both ways.
        lstRows.ContainerPrepared += (_, e) =>
            e.Container.Focusable = lstRows.ItemFromContainer(e.Container) is not (SideHintRow or SideBannerRow or SideAnnounceRow or SideHeaderRow { ShowPing: false, ShowSubActions: false });
        lstRows.ContextRequested += LstRows_ContextRequested;
        txtSearch.KeyDown += TxtSearch_KeyDown;
        txtSearch.TextChanged += (_, _) =>
        {
            btnClearSearch.IsVisible = txtSearch.Text.IsNotEmpty();
            if (_sidebar != null)
            {
                _sidebar.Search = txtSearch.Text ?? string.Empty;
            }
        };
        btnClearSearch.Click += (_, _) =>
        {
            txtSearch.Text = string.Empty;
            txtSearch.Focus();
        };
        // When the core itself failed, the pill leads to the log, where the reason is.
        btnRetryConnection.Click += async (_, _) =>
        {
            if (ViewModel?.StatusBarViewModel is { } status)
            {
                await status.TestServerAvailability();
            }
        };
        btnReplacement.Click += async (_, _) => await FindWorkingServerAsync();
        btnFixPrimary.Click += async (_, _) => await RunFixAsync(_fixPrimary);
        btnDiagnostics.Click += async (_, _) => await ShowDiagnosticsAsync();
        btnBypass.Click += async (_, _) => await ShowBypassAsync();
        menuBypass.Click += async (_, _) => await ShowBypassAsync();
        // What a provider reports (expiry, traffic) can change the note next to Connect.
        SubInfoManager.Instance.Changed += id => Dispatcher.UIThread.Post(() =>
        {
            if (id == _activeSubId)
            {
                RefreshStatePill();
            }
        });
        btnFixSecondary.Click += async (_, _) => await RunFixAsync(_fixSecondary);
        pillState.PointerPressed += (_, e) =>
        {
            if (_pillOpensLog)
            {
                e.Handled = true;
                ShowPage(L.MenuLog, MsgPage);
            }
        };

        // The palette button opens the Appearance page. The theme view model still loads at start:
        // it applies the saved light/dark mode and text size.
        _themeSettings = new ThemeSettingViewModel();
        conTheme.Content ??= AppearanceView.HeaderButton(() => DialogHost.Show(new AppearanceView(_themeSettings)));
        Appearance.Changed += () => Dispatcher.UIThread.Post(ApplyLayout);
        // The layout follows the width (MainWindow.Adaptive.cs), and the "wide" sidebar steps down in a
        // small window; re-laid out only when one of them changes.
        SizeChanged += (_, _) => FollowWidth();
        // Windows' "Animation effects" may have changed while V2pn was in the background.
        Activated += (_, _) =>
        {
            if (Appearance.Current.Motion == "system" && Application.Current is { } app)
            {
                Motion.Refresh(app);
            }
        };

        this.WhenActivated(disposables =>
        {
            var vm = ViewModel!;
            var profiles = vm.ProfilesViewModel;
            var status = vm.StatusBarViewModel;
            InitBypass(vm).DisposeWith(disposables);

            //sub
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.menuSubSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateCmd, v => v.menuSubGroupUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateViaProxyCmd, v => v.menuSubGroupUpdateViaProxy).DisposeWith(disposables);

            //setting
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.btnRoutingSettings).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.btnRailDns).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuSettingsMain).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.btnRailSettings).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.btnRailUpdate).DisposeWith(disposables);
            // The switch lives in the settings window; this window is active again once that one closes.
            Activated += (_, _) => ApplySimpleMode();
            this.WhenAnyValue(v => v.ViewModel.ShowClashUI)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => ApplySimpleMode())
                .DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RebootAsAdminCmd, v => v.menuRebootAsAdmin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ClearServerStatisticsCmd, v => v.menuClearServerStatistics).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenTheFileLocationCmd, v => v.menuOpenTheFileLocation).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetDefaultCmd, v => v.menuRegionalPresetsDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetRussiaCmd, v => v.menuRegionalPresetsRussia).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetIranCmd, v => v.menuRegionalPresetsIran).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);
            // The header badge serves both V2pn's own update and v2rayN's core updates.
            this.WhenAnyValue(v => v.ViewModel.BlNewUpdate)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => RefreshUpdateBadge())
                .DisposeWith(disposables);
            V2pnAppUpdater.Instance.WhenAnyValue(u => u.State)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(OnAppUpdateState)
                .DisposeWith(disposables);

            //add servers
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaImageCmd, v => v.menuAddServerViaImage).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ProfilesViewModel.AddSubCmd, v => v.menuAddSubscription).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddVmessServerCmd, v => v.menuAddVmessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddVlessServerCmd, v => v.menuAddVlessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddShadowsocksServerCmd, v => v.menuAddShadowsocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTrojanServerCmd, v => v.menuAddTrojanServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHysteria2ServerCmd, v => v.menuAddHysteria2Server).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTuicServerCmd, v => v.menuAddTuicServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddWireguardServerCmd, v => v.menuAddWireguardServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddAnytlsServerCmd, v => v.menuAddAnytlsServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddNaiveServerCmd, v => v.menuAddNaiveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddMasqueServerCmd, v => v.menuAddMasqueServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSocksServerCmd, v => v.menuAddSocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHttpServerCmd, v => v.menuAddHttpServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomServerCmd, v => v.menuAddCustomServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddPolicyGroupServerCmd, v => v.menuAddPolicyGroupServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddProxyChainServerCmd, v => v.menuAddProxyChainServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomOutboundServerCmd, v => v.menuAddCustomOutboundServer).DisposeWith(disposables);

            BuildViews();
            InitConnectionBar();
            vm.SubUpdateCmd.IsExecuting
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(busy =>
                {
                    if (_sidebar != null)
                    {
                        _sidebar.SubsUpdate.IsBusy = busy;
                    }
                })
                .DisposeWith(disposables);
            TrackSubUpdates(vm.SubUpdateCmd).DisposeWith(disposables);
            TrackSubUpdates(vm.SubUpdateViaProxyCmd).DisposeWith(disposables);
            _ = InitRoutingPresetsAsync();

            // Connection header, map and statistics follow the proxy mode, TUN and the running server.
            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.SystemProxySelected,
                    v => v.ViewModel.StatusBarViewModel.EnableTun,
                    v => v.ViewModel.StatusBarViewModel.RunningServerDisplay)
                .Throttle(TimeSpan.FromMilliseconds(80))
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .SubscribeAsync(async _ => await RefreshConnectionAsync())
                .DisposeWith(disposables);
            profiles.ProfileItems.CollectionChanged += (_, _) =>
            {
                _ = RefreshConnectionAsync();
                QueueMapLocations();
            };
            profiles.PingTestFinished.AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => QueueMapLocations())
                .DisposeWith(disposables);
            this.WhenAnyValue(v => v.ViewModel.ProfilesViewModel.AllServersCount)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .SubscribeAsync(async _ => await RefreshConnectionAsync())
                .DisposeWith(disposables);
            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.Availability)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    RefreshStatePill();
                    if (ViewModel?.StatusBarViewModel.CurrentAvailability.IsAvailable == true)
                    {
                        Remember(_config.IndexId, CurrentState());
                    }
                })
                .DisposeWith(disposables);
            // The pill follows the core process itself: restarting after a crash, or switched off after a failure.
            V2pnCoreWatch.StateChanged.AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(core =>
                {
                    RefreshStatePill();
                    // A failed start already showed the core's own message; repeated crashes say nothing.
                    if (core == EV2pnCoreState.Failed && V2pnKillSwitch.Holding)
                    {
                        ShowToast(ToastKind.Error, L.KillSwitchToast);
                    }
                    else if (core == EV2pnCoreState.Failed && V2pnCoreWatch.Failure == EV2pnFailure.KeepsStopping)
                    {
                        ShowToast(ToastKind.Error, L.CoreKeepsStoppingToast);
                    }
                    // A kill switch that could not close the internet must not look like one that did.
                    if (core == EV2pnCoreState.Running && V2pnKillSwitch.FirewallError is { } firewallError
                        && firewallError != _shownFirewallError)
                    {
                        _shownFirewallError = firewallError;
                        ShowToast(ToastKind.Error, string.Format(L.KillSwitchFirewallFailed, firewallError));
                    }
                })
                .DisposeWith(disposables);
            V2pnKillSwitch.HoldingChanged.AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => RefreshStatePill())
                .DisposeWith(disposables);

            foreach (var handler in RegisterProfileInteractions(profiles))
            {
                handler.DisposeWith(disposables);
            }

            ViewModel.ReadTextFromClipboardInteraction.RegisterHandler(async interaction =>
            {
                var result = await AvaUtils.GetClipboardData(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.ScanScreenInteraction.RegisterHandler(async interaction =>
            {
                ShowHideWindow(false);
                await Task.Delay(200);
                var result = QRCodeAvaloniaUtils.CaptureScreen();
                ShowHideWindow(true);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.BrowseImageFileInteraction.RegisterHandler(async interaction =>
            {
                var result = await UI.OpenFileDialog(null);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            AppEvents.SendSnackMsgRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(async content => await DelegateSnackMsg(content))
              .DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);

            AppEvents.ShutdownRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(Shutdown)
              .DisposeWith(disposables);

            _pingTimer.Start();
        });

        // Window.Title is what Alt+Tab, the taskbar and Task Manager show: the state, set by RefreshTray.
        if (Utils.IsWindows())
        {
            if (!Design.IsDesignMode)
            {
                ThreadPool.RegisterWaitForSingleObject(Program.ProgramStarted, OnProgramStarted, null, -1, false);
                HotkeyManager.Instance.Init(_config, OnHotkeyHandler);
            }
        }
        else
        {
            menuAddServerViaScan.IsVisible = false;
            menuRebootAsAdmin.IsVisible = false;
            menuSettingsSetUWP.IsVisible = false;
        }

        // V2pn: right after a move from v2rayN the window shows, whatever "start hidden" came over with it.
        _startHidden = AutoStartupHandler.StartsHidden(_config, Program.StartedAtSignIn) && V2pnImport.Imported == null;
        if (_startHidden && Utils.IsWindows())
        {
            WindowState = WindowState.Minimized;
        }
        // Until the close question is answered, X hides to the tray as it always did; the setting says so.
        if (Utils.IsWindows() && !_config.UiItem.V2pnCloseAsked)
        {
            _config.UiItem.Hide2TrayWhenClose = true;
        }

        AddHelpMenuItem();
    }

    #region Views

    private void BuildViews()
    {
        var vm = ViewModel!;
        if (_statusView == null)
        {
            _statusView = new StatusBarView { ViewModel = vm.StatusBarViewModel };
        }
        contentStatusBarView.Content = _statusView;
        ApplyLayout();

        if (_sidebar == null)
        {
            _sidebar = new SidebarViewModel(vm.ProfilesViewModel);
            _sidebar.Rebuilt += RestoreRowFocus;
            // Appearance → Opens on.
            SelectTab(StartTab());
        }
        lstRows.ItemsSource = _sidebar.Rows;
        mapView.LocationClicked -= MapView_LocationClicked;
        mapView.LocationClicked += MapView_LocationClicked;
        QueueMapLocations();
    }

    private bool _mapQueued;

    private void QueueMapLocations()
    {
        if (_mapQueued)
        {
            return;
        }
        _mapQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _mapQueued = false;
            var items = ViewModel?.ProfilesViewModel.ProfileItems.ToList() ?? [];
            var locations = items
                .GroupBy(m => CountryResolver.Resolve(m.Remarks))
                .Where(g => g.Key.IsNotEmpty() && !string.Equals(g.Key, "EU", StringComparison.OrdinalIgnoreCase))
                .Select(g => new MapLocation(
                    g.Key!,
                    WorldMap.Instance.Get(g.Key)?.Name ?? g.Key!,
                    g.Count(),
                    g.Where(m => m.Delay > 0).Select(m => m.Delay).DefaultIfEmpty(0).Min()))
                .ToList();
            mapView.SetLocations(locations);
        }, DispatcherPriority.Background);
    }

    private void MapView_LocationClicked(string code)
    {
        if (_sidebar == null)
        {
            return;
        }
        txtSearch.Text = string.Empty;
        SelectTab(SidebarTab.Countries);
        var row = _sidebar.RevealCountry(code);
        if (row != null)
        {
            Dispatcher.UIThread.Post(() => lstRows.ScrollIntoView(row), DispatcherPriority.Background);
        }
    }

    // Where focus was before a page opened, so closing the page returns the keyboard there.
    private Control? _pageReturnFocus;
    private bool _pageClosing;

    private async void ShowPage(string title, Control? page)
    {
        if (page == null)
        {
            return;
        }
        txtPageTitle.Text = title;
        pageHost.Content = page;
        var opening = !pageOverlay.IsVisible;
        pageOverlay.IsVisible = true;
        if (!opening)
        {
            return;
        }
        _pageReturnFocus = FocusManager?.GetFocusedElement() as Control ?? btnAppMenu;
        try
        {
            await Motion.Enter(pageOverlay, dx: 18, dy: 0, ms: 240);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ShowPage", ex);
        }
        if (!pageOverlay.IsVisible || _pageClosing)
        {
            return;
        }
        // The page covers the stage; its controls leave the Tab order and the screen reader's view.
        panelStage.IsVisible = false;
        btnPageBack.Focus(NavigationMethod.Tab);
    }

    private async void ClosePage()
    {
        if (!pageOverlay.IsVisible || _pageClosing)
        {
            return;
        }
        panelStage.IsVisible = true;
        _pageClosing = true;
        try
        {
            await Motion.Exit(pageOverlay, dx: 14, ms: 160);
        }
        finally
        {
            _pageClosing = false;
        }
        pageOverlay.IsVisible = false;
        pageHost.Content = null;
        var target = _pageReturnFocus;
        _pageReturnFocus = null;
        if (target is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true })
        {
            target.Focus(NavigationMethod.Tab);
        }
        else
        {
            btnAppMenu.Focus(NavigationMethod.Tab);
        }
    }

    private List<IDisposable> RegisterProfileInteractions(ProfilesViewModel profiles)
    {
        var handlers = new List<IDisposable>();
        // The sidebar uses these actions even when the server table page is closed.
        profiles.ShowYesNoInteraction.RegisterHandler(async interaction =>
        {
            var result = await UI.ShowYesNo(interaction.Input);
            interaction.SetOutput(result == ButtonResult.Yes);
        })
        .AddTo(handlers);

        profiles.SaveFileDialogInteraction.RegisterHandler(async interaction =>
        {
            var fileName = await UI.SaveFileDialog("");
            if (fileName.IsNullOrEmpty())
            {
                interaction.SetOutput(false);
                return;
            }
            await profiles.Export2ClientConfigResult(fileName, interaction.Input);
            interaction.SetOutput(true);
        })
        .AddTo(handlers);

        profiles.SetClipboardDataInteraction.RegisterHandler(async interaction =>
        {
            await AvaUtils.SetClipboardData(this, interaction.Input);
            interaction.SetOutput(RxVoid.Default);
        })
        .AddTo(handlers);

        profiles.ShareServerInteraction.RegisterHandler(async interaction =>
        {
            if (interaction.Input.IsNotEmpty())
            {
                await ShowRoomyDialogAsync(new QrcodeView(interaction.Input));
            }
            interaction.SetOutput(RxVoid.Default);
        })
        .AddTo(handlers);

        handlers.Add(profiles.ProfilesFocusInteraction.RegisterHandler(i => i.SetOutput(RxVoid.Default)));
        handlers.Add(profiles.DispatcherRefreshServersBizInteraction.RegisterHandler(i => i.SetOutput(RxVoid.Default)));
        handlers.Add(profiles.AdjustMainLvColWidthInteraction.RegisterHandler(i => i.SetOutput(RxVoid.Default)));
        return handlers;
    }

    #endregion Views

    #region Connection

    private ProtectionState CurrentState()
    {
        var status = ViewModel?.StatusBarViewModel;
        // Without a server the core is not running: "proxy on port N" would describe nothing.
        if (status == null || !_hasServer)
        {
            return ProtectionState.Unprotected;
        }
        if (status.EnableTun)
        {
            return ProtectionState.Protected;
        }
        return (ESysProxyType)status.SystemProxySelected switch
        {
            ESysProxyType.ForcedChange or ESysProxyType.Pac => ProtectionState.Protected,
            ESysProxyType.Unchanged => ProtectionState.LocalOnly,
            _ => ProtectionState.Unprotected,
        };
    }

    /// <summary>
    /// Server statistics: the connection counts as up while the core runs, the state is not "Unprotected"
    /// and the last check did not fail. Saved every couple of minutes so a crash loses little.
    /// </summary>
    private void TickUsage()
    {
        if (!ServerUsageManager.Enabled)
        {
            ServerUsageManager.Instance.Tick(null);
            return;
        }
        var up = _activeIndexId.IsNotEmpty()
                 && CurrentState() != ProtectionState.Unprotected
                 && CoreManager.Instance.IsCoreAlive
                 && ViewModel?.StatusBarViewModel.CurrentAvailability.IsAvailable == true;
        ServerUsageManager.Instance.Tick(up ? _activeIndexId : null);
        if (++_usageTicks % 8 == 0)
        {
            _ = ServerUsageManager.Instance.SaveAsync();
        }
    }

    // Servers the user actually connected through, newest first, for the "Recent" section.
    private void Remember(string? indexId, ProtectionState state)
    {
        if (indexId.IsNullOrEmpty() || state == ProtectionState.Unprotected)
        {
            return;
        }
        var recent = _config.UiItem.V2pnRecent ??= [];
        if (recent.FirstOrDefault() == indexId)
        {
            return;
        }
        recent.Remove(indexId);
        recent.Insert(0, indexId);
        if (recent.Count > 8)
        {
            recent.RemoveRange(8, recent.Count - 8);
        }
        _sidebar?.QueueRebuild();
    }

    private bool _shownOnce;
    private bool _routeShown;
    private string? _mapCode;
    private string? _serverName;
    // The pill's kind and spoken label last shown, and the last settled verdict on the tint and map pin.
    private string? _pillKind;
    private string? _spokenState;
    private bool _quietPill;
    private bool _pillOpensLog;
    private Cursor? _handCursor;
    // Set by the first refresh: before it, nobody knows yet whether the welcome card or the hero shows.
    private bool _stageKnown;

    private static async Task DelayedEnter(Visual target, int delayMs)
    {
        if (!Motion.Enabled)
        {
            return;
        }
        target.Opacity = 0;
        await Task.Delay(delayMs);
        target.Opacity = 1;
        await Motion.Enter(target, dy: 6, ms: 240);
    }

    private ProfileItemModel? FindModel(string? indexId)
    {
        var profiles = ViewModel?.ProfilesViewModel;
        if (profiles == null || indexId.IsNullOrEmpty())
        {
            return null;
        }
        return profiles.ProfileItems.FirstOrDefault(t => t.IndexId == indexId)
               ?? profiles.FavoriteItems.FirstOrDefault(t => t.IndexId == indexId);
    }

    private async Task RefreshConnectionAsync()
    {
        var item = await ConfigDbQuery.GetProfileItem(_config.IndexId);
        try
        {
            _hasServer = item != null;
            var state = CurrentState();
            // Motion only for changes the user caused after the first paint, never for the initial state.
            var serverChanged = _shownOnce && item?.IndexId != _activeIndexId;
            _activeIndexId = item?.IndexId;
            _activeSubId = item?.Subid;

            // V2pn: the partner's country for its server (pack or API) wins over a guess from the name.
            var code = V2pnPartnerLive.Instance.Describe(item?.Subid, item?.Remarks, item?.Address)?.Country ?? CountryResolver.Resolve(item?.Remarks);
            var country = WorldMap.Instance.Get(code);
            _mapCode = country?.Code ?? code;
            _serverName = item == null ? null : CountryResolver.ServerName(item);
            RefreshServersStatus();
            var flag = V2pnConverters.Flag(code);
            imgHeaderFlag.Source = flag;
            borderHeaderFlag.IsVisible = flag != null;

            if (item == null)
            {
                txtHeaderTitle.Text = L.NoServer;
                txtHeaderSubtitle.Text = L.PickServer;
            }
            else
            {
                var name = CountryResolver.ServerName(item);
                var region = string.Equals(code, "EU", StringComparison.OrdinalIgnoreCase) ? L.Europe : null;
                txtHeaderTitle.Text = country?.Name ?? region ?? name;
                var placed = country != null || region != null;
                // One short line under the country: the server's own name without the country repeated, and, in the
                // advanced view, its protocol unless the name already says it ("tls" says nothing to anyone).
                var shortName = placed ? CountryResolver.WithoutCountry(name, code) : name;
                var protocol = item.StreamSecurity == Global.StreamSecurityReality ? $"{item.ConfigType} REALITY" : item.ConfigType.ToString();
                var showProtocol = _config.UiItem.V2pnSettingsAdvanced
                    && !shortName.Contains(item.ConfigType.ToString(), StringComparison.OrdinalIgnoreCase);
                txtHeaderSubtitle.Text = !placed ? (showProtocol ? protocol : name)
                    : showProtocol ? $"{shortName} · {protocol}" : shortName;
            }

            // No servers at all: the welcome card replaces the header, Connect, the mode bar and the
            // statistics, which would only show dashes.
            var empty = (ViewModel?.ProfilesViewModel.AllServersCount ?? 0) == 0 && item == null;
            if (empty)
            {
                await SwitchOffWhenNoServersAsync();
            }
            // The first server just arrived. Nothing is awaited from here on: three triggers re-enter this
            // method, and a second pass must not see the welcome card still up and start the handoff again.
            var leavingWelcome = !empty && cardWelcome.IsVisible && _shownOnce;
            if (empty && !cardWelcome.IsVisible && _shownOnce)
            {
                _ = Motion.Enter(cardWelcome, dy: 10, ms: 260, scale: 0.97);
            }
            cardWelcome.IsVisible = empty;
            btnConnect.IsVisible = !empty;
            barConnection.IsVisible = !empty;
            panelConnect.IsVisible = !empty;
            txtHeaderTitle.IsVisible = !empty;
            txtHeaderSubtitle.IsVisible = !empty;
            contentStatusBarView.IsVisible = !empty && Appearance.Current.ShowStatusCard && _tier != BodyTier.Stacked;
            if (leavingWelcome)
            {
                // The hero arrives as one unit and the statistics a beat later, instead of piece by piece.
                _ = Motion.Enter(panelHeader, dy: 8, ms: 240);
                if (contentStatusBarView.IsVisible)
                {
                    _ = DelayedEnter(contentStatusBarView, 60);
                }
            }
            if (empty)
            {
                RefreshWelcomeRoute();
                ShowImportOnWelcomeOnce();
            }
            var unprotected = state == ProtectionState.Unprotected;
            SetConnectLabel(unprotected ? L.Connect : L.Disconnect);
            btnConnect.Classes.Set("accent", unprotected);
            btnConnect.Classes.Set("disconnect", !unprotected);
            btnConnect.IsEnabled = item != null || !unprotected;

            // Pill, tint, map pin, status line and the Disconnect glyph all follow one verdict.
            _stageKnown = true;
            _quietPill = leavingWelcome;
            RefreshStatePill();
            _quietPill = false;

            SyncModeSelection();

            if (serverChanged && !leavingWelcome)
            {
                // The new server settles into the header: flag and name first, the details a beat later.
                _ = Motion.Enter(borderHeaderFlag, dy: 8, ms: 240);
                _ = Motion.Enter(txtHeaderTitle, dy: 8, ms: 240);
                _ = DelayedEnter(txtHeaderSubtitle, 40);
            }
        }
        finally
        {
            // Even if something above threw, the stage must not stay invisible.
            if (!_shownOnce)
            {
                RevealStage();
                _ = MaybeStartOnboardingAsync();
            }
            _shownOnce = true;
        }

        // V2pn: the partner's buttons under Connect while a server of its subscription is in use.
        RefreshPartnerActionBar(item?.Subid);
        _statusView?.SetServer(item, FindModel(item?.IndexId)?.Delay ?? await StoredDelayAsync(item?.IndexId));
    }

    /// <summary>
    /// First paint: the header and whichever card is showing appear together, once they have content.
    /// Opacity is set explicitly because Motion.Enter does nothing when Windows animations are off.
    /// </summary>
    private void RevealStage()
    {
        panelHeader.Opacity = 1;
        cardWelcome.Opacity = 1;
        contentStatusBarView.Opacity = 1;
        if (!Motion.Enabled)
        {
            return;
        }
        // A load, not a user action: one quiet fade with no travel.
        _ = Motion.Enter(panelHeader, dy: 0, ms: 160);
        Visual? card = cardWelcome.IsVisible ? cardWelcome : contentStatusBarView.IsVisible ? contentStatusBarView : null;
        if (card != null)
        {
            _ = Motion.Enter(card, dy: 0, ms: 160);
        }
    }

    /// <summary>
    /// An empty list means off. Without this, a saved "on" state survives with no servers, and the first
    /// import connects by itself to whatever server lands in row 1. Goes through StatusBarViewModel so the
    /// UI and the core stay in step; the chosen method (UiItem.V2pnProxyMethod) stays, so Connect restores it.
    /// Same path as Disconnect: the Windows proxy is cleared only when it is V2pn's own (V2pnConnection).
    /// </summary>
    private async Task SwitchOffWhenNoServersAsync()
    {
        if (ViewModel?.StatusBarViewModel is not { } status || !IsConnected(status))
        {
            return;
        }
        if (await V2pnConnection.HasAnyServerAsync())
        {
            return;
        }
        if (status.EnableTun)
        {
            status.EnableTun = false;
        }
        status.SystemProxySelected = (int)ESysProxyType.ForcedClear;
    }

    private string? _shownFirewallError;

    private void RefreshStatePill()
    {
        var state = CurrentState();
        var status = ViewModel?.StatusBarViewModel;
        var check = status?.CurrentAvailability ?? AvailabilityCheckResult.Unchecked;
        var enabled = state != ProtectionState.Unprotected;
        var core = V2pnCoreWatch.State;
        var fault = !enabled || core == EV2pnCoreState.Failed ? V2pnCoreWatch.Failure : EV2pnFailure.None;
        var restarting = enabled && core == EV2pnCoreState.Restarting;
        var starting = enabled && core is EV2pnCoreState.Starting or EV2pnCoreState.Stopped;
        var holding = fault != EV2pnFailure.None && V2pnKillSwitch.Holding;
        var (kind, label) = (fault, enabled, restarting, starting, check.State, state) switch
        {
            _ when holding => ("unsafe", L.StateKillSwitch),
            (EV2pnFailure.KeepsStopping, _, _, _, _, _) => ("warn", L.StateCoreStopped),
            (not EV2pnFailure.None, _, _, _, _, _) => ("warn", L.StateCoreFailed),
            (_, false, _, _, _, _) => ("unsafe", L.Unprotected),
            (_, _, true, _, _, _) => ("checking", L.StateReconnecting),
            (_, _, _, true, _, _) => ("checking", L.StateConnecting),
            (_, _, _, _, AvailabilityState.Checking, _) => ("checking", L.StateChecking),
            (_, _, _, _, AvailabilityState.Available, ProtectionState.LocalOnly) => ("safe", L.LocalOnly),
            (_, _, _, _, AvailabilityState.Available, _) => ("safe", status?.EnableTun == true ? L.StateVpnConnected : L.StateProxyConnected),
            _ => ("warn", L.CheckStatus(check)),
        };
        foreach (var c in new[] { "safe", "unsafe", "warn", "checking" })
        {
            pillState.Classes.Set(c, c == kind);
        }
        txtStatePill.Text = label.ToUpper(CultureInfo.CurrentUICulture);
        // A live region: screen readers hear each new verdict once, and not in capitals.
        if (label != _spokenState)
        {
            _spokenState = label;
            Avalonia.Automation.AutomationProperties.SetName(txtStatePill, label);
        }
        var faulted = fault != EV2pnFailure.None;
        var faultTip = !faulted ? null
            : holding ? L.KillSwitchHoldTip
            : V2pnCoreWatch.FailureDetail.IsNullOrEmpty() ? L.StateCoreFailedTip
            : $"{Shorten(V2pnCoreWatch.FailureDetail, 300)}\n\n{L.StateCoreFailedTip}";
        var systemProxy = enabled && status?.EnableTun == false
            && (ESysProxyType)status.SystemProxySelected is ESysProxyType.ForcedChange or ESysProxyType.Pac;
        // What system-proxy mode covers is in the pill's tooltip; a line of its own under every connection was one too many.
        txtSystemProxyScope.IsVisible = false;
        var tip = faulted ? faultTip
            : check.State == AvailabilityState.NoResponse ? L.StateNoResponseTip
            : check.IsFailure ? L.CheckFailureHint
            : state == ProtectionState.LocalOnly ? string.Format(L.LocalOnlyTip, AppManager.Instance.GetLocalPort(EInboundProtocol.socks))
            : systemProxy ? L.SystemProxyScope : null;
        ToolTip.SetTip(pillState, tip);
        _pillOpensLog = faulted;
        pillState.Cursor = faulted ? _handCursor ??= new Cursor(StandardCursorType.Hand) : null;
        btnRetryConnection.IsVisible = enabled && check.IsFailure;
        RefreshFixActions(faulted, enabled);
        RefreshReplacementAction(check, enabled);
        _statusView?.ShowCheck(check);
        spinState.IsVisible = kind == "checking";
        dotState.IsVisible = kind != "checking";

        // The pill moves when the result arrives, not on the click that starts the check.
        if (_shownOnce && !_quietPill && kind != _pillKind && kind != "checking")
        {
            _ = Motion.Enter(pillState, dy: 0, ms: 180, scale: 0.96);
        }
        _pillKind = kind;
        // The button says what a click does now: while the core is coming up it is still "connecting".
        if (state != ProtectionState.Unprotected)
        {
            SetConnectLabel(starting || restarting ? L.StateConnecting : L.Disconnect);
        }
        PaintVerdict(kind, state);
        _statusView?.SetState(state, label, kind, tip);
        RefreshTray(state, label);
    }
    /// <summary>
    /// A setting carried over from v2rayN can leave the local port open to the whole network with no
    /// password. Said once; the Settings page keeps the warning next to the switch.
    /// </summary>
    private void WarnAboutOpenLanProxyOnce()
    {
        var inbound = _config.Inbound.FirstOrDefault();
        if (inbound == null || _config.UiItem.V2pnLanWarned || !inbound.AllowLANConn)
        {
            return;
        }
        if (inbound.NewPort4LAN && inbound.User.IsNotEmpty() && inbound.Pass.IsNotEmpty())
        {
            return;
        }
        _config.UiItem.V2pnLanWarned = true;
        _ = ConfigTextHandler.SaveConfig();
        ShowToast(ToastKind.Warning, L.LanOpenToast);
    }

    /// <summary>
    /// With "Advanced settings" off, the window shows what a person needs to connect and nothing that
    /// takes knowing v2ray: no server table, no Clash pages, no DNS tile, no config template. One switch
    /// in Settings brings them all back.
    /// </summary>
    private void ApplySimpleMode()
    {
        var advanced = _config.UiItem.V2pnSettingsAdvanced;
        // The DNS tile follows this switch unless the Appearance page says otherwise.
        ArrangeSidebar();
        menuPageTable.IsVisible = advanced;
        menuRegion.IsVisible = advanced;
        // The Clash pages also need a core that has them; XAML binds that, this only takes them away.
        if (!advanced)
        {
            menuPageProxies.IsVisible = false;
            menuPageConnections.IsVisible = false;
        }
        else
        {
            var clash = ViewModel?.ShowClashUI == true;
            menuPageProxies.IsVisible = clash;
            menuPageConnections.IsVisible = clash;
        }
        if (_shownAdvanced == null)
        {
            _shownAdvanced = advanced;
        }
        else if (advanced != _shownAdvanced)
        {
            _shownAdvanced = advanced;
            // The header's second line and the status card show less in the simple view.
            _ = RefreshConnectionAsync();
            RefreshActivePing();
        }
    }

    private bool? _shownAdvanced;

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>
    /// The tray tooltip and status item, its Connect/Disconnect item, and Window.Title (Alt+Tab, taskbar)
    /// use the same status text as the pill.
    /// </summary>
    private void RefreshTray(ProtectionState state, string label)
    {
        _trayState = (state, label);
        var server = Shorten(_serverName ?? L.NoServer, 40);
        var text = state == ProtectionState.Unprotected ? label : $"{label} · {server}";
        var title = $"{EditionTitle()} — {text}";
        Title = title;
        if (ViewModel?.StatusBarViewModel is { } status)
        {
            status.TrayStatusText = text;
            status.TrayToolTipText = Utils.IsLinux() ? Global.AppName : Shorten(title, 127);
            status.TrayConnectText = state == ProtectionState.Unprotected ? L.Connect : L.Disconnect;
        }
    }
    /// <summary>Connect/Disconnect from the tray menu: the window's button, without opening the window.</summary>
    public void ToggleConnectionFromTray()
    {
        var status = ViewModel?.StatusBarViewModel;
        if (status == null)
        {
            return;
        }
        // "Pick a server" and the administrator question for VPN mode (the password prompt on macOS and
        // Linux) need the window to be seen.
        var connect = CurrentState() == ProtectionState.Unprotected;
        if (connect && (_config.IndexId.IsNullOrEmpty() || (_config.UiItem.V2pnPreferTun && !status.AllowEnableTun())))
        {
            ShowHideWindow(true);
        }
        BtnConnect_Click(null, new RoutedEventArgs());
    }

    /// <summary>
    /// Paints the tint, the map pin, the status line and the Disconnect glyph in the pill's colour.
    /// A connection whose check is running uses the pending colour.
    /// </summary>
    private void PaintVerdict(string kind, ProtectionState state)
    {
        var noTint = cardWelcome.IsVisible || !_stageKnown;
        tintSafe.Opacity = !noTint && kind == "safe" ? 1 : 0;
        tintWarn.Opacity = !noTint && kind == "warn" ? 1 : 0;
        tintUnsafe.Opacity = !noTint && kind == "unsafe" ? 1 : 0;

        mapView.Focus(_mapCode, kind switch
        {
            "safe" => ProtectionState.Protected,
            "warn" or "checking" => ProtectionState.LocalOnly,
            _ => ProtectionState.Unprotected,
        });
        btnConnect.Classes.Set("safe", kind == "safe");
        btnConnect.Classes.Set("warn", kind == "warn" && state != ProtectionState.Unprotected);
    }
    private static async Task<int> StoredDelayAsync(string? indexId)
    {
        if (indexId.IsNullOrEmpty())
        {
            return 0;
        }
        var exs = await ProfileExManager.Instance.GetProfileExs();
        return exs.FirstOrDefault(t => t.IndexId == indexId)?.Delay ?? 0;
    }

    private async void RefreshActivePing()
    {
        if (_statusView == null || _activeIndexId.IsNullOrEmpty())
        {
            return;
        }
        var item = await ConfigDbQuery.GetProfileItem(_activeIndexId);
        _statusView.SetServer(item, FindModel(_activeIndexId)?.Delay ?? await StoredDelayAsync(_activeIndexId));
    }

    private void BtnConnect_Click(object? sender, RoutedEventArgs e) =>
        ToggleConnection(CurrentState() == ProtectionState.Unprotected);
    private async void ToggleConnection(bool connect)
    {
        var status = ViewModel?.StatusBarViewModel;
        if (status == null)
        {
            return;
        }

        if (connect)
        {
            if (_config.IndexId.IsNullOrEmpty())
            {
                NoticeManager.Instance.Enqueue(L.PickServer);
                return;
            }
            if (_config.UiItem.V2pnPreferTun && !await EnsureTunAllowedAsync(connectAfterRestart: true))
            {
                return;
            }
            ApplyMode(status, _config.UiItem.V2pnPreferTun);
            return;
        }

        // Off stops the core; the Windows proxy is cleared only if V2pn set it (V2pnConnection).
        SetTunDenied(false);
        if (status.EnableTun)
        {
            status.EnableTun = false;
        }
        status.SystemProxySelected = (int)ESysProxyType.ForcedClear;
    }

    private async Task ConnectToAsync(string? indexId)
    {
        var profiles = ViewModel?.ProfilesViewModel;
        if (profiles == null || indexId.IsNullOrEmpty())
        {
            return;
        }
        await profiles.SetDefaultServer(indexId);
        if (CurrentState() == ProtectionState.Unprotected)
        {
            ToggleConnection(true);
        }
    }

    #endregion Connection

    #region Mode and routing

    private bool _modeSync;
    private bool _routeSync;
    private bool _routeBusy;

    private static bool IsConnected(StatusBarViewModel status) =>
        status.EnableTun || (ESysProxyType)status.SystemProxySelected != ESysProxyType.ForcedClear;

    private static void ApplyMode(StatusBarViewModel status, bool tun)
    {
        if (tun)
        {
            status.EnableTun = true;
            // TUN already carries everything; a system proxy left behind would outlive a crash.
            status.SystemProxySelected = (int)ESysProxyType.ForcedClear;
        }
        else
        {
            status.SystemProxySelected = (int)ConnectionPrefs.ProxyMethod(_config);
            status.EnableTun = false;
        }
    }

    private void InitConnectionBar()
    {
        var needsAdmin = Utils.IsWindows() && !Utils.IsAdministrator();
        badgeAdmin.IsVisible = needsAdmin;
        ToolTip.SetTip(itemModeProxy, L.ModeProxyTip);
        ToolTip.SetTip(itemModeVpn, needsAdmin ? $"{L.ModeVpnTip} {L.ModeVpnNeedsAdmin}" : L.ModeVpnTip);
        lstMode.SelectionChanged += LstMode_SelectionChanged;

        btnRouteRussia.Click += async (_, _) => await SelectPresetAsync(RoutingPresetKind.RussiaDirect);
        btnRouteBlocked.Click += async (_, _) => await SelectPresetAsync(RoutingPresetKind.BlockedOnly);
        btnRouteAll.Click += async (_, _) => await SelectPresetAsync(RoutingPresetKind.All);
        InitPartnerRoutes();
        togBlockAds.IsChecked = _config.UiItem.V2pnBlockAds;
        togBlockAds.IsCheckedChanged += async (_, _) => await SetBlockAdsAsync(togBlockAds.IsChecked == true);
        lstCustomRoutes.SelectionChanged += async (_, _) =>
        {
            if (!_routeSync && lstCustomRoutes.SelectedItem is RoutingItem item)
            {
                await ActivateRoutingAsync(item.Id, sourcesChanged: false);
            }
        };
        SyncModeSelection();
    }

    private void SyncModeSelection()
    {
        var status = ViewModel?.StatusBarViewModel;
        if (status == null)
        {
            return;
        }
        var tun = IsConnected(status) ? status.EnableTun : _config.UiItem.V2pnPreferTun;
        _modeSync = true;
        lstMode.SelectedIndex = tun ? 1 : 0;
        _modeSync = false;
    }

    private async void LstMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var status = ViewModel?.StatusBarViewModel;
        if (_modeSync || status == null || lstMode.SelectedIndex < 0)
        {
            return;
        }
        var tun = lstMode.SelectedIndex == 1;
        var connected = IsConnected(status);
        if (tun && connected && !await EnsureTunAllowedAsync(connectAfterRestart: true))
        {
            SyncModeSelection();
            return;
        }

        _config.UiItem.V2pnPreferTun = tun;
        await ConfigTextHandler.SaveConfig();
        if (connected && tun != status.EnableTun)
        {
            ApplyMode(status, tun);
        }
    }

    /// <summary>
    /// TUN needs administrator rights on Windows. Instead of restarting silently (v2rayN's behaviour),
    /// ask first and restart with TUN already switched on, so the choice survives the restart.
    /// </summary>
    private async Task<bool> EnsureTunAllowedAsync(bool connectAfterRestart)
    {
        var status = ViewModel?.StatusBarViewModel;
        if (status == null)
        {
            return false;
        }
        SetTunDenied(false);
        if (status.AllowEnableTun())
        {
            return true;
        }
        if (!Utils.IsWindows())
        {
            var granted = await status.RequestTunPermissionAsync();
            SetTunDenied(!granted);
            return granted;
        }
        if (await UI.ShowYesNo(L.VpnRestartAsAdmin, L.Restart, icon: "SemiIconShieldStroked") != ButtonResult.Yes)
        {
            SetTunDenied(true);
            return false;
        }
        var (preferTun, enableTun, proxyType) =
            (_config.UiItem.V2pnPreferTun, _config.TunModeItem.EnableTun, _config.SystemProxyItem.SysProxyType);
        _config.UiItem.V2pnPreferTun = true;
        if (connectAfterRestart)
        {
            _config.TunModeItem.EnableTun = true;
            _config.SystemProxyItem.SysProxyType = ESysProxyType.ForcedClear;
        }
        await ConfigTextHandler.SaveConfig();
        if (!await AppManager.Instance.RebootAsAdmin())
        {
            // UAC declined: the app keeps running, so put back what it was doing before the question.
            _config.UiItem.V2pnPreferTun = preferTun;
            _config.TunModeItem.EnableTun = enableTun;
            _config.SystemProxyItem.SysProxyType = proxyType;
            await ConfigTextHandler.SaveConfig();
            SyncModeSelection();
            SetTunDenied(true);
        }
        return false;
    }

    private async Task InitRoutingPresetsAsync()
    {
        var status = ViewModel?.StatusBarViewModel;
        if (status == null)
        {
            return;
        }
        try
        {
            var changed = await RoutingPresets.EnsureAsync(_config);
            await SyncPartnerRoutesAsync(refresh: false);
            await UseV2pnRouteOnFreshInstallAsync();
            await status.RefreshRoutingsMenu();
            if (changed && RoutingPresets.IsPreset(status.SelectedRouting?.Id))
            {
                status.ReloadRequested.Publish();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn routing presets", ex);
        }
        status.RoutingItems.CollectionChanged += (_, _) => RefreshRouteUi();
        status.WhenAnyValue(s => s.SelectedRouting)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => RefreshRouteUi());
        RefreshRouteUi();
    }

    /// <summary>
    /// A fresh install starts on v2rayN's "everything except China". For someone who never chose it,
    /// "everything through the VPN" does the same in practice, needs no extra files and reads plainly;
    /// the first-run card then offers the Russian mode, which downloads its lists only when picked.
    /// </summary>
    private async Task UseV2pnRouteOnFreshInstallAsync()
    {
        var active = await ConfigDbHandler.GetDefaultRouting();
        if (active == null || !RoutingPresets.IsV2rayNBuiltin(active.Remarks)
            || (await ConfigDbQuery.SubItems())?.Count > 0
            || (await ConfigDbQuery.ProfileItems(string.Empty))?.Count > 0)
        {
            return;
        }
        if (await ConfigDbQuery.GetRoutingItem(RoutingPresets.IdAll) is { } all)
        {
            await ConfigDbHandler.SetDefaultRouting(all);
        }
    }

    private void RefreshWelcomeRoute()
    {
        var kind = RoutingPresets.KindOf(ViewModel?.StatusBarViewModel.SelectedRouting?.Id);
        btnWelcomeRouteRu.Classes.Set("active", kind == RoutingPresetKind.RussiaDirect);
        btnWelcomeRouteAll.Classes.Set("active", kind == RoutingPresetKind.All);
        NameOption(btnWelcomeRouteRu, L.RouteRussiaDirect, kind == RoutingPresetKind.RussiaDirect);
        NameOption(btnWelcomeRouteAll, L.RouteAll, kind == RoutingPresetKind.All);
        RefreshOnboardingRoute();
    }

    // The tick of the chosen option is visual only; a screen reader hears it in the name.
    private static void NameOption(Button option, string title, bool active) =>
        Avalonia.Automation.AutomationProperties.SetName(option, active ? title + L.OptionSelected : title);

    private void RefreshRouteUi()
    {
        var status = ViewModel?.StatusBarViewModel;
        if (status == null)
        {
            return;
        }
        var active = status.SelectedRouting;
        var kind = RoutingPresets.KindOf(active?.Id);
        txtRoute.Text = kind is { } k ? RoutingPresets.Title(k) : active != null ? RoutingPresets.DisplayName(active.Remarks) : L.RouteTitle;
        btnRouteRussia.Classes.Set("active", kind == RoutingPresetKind.RussiaDirect);
        btnRouteBlocked.Classes.Set("active", kind == RoutingPresetKind.BlockedOnly);
        btnRouteAll.Classes.Set("active", kind == RoutingPresetKind.All);
        NameOption(btnRouteRussia, L.RouteRussiaDirect, kind == RoutingPresetKind.RussiaDirect);
        NameOption(btnRouteBlocked, L.RouteBlockedOnly, kind == RoutingPresetKind.BlockedOnly);
        NameOption(btnRouteAll, L.RouteAll, kind == RoutingPresetKind.All);
        RefreshPartnerRoute(status, active);
        Avalonia.Automation.AutomationProperties.SetName(btnRoute, txtRoute.Text == L.RouteTitle ? L.RouteTitle : $"{L.RouteTitle}: {txtRoute.Text}");

        var custom = status.RoutingItems.Where(t => !RoutingPresets.IsPreset(t.Id) && !V2pnPartnerRoutes.IsPartnerRoute(t.Id)).ToList();
        _routeSync = true;
        lstCustomRoutes.ItemsSource = custom;
        lstCustomRoutes.SelectedItem = kind == null ? custom.FirstOrDefault(t => t.Id == active?.Id) : null;
        _routeSync = false;
        panelCustomRoutes.IsVisible = custom.Count > 0;
        RefreshWelcomeRoute();
        status.BlTrayRouteRussia = kind == RoutingPresetKind.RussiaDirect;
        status.BlTrayRouteBlocked = kind == RoutingPresetKind.BlockedOnly;
        status.BlTrayRouteAll = kind == RoutingPresetKind.All;

        // The bar was transparent until the route name came in, so its change of width is never seen.
        if (!_routeShown)
        {
            _routeShown = true;
            barConnection.Opacity = 1;
            if (_shownOnce)
            {
                _ = Motion.Enter(barConnection, dy: 0, ms: 160);
            }
        }
    }

    // A busy text disables the options (clicks would be dropped anyway); a failure keeps them, to retry.
    private void ShowRouteBusy(string? text, bool failed = false)
    {
        borderRouteBusy.IsVisible = text != null;
        txtRouteBusy.Text = text;
        spinRouteBusy.IsVisible = text != null && !failed;
        iconRouteFailed.IsVisible = text != null && failed;
        // The first-run card shows the same progress: its Russian option downloads the lists too.
        borderWelcomeBusy.IsVisible = text != null && cardWelcome.IsVisible;
        txtWelcomeBusy.Text = text;
        spinWelcomeBusy.IsVisible = borderWelcomeBusy.IsVisible && !failed;
        iconWelcomeFailed.IsVisible = borderWelcomeBusy.IsVisible && failed;
        var enabled = text == null || failed;
        btnWelcomeRouteRu.IsEnabled = enabled;
        btnWelcomeRouteAll.IsEnabled = enabled;
        OnboardingRouteBusy(text, failed);
        btnRouteRussia.IsEnabled = enabled;
        btnRouteBlocked.IsEnabled = enabled;
        btnRouteAll.IsEnabled = enabled;
        btnRoutePartner.IsEnabled = enabled;
    }

    // The failure stays readable for a few seconds, unless a new attempt has replaced it by then.
    private async Task ClearRouteFailureLaterAsync(string text)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (!_routeBusy && txtRouteBusy.Text == text)
        {
            ShowRouteBusy(null);
        }
    }

    /// <summary>A routing preset from the tray menu. A download of the Russian lists shows the window, which reports it.</summary>
    public async Task SelectPresetFromTrayAsync(RoutingPresetKind kind)
    {
        if (RoutingPresets.NeedsRussianSources(_config, kind))
        {
            ShowHideWindow(true);
        }
        await SelectPresetAsync(kind);
        RefreshRouteUi();
    }

    private async Task SelectPresetAsync(RoutingPresetKind kind)
    {
        var vm = ViewModel;
        if (_routeBusy || vm == null)
        {
            return;
        }
        _routeBusy = true;
        string? failure = null;
        try
        {
            var sourcesChanged = RoutingPresets.NeedsRussianSources(_config, kind);
            if (sourcesChanged)
            {
                ShowRouteBusy(L.RouteDownloading);
                // The lists didn't arrive: switching now would point the rules at tags the old files lack,
                // and the core would not start. The previous sources are back, so a retry downloads again.
                if (!await vm.UseRussianSourcesAsync())
                {
                    failure = L.RouteDownloadFailed;
                    await vm.StatusBarViewModel.RefreshRoutingsMenu();
                    ShowToast(ToastKind.Error, failure);
                    return;
                }
            }
            await RoutingPresets.EnsureAsync(_config);
            await ActivateRoutingAsync(RoutingPresets.Id(kind), sourcesChanged);
            btnRoute.Flyout?.Hide();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn routing presets", ex);
            NoticeManager.Instance.Enqueue(L.RouteFailed);
        }
        finally
        {
            _routeBusy = false;
            ShowRouteBusy(failure, failed: failure != null);
            if (failure != null)
            {
                _ = ClearRouteFailureLaterAsync(failure);
            }
        }
    }

    /// <summary>
    /// Activates a routing item directly instead of through StatusBarViewModel.SelectedRouting: setting
    /// that property twice in a row (refresh, then the new choice) races two async handlers.
    /// </summary>
    private async Task ActivateRoutingAsync(string id, bool sourcesChanged)
    {
        var status = ViewModel?.StatusBarViewModel;
        var item = await ConfigDbQuery.GetRoutingItem(id);
        if (status == null || item == null)
        {
            return;
        }
        var changed = await ConfigDbHandler.SetDefaultRouting(item) == 0;
        await status.RefreshRoutingsMenu();
        if (changed || sourcesChanged)
        {
            NoticeManager.Instance.SendMessageEx(ResUI.TipChangeRouting);
            status.ReloadRequested.Publish();
        }
    }

    private async Task SetBlockAdsAsync(bool on)
    {
        var status = ViewModel?.StatusBarViewModel;
        if (status == null || _config.UiItem.V2pnBlockAds == on)
        {
            return;
        }
        _config.UiItem.V2pnBlockAds = on;
        await ConfigTextHandler.SaveConfig();
        var changed = await RoutingPresets.EnsureAsync(_config);
        await status.RefreshRoutingsMenu();
        if (changed && RoutingPresets.IsPreset(status.SelectedRouting?.Id))
        {
            status.ReloadRequested.Publish();
        }
    }

    #endregion Mode and routing

    #region Sidebar

    private void LstTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_sidebar == null || lstTabs.SelectedIndex < 0 || _arrangingTabs)
        {
            return;
        }
        _sidebar.OpenedSub = null;
        _sidebar.Tab = TabOf(lstTabs.SelectedItem);
    }

    private void SideServer_Click(object? sender, RoutedEventArgs e) => ActivateRow((sender as Control)?.DataContext as SideRow);

    private void SideCountry_Click(object? sender, RoutedEventArgs e) => ActivateRow((sender as Control)?.DataContext as SideRow);

    private void SideSub_Click(object? sender, RoutedEventArgs e) => ActivateRow((sender as Control)?.DataContext as SideRow);

    private void SideAction_Click(object? sender, RoutedEventArgs e) => ActivateRow((sender as Control)?.DataContext as SideRow);

    // V2pn: "More" under the provider's announcement opens its announce-url (https or tg only, SubPanelHeaders).
    private void SideAnnounce_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SideAnnounceRow { Url: { } url } && SubPanelHeaders.ParseSecureLink(url) != null)
        {
            ProcUtils.ProcessStart(url);
        }
    }

    private async void ActivateRow(SideRow? target)
    {
        if (_sidebar == null || ViewModel == null)
        {
            return;
        }
        switch (target)
        {
            case SideServerRow server:
                await ConnectToAsync(server.Model.IndexId);
                return;

            case SideCountryRow country:
                _sidebar.ToggleCountry(country.Code);
                SelectRow(r => r is SideCountryRow c && c.Code == country.Code);
                return;

            case SideSubRow sub:
                _sidebar.OpenedSub = sub.Sub;
                return;

            case SideActionRow action:
                await RunActionAsync(action);
                return;

            case SideStatRow { Model: { } model }:
                await ConnectToAsync(model.IndexId);
                return;

            case SideStatRow { Code: { } code }:
                MapView_LocationClicked(code);
                return;
        }
    }

    private void SideStat_Click(object? sender, RoutedEventArgs e) => ActivateRow((sender as Control)?.DataContext as SideRow);

    private void SideHeaderLink_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: SideHeaderRow { ShowLink: true } header })
        {
            RunRowAction(() => RunActionAsync(new SideActionRow(header.LinkKind, header.Link ?? string.Empty, string.Empty)));
        }
    }

    private void SideSort_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: SideHeaderRow { Sort: { } sort } } button)
        {
            ShowSortMenu(button, sort);
        }
    }

    private void ShowSortMenu(Control target, SortState sort)
    {
        if (_sidebar == null)
        {
            return;
        }
        var sidebar = _sidebar;
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var mode in Enum.GetValues<SideSort>())
        {
            var label = new SortState(sort.Target, mode).Label;
            var usageOff = mode == SideSort.Usage && !SidebarViewModel.StatsEnabled;
            var item = new MenuItem
            {
                Header = usageOff ? L.SortUsageOff : label,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = mode == sort.Mode,
                IsEnabled = !usageOff,
            };
            Avalonia.Automation.AutomationProperties.SetName(item, mode == sort.Mode ? label + L.OptionSelected : label);
            item.Click += (_, _) => sidebar.SetSort(sort.Target, mode);
            flyout.Items.Add(item);
        }
        flyout.ShowAt(target);
    }

    // The row the keyboard was last on, as a key that survives a rebuild (rows are new objects then).
    private string? _lastRowKey;
    private bool _rowsByKeyboard;

    private static string? KeyOf(SideRow row) => row switch
    {
        SideServerRow s => "s:" + s.Model.IndexId,
        SideStatRow t => "t:" + (t.Model?.IndexId ?? t.Code),
        SideCountryRow c => "c:" + c.Code,
        SideSubRow u => "u:" + u.Sub.Id,
        SideActionRow a => "a:" + a.Kind,
        _ => null,
    };

    // A ping run, a favourite or a subscription update rebuilds the list; the keyboard keeps its place.
    private void RestoreRowFocus()
    {
        if (_lastRowKey is { } key && _rowsByKeyboard)
        {
            SelectRow(r => KeyOf(r) == key);
        }
    }

    // Rows are rebuilt on expand/collapse; keep keyboard focus on the same logical row.
    private void SelectRow(Func<SideRow, bool> match)
    {
        if (!lstRows.IsKeyboardFocusWithin)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            var row = _sidebar?.Rows.FirstOrDefault(match);
            if (row == null)
            {
                return;
            }
            lstRows.SelectedItem = row;
            lstRows.ContainerFromItem(row)?.Focus(NavigationMethod.Directional);
        }, DispatcherPriority.Background);
    }

    private void LstRows_KeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta;
        if (e.Key is Key.Enter or Key.Space && e.KeyModifiers == KeyModifiers.None && lstRows.SelectedItem is SideHeaderRow header)
        {
            // The header's own buttons sit inside the list item, out of reach of the arrow keys.
            if (header.ShowPing && _sidebar != null)
            {
                e.Handled = true;
                var sidebar = _sidebar;
                RunRowAction(() => sidebar.TogglePingAsync(header.PingScope));
            }
            else if (header.ShowLink)
            {
                e.Handled = true;
                RunRowAction(() => RunActionAsync(new SideActionRow(header.LinkKind, header.Link ?? string.Empty, string.Empty)));
            }
            else if (header.ShowSubActions)
            {
                e.Handled = true;
                ViewModel?.SubUpdateCmd.Execute().Subscribe();
            }
        }
        else if (lstRows.SelectedItem is SideServerRow server && ServerShortcut(e.Key, ctrl, e.KeyModifiers == KeyModifiers.None, server) is { } shortcut)
        {
            e.Handled = true;
            RunRowAction(shortcut);
        }
        else if (e.Key is Key.Enter or Key.Space && lstRows.SelectedItem is SideRow row)
        {
            e.Handled = true;
            ActivateRow(row);
        }
        else if (e.Key == Key.Right && lstRows.SelectedItem is SideCountryRow { IsExpanded: false }
                 || e.Key == Key.Left && lstRows.SelectedItem is SideCountryRow { IsExpanded: true })
        {
            e.Handled = true;
            ActivateRow((SideRow)lstRows.SelectedItem);
        }
        else if (e.Key == Key.Back && _sidebar?.OpenedSub != null)
        {
            e.Handled = true;
            _sidebar.OpenedSub = null;
        }
        else if (e.Key == Key.Back && _sidebar?.StatsOpen == true)
        {
            e.Handled = true;
            _sidebar.StatsOpen = false;
        }
    }

    // The server table's shortcuts, on the focused sidebar row (shown in the row menu too).
    private Func<Task>? ServerShortcut(Key key, bool ctrl, bool plain, SideServerRow row)
    {
        if (ViewModel == null)
        {
            return null;
        }
        var profiles = ViewModel.ProfilesViewModel;
        var model = row.Model;
        void Select()
        {
            profiles.SelectedProfile = model;
            profiles.SelectedProfiles = [model];
        }
        return (key, ctrl, plain) switch
        {
            (Key.Delete, _, true) => async () => { Select(); await profiles.RemoveServerAsync(); },
            (Key.C, true, _) => async () => { Select(); await profiles.Export2ShareUrlAsync(false); },
            (Key.R, true, _) => () => profiles.PingProfileAsync(model),
            (Key.D, true, _) => async () => { Select(); await profiles.EditServerAsync(); },
            (Key.B, true, _) => () => profiles.ToggleFavoriteAsync(model),
            _ => null,
        };
    }

    private static async void RunRowAction(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SidebarKeys", ex);
        }
    }

    // The menu key or Shift+F10 on a focused row (the row buttons themselves are mouse targets), and a
    // right-click on a row without its own menu or on the empty list: the list's own menu (MainWindow.SidebarMenu.cs).
    private void LstRows_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }
        var row = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .TakeWhile(c => c != lstRows)
            .Select(c => c.DataContext)
            .OfType<SideRow>()
            .FirstOrDefault();
        var container = row != null ? lstRows.ContainerFromItem(row) as Control : null;
        switch (row)
        {
            case SideServerRow server when container != null:
                e.Handled = true;
                ShowServerMenu(container, server, PlacementMode.BottomEdgeAlignedLeft);
                break;

            case SideSubRow sub when container != null:
                e.Handled = true;
                ShowSubMenu(container, sub, PlacementMode.BottomEdgeAlignedLeft);
                break;

            default:
                ShowSidebarMenu(e, container ?? lstRows, row?.Section is { } section ? SectionMenuItems(section) : null);
                break;
        }
    }

    private void TxtSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && _sidebar?.Rows.FirstOrDefault(r => r is SideServerRow or SideCountryRow or SideSubRow or SideActionRow) is { } first)
        {
            e.Handled = true;
            _rowsByKeyboard = true;
            lstRows.SelectedItem = first;
            lstRows.ContainerFromItem(first)?.Focus(NavigationMethod.Directional);
        }
    }

    private async Task RunActionAsync(SideActionRow row)
    {
        if (_sidebar == null || ViewModel == null)
        {
            return;
        }
        switch (row.Kind)
        {
            case SideActionKind.Fastest when row.TargetId.IsNullOrEmpty():
                // Nothing tested yet: the row runs the ping its subtitle asks for.
                await FindFastestAsync();
                break;

            case SideActionKind.Fastest when SidebarViewModel.PingIsStale:
                await RecheckFastestAsync();
                break;

            case SideActionKind.Fastest:
                await ConnectToAsync(row.TargetId);
                break;

            case SideActionKind.Back when _sidebar.StatsOpen:
                _sidebar.StatsOpen = false;
                break;

            case SideActionKind.Back:
                _sidebar.OpenedSub = null;
                break;

            case SideActionKind.ShowAllFavorites:
            case SideActionKind.ShowFewerFavorites:
                _sidebar.ToggleFavoritesExpanded();
                SelectRow(r => r is SideActionRow { IsMore: true });
                break;

            case SideActionKind.OpenStats:
                _sidebar.StatsOpen = true;
                break;

            case SideActionKind.EnableStats:
                _sidebar.SetStatsEnabled(true);
                TickUsage();
                ShowToast(ToastKind.Success, L.StatsEnableSub);
                break;

            case SideActionKind.DisableStats:
                ServerUsageManager.Instance.Tick(null);
                await ServerUsageManager.Instance.SaveAsync();
                _sidebar.SetStatsEnabled(false);
                break;

            case SideActionKind.ClearStats:
                if (await UI.ShowYesNo(L.StatsClearConfirm, L.StatsClear, danger: true) == ButtonResult.Yes)
                {
                    await ServerUsageManager.Instance.ClearAsync();
                    _sidebar.QueueRebuild();
                }
                break;

            case SideActionKind.AddSubscription:
                ViewModel.ProfilesViewModel.AddSubCmd.Execute().Subscribe();
                break;

            case SideActionKind.UpdateAll:
                ViewModel.SubUpdateCmd.Execute().Subscribe();
                break;

            case SideActionKind.UpdateSub:
                UpdateSubscription(row.TargetId);
                break;

            case SideActionKind.ClearSearch:
                txtSearch.Text = string.Empty;
                txtSearch.Focus();
                break;

            case SideActionKind.BrowseCountries:
                SelectTab(SidebarTab.Countries);
                break;
        }
    }

    /// <summary>
    /// Starts a ping run and waits for its end: PingProfilesAsync only starts it, the end arrives as
    /// PingTestFinished. False when nothing was started (nothing to test, or a run already going).
    /// </summary>
    private async Task<bool> PingAndWaitAsync(Func<Task> start)
    {
        var profiles = ViewModel?.ProfilesViewModel;
        if (profiles == null || profiles.IsPingTesting)
        {
            return false;
        }
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = profiles.PingTestFinished.AsObservable().Subscribe(_ => finished.TrySetResult());
        await start();
        if (!profiles.IsPingTesting && !finished.Task.IsCompleted)
        {
            return false;
        }
        // A run reports its end even when it fails; the timeout only guards against waiting forever.
        await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromMinutes(5)));
        return finished.Task.IsCompleted;
    }

    // A run started elsewhere (the sidebar's ping) ends first: two at once would mix their progress.
    private static async Task WaitForPingEndAsync(ProfilesViewModel profiles)
    {
        if (!profiles.IsPingTesting)
        {
            return;
        }
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = profiles.PingTestFinished.AsObservable().Subscribe(_ => finished.TrySetResult());
        await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromMinutes(5)));
    }

    // The quickest of these servers by the stored test results, or null when none answered.
    private static async Task<string?> FastestOfAsync(IEnumerable<string> indexIds)
    {
        var ids = indexIds.ToHashSet();
        var exs = await ProfileExManager.Instance.GetProfileExs();
        return exs.Where(t => ids.Contains(t.IndexId) && t.Delay > 0).MinBy(t => t.Delay)?.IndexId;
    }

    /// <summary>
    /// The fastest-server row before any ping: tests every server, then selects the winner. Selecting it
    /// switches a running connection over; when disconnected it stays the choice for the next Connect.
    /// </summary>
    private async Task FindFastestAsync()
    {
        var profiles = ViewModel?.ProfilesViewModel;
        if (profiles == null || _sidebar == null)
        {
            return;
        }
        var ids = profiles.ProfileItems.Select(m => m.IndexId).ToList();
        if (ids.Count == 0 || !await PingAndWaitAsync(_sidebar.PingAllAsync))
        {
            return;
        }
        var ok = ids.ToHashSet();
        var best = SidebarViewModel.PickFastest(profiles.ProfileItems.Where(m => ok.Contains(m.IndexId)))?.IndexId ?? await FastestOfAsync(ids);
        if (best == null)
        {
            ShowToast(ToastKind.Warning, L.NoneResponding);
            return;
        }
        await profiles.SetDefaultServer(best);
    }

    /// <summary>
    /// "Fastest server" with an old result: the servers that were quickest, the favorites and the recent
    /// ones are tested again (seconds, not the minute a whole big subscription takes), and the winner is
    /// connected. When none of them answers, everything is tested.
    /// </summary>
    private async Task RecheckFastestAsync()
    {
        var profiles = ViewModel?.ProfilesViewModel;
        if (profiles == null || _sidebar == null)
        {
            return;
        }
        var sidebar = _sidebar;
        var candidates = sidebar.FastestCandidates();
        if (candidates.Count == 0 || !await PingAndWaitAsync(() => profiles.PingProfilesAsync(candidates)))
        {
            await FindFastestAsync();
            return;
        }
        var ids = candidates.Select(m => m.IndexId).ToHashSet();
        var best = SidebarViewModel.PickFastest(profiles.ProfileItems.Where(m => ids.Contains(m.IndexId)));
        if (best == null)
        {
            await FindFastestAsync();
            return;
        }
        await ConnectToAsync(best.IndexId);
    }

    /// <summary>The country's bolt: the fastest tested server, or a ping of the country first when none is.</summary>
    private async Task ConnectFastestInAsync(SideCountryRow row)
    {
        // A provider's pool is "pick the best one for me", and its members come and go by the hour: of
        // thirty-four, a third may answer. An old result is not trusted there; the pool is tested first.
        if (row.BestId.IsNotEmpty() && !row.IsSeries)
        {
            await ConnectToAsync(row.BestId);
            return;
        }
        var profiles = ViewModel?.ProfilesViewModel;
        if (profiles == null || !await PingAndWaitAsync(() => profiles.PingProfilesAsync(row.Servers)))
        {
            return;
        }
        var best = await FastestOfAsync(row.Servers.Select(m => m.IndexId));
        if (best == null)
        {
            ShowToast(ToastKind.Warning, L.NoneResponding);
            return;
        }
        await ConnectToAsync(best);
    }

    private async void SidePing_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_sidebar != null)
        {
            await _sidebar.TogglePingAsync((sender as Control)?.DataContext is SideHeaderRow header ? header.PingScope : null);
        }
    }

    private async void SideStar_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: SideServerRow row } && ViewModel != null)
        {
            await ViewModel.ProfilesViewModel.ToggleFavoriteAsync(row.Model);
        }
    }

    private async void SideCountryConnect_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: SideCountryRow row })
        {
            await ConnectFastestInAsync(row);
        }
    }

    private async void SideSubUpdate_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control { DataContext: SideSubRow row } || ViewModel == null || row.Sub.Id.IsNullOrEmpty() || row.Busy.IsBusy)
        {
            return;
        }
        await UpdateSubWithStateAsync(row.Sub.Id, false);
    }

    private void SideSubsUpdate_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel?.SubUpdateCmd.Execute().Subscribe();
    }

    private void SideSubsAdd_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel?.ProfilesViewModel.AddSubCmd.Execute().Subscribe();
    }

    private void SideBackUpdate_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: SideActionRow row } && row.TargetId.IsNotEmpty())
        {
            UpdateSubscription(row.TargetId);
        }
    }

    // A new subscription is fetched at once, so its servers, name and traffic fill themselves in.
    private Task UpdateAddedSubscriptionAsync(string subId) => UpdateSubWithStateAsync(subId, false);

    private void UpdateSubscription(string? subId)
    {
        if (ViewModel == null || subId.IsNullOrEmpty())
        {
            return;
        }
        _ = UpdateSubWithStateAsync(subId, false);
    }

    // One subscription with its row spinner; an update that brought nothing is said out loud.
    private async Task UpdateSubWithStateAsync(string subId, bool viaVpn)
    {
        if (ViewModel == null || subId.IsNullOrEmpty())
        {
            return;
        }
        var busy = _sidebar?.SubBusy(subId);
        busy?.IsBusy = true;
        _subUpdatesRunning++;
        try
        {
            var before = await V2pnSubs.SnapshotAsync(subId);
            await ViewModel.UpdateSubscriptionProcess(subId, viaVpn);
            ReportSubUpdates(before, await V2pnSubs.NotUpdatedAsync(before));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("UpdateSubWithState", ex);
        }
        finally
        {
            busy?.IsBusy = false;
            _subUpdatesRunning--;
            RefreshServersStatus();
        }
    }

    // How many subscriptions are downloading right now; the walkthrough shows a spinner while any is.
    private int _subUpdatesRunning;

    // "Update all" (either way) compares every subscription's update time from the start of the run to its end.
    private IDisposable TrackSubUpdates(ReactiveCommand<RxVoid, RxVoid> command)
    {
        Task<Dictionary<string, long>>? snapshot = null;
        return command.IsExecuting
            .DistinctUntilChanged()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async busy =>
            {
                try
                {
                    if (busy)
                    {
                        snapshot = V2pnSubs.SnapshotAsync(null);
                        return;
                    }
                    if (snapshot == null)
                    {
                        return;
                    }
                    var pending = snapshot;
                    snapshot = null;
                    var before = await pending;
                    ReportSubUpdates(before, await V2pnSubs.NotUpdatedAsync(before));
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("TrackSubUpdates", ex);
                }
            });
    }

    private void ReportSubUpdates(IReadOnlyDictionary<string, long> tried, List<SubItem> failed)
    {
        _sidebar?.SetUpdateResults(tried.Keys, failed.Select(s => s.Id));
        if (failed.Count == 0)
        {
            return;
        }
        if (OnboardingShown && ServerCount() == 0)
        {
            _obImportError = L.ObNoServersImported;
            RefreshServersStatus();
        }
        var only = failed.Count == 1 ? SubInfoManager.Instance.Get(failed[0].Id) : null;
        var message = only is { LastResult: not (EV2pnSubUpdateResult.None or EV2pnSubUpdateResult.Ok) }
            ? string.Format(L.SubUpdateFailedOne, SideSubRow.NameOf(failed[0]), L.SubResultLong(only.LastResult, only.LastResultDetail))
            : failed.Count == 1
            ? string.Format(L.SubUpdateFailed, SideSubRow.NameOf(failed[0]))
            : string.Format(L.SubsUpdateFailed, string.Join(", ", failed.Select(SideSubRow.NameOf)));
        // Through the running VPN the provider's site is often reachable again.
        if (CoreManager.Instance.IsCoreAlive)
        {
            message += " " + string.Format(L.SubUpdateTryVpn, L.MenuSubUpdateViaVpn);
        }
        ShowToast(ToastKind.Error, message);
    }

    private void SideServerMenu_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: SideServerRow row } button)
        {
            ShowServerMenu(button, row, PlacementMode.BottomEdgeAlignedRight);
        }
    }

    // Right-click or the menu key on a row opens the same menu as its ⋮ button.
    private void SideServer_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: SideServerRow row } control)
        {
            ShowServerMenu(control, row, ContextPlacement(e, control));
        }
    }

    private void SideSub_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: SideSubRow row } control)
        {
            ShowSubMenu(control, row, ContextPlacement(e, control));
        }
    }

    private void SideCountry_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control { DataContext: SideCountryRow row } control || ViewModel == null)
        {
            return;
        }
        var profiles = ViewModel.ProfilesViewModel;
        var flyout = new MenuFlyout { Placement = ContextPlacement(e, control) };
        flyout.Items.Add(Item(L.ConnectFastestIn, () => ConnectFastestInAsync(row), "SemiIconBolt"));
        flyout.Items.Add(Item(L.TestPing, () => profiles.PingProfilesAsync(row.Servers), "SemiIconPulse"));
        flyout.ShowAt(control, flyout.Placement == PlacementMode.Pointer);
    }

    private static PlacementMode ContextPlacement(ContextRequestedEventArgs e, Control control) =>
        e.TryGetPosition(control, out _) ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft;

    private void ShowServerMenu(Control target, SideServerRow row, PlacementMode placement)
    {
        if (ViewModel == null)
        {
            return;
        }
        var profiles = ViewModel.ProfilesViewModel;
        var model = row.Model;

        void Select()
        {
            profiles.SelectedProfile = model;
            profiles.SelectedProfiles = [model];
        }

        var flyout = new MenuFlyout { Placement = placement };
        flyout.Items.Add(Item(L.Connect, async () => await ConnectToAsync(model.IndexId), "SemiIconPlayCircle"));
        flyout.Items.Add(Item(model.IsFavorite ? L.Unfavorite : L.Favorite, async () => await profiles.ToggleFavoriteAsync(model), model.IsFavorite ? "SemiIconStar" : "SemiIconStarStroked", gesture: new KeyGesture(Key.B, KeyModifiers.Control)));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(L.TestPing, async () => await profiles.PingProfileAsync(model), "SemiIconBolt", gesture: new KeyGesture(Key.R, KeyModifiers.Control)));
        flyout.Items.Add(Item(L.MenuTcping, async () => { Select(); await profiles.ServerSpeedtest(ESpeedActionType.Tcping); }, "SemiIconPulse"));
        flyout.Items.Add(Item(L.TestSpeed, async () => { Select(); await profiles.ServerSpeedtest(ESpeedActionType.Speedtest); }, "SemiIconStopwatchStroked"));
        flyout.Items.Add(new Separator());
        if (V2pnLocks.Instance.ForSub(model.Subid) is { Settings: true } locked)
        {
            // V2pn: the partner closed this server's settings; the menu says so instead of offering what it would refuse.
            flyout.Items.Add(new MenuItem
            {
                Header = string.Format(L.LockedMenu, locked.PartnerName),
                IsEnabled = false,
                Icon = new PathIcon { Classes = { "mi" }, Data = this.FindResource("SemiIconLock") as Geometry },
            });
        }
        else
        {
            flyout.Items.Add(Item(L.Edit, async () => { Select(); await profiles.EditServerAsync(); }, "SemiIconEditStroked", gesture: new KeyGesture(Key.D, KeyModifiers.Control)));
            flyout.Items.Add(Item(L.Share, async () => { Select(); await profiles.ShareServerAsync(); }, "SemiIconQrCode"));
            flyout.Items.Add(Item(L.CopyLink, async () => { Select(); await profiles.Export2ShareUrlAsync(false); }, "SemiIconCopyStroked", gesture: new KeyGesture(Key.C, KeyModifiers.Control)));
        }
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(L.MenuDelete, async () => { Select(); await profiles.RemoveServerAsync(); }, "SemiIconDeleteStroked", danger: true, gesture: new KeyGesture(Key.Delete)));
        // On the home tab: the section the server is listed in (MainWindow.SidebarMenu.cs).
        if (row.Section is { } section)
        {
            flyout.Items.Add(new Separator());
            flyout.Items.Add(Sub(string.Format(L.SbSection, L.ApHomeSection(section)), SectionMenuItems(section)));
        }
        flyout.ShowAt(target, placement == PlacementMode.Pointer);
    }

    private void SideSubMenu_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: SideSubRow row } button)
        {
            ShowSubMenu(button, row, PlacementMode.BottomEdgeAlignedRight);
        }
    }

    private void ShowSubMenu(Control target, SideSubRow row, PlacementMode placement)
    {
        if (ViewModel == null || _sidebar == null)
        {
            return;
        }
        var vm = ViewModel;

        // Subscription commands act on the current group, so open the subscription first.
        void Open() => _sidebar.OpenedSub = row.Sub;

        var flyout = new MenuFlyout { Placement = placement };
        flyout.Items.Add(Item(L.UpdateSubscription, () => UpdateSubWithStateAsync(row.Sub.Id ?? string.Empty, false), "SemiIconRefresh"));
        // Through the VPN only works while it is running; that is what a blocked provider's site needs.
        if (CoreManager.Instance.IsCoreAlive)
        {
            flyout.Items.Add(Item(L.MenuSubUpdateViaVpn, () => UpdateSubWithStateAsync(row.Sub.Id ?? string.Empty, true), "SemiIconRefresh2"));
        }
        flyout.Items.Add(Item(L.TestPing, async () =>
        {
            var servers = await ConfigDbQuery.ProfileModels(row.Sub.Id ?? string.Empty, string.Empty);
            var ids = servers?.Select(s => s.IndexId).ToHashSet() ?? [];
            var models = vm.ProfilesViewModel.ProfileItems.Where(m => ids.Contains(m.IndexId)).ToList();
            await vm.ProfilesViewModel.PingProfilesAsync(models);
        }, "SemiIconPulse"));
        flyout.Items.Add(Item(L.RemoveDead, async () => await RemoveDeadAsync(row), "SemiIconMinusCircleStroked"));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(L.Edit, () => { Open(); vm.ProfilesViewModel.EditSubCmd.Execute().Subscribe(); return Task.CompletedTask; }, "SemiIconEditStroked"));
        flyout.Items.Add(Item(L.MenuDelete, () => { Open(); vm.ProfilesViewModel.DeleteSubCmd.Execute().Subscribe(); return Task.CompletedTask; }, "SemiIconDeleteStroked", danger: true));
        flyout.ShowAt(target, placement == PlacementMode.Pointer);
    }

    private async Task RemoveDeadAsync(SideSubRow row)
    {
        var profiles = ViewModel?.ProfilesViewModel;
        var subId = row.Sub.Id ?? string.Empty;
        if (profiles == null)
        {
            return;
        }
        var exs = await ProfileExManager.Instance.GetProfileExs();
        var failed = exs.Where(t => t.Delay == -1).Select(t => t.IndexId).ToHashSet();
        var servers = await ConfigDbQuery.ProfileModels(subId, string.Empty) ?? [];
        if (!servers.Any(s => failed.Contains(s.IndexId)))
        {
            NoticeManager.Instance.Enqueue(L.RemoveDeadNone);
            return;
        }
        if (await UI.ShowYesNo(string.Format(L.RemoveDeadConfirm, row.Sub.Remarks), L.MenuDelete, danger: true) != ButtonResult.Yes)
        {
            return;
        }
        var count = await ConfigDbHandler.RemoveInvalidServerResult(subId);
        await profiles.RefreshServersBiz();
        await profiles.RefreshNavCounts();
        // v2rayN's own text says "invalid", which reads as an error and breaks the Russian plural.
        ShowToast(ToastKind.Success, string.Format(L.RemoveDeadDone, Math.Max(count, 0)));
    }

    // The gesture is shown only; the sidebar list handles the keys itself (LstRows_KeyDown).
    private static MenuItem Item(string header, Func<Task> action, string? icon = null, bool danger = false, KeyGesture? gesture = null)
    {
        var item = new MenuItem { Header = header, InputGesture = gesture };
        if (icon != null)
        {
            var glyph = new PathIcon { Classes = { "mi" } };
            glyph.Bind(PathIcon.DataProperty, glyph.GetResourceObservable(icon));
            item.Icon = glyph;
        }
        if (danger)
        {
            item.Classes.Add("danger");
        }
        item.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Logging.SaveLog("SidebarMenu", ex);
            }
        };
        return item;
    }

    #endregion Sidebar

    #region Event

    private void OnProgramStarted(object state, bool timeout)
    {
        Dispatcher.UIThread.Post(() =>
            {
                ShowHideWindow(true);
                // A second start may have come from a click on a v2pn:// link.
                _ = HandleStashedLinkAsync();
            },
            DispatcherPriority.Default);
    }

    private async Task DelegateSnackMsg(string content)
    {
        if (_manager == null || content.IsNullOrEmpty())
        {
            return;
        }
        // With no servers yet the welcome card says what to do; v2rayN's "invalid configuration"
        // would greet a new person with an error.
        if (content == ResUI.CheckServerSettings && (ViewModel?.ProfilesViewModel.AllServersCount ?? 0) == 0)
        {
            return;
        }
        // v2rayN's generic notices, in words that say what happened.
        if (content == ResUI.OperationSuccess)
        {
            content = L.Done;
        }
        else if (content == ResUI.NeedRebootTips)
        {
            content = L.LanguageAfterRestart;
        }
        else if (content == ResUI.CheckServerSettings)
        {
            content = L.ServerConfigBroken;
        }
        // v2rayN announces a started core with the server summary; show the server instead of its raw summary.
        // Information, not success: the availability check has not answered yet.
        var item = await ConfigDbQuery.GetProfileItem(_config.IndexId);
        if (item != null && content == item.GetSummary())
        {
            ShowNotice(Toasts.Server(item), NotificationType.Information, TimeSpan.FromSeconds(4));
            return;
        }
        var kind = Toasts.Guess(content);
        ShowNotice(Toasts.Message(kind, null, content), Toasts.TypeOf(kind), TimeSpan.FromSeconds(kind == ToastKind.Error ? 8 : 5));
    }

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                Dispatcher.UIThread.Post(() => ShowHideWindow(null));
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                AppEvents.SysProxyChangeRequested.Publish((ESysProxyType)((int)e - 1));
                break;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_blCloseByUser)
        {
            return;
        }

        Logging.SaveLog("OnClosing -> " + e.CloseReason.ToString());

        switch (e.CloseReason)
        {
            case WindowCloseReason.OwnerWindowClosing or WindowCloseReason.WindowClosing:
                e.Cancel = true;
                if (Utils.IsWindows())
                {
                    _ = CloseFromButtonAsync();
                }
                else
                {
                    ShowHideWindow(false);
                }
                break;

            // The system is signing out or shutting down, or the app is asked to quit (Cmd+Q, Dock > Quit on
            // macOS). The cleanup is awaited before the window goes, or the process ends first: the system
            // proxy keeps pointing at a port nobody serves and, without a job object, the core keeps running.
            case WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown when !e.IsProgrammatic:
                e.Cancel = true;
                _ = CleanUpForShutdownAsync();
                break;

            case WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown:
                await AppManager.Instance.AppExitAsync(false);
                break;
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// X on Windows. The first time, says that V2pn stays in the tray and lets the user choose; afterwards
    /// follows that choice (Settings, "minimize to tray on close"). The quit path lives only here: hiding
    /// for a screen capture also goes through ShowHideWindow(false).
    /// </summary>
    private async Task CloseFromButtonAsync()
    {
        if (_closeAsking)
        {
            return;
        }
        try
        {
            if (!_config.UiItem.V2pnCloseAsked)
            {
                _closeAsking = true;
                var answer = await UI.ShowChoice(L.CloseToTrayTitle, L.CloseToTrayBody, L.CloseToTrayHide, L.Quit, icon: "SemiIconShrink");
                _closeAsking = false;
                // Dismissed without an answer: the window stays, and the question comes back next time.
                if (answer == ButtonResult.None)
                {
                    return;
                }
                _config.UiItem.Hide2TrayWhenClose = answer == ButtonResult.Yes;
                _config.UiItem.V2pnCloseAsked = true;
                await ConfigTextHandler.SaveConfig();
            }
            if (_config.UiItem.Hide2TrayWhenClose)
            {
                ShowHideWindow(false);
                return;
            }
            _blCloseByUser = true;
            StorageUI();
            await AppManager.Instance.AppExitAsync(true);
        }
        catch (Exception ex)
        {
            _closeAsking = false;
            Logging.SaveLog("CloseFromButtonAsync", ex);
        }
    }

    /// <summary>
    /// Holds sign-out or shutdown for the moment AppExitAsync needs (the system proxy first, with the
    /// keep-system-settings rule of SysProxyHandler, then the config and the core), and says why.
    /// </summary>
    private async Task CleanUpForShutdownAsync()
    {
        if (_shutdownCleaning)
        {
            return;
        }
        _shutdownCleaning = true;
        var hwnd = Utils.IsWindows() ? TryGetPlatformHandle()?.Handle ?? IntPtr.Zero : IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            WindowsShell.BlockShutdown(hwnd, L.ShutdownCleaning);
        }
        try
        {
            await AppManager.Instance.AppExitAsync(false);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("CleanUpForShutdownAsync", ex);
        }
        finally
        {
            if (hwnd != IntPtr.Zero)
            {
                WindowsShell.UnblockShutdown(hwnd);
            }
            Shutdown(true);
        }
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        // An in-window dialog (Appearance, QR code, backup) is modal: Esc closes it, and the window's
        // shortcuts must not act on the page behind its scrim.
        if (IsDialogOpen())
        {
            if (e.Key == Key.Escape)
            {
                DialogHost.Close(null);
                e.Handled = true;
            }
            return;
        }
        if (e.Key == Key.R && e.KeyModifiers is (KeyModifiers.Control | KeyModifiers.Shift) or (KeyModifiers.Meta | KeyModifiers.Shift))
        {
            e.Handled = true;
            await PingAllAsync();
            return;
        }
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            switch (e.Key)
            {
                // The log page's editor is not a TextBox, but Ctrl+V there is still a paste.
                case Key.V when e.Source is not (TextBox or AvaloniaEdit.Editing.TextArea):
                    e.Handled = true;
                    await AddServerViaClipboardAsync();
                    break;

                // The screen scan exists on Windows only.
                case Key.S when Utils.IsWindows():
                    e.Handled = true;
                    await ScanScreenTaskAsync();
                    break;

                case Key.F:
                    txtSearch.Focus();
                    txtSearch.SelectAll();
                    e.Handled = true;
                    break;

                // The usual desktop shortcut for settings (Cmd+, on a Mac).
                case Key.OemComma:
                    e.Handled = true;
                    ViewModel?.OptionSettingCmd.Execute().Subscribe();
                    break;

                case Key.D1:
                case Key.D2:
                case Key.D3:
                    SegmentThumb.SnapNext(lstTabs);
                    lstTabs.SelectedIndex = e.Key - Key.D1;
                    txtSearch.Text = string.Empty;
                    e.Handled = true;
                    break;
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (pageOverlay.IsVisible)
            {
                e.Handled = true;
                ClosePage();
            }
            else if (txtSearch.Text.IsNotEmpty())
            {
                e.Handled = true;
                txtSearch.Text = string.Empty;
            }
            else if (_sidebar?.OpenedSub != null)
            {
                e.Handled = true;
                _sidebar.OpenedSub = null;
            }
            else if (_sidebar?.StatsOpen == true)
            {
                e.Handled = true;
                _sidebar.StatsOpen = false;
            }
        }
        else if (e.Key == Key.F5)
        {
            e.Handled = true;
            ViewModel?.Reload();
        }
    }

    private static bool IsDialogOpen()
    {
        try
        {
            return DialogHost.IsDialogOpen(null);
        }
        catch (InvalidOperationException)
        {
            // No DialogHost loaded yet (the window is still starting).
            return false;
        }
    }

    /// <summary>Ctrl+Shift+R and the menu: pings every server of the list, or stops a run in progress.</summary>
    private async Task PingAllAsync()
    {
        if (_sidebar == null)
        {
            return;
        }
        try
        {
            if (_sidebar.Ping.IsTesting)
            {
                await _sidebar.TogglePingAsync();
            }
            else
            {
                await _sidebar.PingAllAsync();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("PingAll", ex);
        }
    }

    private void MenuPromotion_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart($"{Utils.Base64Decode(Global.PromotionUrl)}?t={DateTime.Now.Ticks}");
    }

    private void MenuSettingsSetUWP_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetBinPath("EnableLoopback.exe"));
    }

    /// <summary>
    /// Paste from the welcome card, the + menu, Ctrl+V, the server table and the tray. Subscription links
    /// become subscriptions that download right away; anything else goes to v2rayN's importer.
    /// </summary>
    public async Task AddServerViaClipboardAsync()
    {
        if (ViewModel == null)
        {
            return;
        }
        var text = (await AvaUtils.GetClipboardData(this))?.Trim();
        if (text.IsNullOrEmpty())
        {
            _obImportError = L.ClipboardEmpty;
            ShowToast(ToastKind.Warning, L.ClipboardEmpty);
            return;
        }
        // A provider's page on V2pn's site with its subscription (…/p/<id>/#sub=…): asked about as its button's link is.
        if (V2pnDeepLink.Parse(text) is { Scheme: "page" } page)
        {
            await AskToAddLinkAsync(page);
            return;
        }
        if (V2pnSubs.SubscriptionLinks(text) is { Count: > 0 } links)
        {
            await AddSubscriptionLinksAsync(links);
            return;
        }
        if (await ViewModel.AddServerViaClipboardAsync(text, notifyFailure: false) <= 0)
        {
            _obImportError = L.PasteNotRecognized;
            ShowToast(ToastKind.Error, L.PasteNotRecognized);
        }
    }

    private async Task AddSubscriptionLinksAsync(List<string> links)
    {
        if (ViewModel == null)
        {
            return;
        }
        List<string> added = [];
        List<string> existing = [];
        foreach (var url in links)
        {
            if (await V2pnSubs.AddSubscriptionAsync(_config, url) is { } result)
            {
                (result.Existed ? existing : added).Add(result.Item.Id);
            }
        }
        if (added.Count + existing.Count == 0)
        {
            ShowToast(ToastKind.Error, ResUI.OperationFailed);
            return;
        }

        await ViewModel.ProfilesViewModel.RefreshSubscriptions();
        // Show where the new subscription is, so its row and spinner are in view while it downloads.
        txtSearch.Text = string.Empty;
        if (_sidebar != null)
        {
            _sidebar.OpenedSub = null;
        }
        SelectTab(SidebarTab.Subscriptions);
        ShowToast(ToastKind.Info, added.Count > 0 ? L.SubAddedUpdating : L.SubAlreadyAdded);

        foreach (var id in added.Concat(existing))
        {
            await UpdateAddedSubscriptionAsync(id);
        }
    }

    public async Task ScanScreenTaskAsync()
    {
        ShowHideWindow(false);

        await Task.Delay(200);

        var bytes = QRCodeAvaloniaUtils.CaptureScreen();
        if (bytes != null && ViewModel != null)
        {
            await ViewModel.ScanScreenResult(bytes);
        }

        ShowHideWindow(true);
    }

    private void MenuCheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        _checkUpdateView ??= new CheckUpdateView();
        _checkUpdateView.ViewModel = ViewModel?.CheckUpdateViewModel;
        _ = ShowRoomyDialogAsync(_checkUpdateView);

        // Only v2rayN's core notice is dismissed by looking; V2pn's own stays until it is installed.
        AppEvents.HasUpdateNotified.Publish(false);
    }

    private void MenuBackupAndRestore_Click(object? sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        _ = ShowRoomyDialogAsync(_backupAndRestoreView);
    }

    private async void MenuClose_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (await UI.ShowYesNo(L.QuitConfirm, L.Quit, icon: "SemiIconPower") != ButtonResult.Yes)
            {
                return;
            }

            _blCloseByUser = true;
            StorageUI();

            await AppManager.Instance.AppExitAsync(true);
        }
        catch
        {
            // Ignore
        }
    }

    private void Shutdown(bool obj)
    {
        if (obj is bool b && _blCloseByUser == false)
        {
            _blCloseByUser = b;
        }
        StorageUI();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            HotkeyManager.Instance.Dispose();
            desktop.Shutdown();
        }
    }

    #endregion Event

    #region UI

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ??
                    (Utils.IsLinux() || Utils.IsMacOS()
                    ? (!AppManager.Instance.ShowInTaskbar ^ (WindowState == WindowState.Minimized))
                    : !IsInFront());
        if (bl)
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = _restoreState;
            }
            Activate();
            Focus();
        }
        else
        {
            if (Utils.IsLinux() && _config.UiItem.Hide2TrayWhenClose == false)
            {
                WindowState = WindowState.Minimized;
                return;
            }

            foreach (var ownedWindow in OwnedWindows)
            {
                ownedWindow.Close();
            }
            Hide();
        }

        AppManager.Instance.ShowInTaskbar = bl;
    }

    // Windows: the tray icon and the show/hide hotkey raise a hidden, minimized or covered window, and hide
    // it only when it was in front. A tray click deactivates the window just before it arrives, so a
    // window active a moment ago counts as in front.
    private bool IsInFront() =>
        IsVisible && WindowState != WindowState.Minimized
        && (IsActive || DateTime.UtcNow - _deactivatedAt < TimeSpan.FromMilliseconds(300));

    protected override async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);
        // The bounds just applied are the normal ones. QueueNormalBounds records only while the window is
        // Normal and visible, so a session that starts maximized or in the tray would otherwise save the
        // maximized size and no position.
        if (Width > 0 && Height > 0)
        {
            _normalSize = new Size(Width, Height);
            _normalPosition = Position;
        }
        // Reopens maximized when it was left maximized; a start in the tray restores that on the first show.
        if (ConfigTextHandler.GetWindowSizeItem(GetType().Name) is { IsMaximized: true })
        {
            if (_startHidden)
            {
                _restoreState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }
        if (_startHidden)
        {
            ShowHideWindow(false);
        }
        // Sign-in entries are tidied at every start: one fixed name, the sign-in argument, nothing left
        // from removed or test copies (registry or task; schtasks is slow, hence the background).
        var moved = V2pnImport.Imported != null;
        _ = Task.Run(async () =>
        {
            // The move removed v2rayN's sign-in entry; when the moved settings ask for one, V2pn writes its own.
            if (moved)
            {
                await AutoStartupHandler.UpdateTask(_config);
            }
            await AutoStartupHandler.MigrateAutostartArg(_config);
        });
        WarnAboutOpenLanProxyOnce();
        _ = HandleStartupLinkAsync();
        // V2pn: what providers ask for at start (connect, update, test), once the start itself is done.
        _ = RunProviderStartAsync();
        if (V2pnImport.Imported is { } imported)
        {
            ShowToast(ToastKind.Success, string.Format(L.ImportDone, imported.Servers, imported.Subscriptions));
        }
        if (await AppUpdates.InstallPendingAtStartAsync(_config, message => ShowToast(ToastKind.Info, message)))
        {
            return;
        }
        AppUpdates.Start(_config);
    }

    private string? _announcedVersion;


    private void OnAppUpdateState(EV2pnUpdateState state)
    {
        RefreshUpdateBadge();
        var updater = V2pnAppUpdater.Instance;
        // One toast per version: when it is ready, or found while automatic updates are off.
        var worthTelling = state == EV2pnUpdateState.Ready
            || (state == EV2pnUpdateState.Available && (!AppUpdates.Auto(_config) || V2pnAppUpdater.Instance.ByWinGet));
        if (!worthTelling || updater.LatestVersion == _announcedVersion)
        {
            return;
        }
        _announcedVersion = updater.LatestVersion;
        ShowToast(ToastKind.Info, string.Format(state == EV2pnUpdateState.Ready ? L.UpdToastReady : L.UpdToastAvailable, updater.LatestVersion));
    }

    private void RefreshUpdateBadge()
    {
        var state = V2pnAppUpdater.Instance.State;
        var app = state is EV2pnUpdateState.Available or EV2pnUpdateState.Downloading or EV2pnUpdateState.Ready;
        btnNewUpdate.IsVisible = app || ViewModel?.BlNewUpdate == true;
        // Without an app update the badge is about the cores (Xray, sing-box); "new version" read as V2pn's.
        txtNewUpdate.Text = state == EV2pnUpdateState.Ready ? L.UpdBadgeReady : app ? L.UpdBadgeAvailable : L.CoreUpdatesBadge;
        // A narrow title bar has room for the dot only; its words move to the tooltip.
        var stacked = _tier == BodyTier.Stacked;
        txtNewUpdate.IsVisible = !stacked;
        ToolTip.SetTip(btnNewUpdate, stacked ? txtNewUpdate.Text : null);
        Avalonia.Automation.AutomationProperties.SetName(btnNewUpdate, txtNewUpdate.Text);
    }

    private void ShowToast(ToastKind kind, string message)
    {
        ShowNotice(Toasts.Message(kind, null, message), Toasts.TypeOf(kind), TimeSpan.FromSeconds(6));
    }

    // The normal bounds and the maximized flag: a maximized window reopens maximized, and restores to its
    // own size instead of a screen-sized normal one.
    private void StorageUI(string? key = null)
    {
        var normal = WindowState == WindowState.Normal;
        var size = _normalSize ?? new Size(Width, Height);
        var position = _normalPosition ?? (normal && IsVisible ? (PixelPoint?)Position : null);
        var maximized = WindowState == WindowState.Maximized || (WindowState == WindowState.Minimized && _restoreState == WindowState.Maximized);
        ConfigTextHandler.SaveWindowPlacement(key ?? GetType().Name, size.Width, size.Height, position?.X, position?.Y, maximized);
    }

    private void UpdateNoticeMargin()
    {
        if (_manager == null)
        {
            return;
        }
        var card = contentStatusBarView.IsVisible && panelStage.IsVisible && contentStatusBarView.Bounds.Height > 0;
        _manager.Margin = new Thickness(0, 0, 20, card ? contentStatusBarView.Bounds.Height + 32 : 20);
    }

    // In a short window the pin's usual height falls under the mode switch. Keep it in the band of map
    // left open between the header and the status card; a tall window keeps the usual height.
    private void UpdateMapFocus()
    {
        var h = mapView.Bounds.Height;
        if (h <= 0)
        {
            return;
        }
        const double clearance = 40;
        var top = panelHeader.IsVisible ? panelHeader.Bounds.Bottom : 0;
        var bottom = contentStatusBarView.IsVisible && contentStatusBarView.Bounds.Height > 0 ? contentStatusBarView.Bounds.Top : h;
        if (passHost.Content != null && passHost.IsVisible && passHost.Bounds.Height > 0)
        {
            bottom = Math.Min(bottom, passHost.Bounds.Top);
        }
        var y = h * MapView.DefaultFocusY;
        y = top + clearance <= bottom - clearance
            ? Math.Clamp(y, top + clearance, bottom - clearance)
            : (top + bottom) / 2;
        mapView.FocusY = y / h;
    }

    // The website, a place to report a problem and the project V2pn is built on. The cores' own
    // websites (v2rayN listed a dozen) mean nothing to most people.
    private void AddHelpMenuItem()
    {
        var repo = $"{Global.GithubUrl}/{V2pnInfo.Repo}";
        menuHelp.Items.Add(new MenuItem { Classes = { "menuheader" }, Header = string.Format(L.HelpVersion, V2pnInfo.DisplayVersion), IsEnabled = false });
        menuHelp.Items.Add(HelpLink(L.HelpSite, repo, "SemiIconGlobeStroke"));
        menuHelp.Items.Add(HelpLink(L.HelpReport, $"{repo}/issues", "SemiIconComment"));
        var diagGlyph = new PathIcon { Classes = { "mi" } };
        diagGlyph.Bind(PathIcon.DataProperty, diagGlyph.GetResourceObservable("SemiIconCopy"));
        var diag = new MenuItem { Header = L.DiagMenu, Icon = diagGlyph };
        diag.Click += async (_, _) => await ShowDiagnosticsAsync();
        menuHelp.Items.Add(diag);
        menuHelp.Items.Add(new Separator());
        menuHelp.Items.Add(HelpLink(L.HelpBasedOn, $"{Global.GithubUrl}/2dust/v2rayN", "SemiIconGithubLogo"));
    }

    private MenuItem HelpLink(string header, string url, string icon)
    {
        var glyph = new PathIcon { Classes = { "mi" } };
        glyph.Bind(PathIcon.DataProperty, glyph.GetResourceObservable(icon));
        var item = new MenuItem { Header = header, Tag = url, Icon = glyph };
        ToolTip.SetTip(item, url);
        item.Click += MenuItem_Click;
        return item;
    }

    private void MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            ProcUtils.ProcessStart(item.Tag?.ToString());
        }
    }

    #endregion UI
}
