namespace ServiceLib.Manager;

/// <summary>
/// V2pn: the panel metadata of subscriptions (<see cref="SubInfoItem"/>), stored in its own table and
/// cached for the UI. Applied after every subscription download; it also names the subscription and
/// sets its update interval from the panel while the user has not chosen them.
/// </summary>
public sealed class SubInfoManager
{
    private static readonly Lazy<SubInfoManager> _instance = new(() => new());
    public static SubInfoManager Instance => _instance.Value;

    private static readonly string _tag = "SubInfoManager";

    // What ConfigHandler.AddSubItem(config, url) calls a subscription imported from a bare link.
    private const string ImportedName = "import_sub";

    private readonly ConcurrentDictionary<string, SubInfoItem> _items = new();
    private readonly object _loadLock = new();
    private Task? _load;

    /// <summary>Raised with the subscription id after its panel data changed. May come from any thread.</summary>
    public event Action<string>? Changed;

    public SubInfoItem? Get(string? subId) =>
        subId.IsNotEmpty() && _items.TryGetValue(subId, out var info) ? info : null;

    /// <summary>Fills the cache once; later calls return the same task.</summary>
    public Task LoadAsync()
    {
        lock (_loadLock)
        {
            return _load ??= LoadCoreAsync();
        }
    }

    private async Task LoadCoreAsync()
    {
        try
        {
            SQLiteHelper.Instance.CreateTable<SubInfoItem>();
            var subIds = (await AppManager.Instance.SubItems() ?? []).Select(t => t.Id).ToHashSet();
            foreach (var info in await SQLiteHelper.Instance.TableAsync<SubInfoItem>().ToListAsync())
            {
                // Rows of deleted subscriptions; v2rayN's delete does not know about this table.
                if (!subIds.Contains(info.Id))
                {
                    await SQLiteHelper.Instance.DeleteAsync(info);
                    continue;
                }
                _items[info.Id] = info;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    /// <summary>
    /// Stores what the panel sent with a download of <paramref name="item"/>. When the servers were
    /// imported, also takes the name and the update interval from the panel where the user has not set
    /// their own, and saves <paramref name="item"/>. No headers (a failed or non-HTTP download): no change.
    /// </summary>
    public async Task ApplyAsync(Config config, SubItem item, IReadOnlyDictionary<string, string>? headers, bool imported)
    {
        if (headers == null || item.Id.IsNullOrEmpty())
        {
            return;
        }
        try
        {
            await LoadAsync();
            var panel = SubPanelHeaders.Parse(headers);
            var info = Get(item.Id) ?? new SubInfoItem { Id = item.Id };

            info.HasUserInfo = panel.HasUserInfo;
            info.Upload = panel.Upload;
            info.Download = panel.Download;
            info.Total = panel.Total;
            info.Expire = panel.Expire;
            info.UpdateIntervalHours = panel.UpdateIntervalHours;
            info.SupportUrl = panel.SupportUrl;
            info.WebPageUrl = panel.WebPageUrl;
            info.Announce = panel.Announce;
            info.ReceivedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (imported && ApplyToSubscription(item, info, panel))
            {
                await ConfigHandler.AddSubItem(config, item);
            }

            await SQLiteHelper.Instance.ReplaceAsync(info);
            _items[item.Id] = info;
            Changed?.Invoke(item.Id);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    /// <summary>Name and interval from the panel; true when <paramref name="item"/> changed.</summary>
    public static bool ApplyToSubscription(SubItem item, SubInfoItem info, SubPanelInfo panel)
    {
        var changed = false;
        var name = panel.Title ?? FallbackName(item.Url);
        if (name.IsNotEmpty())
        {
            if (IsPanelName(item, info) && item.Remarks != name)
            {
                item.Remarks = name;
                changed = true;
            }
            info.PanelName = name;
        }

        var minutes = panel.UpdateIntervalHours * 60;
        if (minutes > 0 && (item.AutoUpdateInterval <= 0 || item.AutoUpdateInterval == info.PanelInterval))
        {
            if (item.AutoUpdateInterval != minutes)
            {
                item.AutoUpdateInterval = minutes;
                changed = true;
            }
            info.PanelInterval = minutes;
        }
        return changed;
    }

    /// <summary>
    /// True while the subscription's name is not the user's own: empty, v2rayN's placeholder, the bare
    /// host of the link, or the name the panel gave last time.
    /// </summary>
    public static bool IsPanelName(SubItem item, SubInfoItem? info)
    {
        var remarks = item.Remarks?.Trim();
        return remarks.IsNullOrEmpty()
               || remarks == ImportedName
               || remarks == FallbackName(item.Url)
               || (info?.PanelName.IsNotEmpty() == true && remarks == info.PanelName);
    }

    /// <summary>The name to show until the panel names a subscription: the host of its link.</summary>
    public static string? FallbackName(string? url)
    {
        var host = Utils.TryUri(url?.Trim() ?? string.Empty)?.Host;
        return host.IsNullOrEmpty() ? null : host;
    }

    /// <summary>
    /// A name for a subscription saved with an empty name field: what the panel said last, else the host.
    /// Either counts as the panel's, so the next update can replace it.
    /// </summary>
    public string? SuggestName(SubItem item) => Get(item.Id)?.PanelName ?? FallbackName(item.Url);
}
