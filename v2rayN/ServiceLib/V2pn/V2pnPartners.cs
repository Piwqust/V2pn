namespace ServiceLib;

/// <summary>A partner whose listing and pack passed every check, with its files on disk.</summary>
public sealed class V2pnPartner(V2pnPartnerListing listing, V2pnPartnerPack pack, string folder, V2pnPartnerFeatures? features = null)
{
    /// <summary>What the owner of V2pn allows it: domains, grants, term, reviewed files (the signed index).</summary>
    public V2pnPartnerListing Listing { get; } = listing;
    public V2pnPartnerPack Pack { get; } = pack;

    /// <summary>Its reviewed files (routing.json, transport.json, core.json) and what of them V2pn may use.</summary>
    public V2pnPartnerFeatures Features { get; } = features ?? V2pnPartnerFeatures.None;
    public string Id => Listing.Id;
    public string Name => Pack.Name;

    /// <summary>It dresses the window (Accent, and Scene with a background); false when its listing grants only its name and links.</summary>
    public bool HasEdition => Listing.Allows(V2pnPartnerGrants.Edition);

    /// <summary>The local file of a pack asset, or null when the pack has no such asset.</summary>
    public string? AssetPath(string? name) => Pack.Asset(name) == null ? null : Path.Combine(Folder, name!);

    /// <summary>Where its checked files are kept.</summary>
    public string Folder { get; } = folder;
}

/// <summary>Why a partner was dropped.</summary>
public enum V2pnPartnerGone
{
    /// <summary>The person no longer comes from it: the last subscription of a sign-in by code was deleted.</summary>
    Unlinked,

    /// <summary>Its term in the index ran out.</summary>
    Expired,

    /// <summary>The owner of V2pn took it out of the index.</summary>
    Revoked,
}

/// <summary>
/// The partners the person came from (V2pnEdition: a provider's link, a sign-in by code, the marker of an older build),
/// each with a tab in the title bar. partner-index.json lists every partner with its subscription domains, grants, term
/// and the hash of its pack's zip. Two publish it (<see cref="V2pnInfo.PartnerSources"/>): the V2pn Hub on V2pn's server,
/// signed with the hub's key, and the GitHub release, signed with the owner's root key; V2pn asks both and goes by
/// <see cref="V2pnPartnerIndex.Merge"/> of the newest of each. For each partner the person came from, V2pn
/// downloads its zip (subscription or not: the tab is there before the person signs in), checks it (V2pnPartnerTrust)
/// and keeps it in guiConfigs/partners/&lt;id&gt; with the index it was checked against; every start checks the kept files
/// again, so a pack changed on disk is not trusted either.
/// <para>
/// A subscription is a partner's (<see cref="ForSubscription"/>) only while that partner's tab is open and the link is
/// on its domains; in V2pn's tab, and for a partner the person did not come from, it is just a subscription. A person
/// who came from no partner gets nothing asked for or downloaded, and what an older V2pn kept of a partner is removed;
/// only the sign-in by code (<see cref="LoginOffersAsync"/>), when the person opens it, and a provider's link
/// (<see cref="ComeFromLinkAsync"/>), when the person follows it, ask for the index.
/// </para>
/// </summary>
public sealed class V2pnPartners
{
    private static readonly Lazy<V2pnPartners> _instance = new(() =>
        new(V2pnInfo.PartnerKeys, () => Utils.GetConfigPath("partners"), V2pnEdition.Instance, V2pnInfo.PartnerHubKeys));
    public static V2pnPartners Instance => _instance.Value;

    /// <summary>How often the index is asked for at most, unless a caller forces it.</summary>
    public static readonly TimeSpan IndexInterval = TimeSpan.FromHours(6);

    /// <summary>How often a provider's link asks for the index at most when the kept one does not list that provider.</summary>
    public static readonly TimeSpan LinkIndexInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The kept hub-signed index. The root-signed one keeps the name V2pn 1.2 kept its only index under
    /// (<see cref="V2pnPartnerTrust.IndexFile"/>); either file is placed by the key that verifies it, not by its name.
    /// </summary>
    public const string HubIndexFile = "partner-index-hub.json";

    private const string Tag = nameof(V2pnPartners);
    private const string KeptZip = "pack.zip";

    private readonly IReadOnlyList<string> _rootKeys;
    private readonly IReadOnlyList<string> _hubKeys;
    private readonly Func<string> _root;
    private readonly V2pnEdition _edition;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, V2pnPartner> _partners = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _errors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _slotNotes = new(StringComparer.Ordinal);
    // The debug window's made-up partners and the subscriptions it counts as theirs; never on disk, never pruned.
    private readonly ConcurrentDictionary<string, byte> _debugIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _debugSubs = new(StringComparer.Ordinal);
    // The newest index taken of each signer, and the two merged: what every question goes by.
    private V2pnPartnerIndex? _rootIndex;
    private V2pnPartnerIndex? _hubIndex;
    private V2pnPartnerIndex? _index;
    // The source that served each signer's index: its packs are asked there first.
    private readonly ConcurrentDictionary<V2pnPartnerSigner, string> _servedBy = new();
    private DateTime _indexAsked = DateTime.MinValue;
    private Task? _load;

    /// <summary>Raised after the set of partners or one of them changed. May come from any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised for each partner dropped, with the reason, before <see cref="Changed"/>. May come from any thread.</summary>
    public event Action<V2pnPartner, V2pnPartnerGone>? Removed;

    /// <param name="edition">The partners the person came from and the open tab.</param>
    /// <param name="hubKeys">The V2pn Hub's keys: an index they sign is taken without the reviewed grants.</param>
    public V2pnPartners(IReadOnlyList<string> rootKeys, Func<string> root, V2pnEdition edition, IReadOnlyList<string>? hubKeys = null)
    {
        _rootKeys = rootKeys;
        _hubKeys = hubKeys ?? [];
        _root = root;
        _edition = edition;
        // Another tab makes other subscriptions a partner's: whatever asks ForSubscription asks again.
        edition.Changed += () => Changed?.Invoke();
    }

    /// <summary>The partners the person came from and the open tab.</summary>
    public V2pnEdition Edition => _edition;

    /// <summary>The listing of the partner whose tab is open, in the last index; null in V2pn's tab, before an index, or when it is not listed.</summary>
    private V2pnPartnerListing? OpenListing => _edition.Tab is { } tab ? _index?.Get(tab) : null;

    /// <summary>Overridable for tests: the network.</summary>
    public Func<string, CancellationToken, Task<V2pnHttp.Result>> Fetch { get; set; } = DefaultFetchAsync;

    /// <summary>Overridable for tests: where the index and the zips are published (a releases page: &lt;source&gt;/latest/download/...).</summary>
    public IReadOnlyList<string> Sources { get; set; } = V2pnInfo.PartnerSources;

    /// <summary>Overridable for tests: the links of every subscription on this computer.</summary>
    public Func<Task<IReadOnlyList<string?>>> SubscriptionUrls { get; set; } = DefaultSubscriptionUrlsAsync;

    /// <summary>Overridable for tests: the date terms are checked against.</summary>
    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Overridable for tests: the clock of <see cref="IndexInterval"/>.</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public IReadOnlyList<V2pnPartner> All => _partners.Values.OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();

    public V2pnPartner? Get(string? id) => id != null && _partners.TryGetValue(id, out var partner) ? partner : null;

    /// <summary>The last index that passed its checks; null before the first one.</summary>
    public V2pnPartnerIndex? Index => _index;

    /// <summary>Why the index could not be taken the last time it was asked for; null after a success.</summary>
    public string? IndexError { get; private set; }

    /// <summary>
    /// The partner of a subscription: the partner whose tab is open, when the subscription's link is on its domains.
    /// Null in V2pn's tab and for every other subscription: there it is just a subscription.
    /// </summary>
    public V2pnPartner? ForSubscription(SubItem? item)
    {
        if (item?.Id is { } id && _debugSubs.TryGetValue(id, out var debugPartner))
        {
            return _edition.IsOpen(debugPartner) ? Get(debugPartner) : null;
        }
        return ForUrl(item?.Url);
    }

    /// <summary>The partner whose tab is open when a subscription link is on its domains; null for any other link, and in V2pn's tab.</summary>
    public V2pnPartner? ForUrl(string? subscriptionUrl) =>
        ListingFor(subscriptionUrl) is { } listing && Get(listing.Id) is { } partner && !_debugIds.ContainsKey(partner.Id) ? partner : null;

    /// <summary>The listing of the partner whose tab is open when a subscription link is on its domains, whether or not its pack is here yet.</summary>
    public V2pnPartnerListing? ListingFor(string? subscriptionUrl) =>
        OpenListing is { } listing && V2pnPartnerTrust.DomainAllowed(listing, subscriptionUrl) ? listing : null;

    /// <summary>Pictures of a partner's pack that did not fit their slot and are not shown (V2pnPartnerSlots).</summary>
    public IReadOnlyList<string> SlotNotesFor(string? partnerId) =>
        partnerId != null && _slotNotes.TryGetValue(partnerId, out var notes) ? notes : [];

    /// <summary>Why the last download of a partner's pack failed; null after a success or before any.</summary>
    public string? ErrorFor(string? partnerId) => partnerId != null && _errors.TryGetValue(partnerId, out var error) ? error : null;

    public Task LoadAsync()
    {
        lock (_partners)
        {
            return _load ??= Task.Run(LoadCore);
        }
    }

    private void LoadCore()
    {
        try
        {
            var root = _root();
            if (!Directory.Exists(root))
            {
                return;
            }
            var from = _edition.CameFrom;
            if (from.Count == 0)
            {
                // From no partner: what V2pn 1.2.1 kept of a partner (its index, pack, pictures), or the offers of a
                // sign-in not finished, goes unread.
                Logging.SaveLog($"{Tag}: the person came from no partner; removing kept partner files");
                DeleteQuietly(root);
                return;
            }
            foreach (var file in new[] { V2pnPartnerTrust.IndexFile, HubIndexFile })
            {
                var kept = V2pnPartnerTrust.ReadIndex(ReadOrNull(root, file, V2pnPartnerTrust.MaxIndexBytes),
                    ReadTextOrNull(root, file + ".sig"), _rootKeys, _hubKeys, out var indexError);
                if (kept == null)
                {
                    if (File.Exists(Path.Combine(root, file)))
                    {
                        Logging.SaveLog($"{Tag}: the kept partner index {file} is not trusted: {indexError}");
                    }
                    continue;
                }
                if (kept.Published >= (Kept(kept.Signer)?.Published ?? DateTimeOffset.MinValue))
                {
                    SetKept(kept);
                }
            }
            var index = _index = V2pnPartnerIndex.Merge(_hubIndex, _rootIndex);
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var id = Path.GetFileName(dir);
                var listing = from.Contains(id) ? index?.Get(id) : null;
                if (id.StartsWith('.') || listing == null || listing.Until < Today())
                {
                    // A staging folder left behind, a partner the person does not come from, or one no longer listed or run out.
                    DeleteQuietly(dir);
                    continue;
                }
                var partner = ReadFolder(dir, listing, out var error);
                if (partner == null)
                {
                    Logging.SaveLog($"{Tag}: {id} is no longer trusted: {error}");
                    continue;
                }
                _partners[partner.Id] = partner;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Asks for the index (at start, then at most every <see cref="IndexInterval"/> unless <paramref name="forceIndex"/>),
    /// downloads the pack of each partner the person came from when its zip is not the one kept, and drops a partner that
    /// left the index, ran out or the person no longer comes from. Runs one at a time; a failure keeps what is kept (a
    /// release page being down changes nothing). For a person who came from no partner it asks for nothing.
    /// </summary>
    public async Task RefreshAsync(bool forceIndex = false, CancellationToken cancellationToken = default)
    {
        await LoadAsync();
        if (!_edition.Any)
        {
            return;
        }
        IReadOnlyList<string?> urls;
        try
        {
            urls = await SubscriptionUrls();
        }
        catch (Exception ex)
        {
            // Callers start this and forget it: a failure here is logged, never left unobserved.
            Logging.SaveLog(Tag, ex);
            return;
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (forceIndex || Now() - _indexAsked >= IndexInterval)
            {
                _indexAsked = Now();
                await RefreshIndexAsync(cancellationToken);
            }
            PruneCore(urls, forgetSignIns: false);
            if (_index is not { } index)
            {
                return;
            }
            // The partners the person came from only, subscription or not: the tab is there before the person signs in.
            var from = _edition.CameFrom;
            // The debug window's partner of the same id stays as it is: it stands in for this one on purpose.
            foreach (var listing in index.Partners.Where(l => from.Contains(l.Id) && l.Until >= Today() && !_debugIds.ContainsKey(l.Id)))
            {
                var kept = Get(listing.Id);
                if (kept != null && kept.Listing.Zip.Sha256 == listing.Zip.Sha256)
                {
                    continue;
                }
                var partner = await DownloadAsync(listing, cancellationToken);
                if (partner != null)
                {
                    _errors.TryRemove(listing.Id, out _);
                    _partners[partner.Id] = partner;
                    Changed?.Invoke();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Asks every source at once and takes, for each signer (the root key, the hub's), the newest index served that is not
    /// older than the one kept of that signer; then goes by their merge. The hub's index is usually the newer and the
    /// GitHub release's copy the fallback, but each signer is judged against its own kept index: a lagging mirror of
    /// either is skipped, and neither can push the other out by its date.
    /// </summary>
    private async Task RefreshIndexAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var served = await Task.WhenAll(Sources.Select(async source =>
        {
            var jsonTask = GetBytesAsync($"{source}/latest/download/{V2pnPartnerTrust.IndexFile}", V2pnPartnerTrust.MaxIndexBytes, cancellationToken);
            var sigTask = GetTextAsync($"{source}/latest/download/{V2pnPartnerTrust.IndexFile}.sig", cancellationToken);
            return (Source: source, Json: await jsonTask, Sig: await sigTask);
        }));
        var taken = new Dictionary<V2pnPartnerSigner, (V2pnPartnerIndex Index, byte[] Json, string Sig, string Source)>();
        foreach (var (source, json, sig) in served)
        {
            if (V2pnPartnerTrust.ReadIndex(json, sig, _rootKeys, _hubKeys, out var error) is not { } index)
            {
                errors.Add($"{HostOf(source)}: {error}");
                continue;
            }
            // An older index signed earlier would bring back a partner taken out since; a mirror that lags is skipped.
            if (index.Published < (Kept(index.Signer)?.Published ?? DateTimeOffset.MinValue))
            {
                errors.Add($"{HostOf(source)}: the partner index is older than the one kept");
                continue;
            }
            // Of two sources with the same signer, the newer; on a tie the one asked first.
            if (taken.TryGetValue(index.Signer, out var other) && other.Index.Published >= index.Published)
            {
                continue;
            }
            taken[index.Signer] = (index, json!, sig!, source);
        }
        if (taken.Count == 0)
        {
            IndexError = errors.Count == 0 ? "No source to ask for the partner index." : string.Join("; ", errors);
            Logging.SaveLog($"{Tag}: {IndexError}");
            return;
        }
        var root = _root();
        Directory.CreateDirectory(root);
        foreach (var (signer, (index, json, sig, source)) in taken)
        {
            foreach (var problem in index.Problems)
            {
                Logging.SaveLog($"{Tag}: {signer.ToString().ToLowerInvariant()} index: {problem}");
            }
            var file = signer == V2pnPartnerSigner.Hub ? HubIndexFile : V2pnPartnerTrust.IndexFile;
            await File.WriteAllBytesAsync(Path.Combine(root, file), json, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, file + ".sig"), sig, cancellationToken);
            SetKept(index);
            _servedBy[signer] = source;
            Logging.SaveLog($"{Tag}: partner index from {HostOf(source)}, signed with the {signer.ToString().ToLowerInvariant()} key, published {index.Published:u}: {(index.Partners.Count == 0 ? "no partners" : string.Join(", ", index.Partners.Select(p => p.Id)))}");
        }
        var merged = _index = V2pnPartnerIndex.Merge(_hubIndex, _rootIndex)!;
        foreach (var problem in merged.Problems.Except(_hubIndex?.Problems ?? []).Except(_rootIndex?.Problems ?? []))
        {
            Logging.SaveLog($"{Tag}: index: {problem}");
        }
        IndexError = null;
        // The listings may have changed (grants, term, reviewed files, signer): the kept packs are read again under them.
        foreach (var kept in _partners.Values.Where(p => !_debugIds.ContainsKey(p.Id)).ToList())
        {
            if (merged.Get(kept.Id) is { } listing && ReadFolder(kept.Folder, listing, out _) is { } again)
            {
                _partners[kept.Id] = again;
            }
        }
        Changed?.Invoke();
    }

    private V2pnPartnerIndex? Kept(V2pnPartnerSigner signer) => signer == V2pnPartnerSigner.Hub ? _hubIndex : _rootIndex;

    private void SetKept(V2pnPartnerIndex index)
    {
        if (index.Signer == V2pnPartnerSigner.Hub)
        {
            _hubIndex = index;
        }
        else
        {
            _rootIndex = index;
        }
    }

    /// <summary>The zip of <paramref name="listing"/> from the first source that serves the one the index names, unpacked and checked.</summary>
    private async Task<(byte[] Zip, V2pnPartnerPack Pack, Dictionary<string, byte[]> Files)?> FetchPackAsync(V2pnPartnerListing listing, CancellationToken cancellationToken)
    {
        var error = "No source serves the pack.";
        // The source of the index that named it first: the hub keeps the zips of its index, a release those of its own.
        var first = _servedBy.GetValueOrDefault(listing.Signer);
        foreach (var source in Sources.OrderBy(s => s == first ? 0 : 1))
        {
            var zip = await GetBytesAsync($"{source}/latest/download/{listing.Zip.File}", V2pnPartnerTrust.MaxZipBytes, cancellationToken);
            if (V2pnPartnerTrust.Unpack(zip, listing, out var files, out error) is { } pack)
            {
                return (zip!, pack, files);
            }
        }
        _errors[listing.Id] = error;
        Logging.SaveLog($"{Tag}: {listing.Id}: {error}");
        return null;
    }

    private async Task<V2pnPartner?> DownloadAsync(V2pnPartnerListing listing, CancellationToken cancellationToken)
    {
        if (await FetchPackAsync(listing, cancellationToken) is (var zip, var pack, var files))
        {
            var root = _root();
            Directory.CreateDirectory(root);
            var staging = Path.Combine(root, "." + listing.Id + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(staging);
            try
            {
                foreach (var (name, content) in files)
                {
                    await File.WriteAllBytesAsync(Path.Combine(staging, name), content, cancellationToken);
                }
                await File.WriteAllBytesAsync(Path.Combine(staging, KeptZip), zip, cancellationToken);
                pack = V2pnPartnerSlots.Fit(pack, name => files.GetValueOrDefault(name), out var notes);
                _slotNotes[listing.Id] = notes;
                var features = ReadFeatures(listing, pack, name => files.GetValueOrDefault(name));

                var target = Path.Combine(root, listing.Id);
                // A virus scanner opens files just written; Windows then refuses the delete or the move for a moment.
                await RetryAsync(() =>
                {
                    if (Directory.Exists(target))
                    {
                        Directory.Delete(target, true);
                    }
                    Directory.Move(staging, target);
                }, cancellationToken);
                Logging.SaveLog($"{Tag}: {listing.Id}: pack {listing.Zip.Sha256[..12]} taken");
                return new V2pnPartner(listing, pack, target, features);
            }
            finally
            {
                DeleteQuietly(staging);
            }
        }
        return null;
    }

    /// <summary>
    /// The partners a person can sign in with (V2pnPartnerLogin): every listed partner whose pack says "login" and has an
    /// API under the account grant, the build's marker partner first. Called when the person opens the sign-in (and, in
    /// a build with a marker, for the walkthrough): it asks for the index when there is none, even for a person who came
    /// from no partner yet, since signing in is how one comes from a partner. A kept partner is read from its folder; any
    /// other one's pack is downloaded and checked like a kept one, but only shown: it gets no tab until the sign-in
    /// succeeds, and goes with the next start (guiConfigs/partners/.offers).
    /// </summary>
    public async Task<IReadOnlyList<V2pnPartner>> LoginOffersAsync(CancellationToken cancellationToken = default)
    {
        await LoadAsync();
        if (_index == null)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_index == null)
                {
                    _indexAsked = Now();
                    await RefreshIndexAsync(cancellationToken);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        if (_index is not { } index)
        {
            return [];
        }
        var offers = new List<V2pnPartner>();
        foreach (var listing in index.Partners.Where(l => l.Until >= Today() && l.Allows(V2pnPartnerGrants.Account)))
        {
            var partner = Get(listing.Id) is { } kept && !IsDebug(kept.Id) ? kept : await OfferAsync(listing, cancellationToken);
            if (partner != null && V2pnPartnerLogin.Offers(partner))
            {
                offers.Add(partner);
            }
        }
        return offers.OrderBy(p => p.Id == _edition.Marker ? 0 : 1).ThenBy(p => p.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>
    /// The listing a provider's link names (v2pn://add?url=…&amp;partner=&lt;id&gt;) when the last index lists
    /// <paramref name="partnerId"/>, in term, and <paramref name="subscriptionUrl"/> is on its domains; null otherwise.
    /// Asks nothing: what the window may say before the person answers.
    /// </summary>
    public V2pnPartnerListing? ListingForLink(string? partnerId, string? subscriptionUrl) =>
        V2pnPartnerTrust.ValidId(partnerId) && _index?.Get(partnerId) is { } listing && listing.Until >= Today()
        && V2pnPartnerTrust.DomainAllowed(listing, subscriptionUrl)
            ? listing
            : null;

    /// <summary>
    /// A provider's link, once the person added (or already had) its subscription: when the signed index lists
    /// <paramref name="partnerId"/>, in term, and <paramref name="subscriptionUrl"/> is on its domains, the person came
    /// from that provider from now on, as by a sign-in by code (V2pnEdition.RecordSignIn), and its listing is returned:
    /// the caller opens its tab and refreshes, which brings the pack. Any other link (an unknown id, a link off its
    /// domains, a provider out of term) is just a subscription: null, and nothing is recorded. When the kept index does not
    /// list the provider yet, it is asked for (the person followed a provider's link: like the sign-in, a reason to ask),
    /// at most once every <see cref="LinkIndexInterval"/>.
    /// </summary>
    public async Task<V2pnPartnerListing?> ComeFromLinkAsync(string? partnerId, string? subscriptionUrl, CancellationToken cancellationToken = default)
    {
        if (!V2pnPartnerTrust.ValidId(partnerId) || !V2pnSubs.IsHttpLink(subscriptionUrl))
        {
            return null;
        }
        await LoadAsync();
        if (ListingForLink(partnerId, subscriptionUrl) == null)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (ListingForLink(partnerId, subscriptionUrl) == null && Now() - _indexAsked >= LinkIndexInterval)
                {
                    _indexAsked = Now();
                    await RefreshIndexAsync(cancellationToken);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        if (ListingForLink(partnerId, subscriptionUrl) is not { } listing)
        {
            var known = _index?.Get(partnerId);
            Logging.SaveLog($"{Tag}: a link names {partnerId}, " + (known == null ? "which the partner index does not list"
                : known.Until < Today() ? "whose term ran out" : $"but its subscription is not on its domains ({HostOf(subscriptionUrl!)})")
                + "; it is just a subscription");
            return null;
        }
        _edition.RecordSignIn(listing.Id);
        Logging.SaveLog($"{Tag}: came from {listing.Id} by its link ({listing.Signer.ToString().ToLowerInvariant()}-signed listing)");
        return listing;
    }

    private readonly ConcurrentDictionary<string, V2pnPartner> _offers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _offerGate = new(1, 1);

    private async Task<V2pnPartner?> OfferAsync(V2pnPartnerListing listing, CancellationToken cancellationToken)
    {
        await _offerGate.WaitAsync(cancellationToken);
        try
        {
            if (_offers.TryGetValue(listing.Id, out var known) && known.Listing.Zip.Sha256 == listing.Zip.Sha256 && Directory.Exists(known.Folder))
            {
                return known;
            }
            if (await FetchPackAsync(listing, cancellationToken) is not (_, var pack, var files))
            {
                return null;
            }
            // Its pictures (the mark the sign-in shows) on disk, as a kept pack's are; nothing else of it is used.
            var folder = Path.Combine(_root(), ".offers", listing.Id);
            DeleteQuietly(folder);
            Directory.CreateDirectory(folder);
            foreach (var (name, content) in files)
            {
                await File.WriteAllBytesAsync(Path.Combine(folder, name), content, cancellationToken);
            }
            pack = V2pnPartnerSlots.Fit(pack, name => files.GetValueOrDefault(name), out _);
            var offer = new V2pnPartner(listing, pack, folder);
            _offers[listing.Id] = offer;
            return offer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logging.SaveLog($"{Tag}: {listing.Id} offer", ex);
            return null;
        }
        finally
        {
            _offerGate.Release();
        }
    }

    /// <summary>
    /// The partner kept in <paramref name="dir"/>, checked again from its files under <paramref name="listing"/>: the kept zip
    /// must unpack as a downloaded one does and every file beside it must be the zip's. A zip older than the listing's (its
    /// download failed so far) still counts: the owner approved it when it was taken. Null with the reason when anything fails.
    /// </summary>
    public V2pnPartner? ReadFolder(string dir, V2pnPartnerListing listing, out string error)
    {
        try
        {
            if (Path.GetFileName(dir) != listing.Id)
            {
                error = "The kept partner was renamed.";
                return null;
            }
            var zip = ReadOrNull(dir, KeptZip, V2pnPartnerTrust.MaxZipBytes);
            if (zip == null)
            {
                error = "The kept pack is missing.";
                return null;
            }
            var kept = listing with { Zip = listing.Zip with { Size = zip.LongLength, Sha256 = Convert.ToHexStringLower(SHA256.HashData(zip)) } };
            var pack = V2pnPartnerTrust.Unpack(zip, kept, out var files, out error);
            if (pack == null)
            {
                return null;
            }
            foreach (var (name, content) in files)
            {
                var onDisk = ReadOrNull(dir, name, V2pnPartnerTrust.MaxAssetBytes);
                if (onDisk == null || !onDisk.AsSpan().SequenceEqual(content))
                {
                    error = $"The kept file {name} changed.";
                    return null;
                }
            }
            pack = V2pnPartnerSlots.Fit(pack, name => files.GetValueOrDefault(name), out var notes);
            _slotNotes[listing.Id] = notes;
            error = string.Empty;
            return new V2pnPartner(kept, pack, dir, ReadFeatures(listing, pack, name => files.GetValueOrDefault(name)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Drops a partner when the index no longer lists it, its term ran out, or the person no longer comes from it. Its
    /// kept files go too, so nothing of a former partner stays on the computer. A partner the person came from stays
    /// without a subscription (its tab is there before the sign-in). A sign-in by code is forgotten when no subscription
    /// here is on that partner's domains any more (the person deleted the last one), and its partner goes with it; the
    /// build's marker is never forgotten.
    /// </summary>
    /// <param name="subscriptionUrls">The link of every subscription here.</param>
    public IReadOnlyList<V2pnPartner> Prune(IEnumerable<string?> subscriptionUrls)
    {
        _gate.Wait();
        try
        {
            return PruneCore(subscriptionUrls.ToList(), forgetSignIns: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    // forgetSignIns only with links read after what prompted it (a deletion, the start), never with links read before a
    // sign-in that may be adding its subscription at this moment.
    private List<V2pnPartner> PruneCore(IReadOnlyList<string?> urls, bool forgetSignIns)
    {
        if (forgetSignIns && _index is { } index)
        {
            foreach (var id in _edition.SignedIn.Where(id => id != _edition.Marker))
            {
                // A partner the index does not list (taken out, or no index yet) says nothing of which links are its: it stays.
                if (index.Get(id) is { } signedIn && !urls.Any(u => V2pnPartnerTrust.DomainAllowed(signedIn, u)))
                {
                    Logging.SaveLog($"{Tag}: no subscription from {id} is left; its sign-in is forgotten");
                    _edition.ForgetSignIn(id);
                }
            }
        }
        var from = _edition.CameFrom;
        var gone = new List<(V2pnPartner Partner, V2pnPartnerGone Reason)>();
        foreach (var partner in _partners.Values.Where(p => !_debugIds.ContainsKey(p.Id)).ToList())
        {
            var listing = _index?.Get(partner.Id);
            V2pnPartnerGone? reason = listing == null ? V2pnPartnerGone.Revoked
                : listing.Until < Today() ? V2pnPartnerGone.Expired
                : !from.Contains(partner.Id) ? V2pnPartnerGone.Unlinked
                : null;
            if (reason is { } why && _partners.TryRemove(partner.Id, out _))
            {
                DeleteQuietly(partner.Folder);
                _errors.TryRemove(partner.Id, out _);
                gone.Add((partner, why));
                Logging.SaveLog($"{Tag}: dropped {partner.Id} ({why})");
            }
        }
        foreach (var (partner, why) in gone)
        {
            Removed?.Invoke(partner, why);
        }
        if (gone.Count > 0)
        {
            Changed?.Invoke();
        }
        return gone.Select(g => g.Partner).ToList();
    }

    /// <summary><see cref="Prune"/> against the subscriptions in the database, once the kept partners are loaded (after a subscription was deleted, and at start).</summary>
    public async Task<IReadOnlyList<V2pnPartner>> PruneAsync()
    {
        try
        {
            await LoadAsync();
            await _gate.WaitAsync();
            try
            {
                // Read under the gate, after whatever deleted a subscription: a sign-in is forgotten only by what is here now.
                return PruneCore(await SubscriptionUrls(), forgetSignIns: true);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            return [];
        }
    }

    /// <summary>
    /// Debug only (V2pnPartnerTrust.DebugAllowed): a partner made up in memory with the edition, account and actions grants,
    /// no API, counted as one the person came from until V2pn closes (so it has a tab), so the look can be tried without
    /// a published index. With <paramref name="themeFolder"/> (a folder with a partner.json and its pictures) it takes
    /// that pack's brand, theme and pictures. Null in a plain release.
    /// </summary>
    public V2pnPartner? AddDebugPartner(string id, string name, string? accent, string? themeFolder = null)
    {
        if (!V2pnPartnerTrust.DebugAllowed || !V2pnPartnerTrust.ValidId(id))
        {
            return null;
        }
        var listing = new V2pnPartnerListing(id, name, ["localhost"], [V2pnPartnerGrants.Edition, V2pnPartnerGrants.Account, V2pnPartnerGrants.Actions],
            Today().AddYears(1), new V2pnPartnerZip(V2pnPartnerTrust.ZipName(id), 1, new string('0', 64)));
        var folder = Path.Combine(Path.GetTempPath(), "v2pn-debug-partner-" + id);
        var assets = new List<V2pnPartnerAsset>();
        JsonObject? root = null;
        if (themeFolder != null && File.Exists(Path.Combine(themeFolder, V2pnPartnerTrust.PackFile)))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllBytes(Path.Combine(themeFolder, V2pnPartnerTrust.PackFile))) as JsonObject;
                Directory.CreateDirectory(folder);
                foreach (var file in Directory.GetFiles(themeFolder).Where(f => V2pnPartnerSlots.ImageSize(File.ReadAllBytes(f)) != null).Take(V2pnPartnerTrust.MaxAssets))
                {
                    var bytes = File.ReadAllBytes(file);
                    File.WriteAllBytes(Path.Combine(folder, Path.GetFileName(file)), bytes);
                    assets.Add(new V2pnPartnerAsset(Path.GetFileName(file), bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes))));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Logging.SaveLog($"{Tag}: debug theme {themeFolder}", ex);
                root = null;
            }
        }
        string? Picture(string? file) => file != null && assets.Any(a => a.Name == file) ? file : null;
        var brand = root?["brand"] as JsonObject;
        accent = V2pnColor.TryParse(brand?["accent"]?.GetValue<string>())?.Hex ?? accent;
        var theme = V2pnThemeReader.Parse(root?["theme"], accent, f => Picture(f) != null);
        var pack = new V2pnPartnerPack(id, name, accent, Picture(brand?["logo"]?.GetValue<string>()), Picture(brand?["logoOnLight"]?.GetValue<string>()),
            null, null, null, assets, Picture(brand?["mark"]?.GetValue<string>()),
            Actions: V2pnPartnerCatalog.ParseActions(root?["actions"], listing, hasApi: false), Theme: theme);
        pack = V2pnPartnerSlots.Fit(pack, file => File.Exists(Path.Combine(folder, file)) ? File.ReadAllBytes(Path.Combine(folder, file)) : null, out _);
        var partner = new V2pnPartner(listing, pack, folder);
        _debugIds[id] = 0;
        _partners[id] = partner;
        _edition.AddDebug(id);
        Changed?.Invoke();
        return partner;
    }

    public bool IsDebug(string? id) => id != null && _debugIds.ContainsKey(id);

    /// <summary>Debug only: drops a made-up partner, as if <paramref name="why"/> happened (the look closes with its note).</summary>
    public void RemoveDebugPartner(string id, V2pnPartnerGone why)
    {
        if (_debugIds.TryRemove(id, out _) && _partners.TryRemove(id, out var partner))
        {
            foreach (var sub in _debugSubs.Where(s => s.Value == id).Select(s => s.Key).ToList())
            {
                _debugSubs.TryRemove(sub, out _);
            }
            Removed?.Invoke(partner, why);
            _edition.RemoveDebug(id);
            Changed?.Invoke();
        }
    }

    /// <summary>Debug only: counts a subscription as a partner's (null: as V2pn sees it), in that partner's tab.</summary>
    public void SetDebugSubscription(string subscriptionId, string? partnerId)
    {
        if (!V2pnPartnerTrust.DebugAllowed || (partnerId != null && !_edition.IsFrom(partnerId)))
        {
            return;
        }
        if (partnerId == null)
        {
            _debugSubs.TryRemove(subscriptionId, out _);
        }
        else
        {
            _debugSubs[subscriptionId] = partnerId;
        }
        Changed?.Invoke();
    }

    public string? DebugPartnerOf(string? subscriptionId) =>
        subscriptionId != null && _debugSubs.TryGetValue(subscriptionId, out var id) ? id : null;

    /// <summary>The reviewed files; a file that is not used is logged with why, the rest of the partner works as before.</summary>
    private static V2pnPartnerFeatures ReadFeatures(V2pnPartnerListing listing, V2pnPartnerPack pack, Func<string, byte[]?> content)
    {
        var features = V2pnPartnerFeatures.Read(listing, pack, content);
        foreach (var file in features.Files.Where(f => f.State != V2pnReviewState.Ready))
        {
            Logging.SaveLog($"{Tag}: {listing.Id}: {file.Name} is not used: {file.Problem}");
        }
        return features;
    }

    private static string HostOf(string source) => Utils.TryUri(source)?.Host is { Length: > 0 } host ? host : source;

    private async Task<byte[]?> GetBytesAsync(string url, int maxBytes, CancellationToken cancellationToken)
    {
        var result = await Fetch(url, cancellationToken);
        return result.Ok && result.Body!.Length <= maxBytes ? result.Body : null;
    }

    private async Task<string?> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        var body = await GetBytesAsync(url, 4096, cancellationToken);
        return body == null ? null : Encoding.UTF8.GetString(body).Trim();
    }

    /// <summary>Direct first; through the VPN when the release page does not answer directly, as updates do.</summary>
    private static async Task<V2pnHttp.Result> DefaultFetchAsync(string url, CancellationToken cancellationToken)
    {
        var result = await V2pnHttp.GetAsync(url, null, V2pnPartnerTrust.MaxZipBytes, cancellationToken);
        if (result.Status == null && await V2pnHttp.LocalProxyAsync(cancellationToken) is { } proxy)
        {
            result = await V2pnHttp.GetAsync(url, proxy, V2pnPartnerTrust.MaxZipBytes, cancellationToken);
        }
        return result;
    }

    private static async Task<IReadOnlyList<string?>> DefaultSubscriptionUrlsAsync()
    {
        var subs = await AppManager.Instance.SubItems() ?? [];
        return subs.Select(s => (string?)s.Url).ToList();
    }

    private static async Task RetryAsync(Action act, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                act();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 10)
            {
                await Task.Delay(50 * attempt, cancellationToken);
            }
        }
    }

    private static void DeleteQuietly(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A staging folder left behind is removed on the next start.
        }
    }

    private static byte[]? ReadOrNull(string dir, string name, int maxBytes)
    {
        var path = Path.Combine(dir, name);
        return File.Exists(path) && new FileInfo(path).Length <= maxBytes ? File.ReadAllBytes(path) : null;
    }

    private static string? ReadTextOrNull(string dir, string name) => ReadOrNull(dir, name, 4096) is { } bytes ? Encoding.UTF8.GetString(bytes).Trim() : null;
}
