namespace ServiceLib;

/// <summary>A partner whose certificate and pack passed every check, with its files on disk.</summary>
public sealed class V2pnPartner(V2pnPartnerCert cert, V2pnPartnerPack pack, string folder, string sourceUrl)
{
    public V2pnPartnerCert Cert { get; } = cert;
    public V2pnPartnerPack Pack { get; } = pack;
    public string Id => Cert.Partner;
    public string Name => Pack.Name;

    /// <summary>The v2pn-partner folder it came from.</summary>
    public string SourceUrl { get; } = sourceUrl;

    /// <summary>Its own edition with the variants of its pack; false when its certificate grants only its name and links.</summary>
    public bool HasEdition => Pack.Variants.Count > 0;

    /// <summary>The local file of a pack asset, or null when the pack has no such asset.</summary>
    public string? AssetPath(string? name) => Pack.Asset(name) == null ? null : Path.Combine(folder, name!);
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
    private HashSet<string> _revoked = [];
    private Task? _load;
    private bool _revocationsChecked;

    /// <summary>Raised after the set of partners or one of them changed. May come from any thread.</summary>
    public event Action? Changed;

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
            foreach (var asset in pack.Assets)
            {
                var content = await GetBytesAsync(partnerUrl + Uri.EscapeDataString(asset.Name), V2pnPartnerTrust.MaxAssetBytes, cancellationToken);
                if (content == null || !V2pnPartnerTrust.AssetMatches(asset, content))
                {
                    Fail($"The picture {asset.Name} does not match the signed pack.");
                    return null;
                }
                await File.WriteAllBytesAsync(Path.Combine(staging, asset.Name), content, cancellationToken);
            }
            await File.WriteAllBytesAsync(Path.Combine(staging, "cert.json"), certJson!, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(staging, "cert.json.sig"), certSig!, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(staging, "partner.json"), packJson!, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(staging, "partner.json.sig"), packSig!, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(staging, SourceFile), partnerUrl, cancellationToken);

            var target = Path.Combine(root, cert.Partner);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
            Directory.Move(staging, target);
            return new V2pnPartner(cert, pack, target, partnerUrl);
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
                    error = $"The kept picture {asset.Name} changed.";
                    return null;
                }
            }
            error = string.Empty;
            return new V2pnPartner(cert, pack, dir, source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
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
                _partners.TryRemove(partner.Id, out _);
                Changed?.Invoke();
            }
            return;
        }
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
