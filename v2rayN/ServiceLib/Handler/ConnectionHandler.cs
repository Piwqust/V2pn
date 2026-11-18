namespace ServiceLib.Handler;

public static class ConnectionHandler
{
    /// <summary>Checks the running connection through its local proxy.</summary>
    public static async Task<AvailabilityCheckResult> RunAvailabilityCheck(AvailabilityProbeEndpoint endpoint, string url)
    {
        var proxy = new WebProxy($"socks5://{Global.Loopback}:{endpoint.Port}")
        {
            Credentials = new NetworkCredential(AvailabilityProbeEndpoint.User, endpoint.Password),
        };
        var result = await ProbeAsync(proxy, url, CancellationToken.None);
        return result.IsAvailable
            ? result with { Ip = (await GetIPInfo(proxy))?.ToString() }
            : result;
    }

    public static async Task<int> GetRealPingTime(IWebProxy? webProxy, CancellationToken cancellationToken = default)
    {
        var result = await ProbeAsync(webProxy, AppManager.Instance.Config.SpeedTestItem.SpeedPingTestUrl, cancellationToken);
        return result.IsAvailable ? result.Time : -1;
    }

    private static async Task<AvailabilityCheckResult> ProbeAsync(IWebProxy? proxy, string url, CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = proxy != null,
            AllowAutoRedirect = false,
        });
        return await ProbeHttpAsync(client, url, Global.LocalFetch, cancellationToken);
    }

    internal static async Task<AvailabilityCheckResult> ProbeHttpAsync(HttpClient client, string url, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target)
            || target.Scheme is not ("http" or "https"))
        {
            return new(AvailabilityState.Unreachable);
        }

        // Bundled generate_204 endpoints promise a 204. User-supplied endpoints can return any 2xx.
        var expect204 = target.AbsolutePath == "/generate_204"
            && Global.SpeedPingTestUrls.Any(known => new Uri(known) == target);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            var best = int.MaxValue;
            for (var i = 0; i < 2; i++)
            {
                var timer = Stopwatch.StartNew();
                using var response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                    .ConfigureAwait(false);
                var code = (int)response.StatusCode;
                if (code is 301 or 302 or 303 or 307 or 308)
                {
                    return new(AvailabilityState.Redirected, HttpStatus: code);
                }
                if (!response.IsSuccessStatusCode || (expect204 && code != 204))
                {
                    return new(AvailabilityState.UnexpectedStatus, HttpStatus: code);
                }
                best = Math.Min(best, Math.Max(1, (int)timer.ElapsedMilliseconds));
            }
            return new(AvailabilityState.Available, best);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(AvailabilityState.TimedOut);
        }
        catch (HttpRequestException)
        {
            return new(AvailabilityState.Unreachable);
        }
    }

    /// <summary>
    /// Gets IP and country information through specified proxy.
    /// </summary>
    public static async Task<IpInfoResult?> GetIPInfo(IWebProxy? webProxy, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = AppManager.Instance.Config.SpeedTestItem.IPAPIUrl;
            if (url.IsNullOrEmpty())
            {
                return null;
            }

            var downloadHandle = new DownloadService();
            var result = await downloadHandle.TryDownloadString(url, webProxy, "", cancellationToken);
            if (result == null)
            {
                return null;
            }

            var ipInfo = JsonUtils.Deserialize<IPAPIInfo>(result);
            if (ipInfo == null)
            {
                return null;
            }

            var ip = ipInfo.ip ?? ipInfo.clientIp ?? ipInfo.ip_addr ?? ipInfo.query;
            var country = ipInfo.country_code ?? ipInfo.country ?? ipInfo.countryCode ?? ipInfo.location?.country_code ?? "unknown";

            return new IpInfoResult(country, ip);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
