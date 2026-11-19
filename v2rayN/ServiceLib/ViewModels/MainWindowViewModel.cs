namespace ServiceLib.ViewModels;

public partial class MainWindowViewModel : MyReactiveObject
{
    public Interaction<RxVoid, string?> ReadTextFromClipboardInteraction { get; } = new();
    public Interaction<RxVoid, byte[]?> ScanScreenInteraction { get; } = new();
    public Interaction<RxVoid, string?> BrowseImageFileInteraction { get; } = new();
    public Interaction<bool?, RxVoid> ShowHideWindowInteraction { get; } = new();

    public bool DesignMode { get; set; }

    public ProfilesViewModel ProfilesViewModel { get; } = new();
    public MsgViewModel MsgViewModel { get; } = new();
    public ClashProxiesViewModel ClashProxiesViewModel { get; } = new();
    public ClashConnectionsViewModel ClashConnectionsViewModel { get; } = new();
    public CheckUpdateViewModel CheckUpdateViewModel { get; } = new();
    public BackupAndRestoreViewModel BackupAndRestoreViewModel { get; } = new();
    public StatusBarViewModel StatusBarViewModel { get; } = StatusBarViewModel.Instance;

    #region Menu

    //servers
    public ReactiveCommand<RxVoid, RxVoid> AddVmessServerCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> AddVlessServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddShadowsocksServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddSocksServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddHttpServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddTrojanServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddHysteria2ServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddTuicServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddWireguardServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddAnytlsServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddNaiveServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddMasqueServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddCustomServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddCustomOutboundServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddPolicyGroupServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddProxyChainServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddServerViaClipboardCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddServerViaScanCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddServerViaImageCmd { get; }

    //Subscription
    public ReactiveCommand<RxVoid, RxVoid> SubSettingCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> SubUpdateCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubUpdateViaProxyCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubGroupUpdateCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubGroupUpdateViaProxyCmd { get; }

    //Setting
    public ReactiveCommand<RxVoid, RxVoid> OptionSettingCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> RoutingSettingCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> DNSSettingCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> FullConfigTemplateCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> GlobalHotkeySettingCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> RebootAsAdminCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ClearServerStatisticsCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> OpenTheFileLocationCmd { get; }

    //Presets
    public ReactiveCommand<RxVoid, RxVoid> RegionalPresetDefaultCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> RegionalPresetRussiaCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> RegionalPresetIranCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> ReloadCmd { get; }

    [Reactive]
    public partial bool BlReloadEnabled { get; set; }

    [Reactive]
    public partial bool ShowClashUI { get; set; }

    [Reactive]
    public partial int TabMainSelectedIndex { get; set; }

    [Reactive] public partial bool BlIsWindows { get; set; }

    [Reactive] public partial bool BlNewUpdate { get; set; }

    [Reactive] public partial EGirdOrientation MainGirdOrientation { get; set; }

    #endregion Menu

    #region Init

    public MainWindowViewModel()
    {
        _config = ConfigTextHandler.Config;
        BlIsWindows = Utils.IsWindows();
        MainGirdOrientation = _config.UiItem.MainGirdOrientation;

        #region WhenAnyValue && ReactiveCommand

        //servers
        AddVmessServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.VMess);
        });
        AddVlessServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.VLESS);
        });
        AddShadowsocksServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Shadowsocks);
        });
        AddSocksServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.SOCKS);
        });
        AddHttpServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.HTTP);
        });
        AddTrojanServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Trojan);
        });
        AddHysteria2ServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Hysteria2);
        });
        AddTuicServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.TUIC);
        });
        AddWireguardServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.WireGuard);
        });
        AddAnytlsServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Anytls);
        });
        AddNaiveServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Naive);
        });
        AddMasqueServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.MASQUE);
        });
        AddCustomServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Custom);
        });
        AddCustomOutboundServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Outbound);
        });
        AddPolicyGroupServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.PolicyGroup);
        });
        AddProxyChainServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.ProxyChain);
        });
        AddServerViaClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaClipboardAsync(null);
        });
        AddServerViaScanCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaScanAsync();
        });
        AddServerViaImageCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaImageAsync();
        });

        //Subscription
        SubSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SubSettingAsync();
        });

        SubUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess("", false);
        });
        SubUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess("", true);
        });
        SubGroupUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(_config.SubIndexId, false);
        });
        SubGroupUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(_config.SubIndexId, true);
        });

        //Setting
        OptionSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await OptionSettingAsync();
        });
        RoutingSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RoutingSettingAsync();
        });
        DNSSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await DNSSettingAsync();
        });
        FullConfigTemplateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await FullConfigTemplateAsync();
        });
        GlobalHotkeySettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            var globalHotkeySettingViewModel = new GlobalHotkeySettingViewModel();
            if (await AppManager.Instance.WindowDialog.ShowDialogAsync(globalHotkeySettingViewModel) == true)
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
            }
        });
        RebootAsAdminCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AppManager.Instance.RebootAsAdmin();
        });
        ClearServerStatisticsCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ClearServerStatistics();
        });
        OpenTheFileLocationCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await OpenTheFileLocation();
        });

        ReloadCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Reload();
        });

        RegionalPresetDefaultCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ApplyRegionalPreset(EPresetType.Default);
        });

        RegionalPresetRussiaCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ApplyRegionalPreset(EPresetType.Russia);
        });

        RegionalPresetIranCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ApplyRegionalPreset(EPresetType.Iran);
        });

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        AppEvents.AddServerViaClipboardRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async _ => await AddServerViaClipboardAsync(null));

        AppEvents.HasUpdateNotified
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(bl => BlNewUpdate = bl);

        // V2pn: the core died on its own. Not SubscribeAsync: the handler waits out the restart pause,
        // and a later exit must not queue behind it.
        V2pnCoreWatch.CoreExited
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(code => _ = OnCoreExitedAsync(code));

        #endregion AppEvents

        ProfilesViewModel.RefreshServersRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async _ => await RefreshServersDispatcherAsync());

        var vmReloadRequestedList = new List<IObservable<RxVoid>>
        {
            ProfilesViewModel.ReloadRequested.AsObservable(),
            StatusBarViewModel.ReloadRequested.AsObservable(),
            CheckUpdateViewModel.ReloadRequested.AsObservable(),
        };

        foreach (var reloadRequested in vmReloadRequestedList)
        {
            reloadRequested
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .SubscribeAsync(async _ => await Reload());
        }

        StatusBarViewModel.AddServerViaScanRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async _ => await AddServerViaScanAsync());

        StatusBarViewModel.AddServerViaClipboardRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async _ => await AddServerViaClipboardAsync(null));

        StatusBarViewModel.ShowHideWindowRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async blShow =>
            {
                await ShowHideWindowInteraction.HandleSafe(blShow);
            });

        StatusBarViewModel.SubscriptionsUpdateRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async blProxy => await UpdateSubscriptionProcess("", blProxy));

        Initialized = Init();
    }

    /// <summary>V2pn: the start is done (lists loaded, the core started or left off as it was); the provider's start defaults wait for it.</summary>
    public Task Initialized { get; } = Task.CompletedTask;

    private async Task Init()
    {
        AppManager.Instance.ShowInTaskbar = true;

        if (DesignMode)
        {
            return;
        }

        //await ConfigDbHandler.InitBuiltinRouting();
        await ConfigDbHandler.InitBuiltinDNS();
        await ConfigDbHandler.InitBuiltinFullConfigTemplate();
        await ProfileExManager.Instance.Init();
        await CoreManager.Instance.Init(_config, UpdateHandler);
        await CertPemManager.Instance.Init(_config);
        TaskManager.Instance.RegUpdateTask(_config, UpdateTaskHandler);

        if (_config.GuiItem.EnableStatistics || _config.GuiItem.DisplayRealTimeSpeed)
        {
            await StatisticsManager.Instance.Init(_config, UpdateStatisticsHandler);
        }
        await RefreshServersDispatcherAsync();

        if (V2pnBypass.On(_config))
        {
            await V2pnNetworkId.RefreshAsync();
        }
        await Reload();
        _networkWatch = new V2pnNetworkWatch(OnNetworkSettledAsync);
        _networkWatch.Start();
    }

    private V2pnNetworkWatch? _networkWatch;

    /// <summary>
    /// V2pn: the network changed or the computer woke. The server, mode and route stay as they are; the running
    /// connection is checked again. A connection the person switched off stays off. When nothing answers, the
    /// core is restarted once: its open connections may still be bound to the network that is gone.
    /// </summary>
    private async Task OnNetworkSettledAsync(string reason)
    {
        if (V2pnConnection.IsOff(_config) || !NetworkInterface.GetIsNetworkAvailable() || !CoreManager.Instance.IsCoreAlive)
        {
            return;
        }
        Logging.SaveLog($"V2pn: {reason}; checking the connection again");
        V2pnDiagnostics.NoteNetworkEvent(reason);
        if (await ApplyBypassForNetworkAsync())
        {
            return;
        }
        var done = new TaskCompletionSource<AvailabilityCheckResult?>();
        RxSchedulers.MainThreadScheduler.Schedule(async () =>
        {
            try
            {
                done.SetResult(await StatusBarViewModel.RecheckAvailabilityAsync());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        var result = await done.Task;
        if (result?.CanSearchForReplacement == true && !V2pnConnection.IsOff(_config) && CoreManager.Instance.IsCoreAlive)
        {
            Logging.SaveLog("V2pn: no answer after the network change; restarting the core once");
            RxSchedulers.MainThreadScheduler.Schedule(() => _ = Reload());
        }
    }

    #endregion Init

    #region Actions

    private async Task UpdateHandler(bool notify, string msg)
    {
        NoticeManager.Instance.SendMessage(msg);
        if (notify)
        {
            NoticeManager.Instance.Enqueue(msg);
        }
        await Task.CompletedTask;
    }

    private async Task UpdateTaskHandler(bool success, string msg)
    {
        NoticeManager.Instance.SendMessageEx(msg);
        if (success)
        {
            await RefreshServersDispatcherAsync();

            // V2pn: after a subscription update, restart the core only when the server in use changed.
            if (msg == ResUI.MsgUpdateSubscriptionEnd && await RunningConnectionUnchangedAsync())
            {
                Logging.SaveLog("V2pn: subscription updated, the server in use is unchanged; core left running");
            }
            // V2pn: the server in use got new settings (a rotated key, another SNI). The core must take them
            // whichever subscription the list shows; v2rayN reloads only for the subscription that is open.
            else if (msg == ResUI.MsgUpdateSubscriptionEnd && _runningConnectionKey != null && CoreManager.Instance.IsCoreAlive)
            {
                Logging.SaveLog("V2pn: subscription updated, the server in use changed; core reloaded");
                await Reload();
            }
            else if (_config.IndexId != _config.PreIndexId)
            {
                await Reload();
            }

            if (_config.UiItem.EnableAutoAdjustMainLvColWidth)
            {
                await ProfilesViewModel.AdjustMainLvColWidth();
            }
        }
    }

    private async Task UpdateStatisticsHandler(ServerSpeedItem update)
    {
        if (!AppManager.Instance.ShowInTaskbar)
        {
            return;
        }
        AppEvents.DispatcherStatisticsRequested.Publish(update);
        await Task.CompletedTask;
    }

    #endregion Actions

    #region Servers && Groups

    private readonly SemaphoreSlim _refreshServersSemaphore = new(1, 1);

    private async Task RefreshServers()
    {
        await ProfilesViewModel.RefreshServersBiz();
        await StatusBarViewModel.RefreshServersBiz();

        // await Task.Delay(200);
    }

    private async Task RefreshServersDispatcherAsync()
    {
        //await Observable.Start(async () => await RefreshServers(), RxSchedulers.MainThreadScheduler);
        await _refreshServersSemaphore.WaitAsync();
        try
        {
            await Signal.FromAsync(async () =>
            {
                await RefreshServers();
                return RxVoid.Default;
            })
                .SubscribeOn(RxSchedulers.MainThreadScheduler)
                .ToTask();
        }
        finally
        {
            _refreshServersSemaphore.Release();
        }
    }

    private async Task RefreshSubscriptions()
    {
        //await Observable.Start(async () => await ProfilesViewModel.RefreshSubscriptions(), RxSchedulers.MainThreadScheduler);
        await Signal.FromAsync(async () =>
            {
                await ProfilesViewModel.RefreshSubscriptions();
                return RxVoid.Default;
            })
            .SubscribeOn(RxSchedulers.MainThreadScheduler)
            .ToTask();
    }

    #endregion Servers && Groups

    #region Add Servers

    public async Task AddServerAsync(EConfigType eConfigType)
    {
        ProfileItem item = new()
        {
            Subid = _config.SubIndexId,
            ConfigType = eConfigType,
            IsSub = false,
        };

        bool? ret = false;
        if (eConfigType is EConfigType.Custom or EConfigType.Outbound)
        {
            var addServer2ViewModel = new AddServer2ViewModel(item);
            ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(addServer2ViewModel);
        }
        else if (eConfigType.IsGroupType())
        {
            var addGroupServerViewModel = new AddGroupServerViewModel(item);
            ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(addGroupServerViewModel);
        }
        else
        {
            var addServerViewModel = new AddServerViewModel(item);
            ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(addServerViewModel);
        }
        if (ret == true)
        {
            await RefreshServersDispatcherAsync();
            if (item.IndexId == _config.IndexId)
            {
                await Reload();
            }
        }
    }

    /// <summary>V2pn: the lists catch up with servers and subscriptions added behind their back (a move from another app).</summary>
    public async Task RefreshAfterImportAsync()
    {
        await RefreshSubscriptions();
        await RefreshServersDispatcherAsync();
    }

    /// <returns>How many servers were added; V2pn passes notifyFailure false and words the failure itself.</returns>
    public async Task<int> AddServerViaClipboardAsync(string? clipboardData, bool notifyFailure = true)
    {
        var stringData = clipboardData;
        if (clipboardData == null)
        {
            var result = await ReadTextFromClipboardInteraction.HandleSafe(RxVoid.Default);
            if (result.IsNullOrEmpty())
            {
                if (notifyFailure)
                {
                    NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
                }
                return 0;
            }
            stringData = result;
        }
        var ret = await ConfigDbHandler.AddBatchServers(stringData, _config.SubIndexId, false);
        if (ret > 0)
        {
            await RefreshSubscriptions();
            await RefreshServersDispatcherAsync();
            NoticeManager.Instance.Enqueue(string.Format(ResUI.SuccessfullyImportedServerViaClipboard, ret));
        }
        else if (notifyFailure)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
        return ret;
    }

    public async Task AddServerViaScanAsync()
    {
        var result = await ScanScreenInteraction.HandleSafe(RxVoid.Default);
        await ScanScreenResult(result);
    }

    public async Task ScanScreenResult(byte[]? bytes)
    {
        var result = QRCodeUtils.ParseBarcode(bytes);
        await AddScanResultAsync(result);
    }

    public async Task AddServerViaImageAsync()
    {
        var imageFileName = await BrowseImageFileInteraction.HandleSafe(RxVoid.Default);
        await ScanImageResult(imageFileName);
    }

    public async Task ScanImageResult(string fileName)
    {
        if (fileName.IsNullOrEmpty())
        {
            return;
        }

        var result = QRCodeUtils.ParseBarcode(fileName);
        await AddScanResultAsync(result);
    }

    private async Task AddScanResultAsync(string? result)
    {
        if (result.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.NoValidQRcodeFound);
        }
        else
        {
            var ret = await ConfigDbHandler.AddBatchServers(result, _config.SubIndexId, false);
            if (ret > 0)
            {
                await RefreshSubscriptions();
                await RefreshServersDispatcherAsync();
                NoticeManager.Instance.Enqueue(ResUI.SuccessfullyImportedServerViaScan);
            }
            else
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
            }
        }
    }

    #endregion Add Servers

    #region Subscription

    private async Task SubSettingAsync()
    {
        var subSettingViewModel = new SubSettingViewModel();
        if (await AppManager.Instance.WindowDialog.ShowDialogAsync(subSettingViewModel) == true)
        {
            await RefreshSubscriptions();
        }
    }

    public async Task UpdateSubscriptionProcess(string subId, bool blProxy)
    {
        await Task.Run(async () =>
        {
            var handler = new SubscriptionHandler(_config, blProxy, UpdateTaskHandler);
            await handler.UpdateProcess(subId);
        });
    }

    #endregion Subscription

    #region Setting

    /// <summary>V2pn: Settings on the local proxy page, from "Change port" when the port is taken.</summary>
    public Task OpenLocalPortSettingAsync() => OptionSettingAsync(startPage: 1);

    private async Task OptionSettingAsync(int startPage = 0)
    {
        var settingViewModel = new OptionSettingViewModel { StartPage = startPage };
        var ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(settingViewModel);
        if (ret == true)
        {
            MainGirdOrientation = _config.UiItem.MainGirdOrientation;
            RxSchedulers.MainThreadScheduler.Schedule(async () =>
            {
                await StatusBarViewModel.InboundDisplayStatus();
            });
            await Reload();
        }
    }

    private async Task RoutingSettingAsync()
    {
        var routingSettingViewModel = new RoutingSettingViewModel();
        var ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(routingSettingViewModel);
        if (ret == true)
        {
            await ConfigDbHandler.InitBuiltinRouting();
            RxSchedulers.MainThreadScheduler.Schedule(async () =>
            {
                await StatusBarViewModel.RefreshRoutingsMenu();
            });
            await Reload();
        }
    }

    private async Task DNSSettingAsync()
    {
        var dnsSettingViewModel = new DNSSettingViewModel();
        var ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(dnsSettingViewModel);
        if (ret == true)
        {
            await Reload();
        }
    }

    private async Task FullConfigTemplateAsync()
    {
        var fullConfigTemplateViewModel = new FullConfigTemplateViewModel();
        var ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(fullConfigTemplateViewModel);
        if (ret == true)
        {
            await Reload();
        }
    }

    private async Task ClearServerStatistics()
    {
        await StatisticsManager.Instance.ClearAllServerStatistics();
        await RefreshServersDispatcherAsync();
    }

    private async Task OpenTheFileLocation()
    {
        var path = Utils.StartupPath();
        if (Utils.IsWindows())
        {
            ProcUtils.ProcessStart(path);
        }
        else if (Utils.IsLinux())
        {
            ProcUtils.ProcessStart("xdg-open", path);
        }
        else if (Utils.IsMacOS())
        {
            ProcUtils.ProcessStart("open", path);
        }
        await Task.CompletedTask;
    }

    #endregion Setting

    #region core job

    private bool _hasNextReloadJob = false;
    private readonly SemaphoreSlim _reloadSemaphore = new(1, 1);

    // V2pn: V2pnSubUpdate.ConnectionKey of the server the running core was started with.
    private string? _runningConnectionKey;

    private async Task<bool> RunningConnectionUnchangedAsync()
    {
        if (_runningConnectionKey == null || !CoreManager.Instance.IsCoreAlive)
        {
            return false;
        }
        var current = await ConfigDbQuery.GetProfileItem(_config.IndexId);
        return await V2pnSubUpdate.ConnectionKey(current) == _runningConnectionKey;
    }

    /// <summary>
    /// V2pn: the Windows proxy is set only while a core serves it. When the core cannot start, or keeps
    /// dying, V2pn switches itself off the way the Disconnect button does (which clears the proxy if it is
    /// V2pn's own) and keeps the reason on screen. Leaving the proxy on a dead port cuts the internet;
    /// showing "connected" over it is the complaint every client of this kind gets.
    /// </summary>
    private async Task StopAfterFailureAsync(EV2pnFailure failure, string? detail = null)
    {
        StatusBarViewModel.InvalidateAvailability();
        // With the kill switch the internet stays closed instead (V2pnKillSwitch): the VPN-mode block stays in place,
        // or the system proxy stays on V2pn's port with no core behind it. Disconnect or a new connection ends it.
        var hold = V2pnKillSwitch.CanHold(_config, V2pnCoreWatch.FailureCause);
        V2pnKillSwitch.SetHolding(hold);
        V2pnCoreWatch.MarkFailed(failure, detail);
        if (hold)
        {
            Logging.SaveLog($"V2pn: the connection failed ({failure}) and the kill switch holds the internet closed: {V2pnCoreWatch.FailureDetail}");
            await Task.Run(async () =>
            {
                await CoreManager.Instance.CoreStop();
                V2pnKillSwitch.Sync(_config);
                if (!_config.TunModeItem.EnableTun)
                {
                    await SysProxyHandler.UpdateSysProxy(_config, false);
                }
            });
            return;
        }
        Logging.SaveLog($"V2pn: switched off after a failure ({failure}): {V2pnCoreWatch.FailureDetail}");
        await Task.Run(async () => await CoreManager.Instance.CoreStop());
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            if (StatusBarViewModel.EnableTun)
            {
                StatusBarViewModel.EnableTun = false;
            }
            StatusBarViewModel.SystemProxySelected = (int)ESysProxyType.ForcedClear;
        });
    }

    private async Task OnCoreExitedAsync(int exitCode)
    {
        try
        {
            // A reload in flight restarts or checks the core itself; look again once it is done.
            await _reloadSemaphore.WaitAsync();
            _reloadSemaphore.Release();
            if (V2pnConnection.IsOff(_config)
                || CoreManager.Instance.IsCoreAlive
                || V2pnCoreWatch.State == EV2pnCoreState.Failed)
            {
                return;
            }

            var delay = V2pnCoreWatch.Restarts.NextDelay(DateTime.UtcNow);
            if (delay == null)
            {
                await StopAfterFailureAsync(EV2pnFailure.KeepsStopping, $"exit code {exitCode}");
                return;
            }

            StatusBarViewModel.InvalidateAvailability();
            V2pnCoreWatch.MarkRestarting();
            NoticeManager.Instance.SendMessageEx($"V2pn: the core stopped (exit code {exitCode}); restarting in {delay.Value.TotalSeconds:0} s");
            await Task.Delay(delay.Value);
            if (V2pnConnection.IsOff(_config) || CoreManager.Instance.IsCoreAlive)
            {
                return;
            }
            await Reload();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(OnCoreExitedAsync), ex);
        }
    }

    public async Task Reload()
    {
        var availabilityGeneration = StatusBarViewModel.InvalidateAvailability();
        //If there are unfinished reload job, marked with next job.
        if (!await _reloadSemaphore.WaitAsync(0))
        {
            _hasNextReloadJob = true;
            return;
        }

        if (DesignMode)
        {
            _reloadSemaphore.Release();
            return;
        }

        try
        {
            SetReloadEnabled(false);
            _runningConnectionKey = null;

            RxSchedulers.MainThreadScheduler.Schedule(() =>
            {
                if (TabMainSelectedIndex < 0)
                {
                    TabMainSelectedIndex = 0;
                }
            });
            // V2pn: disconnected means off, not "still proxying on the local port". Checked before the
            // server lookup so Disconnect stops the core even when no server is selected.
            if (V2pnConnection.IsOff(_config))
            {
                await Task.Run(async () =>
                {
                    await CoreManager.Instance.CoreStop();
                    await SysProxyHandler.UpdateSysProxy(_config, false);
                    V2pnKillSwitch.Sync(_config);
                });
                V2pnKillSwitch.SetHolding(false);
                V2pnCoreWatch.MarkStopped();
                return;
            }
            var profileItem = await ConfigTextHandler.GetDefaultServer();
            if (profileItem == null)
            {
                // V2pn: no servers means off. Pointing Windows at a port no core listens on would cut the
                // internet while the UI says Unprotected. forceDisable leaves "keep system settings" alone.
                await Task.Run(async () =>
                {
                    await CoreManager.Instance.CoreStop();
                    await SysProxyHandler.UpdateSysProxy(_config, true);
                });
                V2pnCoreWatch.MarkStopped();
                return;
            }
            var availabilityTarget = StatusBarViewModel.CaptureAvailabilityTarget();
            V2pnCoreWatch.MarkStarting();
            var allResult = await CoreConfigContextBuilder.BuildAll(_config, profileItem);
            if (NoticeManager.Instance.NotifyValidatorResult(allResult.CombinedValidatorResult) && !allResult.Success)
            {
                await StopAfterFailureAsync(EV2pnFailure.StartFailed,
                    allResult.CombinedValidatorResult?.Errors?.FirstOrDefault());
                return;
            }

            var context = allResult.MainResult.Context;
            AvailabilityProbeEndpoint? probe = null;
            if (context.Node.ConfigType != EConfigType.Custom && context.FullConfigTemplate is not { Enabled: true })
            {
                try
                {
                    probe = AvailabilityProbeEndpoint.Create();
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                    Logging.SaveLog("Availability probe port", ex);
                }
            }
            await Task.Run(async () =>
            {
                // The VPN-mode block goes up before the core starts: nothing leaves directly while the tunnel comes up.
                V2pnKillSwitch.Sync(_config);
                await LoadCore(context with { AvailabilityProbe = probe }, allResult.PreSocksResult?.Context);
                // V2pn: a core that is not running gets no system proxy; a second look after the pause
                // catches a core that started and exited at once (a config it rejects).
                if (CoreManager.Instance.IsCoreAlive)
                {
                    await SysProxyHandler.UpdateSysProxy(_config, false);
                    await Task.Delay(1000);
                }
            });
            if (!CoreManager.Instance.IsCoreAlive)
            {
                await StopAfterFailureAsync(EV2pnFailure.StartFailed);
                return;
            }
            V2pnKillSwitch.SetHolding(false);
            V2pnCoreWatch.MarkRunning();
            // The tunnel adapter is up now; the block lets traffic through it by the adapter as well as by address.
            await Task.Run(() => V2pnKillSwitch.Sync(_config));
            _runningConnectionKey = await V2pnSubUpdate.ConnectionKey(profileItem);
            RxSchedulers.MainThreadScheduler.Schedule(async () =>
            {
                if (availabilityTarget.ProfileId != profileItem.IndexId
                    || !StatusBarViewModel.SetAvailabilityReady(availabilityGeneration, availabilityTarget, probe))
                {
                    return;
                }
                var result = await StatusBarViewModel.TestServerAvailability();
                if (result == null || profileItem.IndexId.IsNullOrEmpty())
                {
                    return;
                }

                await ProfilesViewModel.SetSpeedTestResult(new()
                {
                    IndexId = profileItem.IndexId,
                    IpInfo = result.Ip,
                    Delay = result.IsAvailable ? result.Time.ToString() : null
                });
                _ = Task.Run(() => AfterAvailabilityAsync(profileItem, result));
            });

            var showClashUI = AppManager.Instance.IsRunningCore(ECoreType.sing_box);
            if (showClashUI)
            {
                //await Observable.Start(async () =>
                //{
                //    await ClashProxiesViewModel.ProxiesReload();
                //}, RxSchedulers.MainThreadScheduler);
                await Signal.FromAsync(async () =>
                    {
                        await ClashProxiesViewModel.ProxiesReload();
                        return RxVoid.Default;
                    }).SubscribeOn(RxSchedulers.MainThreadScheduler)
                    .ToTask();
            }

            ReloadResult(showClashUI);
        }
        finally
        {
            SetReloadEnabled(true);
            _reloadSemaphore.Release();
            //If there is a next reload job, execute it.
            if (_hasNextReloadJob)
            {
                _hasNextReloadJob = false;
                await Reload();
            }
        }
    }

    private void ReloadResult(bool showClashUI)
    {
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            ShowClashUI = showClashUI;
            if (!showClashUI || TabMainSelectedIndex < 0)
            {
                TabMainSelectedIndex = 0;
            }
        });
    }

    private void SetReloadEnabled(bool enabled)
    {
        RxSchedulers.MainThreadScheduler.Schedule(() => BlReloadEnabled = enabled);
    }

    private async Task LoadCore(CoreConfigContext? mainContext, CoreConfigContext? preContext)
    {
        await CoreManager.Instance.LoadCore(mainContext, preContext);
    }

    #endregion core job

    #region Presets

    public async Task ApplyRegionalPreset(EPresetType type)
    {
        await ConfigDbHandler.ApplyRegionalPreset(type);
        await ConfigDbHandler.InitRouting();
        RxSchedulers.MainThreadScheduler.Schedule(async () =>
        {
            await StatusBarViewModel.RefreshRoutingsMenu();
        });

        await ConfigTextHandler.SaveConfig();
        using var downloadService = await DownloadService.CreateDownloadServiceWithCheck();
        await new UpdateService(downloadService, _config, UpdateTaskHandler).UpdateGeoFileAll();
        await Reload();
    }

    /// <summary>
    /// Switches geo files, rule templates and DNS to the Russian sources and downloads the files,
    /// without reloading the core (the caller selects a routing next, which reloads it).
    /// False when the geo files did not arrive: the previous sources are back, so a retry downloads again.
    /// </summary>
    public async Task<bool> UseRussianSourcesAsync()
    {
        var before = await V2pnRussianSources.TakeAsync(_config);
        await ConfigDbHandler.ApplyRegionalPreset(EPresetType.Russia);
        await ConfigDbHandler.InitRouting();
        await ConfigTextHandler.SaveConfig();
        // Messages only: a "success" here would restart the core right before the routing switch restarts it again.
        using var downloadService = await DownloadService.CreateDownloadServiceWithCheck();
        await new UpdateService(downloadService, _config, (_, msg) => UpdateTaskHandler(false, msg)).UpdateGeoFileAll();
        if (V2pnRussianSources.FilesRefreshed(before.Files))
        {
            return true;
        }
        await V2pnRussianSources.RestoreAsync(_config, before);
        await ConfigTextHandler.SaveConfig();
        return false;
    }

    #endregion Presets
}
