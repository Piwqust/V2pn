namespace ServiceLib;

/// <summary>
/// The partner's live word about its servers (<c>GET servers</c>), kept per partner and asked again no more often
/// than the account call (every 30 minutes, while the window shows the partner's subscription or look). With the
/// pack's notes it describes each server: <see cref="Describe"/>.
/// </summary>
public sealed class V2pnPartnerLive
{
    private static readonly Lazy<V2pnPartnerLive> _instance = new(() => new V2pnPartnerLive());
    public static V2pnPartnerLive Instance => _instance.Value;

    public static readonly TimeSpan Every = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, (DateTime Asked, IReadOnlyList<V2pnServerNote>? Notes)> _servers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _busy = new(StringComparer.Ordinal);

    /// <summary>Overridable for tests: the API call.</summary>
    public Func<V2pnPartner, string?, CancellationToken, Task<IReadOnlyList<V2pnServerNote>?>> FetchServers { get; set; } =
        (partner, token, ct) => V2pnPartnerApi.GetServersAsync(partner, token, ct);

    /// <summary>Raised after a partner's live notes changed. May come from any thread.</summary>
    public event Action? Changed;

    /// <summary>The last notes the partner's API gave; null before the first answer or without an API.</summary>
    public IReadOnlyList<V2pnServerNote>? Servers(string? partnerId) =>
        partnerId != null && _servers.TryGetValue(partnerId, out var entry) ? entry.Notes : null;

    /// <summary>The pack's and the API's word about a server of <paramref name="partner"/>, or null.</summary>
    public V2pnServerNote? Describe(V2pnPartner? partner, string? remarks, string? address) =>
        partner == null ? null : V2pnPartnerChoice.Describe(partner.Pack.ServerNotes, Servers(partner.Id), remarks, address);

    /// <summary>The partner's word about a server of the subscription <paramref name="subId"/>; null when it has no verified partner or says nothing.</summary>
    public V2pnServerNote? Describe(string? subId, string? remarks, string? address) =>
        V2pnLocks.Instance.PartnerOf(subId) is { } partner ? Describe(partner, remarks, address) : null;

    /// <summary>Whether the partner hides this server from the list (only its API's recommendation can still pick it).</summary>
    public bool Hidden(string? subId, string? remarks, string? address) => Describe(subId, remarks, address)?.IsHidden == true;

    /// <summary>
    /// Asks the API when the last question is older than <see cref="Every"/> (or <paramref name="force"/>). A failed
    /// call keeps the last answer. True when it asked and the notes changed.
    /// </summary>
    public async Task<bool> RefreshAsync(V2pnPartner partner, string? token, DateTime now, bool force = false, CancellationToken cancellationToken = default)
    {
        if (partner.Pack.ApiUrl == null || !partner.Listing.Allows(V2pnPartnerGrants.Actions))
        {
            return false;
        }
        if (!force && _servers.TryGetValue(partner.Id, out var last) && now - last.Asked < Every)
        {
            return false;
        }
        if (!_busy.TryAdd(partner.Id, 0))
        {
            return false;
        }
        try
        {
            var previous = Servers(partner.Id);
            _servers[partner.Id] = (now, previous);
            var notes = await FetchServers(partner, token, cancellationToken);
            if (notes == null)
            {
                return false;
            }
            _servers[partner.Id] = (now, notes);
            if (previous != null && previous.SequenceEqual(notes, NoteComparer.Instance))
            {
                return false;
            }
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logging.SaveLog(nameof(V2pnPartnerLive), ex);
            return false;
        }
        finally
        {
            _busy.TryRemove(partner.Id, out _);
        }
    }

    /// <summary>Forgets a partner's notes (it was dropped).</summary>
    public void Forget(string partnerId) => _servers.TryRemove(partnerId, out _);

    private sealed class NoteComparer : IEqualityComparer<V2pnServerNote>
    {
        public static readonly NoteComparer Instance = new();

        public bool Equals(V2pnServerNote? x, V2pnServerNote? y) =>
            x == y || (x != null && y != null && x with { Purposes = [] } == y with { Purposes = [] } && x.Purposes.SequenceEqual(y.Purposes));

        public int GetHashCode(V2pnServerNote obj) => HashCode.Combine(obj.Name, obj.Host, obj.Load);
    }
}

/// <summary>One service check.check.services runs: whether the site opens through V2pn's connection now.</summary>
public sealed record V2pnServiceResult(string Name, bool Opens, int Milliseconds);

/// <summary>
/// check.services: does YouTube, Telegram and ChatGPT open through the current connection. A short HTTPS request to
/// each through V2pn's own local proxy (so the person's routing applies, as in a browser); any answer from the site
/// counts as "opens", a timeout or a refused connection does not.
/// </summary>
public static class V2pnServiceCheck
{
    public static readonly IReadOnlyList<(string Name, string Url)> Services =
    [
        ("YouTube", "https://www.youtube.com/generate_204"),
        ("Telegram", "https://web.telegram.org/"),
        ("ChatGPT", "https://chatgpt.com/"),
    ];

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>Overridable for tests: one request through <paramref name="proxy"/>; the HTTP status, or null for no answer.</summary>
    public static Func<string, IWebProxy?, CancellationToken, Task<HttpStatusCode?>> Probe { get; set; } = DefaultProbeAsync;

    /// <summary>Every service checked at once; null when V2pn is not connected (no local proxy answers).</summary>
    public static async Task<IReadOnlyList<V2pnServiceResult>?> RunAsync(CancellationToken cancellationToken = default)
    {
        var proxy = await V2pnHttp.LocalProxyAsync(cancellationToken);
        if (proxy == null)
        {
            return null;
        }
        return await RunAsync(proxy, cancellationToken);
    }

    public static async Task<IReadOnlyList<V2pnServiceResult>> RunAsync(IWebProxy? proxy, CancellationToken cancellationToken)
    {
        var checks = Services.Select(async service =>
        {
            var watch = Stopwatch.StartNew();
            var status = await Probe(service.Url, proxy, cancellationToken);
            // A site that answers at all opens; "unavailable for legal reasons" is a block, not an answer.
            var opens = status != null && status != HttpStatusCode.UnavailableForLegalReasons;
            return new V2pnServiceResult(service.Name, opens, (int)watch.ElapsedMilliseconds);
        });
        return await Task.WhenAll(checks);
    }

    private static async Task<HttpStatusCode?> DefaultProbeAsync(string url, IWebProxy? proxy, CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                Proxy = proxy,
                UseProxy = proxy != null,
                ConnectTimeout = Timeout,
                AllowAutoRedirect = false,
            };
            using var client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.TryParseAdd($"{Global.AppName}/{V2pnInfo.Version}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.StatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
