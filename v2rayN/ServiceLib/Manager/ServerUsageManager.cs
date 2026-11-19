namespace ServiceLib.Manager;

/// <summary>
/// V2pn: the counting behind server statistics, without storage or settings, so it can be tested.
/// Not thread-safe; <see cref="ServerUsageManager"/> locks around it.
/// </summary>
public sealed class ServerUsageBook
{
    /// <summary>
    /// The longest gap between two ticks that still counts as one connection. A longer one is a sleep or
    /// a hang: the time is not counted and the next tick starts a new session.
    /// </summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(2);

    private readonly Dictionary<string, ServerUsageItem> _items = [];
    private readonly Dictionary<string, UsageDayItem> _days = [];
    private string? _upId;
    private DateTime _lastTick;

    public HashSet<string> DirtyItems { get; } = [];
    public HashSet<string> DirtyDays { get; } = [];

    public IReadOnlyCollection<ServerUsageItem> Items => _items.Values;

    public ServerUsageItem? Get(string? indexId) => indexId != null && _items.TryGetValue(indexId, out var item) ? item : null;

    public void Load(IEnumerable<ServerUsageItem> items, IEnumerable<UsageDayItem> days)
    {
        foreach (var item in items)
        {
            _items[item.IndexId] = item;
        }
        foreach (var day in days)
        {
            _days[day.Day] = day;
        }
    }

    public void Clear()
    {
        _items.Clear();
        _days.Clear();
        DirtyItems.Clear();
        DirtyDays.Clear();
        _upId = null;
    }

    public void Remove(string indexId)
    {
        _items.Remove(indexId);
        DirtyItems.Remove(indexId);
    }

    /// <summary>A ping test or connection check: a positive delay answered, a negative one did not, 0 is no result.</summary>
    public void RecordTest(string indexId, int delay)
    {
        if (indexId.IsNullOrEmpty() || delay == 0)
        {
            return;
        }
        var item = GetOrAdd(indexId);
        item.Tests++;
        if (delay > 0)
        {
            item.Answers++;
            item.DelaySum += delay;
            item.BestDelay = item.BestDelay > 0 ? Math.Min(item.BestDelay, delay) : delay;
        }
        DirtyItems.Add(indexId);
    }

    /// <summary>
    /// Called every few seconds with the server the connection is up through, or null when it is not.
    /// The time since the previous tick goes to that server when the previous tick saw it too.
    /// </summary>
    /// <returns>True when something was counted.</returns>
    public bool Tick(string? connectedId, DateTime now)
    {
        if (connectedId.IsNullOrEmpty())
        {
            _upId = null;
            return false;
        }
        var item = GetOrAdd(connectedId);
        var gap = now - _lastTick;
        if (_upId == connectedId && gap >= TimeSpan.Zero && gap <= MaxGap)
        {
            var seconds = (long)gap.TotalSeconds;
            if (seconds <= 0)
            {
                return false;
            }
            item.ConnectedSeconds += seconds;
            var key = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (!_days.TryGetValue(key, out var day))
            {
                _days[key] = day = new UsageDayItem { Day = key };
            }
            day.Seconds += seconds;
            DirtyDays.Add(key);
            // Whole seconds only; the fraction carries over to the next tick.
            _lastTick += TimeSpan.FromSeconds(seconds);
        }
        else
        {
            item.Sessions++;
            _lastTick = now;
        }
        item.LastConnected = new DateTimeOffset(now).ToUnixTimeSeconds();
        _upId = connectedId;
        DirtyItems.Add(connectedId);
        return true;
    }

    /// <summary>The last <paramref name="count"/> days up to <paramref name="today"/>, oldest first, days without use as 0.</summary>
    public List<(DateTime Day, long Seconds)> LastDays(int count, DateTime today)
    {
        var list = new List<(DateTime, long)>(count);
        for (var i = count - 1; i >= 0; i--)
        {
            var date = today.Date.AddDays(-i);
            var key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            list.Add((date, _days.TryGetValue(key, out var day) ? day.Seconds : 0));
        }
        return list;
    }

    public UsageDayItem? Day(string key) => _days.TryGetValue(key, out var day) ? day : null;

    /// <summary>Drops days older than <paramref name="keepDays"/>; returns their keys.</summary>
    public List<string> PruneDays(DateTime today, int keepDays)
    {
        var oldest = today.Date.AddDays(-keepDays).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var old = _days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList();
        foreach (var key in old)
        {
            _days.Remove(key);
            DirtyDays.Remove(key);
        }
        return old;
    }

    private ServerUsageItem GetOrAdd(string indexId)
    {
        if (!_items.TryGetValue(indexId, out var item))
        {
            _items[indexId] = item = new ServerUsageItem { IndexId = indexId };
        }
        return item;
    }

    /// <summary>Servers by time connected, most first; only those used for a minute or more.</summary>
    public static List<ServerUsageItem> MostUsed(IEnumerable<ServerUsageItem> items, int count) =>
        items.Where(t => t.ConnectedSeconds >= 60)
            .OrderByDescending(t => t.ConnectedSeconds)
            .ThenByDescending(t => t.Sessions)
            .Take(count)
            .ToList();

    /// <summary>
    /// Servers that answer most often, then fastest on average. A server needs a few tests first: one
    /// lucky answer out of one is not reliability.
    /// </summary>
    public static List<ServerUsageItem> MostReliable(IEnumerable<ServerUsageItem> items, int count, int minTests = 3) =>
        items.Where(t => t.Tests >= minTests && t.Answers > 0)
            .OrderByDescending(t => t.AnswerRate)
            .ThenBy(t => t.AverageDelay)
            .Take(count)
            .ToList();
}

/// <summary>
/// V2pn: server statistics (time connected, sessions, answers to tests), kept in the app's database only
/// while the user has turned them on in the sidebar. Nothing is collected while off.
/// </summary>
public sealed class ServerUsageManager
{
    private static readonly Lazy<ServerUsageManager> _instance = new(() => new());
    public static ServerUsageManager Instance => _instance.Value;

    private static readonly string _tag = "ServerUsageManager";

    // Days of the chart and a little more; older days are dropped.
    private const int KeepDays = 90;

    private readonly object _lock = new();
    private readonly ServerUsageBook _book = new();
    private Task? _load;

    /// <summary>Raised after new numbers were recorded or the statistics were cleared. May come from any thread.</summary>
    public event Action? Changed;

    public static bool Enabled => ConfigTextHandler.Config?.UiItem?.V2pnStats == true;

    public Task LoadAsync()
    {
        lock (_lock)
        {
            return _load ??= LoadCoreAsync();
        }
    }

    private async Task LoadCoreAsync()
    {
        try
        {
            SQLiteHelper.Instance.CreateTable<ServerUsageItem>();
            SQLiteHelper.Instance.CreateTable<UsageDayItem>();
            // Rows of servers that were deleted since; deleting a server does not know about this table.
            await SQLiteHelper.Instance.ExecuteAsync("delete from ServerUsageItem where IndexId not in (select IndexId from ProfileItem)");
            var items = await SQLiteHelper.Instance.TableAsync<ServerUsageItem>().ToListAsync();
            var days = await SQLiteHelper.Instance.TableAsync<UsageDayItem>().ToListAsync();
            List<string> old;
            lock (_lock)
            {
                _book.Load(items, days);
                old = _book.PruneDays(DateTime.Now, KeepDays);
            }
            foreach (var key in old)
            {
                await SQLiteHelper.Instance.ExecuteAsync($"delete from UsageDayItem where Day = '{key}'");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        Changed?.Invoke();
    }

    public ServerUsageItem? Get(string? indexId)
    {
        lock (_lock)
        {
            return _book.Get(indexId);
        }
    }

    public List<ServerUsageItem> All()
    {
        lock (_lock)
        {
            return [.. _book.Items];
        }
    }

    public List<(DateTime Day, long Seconds)> LastDays(int count)
    {
        lock (_lock)
        {
            return _book.LastDays(count, DateTime.Now);
        }
    }

    public void RecordTest(string indexId, int delay)
    {
        if (!Enabled || delay == 0)
        {
            return;
        }
        lock (_lock)
        {
            _book.RecordTest(indexId, delay);
        }
    }

    /// <summary>See <see cref="ServerUsageBook.Tick"/>. Called by the main window every few seconds.</summary>
    public void Tick(string? connectedId)
    {
        bool counted;
        lock (_lock)
        {
            counted = _book.Tick(Enabled ? connectedId : null, DateTime.Now);
        }
        if (counted)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Lets the sidebar refresh after a ping run recorded its answers.</summary>
    public void NotifyChanged() => Changed?.Invoke();

    public async Task SaveAsync()
    {
        List<ServerUsageItem> items;
        List<UsageDayItem> days;
        lock (_lock)
        {
            if (_book.DirtyItems.Count == 0 && _book.DirtyDays.Count == 0)
            {
                return;
            }
            // Copies: the rows keep changing on other threads while they are written.
            items = _book.DirtyItems.Select(_book.Get).OfType<ServerUsageItem>().Select(t => t.Clone()).ToList();
            days = _book.DirtyDays.Select(_book.Day).OfType<UsageDayItem>().Select(d => new UsageDayItem { Day = d.Day, Seconds = d.Seconds }).ToList();
            _book.DirtyItems.Clear();
            _book.DirtyDays.Clear();
        }
        try
        {
            await SQLiteHelper.Instance.ReplaceAllAsync(items);
            await SQLiteHelper.Instance.ReplaceAllAsync(days);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    public async Task ClearAsync()
    {
        lock (_lock)
        {
            _book.Clear();
        }
        try
        {
            await SQLiteHelper.Instance.DeleteAllAsync<ServerUsageItem>();
            await SQLiteHelper.Instance.DeleteAllAsync<UsageDayItem>();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        Changed?.Invoke();
    }
}
