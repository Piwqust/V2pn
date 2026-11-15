namespace ServiceLib;

/// <summary>
/// cert.json of a partner: the owner of V2pn's partner root key says which provider may dress V2pn,
/// from which domains, with which key, until when. Signed (cert.json.sig) with a root key whose public
/// half is built into the app (<see cref="V2pnInfo.PartnerKeys"/>).
/// </summary>
public sealed record V2pnPartnerCert(
    string Partner,
    string Name,
    IReadOnlyList<string> Domains,
    string Key,
    IReadOnlyList<string> Grants,
    DateOnly Issued,
    DateOnly Expires)
{
    public bool Allows(string grant) => Grants.Contains(grant);
}

/// <summary>A file of the pack (logo, artwork), pinned by size and SHA-256 in the signed partner.json.</summary>
public sealed record V2pnPartnerAsset(string Name, long Size, string Sha256);

/// <summary>
/// partner.json: what the provider wants V2pn to look like. Signed (partner.json.sig) with the key its
/// certificate names, so the provider changes it without the owner of V2pn, but only within the grants.
/// </summary>
public sealed record V2pnPartnerPack(
    string Id,
    string Name,
    string? Accent,
    string? Logo,
    string? LogoOnLight,
    IReadOnlyList<string> Variants,
    string DefaultVariant,
    IReadOnlyDictionary<string, string> Art,
    string? CabinetUrl,
    string? SupportUrl,
    string? ApiUrl,
    IReadOnlyList<V2pnPartnerAsset> Assets,
    string? Mark = null)
{
    public V2pnPartnerAsset? Asset(string? name) => Assets.FirstOrDefault(a => a.Name == name);
}

/// <summary>
/// The looks V2pn can take for a partner. V2pn draws all of them; a pack only picks which it allows and
/// which comes first, so a partner never ships layout or code.
/// </summary>
public static class V2pnPartnerVariants
{
    /// <summary>V2pn as it is, the partner's logo beside V2pn's and on its subscription cards.</summary>
    public const string Signature = "signature";

    /// <summary>V2pn's layout in the partner's colours and type.</summary>
    public const string Warm = "warm";

    /// <summary>The partner's own personal-cabinet layout.</summary>
    public const string Cabinet = "cabinet";

    /// <summary>The partner's website look: light ground, bitmap type, its artwork as the connect lamp.</summary>
    public const string Studio = "studio";

    /// <summary>Monospace text and an ASCII map.</summary>
    public const string Ascii = "ascii";

    public static readonly IReadOnlyList<string> All = [Signature, Warm, Cabinet, Studio, Ascii];
}

public static class V2pnPartnerGrants
{
    /// <summary>The partner may dress the window (its logo, colours and the variants of its pack).</summary>
    public const string Edition = "edition";

    /// <summary>V2pn may ask the partner's API for the holder's plan and server state.</summary>
    public const string Account = "account";

    public static readonly IReadOnlyList<string> All = [Edition, Account];
}

/// <summary>Reads and checks a partner's certificate and pack. Pure: no network, no files, no clock but the one passed in.</summary>
public static class V2pnPartnerTrust
{
    public const int MaxCertBytes = 16 * 1024;
    public const int MaxPackBytes = 64 * 1024;
    public const int MaxAssetBytes = 1024 * 1024;
    public const int MaxAssets = 8;

    private static readonly string[] AssetExtensions = [".png", ".webp", ".jpg", ".jpeg"];
    private static readonly Regex PartnerId = new("^[a-z0-9][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant);
    private static readonly Regex Colour = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);
    private const int MaxName = 40;

    /// <summary>The certificate when its signature is one of <paramref name="rootKeys"/>, it is well formed and <paramref name="today"/> is inside its term.</summary>
    public static V2pnPartnerCert? ReadCert(byte[]? json, string? signature, IEnumerable<string> rootKeys, DateOnly today, out string error)
    {
        if (json is not { Length: > 0 and <= MaxCertBytes })
        {
            error = "The partner certificate is missing.";
            return null;
        }
        if (!V2pnManifest.VerifySignature(json, signature, rootKeys))
        {
            error = "The partner certificate is not signed by V2pn.";
            return null;
        }
        var cert = ParseCert(json);
        if (cert == null)
        {
            error = "The partner certificate is malformed.";
            return null;
        }
        if (today < cert.Issued || today > cert.Expires)
        {
            error = $"The partner certificate is valid from {cert.Issued:yyyy-MM-dd} to {cert.Expires:yyyy-MM-dd}.";
            return null;
        }
        error = string.Empty;
        return cert;
    }

    /// <summary>The pack when the key of <paramref name="cert"/> signed it and it stays inside the certificate's partner, domains and grants.</summary>
    public static V2pnPartnerPack? ReadPack(byte[]? json, string? signature, V2pnPartnerCert cert, out string error)
    {
        if (json is not { Length: > 0 and <= MaxPackBytes })
        {
            error = "The partner pack is missing.";
            return null;
        }
        if (!V2pnManifest.VerifySignature(json, signature, [cert.Key]))
        {
            error = "The partner pack is not signed by the partner's key.";
            return null;
        }
        var pack = ParsePack(json, cert, out error);
        if (pack == null)
        {
            return null;
        }
        error = string.Empty;
        return pack;
    }

    /// <summary>True when the host of <paramref name="url"/> is one of the certificate's domains or under a "*." one.</summary>
    public static bool DomainAllowed(V2pnPartnerCert cert, string? url)
    {
        var uri = Utils.TryUri(url ?? string.Empty);
        var host = uri?.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.IsNullOrEmpty())
        {
            return false;
        }
        foreach (var domain in cert.Domains)
        {
            if (domain.StartsWith("*.", StringComparison.Ordinal))
            {
                var parent = domain[2..];
                if (host.EndsWith("." + parent, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (host == domain)
            {
                return true;
            }
        }
        return false;
    }

    public static bool AssetMatches(V2pnPartnerAsset asset, byte[] content) =>
        content.LongLength == asset.Size && Convert.ToHexStringLower(SHA256.HashData(content)) == asset.Sha256;

    /// <summary>
    /// partners-revoked.json: certificates the owner withdrew before their term, as "partner@issued" lines in
    /// a signed list. Null when the list is not signed by a root key or does not parse.
    /// </summary>
    public static HashSet<string>? ReadRevoked(byte[]? json, string? signature, IEnumerable<string> rootKeys)
    {
        if (json is not { Length: > 0 and <= MaxCertBytes } || !V2pnManifest.VerifySignature(json, signature, rootKeys))
        {
            return null;
        }
        try
        {
            if (JsonNode.Parse(json)?["revoked"] is not JsonArray array)
            {
                return null;
            }
            return array.Select(n => n?.GetValue<string>()).Where(s => s.IsNotEmpty()).Select(s => s!).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static string RevocationKey(V2pnPartnerCert cert) => $"{cert.Partner}@{cert.Issued:yyyy-MM-dd}";

    private static V2pnPartnerCert? ParseCert(byte[] json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["schema"]?.GetValue<int>() != 1)
            {
                return null;
            }
            var partner = root["partner"]?.GetValue<string>();
            var name = Name(root["name"]?.GetValue<string>());
            var key = root["key"]?.GetValue<string>();
            var domains = Strings(root["domains"]).Select(d => d.Trim().TrimEnd('.').ToLowerInvariant()).ToList();
            var grants = Strings(root["grants"]).ToList();
            if (partner == null || !PartnerId.IsMatch(partner) || name == null || key.IsNullOrEmpty() || !IsP256Key(key)
                || domains.Count == 0 || !domains.All(IsDomain) || grants.Any(g => !V2pnPartnerGrants.All.Contains(g))
                || !DateOnly.TryParseExact(root["issued"]?.GetValue<string>(), "yyyy-MM-dd", out var issued)
                || !DateOnly.TryParseExact(root["expires"]?.GetValue<string>(), "yyyy-MM-dd", out var expires)
                || expires < issued)
            {
                return null;
            }
            return new V2pnPartnerCert(partner, name, domains, key, grants, issued, expires);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static V2pnPartnerPack? ParsePack(byte[] json, V2pnPartnerCert cert, out string error)
    {
        error = "The partner pack is malformed.";
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["schema"]?.GetValue<int>() != 1)
            {
                return null;
            }
            var id = root["id"]?.GetValue<string>();
            if (id != cert.Partner)
            {
                error = "The partner pack belongs to another partner than its certificate.";
                return null;
            }
            var name = Name(root["name"]?.GetValue<string>());
            if (name == null)
            {
                return null;
            }

            var assets = new List<V2pnPartnerAsset>();
            if (root["assets"] is JsonArray assetArray)
            {
                foreach (var node in assetArray)
                {
                    var file = node?["name"]?.GetValue<string>();
                    var size = node?["size"]?.GetValue<long>() ?? -1;
                    var hash = node?["sha256"]?.GetValue<string>()?.ToLowerInvariant();
                    // Names become file names on disk: a plain picture file and nothing else.
                    if (file.IsNullOrEmpty() || file != Path.GetFileName(file) || file.Contains("..")
                        || !AssetExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())
                        || size is <= 0 or > MaxAssetBytes || hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit)
                        || assets.Any(a => a.Name == file))
                    {
                        return null;
                    }
                    assets.Add(new V2pnPartnerAsset(file, size, hash));
                }
            }
            if (assets.Count > MaxAssets)
            {
                return null;
            }
            string? AssetRef(JsonNode? node)
            {
                var file = node?.GetValue<string>();
                return file != null && assets.Any(a => a.Name == file) ? file : null;
            }

            var brand = root["brand"] as JsonObject;
            var accent = brand?["accent"]?.GetValue<string>();
            if (accent != null && !Colour.IsMatch(accent))
            {
                return null;
            }

            var edition = root["edition"] as JsonObject;
            var variants = Strings(edition?["variants"]).Where(V2pnPartnerVariants.All.Contains).Distinct().ToList();
            var first = edition?["default"]?.GetValue<string>();
            if (!cert.Allows(V2pnPartnerGrants.Edition))
            {
                // Without the grant the partner keeps its name and links only; V2pn stays as it is.
                variants = [];
            }
            if (variants.Count > 0 && (first == null || !variants.Contains(first)))
            {
                first = variants[0];
            }

            var art = new Dictionary<string, string>(StringComparer.Ordinal);
            if (edition?["art"] is JsonObject artObject)
            {
                foreach (var (key, value) in artObject)
                {
                    if (AssetRef(value) is { } file)
                    {
                        art[key] = file;
                    }
                }
            }

            var links = root["links"] as JsonObject;
            var api = Https(root["api"]?.GetValue<string>());
            if (api != null && (!cert.Allows(V2pnPartnerGrants.Account) || !DomainAllowed(cert, api)))
            {
                api = null;
            }

            return new V2pnPartnerPack(
                id,
                name,
                accent?.ToUpperInvariant(),
                AssetRef(brand?["logo"]),
                AssetRef(brand?["logoOnLight"]),
                variants,
                first ?? string.Empty,
                art,
                Https(links?["cabinet"]?.GetValue<string>()),
                Https(links?["support"]?.GetValue<string>()),
                api,
                assets,
                AssetRef(brand?["mark"]));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static string? Name(string? text)
    {
        text = text?.Trim();
        // A partner never passes itself off as V2pn: "V2pn Pro" would read as the app's own edition.
        return text is { Length: > 0 and <= MaxName } && !text.Any(char.IsControl)
               && !text.Replace(" ", string.Empty).Contains("v2pn", StringComparison.OrdinalIgnoreCase)
            ? text
            : null;
    }

    private static IEnumerable<string> Strings(JsonNode? node) =>
        node is JsonArray array ? array.Select(n => n?.GetValue<string>()).Where(s => s.IsNotEmpty()).Select(s => s!) : [];

    private static string? Https(string? url)
    {
        var uri = Utils.TryUri(url?.Trim() ?? string.Empty);
        return uri != null && (uri.Scheme == Uri.UriSchemeHttps || DevLoopback(uri)) && uri.UserInfo.Length == 0 ? uri.ToString() : null;
    }

    /// <summary>
    /// A development build, and a release with developer mode on (About → updates), also takes a partner served
    /// over plain http from this computer (127.0.0.1, localhost): a provider previews its pack on its own machine
    /// before publishing. The certificate must still be V2pn's and name that address.
    /// </summary>
    public static bool DevLoopback(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback && DebugAllowed;

    /// <summary>A development build, or a release with developer mode on: the debug tools and previews are there.</summary>
    public static bool DebugAllowed => !V2pnInfo.IsRelease || DeveloperMode();

    private static bool DeveloperMode()
    {
        try
        {
            return AppManager.Instance.Config?.UiItem?.V2pnDevMode == true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            return false;
        }
    }

    private static bool IsDomain(string domain)
    {
        if (!V2pnInfo.IsRelease && domain is "127.0.0.1" or "localhost")
        {
            return true;
        }
        var host = domain.StartsWith("*.", StringComparison.Ordinal) ? domain[2..] : domain;
        // At least a name and a zone, so "*.com" can never be granted.
        return host.Contains('.') && Uri.CheckHostName(host) == UriHostNameType.Dns && !host.Contains('*');
    }

    private static bool IsP256Key(string key)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
            return ecdsa.KeySize == 256;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
