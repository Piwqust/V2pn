using System.Collections.Concurrent;
using ServiceLib.Services;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

public partial class MainWindow
{
    private CancellationTokenSource? _replacementSearch;
    private AvailabilityCheckResult? _replacementCheck;
    private AvailabilityCheckResult? _offerOtherCountriesFor;

    // The subscription of the selected server, for what its provider reports next to Connect.
    private string? _activeSubId;

    // VPN rights were asked for and refused; cleared by the next attempt or by disconnecting.
    private bool _tunDenied;
    private Func<Task>? _fixPrimary;
    private Func<Task>? _fixSecondary;

    private void SetTunDenied(bool denied)
    {
        if (denied)
        {
            V2pnCoreWatch.ClearFailure();
        }
        if (_tunDenied != denied)
        {
            _tunDenied = denied;
            RefreshStatePill();
        }
    }

    /// <summary>
    /// An action next to the problem, only for causes V2pn has seen itself: a taken port opens the port setting,
    /// refused VPN rights can be asked for again or traded for proxy mode. An unknown failure keeps its own words
    /// and points at the log instead of guessing.
    /// </summary>
    private void RefreshFixActions(bool faulted, bool enabled)
    {
        string? text = null;
        (string Label, Func<Task> Run)? primary = null;
        (string Label, Func<Task> Run)? secondary = null;
        var showLog = (L.FixShowLog, (Func<Task>)(() =>
        {
            ShowPage(L.MenuLog, MsgPage);
            return Task.CompletedTask;
        }));
        if (faulted && V2pnCoreWatch.FailureCause == EV2pnFailureCause.PortBusy)
        {
            text = V2pnCoreWatch.FailureDetail;
            primary = (L.FixChangePort, () => ViewModel?.OpenLocalPortSettingAsync() ?? Task.CompletedTask);
            secondary = showLog;
        }
        else if (faulted)
        {
            text = V2pnCoreWatch.FailureDetail.IsNotEmpty()
                ? $"{Shorten(V2pnCoreWatch.FailureDetail!, 160)} {L.UnknownFailureExplain}"
                : L.UnknownFailureExplain;
            primary = showLog;
        }
        else if (_tunDenied)
        {
            text = L.TunDeniedExplain;
            primary = (L.FixTryVpnAgain, () =>
            {
                if (enabled)
                {
                    lstMode.SelectedIndex = 1;
                }
                else
                {
                    ToggleConnection(true);
                }
                return Task.CompletedTask;
            });
            if (!enabled)
            {
                secondary = (L.FixUseProxy, () =>
                {
                    SetTunDenied(false);
                    lstMode.SelectedIndex = 0;
                    ToggleConnection(true);
                    return Task.CompletedTask;
                });
            }
        }

        else if (SubscriptionOver(_activeSubId) is { } over)
        {
            text = over.Text;
            var subId = _activeSubId!;
            primary = (L.SubUpdateNow, () => UpdateSubWithStateAsync(subId, viaVpn: false));
            if (over.Page.IsNotEmpty())
            {
                secondary = (L.SubWebPage, () =>
                {
                    ProcUtils.ProcessStart(over.Page);
                    return Task.CompletedTask;
                });
            }
        }

        // Something went wrong on screen: the report for support is one click away.
        btnDiagnostics.IsVisible = faulted || _tunDenied
            || ViewModel?.StatusBarViewModel.CurrentAvailability.IsFailure == true;
        RefreshBypassUi(ViewModel?.StatusBarViewModel.CurrentAvailability.IsFailure == true);

        txtProblem.Text = text;
        txtProblem.IsVisible = text.IsNotEmpty();
        _fixPrimary = primary?.Run;
        _fixSecondary = secondary?.Run;
        btnFixPrimary.Content = primary?.Label;
        btnFixPrimary.IsVisible = primary != null;
        btnFixSecondary.Content = secondary?.Label;
        btnFixSecondary.IsVisible = secondary != null;
    }

    /// <summary>The provider's own word that the subscription is over: expired, or out of traffic.</summary>
    private (string Text, string? Page)? SubscriptionOver(string? subId)
    {
        if (SubInfoManager.Instance.Get(subId) is not { HasUserInfo: true } info)
        {
            return null;
        }
        var sub = ViewModel?.ProfilesViewModel.SubNavItems.FirstOrDefault(s => s.Id == subId)?.Item;
        var name = SideSubRow.NameOf(sub);
        var page = info.WebPageUrl.IsNotEmpty() ? info.WebPageUrl : info.SupportUrl;
        if (info.Expire > 0 && info.Expire < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(info.Expire).LocalDateTime.ToString("d MMMM yyyy", CultureInfo.CurrentUICulture);
            return (string.Format(L.SubExpiredNotice, name, date), page);
        }
        if (info.Total > 0 && info.Upload + info.Download >= info.Total)
        {
            return (string.Format(L.SubTrafficOutNotice, name), page);
        }
        return null;
    }

    private async Task ShowDiagnosticsAsync()
    {
        try
        {
            var status = ViewModel?.StatusBarViewModel;
            var routeId = status?.SelectedRouting?.Id;
            var route = RoutingPresets.KindOf(routeId) is { } kind ? RoutingPresets.Id(kind) : routeId.IsNullOrEmpty() ? "none" : "custom";
            var snapshot = await V2pnDiagnostics.CollectAsync(_config, status?.CurrentAvailability, status?.AvailabilityCheckedAt, route);
            await ShowRoomyDialog(new DiagnosticsView(V2pnDiagnostics.Format(snapshot)));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(ShowDiagnosticsAsync), ex);
        }
    }

    private static async Task RunFixAsync(Func<Task>? fix)
    {
        if (fix == null)
        {
            return;
        }
        try
        {
            await fix();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(RunFixAsync), ex);
        }
    }

    private void RefreshReplacementAction(AvailabilityCheckResult check, bool enabled)
    {
        if (_replacementSearch != null && !ReferenceEquals(check, _replacementCheck))
        {
            _replacementSearch.Cancel();
        }
        var searching = _replacementSearch is { IsCancellationRequested: false };
        btnReplacement.IsVisible = enabled && check.CanSearchForReplacement;
        var label = searching ? L.CancelServerSearch
            : ReferenceEquals(_offerOtherCountriesFor, check) ? L.CheckOtherCountries : L.FindWorkingServer;
        txtReplacement.Text = label;
        Avalonia.Automation.AutomationProperties.SetName(btnReplacement, label);
    }

    private async Task FindWorkingServerAsync()
    {
        if (_replacementSearch is { } running)
        {
            running.Cancel();
            btnReplacement.IsEnabled = false;
            return;
        }

        var status = ViewModel?.StatusBarViewModel;
        var profiles = ViewModel?.ProfilesViewModel;
        var origin = status?.CurrentAvailability;
        if (origin?.CanSearchForReplacement != true || profiles == null)
        {
            return;
        }

        using var search = new CancellationTokenSource();
        _replacementSearch = search;
        _replacementCheck = origin;
        RefreshReplacementAction(origin, enabled: true);
        var indexId = _config.IndexId;
        bool IsCurrent() => !search.IsCancellationRequested && _config.IndexId == indexId
            && ReferenceEquals(status!.CurrentAvailability, origin);

        try
        {
            var current = await ConfigDbQuery.GetProfileItem(indexId);
            if (current == null || !IsCurrent())
            {
                return;
            }

            var country = ServerCountry(current);
            var otherCountries = ReferenceEquals(_offerOtherCountriesFor, origin);
            var all = await ConfigDbQuery.ProfileItems(string.Empty) ?? [];
            var sameCountry = SameCountry(current, all);
            var elsewhere = all.Where(p => p.IndexId != indexId && current.Subid.IsNotEmpty()
                && p.Subid == current.Subid && !string.Equals(ServerCountry(p), country, StringComparison.OrdinalIgnoreCase)).ToList();
            var candidates = otherCountries ? elsewhere : sameCountry;
            var best = await ProbeReplacementAsync(candidates, search.Token);
            if (!IsCurrent())
            {
                return;
            }
            if (best == null)
            {
                if (!otherCountries && elsewhere.Count > 0)
                {
                    _offerOtherCountriesFor = origin;
                    ShowToast(ToastKind.Warning, L.NoServersInCountryResponded);
                }
                else
                {
                    ShowToast(ToastKind.Warning, L.NoneResponding);
                }
                return;
            }

            var place = CountryName(ServerCountry(best));
            var confirm = otherCountries
                ? string.Format(L.ConfirmReplacementOtherCountry, CountryName(country), CountryResolver.ServerName(best), place)
                : string.Format(L.ConfirmReplacement, CountryResolver.ServerName(best), place);
            if (await UI.ShowYesNo(confirm, L.SwitchServer, icon: "SemiIconRefresh") == ButtonResult.Yes && IsCurrent())
            {
                await profiles.SetDefaultServer(best.IndexId);
            }
        }
        catch (OperationCanceledException) when (search.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(FindWorkingServerAsync), ex);
            if (IsCurrent())
            {
                ShowToast(ToastKind.Warning, L.CheckFailureHint);
            }
        }
        finally
        {
            _replacementSearch = null;
            _replacementCheck = null;
            btnReplacement.IsEnabled = true;
            RefreshStatePill();
        }
    }

    private async Task<ProfileItem?> ProbeReplacementAsync(List<ProfileItem> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var delays = new ConcurrentDictionary<string, int>();
        var service = new SpeedtestService(_config, result =>
        {
            if (result.IndexId.IsNotEmpty() && int.TryParse(result.Delay, out var delay) && delay > 0)
            {
                delays[result.IndexId] = delay;
            }
            return Task.CompletedTask;
        });
        await Task.Run(() => service.RunLoop(ESpeedActionType.Realping, candidates, cancellationToken), cancellationToken);
        return candidates.Where(p => delays.ContainsKey(p.IndexId)).MinBy(p => delays[p.IndexId]);
    }

    /// <summary>The other servers of <paramref name="current"/>'s country, from every subscription.</summary>
    private static List<ProfileItem> SameCountry(ProfileItem current, List<ProfileItem> all)
    {
        var country = ServerCountry(current);
        return all.Where(p => p.IndexId != current.IndexId && country.IsNotEmpty()
            && string.Equals(ServerCountry(p), country, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    #region Switching by itself (V2pnAutoSwitch)

    private readonly V2pnAutoSwitch _autoSwitch = new();
    private DispatcherTimer? _recheckTimer;
    private bool _autoSwitching;

    /// <summary>Checks a working connection again every few minutes, quietly, so a server that dies is noticed.</summary>
    private void StartConnectionRecheck()
    {
        if (_recheckTimer != null)
        {
            return;
        }
        _recheckTimer = new DispatcherTimer { Interval = V2pnAutoSwitch.Recheck };
        _recheckTimer.Tick += async (_, _) =>
        {
            if (ViewModel?.StatusBarViewModel is { } status && V2pnConnectionService.Instance.IsConnected
                && status.CurrentAvailability.IsAvailable)
            {
                await status.TestServerAvailability(quiet: true);
            }
        };
        _recheckTimer.Start();
    }

    /// <summary>After every check: check a failure once more, then switch to a working server of the same country.</summary>
    private async Task OnCheckForAutoSwitchAsync()
    {
        if (ViewModel?.StatusBarViewModel is not { } status || _autoSwitching)
        {
            return;
        }
        var check = status.CurrentAvailability;
        if (check.State is AvailabilityState.Unchecked or AvailabilityState.Checking or AvailabilityState.Unsupported)
        {
            return;
        }
        var server = _config.IndexId;
        var action = _autoSwitch.OnResult(V2pnAutoSwitch.Enabled(_config), V2pnConnectionService.Instance.IsConnected, server, check, DateTime.UtcNow);
        if (action == EV2pnAutoAction.RecheckSoon)
        {
            await Task.Delay(V2pnAutoSwitch.Confirm);
            if (_config.IndexId == server && V2pnConnectionService.Instance.IsConnected)
            {
                await status.TestServerAvailability();
            }
        }
        else if (action == EV2pnAutoAction.Search && _replacementSearch == null)
        {
            await AutoSwitchAsync(server, check);
        }
    }

    private async Task AutoSwitchAsync(string? server, AvailabilityCheckResult origin)
    {
        var status = ViewModel?.StatusBarViewModel;
        var profiles = ViewModel?.ProfilesViewModel;
        if (status == null || profiles == null || await ConfigDbQuery.GetProfileItem(server) is not { } current)
        {
            return;
        }
        _autoSwitching = true;
        try
        {
            var all = await ConfigDbQuery.ProfileItems(string.Empty) ?? [];
            using var search = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var best = await ProbeReplacementAsync(SameCountry(current, all), search.Token);
            _autoSwitch.Searched(DateTime.UtcNow);
            // The person may have changed the server, disconnected or got the old one back meanwhile.
            if (best == null || _config.IndexId != server || !V2pnConnectionService.Instance.IsConnected
                || status.CurrentAvailability.IsAvailable)
            {
                return;
            }
            Logging.SaveLog($"V2pn: {current.IndexId} stopped answering ({origin.State}); switched to {best.IndexId} in the same country");
            await profiles.SetDefaultServer(best.IndexId);
            ShowToast(ToastKind.Info, string.Format(L.AutoSwitched, CountryResolver.ServerName(current), CountryResolver.ServerName(best)));
        }
        catch (OperationCanceledException)
        {
            // The search took too long; the button stays for the person.
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(AutoSwitchAsync), ex);
        }
        finally
        {
            _autoSwitching = false;
        }
    }

    #endregion Switching by itself (V2pnAutoSwitch)

    private static string CountryName(string? code) =>
        WorldMap.Instance.Get(code)?.Name ?? (code.IsNotEmpty() ? code! : L.UnknownServerCountry);

    private static string? ServerCountry(ProfileItem profile) =>
        V2pnPartnerLive.Instance.Describe(profile.Subid, profile.Remarks, profile.Address)?.Country
        ?? CountryResolver.Resolve(profile.Remarks);
}
