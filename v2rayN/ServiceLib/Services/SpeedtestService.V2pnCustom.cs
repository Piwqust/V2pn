namespace ServiceLib.Services;

// V2pn: tests of whole configs ("Custom"), which v2rayN passed by (V2pnCustomPing).
public partial class SpeedtestService
{
    /// <summary>Whole configs tested at once: each runs a core of its own.</summary>
    private const int CustomConcurrency = 3;

    private async Task RunV2pnCustomAsync(ESpeedActionType actionType, List<ServerTestItem> items,
        ConcurrentDictionary<string, byte> completedIds, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return;
        }
        var options = new ParallelOptions { MaxDegreeOfParallelism = CustomConcurrency, CancellationToken = ct };
        await Parallel.ForEachAsync(items, options, async (it, innerCt) =>
        {
            try
            {
                var core = it.Profile.CoreType ?? ECoreType.Xray;
                var text = await V2pnCustomPing.ReadAsync(it.Profile);
                if (text == null)
                {
                    await UpdateFunc(it.IndexId, ResUI.SpeedtestingSkip);
                    completedIds.TryAdd(it.IndexId, 0);
                    return;
                }
                if (actionType == ESpeedActionType.Tcping)
                {
                    await TcpingV2pnCustom(it, text, core, innerCt);
                }
                else
                {
                    await RealPingV2pnCustom(it, text, core, completedIds, innerCt);
                }
                completedIds.TryAdd(it.IndexId, 0);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
        });
    }

    /// <summary>The fastest TCP connect to any server the config names.</summary>
    private async Task TcpingV2pnCustom(ServerTestItem it, string text, ECoreType core, CancellationToken ct)
    {
        var times = await Task.WhenAll(V2pnCustomPing.Endpoints(text, core).Take(16).Select(async e =>
        {
            try
            {
                return await GetTcpingTime(e.Host, e.Port, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return -1;
            }
        }));
        var best = times.Where(t => t > 0).DefaultIfEmpty(-1).Min();
        ProfileExManager.Instance.SetTestDelay(it.IndexId, best);
        await UpdateFunc(it.IndexId, best.ToString());
    }

    /// <summary>The config on a core of its own, its local ports replaced by one of the test's, and the usual real ping through it.</summary>
    private async Task RealPingV2pnCustom(ServerTestItem it, string text, ECoreType core,
        ConcurrentDictionary<string, byte> completedIds, CancellationToken ct)
    {
        var port = FreeLoopbackPort();
        var config = port > 0 ? V2pnCustomPing.TestConfig(text, core, port) : null;
        var process = config == null ? null : await CoreManager.Instance.LoadCoreConfigSpeedtestCustom(core, config);
        if (process == null)
        {
            ProfileExManager.Instance.SetTestDelay(it.IndexId, -1);
            await UpdateFunc(it.IndexId, "-1");
            return;
        }
        try
        {
            await Task.Delay(1000, ct);
            it.Port = port;
            await DoRealPing(it, completedIds, ct);
        }
        finally
        {
            await process.StopAsync();
            process.Dispose();
        }
    }

    private static int FreeLoopbackPort()
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
        catch
        {
            return 0;
        }
    }
}
