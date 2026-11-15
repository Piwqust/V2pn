namespace ServiceLib;

/// <summary>
/// What a partner with the lock grant closed on one of its subscriptions.
/// <see cref="Settings"/> (hide-settings, or "lock" in the pack): the servers' addresses, ports and keys are not shown,
/// shared, copied, exported, backed up or edited, the link cannot be changed, and the provider's defaults for the
/// subscription (connecting at start, the order) cannot be overridden. <see cref="HideUrl"/> (hide-url): the link
/// itself is not shown or changed. What stays open whatever the partner sends: connecting and disconnecting, any
/// server of the subscription, deleting the subscription, plain V2pn, the person's own servers and routing, the kill
/// switch and V2pn's updates.
/// </summary>
public sealed record V2pnLockState(bool Settings, bool HideUrl, string? PartnerName)
{
    public static readonly V2pnLockState None = new(false, false, null);

    /// <summary>Anything closed at all.</summary>
    public bool Any => Settings || HideUrl;

    /// <summary>The subscription's link cannot be changed.</summary>
    public bool UrlLocked => Settings || HideUrl;

    /// <summary>
    /// The flags count only for a verified partner whose certificate has the lock grant; from anyone else they do
    /// nothing, or any subscription could hide where the person's traffic goes.
    /// </summary>
    public static V2pnLockState For(V2pnPartner? partner, SubInfoItem? info) =>
        For(partner?.Cert, partner?.Pack, info, partner?.Name);

    public static V2pnLockState For(V2pnPartnerCert? cert, V2pnPartnerPack? pack, SubInfoItem? info, string? name)
    {
        if (cert?.Allows(V2pnPartnerGrants.Lock) != true)
        {
            return None;
        }
        var settings = info?.HideSettings == true || pack?.Lock == true;
        var url = info?.HideUrl == true;
        return settings || url ? new V2pnLockState(settings, url, name ?? cert.Name) : None;
    }
}

/// <summary>
/// The verified partner and the lock of every subscription, kept up to date as partners and the panels' answers
/// change, for the places that list, show or export a server (sync reads) and for the commands that would export one
/// (<see cref="ForSubAsync"/>, read fresh).
/// </summary>
public sealed class V2pnLocks
{
    private static readonly Lazy<V2pnLocks> _instance = new(() => new V2pnLocks());
    public static V2pnLocks Instance => _instance.Value;

    private volatile IReadOnlyDictionary<string, V2pnLockState> _bySub = new Dictionary<string, V2pnLockState>();
    private volatile IReadOnlyDictionary<string, V2pnPartner> _partnerBySub = new Dictionary<string, V2pnPartner>();
    private int _refreshing;
    private bool _again;

    /// <summary>Raised after the partners of the subscriptions or what they closed changed. May come from any thread.</summary>
    public event Action? Changed;

    private V2pnLocks()
    {
        V2pnPartners.Instance.Changed += () => _ = RefreshAsync();
        SubInfoManager.Instance.Changed += id => _ = RefreshAsync();
        _ = RefreshAsync();
    }

    /// <summary>The verified partner of a subscription as last computed; null for none.</summary>
    public V2pnPartner? PartnerOf(string? subId) =>
        subId.IsNotEmpty() && _partnerBySub.TryGetValue(subId!, out var partner) ? partner : null;

    /// <summary>The subscriptions of verified partners, by id.</summary>
    public IReadOnlyDictionary<string, V2pnPartner> Partners => _partnerBySub;

    /// <summary>The lock of a subscription as last computed; <see cref="V2pnLockState.None"/> for none or unknown.</summary>
    public V2pnLockState ForSub(string? subId) =>
        subId.IsNotEmpty() && _bySub.TryGetValue(subId!, out var state) ? state : V2pnLockState.None;

    /// <summary>Whether a server's address and settings are closed (it comes from a subscription with closed settings).</summary>
    public bool Hides(ProfileItem? item) => ForSub(item?.Subid).Settings;

    public bool Hides(string? subId) => ForSub(subId).Settings;

    /// <summary>The lock read now from the subscription and its partner; for a command about to show or export something.</summary>
    public async Task<V2pnLockState> ForSubAsync(string? subId)
    {
        if (subId.IsNullOrEmpty())
        {
            return V2pnLockState.None;
        }
        try
        {
            await SubInfoManager.Instance.LoadAsync();
            await V2pnPartners.Instance.LoadAsync();
            var sub = await AppManager.Instance.GetSubItem(subId);
            return sub == null ? V2pnLockState.None : V2pnLockState.For(V2pnPartners.Instance.ForSubscription(sub), SubInfoManager.Instance.Get(sub.Id));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnLocks), ex);
            // When in doubt about a partner's subscription, the cached answer; never "open" by an error alone.
            return ForSub(subId);
        }
    }

    /// <summary>True when any of <paramref name="items"/> (or a server inside a group among them) is closed.</summary>
    public async Task<bool> AnyClosedAsync(IEnumerable<ProfileItem> items)
    {
        // Each subscription is looked up once, however many of its servers are selected.
        var known = new Dictionary<string, bool>(StringComparer.Ordinal);
        async Task<bool> Closed(string? subId)
        {
            if (subId.IsNullOrEmpty())
            {
                return false;
            }
            if (!known.TryGetValue(subId!, out var closed))
            {
                known[subId!] = closed = (await ForSubAsync(subId)).Settings;
            }
            return closed;
        }
        foreach (var item in items)
        {
            if (await Closed(item.Subid))
            {
                return true;
            }
            if (item.ConfigType.IsGroupType())
            {
                foreach (var child in await GroupMembersAsync(item))
                {
                    if (await Closed(child.Subid))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static async Task<List<ProfileItem>> GroupMembersAsync(ProfileItem group)
    {
        try
        {
            var (members, _) = await GroupProfileManager.GetChildProfileItems(group);
            return members;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnLocks), ex);
            return [];
        }
    }

    /// <summary>
    /// Takes what a partner closed out of a copy of the database on its way into a backup: the servers of a subscription
    /// with closed settings (an update brings them back after a restore), and with hide-url the subscription itself.
    /// </summary>
    public async Task StripFromBackupAsync(string dbFile)
    {
        await RefreshAsync();
        var closed = _bySub.Where(kv => kv.Value.Any).ToList();
        if (closed.Count == 0 || !File.Exists(dbFile))
        {
            return;
        }
        using var db = new SQLiteConnection(dbFile, false);
        db.RunInTransaction(() =>
        {
            foreach (var (subId, state) in closed)
            {
                db.Execute("DELETE FROM ProfileItem WHERE Subid = ?", subId);
                if (state.HideUrl)
                {
                    db.Execute("DELETE FROM SubItem WHERE Id = ?", subId);
                }
            }
        });
    }

    public async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            _again = true;
            return;
        }
        try
        {
            do
            {
                _again = false;
                await SubInfoManager.Instance.LoadAsync();
                await V2pnPartners.Instance.LoadAsync();
                var subs = await AppManager.Instance.SubItems() ?? [];
                var map = new Dictionary<string, V2pnLockState>(StringComparer.Ordinal);
                var partners = new Dictionary<string, V2pnPartner>(StringComparer.Ordinal);
                foreach (var sub in subs.Where(s => s.Id.IsNotEmpty()))
                {
                    var partner = V2pnPartners.Instance.ForSubscription(sub);
                    if (partner != null)
                    {
                        partners[sub.Id] = partner;
                    }
                    var state = V2pnLockState.For(partner, SubInfoManager.Instance.Get(sub.Id));
                    if (state.Any)
                    {
                        map[sub.Id] = state;
                    }
                }
                var changed = map.Count != _bySub.Count || map.Any(kv => !_bySub.TryGetValue(kv.Key, out var old) || old != kv.Value)
                    || partners.Count != _partnerBySub.Count || partners.Any(kv => !_partnerBySub.TryGetValue(kv.Key, out var old) || !ReferenceEquals(old, kv.Value));
                _bySub = map;
                _partnerBySub = partners;
                if (changed)
                {
                    Changed?.Invoke();
                }
            }
            while (_again);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnLocks), ex);
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }
}
