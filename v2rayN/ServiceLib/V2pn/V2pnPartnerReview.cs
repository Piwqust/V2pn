namespace ServiceLib;

/// <summary>
/// The files of a pack that change traffic and are used only after the owner of V2pn read them by hand: the
/// listing grants the right and names each file's SHA-256 in "reviewed", so a file changed afterwards is not used
/// until the owner reviews it again and publishes its new hash.
/// </summary>
public static class V2pnReviewedFiles
{
    public const string Routing = "routing.json";
    public const string Transport = "transport.json";
    public const string Core = "core.json";

    public const int MaxBytes = 64 * 1024;

    public static readonly IReadOnlyList<string> All = [Routing, Transport, Core];

    public static string GrantOf(string file) => file switch
    {
        Routing => V2pnPartnerGrants.Routing,
        Transport => V2pnPartnerGrants.Transport,
        _ => V2pnPartnerGrants.Core,
    };

    public static string Hash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}

public enum V2pnReviewState
{
    /// <summary>The listing has no grant or no reviewed hash for the file: V2pn never read it.</summary>
    NotReviewed,

    /// <summary>The file differs from the one V2pn reviewed.</summary>
    Changed,

    /// <summary>The reviewed file does not pass this V2pn's checks (a newer format, or a check added later).</summary>
    Invalid,

    /// <summary>Reviewed, unchanged and valid: V2pn may use it.</summary>
    Ready,
}

/// <summary>What became of one reviewed file of a pack; <see cref="Problem"/> says why it is not used, in English, for logs and tools.</summary>
public sealed record V2pnReviewedFile(string Name, string Grant, V2pnReviewState State, string? Problem);

/// <summary>A partner's reviewed files and what V2pn may use of them.</summary>
public sealed class V2pnPartnerFeatures
{
    public static readonly V2pnPartnerFeatures None = new([], null, null, null);

    public V2pnPartnerFeatures(IReadOnlyList<V2pnReviewedFile> files, V2pnRoutingPreset? routing, V2pnTransport? transport, V2pnCoreFragment? core)
    {
        Files = files;
        Routing = routing;
        Transport = transport;
        Core = core;
    }

    /// <summary>Every reviewed file the pack lists, whatever its state.</summary>
    public IReadOnlyList<V2pnReviewedFile> Files { get; }

    /// <summary>The routing preset when routing.json is ready.</summary>
    public V2pnRoutingPreset? Routing { get; }

    public V2pnTransport? Transport { get; }

    public V2pnCoreFragment? Core { get; }

    public V2pnReviewedFile? File(string name) => Files.FirstOrDefault(f => f.Name == name);

    /// <summary>
    /// Reads the reviewed files of <paramref name="pack"/>: each is used only when the listing grants it and names
    /// the very hash the pack pins (and the bytes have it), and it passes its format's checks.
    /// </summary>
    /// <param name="content">The bytes of a pack file, already checked against the pack's size and hash; null when missing.</param>
    public static V2pnPartnerFeatures Read(V2pnPartnerListing listing, V2pnPartnerPack pack, Func<string, byte[]?> content)
    {
        var files = new List<V2pnReviewedFile>();
        V2pnRoutingPreset? routing = null;
        V2pnTransport? transport = null;
        V2pnCoreFragment? core = null;
        // Routes, then connection settings, then core config, whatever order the pack lists them in.
        foreach (var asset in V2pnReviewedFiles.All.Select(pack.Asset).OfType<V2pnPartnerAsset>())
        {
            var grant = V2pnReviewedFiles.GrantOf(asset.Name);
            if (!listing.Allows(grant) || !listing.ReviewedFiles.TryGetValue(asset.Name, out var reviewed))
            {
                files.Add(new(asset.Name, grant, V2pnReviewState.NotReviewed, $"the listing has no {grant} grant with a reviewed hash for {asset.Name}"));
                continue;
            }
            var bytes = content(asset.Name);
            if (asset.Sha256 != reviewed || bytes == null || V2pnReviewedFiles.Hash(bytes) != reviewed)
            {
                files.Add(new(asset.Name, grant, V2pnReviewState.Changed, $"{asset.Name} changed after the review (reviewed {reviewed[..12]}, now {asset.Sha256[..12]})"));
                continue;
            }
            string problem;
            object? parsed = asset.Name switch
            {
                V2pnReviewedFiles.Routing => routing = V2pnRoutingPreset.Parse(bytes, out problem),
                V2pnReviewedFiles.Transport => transport = V2pnTransport.Parse(bytes, out problem),
                _ => core = V2pnCoreFragment.Parse(bytes, out problem),
            };
            files.Add(parsed == null
                ? new(asset.Name, grant, V2pnReviewState.Invalid, $"{asset.Name}: {problem}")
                : new(asset.Name, grant, V2pnReviewState.Ready, null));
        }
        return files.Count == 0 ? None : new V2pnPartnerFeatures(files, routing, transport, core);
    }
}
