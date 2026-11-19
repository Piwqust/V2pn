namespace ServiceLib.ViewModels;

/// <summary>The way being tried on the connected server: which (null: none), its number, how many.</summary>
public sealed record V2pnTuneProgress(V2pnBypassVariant? Variant, int Index, int Count);

// V2pn: "Обход блокировок" on the running connection: the network check, the search for a way, the network memory.
public partial class MainWindowViewModel
{
    private CancellationTokenSource? _bypassTune;

    // Server and network pairs looked at automatically in this session; each gets one automatic look.
    private readonly HashSet<string> _bypassAutoLooked = [];

    [Reactive]
    public partial V2pnTuneProgress? BypassTune { get; set; }

    /// <summary>A search for a way ended; true when the person started it.</summary>
    public event Action<V2pnTuneOutcome, bool>? BypassTuned;

    /// <summary>The network check ran by itself after a failure and found what blocks the connection.</summary>
    public event Action<V2pnBlockReport>? BypassChecked;

    public bool BypassTuning => _bypassTune != null;

    public void CancelBypassTune() => _bypassTune?.Cancel();

    /// <summary>The network check on the selected server and the running connection.</summary>
    public async Task<V2pnBlockReport> CheckNetworkAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var server = await ConfigTextHandler.GetDefaultServer();
        var proxy = StatusBarViewModel.CurrentProbeProxy();
        var report = await Task.Run(() => new V2pnBlockCheck(server, proxy, _config.TunModeItem.EnableTun).RunAsync(progress, ct), ct);
        Logging.SaveLog($"V2pn bypass: network check: {report.Verdict}; {string.Join("; ", report.Probes.Select(p => $"{p.Name} {p.Ok} {p.Detail}"))}");
        return report;
    }

    /// <summary>
    /// Tries the bypass ways on the selected server until one makes it carry traffic again (V2pnBypassTuner). Null
    /// when the bypass is off, a search is already running, or the server is not one fragmentation can help.
    /// </summary>
    public async Task<V2pnTuneOutcome?> TuneBypassAsync(bool manual)
    {
        if (!V2pnBypass.On(_config) || _bypassTune != null || V2pnConnection.IsOff(_config))
        {
            return null;
        }
        var server = await ConfigTextHandler.GetDefaultServer();
        if (server == null)
        {
            return null;
        }
        var core = AppManager.Instance.GetCoreType(server, server.ConfigType);
        if (V2pnBypass.CandidatesFor(server, core).Count == 0 && V2pnBypass.Learned(_config, server.IndexId, V2pnBypass.NetworkKey(_config)) == null)
        {
            return null;
        }

        using var cts = new CancellationTokenSource();
        _bypassTune = cts;
        try
        {
            await V2pnNetworkId.RefreshAsync();
            var outcome = await V2pnBypassTuner.TuneAsync(_config, server, core,
                ReloadSettledAsync,
                ProxyCarriesAsync,
                (variant, index, count) => RxSchedulers.MainThreadScheduler.Schedule(() => BypassTune = new(variant, index, count)),
                cts.Token);
            await ConfigTextHandler.SaveConfig();
            BypassTuned?.Invoke(outcome, manual);
            return outcome;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(TuneBypassAsync), ex);
            return null;
        }
        finally
        {
            _bypassTune = null;
            RxSchedulers.MainThreadScheduler.Schedule(() => BypassTune = null);
        }
    }

    /// <summary>Forgets the way kept for the selected server on this network and reconnects without it.</summary>
    public async Task ForgetBypassForServerAsync()
    {
        var server = await ConfigTextHandler.GetDefaultServer();
        if (server != null && V2pnBypass.Forget(_config, server.IndexId, V2pnBypass.NetworkKey(_config)))
        {
            await ConfigTextHandler.SaveConfig();
            await Reload();
        }
    }

    /// <summary>The way kept for the selected server on this network, for the screen.</summary>
    public async Task<V2pnBypassVariant?> BypassWayForServerAsync()
    {
        var server = await ConfigTextHandler.GetDefaultServer();
        return server == null || !V2pnBypass.On(_config) ? null : V2pnBypass.Learned(_config, server.IndexId, V2pnBypass.NetworkKey(_config));
    }

    /// <summary>A reload that waits for one already running, so the way on trial is the one connected.</summary>
    private async Task ReloadSettledAsync()
    {
        await _reloadSemaphore.WaitAsync();
        _reloadSemaphore.Release();
        await Reload();
    }

    /// <summary>A small and a large download go through the restarted connection (not stopping at the frozen kilobytes).</summary>
    private async Task<bool> ProxyCarriesAsync(CancellationToken ct)
    {
        IWebProxy? proxy = null;
        for (var i = 0; i < 20 && proxy == null; i++)
        {
            await Task.Delay(500, ct);
            proxy = CoreManager.Instance.IsCoreAlive ? StatusBarViewModel.CurrentProbeProxy() : null;
        }
        if (proxy == null)
        {
            return false;
        }
        var report = await new V2pnBlockCheck(null, proxy, tun: true).RunAsync(ct: ct);
        return report.ProxySmall == true && report.ProxyLarge == true;
    }

    /// <summary>
    /// After a connection is checked: with automatic search on, a failure gets the network check, and when the check
    /// points at the connection itself (frozen or cut, not a whitelist or a dead network), the ways are tried. A
    /// connection that answers gets one large download, to catch the freeze a small check passes. Once per server
    /// and network in a session.
    /// </summary>
    private async Task AfterAvailabilityAsync(ProfileItem server, AvailabilityCheckResult result)
    {
        try
        {
            if (!V2pnBypass.AutoTune(_config) || _bypassTune != null || server.IndexId.IsNullOrEmpty())
            {
                return;
            }
            if (V2pnNetworkId.Current.IsNullOrEmpty())
            {
                await V2pnNetworkId.RefreshAsync();
            }
            var key = $"{V2pnBypass.NetworkKey(_config)}|{server.IndexId}";
            if (result.IsAvailable)
            {
                var core = AppManager.Instance.GetCoreType(server, server.ConfigType);
                if (V2pnBypass.CandidatesFor(server, core).Count == 0 || !_bypassAutoLooked.Add(key))
                {
                    return;
                }
                if (!await ProxyCarriesAsync(CancellationToken.None))
                {
                    Logging.SaveLog("V2pn bypass: small requests pass, a large download stops; looking for a way");
                    await TuneBypassAsync(false);
                }
                return;
            }
            if (!result.CanSearchForReplacement || !_bypassAutoLooked.Add(key))
            {
                return;
            }
            var report = await CheckNetworkAsync();
            BypassChecked?.Invoke(report);
            if (report.Verdict is EV2pnBlockVerdict.Freeze or EV2pnBlockVerdict.ProxyCut or EV2pnBlockVerdict.Unknown)
            {
                await TuneBypassAsync(false);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(AfterAvailabilityAsync), ex);
        }
    }

    /// <summary>
    /// The network changed: when the selected server has another way kept for the new network (or had one for the old),
    /// the connection is restarted with it. True when it was restarted.
    /// </summary>
    private async Task<bool> ApplyBypassForNetworkAsync()
    {
        if (!V2pnBypass.On(_config))
        {
            return false;
        }
        var server = await ConfigTextHandler.GetDefaultServer();
        var before = V2pnBypassPlan.From(_config).For(server)?.Id;
        if (!await V2pnNetworkId.RefreshAsync())
        {
            return false;
        }
        var after = V2pnBypassPlan.From(_config).For(server)?.Id;
        if (before == after)
        {
            return false;
        }
        Logging.SaveLog($"V2pn bypass: another network; the server's way goes from {before ?? "none"} to {after ?? "none"}");
        RxSchedulers.MainThreadScheduler.Schedule(() => _ = Reload());
        return true;
    }
}
