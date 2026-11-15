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
    /// The "#key: value" lines at the top of <paramref name="body"/> fill in what the headers did not say.
    /// </summary>
    public async Task ApplyAsync(Config config, SubItem item, IReadOnlyDictionary<string, string>? headers, bool imported, string? body = null)
    {
        if (headers == null || item.Id.IsNullOrEmpty())
        {
            return;
        }
        try
        {
            await LoadAsync();
            var panel = SubPanelHeaders.Parse(SubPanelHeaders.WithBody(headers, body));
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
            info.PartnerUrl = panel.PartnerUrl;
            info.AccountToken = panel.AccountToken;
            CopyCompat(panel, info);
            info.ReceivedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (imported && ApplyToSubscription(item, info, panel))
            {
                await ConfigHandler.AddSubItem(config, item);
            }

            await SQLiteHelper.Instance.ReplaceAsync(info);
            _items[item.Id] = info;
            Changed?.Invoke(item.Id);

            if (info.PartnerUrl.IsNotEmpty() && item.Url.IsNotEmpty())
            {
                // The pack is checked in the background; a slow or broken partner site never holds up the update.
                var partnerUrl = info.PartnerUrl;
                var subscriptionUrl = item.Url;
                _ = Task.Run(() => V2pnPartners.Instance.RefreshAsync(subscriptionUrl, partnerUrl));
            }
            else
            {
                // A panel that answered without v2pn-partner no longer offers its look on this subscription.
                _ = Task.Run(V2pnPartners.Instance.PruneAsync);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    /// <summary>
    /// The person's own choices for a subscription whose provider set defaults: deciding about connecting at start
    /// themselves, and the order of its servers (null follows the provider). Updates never overwrite them.
    /// </summary>
    public async Task SetOwnChoicesAsync(string subId, bool ownAutoconnect, string? ownSort)
    {
        try
        {
            await LoadAsync();
            if (Get(subId) is not { } info || (info.OwnAutoconnect == ownAutoconnect && info.OwnSort == ownSort))
            {
                return;
            }
            info.OwnAutoconnect = ownAutoconnect;
            info.OwnSort = SubPanelHeaders.ParseSortType(ownSort);
            await SQLiteHelper.Instance.ReplaceAsync(info);
            Changed?.Invoke(subId);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    /// <summary>The person's switch for the partner's connection settings of a subscription; true when it changed.</summary>
    public async Task<bool> SetPartnerConnectionOffAsync(string subId, bool off)
    {
        try
        {
            await LoadAsync();
            if (Get(subId) is not { } info || info.PartnerConnectionOff == off)
            {
                return false;
            }
            info.PartnerConnectionOff = off;
            await SQLiteHelper.Instance.ReplaceAsync(info);
            Changed?.Invoke(subId);
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return false;
        }
    }

    /// <summary>The Happ, INCY and v2RayTun values of a response; the person's own choices on <paramref name="info"/> stay.</summary>
    public static void CopyCompat(SubPanelInfo panel, SubInfoItem info)
    {
        info.AnnounceUrl = panel.AnnounceUrl;
        info.InfoText = panel.InfoText;
        info.InfoTone = panel.InfoTone;
        info.InfoButtonText = panel.InfoButtonText;
        info.InfoButtonUrl = panel.InfoButtonUrl;
        info.ExpiryNotice = panel.ExpiryNotice switch { true => 1, false => -1, null => 0 };
        info.ExpiryText = panel.ExpiryText;
        info.ExpiryButtonText = panel.ExpiryButtonText;
        info.ExpiryButtonUrl = panel.ExpiryButtonUrl;
        info.Autoconnect = panel.Autoconnect;
        info.AutoconnectType = panel.AutoconnectType;
        info.SortType = panel.SortType;
        info.PingOnOpen = panel.PingOnOpen;
        info.PingType = panel.PingType;
        info.CheckUrl = panel.CheckUrl;
        info.UpdateOnOpen = panel.UpdateOnOpen;
        info.HideSettings = panel.HideSettings;
        info.HideUrl = panel.HideUrl;
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
        // The panel's interval fills an empty field once, and is followed while the field still holds it.
        // A 0 the user set after that means "never": scheduled updates are what drops connections, so it must stick.
        var untouched = item.AutoUpdateInterval <= 0 && info.PanelInterval <= 0;
        if (minutes > 0 && (untouched || item.AutoUpdateInterval == info.PanelInterval))
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
    /// The name to show: the subscription's own, or the host of its link while the name is empty or v2rayN's
    /// placeholder. Null when there is neither.
    /// </summary>
    public static string? ReadableName(SubItem item)
    {
        var remarks = item.Remarks?.Trim();
        return remarks.IsNullOrEmpty() || remarks == ImportedName ? FallbackName(item.Url) : remarks;
    }

    /// <summary>
    /// A name for a subscription saved with an empty name field: what the panel said last, else the host.
    /// Either counts as the panel's, so the next update can replace it.
    /// </summary>
    public string? SuggestName(SubItem item) => Get(item.Id)?.PanelName ?? FallbackName(item.Url);
}
