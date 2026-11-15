using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

// v2pn:// links from a provider's cabinet or bot (V2pnDeepLink): asked about first, then added like a pasted subscription.
public partial class MainWindow
{
    private bool _askingAboutLink;
    // A link that came before the window was up (macOS starts the app for a click and then hands it the link).
    private V2pnAddLink? _queuedLink;
    private bool _linksReady;

    /// <summary>The link V2pn was started with, once the window is up.</summary>
    private async Task HandleStartupLinkAsync()
    {
        _linksReady = true;
        var link = Program.StartupLink ?? _queuedLink;
        _queuedLink = null;
        if (link != null)
        {
            await AskToAddLinkAsync(link);
        }
    }

    /// <summary>A link from the system (macOS's open-URL event): asked about now, or once the window is up.</summary>
    public void QueueAddLink(V2pnAddLink link)
    {
        if (!_linksReady)
        {
            _queuedLink = link;
            return;
        }
        ShowHideWindow(true);
        _ = AskToAddLinkAsync(link);
    }

    /// <summary>A link a second start handed over (a click on v2pn:// while V2pn already runs).</summary>
    private async Task HandleStashedLinkAsync()
    {
        if (V2pnDeepLink.TakeStashed() is { } link)
        {
            await AskToAddLinkAsync(link);
        }
    }

    private async Task AskToAddLinkAsync(V2pnAddLink link)
    {
        // A second click while the question is open would stack a second dialog over the first.
        if (_askingAboutLink)
        {
            return;
        }
        _askingAboutLink = true;
        try
        {
            // A link to a subscription V2pn already has (a second click in the cabinet): nothing to ask.
            var subs = await AppManager.Instance.SubItems() ?? [];
            if (subs.FirstOrDefault(s => string.Equals(s.Url?.Trim(), link.SubscriptionUrl.Trim(), StringComparison.Ordinal)) is { } known)
            {
                ShowToast(ToastKind.Info, string.Format(L.LinkAlreadyAdded, ViewModels.SideSubRow.NameOf(known)));
                return;
            }
            var host = Utils.TryUri(link.SubscriptionUrl)?.Host ?? link.SubscriptionUrl;
            // A provider V2pn already vouches for says so; an unknown one gets the general words.
            var partner = V2pnPartners.Instance.All.FirstOrDefault(p => V2pnPartnerTrust.DomainAllowed(p.Cert, link.SubscriptionUrl));
            var body = partner != null
                ? string.Format(L.LinkAddBodyPartner, link.Name ?? host, host, partner.Name)
                : string.Format(L.LinkAddBody, link.Name ?? host, host);
            var answer = await UI.ShowChoice(L.LinkAddTitle, body, L.LinkAddYes, L.LinkAddNo);
            if (answer == ButtonResult.Yes)
            {
                await AddSubscriptionLinksAsync([link.SubscriptionUrl]);
            }
        }
        finally
        {
            _askingAboutLink = false;
        }
    }
}
