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
        btnSupport.IsVisible = info?.SupportUrl.IsNotEmpty() == true;
        btnWebPage.IsVisible = info?.WebPageUrl.IsNotEmpty() == true;
        rowLinks.IsVisible = btnSupport.IsVisible || btnWebPage.IsVisible;
        ShowPartner(source, info);
        var rows = new Border[] { rowTraffic, rowExpiry, rowAnnounce, rowLinks, rowPartner };
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

    /// <summary>
    /// Whether the provider dresses V2pn in its own look: confirmed by V2pn (and until when), still being checked, or
    /// refused, so a look that never shows up is never a mystery.
    /// </summary>
    private void ShowPartner(SubItem? source, SubInfoItem? info)
    {
        rowPartner.IsVisible = info?.PartnerUrl.IsNotEmpty() == true;
        if (!rowPartner.IsVisible || source == null)
        {
            return;
        }
        var partner = V2pnPartners.Instance.ForSource(info!.PartnerUrl, source.Url);
        var error = V2pnPartners.Instance.ErrorFor(info.PartnerUrl);
        btnPartner.IsVisible = partner?.HasEdition == true && Editions.Partner?.Id != partner.Id;
        ToolTip.SetTip(txtPartner, error);
        txtPartner.Text = partner != null
            ? string.Format(L.SubPartnerConfirmed, partner.Name, partner.Cert.Expires.ToDateTime(TimeOnly.MinValue).ToString("d MMMM yyyy", CultureInfo.CurrentUICulture))
              + (Editions.Partner?.Id == partner.Id ? L.SubPartnerIsOpen : string.Empty)
            : error != null ? L.SubPartnerRefused : L.SubPartnerChecking;
        txtPartner.Classes.Set("bad", partner == null && error != null);
        btnPartner.Tag = partner?.Id;
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
