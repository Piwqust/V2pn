namespace ServiceLib;

/// <summary>A partner whose certificate and pack passed every check, with its files on disk.</summary>
public sealed class V2pnPartner(V2pnPartnerCert cert, V2pnPartnerPack pack, string folder, string sourceUrl, V2pnPartnerFeatures? features = null)
{
    public V2pnPartnerCert Cert { get; } = cert;
    public V2pnPartnerPack Pack { get; } = pack;

    /// <summary>Its reviewed files (routing.json, transport.json, core.json) and what of them V2pn may use.</summary>
    public V2pnPartnerFeatures Features { get; } = features ?? V2pnPartnerFeatures.None;
    public string Id => Cert.Partner;
    public string Name => Pack.Name;

    /// <summary>The v2pn-partner folder it came from.</summary>
    public string SourceUrl { get; } = sourceUrl;

    /// <summary>Its own edition with the variants of its pack; false when its certificate grants only its name and links.</summary>
    public bool HasEdition => Pack.Variants.Count > 0;

    /// <summary>The local file of a pack asset, or null when the pack has no such asset.</summary>
    public string? AssetPath(string? name) => Pack.Asset(name) == null ? null : Path.Combine(Folder, name!);

    /// <summary>Where its checked files are kept.</summary>
    public string Folder { get; } = folder;
}

/// <summary>Why a partner was dropped.</summary>
public enum V2pnPartnerGone
{
    /// <summary>No subscription points to it any more: deleted, or its panel stopped naming the pack.</summary>
    Unlinked,

    /// <summary>Its certificate ran out.</summary>
    Expired,

    /// <summary>The owner of V2pn withdrew its certificate.</summary>
    Revoked,
}

/// <summary>
/// The partners of the subscriptions on this computer. A subscription whose panel sends v2pn-partner gets
/// its partner's signed pack downloaded, checked (V2pnPartnerTrust) and kept in guiConfigs/partners/&lt;id&gt;;
/// every start checks the kept files again, so a pack changed on disk is not trusted either.
/// </summary>
public sealed class V2pnPartners
{
    private static readonly Lazy<V2pnPartners> _instance = new(() => new(V2pnInfo.PartnerKeys, () => Utils.GetConfigPath("partners")));
    public static V2pnPartners Instance => _instance.Value;

    private const string Tag = nameof(V2pnPartners);
    private const string SourceFile = "source.url";
    private const string RevokedFile = "partners-revoked.json";

    private readonly IReadOnlyList<string> _rootKeys;
    private readonly Func<string> _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, V2pnPartner> _partners = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _errors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _slotNotes = new(StringComparer.Ordinal);
    private HashSet<string> _revoked = [];
    // The debug window's made-up partners and the subscriptions it counts as theirs; never on disk, never pruned.
    private readonly ConcurrentDictionary<string, byte> _debugIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, V2pnTheme> _debugThemes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _debugSubs = new(StringComparer.Ordinal);
    private Task? _load;
    private bool _revocationsChecked;

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

    /// <summary>Overridable for tests: the date certificates are checked against.</summary>
    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTime.UtcNow);

    public IReadOnlyList<V2pnPartner> All => _partners.Values.OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();

    public V2pnPartner? Get(string? id) => id != null && _partners.TryGetValue(id, out var partner) ? partner : null;

    /// <summary>The partner of a subscription: the one its panel's v2pn-partner folder belongs to, if verified.</summary>
    public V2pnPartner? ForSubscription(SubItem? item)
    {
        if (item?.Id is { } id && _debugSubs.TryGetValue(id, out var debugPartner) && Get(debugPartner) is { } linked)
        {
            return linked;
        }
        var info = SubInfoManager.Instance.Get(item?.Id);
        return ForSource(info?.PartnerUrl, item?.Url);
    }

    public V2pnPartner? ForSource(string? partnerUrl, string? subscriptionUrl)
    {
        if (partnerUrl.IsNullOrEmpty())
        {
            return null;
        }
        var partner = _partners.Values.FirstOrDefault(p => p.SourceUrl == partnerUrl);
        // A pack only dresses subscriptions from its certificate's domains, however the link reached the app.
        return partner != null && V2pnPartnerTrust.DomainAllowed(partner.Cert, subscriptionUrl) ? partner : null;
    }

    /// <summary>Pictures of a partner's pack that did not fit their slot and are not shown (V2pnPartnerSlots).</summary>
    public IReadOnlyList<string> SlotNotesFor(string? partnerId) =>
        partnerId != null && _slotNotes.TryGetValue(partnerId, out var notes) ? notes : [];

    /// <summary>Why the last check of a v2pn-partner folder failed; null after a success or before any check.</summary>
    public string? ErrorFor(string? partnerUrl) => partnerUrl != null && _errors.TryGetValue(partnerUrl, out var error) ? error : null;

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
            _revoked = ReadKeptRevocations(root) ?? [];
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                if (Path.GetFileName(dir).StartsWith('.'))
                {
                    DeleteQuietly(dir);
                    continue;
                }
                var partner = ReadFolder(dir, out var error);
                if (partner == null)
                {
                    Logging.SaveLog($"{Tag}: {Path.GetFileName(dir)} is no longer trusted: {error}");
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
    /// Downloads and checks the pack a subscription's panel points to, and keeps it when it passes. Runs one at
    /// a time; a failure keeps the partner already on disk (a provider's site being down changes nothing).
    /// </summary>
    public async Task<V2pnPartner?> RefreshAsync(string subscriptionUrl, string partnerUrl, CancellationToken cancellationToken = default)
    {
        await LoadAsync();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_revocationsChecked)
            {
                _revocationsChecked = true;
                await RefreshRevocationsAsync(cancellationToken);
            }
            var partner = await DownloadAsync(subscriptionUrl, partnerUrl, cancellationToken);
            if (partner != null)
            {
                _errors.TryRemove(partnerUrl, out _);
                _partners[partner.Id] = partner;
                Changed?.Invoke();
            }
            return partner;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            _errors[partnerUrl] = ex.Message;
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<V2pnPartner?> DownloadAsync(string subscriptionUrl, string partnerUrl, CancellationToken cancellationToken)
    {
        string? Fail(string error)
        {
            _errors[partnerUrl] = error;
            Logging.SaveLog($"{Tag}: {Utils.TryUri(partnerUrl)?.Host}: {error}");
            return null;
        }

        var certJson = await GetBytesAsync(partnerUrl + "cert.json", V2pnPartnerTrust.MaxCertBytes, cancellationToken);
        var certSig = await GetTextAsync(partnerUrl + "cert.json.sig", cancellationToken);
        var cert = V2pnPartnerTrust.ReadCert(certJson, certSig, _rootKeys, Today(), out var error);
        if (cert == null)
        {
            Fail(error);
            return null;
        }
        if (_revoked.Contains(V2pnPartnerTrust.RevocationKey(cert)))
        {
            Fail("The partner certificate was withdrawn.");
            return null;
        }
        if (!V2pnPartnerTrust.DomainAllowed(cert, partnerUrl) || !V2pnPartnerTrust.DomainAllowed(cert, subscriptionUrl))
        {
            Fail("The subscription or the pack comes from a domain the partner certificate does not name.");
            return null;
        }

        var packJson = await GetBytesAsync(partnerUrl + "partner.json", V2pnPartnerTrust.MaxPackBytes, cancellationToken);
        var packSig = await GetTextAsync(partnerUrl + "partner.json.sig", cancellationToken);
        var pack = V2pnPartnerTrust.ReadPack(packJson, packSig, cert, out error);
        if (pack == null)
        {
            Fail(error);
            return null;
        }

        var root = _root();
        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, "." + cert.Partner + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);
        try
        {
            var pictures = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var asset in pack.Assets)
            {
                var content = await GetBytesAsync(partnerUrl + Uri.EscapeDataString(asset.Name), V2pnPartnerTrust.MaxAssetBytes, cancellationToken);
                if (content == null || !V2pnPartnerTrust.AssetMatches(asset, content))
                {
                    Fail($"The file {asset.Name} does not match the signed pack.");
                    return null;
                }
                pictures[asset.Name] = content;
                await File.WriteAllBytesAsync(Path.Combine(staging, asset.Name), content, cancellationToken);
            }
            pack = V2pnPartnerSlots.Fit(pack, name => pictures.GetValueOrDefault(name), out var notes);
            _slotNotes[cert.Partner] = notes;
            var features = ReadFeatures(cert, pack, name => pictures.GetValueOrDefault(name));
            await File.WriteAllBytesAsync(Path.Combine(staging, "cert.json"), certJson!, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(staging, "cert.json.sig"), certSig!, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(staging, "partner.json"), packJson!, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(staging, "partner.json.sig"), packSig!, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(staging, SourceFile), partnerUrl, cancellationToken);

            var target = Path.Combine(root, cert.Partner);
            // A virus scanner opens files just written; Windows then refuses the delete or the move for a moment.
            await RetryAsync(() =>
            {
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }
                Directory.Move(staging, target);
            }, cancellationToken);
            return new V2pnPartner(cert, pack, target, partnerUrl, features);
        }
        finally
        {
            DeleteQuietly(staging);
        }
    }

    /// <summary>The partner kept in <paramref name="dir"/>, checked again from its files; null when anything fails.</summary>
    public V2pnPartner? ReadFolder(string dir, out string error)
    {
        try
        {
            var cert = V2pnPartnerTrust.ReadCert(ReadOrNull(dir, "cert.json"), ReadTextOrNull(dir, "cert.json.sig"), _rootKeys, Today(), out error);
            if (cert == null)
            {
                return null;
            }
            if (cert.Partner != Path.GetFileName(dir) || _revoked.Contains(V2pnPartnerTrust.RevocationKey(cert)))
            {
                error = "The kept partner was withdrawn or renamed.";
                return null;
            }
            var pack = V2pnPartnerTrust.ReadPack(ReadOrNull(dir, "partner.json"), ReadTextOrNull(dir, "partner.json.sig"), cert, out error);
            var source = ReadTextOrNull(dir, SourceFile)?.Trim();
            if (pack == null || source.IsNullOrEmpty() || !V2pnPartnerTrust.DomainAllowed(cert, source))
            {
                error = error.IsNullOrEmpty() ? "The kept partner has no source." : error;
                return null;
            }
            foreach (var asset in pack.Assets)
            {
                var content = ReadOrNull(dir, asset.Name);
                if (content == null || !V2pnPartnerTrust.AssetMatches(asset, content))
                {
                    error = $"The kept file {asset.Name} changed.";
                    return null;
                }
            }
            pack = V2pnPartnerSlots.Fit(pack, name => ReadOrNull(dir, name), out var notes);
            _slotNotes[cert.Partner] = notes;
            error = string.Empty;
            return new V2pnPartner(cert, pack, dir, source, ReadFeatures(cert, pack, name => ReadOrNull(dir, name)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Drops partners no subscription points to any more (the subscription was deleted, its panel stopped naming the
    /// pack, or its link moved to a domain the certificate does not name) and partners whose certificate ran out
    /// while the app was open. Their kept files go too, so nothing of a former partner stays on the computer.
    /// </summary>
    /// <param name="links">Every subscription as (the v2pn-partner folder its panel named last, its link).</param>
    public IReadOnlyList<V2pnPartner> Prune(IEnumerable<(string? PartnerUrl, string? SubscriptionUrl)> links)
    {
        var linked = links.ToList();
        var gone = new List<(V2pnPartner Partner, V2pnPartnerGone Reason)>();
        foreach (var partner in _partners.Values.Where(p => !_debugIds.ContainsKey(p.Id)).ToList())
        {
            V2pnPartnerGone? reason = partner.Cert.Expires < Today() ? V2pnPartnerGone.Expired
                : linked.Any(l => l.PartnerUrl == partner.SourceUrl && V2pnPartnerTrust.DomainAllowed(partner.Cert, l.SubscriptionUrl)) ? null
                : V2pnPartnerGone.Unlinked;
            if (reason is { } why && _partners.TryRemove(partner.Id, out _))
            {
                DeleteQuietly(partner.Folder);
                _errors.TryRemove(partner.SourceUrl, out _);
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

    /// <summary>
    /// Debug only (V2pnPartnerTrust.DebugAllowed): a partner made up in memory with every look and grant, no API, so every
    /// look and every scenario can be tried without a provider's site. With <paramref name="themeFolder"/> (a folder with a
    /// partner.json and its pictures, unsigned) it takes that pack's brand, theme and pictures, checked as a real pack's
    /// are. Null in a plain release.
    /// </summary>
    public V2pnPartner? AddDebugPartner(string id, string name, string? accent, string? themeFolder = null)
    {
        if (!V2pnPartnerTrust.DebugAllowed)
        {
            return null;
        }
        var today = Today();
        var cert = new V2pnPartnerCert(id, name, ["localhost"], string.Empty, [V2pnPartnerGrants.Edition, V2pnPartnerGrants.Account, V2pnPartnerGrants.Actions], today, today.AddYears(1));
        var folder = Path.Combine(Path.GetTempPath(), "v2pn-debug-partner-" + id);
        var assets = new List<V2pnPartnerAsset>();
        JsonObject? root = null;
        if (themeFolder != null && File.Exists(Path.Combine(themeFolder, "partner.json")))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllBytes(Path.Combine(themeFolder, "partner.json"))) as JsonObject;
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
            V2pnPartnerVariants.All.ToList(), root?["theme"] != null ? V2pnPartnerVariants.Theme : V2pnPartnerVariants.Cabinet,
            new Dictionary<string, string>(), null, null, null, assets, Picture(brand?["mark"]?.GetValue<string>()),
            Actions: V2pnPartnerCatalog.ParseActions(root?["actions"], cert, hasApi: false), Theme: theme);
        pack = V2pnPartnerSlots.Fit(pack, file => File.Exists(Path.Combine(folder, file)) ? File.ReadAllBytes(Path.Combine(folder, file)) : null, out _);
        var partner = new V2pnPartner(cert, pack, folder, "debug://" + id + "/");
        _debugThemes[id] = pack.Theme!;
        _debugIds[id] = 0;
        _partners[id] = partner;
        Changed?.Invoke();
        return partner;
    }

    /// <summary>
    /// Debug only: the made-up partner with its theme as it was added, changed by <paramref name="edit"/> (another
    /// template, say); edits never pile up, so a template that drops a picture does not take it from the next one.
    /// </summary>
    public void EditDebugTheme(string id, Func<V2pnTheme, V2pnTheme> edit)
    {
        if (!_debugIds.ContainsKey(id) || !_partners.TryGetValue(id, out var partner))
        {
            return;
        }
        var theme = edit(_debugThemes.GetValueOrDefault(id) ?? partner.Pack.Theme ?? V2pnTheme.Default(partner.Pack.Accent));
        _partners[id] = new V2pnPartner(partner.Cert, partner.Pack with { Theme = theme }, partner.Folder, partner.SourceUrl, partner.Features);
        Changed?.Invoke();
    }

    public bool IsDebug(string? id) => id != null && _debugIds.ContainsKey(id);

    /// <summary>
    /// The id of the theme builder's preview. A certificate's id never has a dot, so no real partner can be it.
    /// </summary>
    public const string PreviewId = "builder.preview";

    private string? _previewFolder;

    public static bool IsPreview(string? id) => id == PreviewId;

    /// <summary>
    /// Developer mode only (V2pnPartnerTrust.DebugAllowed): the unsigned pack in <paramref name="folder"/> (partner.json and its
    /// pictures, as the theme builder saves them) worn by this running V2pn as a preview. It is read with a real pack's checks
    /// from a copy in the temp folder, never written to the partners' folder, never pruned, and gone when V2pn closes or
    /// <see cref="RemovePreview"/> is called. Null with the reason when the pack cannot be read.
    /// </summary>
    public V2pnPartner? SetPreview(string folder, out string error)
    {
        if (!V2pnPartnerTrust.DebugAllowed)
        {
            error = "The preview is there in developer mode only.";
            return null;
        }
        var copy = Path.Combine(Path.GetTempPath(), "v2pn-preview-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(copy);
            foreach (var name in V2pnPackWriter.Files(folder).Append(V2pnPackWriter.PackFile))
            {
                File.Copy(Path.Combine(folder, name), Path.Combine(copy, name), true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(copy);
            error = ex.Message;
            return null;
        }
        var partner = ReadUnsigned(copy, PreviewId, Today(), out _, out error);
        if (partner == null)
        {
            DeleteQuietly(copy);
            return null;
        }
        var old = _previewFolder;
        _previewFolder = copy;
        _debugIds[PreviewId] = 0;
        _partners[PreviewId] = partner;
        if (old != null)
        {
            DeleteQuietly(old);
        }
        Changed?.Invoke();
        return partner;
    }

    /// <summary>Takes the theme builder's preview away; nothing of it stays.</summary>
    public void RemovePreview()
    {
        var had = _partners.TryRemove(PreviewId, out _);
        _debugIds.TryRemove(PreviewId, out _);
        foreach (var sub in _debugSubs.Where(s => s.Value == PreviewId).Select(s => s.Key).ToList())
        {
            _debugSubs.TryRemove(sub, out _);
        }
        if (_previewFolder != null)
        {
            DeleteQuietly(_previewFolder);
            _previewFolder = null;
        }
        if (had)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// The unsigned pack in <paramref name="folder"/> as partner <paramref name="partnerId"/> under a made-up certificate with the
    /// edition, account and actions grants for the domains its links name: what the theme builder draws and previews. Its
    /// "assets" are taken from the folder as tools/partner.cs pack would write them; pictures are fitted to their slots.
    /// </summary>
    public static V2pnPartner? ReadUnsigned(string folder, string partnerId, DateOnly today, out IReadOnlyList<string> slotNotes, out string error)
    {
        slotNotes = [];
        if (V2pnPackWriter.Read(folder, out error) is not { } root)
        {
            return null;
        }
        try
        {
            root["id"] = partnerId;
            // A name still being typed draws as "Provider" rather than taking the preview away.
            var name = V2pnPartnerTrust.ValidName(root["name"] is JsonValue v && v.TryGetValue<string>(out var text) ? text : null) ?? "Provider";
            root["name"] = name;
            var json = V2pnPackWriter.Bytes(root, folder);
            var domains = new List<string> { "localhost" };
            foreach (var url in new[] { root["links"]?["cabinet"], root["links"]?["support"], root["api"] }
                         .Concat((root["actions"] as JsonArray)?.Select(a => a?["url"]) ?? []))
            {
                if (url is JsonValue value && value.TryGetValue<string>(out var link) && Utils.TryUri(link) is { } uri && uri.IdnHost.Length > 0)
                {
                    domains.Add(uri.IdnHost.TrimEnd('.').ToLowerInvariant());
                }
            }
            var cert = new V2pnPartnerCert(partnerId, name, domains.Distinct().ToList(), string.Empty,
                [V2pnPartnerGrants.Edition, V2pnPartnerGrants.Account, V2pnPartnerGrants.Actions], today, today.AddYears(1));
            var pack = V2pnPartnerTrust.ReadUnsignedPack(json, cert, out error);
            if (pack == null)
            {
                return null;
            }
            pack = V2pnPartnerSlots.Fit(pack, file => ReadOrNull(folder, file), out slotNotes);
            return new V2pnPartner(cert, pack, folder, "preview://" + partnerId + "/");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = ex.Message;
            return null;
        }
    }

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

    /// <summary><see cref="Prune"/> against the subscriptions in the database, once the kept partners are loaded.</summary>
    public async Task<IReadOnlyList<V2pnPartner>> PruneAsync()
    {
        try
        {
            await LoadAsync();
            await SubInfoManager.Instance.LoadAsync();
            var subs = await AppManager.Instance.SubItems() ?? [];
            await _gate.WaitAsync();
            try
            {
                return Prune(subs.Select(s => (SubInfoManager.Instance.Get(s.Id)?.PartnerUrl, (string?)s.Url)));
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

    /// <summary>Fetches the signed list of withdrawn certificates from where releases come from; keeps the last good one.</summary>
    private async Task RefreshRevocationsAsync(CancellationToken cancellationToken)
    {
        foreach (var source in V2pnInfo.UpdateSources)
        {
            var json = await GetBytesAsync($"{source}/latest/download/{RevokedFile}", V2pnPartnerTrust.MaxCertBytes, cancellationToken);
            var sig = await GetTextAsync($"{source}/latest/download/{RevokedFile}.sig", cancellationToken);
            if (V2pnPartnerTrust.ReadRevoked(json, sig, _rootKeys) is not { } revoked)
            {
                continue;
            }
            var root = _root();
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(Path.Combine(root, RevokedFile), json!, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, RevokedFile + ".sig"), sig!, cancellationToken);
            _revoked = revoked;
            foreach (var partner in _partners.Values.Where(p => revoked.Contains(V2pnPartnerTrust.RevocationKey(p.Cert))).ToList())
            {
                if (_partners.TryRemove(partner.Id, out _))
                {
                    DeleteQuietly(partner.Folder);
                    Removed?.Invoke(partner, V2pnPartnerGone.Revoked);
                    Changed?.Invoke();
                }
            }
            return;
        }
    }

    /// <summary>The reviewed files; a file that is not used is logged with why, the rest of the partner works as before.</summary>
    private static V2pnPartnerFeatures ReadFeatures(V2pnPartnerCert cert, V2pnPartnerPack pack, Func<string, byte[]?> content)
    {
        var features = V2pnPartnerFeatures.Read(cert, pack, content);
        foreach (var file in features.Files.Where(f => f.State != V2pnReviewState.Ready))
        {
            Logging.SaveLog($"{Tag}: {cert.Partner}: {file.Name} is not used: {file.Problem}");
        }
        return features;
    }

    private HashSet<string>? ReadKeptRevocations(string root) =>
        V2pnPartnerTrust.ReadRevoked(ReadOrNull(root, RevokedFile), ReadTextOrNull(root, RevokedFile + ".sig"), _rootKeys);

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

    /// <summary>Direct first; through the VPN when the provider's site does not answer directly, as subscriptions do.</summary>
    private static async Task<V2pnHttp.Result> DefaultFetchAsync(string url, CancellationToken cancellationToken)
    {
        var result = await V2pnHttp.GetAsync(url, null, V2pnPartnerTrust.MaxAssetBytes, cancellationToken);
        if (result.Status == null && await V2pnHttp.LocalProxyAsync(cancellationToken) is { } proxy)
        {
            result = await V2pnHttp.GetAsync(url, proxy, V2pnPartnerTrust.MaxAssetBytes, cancellationToken);
        }
        return result;
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

    private static byte[]? ReadOrNull(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        return File.Exists(path) && new FileInfo(path).Length <= V2pnPartnerTrust.MaxAssetBytes ? File.ReadAllBytes(path) : null;
    }

    private static string? ReadTextOrNull(string dir, string name) => ReadOrNull(dir, name) is { } bytes ? Encoding.UTF8.GetString(bytes).Trim() : null;
}
