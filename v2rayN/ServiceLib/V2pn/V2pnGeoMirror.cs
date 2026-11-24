namespace ServiceLib;

/// <summary>
/// V2pn: the rule lists of the Russian ways (runetfreedom's geoip.dat and geosite.dat) from more than one place, each
/// checked against the publisher's SHA-256. They came only from a GitHub release; with GitHub blocked or slow the Russian
/// ways kept old lists or none. Now a file that did not come, or came changed, is fetched from the release branch on
/// raw.githubusercontent.com, V2pn's own copy on its server (refreshed every six hours) and jsDelivr (files up to
/// 20 MB). Whatever the source, a file is taken only when its hash is one the publisher gives (the release, the branch
/// or jsDelivr, which can lag by hours, so any of them counts): a mirror cannot slip in a list of its own.
/// </summary>
public sealed class V2pnGeoMirror
{
    private const string Repo = "runetfreedom/russia-v2ray-rules-dat";
    private const long MaxBytes = 200L * 1024 * 1024;
    public static readonly IReadOnlyList<string> Files = ["geoip.dat", "geosite.dat"];

    private readonly Dictionary<string, HashSet<string>> _hashes;
    private readonly Dictionary<string, DateTime?> _stamps;

    private V2pnGeoMirror(Dictionary<string, HashSet<string>> hashes, Dictionary<string, DateTime?> stamps)
    {
        _hashes = hashes;
        _stamps = stamps;
    }

    /// <summary>The update under way, so the download can check each file it got.</summary>
    public static V2pnGeoMirror? Current { get; private set; }

    /// <summary>Whether the rule lists come from runetfreedom (the "Russia" sources V2pn's Russian ways use).</summary>
    public static bool Applies(Config config) => config.ConstItem.GeoSourceUrl == Global.GeoFilesSources[1];

    /// <summary>Where a file is fetched when the release did not give it, in order.</summary>
    public static IReadOnlyList<string> Mirrors(string file) =>
    [
        $"https://raw.githubusercontent.com/{Repo}/release/{file}",
        $"{V2pnInfo.SiteUrl}/geo/runetfreedom/{file}",
        $"https://cdn.jsdelivr.net/gh/{Repo}@release/{file}",
        $"https://fastly.jsdelivr.net/gh/{Repo}@release/{file}",
    ];

    /// <summary>Where the publisher's hash of a file is read; every one that answers counts.</summary>
    public static IReadOnlyList<string> HashSources(string file) =>
    [
        $"https://github.com/{Repo}/releases/latest/download/{file}.sha256sum",
        $"https://raw.githubusercontent.com/{Repo}/release/{file}.sha256sum",
        $"https://cdn.jsdelivr.net/gh/{Repo}@release/{file}.sha256sum",
        $"https://fastly.jsdelivr.net/gh/{Repo}@release/{file}.sha256sum",
    ];

    /// <summary>The hash in a ".sha256sum" text ("&lt;64 hex&gt;  geosite.dat"), lower case, or null.</summary>
    public static string? ParseHash(string? text)
    {
        var first = (text ?? string.Empty).Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    /// <summary>Starts an update: reads the publisher's hashes and notes how the files look now.</summary>
    public static async Task<V2pnGeoMirror?> BeginAsync(Config config, CancellationToken cancellationToken)
    {
        if (!Applies(config))
        {
            Current = null;
            return null;
        }
        var proxy = await V2pnHttp.LocalProxyAsync(cancellationToken);
        var hashes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in Files)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in HashSources(file))
            {
                foreach (var route in Routes(proxy))
                {
                    var answer = await V2pnHttp.GetAsync(source, route, 1024, cancellationToken, limits: V2pnHttp.Quick);
                    if (answer.Ok && ParseHash(Encoding.UTF8.GetString(answer.Body!)) is { } hash)
                    {
                        found.Add(hash);
                        break;
                    }
                }
            }
            hashes[file] = found;
        }
        Current = new V2pnGeoMirror(hashes, V2pnRussianSources.StampFiles(Files.Select(f => Utils.GetBinPath(f))));
        return Current;
    }

    /// <summary>The hashes the publisher gives for <paramref name="file"/>; empty when no source answered.</summary>
    public IReadOnlyCollection<string> HashesOf(string file) => _hashes.TryGetValue(file, out var set) ? set : [];

    /// <summary>
    /// Whether a downloaded <paramref name="file"/> at <paramref name="path"/> may replace the one in use: its hash is one
    /// the publisher gives, or no source of hashes answered (then it is taken as before, from the release).
    /// </summary>
    public bool Accept(string file, string path)
    {
        if (!_hashes.TryGetValue(file, out var expected) || expected.Count == 0)
        {
            return true;
        }
        var ok = File.Exists(path) && expected.Contains(Sha256(path));
        if (!ok)
        {
            Logging.SaveLog($"V2pn: {file} does not match the publisher's SHA-256; kept the one in use");
        }
        return ok;
    }

    /// <summary>After the release's download: fetches from the mirrors every file that did not change, then ends the update.</summary>
    public async Task FillGapsAsync(Func<bool, string, Task> report, CancellationToken cancellationToken)
    {
        try
        {
            var proxy = await V2pnHttp.LocalProxyAsync(cancellationToken);
            foreach (var file in Files)
            {
                var target = Utils.GetBinPath(file);
                var stamp = _stamps.GetValueOrDefault(target);
                var refreshed = File.Exists(target) && File.GetLastWriteTimeUtc(target) != stamp;
                if (refreshed || HashesOf(file).Count == 0)
                {
                    // Came from the release, or there is no hash to check a mirror's copy against.
                    continue;
                }
                if (File.Exists(target) && HashesOf(file).Contains(Sha256(target)))
                {
                    continue;
                }
                var source = await FetchAsync(file, target, proxy, cancellationToken);
                await report(false, source != null
                    ? $"V2pn: {file} came from {new Uri(source).Host}, checked against the publisher's SHA-256"
                    : $"V2pn: {file} did not come from any mirror either; the one in use stays");
            }
        }
        finally
        {
            Current = null;
        }
    }

    /// <summary>Downloads <paramref name="file"/> from the first mirror whose copy has the publisher's hash; the source, or null.</summary>
    private async Task<string?> FetchAsync(string file, string target, IWebProxy? proxy, CancellationToken cancellationToken)
    {
        foreach (var url in Mirrors(file))
        {
            foreach (var route in Routes(proxy))
            {
                var part = Utils.GetTempPath(Utils.GetGuid());
                try
                {
                    if (await DownloadAsync(url, route, part, cancellationToken) && Accept(file, part))
                    {
                        File.Copy(part, target, true);
                        return url;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or InvalidDataException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                }
                finally
                {
                    TryDelete(part);
                }
            }
        }
        return null;
    }

    private static async Task<bool> DownloadAsync(string url, IWebProxy? proxy, string path, CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = proxy != null,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AutomaticDecompression = DecompressionMethods.All,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"V2pn/{V2pnInfo.Version}");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
        {
            return false;
        }
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(path);
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxBytes)
            {
                throw new InvalidDataException($"{url} is larger than V2pn takes.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return total > 0;
    }

    private static IEnumerable<IWebProxy?> Routes(IWebProxy? proxy) => proxy == null ? [null] : [null, proxy];

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp file; the next start clears guiTemps anyway.
        }
    }
}
