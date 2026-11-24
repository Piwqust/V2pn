namespace ServiceLib;

/// <summary>
/// V2pn: the rule of a hub partner's cabinet and support links. THE HUB APPLIES THE SAME ONE (hub/lib/validate.mjs,
/// linkAllowed and partnerSite); keep the two alike, a test compares the suffix lists. A link passes when it is https
/// without a user name or password and its host is
///   1. t.me or telegram.me, or
///   2. one of the partner's domains (without "*.") or a name under it, or
///   3. the site of one of them (a parent domain, never a public suffix) or a name under that site.
/// For sub.nordline.com: sub.nordline.com, my.sub.nordline.com, nordline.com and lk.nordline.com pass; com,
/// nordline.com.evil.com and evil.com do not; for sub.nordline.co.uk, nordline.co.uk passes and co.uk does not.
/// </summary>
public static class V2pnPartnerLinks
{
    /// <summary>Public suffixes of more than one label, as hub/lib/validate.mjs lists them (PUBLIC_SUFFIXES).</summary>
    public static readonly IReadOnlyList<string> PublicSuffixes =
    [
        // Russia and its neighbours
        "com.ru", "net.ru", "org.ru", "pp.ru", "msk.ru", "spb.ru", "msk.su", "spb.su",
        "com.ua", "net.ua", "org.ua", "in.ua", "pp.ua", "kiev.ua", "com.by", "net.by", "org.by", "com.kz", "net.kz", "org.kz",
        "com.ge", "com.az", "com.kg", "com.tj", "com.uz", "co.uz", "com.am",
        // Elsewhere
        "co.uk", "org.uk", "me.uk", "ltd.uk", "plc.uk", "ac.uk", "gov.uk", "com.br", "net.br", "org.br", "com.tr", "net.tr",
        "org.tr", "co.jp", "ne.jp", "or.jp", "com.au", "net.au", "org.au", "co.kr", "or.kr", "co.in", "net.in", "org.in",
        "com.cn", "net.cn", "org.cn", "com.hk", "com.tw", "com.sg", "co.nz", "co.za", "com.mx", "com.ar", "co.id", "com.my",
        "com.vn", "co.il", "com.pl", "co.th", "com.ph", "com.eg", "com.sa", "ae.org", "eu.org", "us.com", "uk.com", "eu.com",
        // Hosting and dynamic DNS: every customer gets a name under the service's
        "github.io", "gitlab.io", "pages.dev", "workers.dev", "r2.dev", "vercel.app", "netlify.app", "herokuapp.com",
        "onrender.com", "fly.dev", "deno.dev", "web.app", "firebaseapp.com", "appspot.com", "azurewebsites.net",
        "cloudfront.net", "amazonaws.com", "s3.amazonaws.com", "trycloudflare.com", "ngrok.io", "ngrok-free.app",
        "duckdns.org", "ddns.net", "no-ip.org", "hopto.org", "sslip.io", "nip.io", "glitch.me", "replit.app", "blogspot.com",
    ];

    private static readonly HashSet<string> Suffixes = new(PublicSuffixes, StringComparer.Ordinal);
    private static readonly string[] TelegramHosts = ["t.me", "telegram.me"];

    /// <summary>
    /// The partner's site: going up from <paramref name="domain"/> while the name has at least two labels and is not a
    /// public suffix, the last name reached; null when the domain itself is one.
    /// </summary>
    public static string? Site(string domain)
    {
        var labels = domain.StartsWith("*.", StringComparison.Ordinal) ? domain[2..].ToLowerInvariant().Split('.') : domain.ToLowerInvariant().Split('.');
        string? site = null;
        for (var i = 0; labels.Length - i >= 2; i++)
        {
            var name = string.Join('.', labels[i..]);
            if (Suffixes.Contains(name))
            {
                break;
            }
            site = name;
        }
        return site;
    }

    /// <summary>Whether a hub partner with <paramref name="domains"/> may show <paramref name="url"/> as its cabinet or support link.</summary>
    public static bool Allowed(string? url, IEnumerable<string> domains)
    {
        if (Utils.TryUri(url?.Trim() ?? string.Empty) is not { IsAbsoluteUri: true } uri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0)
        {
            return false;
        }
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (TelegramHosts.Contains(host))
        {
            return true;
        }
        static bool Under(string name, string root) => name == root || name.EndsWith("." + root, StringComparison.Ordinal);
        return domains.Any(d =>
        {
            var bare = (d.StartsWith("*.", StringComparison.Ordinal) ? d[2..] : d).ToLowerInvariant();
            return Under(host, bare) || (Site(bare) is { } site && Under(host, site));
        });
    }
}
