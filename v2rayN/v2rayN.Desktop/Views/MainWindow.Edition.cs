using Avalonia.Automation;
using Avalonia.Layout;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Views;

// The space of the partner whose tab is open (Common/Editions.cs, MainWindow.Tabs.cs), on V2pn's own layout; V2pn's tab
// has none: "Accent": Connect in its colour, its card at the top of the sidebar. "Scene", when its theme has a
// background: the same, with the background where the map is and its plan on it.
public partial class MainWindow
{
    private PartnerCard? _partnerCard;
    private PlanBlock? _planBlock;
    private V2pnPartnerStage? _stage;
    private string? _lookKey;
    private DispatcherTimer? _accountTimer;
    private bool _placeQueued;
    // How the plan last fitted the stage, for the debug window.
    private string _planFit = "—";
    // The last state the tray and the title showed; a partner coming or going renames both without waiting for the next one.
    private (ProtectionState State, string Label)? _trayState;

    private void InitEdition()
    {
        InitTabs();
        Editions.Changed += RefreshEdition;
        Editions.Closed += (partner, why) => ShowToast(why == V2pnPartnerGone.Unlinked ? ToastKind.Info : ToastKind.Warning, why switch
        {
            V2pnPartnerGone.Expired => string.Format(L.EditionGoneExpired, partner.Name),
            V2pnPartnerGone.Revoked => string.Format(L.EditionGoneRevoked, partner.Name),
            _ => string.Format(L.EditionGoneUnlinked, partner.Name),
        });
        ActualThemeVariantChanged += (_, _) => RefreshEdition();
        DebugOverrides.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            // Taking the background away rebuilds the look; the account is laid over by RefreshPartnerAccount.
            Editions.Update();
            RefreshPartnerAccount();
        });
        // The glass follows the header's parts and the plan block the room left under them.
        foreach (var part in new Control[] { panelHeader, panelHeadline, barConnection, btnConnect, contentStatusBarView, borderStage })
        {
            part.SizeChanged += (_, _) => QueuePlace();
        }
        pageOverlay.GetObservable(IsVisibleProperty).Subscribe(_ => QueuePlace());
        Editions.Start();
    }

    /// <summary>"V2pn × Configra" in Configra's tab; else V2pn.</summary>
    private static string EditionTitle() => Editions.Partner is { } partner ? $"{L.AppName} × {partner.Name}" : L.AppName;

    private void RefreshEdition()
    {
        var partner = Editions.Partner;
        if (_trayState is { } tray)
        {
            RefreshTray(tray.State, tray.Label);
        }
        UpdateLockup();

        var dark = ActualThemeVariant != ThemeVariant.Light;
        var stage = Editions.Stage(dark);
        var key = partner == null ? null
            : $"{partner.Id}|{partner.Folder}|{Editions.ThemeOf(partner).Key}|{dark}|{(stage == null ? "map" : $"{string.Join(",", stage.Stops)}|{stage.Scrim}|{stage.Art}")}";
        if (key != _lookKey)
        {
            _lookKey = key;
            _stage = stage;
            BuildLook(partner, dark);
        }
        ArrangeEdition();
        // A partner shown from the debug window brings its buttons too.
        RefreshPartnerActionBar();
        RefreshPartnerAccount();
        RefreshScene();

        if (partner != null)
        {
            // The plan and the days change rarely: the panel's answer after an update, the partner's API, a new day.
            _accountTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) =>
            {
                if (IsVisible)
                {
                    RefreshPartnerAccount();
                }
            });
            _accountTimer.Start();
        }
        else
        {
            _accountTimer?.Stop();
        }
    }

    private void BuildLook(V2pnPartner? partner, bool dark)
    {
        void Open(string url) => ProcUtils.ProcessStart(url);
        if (partner == null)
        {
            _partnerCard = null;
            _planBlock = null;
        }
        else
        {
            var accent = Editions.PartnerAccent;
            var onAccent = Appearance.OnAccent(Color.Parse(accent));
            // The links are text on the card: the brand moved in lightness until it reads there.
            var card = this.TryFindResource("V2CardMutedBackground", ActualThemeVariant, out var found) && found is ISolidColorBrush brush
                ? brush.Color
                : Color.Parse(dark ? "#26262F" : "#F0F1F5");
            var link = V2pnThemePalette.ReadableOn(V2pnColor.Parse(accent), new V2pnColor(card.R, card.G, card.B)).Hex;
            _partnerCard = new PartnerCard(partner, accent, $"#{onAccent.R:X2}{onAccent.G:X2}{onAccent.B:X2}", link, Open);
            _planBlock = _stage == null ? null : new PlanBlock(partner, _stage, Open);
        }
        partnerCardHost.Content = _partnerCard;
        passHost.Content = _planBlock;
        stageBackdrop.Content = partner != null && _stage != null ? PartnerStage.Build(partner, _stage) : null;
        stageBackdrop.IsVisible = stageBackdrop.Content != null;
        // The map's own fade only suits the map.
        borderStageFade.IsVisible = _stage == null;
        headerGlass.IsVisible = _stage != null;
        ApplyMapVisibility();
    }

    /// <summary>The map shows unless it is turned off, the window is one column, or a partner's background takes its place.</summary>
    private void ApplyMapVisibility()
    {
        mapView.IsVisible = Appearance.Current.ShowMap && _tier != BodyTier.Stacked && _stage == null;
    }

    /// <summary>What a change of layout (mini, medium, wide) changes in the partner's look; called by ArrangeForWidth too.</summary>
    private void ArrangeEdition()
    {
        UpdateLockup();
        PlacePartnerCard();
        QueuePlace();
    }

    /// <summary>
    /// In the mini window the sidebar is the strip under the stage, and the card under search and tabs would sit at the
    /// window's bottom edge, cut off: there it goes first, right under the stage. Wider, it stays under the tabs.
    /// </summary>
    private void PlacePartnerCard()
    {
        if (partnerCardHost.Parent is not DockPanel dock)
        {
            return;
        }
        var stacked = _tier == BodyTier.Stacked;
        var first = dock.Children.IndexOf(partnerCardHost) == 0;
        if (stacked != first)
        {
            dock.Children.Remove(partnerCardHost);
            dock.Children.Insert(stacked ? 0 : dock.Children.IndexOf(lstTabs) + 1, partnerCardHost);
        }
        partnerCardHost.Margin = stacked ? new Thickness(10, 10, 10, 0) : new Thickness(10, 0, 10, 6);
    }

    /// <summary>
    /// The title bar's left side: V2pn's fork and name when the person came from no partner, the tabs otherwise
    /// (MainWindow.Tabs.cs). The mini window shows a partner's tab as its mark alone.
    /// </summary>
    private void UpdateLockup()
    {
        AutomationProperties.SetName(borderBrand, L.AppName);
        RefreshTabs();
    }

    private void QueuePlace()
    {
        if (_placeQueued)
        {
            return;
        }
        _placeQueued = true;
        // After the layout pass: the parts' bounds are final then.
        Dispatcher.UIThread.Post(() =>
        {
            _placeQueued = false;
            UpdateHeaderGlass();
            UpdatePassPlace();
        }, DispatcherPriority.Background);
    }

    /// <summary>The glass under the header's parts (the pill, the server, Connect, mode and route); the partner's buttons stay outside it.</summary>
    private void UpdateHeaderGlass()
    {
        if (_stage == null)
        {
            return;
        }
        Rect? box = null;
        foreach (var part in new Control[] { pillState, txtHeaderTitle, txtHeaderSubtitle, btnConnect, barConnection })
        {
            if (!part.IsEffectivelyVisible || part.Bounds.Width <= 0 || part.TranslatePoint(default, panelStage) is not { } at)
            {
                continue;
            }
            var rect = new Rect(at, part.Bounds.Size);
            box = box is { } b ? b.Union(rect) : rect;
        }
        if (box is not { } r)
        {
            headerGlass.IsVisible = false;
            return;
        }
        // At least as wide as the bar under Connect gets, so a short server name does not make a narrow glass.
        const double padX = 20;
        const double padY = 16;
        var left = Math.Max(4, r.X - padX);
        var right = Math.Min(panelStage.Bounds.Width - 4, r.Right + padX);
        headerGlass.Margin = new Thickness(left, Math.Max(4, r.Y - padY), 0, 0);
        headerGlass.Width = Math.Max(0, right - left);
        headerGlass.Height = r.Height + (padY * 2);
        headerGlass.IsVisible = true;
    }

    /// <summary>
    /// The plan on the stage, over the status card, when there is room for it between the header and the card: roomy in
    /// the wide window, compact in a narrower or lower one. Without room, and in the mini window, the plan goes back to
    /// the partner's card in the sidebar.
    /// </summary>
    private void UpdatePassPlace()
    {
        var onStage = false;
        if (_planBlock != null && _tier != BodyTier.Stacked && !pageOverlay.IsVisible && borderStage.Bounds.Height > 0)
        {
            var status = contentStatusBarView.IsVisible && contentStatusBarView.Bounds.Height > 0 ? contentStatusBarView.Bounds.Height + 12 : 0;
            var big = _tier == BodyTier.Full;
            var inset = big ? 28 : 20;
            // Under the glass and under the partner's buttons, which sit below it.
            var top = Math.Max(headerGlass.IsVisible ? headerGlass.Margin.Top + headerGlass.Height : 0, panelHeader.Bounds.Bottom) + 16;
            var room = borderStage.Bounds.Height - inset - status - top;
            var width = Math.Min(big ? 340 : 420, Math.Max(0, borderStage.Bounds.Width - (inset * 2)));
            for (var level = big ? PlanBlock.Big : PlanBlock.Compact; level <= PlanBlock.Bare && !onStage; level++)
            {
                _planBlock.Arrange(level);
                var need = _planBlock.HeightAt(width);
                onStage = need <= room;
                _planFit = $"место {room:0}, нужно {need:0} (уровень {level})";
            }
            passHost.Margin = new Thickness(inset, 0, inset, inset + status);
        }
        passHost.IsVisible = onStage;
        // In Accent, and wherever the plan is not on the stage, the partner's card in the sidebar says it.
        partnerCardHost.IsVisible = _partnerCard != null && !onStage;
    }

    // The partner's API, if it has one: asked when its server is first selected, then every 30 min (account) and
    // 15 min (status) while the window shows its brand.
    private V2pnPartnerAccount? _partnerAccount;
    private IReadOnlyList<V2pnPartnerIncident>? _partnerIncidents;
    private string? _apiPartner;
    private DateTime _accountAsked;
    private DateTime _statusAsked;
    private bool _apiBusy;

    private async Task RefreshPartnerApiAsync(V2pnPartner partner, IReadOnlyList<SubItem> partnerSubs)
    {
        var now = DateTime.UtcNow;
        if (_apiPartner != partner.Id)
        {
            (_apiPartner, _partnerAccount, _partnerIncidents, _accountAsked, _statusAsked) = (partner.Id, null, null, default, default);
        }
        var wantAccount = now - _accountAsked > TimeSpan.FromMinutes(30);
        var wantStatus = now - _statusAsked > TimeSpan.FromMinutes(15);
        if (_apiBusy || partner.Pack.ApiUrl == null || (!wantAccount && !wantStatus))
        {
            return;
        }
        _apiBusy = true;
        var changed = false;
        try
        {
            if (wantAccount)
            {
                _accountAsked = now;
                var token = partnerSubs.Select(t => SubInfoManager.Instance.Get(t.Id)?.AccountToken).FirstOrDefault(t => t.IsNotEmpty());
                if (token != null && await Task.Run(() => V2pnPartnerApi.GetAccountAsync(partner, token)) is { } account && _apiPartner == partner.Id)
                {
                    _partnerAccount = account;
                    changed = true;
                }
                // The partner's live word about its servers, on the account's schedule (V2pnPartnerLive keeps its own clock).
                await RefreshPartnerLiveAsync(partner, force: false);
            }
            if (wantStatus)
            {
                _statusAsked = now;
                if (await Task.Run(() => V2pnPartnerApi.GetStatusAsync(partner)) is { } incidents && _apiPartner == partner.Id)
                {
                    _partnerIncidents = incidents;
                    changed = true;
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn partner API", ex);
        }
        finally
        {
            _apiBusy = false;
        }
        if (changed)
        {
            RefreshPartnerAccount();
        }
    }

    /// <summary>
    /// What the partner's panel and API say about the subscription (plan, term, traffic, devices, offer, links), for its
    /// card and its plan on the stage.
    /// </summary>
    private void RefreshPartnerAccount()
    {
        if (Editions.Partner is not { } partner || (_partnerCard == null && _planBlock == null))
        {
            return;
        }
        var profiles = ViewModel?.ProfilesViewModel;
        var partnerSubs = profiles == null
            ? []
            : profiles.SubItems.Where(t => t.Id.IsNotEmpty() && V2pnPartners.Instance.ForSubscription(t)?.Id == partner.Id).ToList();
        if (IsVisible && partnerSubs.Count > 0)
        {
            _ = RefreshPartnerApiAsync(partner, partnerSubs);
        }
        var api = _apiPartner == partner.Id ? _partnerAccount : null;
        // The subscription of the selected server first: its panel speaks for the plan in use.
        var info = partnerSubs.OrderByDescending(t => t.Id == _barSubId).Select(t => SubInfoManager.Instance.Get(t.Id)).FirstOrDefault(t => t != null);
        var usage = SubUsage.From(info);
        var incident = _apiPartner == partner.Id ? _partnerIncidents?.FirstOrDefault() : null;
        var account = new SceneAccount(
            usage?.HasTraffic == true ? usage.TrafficText : null,
            usage?.Fraction ?? 0,
            usage?.ShowBar == true,
            usage?.HasExpiry == true ? usage.ExpiryLong : api?.Until is { } until ? string.Format(L.PartnerUntil, until.ToDateTime(TimeOnly.MinValue).ToString("d MMMM yyyy", CultureInfo.CurrentUICulture)) : null,
            usage?.TrafficWarn == true || usage?.ExpiryWarn == true,
            usage?.TrafficBad == true || usage?.ExpiryBad == true,
            NonEmpty(api?.Note) ?? (info?.Announce.IsNotEmpty() == true ? info.Announce : null),
            NonEmpty(info?.WebPageUrl) ?? NonEmpty(partner.Pack.CabinetUrl),
            NonEmpty(info?.SupportUrl) ?? NonEmpty(partner.Pack.SupportUrl),
            NonEmpty(api?.Plan),
            api?.Devices is { } count ? api.DeviceLimit is { } limit ? string.Format(L.PartnerDevicesOf, count, limit) : string.Format(L.PartnerDevices, count) : null,
            incident == null ? null : incident.Text.IsNullOrEmpty() ? incident.Title : $"{incident.Title}. {incident.Text}",
            incident?.Severe == true,
            // The announce-url belongs to the panel's announcement, not to the API's note.
            NonEmpty(api?.Note) == null ? NonEmpty(info?.AnnounceUrl) : null,
            V2pnSubDefaults.Banner(info, DateTimeOffset.Now),
            usage?.DaysLeft ?? (api?.Until is { } end ? Math.Max(0, end.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber) : null));
        var shown = DebugOverrides.Apply(account.IsEmpty ? null : account);
        _sceneAccount = shown;
        _partnerCard?.Update(shown, partner.Name);
        _planBlock?.Update(shown, partner.Name);
        QueuePlace();
    }

    private static string? NonEmpty(string? text) => text.IsNullOrEmpty() ? null : text;
}
