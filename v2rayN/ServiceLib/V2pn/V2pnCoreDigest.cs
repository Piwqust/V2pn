namespace ServiceLib;

/// <summary>
/// V2pn: a core updated from the Updates window (Xray, sing-box, mihomo) is checked against the SHA-256 GitHub keeps for
/// that release file (the "digest" of the asset in GitHub's API) before it replaces the one in use. The archive used to
/// be unpacked as it came: a download cut short, or changed on the way by a mirror or a proxy, became the program that
/// carries all traffic (and runs as administrator or root in VPN mode). When GitHub's API does not answer, the core is
/// taken as before and the log says it was not checked.
/// </summary>
public static class V2pnCoreDigest
{
    private static readonly Regex AssetUrl = new(@"^https://github\.com/([^/]+)/([^/]+)/releases/download/([^/]+)/([^/?#]+)$", RegexOptions.IgnoreCase);

    /// <summary>The API address of the release a download URL belongs to, and the file's name; null when it is not a GitHub release file.</summary>
    public static (string ApiUrl, string Asset)? Locate(string? url)
    {
        var m = AssetUrl.Match(url?.Trim() ?? string.Empty);
        return m.Success
            ? ($"https://api.github.com/repos/{m.Groups[1].Value}/{m.Groups[2].Value}/releases/tags/{Uri.EscapeDataString(Uri.UnescapeDataString(m.Groups[3].Value))}",
               Uri.UnescapeDataString(m.Groups[4].Value))
            : null;
    }

    /// <summary>The SHA-256 GitHub gives for <paramref name="asset"/> in a release's API answer, lower case; null when absent.</summary>
    public static string? DigestIn(string? releaseJson, string asset)
    {
        try
        {
            if (JsonNode.Parse(releaseJson ?? string.Empty)?["assets"] is not JsonArray assets)
            {
                return null;
            }
            var digest = assets.OfType<JsonObject>()
                .FirstOrDefault(a => a["name"]?.GetValue<string>() == asset)?["digest"]?.GetValue<string>();
            return digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                   && d[7..] is { Length: 64 } hex && hex.All(Uri.IsHexDigit)
                ? hex.ToLowerInvariant()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The SHA-256 GitHub keeps for the file at <paramref name="url"/>, asked directly and then through the connection; null when unknown.</summary>
    public static async Task<string?> ExpectedAsync(string? url, CancellationToken cancellationToken)
    {
        if (Locate(url) is not { } where)
        {
            return null;
        }
        var proxy = await V2pnHttp.LocalProxyAsync(cancellationToken);
        foreach (var route in proxy == null ? new IWebProxy?[] { null } : [null, proxy])
        {
            var answer = await V2pnHttp.GetAsync(where.ApiUrl, route, 4 * 1024 * 1024, cancellationToken, limits: V2pnHttp.Quick);
            if (answer.Ok && DigestIn(Encoding.UTF8.GetString(answer.Body!), where.Asset) is { } digest)
            {
                return digest;
            }
        }
        return null;
    }

    /// <summary>Whether the downloaded file has the expected hash; true when none is known.</summary>
    public static bool Matches(string path, string? expected) =>
        expected == null || (File.Exists(path) && V2pnGeoMirror.Sha256(path) == expected);
}
