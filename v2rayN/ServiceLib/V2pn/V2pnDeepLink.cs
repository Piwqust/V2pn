using Microsoft.Win32;

namespace ServiceLib;

/// <summary>
/// A subscription a website or a bot asked V2pn to add, with the name it suggested and the provider whose link it is
/// (<paramref name="Partner"/>: its id when the link says so; V2pnPartners.ComeFromLinkAsync checks it against the signed
/// index before anything comes of it).
/// </summary>
public sealed record V2pnAddLink(string SubscriptionUrl, string? Name, string Scheme, string? Partner = null);

/// <summary>
/// v2pn://add?url=…&amp;name=…[&amp;partner=&lt;id&gt;] from a provider's cabinet, bot or page, and the add links other
/// clients use (happ://add/…, v2raytun://import/…, hiddify://import/…), so a provider's existing buttons reach V2pn
/// too. Also a provider's page on V2pn's site with its subscription after "#sub=" (https://v2pn.angleford.co/p/&lt;id&gt;/#sub=…,
/// what its "Open in V2pn" button turns into v2pn://add?…&amp;partner=&lt;id&gt;), when the person pastes that address.
/// A link is never acted on silently: the window asks before adding.
/// </summary>
public static class V2pnDeepLink
{
    public const string Scheme = "v2pn";
    public static readonly IReadOnlyList<string> Schemes = [Scheme, "happ", "v2raytun", "hiddify"];

    private const int MaxLink = 4096;
    private const string StashFile = "v2pn-link.txt";

    /// <summary>
    /// Where providers' pages live: &lt;origin&gt;/p/&lt;id&gt;/ on the site of each partner source laid out as the V2pn Hub
    /// publishes (&lt;origin&gt;/partners), which writes the pages beside it. Overridable for tests.
    /// </summary>
    public static IReadOnlyList<string> PageOrigins { get; set; } = V2pnInfo.PartnerSources
        .Where(s => s.EndsWith("/partners", StringComparison.Ordinal)).Select(s => s[..^"/partners".Length]).ToList();

    private static readonly Regex PagePath = new("^/p/([a-z0-9][a-z0-9-]{1,31})/?$", RegexOptions.CultureInvariant);

    public static V2pnAddLink? Parse(string? text)
    {
        text = text?.Trim();
        if (text.IsNullOrEmpty() || text.Length > MaxLink || !Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }
        var scheme = uri.Scheme.ToLowerInvariant();
        string? url = null;
        string? name = null;
        string? partner = null;
        switch (scheme)
        {
            case Scheme:
                // v2pn://add?url=<encoded>&name=<encoded>&partner=<id>; "add" comes as the host.
                if (!uri.Host.Equals("add", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                var query = Utils.ParseQueryString(uri.Query);
                url = query["url"];
                name = query["name"];
                partner = query["partner"];
                break;
            case "https":
                // A provider's page: https://<site>/p/<id>/#sub=<subscription>, its link raw or encoded, as the page reads it.
                var origin = uri.GetLeftPart(UriPartial.Authority);
                var path = PagePath.Match(uri.AbsolutePath);
                if (!PageOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase)) || !path.Success
                    || uri.Query.Length > 0 || !uri.Fragment.StartsWith("#sub=", StringComparison.Ordinal))
                {
                    return null;
                }
                url = uri.Fragment["#sub=".Length..];
                if (Regex.IsMatch(url, "%[0-9A-Fa-f]{2}"))
                {
                    url = Uri.UnescapeDataString(url);
                }
                // The page offers only https links without a user name; so does V2pn.
                if (Utils.TryUri(url) is not { Scheme: "https", UserInfo.Length: 0 })
                {
                    return null;
                }
                partner = path.Groups[1].Value;
                scheme = "page";
                break;
            case "happ":
            case "v2raytun":
            case "hiddify":
                // <scheme>://add/<url> or <scheme>://import/<url>[#name]: everything after the verb is the link.
                var verb = scheme == "happ" ? "add/" : "import/";
                var start = text.IndexOf("://", StringComparison.Ordinal) + 3;
                var rest = text[start..];
                if (!rest.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                url = rest[verb.Length..];
                var hash = url.IndexOf('#');
                if (hash >= 0)
                {
                    name = Uri.UnescapeDataString(url[(hash + 1)..]);
                    url = url[..hash];
                }
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = Uri.UnescapeDataString(url);
                }
                break;
            default:
                return null;
        }
        if (!V2pnSubs.IsHttpLink(url))
        {
            return null;
        }
        name = name?.Trim();
        if (name is { Length: > 60 } || name?.Any(char.IsControl) == true)
        {
            name = null;
        }
        partner = partner?.Trim();
        return new V2pnAddLink(url!, name.IsNullOrEmpty() ? null : name, scheme, V2pnPartnerTrust.ValidId(partner) ? partner : null);
    }

    /// <summary>The first argument that is a link V2pn can add.</summary>
    public static V2pnAddLink? FromArgs(IEnumerable<string>? args) =>
        (args ?? []).Select(Parse).FirstOrDefault(l => l != null);

    /// <summary>
    /// Leaves a link for the copy of V2pn already running: a second start (a click on a v2pn:// link) writes it
    /// here and wakes the first, which takes it with <see cref="TakeStashed"/>.
    /// </summary>
    public static void Stash(string link)
    {
        try
        {
            File.WriteAllText(Utils.GetTempPath(StashFile), link);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnDeepLink), ex);
        }
    }

    public static V2pnAddLink? TakeStashed()
    {
        try
        {
            var path = Utils.GetTempPath(StashFile);
            if (!File.Exists(path))
            {
                return null;
            }
            var text = File.ReadAllText(path);
            File.Delete(path);
            return Parse(text);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnDeepLink), ex);
            return null;
        }
    }

    /// <summary>
    /// Registers v2pn:// for the current user on Windows, pointing at this copy of V2pn. Only a release does
    /// it: a development build in a sandbox must not take the links away from the installed app.
    /// </summary>
    public static void RegisterScheme()
    {
        if (!Utils.IsWindows() || !V2pnInfo.IsRelease)
        {
            return;
        }
        try
        {
            var command = $"\"{Utils.GetExePath()}\" \"%1\"";
            const string root = $@"Software\Classes\{Scheme}";
            using var existing = Registry.CurrentUser.OpenSubKey($@"{root}\shell\open\command", false);
            if (existing?.GetValue(null) as string == command)
            {
                return;
            }
            using var key = Registry.CurrentUser.CreateSubKey(root);
            key.SetValue(null, "URL:V2pn");
            key.SetValue("URL Protocol", string.Empty);
            using var open = Registry.CurrentUser.CreateSubKey($@"{root}\shell\open\command");
            open.SetValue(null, command);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnDeepLink), ex);
        }
    }
}
