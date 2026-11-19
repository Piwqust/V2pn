using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

// A partner's reviewed routing.json as one more mode of "What goes through the VPN" (V2pnPartnerRoutes) while its tab is
// open, and its reviewed connection settings, which the core picks up on the next reload. V2pn's tab has no partner's
// mode: leaving a partner's tab takes its mode out, and V2pn's "Everything through VPN" takes its place if it was on.
public partial class MainWindow
{
    private string? _partnerRouteId;
    // What the partners' reviewed files are now; a change while a partner's server runs reloads the core.
    private string? _partnerFilesSignature;
    private readonly SemaphoreSlim _partnerRoutesGate = new(1, 1);

    private void InitPartnerRoutes()
    {
        btnRoutePartner.Click += async (_, _) =>
        {
            if (_partnerRouteId is { } id)
            {
                await SelectPartnerRouteAsync(id);
            }
        };
        // A partner added, refreshed, unlinked, expired or revoked, or another tab opened: the presets follow.
        V2pnPartners.Instance.Changed += () => Dispatcher.UIThread.Post(() => _ = SyncPartnerRoutesAsync(refresh: true));
        V2pnPartnerTraffics.Changed += subId => Dispatcher.UIThread.Post(() => OnPartnerTrafficChanged(subId));
    }

    /// <summary>Brings the partner presets in line with the partners; reloads the core when the active mode changed under it.</summary>
    private async Task SyncPartnerRoutesAsync(bool refresh)
    {
        var status = ViewModel?.StatusBarViewModel;
        if (status == null)
        {
            return;
        }
        await _partnerRoutesGate.WaitAsync();
        try
        {
            await V2pnPartners.Instance.LoadAsync();
            var changes = await V2pnPartnerRoutes.SyncAsync(_config, V2pnPartners.Instance.All.Where(p => V2pnPartners.Instance.Edition.IsOpen(p.Id)));
            if (changes.Any && refresh)
            {
                await status.RefreshRoutingsMenu();
                if (changes.ActiveChanged && IsConnected(status))
                {
                    status.ReloadRequested.Publish();
                }
            }
            OfferPartnerRoutes(changes);
            ReloadIfPartnerFilesChanged(status);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn partner routes", ex);
        }
        finally
        {
            _partnerRoutesGate.Release();
        }
    }

    /// <summary>
    /// A partner's pack came back with other reviewed files (one changed after the review, a new certificate covers
    /// another): a running server of a partner reconnects, so a file no longer reviewed stops being used at once.
    /// </summary>
    private void ReloadIfPartnerFilesChanged(StatusBarViewModel status)
    {
        var signature = string.Join(";", V2pnPartners.Instance.All.Select(p =>
            p.Id + ":" + string.Join(",", p.Features.Files.Select(f => $"{f.Name}={f.State}"))));
        var before = _partnerFilesSignature;
        _partnerFilesSignature = signature;
        if (before != null && before != signature && IsConnected(status) && V2pnLocks.Instance.PartnerOf(_barSubId) != null)
        {
            status.ReloadRequested.Publish();
        }
    }

    /// <summary>Says once per partner that its routes are there to pick; nothing is switched on by itself.</summary>
    private void OfferPartnerRoutes(V2pnPartnerRoutes.Changes changes)
    {
        var offered = _config.UiItem.V2pnPartnerRoutesOffered ??= [];
        var fresh = changes.Upserts.Where(u => !offered.Contains(u.Id)).ToList();
        foreach (var item in fresh)
        {
            offered.Add(item.Id);
            var partner = V2pnPartners.Instance.Get(V2pnPartnerRoutes.PartnerOf(item.Id));
            ShowToast(ToastKind.Info, string.Format(L.RoutePartnerOffered, partner?.Name ?? item.Remarks, item.Remarks));
        }
        if (fresh.Count > 0)
        {
            _ = ConfigTextHandler.SaveConfig();
        }
    }

    /// <summary>The partner mode in the menu: the one of the subscription in use, or the one switched on.</summary>
    private void RefreshPartnerRoute(StatusBarViewModel status, RoutingItem? active)
    {
        var inUse = V2pnLocks.Instance.PartnerOf(_barSubId);
        var candidate = inUse != null && status.RoutingItems.Any(t => t.Id == V2pnPartnerRoutes.Id(inUse.Id)) ? V2pnPartnerRoutes.Id(inUse.Id)
            : V2pnPartnerRoutes.IsPartnerRoute(active?.Id) ? active!.Id
            : null;
        var item = status.RoutingItems.FirstOrDefault(t => t.Id == candidate);
        var partner = V2pnPartners.Instance.Get(V2pnPartnerRoutes.PartnerOf(candidate));
        _partnerRouteId = item?.Id;
        btnRoutePartner.IsVisible = item != null;
        if (item == null)
        {
            return;
        }
        var hint = string.Format(L.RoutePartnerHint, partner?.Name ?? item.Remarks);
        txtRoutePartner.Text = item.Remarks;
        txtRoutePartnerHint.Text = partner?.Features.Routing?.Name is { Length: > 0 } presetName ? $"{presetName}. {hint}" : hint;
        var on = active?.Id == item.Id;
        btnRoutePartner.Classes.Set("active", on);
        NameOption(btnRoutePartner, item.Remarks, on);
    }

    private async Task SelectPartnerRouteAsync(string id)
    {
        var vm = ViewModel;
        if (_routeBusy || vm == null)
        {
            return;
        }
        _routeBusy = true;
        string? failure = null;
        try
        {
            // Geo lists of a provider's preset come from the same sources as V2pn's Russian modes.
            var preset = V2pnPartners.Instance.Get(V2pnPartnerRoutes.PartnerOf(id))?.Features.Routing;
            var sourcesChanged = preset?.UsesGeoFiles == true && RoutingPresets.NeedsRussianSources(_config, RoutingPresetKind.RussiaDirect);
            if (sourcesChanged)
            {
                ShowRouteBusy(L.RouteDownloading);
                if (!await vm.UseRussianSourcesAsync())
                {
                    failure = L.RouteDownloadFailed;
                    await vm.StatusBarViewModel.RefreshRoutingsMenu();
                    ShowToast(ToastKind.Error, failure);
                    return;
                }
            }
            await ActivateRoutingAsync(id, sourcesChanged);
            btnRoute.Flyout?.Hide();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn partner routes", ex);
            NoticeManager.Instance.Enqueue(L.RouteFailed);
        }
        finally
        {
            _routeBusy = false;
            ShowRouteBusy(failure, failed: failure != null);
            if (failure != null)
            {
                _ = ClearRouteFailureLaterAsync(failure);
            }
        }
    }

    /// <summary>The person turned a subscription's connection settings on or off: a running server of it reconnects with them.</summary>
    private void OnPartnerTrafficChanged(string subId)
    {
        if (ViewModel?.StatusBarViewModel is { } status && IsConnected(status) && subId == _barSubId)
        {
            status.ReloadRequested.Publish();
        }
    }
}
