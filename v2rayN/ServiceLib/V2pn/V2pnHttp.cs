namespace ServiceLib;

/// <summary>
/// Small fetches for the update check: bounded in time and size, and able to say whether the
/// server answered "not there" (no release yet) or did not answer at all.
/// </summary>
public static class V2pnHttp
{
    public sealed record Result(HttpStatusCode? Status, byte[]? Body, string? Error)
    {
        public bool Ok => Status == HttpStatusCode.OK && Body != null;

        public bool NotFound => Status == HttpStatusCode.NotFound;
    }

    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(25);

    /// <summary>The time limits of one fetch: <see cref="ConnectTimeout"/> and <see cref="TotalTimeout"/> unless a caller has a fallback to go to sooner.</summary>
    public sealed record Limits(TimeSpan Connect, TimeSpan Total);

    /// <summary>
    /// For a small file from a source with another one after it: a server that does not answer is left in seconds, not
    /// half a minute. A redirect (V2pn's server sends the releases on to GitHub) is followed within the same limits.
    /// </summary>
    public static readonly Limits Quick = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(12));

    /// <summary>The local SOCKS inbound when a core is listening on it, else null.</summary>
    public static async Task<IWebProxy?> LocalProxyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            using var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(400));
            await tcp.ConnectAsync(Global.Loopback, port, timeout.Token);
            return new WebProxy($"socks5://{Global.Loopback}:{port}");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    /// <param name="bearer">Sent as "Authorization: Bearer"; with it redirects are not followed, so the token reaches only <paramref name="url"/>.</param>
    /// <param name="redirects">False when the address itself carries a secret (a sign-in token): it must not travel on to another host.</param>
    /// <param name="limits">Shorter limits than the usual ones (<see cref="Quick"/>); null for <see cref="ConnectTimeout"/> and <see cref="TotalTimeout"/>.</param>
    public static Task<Result> GetAsync(string url, IWebProxy? proxy, int maxBytes, CancellationToken cancellationToken = default, string? bearer = null, bool redirects = true,
        Limits? limits = null) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), proxy, maxBytes, bearer, redirects && bearer == null, cancellationToken, limits);

    /// <summary>POST of a JSON body; redirects are not followed (a POST that is redirected turns into a GET elsewhere).</summary>
    public static Task<Result> PostJsonAsync(string url, string json, IWebProxy? proxy, int maxBytes, CancellationToken cancellationToken = default) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") },
            proxy, maxBytes, null, false, cancellationToken);

    private static async Task<Result> SendAsync(Func<HttpRequestMessage> request, IWebProxy? proxy, int maxBytes, string? bearer, bool redirects, CancellationToken cancellationToken,
        Limits? limits = null)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                Proxy = proxy,
                UseProxy = proxy != null,
                ConnectTimeout = limits?.Connect ?? ConnectTimeout,
                AutomaticDecompression = DecompressionMethods.All,
                // HttpClient follows https to https across hosts (V2pn's server to GitHub, GitHub to its file host) and
                // never https to http.
                AllowAutoRedirect = redirects,
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.TryParseAdd($"{Global.AppName}/{V2pnInfo.Version}");
            if (bearer != null)
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(limits?.Total ?? TotalTimeout);
            using var message = request();
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new Result(response.StatusCode, null, $"HTTP {(int)response.StatusCode}");
            }
            if (response.Content.Headers.ContentLength > maxBytes)
            {
                return new Result(response.StatusCode, null, "The answer is too large.");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var body = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (body.Length + read > maxBytes)
                {
                    return new Result(response.StatusCode, null, "The answer is too large.");
                }
                body.Write(buffer, 0, read);
            }
            return new Result(response.StatusCode, body.ToArray(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new Result(null, null, "No answer in time.");
        }
        catch (Exception ex)
        {
            return new Result(null, null, (ex.InnerException ?? ex).Message);
        }
    }
}
