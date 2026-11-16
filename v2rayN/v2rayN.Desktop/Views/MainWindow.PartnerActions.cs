using Avalonia.Layout;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

// A partner's buttons (pack "actions", V2pnPartnerCatalog): under Connect while a server of the partner's subscription
// is in use. Each button only names an action of
// V2pn; this file runs them. Server choices come from V2pn's own checks or the partner's API (V2pnPartnerChoice), and
// after a connect the window says where it went and why.
public partial class MainWindow
{
    private readonly V2pnActionCooldown _actionCooldown = new(V2pnActionCooldown.Default);
    private PartnerActionsBar? _actionsBar;
    private string? _barSubId;
    private V2pnPartner? _barPartner;
    // The button running now ("partner:action"); one at a time, the others wait greyed out.
    private (string Partner, string Action)? _actionRunning;

    private void InitPartnerActions()
    {
        _actionsBar = new PartnerActionsBar();
        _actionsBar.Run += (action, anchor) =>
        {
            if (_barPartner is { } partner)
            {
                _ = RunPartnerActionAsync(partner, action, anchor);
            }
        };
        panelConnect.Children.Add(_actionsBar);
        // Hidden servers, groups and closed settings follow the partners and their API.
        V2pnLocks.Instance.Changed += () => Dispatcher.UIThread.Post(OnPartnerDataChanged);
        V2pnPartnerLive.Instance.Changed += () => Dispatcher.UIThread.Post(OnPartnerDataChanged);
        V2pnPartners.Instance.Removed += (partner, _) => V2pnPartnerLive.Instance.Forget(partner.Id);
        _ = V2pnLocks.Instance.RefreshAsync();
        _ = PruneDelayHistoryAsync();
    }

    // Once a start: the delay history ("stable") forgets servers that are gone, so its file does not grow forever.
    private static async Task PruneDelayHistoryAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            var ids = (await AppManager.Instance.ProfileModels(string.Empty, string.Empty) ?? []).Select(m => m.IndexId).ToList();
            if (ids.Count > 0)
            {
                await Task.Run(() => V2pnDelayHistory.Instance.Retain(ids));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn delay history", ex);
        }
    }

    private void OnPartnerDataChanged()
    {
        if (ViewModel?.ProfilesViewModel is { } profiles)
        {
            _ = profiles.RefreshServersBiz();
        }
        RefreshPartnerActionBar();
        RefreshPartnerAccount();
    }

    /// <summary>Called with the subscription of the server in use whenever the header changes.</summary>
    private void RefreshPartnerActionBar(string? subId)
    {
        _barSubId = subId;
        RefreshPartnerActionBar();
        // The partner's routing mode shows while its subscription is in use (once the menu has its routes).
        if (_routeShown)
        {
            RefreshRouteUi();
        }
    }

    private void RefreshPartnerActionBar()
    {
        // The partner's buttons belong to its tab, whatever server is selected there.
        var partner = Editions.Partner;
        _barPartner = partner;
        _actionsBar?.Show(partner, RunningAction(partner));
        if (partner != null && IsVisible)
        {
            _ = RefreshPartnerLiveAsync(partner, force: false);
        }
    }

    private string? RunningAction(V2pnPartner? partner) =>
        partner != null && _actionRunning is { } running && running.Partner == partner.Id ? running.Action : null;

    /// <summary>The subscriptions of <paramref name="partner"/> on this computer.</summary>
    private static List<string> PartnerSubIds(V2pnPartner partner) =>
        V2pnLocks.Instance.Partners.Where(kv => kv.Value.Id == partner.Id).Select(kv => kv.Key).ToList();

    private static string? PartnerToken(V2pnPartner partner) =>
        PartnerSubIds(partner).Select(id => SubInfoManager.Instance.Get(id)?.AccountToken).FirstOrDefault(t => t.IsNotEmpty());

    /// <summary>The partner's live server notes (load, health, groups), asked as often as its account: every 30 minutes at most.</summary>
    private static async Task RefreshPartnerLiveAsync(V2pnPartner partner, bool force)
    {
        try
        {
            await Task.Run(() => V2pnPartnerLive.Instance.RefreshAsync(partner, PartnerToken(partner), DateTime.UtcNow, force));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn partner servers", ex);
        }
    }

    /// <summary>Every server of the partner's subscriptions (hidden ones too) with what V2pn and the partner know about it.</summary>
    private static async Task<(List<V2pnCandidate> Candidates, Dictionary<string, ProfileItemModel> Models)> PartnerServersAsync(V2pnPartner partner)
    {
        var subs = PartnerSubIds(partner).ToHashSet(StringComparer.Ordinal);
        var models = (await AppManager.Instance.ProfileModels(string.Empty, string.Empty) ?? [])
            .Where(m => m.Subid != null && subs.Contains(m.Subid))
            .ToList();
        var delays = (await ProfileExManager.Instance.GetProfileExs()).GroupBy(t => t.IndexId).ToDictionary(g => g.Key, g => g.First().Delay);
        var candidates = models.Select(m => new V2pnCandidate(
            m.IndexId,
            m.Remarks ?? string.Empty,
            m.Address,
            delays.GetValueOrDefault(m.IndexId),
            V2pnPartnerLive.Instance.Describe(partner, m.Remarks, m.Address),
            V2pnDelayHistory.Instance.Get(m.IndexId))).ToList();
        return (candidates, models.ToDictionary(m => m.IndexId));
    }

    /// <summary>Tests <paramref name="servers"/> now (after any run already going) and reads the results back.</summary>
    private async Task<List<V2pnCandidate>> MeasureAsync(V2pnPartner partner, IReadOnlyList<V2pnCandidate> servers, Dictionary<string, ProfileItemModel> models)
    {
        var profiles = ViewModel?.ProfilesViewModel;
        if (profiles != null && servers.Count > 0)
        {
            await WaitForPingEndAsync(profiles);
            await PingAndWaitAsync(() => profiles.PingProfilesAsync(servers.Select(s => models[s.Id]).ToList()));
        }
        var ids = servers.Select(s => s.Id).ToHashSet();
        return (await PartnerServersAsync(partner)).Candidates.Where(c => ids.Contains(c.Id)).ToList();
    }

    /// <summary>Runs one of the partner's buttons. A press within 3 s of the same button does nothing.</summary>
    private async Task RunPartnerActionAsync(V2pnPartner partner, V2pnPartnerAction action, Control? anchor)
    {
        if (_actionRunning != null || !_actionCooldown.TryStart($"{partner.Id}:{action.Id}", DateTime.UtcNow))
        {
            return;
        }
        _actionRunning = (partner.Id, action.Id);
        RefreshPartnerActionBar();
        RefreshPartnerAccount();
        try
        {
            Logging.SaveLog($"V2pn: {partner.Id} button {action.Id} ({action.Do})");
            if (V2pnActionKinds.Connects(action.Do))
            {
                await ConnectByActionAsync(partner, action);
            }
            else
            {
                await RunOtherActionAsync(partner, action);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn partner button", ex);
        }
        finally
        {
            _actionRunning = null;
            RefreshPartnerActionBar();
            RefreshPartnerAccount();
        }
    }

    private async Task ConnectByActionAsync(V2pnPartner partner, V2pnPartnerAction action)
    {
        var (all, models) = await PartnerServersAsync(partner);
        V2pnChoice? choice = null;
        string? reason = null;
        switch (action.Do)
        {
            case V2pnActionKinds.ConnectServer:
                if (V2pnPartnerChoice.Named(action.Server!, all) is { } named)
                {
                    choice = new V2pnChoice(named, V2pnChoiceReason.Named);
                }
                else
                {
                    ShowToast(ToastKind.Warning, string.Format(L.ActionNoServers, partner.Name));
                    return;
                }
                break;

            case V2pnActionKinds.ConnectGroup:
            case V2pnActionKinds.ConnectBest:
                var scope = action.Do == V2pnActionKinds.ConnectGroup ? new V2pnActionScope(action.Group, null, []) : action.Scope;
                var inScope = V2pnPartnerChoice.InScope(all, scope);
                if (inScope.Count == 0)
                {
                    ShowToast(ToastKind.Warning, string.Format(L.ActionNoServers, partner.Name));
                    return;
                }
                if (action.Strategy == V2pnStrategies.Load)
                {
                    await RefreshPartnerLiveAsync(partner, force: false);
                }
                // "Now": a fresh check of the servers it chooses from, which also adds to the history "stable" reads.
                var measured = await MeasureAsync(partner, inScope, models);
                choice = V2pnPartnerChoice.Best(measured, action.Strategy ?? V2pnStrategies.Ping);
                if (action.Do == V2pnActionKinds.ConnectGroup && choice != null)
                {
                    reason = string.Format(L.ReasonFastestIn, action.Group);
                }
                break;

            case V2pnActionKinds.ConnectRecommend:
                var token = PartnerToken(partner);
                var recommended = await Task.Run(() => V2pnPartnerApi.GetRecommendAsync(partner, token, action.Purpose!));
                if (recommended != null)
                {
                    var named2 = V2pnPartnerChoice.RecommendedIn(recommended, all);
                    var checkedServers = await MeasureAsync(partner, named2, models);
                    choice = V2pnPartnerChoice.Recommended(recommended, checkedServers);
                }
                else
                {
                    Logging.SaveLog($"V2pn: {partner.Id} gave no recommendation; the fastest for {action.Purpose} instead");
                }
                if (choice == null)
                {
                    // The partner's API failed or none of its picks answered: the quickest server for the purpose.
                    var forPurpose = V2pnPartnerChoice.InScope(all, new V2pnActionScope(null, action.Purpose, []));
                    choice = V2pnPartnerChoice.Fastest(await MeasureAsync(partner, forPurpose, models));
                }
                break;
        }

        if (choice == null || !models.TryGetValue(choice.Server.Id, out var model))
        {
            ShowToast(ToastKind.Warning, string.Format(L.ActionNoAnswer, partner.Name));
            return;
        }
        await ConnectToAsync(model.IndexId);
        reason ??= choice.Reason switch
        {
            V2pnChoiceReason.Steadiest => L.ReasonSteadiest,
            V2pnChoiceReason.LeastLoaded => L.ReasonLeastLoaded,
            V2pnChoiceReason.ByPartner => string.Format(L.ReasonByPartner, partner.Name),
            V2pnChoiceReason.Named => string.Format(L.ReasonNamed, partner.Name),
            _ => L.ReasonFastest,
        };
        Logging.SaveLog($"V2pn: {partner.Id} button {action.Id} connected to {model.IndexId} ({choice.Reason})");
        ShowToast(ToastKind.Success, string.Format(L.ActionConnected, ServerNames.TrimLeadingSymbols(CountryResolver.ServerName(model)), reason));
    }

    private async Task RunOtherActionAsync(V2pnPartner partner, V2pnPartnerAction action)
    {
        var info = PartnerSubIds(partner).Select(id => SubInfoManager.Instance.Get(id)).FirstOrDefault(i => i != null);
        switch (action.Do)
        {
            case V2pnActionKinds.OpenCabinet:
                OpenPartnerLink(partner, NonEmpty(info?.WebPageUrl) ?? partner.Pack.CabinetUrl);
                break;

            case V2pnActionKinds.OpenRenew:
                OpenPartnerLink(partner, NonEmpty(info?.ExpiryButtonUrl) ?? NonEmpty(info?.WebPageUrl) ?? partner.Pack.CabinetUrl);
                break;

            case V2pnActionKinds.OpenSupport:
                OpenPartnerLink(partner, NonEmpty(info?.SupportUrl) ?? partner.Pack.SupportUrl);
                break;

            case V2pnActionKinds.OpenLink:
                OpenPartnerLink(partner, V2pnPartnerCatalog.ActionLink(partner.Listing, action.Url));
                break;

            case V2pnActionKinds.SubscriptionUpdate:
                foreach (var subId in PartnerSubIds(partner))
                {
                    await UpdateSubWithStateAsync(subId, false);
                }
                await RefreshPartnerLiveAsync(partner, force: true);
                break;

            case V2pnActionKinds.CheckServices:
                await ShowServiceCheckAsync();
                break;

            case V2pnActionKinds.InfoCard:
                var card = await Task.Run(() => V2pnPartnerApi.GetCardAsync(partner, PartnerToken(partner), action.Id));
                if (card == null)
                {
                    ShowToast(ToastKind.Warning, string.Format(L.ActionPartnerSilent, partner.Name));
                    return;
                }
                // Not awaited: the button is free again while the card is open.
                _ = ShowPartnerDialogAsync(new PartnerCardView(string.Format(L.CardFrom, partner.Name), card.Title ?? action.Label, PartnerCardView.Text(card.Text)));
                break;
        }
    }

    // Links from the subscription's headers are checked when they arrive, the pack's when it is read; here only that
    // nothing but a web page or Telegram opens.
    private void OpenPartnerLink(V2pnPartner partner, string? url)
    {
        if (url.IsNullOrEmpty() || SubPanelHeaders.ParseLink(url) == null)
        {
            ShowToast(ToastKind.Warning, string.Format(L.ActionNoLink, partner.Name));
            return;
        }
        ProcUtils.ProcessStart(url);
    }

    /// <summary>check.services: YouTube, Telegram and ChatGPT through the connection, each with its time.</summary>
    private async Task ShowServiceCheckAsync()
    {
        var rows = new StackPanel { Spacing = 8 };
        var status = new TextBlock { Classes = { "srowhint" }, Margin = new Thickness(0), Text = L.ServicesChecking, TextWrapping = TextWrapping.Wrap };
        rows.Children.Add(status);
        var view = new PartnerCardView(L.AppName, L.ServicesTitle, rows);
        _ = ShowPartnerDialogAsync(view);
        var results = await Task.Run(() => V2pnServiceCheck.RunAsync());
        if (results == null)
        {
            status.Text = L.ServicesOff;
        }
        else
        {
            status.Text = L.ServicesNote;
            foreach (var result in results)
            {
                var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 10 };
                var mark = new PathIcon { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
                mark.Bind(PathIcon.DataProperty, mark.GetResourceObservable(result.Opens ? "SemiIconTickCircle" : "SemiIconAlertCircle"));
                mark.Bind(PathIcon.ForegroundProperty, mark.GetResourceObservable(result.Opens ? "V2Safe" : "V2Unsafe"));
                grid.Children.Add(mark);
                var name = new TextBlock { Text = result.Name, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(name, 1);
                grid.Children.Add(name);
                var verdict = new TextBlock
                {
                    Classes = { "muted" },
                    Text = result.Opens ? string.Format(L.ServicesOpens, result.Milliseconds) : L.ServicesFails,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(verdict, 2);
                grid.Children.Add(verdict);
                rows.Children.Add(grid);
            }
        }
    }

    private static async Task ShowPartnerDialogAsync(Control view)
    {
        if (DialogHost.IsDialogOpen(null))
        {
            DialogHost.Close(null);
        }
        await DialogHost.Show(view);
    }
}
