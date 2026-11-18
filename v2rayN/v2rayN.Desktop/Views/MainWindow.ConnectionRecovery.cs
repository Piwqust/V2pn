using System.Collections.Concurrent;
using ServiceLib.Services;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class MainWindow
{
    private CancellationTokenSource? _replacementSearch;
    private AvailabilityCheckResult? _replacementCheck;
    private AvailabilityCheckResult? _offerOtherCountriesFor;

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
            var current = await AppManager.Instance.GetProfileItem(indexId);
            if (current == null || !IsCurrent())
            {
                return;
            }

            var country = ServerCountry(current);
            var otherCountries = ReferenceEquals(_offerOtherCountriesFor, origin);
            var all = await AppManager.Instance.ProfileItems(string.Empty) ?? [];
            var sameCountry = all.Where(p => p.IndexId != indexId && country.IsNotEmpty()
                && string.Equals(ServerCountry(p), country, StringComparison.OrdinalIgnoreCase)).ToList();
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

            var code = ServerCountry(best);
            var place = WorldMap.Instance.Get(code)?.Name ?? (code.IsNotEmpty() ? code : L.UnknownServerCountry);
            var confirm = string.Format(L.ConfirmReplacement, CountryResolver.ServerName(best), place);
            if (await UI.ShowYesNo(confirm, L.SwitchServer) == ButtonResult.Yes && IsCurrent())
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

    private static string? ServerCountry(ProfileItem profile) =>
        V2pnPartnerLive.Instance.Describe(profile.Subid, profile.Remarks, profile.Address)?.Country
        ?? CountryResolver.Resolve(profile.Remarks);
}
