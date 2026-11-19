namespace ServiceLib.ViewModels;

public partial class StatusBarViewModel : MyReactiveObject
{
    public Interaction<string, RxVoid> SetClipboardDataInteraction { get; } = new();
    public Interaction<RxVoid, string?> PasswordInputInteraction { get; } = new();
    public Interaction<RxVoid, RxVoid> DispatcherRefreshIconInteraction { get; } = new();
    public EventChannel<bool> SubscriptionsUpdateRequested { get; } = new();
    public EventChannel<bool?> ShowHideWindowRequested { get; } = new();

    private static readonly Lazy<StatusBarViewModel> _instance = new(() => new());
    public static StatusBarViewModel Instance => _instance.Value;

    public EventChannel<RxVoid> ReloadRequested { get; } = new();
    public EventChannel<RxVoid> AddServerViaScanRequested { get; } = new();
    public EventChannel<RxVoid> AddServerViaClipboardRequested { get; } = new();

    #region ObservableCollection

    public BulkObservableCollection<RoutingItem> RoutingItems { get; } = [];

    [Reactive]
    public partial RoutingItem SelectedRouting { get; set; }

    #endregion ObservableCollection

    public ReactiveCommand<RxVoid, RxVoid> AddServerViaClipboardCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddServerViaScanCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubUpdateCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubUpdateViaProxyCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> CopyProxyCmdToClipboardCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> NotifyLeftClickCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ShowWindowCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> HideWindowCmd { get; }

    #region System Proxy

    [Reactive]
    public partial bool BlSystemProxyClear { get; set; }

    [Reactive]
    public partial bool BlSystemProxySet { get; set; }

    [Reactive]
    public partial bool BlSystemProxyNothing { get; set; }

    [Reactive]
    public partial bool BlSystemProxyPac { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> SystemProxyClearCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SystemProxySetCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SystemProxyNothingCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SystemProxyPacCmd { get; }

    [Reactive]
    public partial int SystemProxySelected { get; set; }

    [Reactive]
    public partial bool BlSystemProxyPacVisible { get; set; }

    #endregion System Proxy

    #region UI

    [Reactive]
    public partial string InboundDisplay { get; set; }

    [Reactive]
    public partial string InboundLanDisplay { get; set; }

    [Reactive]
    public partial string RunningServerDisplay { get; set; }

    [Reactive]
    public partial string RunningServerToolTipText { get; set; }

    [Reactive]
    public partial string RunningInfoDisplay { get; set; }

    [Reactive]
    public partial AvailabilityCheckResult Availability { get; private set; }

    [Reactive]
    public partial string SpeedProxyDisplay { get; set; }

    [Reactive]
    public partial string SpeedDirectDisplay { get; set; }

    [Reactive]
    public partial bool EnableTun { get; set; }

    [Reactive]
    public partial bool BlIsNonWindows { get; set; }

    #endregion UI

    #region V2pn tray

    // Set by the window from the same verdict as its pill, so the tray never contradicts it
    // (the system-proxy type alone says nothing about VPN mode or a server that doesn't answer).

    [Reactive]
    public partial string TrayToolTipText { get; set; }

    [Reactive]
    public partial string? TrayStatusText { get; set; }

    [Reactive]
    public partial string? TrayConnectText { get; set; }

    [Reactive]
    public partial bool BlTrayRouteRussia { get; set; }

    [Reactive]
    public partial bool BlTrayRouteBlocked { get; set; }

    [Reactive]
    public partial bool BlTrayRouteAll { get; set; }

    #endregion V2pn tray

    public StatusBarViewModel()
    {
        _config = ConfigTextHandler.Config;
        SelectedRouting = new();
        Availability = AvailabilityCheckResult.Unchecked;
        RunningServerToolTipText = GetRunningServerToolTipText("-");
        TrayToolTipText = Global.AppName;
        BlSystemProxyPacVisible = Utils.IsWindows();
        BlIsNonWindows = Utils.IsNonWindows();

        if (_config.TunModeItem.EnableTun && AllowEnableTun())
        {
            EnableTun = true;
        }
        else
        {
            _config.TunModeItem.EnableTun = EnableTun = false;
        }

        #region WhenAnyValue && ReactiveCommand

        this.WhenAnyValue(x => x.SelectedRouting)
            .Where(y => y != null && !y.Remarks.IsNullOrEmpty())
            .SubscribeAsync(async _ => await RoutingSelectedChangedAsync());

        SystemProxySelected = (int)_config.SystemProxyItem.SysProxyType;
        this.WhenAnyValue(x => x.SystemProxySelected)
            .Where(y => y >= 0)
            .SubscribeAsync(async _ => await DoSystemProxySelected());

        this.WhenAnyValue(x => x.EnableTun)
            .SubscribeAsync(async _ => await DoEnableTun());

        CopyProxyCmdToClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await CopyProxyCmdToClipboard();
        });

        NotifyLeftClickCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(null);
            await Task.CompletedTask;
        });
        ShowWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(true);
            await Task.CompletedTask;
        });
        HideWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(false);
            await Task.CompletedTask;
        });

        AddServerViaClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
            {
                await AddServerViaClipboard();
            });
        AddServerViaScanCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaScan();
        });
        SubUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(false);
        });
        SubUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(true);
        });

        //System proxy
        SystemProxyClearCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.ForcedClear);
        });
        SystemProxySetCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.ForcedChange);
        });
        SystemProxyNothingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Unchanged);
        });
        SystemProxyPacCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Pac);
        });

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        AppEvents.DispatcherStatisticsRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async result => await UpdateStatistics(result));

        AppEvents.SysProxyChangeRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async result => await SetListenerType(result));

        #endregion AppEvents

        _ = Init();
    }

    private async Task Init()
    {
        await ConfigDbHandler.InitBuiltinRouting();
        await RefreshRoutingsMenu();
        await InboundDisplayStatus();
        // The view refreshes its icon on initialization; interaction handlers are not registered yet.
        // V2pn: when connected, the first reload sets the proxy once the core listens. When off, apply now:
        // that clears a proxy a crashed V2pn left pointing at its dead port.
        await ChangeSystemProxyAsync(_config.SystemProxyItem.SysProxyType, false, V2pnConnection.IsOff(_config));
    }

    private async Task CopyProxyCmdToClipboard()
    {
        var cmd = Utils.IsWindows() ? "set" : "export";
        var address = $"{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}";

        var sb = new StringBuilder();
        sb.AppendLine($"{cmd} http_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} https_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} all_proxy={Global.Socks5Protocol}{address}");
        sb.AppendLine("");
        sb.AppendLine($"{cmd} HTTP_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} HTTPS_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} ALL_PROXY={Global.Socks5Protocol}{address}");

        await SetClipboardDataInteraction.HandleSafe(sb.ToString());
    }

    private async Task AddServerViaClipboard()
    {
        AddServerViaClipboardRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task AddServerViaScan()
    {
        AddServerViaScanRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task UpdateSubscriptionProcess(bool blProxy)
    {
        // V2pn: started from the tray; the window shows the progress and the result.
        ShowHideWindowRequested.Publish(true);
        SubscriptionsUpdateRequested.Publish(blProxy);
        await Task.Delay(1000);
    }

    public async Task RefreshServersBiz()
    {
        //display running server
        var running = await ConfigTextHandler.GetDefaultServer();
        if (running != null)
        {
            RunningServerDisplay = running.GetSummary();
            RunningServerToolTipText = GetRunningServerToolTipText(RunningServerDisplay);
        }
        else
        {
            RunningServerDisplay = ResUI.CheckServerSettings;
            RunningServerToolTipText = GetRunningServerToolTipText(RunningServerDisplay);
        }
    }

    private string GetRunningServerToolTipText(string serverInfo)
    {
        return Utils.IsLinux() ? Global.AppName : serverInfo;
    }

    internal sealed record AvailabilityTarget(string ProfileId, string? RouteId, bool Tun, ESysProxyType Proxy, int Port, string Url);

    private long _availabilityGeneration;
    private (long Generation, AvailabilityTarget Target, AvailabilityProbeEndpoint? Endpoint)? _runningProbe;

    internal AvailabilityTarget CaptureAvailabilityTarget() => new(_config.IndexId,
        _config.RoutingBasicItem.RoutingIndexId, _config.TunModeItem.EnableTun, _config.SystemProxyItem.SysProxyType,
        AppManager.Instance.GetLocalPort(EInboundProtocol.socks), _config.SpeedTestItem.SpeedPingTestUrl);

    private bool IsProbeCurrent(long generation, AvailabilityTarget target) =>
        generation == Interlocked.Read(ref _availabilityGeneration)
        && !V2pnConnection.IsOff(_config)
        && CoreManager.Instance.IsCoreAlive
        && target == CaptureAvailabilityTarget();

    public AvailabilityCheckResult CurrentAvailability =>
        _runningProbe is { } probe && IsProbeCurrent(probe.Generation, probe.Target)
            ? Availability : AvailabilityCheckResult.Unchecked;

    /// <summary>The running connection's check port as a proxy, for the bypass's own checks; null when not running.</summary>
    public IWebProxy? CurrentProbeProxy() =>
        _runningProbe is { Endpoint: { } endpoint } probe && IsProbeCurrent(probe.Generation, probe.Target)
            ? new WebProxy($"socks5://{Global.Loopback}:{endpoint.Port}")
            {
                Credentials = new NetworkCredential(AvailabilityProbeEndpoint.User, endpoint.Password),
            }
            : null;

    internal long InvalidateAvailability()
    {
        var generation = Interlocked.Increment(ref _availabilityGeneration);
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            if (generation == Interlocked.Read(ref _availabilityGeneration))
            {
                _runningProbe = null;
                _recheckAfterCurrent = false;
                Availability = AvailabilityCheckResult.Unchecked;
                RunningInfoDisplay = string.Empty;
            }
        });
        return generation;
    }

    internal bool SetAvailabilityReady(long generation, AvailabilityTarget target, AvailabilityProbeEndpoint? endpoint)
    {
        if (!IsProbeCurrent(generation, target))
        {
            return false;
        }
        _runningProbe = (generation, target, endpoint);
        Availability = endpoint != null ? AvailabilityCheckResult.Unchecked : new(AvailabilityState.Unsupported);
        return true;
    }

    private bool _recheckAfterCurrent;

    /// <summary>When the last check result landed; for the diagnostics report.</summary>
    public DateTime? AvailabilityCheckedAt { get; private set; }

    /// <summary>
    /// Checks the connection again after a network change. A check already running started on the old network,
    /// so another one follows it.
    /// </summary>
    public async Task<AvailabilityCheckResult?> RecheckAvailabilityAsync()
    {
        if (Availability.State == AvailabilityState.Checking)
        {
            _recheckAfterCurrent = true;
            return null;
        }
        return await TestServerAvailability();
    }

    public async Task<AvailabilityCheckResult?> TestServerAvailability()
    {
        if (_runningProbe is not { Endpoint: { } endpoint } probe || !IsProbeCurrent(probe.Generation, probe.Target)
            || Availability.State == AvailabilityState.Checking)
        {
            return null;
        }

        Availability = new(AvailabilityState.Checking);
        RunningInfoDisplay = ResUI.Speedtesting;
        var result = await ConnectionHandler.RunAvailabilityCheck(endpoint, probe.Target.Url);
        if (!IsProbeCurrent(probe.Generation, probe.Target))
        {
            return null;
        }

        if (_recheckAfterCurrent)
        {
            _recheckAfterCurrent = false;
            Availability = AvailabilityCheckResult.Unchecked;
            return await TestServerAvailability();
        }
        Availability = result;
        AvailabilityCheckedAt = DateTime.Now;
        var message = string.Format(ResUI.TestMeOutput, result.IsAvailable ? result.Time : -1, result.Ip ?? Global.None);
        RunningInfoDisplay = message;
        NoticeManager.Instance.SendMessageEx(message);
        if (result.IsAvailable)
        {
            var id = probe.Target.ProfileId;
            if (result.Ip.IsNotEmpty())
            {
                ProfileExManager.Instance.SetTestIpInfo(id, result.Ip);
            }
            ProfileExManager.Instance.SetTestDelay(id, result.Time);
        }
        return result;
    }

    #region System proxy and Routings

    private async Task SetListenerType(ESysProxyType type)
    {
        if (_config.SystemProxyItem.SysProxyType == type)
        {
            return;
        }
        var wasOff = V2pnConnection.IsOff(_config); // V2pn
        InvalidateAvailability();
        _config.SystemProxyItem.SysProxyType = type;
        // V2pn: when turning on, the reload below starts the core and sets the proxy once the core listens.
        // Setting it here would point Windows at a port nobody answers until the core is up.
        var turningOn = wasOff && !V2pnConnection.IsOff(_config);
        await ChangeSystemProxyAsync(type, true, !turningOn);
        NoticeManager.Instance.SendMessageEx($"{ResUI.TipChangeSystemProxy} - {_config.SystemProxyItem.SysProxyType}");

        SystemProxySelected = (int)_config.SystemProxyItem.SysProxyType;
        await ConfigTextHandler.SaveConfig();
        // V2pn: the core runs only while connected, so turning on or off starts or stops it.
        if (wasOff != V2pnConnection.IsOff(_config) || !_config.TunModeItem.EnableTun)
        {
            ReloadRequested.Publish();
        }
    }

    public async Task ChangeSystemProxyAsync(ESysProxyType type, bool blChange, bool applyNow = true)
    {
        if (applyNow)
        {
            await SysProxyHandler.UpdateSysProxy(_config, false);
        }

        BlSystemProxyClear = type == ESysProxyType.ForcedClear;
        BlSystemProxySet = type == ESysProxyType.ForcedChange;
        BlSystemProxyNothing = type == ESysProxyType.Unchanged;
        BlSystemProxyPac = type == ESysProxyType.Pac;

        if (blChange)
        {
            await DispatcherRefreshIconInteraction.HandleSafe(RxVoid.Default);
        }
    }

    public async Task RefreshRoutingsMenu()
    {
        var routings = await ConfigDbQuery.RoutingItems();

        RoutingItems.ReplaceRange(routings);

        SelectedRouting = routings.FirstOrDefault(t => t.IsActive == true);
    }

    private async Task RoutingSelectedChangedAsync()
    {
        if (SelectedRouting == null)
        {
            return;
        }

        var item = await ConfigDbQuery.GetRoutingItem(SelectedRouting?.Id);
        if (item is null)
        {
            return;
        }

        if (await ConfigDbHandler.SetDefaultRouting(item) == 0)
        {
            NoticeManager.Instance.SendMessageEx(ResUI.TipChangeRouting);
            ReloadRequested.Publish();
            await DispatcherRefreshIconInteraction.HandleSafe(RxVoid.Default);
        }
    }

    private async Task DoSystemProxySelected()
    {
        if (_config.SystemProxyItem.SysProxyType == (ESysProxyType)SystemProxySelected)
        {
            return;
        }
        await SetListenerType((ESysProxyType)SystemProxySelected);
    }

    private async Task DoEnableTun()
    {
        if (_config.TunModeItem.EnableTun == EnableTun)
        {
            return;
        }

        InvalidateAvailability();
        _config.TunModeItem.EnableTun = EnableTun;

        if (EnableTun && AllowEnableTun() == false)
        {
            // When running as a non-administrator, reboot to administrator mode
            if (Utils.IsWindows())
            {
                _config.TunModeItem.EnableTun = false;
                if (!await AppManager.Instance.RebootAsAdmin())
                {
                    // V2pn: UAC declined, the app stays open; the toggle goes back to off.
                    EnableTun = false;
                }
                return;
            }
            else
            {
                var password = await PasswordInputInteraction.HandleSafe(RxVoid.Default);
                if (password.IsNullOrEmpty())
                {
                    EnableTun = false;
                    return;
                }
            }
        }

        await ConfigTextHandler.SaveConfig();
        ReloadRequested.Publish();
    }

    public bool AllowEnableTun()
    {
        if (Utils.IsWindows())
        {
            return Utils.IsAdministrator();
        }
        else if (Utils.IsLinux())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        else if (Utils.IsMacOS())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        return false;
    }

    public async Task<bool> RequestTunPermissionAsync() =>
        AllowEnableTun() || (await PasswordInputInteraction.HandleSafe(RxVoid.Default)).IsNotEmpty();

    #endregion System proxy and Routings

    #region UI

    public async Task InboundDisplayStatus()
    {
        StringBuilder sb = new();
        sb.Append($"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");
        if (_config.Inbound.First().SecondLocalPortEnabled)
        {
            sb.Append($",{AppManager.Instance.GetLocalPort(EInboundProtocol.socks2)}");
        }
        sb.Append(']');
        InboundDisplay = $"{ResUI.LabLocal}:{sb}";

        if (_config.Inbound.First().AllowLANConn)
        {
            var lan = _config.Inbound.First().NewPort4LAN
                ? $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks3)}]"
                : $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}]";
            InboundLanDisplay = $"{ResUI.LabLAN}:{lan}";
        }
        else
        {
            InboundLanDisplay = $"{ResUI.LabLAN}:{Global.None}";
        }
        await Task.CompletedTask;
    }

    public async Task UpdateStatistics(ServerSpeedItem update)
    {
        if (!_config.GuiItem.DisplayRealTimeSpeed)
        {
            return;
        }

        try
        {
            if (AppManager.Instance.IsRunningCore(ECoreType.sing_box))
            {
                SpeedProxyDisplay = string.Format(ResUI.SpeedDisplayText, EInboundProtocol.mixed, Utils.HumanFy(update.ProxyUp), Utils.HumanFy(update.ProxyDown));
                SpeedDirectDisplay = string.Empty;
            }
            else
            {
                SpeedProxyDisplay = string.Format(ResUI.SpeedDisplayText, Global.ProxyTag, Utils.HumanFy(update.ProxyUp), Utils.HumanFy(update.ProxyDown));
                SpeedDirectDisplay = string.Format(ResUI.SpeedDisplayText, Global.DirectTag, Utils.HumanFy(update.DirectUp), Utils.HumanFy(update.DirectDown));
            }
        }
        catch
        {
        }
        await Task.CompletedTask;
    }

    #endregion UI
}
