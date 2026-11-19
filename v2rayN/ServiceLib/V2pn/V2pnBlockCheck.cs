using System.Net.Http;
using System.Net.Quic;

namespace ServiceLib;

/// <summary>What the network check found, as one answer the person can act on.</summary>
public enum EV2pnBlockVerdict
{
    /// <summary>Nothing points at a block: the server carries traffic, small and large.</summary>
    Fine,

    /// <summary>Neither Russian nor foreign sites open directly: the internet itself is down.</summary>
    NoInternet,

    /// <summary>Russian sites open, foreign ones do not: the operator lets only approved addresses through.</summary>
    Whitelist,

    /// <summary>Foreign sites open, the server's address does not: the address (or its hosting's network) is blocked.</summary>
    ServerBlocked,

    /// <summary>Small requests pass through the server, larger downloads stop after the first kilobytes.</summary>
    Freeze,

    /// <summary>The server's port opens, but nothing passes through the connection: the filter cuts it.</summary>
    ProxyCut,

    /// <summary>The checks disagree or could not be made.</summary>
    Unknown,
}

/// <summary>One check: null when it was not made.</summary>
public sealed record V2pnProbe(string Name, bool? Ok, int Ms = 0, string? Detail = null);

public sealed record V2pnBlockReport(
    EV2pnBlockVerdict Verdict,
    bool DirectChecked,
    bool? RussianSites,
    bool? ForeignSites,
    bool? ServerReachable,
    bool? QuicWorks,
    bool? ProxySmall,
    bool? ProxyLarge,
    long LargeBytes,
    IReadOnlyList<V2pnProbe> Probes);

/// <summary>
/// The network check of "Обход блокировок". Run only when the person asks (or the bypass's own tuning needs it):
/// it opens a few Russian and foreign sites directly, the server's port, QUIC, and, when connected, a small and a
/// large download through the server. In VPN mode the direct checks would go through the tunnel, so they are skipped.
/// Each address is asked once; nothing runs in a burst.
/// </summary>
public sealed class V2pnBlockCheck
{
    public static readonly string[] RussianUrls = ["https://ya.ru/", "https://vk.com/"];
    public static readonly string[] ForeignUrls = ["https://www.google.com/generate_204", "https://github.com/", "https://www.wikipedia.org/"];
    public static readonly string[] QuicHosts = ["cloudflare-quic.com", "www.google.com"];
    public const string SmallUrl = "https://www.gstatic.com/generate_204";

    // Large enough to pass the ~16 KB where frozen connections stop, small enough to cost nothing.
    public const int LargeBytes = 256 * 1024;
    public static readonly string[] LargeUrls = [$"https://speed.cloudflare.com/__down?bytes={LargeBytes}", "https://proof.ovh.net/files/1Mb.dat"];

    /// <summary>Downloads that stopped below this many bytes stopped where the filter freezes connections.</summary>
    public const int FreezeCeiling = 96 * 1024;

    private static readonly TimeSpan SiteTimeout = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(8);

    private readonly ProfileItem? _server;
    private readonly IWebProxy? _proxy;
    private readonly bool _tun;
    private readonly List<V2pnProbe> _probes = [];

    /// <param name="server">The selected server; its port is opened directly.</param>
    /// <param name="proxy">The running connection's local proxy; null when disconnected.</param>
    /// <param name="tun">VPN mode is on: direct connections would go through the tunnel.</param>
    public V2pnBlockCheck(ProfileItem? server, IWebProxy? proxy, bool tun)
    {
        _server = server;
        _proxy = proxy;
        _tun = tun;
    }

    public async Task<V2pnBlockReport> RunAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        bool? russian = null, foreign = null, server = null, quic = null, small = null, large = null;
        long largeBytes = 0;
        if (!_tun)
        {
            progress?.Report("russian");
            russian = await AnyAsync("ru", RussianUrls, ct);
            progress?.Report("foreign");
            foreign = await AnyAsync("foreign", ForeignUrls, ct);
            progress?.Report("server");
            server = await ServerPortAsync(ct);
            if (foreign == true)
            {
                progress?.Report("quic");
                quic = await QuicAsync(ct);
            }
        }
        if (_proxy != null)
        {
            progress?.Report("proxy");
            small = await SiteAsync("proxy-small", SmallUrl, _proxy, ct);
            if (small == true)
            {
                progress?.Report("large");
                (large, largeBytes) = await LargeAsync(ct);
            }
        }
        var verdict = Decide(!_tun, russian, foreign, server, small, large, largeBytes);
        return new(verdict, !_tun, russian, foreign, server, quic, small, large, largeBytes, _probes.ToList());
    }

    /// <summary>The answer from the checks' results; null results were not checked.</summary>
    public static EV2pnBlockVerdict Decide(bool directChecked, bool? russian, bool? foreign, bool? server,
        bool? proxySmall, bool? proxyLarge, long largeBytes)
    {
        if (proxySmall == true && proxyLarge == true)
        {
            return EV2pnBlockVerdict.Fine;
        }
        if (directChecked)
        {
            if (russian == false && foreign == false)
            {
                return EV2pnBlockVerdict.NoInternet;
            }
            if (russian == true && foreign == false)
            {
                return EV2pnBlockVerdict.Whitelist;
            }
            if (foreign == true && server == false)
            {
                return EV2pnBlockVerdict.ServerBlocked;
            }
        }
        if (proxySmall == true && proxyLarge == false)
        {
            return largeBytes < FreezeCeiling ? EV2pnBlockVerdict.Freeze : EV2pnBlockVerdict.Unknown;
        }
        if (proxySmall == false && server != false)
        {
            return EV2pnBlockVerdict.ProxyCut;
        }
        if (proxySmall == null && directChecked && russian == true && foreign == true && server != false)
        {
            // Not connected: the network and the server's port are fine, which is all that can be said.
            return EV2pnBlockVerdict.Fine;
        }
        return EV2pnBlockVerdict.Unknown;
    }

    private async Task<bool?> AnyAsync(string name, IEnumerable<string> urls, CancellationToken ct)
    {
        foreach (var url in urls)
        {
            if (await SiteAsync(name, url, null, ct) == true)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Any HTTP answer counts: the question is whether the site can be reached at all.</summary>
    private async Task<bool?> SiteAsync(string name, string url, IWebProxy? proxy, CancellationToken ct)
    {
        using var client = NewClient(proxy);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(SiteTimeout);
        var timer = Stopwatch.StartNew();
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            _probes.Add(new(name, true, (int)timer.ElapsedMilliseconds, $"{Host(url)} {(int)response.StatusCode}"));
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _probes.Add(new(name, false, (int)timer.ElapsedMilliseconds, $"{Host(url)} timeout"));
        }
        catch (HttpRequestException ex)
        {
            _probes.Add(new(name, false, (int)timer.ElapsedMilliseconds, $"{Host(url)} {Short(ex)}"));
        }
        return false;
    }

    private async Task<bool?> ServerPortAsync(CancellationToken ct)
    {
        if (_server == null || _server.Address.IsNullOrEmpty() || _server.Port <= 0 || UsesUdp(_server))
        {
            _probes.Add(new("server", null));
            return null;
        }
        var timer = Stopwatch.StartNew();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(6));
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(_server.Address, _server.Port, deadline.Token);
            _probes.Add(new("server", true, (int)timer.ElapsedMilliseconds));
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _probes.Add(new("server", false, (int)timer.ElapsedMilliseconds, "timeout"));
        }
        catch (SocketException ex)
        {
            _probes.Add(new("server", false, (int)timer.ElapsedMilliseconds, ex.SocketErrorCode.ToString()));
        }
        return false;
    }

    /// <summary>
    /// A QUIC handshake (ALPN h3) to sites that speak it; any one is enough. Google's is not used alone: on Russian
    /// networks QUIC to Google and YouTube is cut by name while QUIC as such still works. Null where the system has
    /// no QUIC (macOS without msquic).
    /// </summary>
    private async Task<bool?> QuicAsync(CancellationToken ct)
    {
#pragma warning disable CA2252, CA1416
        if (!QuicConnection.IsSupported)
        {
            _probes.Add(new("quic", null, Detail: "unsupported"));
            return null;
        }
        foreach (var host in QuicHosts)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(6));
            var timer = Stopwatch.StartNew();
            try
            {
                await using var connection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
                {
                    RemoteEndPoint = new DnsEndPoint(host, 443),
                    DefaultStreamErrorCode = 0,
                    DefaultCloseErrorCode = 0,
                    ClientAuthenticationOptions = new System.Net.Security.SslClientAuthenticationOptions
                    {
                        TargetHost = host,
                        ApplicationProtocols = [new System.Net.Security.SslApplicationProtocol("h3")],
                    },
                }, deadline.Token);
                _probes.Add(new("quic", true, (int)timer.ElapsedMilliseconds, host));
                return true;
            }
            catch (Exception ex) when (ex is QuicException or OperationCanceledException or System.Security.Authentication.AuthenticationException
                                           or SocketException && !ct.IsCancellationRequested)
            {
                _probes.Add(new("quic", false, (int)timer.ElapsedMilliseconds, $"{host} {(ex is OperationCanceledException ? "timeout" : ex.GetType().Name)}"));
            }
        }
#pragma warning restore CA2252, CA1416
        return false;
    }

    /// <summary>Downloads past the frozen kilobytes through the server; a stop is a pause of several seconds with no bytes.</summary>
    private async Task<(bool? Ok, long Bytes)> LargeAsync(CancellationToken ct)
    {
        long best = 0;
        foreach (var url in LargeUrls)
        {
            var (ok, bytes, answered) = await DownloadAsync(url, ct);
            best = Math.Max(best, bytes);
            if (ok)
            {
                return (true, bytes);
            }
            if (answered)
            {
                // The site answered and then stopped sending: that is the answer, a second site would only repeat it.
                return (false, bytes);
            }
        }
        return (false, best);
    }

    private async Task<(bool Ok, long Bytes, bool Answered)> DownloadAsync(string url, CancellationToken ct)
    {
        using var client = NewClient(_proxy);
        var timer = Stopwatch.StartNew();
        long read = 0;
        var answered = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, LargeBytes - 1);
            using var headers = CancellationTokenSource.CreateLinkedTokenSource(ct);
            headers.CancelAfter(SiteTimeout);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token);
            answered = true;
            if (!response.IsSuccessStatusCode)
            {
                _probes.Add(new("proxy-large", false, (int)timer.ElapsedMilliseconds, $"{Host(url)} {(int)response.StatusCode}"));
                return (false, 0, false);
            }
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[16 * 1024];
            while (read < LargeBytes)
            {
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(StallTimeout);
                var n = await stream.ReadAsync(buffer, stall.Token);
                if (n == 0)
                {
                    break;
                }
                read += n;
            }
            var ok = read >= LargeBytes || (response.Content.Headers.ContentLength is { } length && read >= length && length > FreezeCeiling);
            _probes.Add(new("proxy-large", ok, (int)timer.ElapsedMilliseconds, $"{Host(url)} {read / 1024} KB"));
            return (ok, read, true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _probes.Add(new("proxy-large", false, (int)timer.ElapsedMilliseconds, $"{Host(url)} stalled at {read / 1024} KB"));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _probes.Add(new("proxy-large", false, (int)timer.ElapsedMilliseconds, $"{Host(url)} {Short(ex)} at {read / 1024} KB"));
        }
        return (false, read, answered);
    }

    public static bool UsesUdp(ProfileItem server) =>
        server.ConfigType is EConfigType.Hysteria2 or EConfigType.TUIC or EConfigType.WireGuard or EConfigType.MASQUE
        || server.GetNetwork() is nameof(ETransport.kcp) or nameof(ETransport.quic);

    private static HttpClient NewClient(IWebProxy? proxy) => new(new SocketsHttpHandler
    {
        Proxy = proxy,
        UseProxy = proxy != null,
        AllowAutoRedirect = false,
        ConnectTimeout = SiteTimeout,
    });

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;

    private static string Short(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null)
        {
            inner = inner.InnerException;
        }
        return inner is SocketException se ? se.SocketErrorCode.ToString() : inner.GetType().Name;
    }
}
