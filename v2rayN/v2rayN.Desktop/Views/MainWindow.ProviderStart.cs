using v2rayN.Desktop.Common;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

// V2pn: what a provider asks for at start with the headers Happ and v2RayTun read (V2pnSubDefaults): connecting by
// itself, updating its subscription, testing its servers. Each is a default the person can turn off per subscription.
public partial class MainWindow
{
    private bool _providerStartRan;

    private async Task RunProviderStartAsync()
    {
        var vm = ViewModel;
        if (_providerStartRan || vm == null)
        {
            return;
        }
        _providerStartRan = true;
        try
        {
            await vm.Initialized;
            await SubInfoManager.Instance.LoadAsync();
            await V2pnLocks.Instance.RefreshAsync();
            // Read once the start is done: a person who left V2pn connected is connected again by now.
            var startsConnected = !V2pnConnection.IsOff(_config);
            var subs = (await ConfigDbQuery.SubItems() ?? []).Where(s => s.Id.IsNotEmpty() && s.Enabled).ToList();
            if (subs.Count == 0)
            {
                return;
            }
            var bySub = await ServersBySubAsync();
            string? serverSubId = null;
            if (_config.IndexId.IsNotEmpty() && await ConfigDbQuery.GetProfileItem(_config.IndexId) is { } current)
            {
                serverSubId = current.Subid ?? string.Empty;
            }

            var tested = new HashSet<string>();
            var plan = V2pnSubDefaults.StartPlan(startsConnected, subs, serverSubId, SubInfoManager.Instance.Get, bySub.ContainsKey,
                id => V2pnLocks.Instance.Hides(id));
            if (plan != null)
            {
                var sub = subs.First(s => s.Id == plan.SubId);
                await AutoconnectAsync(vm, plan, SideSubRow.NameOf(sub), bySub[plan.SubId]);
                if (plan.Type == SubPanelHeaders.ConnectLowestDelay)
                {
                    tested.Add(plan.SubId);
                }
            }

            // Updated one at a time, as the scheduler does; a provider's site that is blocked directly is tried through the VPN.
            var updated = false;
            foreach (var sub in subs.Where(s => s.Url.IsNotEmpty() && V2pnSubDefaults.UpdatesOnStart(SubInfoManager.Instance.Get(s.Id))))
            {
                Logging.SaveLog($"V2pn: updating subscription {sub.Id} at start, as its provider asks");
                await vm.UpdateSubscriptionProcess(sub.Id, false);
                updated = true;
            }

            var toTest = subs.Where(s => !tested.Contains(s.Id) && V2pnSubDefaults.PingsOnStart(SubInfoManager.Instance.Get(s.Id))).Select(s => s.Id).ToHashSet();
            if (toTest.Count > 0)
            {
                // An update may have brought other servers.
                if (updated)
                {
                    bySub = await ServersBySubAsync();
                }
                var servers = toTest.Where(bySub.ContainsKey).SelectMany(id => bySub[id]).ToList();
                if (servers.Count > 0)
                {
                    Logging.SaveLog($"V2pn: testing {servers.Count} servers at start, as their providers ask");
                    await WaitForPingEndAsync(vm.ProfilesViewModel);
                    await PingAndWaitAsync(() => vm.ProfilesViewModel.PingProfilesAsync(servers));
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn: provider start", ex);
        }
    }

    // Every server that belongs to a subscription, in the order the app keeps them.
    private static async Task<Dictionary<string, List<ProfileItemModel>>> ServersBySubAsync() =>
        (await ConfigDbQuery.ProfileModels(string.Empty, string.Empty) ?? [])
        .Where(m => m.Subid.IsNotEmpty())
        .GroupBy(m => m.Subid!)
        .ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>
    /// Connects to a server of the provider's subscription, picked the way the provider asked. Only what can happen
    /// without a question: VPN mode that needs administrator rights waits for the person's own Connect.
    /// </summary>
    private async Task AutoconnectAsync(MainWindowViewModel vm, SubAutoconnectPlan plan, string provider, List<ProfileItemModel> servers)
    {
        var status = vm.StatusBarViewModel;
        if (_config.UiItem.V2pnPreferTun && !status.AllowEnableTun())
        {
            Logging.SaveLog("V2pn: the provider's autoconnect waits: VPN mode needs administrator rights");
            return;
        }
        if (plan.Type == SubPanelHeaders.ConnectLowestDelay)
        {
            await WaitForPingEndAsync(vm.ProfilesViewModel);
            if (!await PingAndWaitAsync(() => vm.ProfilesViewModel.PingProfilesAsync(servers)))
            {
                return;
            }
        }
        var delays = (await ProfileExManager.Instance.GetProfileExs()).GroupBy(t => t.IndexId).ToDictionary(g => g.Key, g => g.First().Delay);
        var candidates = servers.Select(m => (m.IndexId, delays.GetValueOrDefault(m.IndexId))).ToList();
        var id = V2pnSubDefaults.PickServer(plan.Type, candidates, _config.UiItem.V2pnRecent ?? [], _config.IndexId, Random.Shared);
        if (id == null)
        {
            ShowToast(ToastKind.Warning, string.Format(L.AutoconnectNone, provider));
            return;
        }
        // The person may have connected (or chosen a server) while the servers were being tested.
        if (CurrentState() != ProtectionState.Unprotected)
        {
            return;
        }
        var server = servers.First(m => m.IndexId == id);
        Logging.SaveLog($"V2pn: connecting at start to {id} ({plan.Type}), as the provider of subscription {plan.SubId} advises");
        await ConnectToAsync(id);
        ShowToast(ToastKind.Info, string.Format(L.AutoconnectToast, CountryResolver.ServerName(server), provider));
    }
}
