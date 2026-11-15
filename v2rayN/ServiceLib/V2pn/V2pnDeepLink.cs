using Microsoft.Win32;

namespace ServiceLib;

/// <summary>A subscription a website or a bot asked V2pn to add, with the name it suggested.</summary>
public sealed record V2pnAddLink(string SubscriptionUrl, string? Name, string Scheme);

/// <summary>
/// v2pn://add?url=…&amp;name=… from a provider's cabinet or bot, and the add links other clients use
/// (happ://add/…, v2raytun://import/…, hiddify://import/…), so a provider's existing buttons reach V2pn
/// too. A link is never acted on silently: the window asks before adding.
/// </summary>
public static class V2pnDeepLink
{
    public const string Scheme = "v2pn";
    public static readonly IReadOnlyList<string> Schemes = [Scheme, "happ", "v2raytun", "hiddify"];

    private const int MaxLink = 4096;
    private const string StashFile = "v2pn-link.txt";

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
        switch (scheme)
        {
            case Scheme:
                // v2pn://add?url=<encoded>&name=<encoded>; "add" comes as the host.
                if (!uri.Host.Equals("add", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                var query = Utils.ParseQueryString(uri.Query);
                url = query["url"];
                name = query["name"];
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
        return new V2pnAddLink(url!, name.IsNullOrEmpty() ? null : name, scheme);
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
