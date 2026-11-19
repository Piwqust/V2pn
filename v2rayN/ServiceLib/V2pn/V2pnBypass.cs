namespace ServiceLib;

/// <summary>
/// One way of getting a connection past the filter that V2pn can try on a server by itself. Only TLS-handshake
/// fragmentation is tried: it changes nothing on the server's side, so a server that does not need it keeps working.
/// Noise and mux are left out on purpose: noise only fits some UDP transports, and mux needs the server's support.
/// </summary>
public sealed record V2pnBypassVariant(string Id, V2pnFragment Fragment)
{
    public V2pnTransport Transport { get; } = new(Fragment, []);
}

/// <summary>
/// "Обход блокировок": the switches, the ways V2pn may try, and the way found for each server on each network.
/// Everything is off until <see cref="V2pnBypassItem.Enabled"/> is on.
/// </summary>
public static class V2pnBypass
{
    /// <summary>A record per server and network; the oldest go first past this many.</summary>
    public const int MaxLearned = 300;

    /// <summary>The key used when ways are not kept per network.</summary>
    public const string AnyNetwork = "*";

    /// <summary>The ClientHello split into TLS records of 100-200 bytes; the most common setting on the forums.</summary>
    public static readonly V2pnBypassVariant Fragment = new("fragment", new("tlshello", "100-200", "10-20"));

    /// <summary>Finer records with shorter pauses, for filters that put larger pieces back together.</summary>
    public static readonly V2pnBypassVariant FragmentFine = new("fragment-fine", new("tlshello", "10-30", "5-15"));

    /// <summary>The first TCP writes split instead of the TLS records; works with REALITY, which tlshello breaks.</summary>
    public static readonly V2pnBypassVariant FragmentTcp = new("fragment-tcp", new("1-3", "50-100", "10-20"));

    public static IReadOnlyList<V2pnBypassVariant> All { get; } = [Fragment, FragmentFine, FragmentTcp];

    /// <summary>The most servers tested at once in calm mode.</summary>
    public const int CalmBatchSize = 12;

    /// <summary>The pause between two calm batches.</summary>
    public static readonly TimeSpan CalmPause = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Splits servers into test batches of at most <see cref="CalmBatchSize"/> (or <paramref name="pageSize"/>, if
    /// smaller), with one entry per server address in each: several handshakes to one address in a moment look like a
    /// VPN to the filter, which then freezes that address for minutes. Order is kept as far as that allows.
    /// </summary>
    public static List<List<T>> CalmBatches<T>(IReadOnlyList<T> items, Func<T, string?> address, int pageSize)
    {
        var size = Math.Clamp(pageSize, 1, CalmBatchSize);
        var batches = new List<List<T>>();
        var hosts = new List<HashSet<string>>();
        foreach (var item in items)
        {
            var host = (address(item) ?? string.Empty).Trim().ToLowerInvariant();
            var placed = false;
            for (var i = 0; i < batches.Count; i++)
            {
                if (batches[i].Count < size && (host.Length == 0 || !hosts[i].Contains(host)))
                {
                    batches[i].Add(item);
                    hosts[i].Add(host);
                    placed = true;
                    break;
                }
            }
            if (!placed)
            {
                batches.Add([item]);
                hosts.Add([host]);
            }
        }
        return batches;
    }

    public static V2pnBypassVariant? Variant(string? id) => All.FirstOrDefault(v => v.Id == id);

    public static V2pnBypassItem Item(Config config) => config.V2pnBypassItem ??= new();

    public static bool On(Config? config) => config?.V2pnBypassItem?.Enabled == true;

    public static bool Calm(Config? config) => On(config) && config!.V2pnBypassItem!.CalmTests;

    public static bool AutoTune(Config? config) => On(config) && config!.V2pnBypassItem!.AutoTune;

    public static bool DirectFragment(Config? config) => On(config) && config!.V2pnBypassItem!.DirectFragment;

    /// <summary>The network the ways are kept for now: the current one, or any when they are not kept per network.</summary>
    public static string NetworkKey(Config config) =>
        Item(config).PerNetwork && V2pnNetworkId.Current.IsNotEmpty() ? V2pnNetworkId.Current : AnyNetwork;

    /// <summary>
    /// The ways V2pn may try on <paramref name="node"/>, most likely first. Only servers that speak TLS or REALITY over
    /// TCP can be helped by fragmentation; sing-box cannot size the pieces, so it gets a single try.
    /// </summary>
    public static IReadOnlyList<V2pnBypassVariant> CandidatesFor(ProfileItem node, ECoreType core)
    {
        if (node.ConfigType.IsGroupType() || node.ConfigType is EConfigType.Custom or EConfigType.Outbound
            || node.ConfigType is EConfigType.Hysteria2 or EConfigType.TUIC or EConfigType.WireGuard or EConfigType.MASQUE)
        {
            return [];
        }
        if (node.StreamSecurity is not (Global.StreamSecurity or Global.StreamSecurityReality))
        {
            return [];
        }
        if (node.GetNetwork() is nameof(ETransport.kcp) or nameof(ETransport.quic))
        {
            return [];
        }
        if (core == ECoreType.sing_box)
        {
            return [Fragment];
        }
        return node.StreamSecurity == Global.StreamSecurityReality
            ? [FragmentTcp, FragmentFine]
            : [Fragment, FragmentFine, FragmentTcp];
    }

    /// <summary>The way kept for a server on a network, if any.</summary>
    public static V2pnBypassVariant? Learned(Config config, string serverId, string network) =>
        Variant(Item(config).Learned?.FirstOrDefault(l => l.ServerId == serverId && l.Network == network)?.Variant);

    public static void Remember(Config config, string serverId, string network, string variant, long? now = null)
    {
        var item = Item(config);
        var list = item.Learned ??= [];
        list.RemoveAll(l => l.ServerId == serverId && l.Network == network);
        list.Add(new() { ServerId = serverId, Network = network, Variant = variant, At = now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        if (list.Count > MaxLearned)
        {
            var keep = list.OrderByDescending(l => l.At).Take(MaxLearned).ToHashSet();
            list.RemoveAll(l => !keep.Contains(l));
        }
    }

    public static bool Forget(Config config, string serverId, string network) =>
        (Item(config).Learned?.RemoveAll(l => l.ServerId == serverId && l.Network == network) ?? 0) > 0;

    public static void ForgetAll(Config config) => Item(config).Learned = null;

    // A way being tried right now by the tuner; it wins over what is kept, so a reload connects with it.
    private static (string ServerId, string Variant)? _trial;
    private static readonly Lock TrialLock = new();

    internal static void SetTrial(string? serverId, string? variant)
    {
        lock (TrialLock)
        {
            _trial = serverId.IsNotEmpty() && variant != null ? (serverId!, variant) : null;
        }
    }

    internal static (string ServerId, string Variant)? Trial
    {
        get
        {
            lock (TrialLock)
            {
                return _trial;
            }
        }
    }
}

/// <summary>
/// What the bypass adds to one generated core config. Taken once per config, so every outbound of it sees the same
/// network and the same ways.
/// </summary>
public sealed record V2pnBypassPlan(bool Calm, bool DirectFragment, IReadOnlyDictionary<string, V2pnBypassVariant> Variants)
{
    public static V2pnBypassPlan Off { get; } = new(false, false, new Dictionary<string, V2pnBypassVariant>());

    public bool IsOff => !Calm && !DirectFragment && Variants.Count == 0;

    public V2pnBypassVariant? For(ProfileItem? node) =>
        node?.IndexId is { Length: > 0 } id && Variants.TryGetValue(id, out var variant) ? variant : null;

    public static V2pnBypassPlan From(Config config)
    {
        if (!V2pnBypass.On(config))
        {
            return Off;
        }
        var item = V2pnBypass.Item(config);
        var network = V2pnBypass.NetworkKey(config);
        var variants = new Dictionary<string, V2pnBypassVariant>();
        foreach (var learned in item.Learned ?? [])
        {
            if (learned.Network == network && V2pnBypass.Variant(learned.Variant) is { } variant)
            {
                variants[learned.ServerId] = variant;
            }
        }
        if (V2pnBypass.Trial is { } trial)
        {
            if (V2pnBypass.Variant(trial.Variant) is { } variant)
            {
                variants[trial.ServerId] = variant;
            }
            else
            {
                // An empty trial is the server tried without any way, over what is kept for it.
                variants.Remove(trial.ServerId);
            }
        }
        return new(item.CalmTests, item.DirectFragment, variants);
    }
}
