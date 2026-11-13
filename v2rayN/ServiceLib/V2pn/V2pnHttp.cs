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

    public static async Task<Result> GetAsync(string url, IWebProxy? proxy, int maxBytes, CancellationToken cancellationToken = default)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                Proxy = proxy,
                UseProxy = proxy != null,
                ConnectTimeout = ConnectTimeout,
                AutomaticDecompression = DecompressionMethods.All,
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.TryParseAdd($"{Global.AppName}/{V2pnInfo.Version}");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TotalTimeout);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
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
