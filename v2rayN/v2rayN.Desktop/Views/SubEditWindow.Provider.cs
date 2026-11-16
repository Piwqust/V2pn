using Avalonia.Automation;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

// What the subscription's panel reported (traffic, expiry, announcement, links) and the self-filling name.
public partial class SubEditWindow
{
    private void ShowProviderInfo()
    {
        var source = ViewModel?.SelectedSource;
        var info = source == null || _isNew ? null : SubInfoManager.Instance.Get(source.Id);
        if (info?.PanelName.IsNotEmpty() == true)
        {
            txtRemarks.PlaceholderText = info.PanelName;
        }
        if (info?.UpdateIntervalHours > 0)
        {
            txtIntervalHint.Text = $"{L.HintSubInterval} {string.Format(L.SubPanelInterval, info.UpdateIntervalHours)}";
        }

        var usage = SubUsage.From(info);
        rowTraffic.IsVisible = usage?.HasTraffic == true;
        rowExpiry.IsVisible = usage?.HasExpiry == true;
        rowAnnounce.IsVisible = info?.Announce.IsNotEmpty() == true;
        btnAnnounceMore.IsVisible = info?.AnnounceUrl.IsNotEmpty() == true;
        btnAnnounceMore.Tag = info?.AnnounceUrl;
        ToolTip.SetTip(btnAnnounceMore, info?.AnnounceUrl);
        viewBanner.Banner = V2pnSubDefaults.Banner(info, DateTimeOffset.Now);
        rowBanner.IsVisible = viewBanner.Banner != null;
        ShowProviderDefaults(info);
        btnSupport.IsVisible = info?.SupportUrl.IsNotEmpty() == true;
        btnWebPage.IsVisible = info?.WebPageUrl.IsNotEmpty() == true;
        rowLinks.IsVisible = btnSupport.IsVisible || btnWebPage.IsVisible;
        ShowPartner(source, info);
        ShowLock(source);
        var rows = new Border[] { rowLock, rowBanner, rowTraffic, rowExpiry, rowAnnounce, rowLinks, rowStartConnect, rowOrder, rowOnStart, rowPartner, rowReviewed, rowPartnerConn };
        if (info == null || !rows.Any(r => r.IsVisible))
        {
            grpProvider.IsVisible = false;
            return;
        }

        if (usage != null)
        {
            txtTraffic.Text = usage.TrafficText;
            txtTraffic.Classes.Set("warn", usage.TrafficWarn);
            txtTraffic.Classes.Set("bad", usage.TrafficBad);
            barTraffic.IsVisible = usage.ShowBar;
            barTraffic.Value = usage.Fraction;
            barTraffic.Classes.Set("warn", usage.TrafficWarn);
            barTraffic.Classes.Set("bad", usage.TrafficBad);
            txtTrafficHint.Text = usage.TrafficHint;
            txtExpiry.Text = usage.ExpiryLong;
            txtExpiry.Classes.Set("warn", usage.ExpiryWarn);
            txtExpiry.Classes.Set("bad", usage.ExpiryBad);
        }
        txtAnnounce.Text = info.Announce;
        btnSupport.Tag = info.SupportUrl;
        ToolTip.SetTip(btnSupport, info.SupportUrl);
        btnWebPage.Tag = info.WebPageUrl;
        ToolTip.SetTip(btnWebPage, info.WebPageUrl);
        txtInfoAsOf.Text = info.ReceivedTime > 0
            ? string.Format(L.SubInfoAsOf, Fmt.Ago(DateTimeOffset.FromUnixTimeSeconds(info.ReceivedTime).LocalDateTime))
            : string.Empty;

        // Rows hidden above the first visible one would leave its top divider hanging.
        rows.First(r => r.IsVisible).BorderThickness = new Thickness(0);
        grpProvider.IsVisible = true;
    }

    // Order choices, in the combo box's order; null follows the provider.
    private static readonly string?[] OrderChoices = [null, SubPanelHeaders.SortPing, SubPanelHeaders.SortAlphabet, SubPanelHeaders.SortWithout];

    private static string OrderName(string? sort) => sort switch
    {
        SubPanelHeaders.SortAlphabet => L.SubOrderName,
        SubPanelHeaders.SortWithout => L.SubOrderAsIs,
        _ => L.SubOrderPing,
    };

    /// <summary>
    /// What the provider advises (connecting at start, the order of its servers, updating and testing at start), each
    /// shown only when the provider set it, with the person's own choice next to it.
    /// </summary>
    private void ShowProviderDefaults(SubInfoItem? info)
    {
        rowStartConnect.IsVisible = info?.Autoconnect == true;
        if (info?.Autoconnect == true)
        {
            cmbStartConnect.ItemsSource = new[] { L.SubStartConnectProvider, L.SubStartConnectOwn };
            cmbStartConnect.SelectedIndex = info.OwnAutoconnect ? 1 : 0;
            txtStartConnectHint.Text = V2pnSubDefaults.AutoconnectType(info) switch
            {
                SubPanelHeaders.ConnectLowestDelay => L.SubStartConnectLowestDelay,
                SubPanelHeaders.ConnectRandom => L.SubStartConnectRandom,
                _ => L.SubStartConnectLastUsed,
            };
        }

        rowOrder.IsVisible = info?.SortType.IsNotEmpty() == true;
        if (info?.SortType.IsNotEmpty() == true)
        {
            cmbOrder.ItemsSource = new[] { L.SubOrderProvider, L.SubOrderPing, L.SubOrderName, L.SubOrderAsIs };
            cmbOrder.SelectedIndex = Math.Max(0, Array.IndexOf(OrderChoices, SubPanelHeaders.ParseSortType(info.OwnSort)));
            txtOrderHint.Text = string.Format(L.SubOrderAdvice, OrderName(info.SortType).ToLower(CultureInfo.CurrentUICulture));
        }

        var update = V2pnSubDefaults.UpdatesOnStart(info);
        var ping = V2pnSubDefaults.PingsOnStart(info);
        rowOnStart.IsVisible = update || ping;
        txtOnStart.Text = update && ping ? L.SubOnStartBoth : update ? L.SubOnStartUpdate : L.SubOnStartPing;
    }

    /// <summary>
    /// V2pn: what the partner closed (V2pnLocks): the link is read-only or hidden, the provider's defaults and the
    /// fields that change how the link is fetched cannot be changed. Everything else stays the person's.
    /// </summary>
    private void ShowLock(SubItem? source)
    {
        var locked = source == null || _isNew ? V2pnLockState.None : V2pnLocks.Instance.ForSub(source.Id);
        rowLock.IsVisible = locked.Any;
        txtLock.Text = locked.Settings ? string.Format(L.LockedBy, locked.PartnerName) : string.Format(L.LockedUrl, locked.PartnerName);
        txtLockHint.IsVisible = locked.Settings;
        txtUrl.IsReadOnly = locked.UrlLocked;
        txtUrl.IsVisible = !locked.HideUrl;
        txtUrlHidden.IsVisible = locked.HideUrl;
        txtUrlHidden.Text = string.Format(L.LockedUrl, locked.PartnerName);
        foreach (var control in new Control[] { txtUserAgent, txtRequestHeaders, cmbConvertTarget, txtMoreUrl })
        {
            control.IsEnabled = !locked.Settings;
        }
        if (locked.Settings)
        {
            // The provider's advice holds while its settings are closed (V2pnSubDefaults with locked: true).
            cmbStartConnect.SelectedIndex = 0;
            cmbStartConnect.IsEnabled = false;
            cmbOrder.SelectedIndex = 0;
            cmbOrder.IsEnabled = false;
            ToolTip.SetTip(cmbStartConnect, string.Format(L.LockedChoice, locked.PartnerName));
            ToolTip.SetTip(cmbOrder, string.Format(L.LockedChoice, locked.PartnerName));
        }
    }

    /// <summary>Keeps the person's choices next to the provider's defaults; called on Save.</summary>
    private void SaveProviderChoices()
    {
        var source = ViewModel?.SelectedSource;
        if (_isNew || source?.Id.IsNotEmpty() != true || SubInfoManager.Instance.Get(source.Id) is not { } info
            || V2pnLocks.Instance.ForSub(source.Id).Settings)
        {
            return;
        }
        var ownAutoconnect = rowStartConnect.IsVisible ? cmbStartConnect.SelectedIndex == 1 : info.OwnAutoconnect;
        var ownSort = rowOrder.IsVisible ? OrderChoices.ElementAtOrDefault(cmbOrder.SelectedIndex) : info.OwnSort;
        _ = SubInfoManager.Instance.SetOwnChoicesAsync(source.Id, ownAutoconnect, ownSort);
        if (rowPartnerConn.IsVisible)
        {
            _ = V2pnPartnerTraffics.SetOffAsync(source.Id, togPartnerConn.IsChecked != true);
        }
    }

    /// <summary>
    /// Whether the provider dresses V2pn in its own look: confirmed by V2pn (and until when), still being checked, or
    /// refused, so a look that never shows up is never a mystery.
    /// </summary>
    private void ShowPartner(SubItem? source, SubInfoItem? info)
    {
        var listing = V2pnPartners.Instance.ListingFor(source?.Url);
        rowPartner.IsVisible = listing != null;
        if (listing == null || source == null)
        {
            return;
        }
        var partner = V2pnPartners.Instance.ForSubscription(source);
        var error = V2pnPartners.Instance.ErrorFor(listing.Id);
        ToolTip.SetTip(txtPartner, error);
        txtPartner.Text = partner != null
            ? string.Format(L.SubPartnerConfirmed, partner.Name, partner.Listing.Until.ToDateTime(TimeOnly.MinValue).ToString("d MMMM yyyy", CultureInfo.CurrentUICulture))
            : string.Format(error != null ? L.SubPartnerRefused : L.SubPartnerChecking, listing.Name);
        txtPartner.Classes.Set("bad", partner == null && error != null);
        txtPartnerMay.IsVisible = partner != null;
        if (partner != null)
        {
            txtPartnerMay.Text = PartnerMay(partner);
        }
        ShowReviewed(partner, info);
    }

    /// <summary>
    /// The partner's reviewed files, one line each: in use, available, turned off by the person, or not used and why
    /// ("Kestrel routes changed after the review and are not used"), and the switch for its connection settings.
    /// </summary>
    private void ShowReviewed(V2pnPartner? partner, SubInfoItem? info)
    {
        var files = partner?.Features.Files ?? [];
        rowReviewed.IsVisible = files.Count > 0;
        var off = info?.PartnerConnectionOff == true;
        if (partner != null && files.Count > 0)
        {
            _ = FillReviewedAsync(partner, files, off);
        }

        rowPartnerConn.IsVisible = partner?.Features.Transport != null || partner?.Features.Core != null;
        if (rowPartnerConn.IsVisible)
        {
            txtPartnerConn.Text = string.Format(L.ReviewedTransport, partner!.Name);
            txtPartnerConnHint.Text = string.Format(L.SubPartnerConnHint, partner.Name);
            togPartnerConn.IsChecked = !off;
            AutomationProperties.SetName(togPartnerConn, txtPartnerConn.Text);
        }
    }

    private async Task FillReviewedAsync(V2pnPartner partner, IReadOnlyList<V2pnReviewedFile> files, bool off)
    {
        // Routes are "in use" only while they are the active mode.
        var activeRoute = (await ConfigHandler.GetDefaultRouting(AppManager.Instance.Config))?.Id;
        panelReviewed.Children.Clear();
        foreach (var file in files)
        {
            var feature = file.Name switch
            {
                V2pnReviewedFiles.Routing => string.Format(L.ReviewedRouting, partner.Name),
                V2pnReviewedFiles.Transport => string.Format(L.ReviewedTransport, partner.Name),
                _ => string.Format(L.ReviewedCore, partner.Name),
            };
            var (format, bad) = file.State switch
            {
                V2pnReviewState.Changed => (L.ReviewedChanged, true),
                V2pnReviewState.NotReviewed => (L.ReviewedNot, true),
                V2pnReviewState.Invalid => (L.ReviewedInvalid, true),
                _ when file.Name == V2pnReviewedFiles.Routing =>
                    (activeRoute == V2pnPartnerRoutes.Id(partner.Id) ? L.ReviewedUsed : L.ReviewedPickable, false),
                _ => (off ? L.ReviewedOff : L.ReviewedUsed, false),
            };
            var line = new TextBlock { Text = string.Format(format, feature), TextWrapping = TextWrapping.Wrap };
            line.Classes.Add("srowhint");
            line.Classes.Set("warn", bad);
            ToolTip.SetTip(line, file.Problem);
            panelReviewed.Children.Add(line);
        }
    }

    /// <summary>"Kestrel may: its look, plan details, its own buttons, closed settings." from the listing's grants.</summary>
    private static string PartnerMay(V2pnPartner partner)
    {
        var may = new List<string>();
        if (partner.Listing.Allows(V2pnPartnerGrants.Edition))
        {
            may.Add(L.GrantEdition);
        }
        if (partner.Listing.Allows(V2pnPartnerGrants.Account))
        {
            may.Add(L.GrantAccount);
        }
        if (partner.Listing.Allows(V2pnPartnerGrants.Actions))
        {
            may.Add(L.GrantActions);
        }
        if (partner.Listing.Allows(V2pnPartnerGrants.Lock))
        {
            may.Add(L.GrantLock);
        }
        if (partner.Features.Routing != null)
        {
            may.Add(L.GrantRouting);
        }
        if (partner.Features.Transport != null)
        {
            may.Add(L.GrantTransport);
        }
        if (partner.Features.Core != null)
        {
            may.Add(L.GrantCore);
        }
        return may.Count == 0 ? string.Format(L.PartnerMayLittle, partner.Name) : string.Format(L.PartnerMay, partner.Name, string.Join(", ", may));
    }

    private void FillEmptyName()
    {
        var source = ViewModel?.SelectedSource;
        if (source == null || !string.IsNullOrWhiteSpace(txtRemarks.Text))
        {
            return;
        }
        // The panel's name or the link's host; either counts as the panel's, so the next update renames it.
        source.Remarks = SubInfoManager.Instance.SuggestName(source) ?? string.Empty;
    }

    private void AnnounceIfAdded()
    {
        var source = ViewModel?.SelectedSource;
        var url = source?.Url?.Trim();
        // The id is assigned when the subscription is saved; Cancel leaves it empty.
        if (!_isNew || source == null || source.Id.IsNullOrEmpty()
            || url.IsNullOrEmpty() || !(url.StartsWith(Global.HttpsProtocol) || url.StartsWith(Global.HttpProtocol)))
        {
            return;
        }
        SubscriptionAdded?.Invoke(source.Id);
    }
}
