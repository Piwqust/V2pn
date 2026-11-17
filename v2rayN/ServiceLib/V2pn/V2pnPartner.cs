namespace ServiceLib;

/// <summary>partner-&lt;id&gt;.zip of the published index: its pack, pinned by size and SHA-256.</summary>
public sealed record V2pnPartnerZip(string File, long Size, string Sha256);

/// <summary>
/// A partner as the owner of V2pn lists it in partner-index.json, the index V2pn publishes with its releases and signs with the
/// partner root key (<see cref="V2pnInfo.PartnerKeys"/>): which subscription domains are the partner's, what it may do,
/// until when, and which pack it wears.
/// </summary>
public sealed record V2pnPartnerListing(
    string Id,
    string Name,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Grants,
    DateOnly Until,
    V2pnPartnerZip Zip,
    IReadOnlyDictionary<string, string>? Reviewed = null)
{
    public bool Allows(string grant) => Grants.Contains(grant);

    /// <summary>Which key signed the index this listing came from: a hub-signed listing never carries a reviewed grant.</summary>
    public V2pnPartnerSigner Signer { get; init; } = V2pnPartnerSigner.Root;

    /// <summary>
    /// "reviewed": the SHA-256 of each file of the pack the owner of V2pn read by hand (routing.json, transport.json,
    /// core.json). A routing, transport or core grant counts only for the file with exactly this hash.
    /// </summary>
    public IReadOnlyDictionary<string, string> ReviewedFiles => Reviewed ?? EmptyReviewed;

    private static readonly IReadOnlyDictionary<string, string> EmptyReviewed = new Dictionary<string, string>();
}

/// <summary>partner-index.json: every partner V2pn knows, as the owner of V2pn signed it.</summary>
/// <param name="Problems">Entries left out and why (a newer format, a mistake); the rest of the index stands.</param>
public sealed record V2pnPartnerIndex(IReadOnlyList<V2pnPartnerListing> Partners, IReadOnlyList<string> Problems)
{
    public V2pnPartnerListing? Get(string? id) => Partners.FirstOrDefault(p => p.Id == id);

    /// <summary>The partner whose domains hold the host of <paramref name="subscriptionUrl"/>; the first one listed when several do.</summary>
    public V2pnPartnerListing? ForUrl(string? subscriptionUrl) => Partners.FirstOrDefault(p => V2pnPartnerTrust.DomainAllowed(p, subscriptionUrl));

    /// <summary>Which key signed it; a merged index (<see cref="Merge"/>) says Hub when the hub's index is in it.</summary>
    public V2pnPartnerSigner Signer { get; init; } = V2pnPartnerSigner.Root;

    /// <summary>"published" of the index; the oldest time when it has none.</summary>
    public DateTimeOffset Published { get; init; } = DateTimeOffset.MinValue;

    /// <summary>
    /// The partners V2pn goes by when it has the hub's index and a root-signed one. The hub's index is the main one: every
    /// partner it lists comes from it, whatever the root-signed index says of the same id, so a root-signed copy attached
    /// to an older GitHub release can never shadow what a provider changed on the hub since. The root-signed index adds
    /// the partners the hub does not list, unless one claims a domain of a hub partner (one domain, one partner): those
    /// are the partners the owner signs on his computer, the only way to the grants he reviews by hand. Either index
    /// alone is taken as it is.
    /// </summary>
    public static V2pnPartnerIndex? Merge(V2pnPartnerIndex? hub, V2pnPartnerIndex? root)
    {
        if (hub == null || root == null)
        {
            return hub ?? root;
        }
        var partners = hub.Partners.ToList();
        var problems = hub.Problems.Concat(root.Problems).ToList();
        foreach (var listing in root.Partners)
        {
            if (hub.Get(listing.Id) != null)
            {
                continue;
            }
            if (hub.Partners.FirstOrDefault(h => V2pnPartnerTrust.DomainsOverlap(h.Domains, listing.Domains)) is { } clash)
            {
                problems.Add($"{listing.Id}: the root-signed index gives it a domain of {clash.Id}, which the hub lists; left out");
                continue;
            }
            partners.Add(listing);
        }
        return new V2pnPartnerIndex(partners, problems)
        {
            Signer = V2pnPartnerSigner.Hub,
            Published = hub.Published > root.Published ? hub.Published : root.Published,
        };
    }
}

/// <summary>The kind of key that signed a partner index.</summary>
public enum V2pnPartnerSigner
{
    /// <summary>The owner's root key (tools/partner.cs publish, on his computer): every grant counts.</summary>
    Root,

    /// <summary>The V2pn Hub's key (on the server): the reviewed grants (<see cref="V2pnPartnerGrants.Reviewed"/>) are dropped.</summary>
    Hub,
}

/// <summary>A file of the pack (logo, artwork, reviewed file), pinned by size and SHA-256 in partner.json.</summary>
public sealed record V2pnPartnerAsset(string Name, long Size, string Sha256);

/// <summary>partner.json: what the partner wants V2pn to look like, taken only within its listing's grants.</summary>
public sealed record V2pnPartnerPack(
    string Id,
    string Name,
    string? Accent,
    string? Logo,
    string? LogoOnLight,
    string? CabinetUrl,
    string? SupportUrl,
    string? ApiUrl,
    IReadOnlyList<V2pnPartnerAsset> Assets,
    string? Mark = null,
    IReadOnlyList<V2pnServerNote>? Servers = null,
    IReadOnlyList<V2pnPartnerAction>? Actions = null,
    bool Lock = false,
    V2pnTheme? Theme = null,
    bool Login = false)
{
    /// <summary>
    /// "login": true with an API under the account grant: V2pn offers to sign in with this partner by a code the person
    /// confirms in the partner's bot (V2pnPartnerLogin), instead of copying a link.
    /// </summary>
    public bool OffersLogin => Login && ApiUrl != null;

    /// <summary>What the pack says about its servers (needs the actions grant).</summary>
    public IReadOnlyList<V2pnServerNote> ServerNotes => Servers ?? [];

    /// <summary>The partner's buttons, at most four (needs the actions grant).</summary>
    public IReadOnlyList<V2pnPartnerAction> ActionList => Actions ?? [];

    /// <summary>Fields of older pack formats that were read without error and are not used, one line each (tools/partner-check.cs).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public V2pnPartnerAsset? Asset(string? name) => Assets.FirstOrDefault(a => a.Name == name);
}

public static class V2pnPartnerGrants
{
    /// <summary>The partner may dress the window: Accent (its logo and colour), and Scene when its theme has a background.</summary>
    public const string Edition = "edition";

    /// <summary>V2pn may ask the partner's API for the holder's plan and server state.</summary>
    public const string Account = "account";

    /// <summary>The partner's buttons and its description of its servers (groups, countries, purposes).</summary>
    public const string Actions = "actions";

    /// <summary>
    /// The partner may close its subscription's settings (hide-settings, hide-url, pack "lock"): the addresses of its
    /// servers are not shown or exported, its link and its defaults cannot be changed. Given after a review only.
    /// </summary>
    public const string Lock = "lock";

    /// <summary>The partner's routing preset (routing.json), offered as a mode of "What goes through the VPN". Given after a review only.</summary>
    public const string Routing = "routing";

    /// <summary>Fragmentation and noise for the partner's servers (transport.json). Given after a review only.</summary>
    public const string Transport = "transport";

    /// <summary>Extra outbounds, balancers and rules for the partner's servers (core.json). Given after a review only.</summary>
    public const string Core = "core";

    public static readonly IReadOnlyList<string> All = [Edition, Account, Actions, Lock, Routing, Transport, Core];

    /// <summary>
    /// The grants of files the owner of V2pn reads by hand (their hashes in "reviewed"): taken only from an index signed
    /// with a root key, never from the hub's. The hub drops them too (hub/lib/publish.mjs REVIEWED_GRANTS).
    /// </summary>
    public static readonly IReadOnlyList<string> Reviewed = [Routing, Transport, Core];
}

/// <summary>Reads and checks the partner index and the packs it pins. Pure: no network, no files, no clock but the one passed in.</summary>
public static class V2pnPartnerTrust
{
    /// <summary>
    /// The index V2pn reads for the partners a person came from (V2pnEdition). Its own name, so copies of 1.2.1 and older,
    /// which dressed V2pn in a partner's look by the selected server, never see a partner again: they read
    /// <see cref="LegacyIndexFile"/>.
    /// </summary>
    public const string IndexFile = "partner-index.json";

    /// <summary>What V2pn 1.2.1 and older read; every release still publishes it, signed and empty, so they drop every partner.</summary>
    public const string LegacyIndexFile = "partners.json";
    public const string PackFile = "partner.json";
    public const int MaxIndexBytes = 256 * 1024;
    public const int MaxPackBytes = 64 * 1024;
    public const int MaxAssetBytes = 1024 * 1024;
    public const int MaxAssets = 8;

    /// <summary>A pack's zip at most: partner.json, eight pictures and the three reviewed files, with room for the zip's own headers.</summary>
    public const int MaxZipBytes = MaxPackBytes + MaxAssets * MaxAssetBytes + 3 * V2pnReviewedFiles.MaxBytes + 64 * 1024;

    private static readonly string[] AssetExtensions = [".png", ".webp", ".jpg", ".jpeg"];
    private static readonly Regex PartnerId = new("^[a-z0-9][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant);
    private static readonly Regex Colour = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

    // File names that become names on disk: plain ASCII, no folders, no leading dot, nothing Windows reserves.
    private static readonly Regex FileName = new("^[A-Za-z0-9][A-Za-z0-9_-]{0,47}(\\.[A-Za-z0-9]{1,8})$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };
    private const int MaxName = 40;

    /// <summary>The name the zip of partner <paramref name="id"/> has in a release.</summary>
    public static string ZipName(string id) => $"partner-{id}.zip";

    /// <summary>
    /// The index when its signature is one of <paramref name="rootKeys"/> over these exact bytes and it parses. An entry
    /// that does not parse is left out and named in <see cref="V2pnPartnerIndex.Problems"/>; one partner twice refuses the whole index.
    /// </summary>
    public static V2pnPartnerIndex? ReadIndex(byte[]? json, string? signature, IEnumerable<string> rootKeys, out string error) =>
        ReadIndex(json, signature, rootKeys, [], out error);

    /// <summary>
    /// The index when its signature is one of <paramref name="rootKeys"/> (every grant counts) or one of
    /// <paramref name="hubKeys"/> (the V2pn Hub's: the reviewed grants and the hashes of reviewed files are dropped and
    /// named in <see cref="V2pnPartnerIndex.Problems"/>, the rest of each listing stands). A root key is tried first, so a
    /// key in both lists counts as a root key. <see cref="V2pnPartnerIndex.Signer"/> and every listing say which it was.
    /// </summary>
    public static V2pnPartnerIndex? ReadIndex(byte[]? json, string? signature, IEnumerable<string> rootKeys, IEnumerable<string> hubKeys, out string error)
    {
        if (json is not { Length: > 0 and <= MaxIndexBytes })
        {
            error = "The partner index is missing or larger than 256 KB.";
            return null;
        }
        V2pnPartnerSigner signer;
        if (V2pnManifest.VerifySignature(json, signature, rootKeys))
        {
            signer = V2pnPartnerSigner.Root;
        }
        else if (V2pnManifest.VerifySignature(json, signature, hubKeys))
        {
            signer = V2pnPartnerSigner.Hub;
        }
        else
        {
            error = "The partner index is not signed by V2pn.";
            return null;
        }
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["schema"]?.GetValue<int>() != 1 || root["partners"] is not JsonArray array)
            {
                error = "The partner index is malformed.";
                return null;
            }
            var partners = new List<V2pnPartnerListing>();
            var problems = new List<string>();
            foreach (var (node, i) in array.Select((n, i) => (n, i)))
            {
                var problem = "not an object";
                var listing = node is JsonObject item ? ParseListing(item, out problem) : null;
                if (listing == null)
                {
                    problems.Add($"partners[{i}]: {problem}; left out");
                    continue;
                }
                if (partners.Any(p => p.Id == listing.Id))
                {
                    error = $"The partner index lists {listing.Id} twice.";
                    return null;
                }
                if (signer == V2pnPartnerSigner.Hub)
                {
                    // The server's key may be taken from the server; what the owner reads by hand needs his own key.
                    var dropped = listing.Grants.Where(V2pnPartnerGrants.Reviewed.Contains).ToList();
                    if (dropped.Count > 0 || listing.ReviewedFiles.Count > 0)
                    {
                        problems.Add($"{listing.Id}: {string.Join(", ", dropped.Concat(listing.ReviewedFiles.Keys).Distinct())} left out: only the root key grants what is reviewed by hand");
                    }
                    listing = listing with
                    {
                        Grants = listing.Grants.Where(g => !V2pnPartnerGrants.Reviewed.Contains(g)).ToList(),
                        Reviewed = new Dictionary<string, string>(),
                    };
                }
                partners.Add(listing with { Signer = signer });
            }
            error = string.Empty;
            var published = root["published"] is JsonValue value && value.TryGetValue<string>(out var text)
                            && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var time)
                ? time
                : DateTimeOffset.MinValue;
            return new V2pnPartnerIndex(partners, problems) { Signer = signer, Published = published };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            error = "The partner index is malformed.";
            return null;
        }
    }

    /// <summary>
    /// Opens partner-&lt;id&gt;.zip of <paramref name="listing"/>: its size and SHA-256 must be the index's, every entry a
    /// plain file name of an allowed kind within the size limits, partner.json must read under the listing, and every
    /// other entry must be one of its assets with its size and hash. Null with the reason otherwise.
    /// </summary>
    public static V2pnPartnerPack? Unpack(byte[]? zip, V2pnPartnerListing listing, out Dictionary<string, byte[]> files, out string error)
    {
        files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (zip == null || zip.LongLength != listing.Zip.Size || zip.Length > MaxZipBytes || Convert.ToHexStringLower(SHA256.HashData(zip)) != listing.Zip.Sha256)
        {
            error = $"{listing.Zip.File} is not the one the partner index names.";
            return null;
        }
        if (ReadZip(zip, out var entries, out error) == false)
        {
            return null;
        }
        if (!entries.TryGetValue(PackFile, out var packJson))
        {
            error = $"{listing.Zip.File} has no {PackFile}.";
            return null;
        }
        var pack = ReadPack(packJson, listing, out error);
        if (pack == null)
        {
            return null;
        }
        foreach (var asset in pack.Assets)
        {
            if (!entries.TryGetValue(asset.Name, out var content) || !AssetMatches(asset, content))
            {
                error = $"The file {asset.Name} does not match partner.json.";
                return null;
            }
        }
        if (entries.Keys.FirstOrDefault(name => name != PackFile && pack.Asset(name) == null) is { } stray)
        {
            error = $"{listing.Zip.File} has {stray}, which partner.json does not list.";
            return null;
        }
        files = entries;
        error = string.Empty;
        return pack;
    }

    /// <summary>The entries of a pack's zip by name, read within the limits; false with the reason when any entry breaks a rule.</summary>
    private static bool ReadZip(byte[] zip, out Dictionary<string, byte[]> entries, out string error)
    {
        entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip, writable: false), System.IO.Compression.ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxAssets + 1 + V2pnReviewedFiles.All.Count)
            {
                error = "The pack has more files than a pack may.";
                return false;
            }
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (!SafeFileName(name) || !(name == PackFile || V2pnReviewedFiles.All.Contains(name) || IsPictureName(name)))
                {
                    error = $"The pack has an entry V2pn does not take: \"{Clip(name)}\".";
                    return false;
                }
                // Two names Windows sees as one would overwrite each other on disk.
                if (entries.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)))
                {
                    error = $"The pack has {name} twice.";
                    return false;
                }
                var limit = name == PackFile ? MaxPackBytes : V2pnReviewedFiles.All.Contains(name) ? V2pnReviewedFiles.MaxBytes : MaxAssetBytes;
                // The declared size can lie; the read stops one byte past the limit whatever it says.
                using var stream = entry.Open();
                var content = ReadAtMost(stream, limit + 1);
                if (content.Length > limit)
                {
                    error = $"{name} in the pack is larger than {limit / 1024} KB.";
                    return false;
                }
                total += content.Length;
                if (total > MaxZipBytes)
                {
                    error = "The pack unpacks to more than a pack may hold.";
                    return false;
                }
                entries[name] = content;
            }
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or ArgumentException)
        {
            error = $"The pack is not a zip V2pn can read: {ex.Message}";
            return false;
        }
    }

    private static byte[] ReadAtMost(Stream stream, int max)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while (buffer.Length < max && (read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, max - buffer.Length))) > 0)
        {
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>partner.json read under <paramref name="listing"/>: its id must be the listing's; what the grants do not allow is left out.</summary>
    public static V2pnPartnerPack? ReadPack(byte[]? json, V2pnPartnerListing listing, out string error)
    {
        if (json is not { Length: > 0 and <= MaxPackBytes })
        {
            error = "partner.json is missing or larger than 64 KB.";
            return null;
        }
        var pack = ParsePack(json, listing, out error);
        if (pack != null)
        {
            error = string.Empty;
        }
        return pack;
    }

    /// <summary>True when the host of <paramref name="url"/> is one of the listing's domains or under a "*." one.</summary>
    public static bool DomainAllowed(V2pnPartnerListing listing, string? url)
    {
        var uri = Utils.TryUri(url ?? string.Empty);
        var host = uri?.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.IsNullOrEmpty())
        {
            return false;
        }
        foreach (var domain in listing.Domains)
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

    /// <summary>True when a host could be on both lists of domains ("a.example.com" and "*.example.com" overlap).</summary>
    public static bool DomainsOverlap(IEnumerable<string> a, IEnumerable<string> b)
    {
        static string Base(string d) => d.StartsWith("*.", StringComparison.Ordinal) ? d[2..] : d;
        static bool Covers(string wildcard, string other) =>
            wildcard.StartsWith("*.", StringComparison.Ordinal) && Base(other).EndsWith("." + wildcard[2..], StringComparison.Ordinal);
        var second = b.ToList();
        return a.Any(x => second.Any(y => x == y || Covers(x, y) || Covers(y, x)));
    }

    public static bool AssetMatches(V2pnPartnerAsset asset, byte[] content) =>
        content.LongLength == asset.Size && Convert.ToHexStringLower(SHA256.HashData(content)) == asset.Sha256;

    /// <summary>A partner id: lowercase letters, digits and dashes, 2 to 32 characters.</summary>
    public static bool ValidId(string? id) => id != null && PartnerId.IsMatch(id);

    /// <summary>The name people see, trimmed, when a listing or pack may carry it: up to 40 characters, no control characters, no "V2pn".</summary>
    public static string? ValidName(string? name) => Name(name);

    /// <summary>A subscription domain a listing may name ("example.com", "*.example.com"; 127.0.0.1 and localhost in a development build).</summary>
    public static bool ValidDomain(string? domain) => domain != null && IsDomain(domain);

    /// <summary>A file name a pack may carry: plain ASCII letters, digits, "-" and "_", one extension, no folder, nothing Windows reserves.</summary>
    public static bool SafeFileName(string? name) =>
        name != null && FileName.IsMatch(name) && !ReservedNames.Contains(Path.GetFileNameWithoutExtension(name));

    public static bool IsPictureName(string name) => AssetExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());

    /// <summary>One entry of the index; null with the reason when it breaks a rule.</summary>
    private static V2pnPartnerListing? ParseListing(JsonObject item, out string problem)
    {
        var id = Text(item["id"]);
        if (!ValidId(id))
        {
            problem = "id is not 2 to 32 lowercase letters, digits and dashes";
            return null;
        }
        var name = Name(Text(item["name"]));
        var domains = Strings(item["domains"]).Select(d => d.Trim().TrimEnd('.').ToLowerInvariant()).ToList();
        // A grant this V2pn does not know yet is left out, so a newer index still works here.
        var grants = Strings(item["grants"]).Where(V2pnPartnerGrants.All.Contains).Distinct().ToList();
        // A file name this V2pn does not know, or a value that is not a SHA-256, is left out the same way.
        var reviewed = new Dictionary<string, string>(StringComparer.Ordinal);
        if (item["reviewed"] is JsonObject reviewedObject)
        {
            foreach (var (file, value) in reviewedObject)
            {
                if (V2pnReviewedFiles.All.Contains(file) && Text(value) is { Length: 64 } hash && hash.All(Uri.IsHexDigit))
                {
                    reviewed[file] = hash.ToLowerInvariant();
                }
            }
        }
        var zip = item["pack"] as JsonObject;
        var zipHash = Text(zip?["sha256"])?.ToLowerInvariant();
        long zipSize = zip?["size"] is JsonValue sizeValue && sizeValue.TryGetValue<long>(out var size) ? size : -1;
        if (name == null)
        {
            problem = $"{id}: name is missing, too long or passes for V2pn";
        }
        else if (domains.Count == 0 || !domains.All(IsDomain))
        {
            problem = $"{id}: domains has none or one that is not a domain";
        }
        else if (!DateOnly.TryParseExact(Text(item["until"]), "yyyy-MM-dd", out var until))
        {
            problem = $"{id}: until is not a yyyy-MM-dd date";
        }
        else if (Text(zip?["file"]) != ZipName(id!) || zipSize is <= 0 or > MaxZipBytes || zipHash is not { Length: 64 } || !zipHash.All(Uri.IsHexDigit))
        {
            problem = $"{id}: pack is not {ZipName(id!)} with its size and sha256";
        }
        else
        {
            problem = string.Empty;
            return new V2pnPartnerListing(id!, name, domains, grants, until, new V2pnPartnerZip(ZipName(id!), zipSize, zipHash), reviewed);
        }
        return null;
    }

    // Fields of the pack formats before the Accent and Scene look: read without error, used no more.
    private static readonly string[] RetiredEdition = ["variants", "default", "art"];

    private static V2pnPartnerPack? ParsePack(byte[] json, V2pnPartnerListing listing, out string error)
    {
        error = "partner.json is malformed.";
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["schema"]?.GetValue<int>() != 1)
            {
                return null;
            }
            var id = Text(root["id"]);
            if (id != listing.Id)
            {
                error = "partner.json belongs to another partner than its listing.";
                return null;
            }
            var name = Name(Text(root["name"]));
            if (name == null)
            {
                error = "partner.json: name is missing, longer than 40 characters or passes for V2pn.";
                return null;
            }

            var assets = new List<V2pnPartnerAsset>();
            if (root["assets"] is JsonArray assetArray)
            {
                foreach (var node in assetArray)
                {
                    var file = Text(node?["name"]);
                    var size = node?["size"] is JsonValue sizeValue && sizeValue.TryGetValue<long>(out var s) ? s : -1;
                    var hash = Text(node?["sha256"])?.ToLowerInvariant();
                    // Names become file names on disk: a plain picture file, or one of the reviewed JSON files, and nothing else.
                    var reviewedFile = V2pnReviewedFiles.All.Contains(file ?? string.Empty);
                    if (!SafeFileName(file) || !(reviewedFile || IsPictureName(file!))
                        || size <= 0 || size > (reviewedFile ? V2pnReviewedFiles.MaxBytes : MaxAssetBytes)
                        || hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit)
                        || assets.Any(a => string.Equals(a.Name, file, StringComparison.OrdinalIgnoreCase)))
                    {
                        error = $"partner.json: the asset \"{Clip(file ?? "")}\" is not a picture or reviewed file with its size and sha256.";
                        return null;
                    }
                    assets.Add(new V2pnPartnerAsset(file!, size, hash));
                }
            }
            if (assets.Count(a => !V2pnReviewedFiles.All.Contains(a.Name)) > MaxAssets)
            {
                error = $"partner.json lists more than {MaxAssets} pictures.";
                return null;
            }
            string? AssetRef(JsonNode? node)
            {
                var file = Text(node);
                return file != null && !V2pnReviewedFiles.All.Contains(file) && assets.Any(a => a.Name == file) ? file : null;
            }

            var brand = root["brand"] as JsonObject;
            var accent = Text(brand?["accent"]);
            if (accent != null && !Colour.IsMatch(accent))
            {
                error = "partner.json: brand.accent is not a #RRGGBB colour.";
                return null;
            }

            var notes = new List<string>();
            if (root["edition"] is JsonObject edition)
            {
                foreach (var (key, _) in edition)
                {
                    notes.Add(RetiredEdition.Contains(key)
                        ? $"edition.{Clip(key)} is no longer used: V2pn has one look (Accent, and Scene with a background); ignored"
                        : $"edition.{Clip(key)} is not a field V2pn knows; ignored");
                }
            }

            // Without the edition grant the partner keeps its name and links only; V2pn stays as it is.
            var theme = listing.Allows(V2pnPartnerGrants.Edition)
                ? V2pnThemeReader.Parse(root["theme"], accent, file => AssetRef(JsonValue.Create(file)) != null)
                : null;

            var links = root["links"] as JsonObject;
            var api = Https(Text(root["api"]));
            if (api != null && (!listing.Allows(V2pnPartnerGrants.Account) || !DomainAllowed(listing, api)))
            {
                api = null;
            }

            // Buttons and server notes come with the actions grant; closed settings only with the lock grant.
            var catalog = listing.Allows(V2pnPartnerGrants.Actions);
            var servers = catalog ? V2pnPartnerCatalog.ParseServers(root["servers"], live: false) : [];
            var actions = catalog ? V2pnPartnerCatalog.ParseActions(root["actions"], listing, api != null) : [];
            var locked = listing.Allows(V2pnPartnerGrants.Lock) && root["lock"] is JsonValue lockValue && lockValue.TryGetValue<bool>(out var lockOn) && lockOn;
            // Sign-in by code talks to the API, so it comes with it (the account grant, on the listing's domains).
            var login = api != null && root["login"] is JsonValue loginValue && loginValue.TryGetValue<bool>(out var loginOn) && loginOn;

            return new V2pnPartnerPack(
                id,
                name,
                accent?.ToUpperInvariant(),
                AssetRef(brand?["logo"]),
                AssetRef(brand?["logoOnLight"]),
                Https(Text(links?["cabinet"])),
                Https(Text(links?["support"])),
                api,
                assets,
                AssetRef(brand?["mark"]),
                servers,
                actions,
                locked,
                theme,
                login)
            {
                Notes = notes,
            };
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

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static IEnumerable<string> Strings(JsonNode? node) =>
        node is JsonArray array ? array.Select(Text).Where(s => s.IsNotEmpty()).Select(s => s!) : [];

    private static string Clip(string text) => text.Length <= 40 ? text : text[..40] + "…";

    private static string? Https(string? url)
    {
        var uri = Utils.TryUri(url?.Trim() ?? string.Empty);
        return uri != null && (uri.Scheme == Uri.UriSchemeHttps || DevLoopback(uri)) && uri.UserInfo.Length == 0 ? uri.ToString() : null;
    }

    /// <summary>
    /// A development build, and a release with developer mode on (About → updates), also takes a partner's links and API
    /// over plain http from this computer (127.0.0.1, localhost), for previews with tools/demo.
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
}
