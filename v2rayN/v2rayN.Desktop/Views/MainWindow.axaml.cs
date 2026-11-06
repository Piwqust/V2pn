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

    // The proxy mode to restore on Connect after a Disconnect (On or PAC).
    private int _lastProtectedMode = (int)ESysProxyType.ForcedChange;
    private string? _activeIndexId;

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3, Position = NotificationPosition.TopRight };
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
        btnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        btnMaximize.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        btnCloseWindow.Click += (_, _) => Close();
        btnPageBack.Click += (_, _) => ClosePage();
        menuPageLog.Click += (_, _) => ShowPage(L.MenuLog, _msgView);
        menuPageTable.Click += (_, _) => ShowPage(L.MenuTable, _profilesView);
        menuPageProxies.Click += (_, _) => ShowPage(L.MenuClashProxies, _clashProxiesView);
        menuPageConnections.Click += (_, _) => ShowPage(L.MenuClashConnections, _clashConnectionsView);
        lstTabs.SelectionChanged += LstTabs_SelectionChanged;
        txtSearch.TextChanged += (_, _) =>
        {
            if (_sidebar != null)
            {
                _sidebar.Search = txtSearch.Text ?? string.Empty;
            }
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

            // Connection header, map and statistics follow the proxy mode, TUN and the running server.
            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.SystemProxySelected,
                    v => v.ViewModel.StatusBarViewModel.EnableTun,
                    v => v.ViewModel.StatusBarViewModel.RunningServerDisplay)
                .Throttle(TimeSpan.FromMilliseconds(80))
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .SubscribeAsync(async _ => await RefreshConnectionAsync())
                .DisposeWith(disposables);
            profiles.ProfileItems.CollectionChanged += (_, _) => _ = RefreshConnectionAsync();

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
            if (lstProxyMode.Items.Count > 3)
            {
                lstProxyMode.Items.RemoveAt(3);
            }
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
    }

    private void ShowPage(string title, Control? page)
    {
        if (page == null)
        {
            return;
        }
        txtPageTitle.Text = title;
        pageHost.Content = page;
        pageOverlay.IsVisible = true;
    }

    private void ClosePage()
    {
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
        _activeIndexId = item?.IndexId;

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

        var proxyMode = (ESysProxyType)(ViewModel?.StatusBarViewModel.SystemProxySelected ?? 0);
        dotRailProxy.IsVisible = proxyMode is ESysProxyType.ForcedChange or ESysProxyType.Pac;

        mapView.Focus(country?.Code ?? code, state);
        _statusView?.SetState(state);
        _statusView?.SetServer(item, FindModel(item?.IndexId)?.Delay ?? await StoredDelayAsync(item?.IndexId));
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

    private void BtnConnect_Click(object? sender, RoutedEventArgs e) => ToggleConnection(CurrentState() == ProtectionState.Unprotected);

    private void ToggleConnection(bool connect)
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
            status.SystemProxySelected = _lastProtectedMode;
            return;
        }

        if ((ESysProxyType)status.SystemProxySelected is ESysProxyType.ForcedChange or ESysProxyType.Pac)
        {
            _lastProtectedMode = status.SystemProxySelected;
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

    private async void SideServer_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SideServerRow row })
        {
            await ConnectToAsync(row.Model.IndexId);
        }
    }

    private void SideCountry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SideCountryRow row })
        {
            _sidebar?.ToggleCountry(row.Code);
        }
    }

    private void SideSub_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SideSubRow row } && _sidebar != null)
        {
            _sidebar.OpenedSub = row.Sub;
        }
    }

    private async void SideAction_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SideActionRow row } || _sidebar == null || ViewModel == null)
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

    private void SideServerMenu_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: SideServerRow row } button || ViewModel == null)
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

        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Items.Add(Item(L.Connect, async () => await ConnectToAsync(model.IndexId)));
        flyout.Items.Add(Item(model.IsFavorite ? L.Unfavorite : L.Favorite, async () => await profiles.ToggleFavoriteAsync(model)));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(ResUI.menuRealPingServer, async () => await profiles.PingProfileAsync(model)));
        flyout.Items.Add(Item(ResUI.menuTcpingServer, async () => { Select(); await profiles.ServerSpeedtest(ESpeedActionType.Tcping); }));
        flyout.Items.Add(Item(ResUI.menuSpeedServer, async () => { Select(); await profiles.ServerSpeedtest(ESpeedActionType.Speedtest); }));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(ResUI.menuEditServer, async () => { Select(); await profiles.EditServerAsync(); }));
        flyout.Items.Add(Item(ResUI.menuShareServer, async () => { Select(); await profiles.ShareServerAsync(); }));
        flyout.Items.Add(Item(ResUI.menuExport2ShareUrl, async () => { Select(); await profiles.Export2ShareUrlAsync(false); }));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(ResUI.menuRemoveServer, async () => { Select(); await profiles.RemoveServerAsync(); }));
        flyout.ShowAt(button);
    }

    private void SideSubMenu_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: SideSubRow row } button || ViewModel == null || _sidebar == null)
        {
            return;
        }
        var vm = ViewModel;

        // Subscription commands act on the current group, so open the subscription first.
        void Open() => _sidebar.OpenedSub = row.Sub;

        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Items.Add(Item(ResUI.menuSubGroupUpdate, () => { Open(); vm.SubGroupUpdateCmd.Execute().Subscribe(); return Task.CompletedTask; }));
        flyout.Items.Add(Item(ResUI.menuSubGroupUpdateViaProxy, () => { Open(); vm.SubGroupUpdateViaProxyCmd.Execute().Subscribe(); return Task.CompletedTask; }));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(ResUI.menuSubEdit, () => { Open(); vm.ProfilesViewModel.EditSubCmd.Execute().Subscribe(); return Task.CompletedTask; }));
        flyout.Items.Add(Item(ResUI.menuSubDelete, () => { Open(); vm.ProfilesViewModel.DeleteSubCmd.Execute().Subscribe(); return Task.CompletedTask; }));
        flyout.ShowAt(button);
    }

    private static MenuItem Item(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
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
        _manager?.Show(new Avalonia.Controls.Notifications.Notification(null, content, NotificationType.Information));
        await Task.CompletedTask;
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
