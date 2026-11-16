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
    /// <summary>No subscription on this computer is from its domains any more.</summary>
    Unlinked,

    /// <summary>Its term in the index ran out.</summary>
    Expired,

    /// <summary>The owner of V2pn took it out of the index.</summary>
    Revoked,
}

/// <summary>
/// The partners of the subscriptions on this computer. V2pn publishes partners.json with its releases, signed with the
/// partner root key: every partner with its subscription domains, grants, term and the hash of its pack's zip. A
/// subscription from a partner's domains gets that zip downloaded, checked (V2pnPartnerTrust) and kept in
/// guiConfigs/partners/&lt;id&gt; with the index it was checked against; every start checks the kept files again, so a pack
/// changed on disk is not trusted either.
/// </summary>
public sealed class V2pnPartners
{
    private static readonly Lazy<V2pnPartners> _instance = new(() => new(V2pnInfo.PartnerKeys, () => Utils.GetConfigPath("partners")));
    public static V2pnPartners Instance => _instance.Value;

    /// <summary>How often the index is asked for at most, unless a caller forces it.</summary>
    public static readonly TimeSpan IndexInterval = TimeSpan.FromHours(6);

    private const string Tag = nameof(V2pnPartners);
    private const string KeptZip = "pack.zip";

    private readonly IReadOnlyList<string> _rootKeys;
    private readonly Func<string> _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, V2pnPartner> _partners = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _errors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _slotNotes = new(StringComparer.Ordinal);
    // The debug window's made-up partners and the subscriptions it counts as theirs; never on disk, never pruned.
    private readonly ConcurrentDictionary<string, byte> _debugIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _debugSubs = new(StringComparer.Ordinal);
    private V2pnPartnerIndex? _index;
    private DateTimeOffset _indexPublished = DateTimeOffset.MinValue;
    private DateTime _indexAsked = DateTime.MinValue;
    private Task? _load;

    /// <summary>Raised after the set of partners or one of them changed. May come from any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised for each partner dropped, with the reason, before <see cref="Changed"/>. May come from any thread.</summary>
    public event Action<V2pnPartner, V2pnPartnerGone>? Removed;

    public V2pnPartners(IReadOnlyList<string> rootKeys, Func<string> root)
    {
        _rootKeys = rootKeys;
        _root = root;
    }

    /// <summary>Overridable for tests: the network.</summary>
    public Func<string, CancellationToken, Task<V2pnHttp.Result>> Fetch { get; set; } = DefaultFetchAsync;

    /// <summary>Overridable for tests: where the index and the zips are published (a releases page: &lt;source&gt;/latest/download/...).</summary>
    public IReadOnlyList<string> Sources { get; set; } = V2pnInfo.UpdateSources;

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

    /// <summary>The partner of a subscription: the kept partner whose domains hold its link.</summary>
    public V2pnPartner? ForSubscription(SubItem? item)
    {
        if (item?.Id is { } id && _debugSubs.TryGetValue(id, out var debugPartner) && Get(debugPartner) is { } linked)
        {
            return linked;
        }
        return ForUrl(item?.Url);
    }

    /// <summary>The partner a subscription link belongs to, by its host; the one listed first when several match.</summary>
    public V2pnPartner? ForUrl(string? subscriptionUrl) =>
        _index?.ForUrl(subscriptionUrl) is { } listing && Get(listing.Id) is { } partner && !_debugIds.ContainsKey(partner.Id) ? partner : null;

    /// <summary>The listing a subscription link belongs to, whether or not its pack is here yet.</summary>
    public V2pnPartnerListing? ListingFor(string? subscriptionUrl) => _index?.ForUrl(subscriptionUrl);

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
            var index = V2pnPartnerTrust.ReadIndex(ReadOrNull(root, V2pnPartnerTrust.IndexFile, V2pnPartnerTrust.MaxIndexBytes),
                ReadTextOrNull(root, V2pnPartnerTrust.IndexFile + ".sig"), _rootKeys, out var indexError);
            if (index == null && File.Exists(Path.Combine(root, V2pnPartnerTrust.IndexFile)))
            {
                Logging.SaveLog($"{Tag}: the kept partner index is not trusted: {indexError}");
            }
            if (index != null)
            {
                _index = index;
                _indexPublished = ReadPublished(root);
            }
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var id = Path.GetFileName(dir);
                var listing = index?.Get(id);
                if (id.StartsWith('.') || listing == null || listing.Until < Today())
                {
                    // A staging folder left behind, or a partner the index no longer lists or whose term ran out.
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
    /// downloads the pack of every partner a subscription here belongs to whose zip is not the one kept, and drops the
    /// partners that left the index, ran out or have no subscription here. Runs one at a time; a failure keeps what is
    /// kept (a release page being down changes nothing).
    /// </summary>
    public async Task RefreshAsync(bool forceIndex = false, CancellationToken cancellationToken = default)
    {
        await LoadAsync();
        var urls = await SubscriptionUrls();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (forceIndex || Now() - _indexAsked >= IndexInterval)
            {
                _indexAsked = Now();
                await RefreshIndexAsync(cancellationToken);
            }
            PruneCore(urls);
            if (_index is not { } index)
            {
                return;
            }
            foreach (var listing in index.Partners.Where(l => l.Until >= Today() && urls.Any(u => V2pnPartnerTrust.DomainAllowed(l, u))))
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

    /// <summary>Takes the index from the first source that serves one signed by a root key and not older than the kept one.</summary>
    private async Task RefreshIndexAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var source in Sources)
        {
            var json = await GetBytesAsync($"{source}/latest/download/{V2pnPartnerTrust.IndexFile}", V2pnPartnerTrust.MaxIndexBytes, cancellationToken);
            var sig = await GetTextAsync($"{source}/latest/download/{V2pnPartnerTrust.IndexFile}.sig", cancellationToken);
            if (V2pnPartnerTrust.ReadIndex(json, sig, _rootKeys, out var error) is not { } index)
            {
                errors.Add($"{Utils.TryUri(source)?.Host}: {error}");
                continue;
            }
            var published = Published(json!);
            // An older index signed earlier would bring back a partner taken out since; a mirror that lags is skipped.
            if (published < _indexPublished)
            {
                errors.Add($"{Utils.TryUri(source)?.Host}: the partner index is older than the one kept");
                continue;
            }
            foreach (var problem in index.Problems)
            {
                Logging.SaveLog($"{Tag}: index: {problem}");
            }
            var root = _root();
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(Path.Combine(root, V2pnPartnerTrust.IndexFile), json!, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, V2pnPartnerTrust.IndexFile + ".sig"), sig!, cancellationToken);
            _index = index;
            _indexPublished = published;
            IndexError = null;
            // The listings may have changed (grants, term, reviewed files): the kept packs are read again under them.
            foreach (var kept in _partners.Values.Where(p => !_debugIds.ContainsKey(p.Id)).ToList())
            {
                if (index.Get(kept.Id) is { } listing && ReadFolder(kept.Folder, listing, out _) is { } again)
                {
                    _partners[kept.Id] = again;
                }
            }
            Changed?.Invoke();
            return;
        }
        IndexError = errors.Count == 0 ? "No source to ask for the partner index." : string.Join("; ", errors);
        Logging.SaveLog($"{Tag}: {IndexError}");
    }

    private async Task<V2pnPartner?> DownloadAsync(V2pnPartnerListing listing, CancellationToken cancellationToken)
    {
        var error = "No source serves the pack.";
        foreach (var source in Sources)
        {
            var zip = await GetBytesAsync($"{source}/latest/download/{listing.Zip.File}", V2pnPartnerTrust.MaxZipBytes, cancellationToken);
            var pack = V2pnPartnerTrust.Unpack(zip, listing, out var files, out error);
            if (pack == null)
            {
                continue;
            }
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
                await File.WriteAllBytesAsync(Path.Combine(staging, KeptZip), zip!, cancellationToken);
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
        _errors[listing.Id] = error;
        Logging.SaveLog($"{Tag}: {listing.Id}: {error}");
        return null;
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
    /// Drops partners the index no longer lists, whose term ran out, or from whose domains no subscription here is (the
    /// subscription was deleted or moved). Their kept files go too, so nothing of a former partner stays on the computer.
    /// </summary>
    /// <param name="subscriptionUrls">The link of every subscription here.</param>
    public IReadOnlyList<V2pnPartner> Prune(IEnumerable<string?> subscriptionUrls)
    {
        _gate.Wait();
        try
        {
            return PruneCore(subscriptionUrls.ToList());
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<V2pnPartner> PruneCore(IReadOnlyList<string?> urls)
    {
        var gone = new List<(V2pnPartner Partner, V2pnPartnerGone Reason)>();
        foreach (var partner in _partners.Values.Where(p => !_debugIds.ContainsKey(p.Id)).ToList())
        {
            var listing = _index?.Get(partner.Id);
            V2pnPartnerGone? reason = listing == null ? V2pnPartnerGone.Revoked
                : listing.Until < Today() ? V2pnPartnerGone.Expired
                : urls.Any(u => V2pnPartnerTrust.DomainAllowed(listing, u)) ? null
                : V2pnPartnerGone.Unlinked;
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

    /// <summary><see cref="Prune"/> against the subscriptions in the database, once the kept partners are loaded.</summary>
    public async Task<IReadOnlyList<V2pnPartner>> PruneAsync()
    {
        try
        {
            await LoadAsync();
            var urls = await SubscriptionUrls();
            await _gate.WaitAsync();
            try
            {
                return PruneCore(urls);
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
    /// no API, so the look can be tried without a published index. With <paramref name="themeFolder"/> (a folder with a
    /// partner.json and its pictures) it takes that pack's brand, theme and pictures. Null in a plain release.
    /// </summary>
    public V2pnPartner? AddDebugPartner(string id, string name, string? accent, string? themeFolder = null)
    {
        if (!V2pnPartnerTrust.DebugAllowed)
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
            Changed?.Invoke();
        }
    }

    /// <summary>Debug only: counts a subscription as a partner's (null: as V2pn sees it).</summary>
    public void SetDebugSubscription(string subscriptionId, string? partnerId)
    {
        if (!V2pnPartnerTrust.DebugAllowed)
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

    /// <summary>"published" of an index already checked; the oldest time when it has none V2pn reads.</summary>
    private static DateTimeOffset Published(byte[] json)
    {
        try
        {
            return JsonNode.Parse(json)?["published"] is JsonValue value && value.TryGetValue<string>(out var text)
                   && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var time)
                ? time
                : DateTimeOffset.MinValue;
        }
        catch (JsonException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    private static DateTimeOffset ReadPublished(string root) =>
        ReadOrNull(root, V2pnPartnerTrust.IndexFile, V2pnPartnerTrust.MaxIndexBytes) is { } json ? Published(json) : DateTimeOffset.MinValue;

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
