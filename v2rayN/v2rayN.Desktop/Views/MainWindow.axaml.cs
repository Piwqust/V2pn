using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
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
    private CheckUpdateView? _checkUpdateView;
    private BackupAndRestoreView? _backupAndRestoreView;
    private bool _blCloseByUser = false;

    private StatusBarView? _statusView;
    private SidebarViewModel? _sidebar;
    private MsgView? _msgView;
    private ProfilesView? _profilesView;
    private ClashConnectionsView? _clashConnectionsView;
    private ClashProxiesView? _clashProxiesView;

    private string? _activeIndexId;

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        // Above the status card and clear of the feature rail, over the empty part of the map.
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this))
        {
            MaxItems = 3,
            Position = NotificationPosition.BottomRight,
            Margin = new Thickness(0, 0, 20, 196),
        };
        _pingTimer = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) => RefreshActivePing());

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
        btnWelcomeSub.Click += (_, _) => ViewModel?.ProfilesViewModel.AddSubCmd.Execute().Subscribe();
        btnWelcomeQr.Click += (_, _) => ViewModel?.AddServerViaScanCmd.Execute().Subscribe();
        btnWelcomeQr.IsVisible = Utils.IsWindows();
        menuRegionalPresetsRussia.Icon = new Image { Source = V2pnConverters.Flag("RU"), Width = 16, Height = 12 };
        menuRegionalPresetsIran.Icon = new Image { Source = V2pnConverters.Flag("IR"), Width = 16, Height = 12 };
        btnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        btnMaximize.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        btnCloseWindow.Click += (_, _) => Close();
        btnPageBack.Click += (_, _) => ClosePage();
        menuPageLog.Click += (_, _) => ShowPage(L.MenuLog, _msgView);
        menuPageTable.Click += (_, _) => ShowPage(L.MenuTable, _profilesView);
        menuPageProxies.Click += (_, _) => ShowPage(L.MenuClashProxies, _clashProxiesView);
        menuPageConnections.Click += (_, _) => ShowPage(L.MenuClashConnections, _clashConnectionsView);
        lstTabs.SelectionChanged += LstTabs_SelectionChanged;
        lstRows.KeyDown += LstRows_KeyDown;
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

        conTheme.Content ??= new ThemeSettingView();

        this.WhenActivated(disposables =>
        {
            var vm = ViewModel!;
            var profiles = vm.ProfilesViewModel;
            var status = vm.StatusBarViewModel;

            //sub
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.menuSubSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateCmd, v => v.menuSubGroupUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateViaProxyCmd, v => v.menuSubGroupUpdateViaProxy).DisposeWith(disposables);

            //setting
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuOptionSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.menuRoutingSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.btnRoutingSettings).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.menuDNSSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.btnRailDns).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FullConfigTemplateCmd, v => v.menuFullConfigTemplate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GlobalHotkeySettingCmd, v => v.menuGlobalHotkeySetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RebootAsAdminCmd, v => v.menuRebootAsAdmin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ClearServerStatisticsCmd, v => v.menuClearServerStatistics).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenTheFileLocationCmd, v => v.menuOpenTheFileLocation).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetDefaultCmd, v => v.menuRegionalPresetsDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetRussiaCmd, v => v.menuRegionalPresetsRussia).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetIranCmd, v => v.menuRegionalPresetsIran).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlNewUpdate, v => v.btnNewUpdate.IsVisible).DisposeWith(disposables);

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
            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.AvailabilityDelay)
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

        if (Utils.IsWindows())
        {
            Title = $"{L.AppName} {Utils.GetVersionInfo()} - {(Utils.IsAdministrator() ? ResUI.RunAsAdmin : ResUI.NotRunAsAdmin)}";

            if (!Design.IsDesignMode)
            {
                ThreadPool.RegisterWaitForSingleObject(Program.ProgramStarted, OnProgramStarted, null, -1, false);
                HotkeyManager.Instance.Init(_config, OnHotkeyHandler);
            }
        }
        else
        {
            Title = $"{L.AppName} {Utils.GetVersionInfo()}";
            menuAddServerViaScan.IsVisible = false;
            menuGlobalHotkeySetting.IsVisible = false;
            menuRebootAsAdmin.IsVisible = false;
            menuSettingsSetUWP.IsVisible = false;
        }

        if (_config.UiItem.AutoHideStartup && Utils.IsWindows())
        {
            WindowState = WindowState.Minimized;
        }

        AddHelpMenuItem();
    }

    #region Views

    private void BuildViews()
    {
        var vm = ViewModel!;
        _statusView ??= new StatusBarView { ViewModel = vm.StatusBarViewModel };
        contentStatusBarView.Content = _statusView;

        _msgView ??= new MsgView { DataContext = vm.MsgViewModel };
        _profilesView ??= new ProfilesView { DataContext = vm.ProfilesViewModel };
        _profilesView.MainViewModel = vm;
        _clashProxiesView ??= new ClashProxiesView { DataContext = vm.ClashProxiesViewModel };
        _clashConnectionsView ??= new ClashConnectionsView { DataContext = vm.ClashConnectionsViewModel };

        _sidebar ??= new SidebarViewModel(vm.ProfilesViewModel);
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
        lstTabs.SelectedIndex = (int)SidebarTab.Countries;
        var row = _sidebar.RevealCountry(code);
        if (row != null)
        {
            Dispatcher.UIThread.Post(() => lstRows.ScrollIntoView(row), DispatcherPriority.Background);
        }
    }

    private void ShowPage(string title, Control? page)
    {
        if (page == null)
        {
            return;
        }
        txtPageTitle.Text = title;
        pageHost.Content = page;
        var opening = !pageOverlay.IsVisible;
        pageOverlay.IsVisible = true;
        if (opening)
        {
            _ = Motion.Enter(pageOverlay, dx: 18, dy: 0, ms: 240);
        }
    }

    private async void ClosePage()
    {
        if (!pageOverlay.IsVisible)
        {
            return;
        }
        await Motion.Exit(pageOverlay, dx: 14, ms: 160);
        pageOverlay.IsVisible = false;
        pageHost.Content = null;
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
                await DialogHost.Show(new QrcodeView(interaction.Input));
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
        if (status == null)
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
    private ProtectionState _shownState;

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
        var state = CurrentState();
        var item = await AppManager.Instance.GetProfileItem(_config.IndexId);
        // Motion only for changes the user caused after the first paint, never for the initial state.
        var serverChanged = _shownOnce && item?.IndexId != _activeIndexId;
        var stateChanged = _shownOnce && state != _shownState;
        _activeIndexId = item?.IndexId;
        _shownState = state;

        var code = CountryResolver.Resolve(item?.Remarks);
        var country = WorldMap.Instance.Get(code);
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
            var name = CountryResolver.StripFlag(item.Remarks);
            var region = string.Equals(code, "EU", StringComparison.OrdinalIgnoreCase) ? L.Europe : null;
            txtHeaderTitle.Text = country?.Name ?? region ?? name;
            var transport = string.Join(" · ", new[] { item.ConfigType.ToString(), item.StreamSecurity }.Where(t => t.IsNotEmpty()));
            txtHeaderSubtitle.Text = country != null || region != null ? $"{name}  ·  {transport}" : transport;
        }

        Remember(item?.IndexId, state);
        // No servers at all: the welcome card replaces Connect and the mode bar.
        var empty = (ViewModel?.ProfilesViewModel.AllServersCount ?? 0) == 0 && item == null;
        if (empty && !cardWelcome.IsVisible && _shownOnce)
        {
            _ = Motion.Enter(cardWelcome, dy: 10, ms: 260, scale: 0.97);
        }
        cardWelcome.IsVisible = empty;
        btnConnect.IsVisible = !empty;
        barConnection.IsVisible = !empty;
        RefreshStatePill();
        var unprotected = state == ProtectionState.Unprotected;
        btnConnect.Content = unprotected ? L.Connect : L.Disconnect;
        btnConnect.Classes.Set("accent", unprotected);
        btnConnect.Classes.Set("disconnect", !unprotected);
        btnConnect.IsEnabled = item != null || !unprotected;

        var tint = state switch
        {
            ProtectionState.Protected => Color.Parse("#3DD68C"),
            ProtectionState.LocalOnly => Color.Parse("#F2B84B"),
            _ => Color.Parse("#F0625A"),
        };
        stateTint.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x38, tint.R, tint.G, tint.B), 0),
                new GradientStop(Color.FromArgb(0x00, tint.R, tint.G, tint.B), 1),
            },
        };

        SyncModeSelection();

        if (serverChanged)
        {
            // The new server settles into the header: flag and name first, the details a beat later.
            _ = Motion.Enter(borderHeaderFlag, dy: 8, ms: 240);
            _ = Motion.Enter(txtHeaderTitle, dy: 8, ms: 240);
            _ = DelayedEnter(txtHeaderSubtitle, 40);
        }
        if (stateChanged)
        {
            _ = Motion.Enter(pillState, dy: 0, ms: 220, scale: 0.9);
            _ = Motion.Enter(stateTint, dy: 0, ms: 420);
        }
        _shownOnce = true;

        mapView.Focus(country?.Code ?? code, state);
        _statusView?.SetState(state);
        _statusView?.SetServer(item, FindModel(item?.IndexId)?.Delay ?? await StoredDelayAsync(item?.IndexId));
    }

    // The pill combines the proxy mode with the last connection check, so "Protected" means the check passed.
    private void RefreshStatePill()
    {
        var state = CurrentState();
        var delay = ViewModel?.StatusBarViewModel.AvailabilityDelay ?? 0;
        var checking = state != ProtectionState.Unprotected && delay == -2;
        var failed = state != ProtectionState.Unprotected && delay == -1;

        string kind;
        string text;
        if (state == ProtectionState.Unprotected)
        {
            (kind, text) = ("unsafe", L.Unprotected);
        }
        else if (checking)
        {
            (kind, text) = ("checking", L.StateChecking);
        }
        else if (failed)
        {
            (kind, text) = ("warn", L.StateNoResponse);
        }
        else if (state == ProtectionState.LocalOnly)
        {
            (kind, text) = ("warn", L.LocalOnly);
        }
        else
        {
            (kind, text) = ("safe", delay > 0 ? $"{L.Protected} · {L.Ms(delay)}" : L.Protected);
        }

        foreach (var c in new[] { "safe", "unsafe", "warn", "checking" })
        {
            pillState.Classes.Set(c, c == kind);
        }
        txtStatePill.Text = text.ToUpper(CultureInfo.CurrentUICulture);
        spinState.IsVisible = checking;
        dotState.IsVisible = !checking;
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
        var item = await AppManager.Instance.GetProfileItem(_activeIndexId);
        _statusView.SetServer(item, FindModel(_activeIndexId)?.Delay ?? await StoredDelayAsync(_activeIndexId));
    }

    private void BtnConnect_Click(object? sender, RoutedEventArgs e)
    {
        var connect = CurrentState() == ProtectionState.Unprotected;
        ToggleConnection(connect);
        if (connect)
        {
            // Turning the proxy on proves nothing by itself; check that traffic actually goes through.
            _ = ViewModel?.StatusBarViewModel.TestServerAvailability();
        }
    }

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
        if (tun && !await EnsureTunAllowedAsync(connectAfterRestart: connected))
        {
            SyncModeSelection();
            return;
        }

        _config.UiItem.V2pnPreferTun = tun;
        await ConfigHandler.SaveConfig(_config);
        if (connected && tun != status.EnableTun)
        {
            ApplyMode(status, tun);
            if (!tun)
            {
                // Switching to the proxy doesn't reload the core, so nothing re-checks the connection by itself.
                _ = status.TestServerAvailability();
            }
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
        if (status.AllowEnableTun() || !Utils.IsWindows())
        {
            // Linux and macOS ask for the password when TUN turns on.
            return true;
        }
        if (await UI.ShowYesNo(L.VpnRestartAsAdmin) != ButtonResult.Yes)
        {
            return false;
        }
        _config.UiItem.V2pnPreferTun = true;
        if (connectAfterRestart)
        {
            _config.TunModeItem.EnableTun = true;
            _config.SystemProxyItem.SysProxyType = ESysProxyType.ForcedClear;
        }
        await ConfigHandler.SaveConfig(_config);
        await AppManager.Instance.RebootAsAdmin();
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

    private void RefreshRouteUi()
    {
        var status = ViewModel?.StatusBarViewModel;
        if (status == null)
        {
            return;
        }
        var active = status.SelectedRouting;
        var kind = RoutingPresets.KindOf(active?.Id);
        txtRoute.Text = kind is { } k ? RoutingPresets.Title(k) : active?.Remarks ?? L.RouteTitle;
        btnRouteRussia.Classes.Set("active", kind == RoutingPresetKind.RussiaDirect);
        btnRouteBlocked.Classes.Set("active", kind == RoutingPresetKind.BlockedOnly);
        btnRouteAll.Classes.Set("active", kind == RoutingPresetKind.All);

        var custom = status.RoutingItems.Where(t => !RoutingPresets.IsPreset(t.Id)).ToList();
        _routeSync = true;
        lstCustomRoutes.ItemsSource = custom;
        lstCustomRoutes.SelectedItem = kind == null ? custom.FirstOrDefault(t => t.Id == active?.Id) : null;
        _routeSync = false;
        panelCustomRoutes.IsVisible = custom.Count > 0;
    }

    private void ShowRouteBusy(string? text)
    {
        borderRouteBusy.IsVisible = text != null;
        txtRouteBusy.Text = text;
    }

    private async Task SelectPresetAsync(RoutingPresetKind kind)
    {
        var vm = ViewModel;
        if (_routeBusy || vm == null)
        {
            return;
        }
        _routeBusy = true;
        try
        {
            var sourcesChanged = RoutingPresets.NeedsRussianSources(_config, kind);
            if (sourcesChanged)
            {
                ShowRouteBusy(L.RouteDownloading);
                await vm.UseRussianSourcesAsync();
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
            ShowRouteBusy(null);
            _routeBusy = false;
        }
    }

    /// <summary>
    /// Activates a routing item directly instead of through StatusBarViewModel.SelectedRouting: setting
    /// that property twice in a row (refresh, then the new choice) races two async handlers.
    /// </summary>
    private async Task ActivateRoutingAsync(string id, bool sourcesChanged)
    {
        var status = ViewModel?.StatusBarViewModel;
        var item = await AppManager.Instance.GetRoutingItem(id);
        if (status == null || item == null)
        {
            return;
        }
        var changed = await ConfigHandler.SetDefaultRouting(_config, item) == 0;
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
        await ConfigHandler.SaveConfig(_config);
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
        if (_sidebar == null || lstTabs.SelectedIndex < 0)
        {
            return;
        }
        _sidebar.OpenedSub = null;
        _sidebar.Tab = (SidebarTab)lstTabs.SelectedIndex;
    }

    private void SideServer_Click(object? sender, RoutedEventArgs e) => ActivateRow((sender as Control)?.DataContext as SideRow);

    private void SideCountry_Click(object? sender, RoutedEventArgs e) => ActivateRow((sender as Control)?.DataContext as SideRow);

    private void SideSub_Click(object? sender, RoutedEventArgs e) => ActivateRow((sender as Control)?.DataContext as SideRow);

    private void SideAction_Click(object? sender, RoutedEventArgs e) => ActivateRow((sender as Control)?.DataContext as SideRow);

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
        if (e.Key is Key.Enter or Key.Space && lstRows.SelectedItem is SideRow row)
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
    }

    // The menu key or Shift+F10 on a focused row (the row buttons themselves are mouse targets).
    private void LstRows_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Handled || lstRows.SelectedItem is not SideRow row || lstRows.ContainerFromItem(row) is not Control container)
        {
            return;
        }
        e.Handled = true;
        switch (row)
        {
            case SideServerRow server:
                ShowServerMenu(container, server, PlacementMode.BottomEdgeAlignedLeft);
                break;

            case SideSubRow sub:
                ShowSubMenu(container, sub, PlacementMode.BottomEdgeAlignedLeft);
                break;
        }
    }

    private void TxtSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && _sidebar?.Rows.FirstOrDefault(r => r is SideServerRow or SideCountryRow or SideSubRow or SideActionRow) is { } first)
        {
            e.Handled = true;
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
            case SideActionKind.Fastest:
                await ConnectToAsync(row.TargetId);
                break;

            case SideActionKind.Back:
                _sidebar.OpenedSub = null;
                break;

            case SideActionKind.AddSubscription:
                ViewModel.ProfilesViewModel.AddSubCmd.Execute().Subscribe();
                break;

            case SideActionKind.UpdateAll:
                ViewModel.SubUpdateCmd.Execute().Subscribe();
                break;
        }
    }

    private async void SidePing_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_sidebar != null)
        {
            await _sidebar.TogglePingAsync();
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
        if (sender is Control { DataContext: SideCountryRow row } && row.BestId.IsNotEmpty())
        {
            await ConnectToAsync(row.BestId);
        }
    }

    private async void SideSubUpdate_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control { DataContext: SideSubRow row } || ViewModel == null || row.Sub.Id.IsNullOrEmpty() || row.Busy.IsBusy)
        {
            return;
        }
        row.Busy.IsBusy = true;
        try
        {
            await ViewModel.UpdateSubscriptionProcess(row.Sub.Id, false);
        }
        finally
        {
            row.Busy.IsBusy = false;
        }
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

    private void UpdateSubscription(string? subId)
    {
        if (ViewModel == null || subId.IsNullOrEmpty())
        {
            return;
        }
        _ = ViewModel.UpdateSubscriptionProcess(subId, false);
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
        var connect = Item(L.ConnectFastestIn, async () => await ConnectToAsync(row.BestId), "SemiIconBolt");
        connect.IsEnabled = row.BestId.IsNotEmpty();
        flyout.Items.Add(connect);
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
        flyout.Items.Add(Item(model.IsFavorite ? L.Unfavorite : L.Favorite, async () => await profiles.ToggleFavoriteAsync(model), model.IsFavorite ? "SemiIconStar" : "SemiIconStarStroked"));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(ResUI.menuRealPingServer, async () => await profiles.PingProfileAsync(model), "SemiIconBolt"));
        flyout.Items.Add(Item(ResUI.menuTcpingServer, async () => { Select(); await profiles.ServerSpeedtest(ESpeedActionType.Tcping); }, "SemiIconPulse"));
        flyout.Items.Add(Item(ResUI.menuSpeedServer, async () => { Select(); await profiles.ServerSpeedtest(ESpeedActionType.Speedtest); }, "SemiIconStopwatchStroked"));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(ResUI.menuEditServer, async () => { Select(); await profiles.EditServerAsync(); }, "SemiIconEditStroked"));
        flyout.Items.Add(Item(ResUI.menuShareServer, async () => { Select(); await profiles.ShareServerAsync(); }, "SemiIconQrCode"));
        flyout.Items.Add(Item(ResUI.menuExport2ShareUrl, async () => { Select(); await profiles.Export2ShareUrlAsync(false); }, "SemiIconCopyStroked"));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(ResUI.menuRemoveServer, async () => { Select(); await profiles.RemoveServerAsync(); }, "SemiIconDeleteStroked", danger: true));
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
        flyout.Items.Add(Item(ResUI.menuSubGroupUpdate, () => vm.UpdateSubscriptionProcess(row.Sub.Id ?? string.Empty, false), "SemiIconRefresh"));
        flyout.Items.Add(Item(ResUI.menuSubGroupUpdateViaProxy, () => vm.UpdateSubscriptionProcess(row.Sub.Id ?? string.Empty, true), "SemiIconRefresh2"));
        flyout.Items.Add(Item(L.TestPing, async () =>
        {
            var servers = await AppManager.Instance.ProfileModels(row.Sub.Id ?? string.Empty, string.Empty);
            var ids = servers?.Select(s => s.IndexId).ToHashSet() ?? [];
            var models = vm.ProfilesViewModel.ProfileItems.Where(m => ids.Contains(m.IndexId)).ToList();
            await vm.ProfilesViewModel.PingProfilesAsync(models);
        }, "SemiIconPulse"));
        flyout.Items.Add(Item(L.RemoveDead, async () => await RemoveDeadAsync(row), "SemiIconMinusCircleStroked"));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(ResUI.menuSubEdit, () => { Open(); vm.ProfilesViewModel.EditSubCmd.Execute().Subscribe(); return Task.CompletedTask; }, "SemiIconEditStroked"));
        flyout.Items.Add(Item(ResUI.menuSubDelete, () => { Open(); vm.ProfilesViewModel.DeleteSubCmd.Execute().Subscribe(); return Task.CompletedTask; }, "SemiIconDeleteStroked", danger: true));
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
        var servers = await AppManager.Instance.ProfileModels(subId, string.Empty) ?? [];
        if (!servers.Any(s => failed.Contains(s.IndexId)))
        {
            NoticeManager.Instance.Enqueue(L.RemoveDeadNone);
            return;
        }
        if (await UI.ShowYesNo(string.Format(L.RemoveDeadConfirm, row.Sub.Remarks)) != ButtonResult.Yes)
        {
            return;
        }
        var count = await ConfigHandler.RemoveInvalidServerResult(_config, subId);
        await profiles.RefreshServersBiz();
        await profiles.RefreshNavCounts();
        NoticeManager.Instance.Enqueue(string.Format(ResUI.RemoveInvalidServerResultTip, Math.Max(count, 0)));
    }

    private static MenuItem Item(string header, Func<Task> action, string? icon = null, bool danger = false)
    {
        var item = new MenuItem { Header = header };
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
                ShowHideWindow(true),
            DispatcherPriority.Default);
    }

    private async Task DelegateSnackMsg(string content)
    {
        if (_manager == null || content.IsNullOrEmpty())
        {
            return;
        }
        // v2rayN announces a started core with the server summary; show the server instead of its raw summary.
        var item = await AppManager.Instance.GetProfileItem(_config.IndexId);
        if (item != null && content == item.GetSummary())
        {
            _manager.Show(Toasts.Server(item), NotificationType.Success, TimeSpan.FromSeconds(4));
            return;
        }
        var kind = Toasts.Guess(content);
        _manager.Show(Toasts.Message(kind, null, content), Toasts.TypeOf(kind), TimeSpan.FromSeconds(kind == ToastKind.Error ? 8 : 5));
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
                ShowHideWindow(false);
                break;

            case WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown:
                await AppManager.Instance.AppExitAsync(false);
                break;
        }

        base.OnClosing(e);
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            switch (e.Key)
            {
                case Key.V when e.Source is not TextBox:
                    await AddServerViaClipboardAsync();
                    break;

                case Key.S:
                    await ScanScreenTaskAsync();
                    break;

                case Key.F:
                    txtSearch.Focus();
                    txtSearch.SelectAll();
                    e.Handled = true;
                    break;

                case Key.D1:
                case Key.D2:
                case Key.D3:
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
                ClosePage();
            }
            else if (txtSearch.Text.IsNotEmpty())
            {
                txtSearch.Text = string.Empty;
            }
        }
        else if (e.Key == Key.F5)
        {
            ViewModel?.Reload();
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

    public async Task AddServerViaClipboardAsync()
    {
        var clipboardData = await AvaUtils.GetClipboardData(this);
        if (clipboardData.IsNotEmpty() && ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
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
        DialogHost.Show(_checkUpdateView);

        AppEvents.HasUpdateNotified.Publish(false);
    }

    private void MenuBackupAndRestore_Click(object? sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        DialogHost.Show(_backupAndRestoreView);
    }

    private async void MenuClose_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (await UI.ShowYesNo(ResUI.menuExitTips) != ButtonResult.Yes)
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
                    : !AppManager.Instance.ShowInTaskbar);
        if (bl)
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
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

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);
        if (_config.UiItem.AutoHideStartup)
        {
            ShowHideWindow(false);
        }
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);
    }

    private void AddHelpMenuItem()
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
        foreach (var it in coreInfo
            .Where(t => t.CoreType is not ECoreType.v2fly
                        and not ECoreType.hysteria))
        {
            var item = new MenuItem()
            {
                Tag = it.Url?.Replace(@"/releases", ""),
                Header = string.Format(ResUI.menuWebsiteItem, it.CoreType.ToString().Replace("_", " ")).UpperFirstChar()
            };
            item.Click += MenuItem_Click;
            menuHelp.Items.Add(item);
        }
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
