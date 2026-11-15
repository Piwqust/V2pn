using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

// v2pn:// links from a provider's cabinet or bot (V2pnDeepLink): asked about first, then added like a pasted subscription.
public partial class MainWindow
{
    private bool _askingAboutLink;

    /// <summary>The link V2pn was started with, once the window is up.</summary>
    private async Task HandleStartupLinkAsync()
    {
        if (Program.StartupLink is { } link)
        {
            await AskToAddLinkAsync(link);
        }
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
            var host = Utils.TryUri(link.SubscriptionUrl)?.Host ?? link.SubscriptionUrl;
            var answer = await UI.ShowChoice(L.LinkAddTitle, string.Format(L.LinkAddBody, link.Name ?? host, host), L.LinkAddYes, L.LinkAddNo);
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
