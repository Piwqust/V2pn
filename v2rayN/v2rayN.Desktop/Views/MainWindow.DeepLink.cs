using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

// v2pn:// links from a provider's cabinet, bot or page (V2pnDeepLink): asked about first, then added like a pasted
// subscription. A link with partner=<id> also makes the person come from that provider, with its tab, when the signed
// index lists it and the subscription is on its domains (V2pnPartners.ComeFromLinkAsync); otherwise it is just a
// subscription.
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
        await AddPresetAsync();
    }

    /// <summary>The subscription a special build carries (V2pnPreset), added once without asking.</summary>
    private async Task AddPresetAsync()
    {
        var links = V2pnPreset.Pending();
        if (links.Count == 0)
        {
            return;
        }
        await AddSubscriptionLinksAsync(links);
        // Only what made it in counts as done; a link that failed is tried again at the next start.
        var subs = await ConfigDbQuery.SubItems() ?? [];
        V2pnPreset.MarkApplied(links.Where(l => subs.Any(s => string.Equals(s.Url?.Trim(), l, StringComparison.Ordinal))));
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
        foreach (var link in V2pnDeepLink.TakeStashed())
        {
            await AskToAddLinkAsync(link);
        }
    }

    private async Task AskToAddLinkAsync(V2pnAddLink link)
    {
        // A second click while the question is open would stack a second dialog over the first: it waits its turn.
        if (_askingAboutLink)
        {
            if (!_waitingLinks.Any(l => l.SubscriptionUrl == link.SubscriptionUrl) && _waitingLinks.Count < 8)
            {
                _waitingLinks.Enqueue(link);
            }
            return;
        }
        _askingAboutLink = true;
        try
        {
            // A link to a subscription V2pn already has (a second click in the cabinet): nothing to ask.
            var subs = await ConfigDbQuery.SubItems() ?? [];
            if (subs.FirstOrDefault(s => string.Equals(s.Url?.Trim(), link.SubscriptionUrl.Trim(), StringComparison.Ordinal)) is { } known)
            {
                ShowToast(ToastKind.Info, string.Format(L.LinkAlreadyAdded, ViewModels.SideSubRow.NameOf(known)));
                // The person added it before and now comes by its provider's own link: that is coming from it.
                await ComeFromProviderLinkAsync(link);
                return;
            }
            var host = Utils.TryUri(link.SubscriptionUrl)?.Host ?? link.SubscriptionUrl;
            // A provider V2pn already vouches for says so; an unknown one gets the general words.
            var partner = V2pnPartners.Instance.ListingFor(link.SubscriptionUrl)
                          ?? V2pnPartners.Instance.ListingForLink(link.Partner, link.SubscriptionUrl);
            var body = partner != null ? string.Format(L.LinkAddBodyPartner, partner.Name) : L.LinkAddBody;
            // A subscription over plain http can be read and changed on the way: the person decides knowing it.
            if (link.SubscriptionUrl.StartsWith(Global.HttpProtocol, StringComparison.OrdinalIgnoreCase))
            {
                body += "\n\n" + L.LinkAddPlainHttp;
            }
            var card = new DialogCard(link.Name ?? host,
                partner != null ? string.Format(L.LinkAddPartnerCard, host) : host,
                partner != null ? "SemiIconShieldStroked" : "SemiIconLink");
            var answer = await UI.ShowChoice(L.LinkAddTitle, L.LinkAddAsk + "\n\n" + body, L.LinkAddYes, L.LinkAddNo,
                DialogTone.Info, "SemiIconDownloadStroked", card);
            if (answer == ButtonResult.Yes)
            {
                await AddSubscriptionLinksAsync([link.SubscriptionUrl]);
                await ComeFromProviderLinkAsync(link);
            }
        }
        finally
        {
            _askingAboutLink = false;
        }
        if (_waitingLinks.TryDequeue(out var next))
        {
            await AskToAddLinkAsync(next);
        }
    }

    private readonly Queue<V2pnAddLink> _waitingLinks = new();

    /// <summary>
    /// A link with partner=&lt;id&gt;, after its subscription is here: when V2pn's signed index says the subscription is
    /// that provider's, the person came from it and its tab opens (as after a sign-in by code; the pack comes with the
    /// refresh). Anything else stays just a subscription, with nothing said.
    /// </summary>
    private async Task ComeFromProviderLinkAsync(V2pnAddLink link)
    {
        if (link.Partner == null)
        {
            return;
        }
        try
        {
            if (await Task.Run(() => V2pnPartners.Instance.ComeFromLinkAsync(link.Partner, link.SubscriptionUrl)) is not { } listing)
            {
                return;
            }
            V2pnEdition.Instance.Open(listing.Id);
            _ = Task.Run(() => V2pnPartners.Instance.RefreshAsync());
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn provider link", ex);
        }
    }
}
