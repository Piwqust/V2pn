namespace ServiceLib;

/// <param name="Worked">A way (or none, <paramref name="Variant"/> null) made the server carry traffic again.</param>
public sealed record V2pnTuneOutcome(bool Worked, V2pnBypassVariant? Variant, int Tried, bool Cancelled);

/// <summary>
/// Tries the bypass ways on one server, one by one: each is put on the server, the connection is restarted and checked
/// with a small and a large download. The first that works is kept for this network. Tries are spaced out, since
/// several handshakes to one address in a moment are what gets that address frozen.
/// </summary>
public static class V2pnBypassTuner
{
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(8);

    /// <param name="reload">Restarts the connection with the current settings (the way on trial included).</param>
    /// <param name="works">Checks the restarted connection.</param>
    /// <param name="progress">The way being tried (null: none), its number, and how many there are.</param>
    public static async Task<V2pnTuneOutcome> TuneAsync(Config config, ProfileItem server, ECoreType core,
        Func<Task> reload, Func<CancellationToken, Task<bool>> works,
        Action<V2pnBypassVariant?, int, int>? progress = null, CancellationToken ct = default,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        var network = V2pnBypass.NetworkKey(config);
        var current = V2pnBypass.Learned(config, server.IndexId, network);
        var order = new List<V2pnBypassVariant?>();
        order.AddRange(V2pnBypass.CandidatesFor(server, core).Where(v => v.Id != current?.Id));
        if (current != null)
        {
            // The kept way stopped working; maybe the server does not need one any more.
            order.Add(null);
        }

        var tried = 0;
        var worked = false;
        try
        {
            foreach (var variant in order)
            {
                ct.ThrowIfCancellationRequested();
                if (tried > 0)
                {
                    await delay(Pause, ct);
                }
                tried++;
                progress?.Invoke(variant, tried, order.Count);
                V2pnBypass.SetTrial(server.IndexId, variant?.Id ?? string.Empty);
                await reload();
                if (!await works(ct))
                {
                    continue;
                }
                if (variant == null)
                {
                    V2pnBypass.Forget(config, server.IndexId, network);
                }
                else
                {
                    V2pnBypass.Remember(config, server.IndexId, network, variant.Id);
                }
                Logging.SaveLog($"V2pn bypass: {variant?.Id ?? "no way"} works for a server on this network");
                worked = true;
                return new(true, variant, tried, false);
            }
            Logging.SaveLog($"V2pn bypass: none of {tried} ways worked");
            return new(false, null, tried, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new(false, null, tried, true);
        }
        finally
        {
            V2pnBypass.SetTrial(null, null);
            if (!worked && tried > 0)
            {
                // Back to what was there before the tries.
                try
                {
                    await reload();
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(nameof(V2pnBypassTuner), ex);
                }
            }
        }
    }
}
