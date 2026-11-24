namespace ServiceLib;

/// <summary>
/// A server of the partner's subscription as an action sees it: its last delay (0 untested, below 0 no answer), the
/// partner's note about it (pack and API merged) and V2pn's last delay results for it, oldest first.
/// </summary>
public sealed record V2pnCandidate(string Id, string Name, string? Host, int Delay, V2pnServerNote? Note = null, IReadOnlyList<int>? History = null)
{
    public bool Hidden => Note?.IsHidden == true;

    public int Priority => Note?.Priority ?? 0;

    public bool Is(string nameOrHost) =>
        Name == nameOrHost || (Host != null && string.Equals(Host.Trim().TrimEnd('.'), nameOrHost.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Why an action connected where it did; the window says it in words after the server's name.</summary>
public enum V2pnChoiceReason
{
    /// <summary>The lowest delay V2pn measured.</summary>
    Fastest,

    /// <summary>The steadiest over V2pn's last checks.</summary>
    Steadiest,

    /// <summary>The lowest load the partner reported.</summary>
    LeastLoaded,

    /// <summary>The partner's API recommended it.</summary>
    ByPartner,

    /// <summary>The partner's button names this server.</summary>
    Named,
}

public sealed record V2pnChoice(V2pnCandidate Server, V2pnChoiceReason Reason);

/// <summary>
/// How the partner's buttons pick a server. Pure: the window measures, asks the API and connects; this only decides.
/// A hidden server is never offered, except when the partner's API recommends it by name.
/// </summary>
public static class V2pnPartnerChoice
{
    /// <summary>The partner's word about a server: its API's note over its pack's, field by field; null when neither names it.</summary>
    public static V2pnServerNote? Describe(IReadOnlyList<V2pnServerNote> pack, IReadOnlyList<V2pnServerNote>? live, string? remarks, string? address)
    {
        var fromPack = pack.FirstOrDefault(n => n.Matches(remarks, address));
        var fromApi = live?.FirstOrDefault(n => n.Matches(remarks, address));
        return fromApi?.Over(fromPack) ?? fromPack;
    }

    /// <summary>The servers an action may choose from: not hidden, and inside its scope (group, purpose, named servers).</summary>
    public static IReadOnlyList<V2pnCandidate> InScope(IEnumerable<V2pnCandidate> servers, V2pnActionScope? scope)
    {
        var list = servers.Where(s => !s.Hidden);
        if (scope?.Group is { } group)
        {
            list = list.Where(s => string.Equals(s.Note?.Group, group, StringComparison.CurrentCultureIgnoreCase));
        }
        if (scope?.Purpose is { } purpose)
        {
            list = list.Where(s => s.Note?.HasPurpose(purpose) == true);
        }
        if (scope is { Servers.Count: > 0 } named)
        {
            list = list.Where(s => named.Servers.Any(s.Is));
        }
        return list.ToList();
    }

    /// <summary>The server <paramref name="strategy"/> picks, with the reason; null when none answered.</summary>
    public static V2pnChoice? Best(IReadOnlyList<V2pnCandidate> servers, string strategy)
    {
        switch (strategy)
        {
            case V2pnStrategies.Stable:
                var steady = servers
                    .Select(s => (Server: s, Score: Stability(s.History)))
                    .Where(t => t.Score.Answers > 0)
                    .OrderByDescending(t => t.Score.Answers)
                    .ThenBy(t => t.Score.Median)
                    .ThenBy(t => t.Score.Spread)
                    .ThenByDescending(t => t.Server.Priority)
                    .Select(t => t.Server)
                    .FirstOrDefault();
                return steady != null ? new V2pnChoice(steady, V2pnChoiceReason.Steadiest) : Fastest(servers);

            case V2pnStrategies.Load:
                // A server the partner says is down is not "least loaded"; without any load numbers, by ping.
                var light = servers
                    .Where(s => s.Note?.Load != null && s.Note.Healthy != false && s.Delay >= 0)
                    .OrderBy(s => s.Note!.Load)
                    .ThenBy(s => s.Delay > 0 ? s.Delay : int.MaxValue)
                    .ThenByDescending(s => s.Priority)
                    .FirstOrDefault();
                return light != null ? new V2pnChoice(light, V2pnChoiceReason.LeastLoaded) : Fastest(servers);

            default:
                return Fastest(servers);
        }
    }

    /// <summary>The lowest delay that answered; the partner's priority (higher first) breaks a tie.</summary>
    public static V2pnChoice? Fastest(IReadOnlyList<V2pnCandidate> servers)
    {
        var best = servers.Where(s => s.Delay > 0).OrderBy(s => s.Delay).ThenByDescending(s => s.Priority).FirstOrDefault();
        return best == null ? null : new V2pnChoice(best, V2pnChoiceReason.Fastest);
    }

    /// <summary>
    /// The first server the partner recommends that belongs to the subscription and answered V2pn's check (a hidden
    /// one too: the partner named it). Null when none of them did; the caller falls back to the fastest in the purpose.
    /// </summary>
    public static V2pnChoice? Recommended(IReadOnlyList<string> recommended, IReadOnlyList<V2pnCandidate> servers)
    {
        foreach (var name in recommended)
        {
            var server = servers.FirstOrDefault(s => s.Is(name));
            if (server is { Delay: > 0 })
            {
                return new V2pnChoice(server, V2pnChoiceReason.ByPartner);
            }
        }
        return null;
    }

    /// <summary>The servers the partner recommends, in its order, that are in the subscription: what V2pn checks first.</summary>
    public static IReadOnlyList<V2pnCandidate> RecommendedIn(IReadOnlyList<string> recommended, IReadOnlyList<V2pnCandidate> servers) =>
        recommended.Select(name => servers.FirstOrDefault(s => s.Is(name))).Where(s => s != null).Select(s => s!).Distinct().ToList();

    /// <summary>The server a connect.server button names; a hidden one too, since the partner named it.</summary>
    public static V2pnCandidate? Named(string nameOrHost, IReadOnlyList<V2pnCandidate> servers) => servers.FirstOrDefault(s => s.Is(nameOrHost));

    /// <summary>How steady a server was: answers out of the last checks, the median of those answers and their spread (max − min).</summary>
    public static (int Answers, int Median, int Spread) Stability(IReadOnlyList<int>? history)
    {
        var answers = (history ?? []).Where(d => d > 0).Order().ToList();
        if (answers.Count == 0)
        {
            return (0, int.MaxValue, int.MaxValue);
        }
        var mid = answers.Count / 2;
        var median = answers.Count % 2 == 1 ? answers[mid] : (answers[mid - 1] + answers[mid]) / 2;
        return (answers.Count, median, answers[^1] - answers[0]);
    }
}

/// <summary>
/// The last delay results of each server (V2pn's own checks, 0 or less for no answer) and when the last one came, kept
/// between starts in guiConfigs/v2pn-delays.json for the "stable" strategy and for "Fastest", which takes only fresh
/// results. Only numbers by server id; nothing leaves the computer.
/// </summary>
public sealed class V2pnDelayHistory
{
    public const int Keep = 10;

    /// <summary>The file's key for the times of the last results; an array per server id would be read as a history.</summary>
    private const string TimesKey = "@at";
    private static readonly Lazy<V2pnDelayHistory> _instance = new(() => new V2pnDelayHistory(() => Utils.GetConfigPath("v2pn-delays.json")));
    public static V2pnDelayHistory Instance => _instance.Value;

    private readonly Func<string> _file;
    private readonly Dictionary<string, List<int>> _history = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _at = new(StringComparer.Ordinal);
    private bool _loaded;
    private bool _saveQueued;

    public V2pnDelayHistory(Func<string> file)
    {
        _file = file;
    }

    /// <summary>Adds a result; the oldest beyond <see cref="Keep"/> goes. Saved a few seconds later, once for a whole run.</summary>
    public void Record(string? indexId, int delay, DateTime? now = null)
    {
        if (indexId.IsNullOrEmpty())
        {
            return;
        }
        lock (_history)
        {
            Load();
            if (!_history.TryGetValue(indexId!, out var list))
            {
                _history[indexId!] = list = [];
            }
            list.Add(delay > 0 ? delay : -1);
            _at[indexId!] = new DateTimeOffset(now ?? DateTime.UtcNow).ToUnixTimeSeconds();
            if (list.Count > Keep)
            {
                list.RemoveRange(0, list.Count - Keep);
            }
            if (_saveQueued)
            {
                return;
            }
            _saveQueued = true;
        }
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => Save(), TaskScheduler.Default);
    }

    public IReadOnlyList<int> Get(string? indexId)
    {
        lock (_history)
        {
            Load();
            return indexId != null && _history.TryGetValue(indexId, out var list) ? list.ToList() : [];
        }
    }

    /// <summary>When the server's last result came (UTC); null before its first one, or for results kept by V2pn 1.4 and older.</summary>
    public DateTime? MeasuredAt(string? indexId)
    {
        lock (_history)
        {
            Load();
            return indexId != null && _at.TryGetValue(indexId, out var at) ? DateTimeOffset.FromUnixTimeSeconds(at).UtcDateTime : null;
        }
    }

    /// <summary>
    /// A server that answered fewer than half of its last checks, at least four of them: likely to drop again even when
    /// its last answer was quick.
    /// </summary>
    public static bool Flaky(IReadOnlyList<int> history) => history.Count >= 4 && history.Count(d => d > 0) * 2 < history.Count;

    /// <summary>Drops the servers that are gone (by the ids still there), so the file does not grow forever.</summary>
    public void Retain(IEnumerable<string> indexIds)
    {
        var keep = indexIds.ToHashSet(StringComparer.Ordinal);
        lock (_history)
        {
            Load();
            foreach (var id in _history.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _history.Remove(id);
            }
            foreach (var id in _at.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _at.Remove(id);
            }
        }
        Save();
    }

    public void Save()
    {
        string text;
        lock (_history)
        {
            _saveQueued = false;
            var root = new JsonObject();
            foreach (var (id, list) in _history)
            {
                root[id] = new JsonArray(list.Select(d => (JsonNode)JsonValue.Create(d)).ToArray());
            }
            var times = new JsonObject();
            foreach (var (id, at) in _at)
            {
                times[id] = at;
            }
            root[TimesKey] = times;
            text = root.ToJsonString();
        }
        try
        {
            File.WriteAllText(_file(), text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logging.SaveLog(nameof(V2pnDelayHistory), ex);
        }
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        try
        {
            var path = _file();
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024 || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
            {
                return;
            }
            foreach (var (id, node) in root)
            {
                if (node is JsonArray array)
                {
                    _history[id] = array.Select(n => n is JsonValue v && v.TryGetValue<int>(out var d) ? d : -1).TakeLast(Keep).ToList();
                }
                else if (id == TimesKey && node is JsonObject times)
                {
                    foreach (var (server, at) in times)
                    {
                        if (at is JsonValue v && v.TryGetValue<long>(out var seconds))
                        {
                            _at[server] = seconds;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Logging.SaveLog(nameof(V2pnDelayHistory), ex);
        }
    }
}

/// <summary>A pause between presses of the same button, so a partner's API is never asked in a loop.</summary>
public sealed class V2pnActionCooldown(TimeSpan pause)
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(3);
    private readonly Dictionary<string, DateTime> _last = new(StringComparer.Ordinal);

    /// <summary>True (and the pause starts) when <paramref name="key"/> was not pressed within the pause.</summary>
    public bool TryStart(string key, DateTime now)
    {
        lock (_last)
        {
            if (_last.TryGetValue(key, out var last) && now - last < pause)
            {
                return false;
            }
            _last[key] = now;
            return true;
        }
    }
}
