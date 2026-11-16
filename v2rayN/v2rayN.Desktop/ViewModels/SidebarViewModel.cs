using v2rayN.Desktop.Common;
using v2rayN.Desktop.Converters;

namespace v2rayN.Desktop.ViewModels;

public enum SidebarTab
{
    Home,
    Countries,
    Subscriptions,
}

/// <summary>List order; stored in UiItem.V2pnSort (countries) and V2pnServerSort (server lists).</summary>
public enum SideSort
{
    Name,
    Ping,
    Usage,
}

/// <summary>What a header's order button sorts: the country list, or a flat list of servers.</summary>
public enum SideSortTarget
{
    Countries,
    Servers,
}

public enum SideActionKind
{
    Fastest,
    Back,
    AddSubscription,
    UpdateAll,
    UpdateSub,
    ClearSearch,
    BrowseCountries,
    ShowAllFavorites,
    ShowFewerFavorites,
    OpenStats,
    EnableStats,
    ClearStats,
    DisableStats,
}

public abstract class SideRow : ReactiveObject;

/// <summary>Progress of the ping run started from the sidebar, shared by every list header.</summary>
public sealed partial class PingState : ReactiveObject
{
    [Reactive]
    public partial bool IsTesting { get; set; }

    [Reactive]
    public partial string Label { get; set; } = L.TestPing;

    /// <summary>"Checking 3 of 40…", for rows that start a ping themselves (the fastest server).</summary>
    [Reactive]
    public partial string Progress { get; set; } = string.Empty;
}

/// <summary>Something running in the background that a row shows a spinner for (a subscription update).</summary>
public sealed partial class BusyState : ReactiveObject
{
    [Reactive]
    public partial bool IsBusy { get; set; }
}

/// <summary>A header's order button: what it sorts and the order now.</summary>
public sealed class SortState(SideSortTarget target, SideSort mode)
{
    public SideSortTarget Target { get; } = target;
    public SideSort Mode { get; } = mode;
    public string Label => Mode switch
    {
        SideSort.Ping => L.SortPing,
        SideSort.Usage => L.SortUsage,
        _ => L.SortName,
    };
    public string Tip => $"{L.SortBy}: {Label}";
}

public sealed class SideHeaderRow(string title, PingState? ping = null, BusyState? subsUpdate = null) : SideRow
{
    public string Title { get; } = title;
    public PingState? Ping { get; } = ping;
    public bool ShowPing => Ping != null;

    /// <summary>The servers this header's ping button tests; null tests what the list shows.</summary>
    public IReadOnlyList<ProfileItemModel>? PingScope { get; init; }

    /// <summary>The order button; null on headers of lists that have no choice.</summary>
    public SortState? Sort { get; init; }
    public bool ShowSort => Sort != null;

    /// <summary>A link on the right ("All statistics"), opening another view.</summary>
    public string? Link { get; init; }
    public SideActionKind LinkKind { get; init; }
    public bool ShowLink => Link != null;

    /// <summary>Set on the subscriptions header: it carries "update all" and "add" instead of rows below the list.</summary>
    public BusyState? SubsUpdate { get; } = subsUpdate;
    public bool ShowSubActions => SubsUpdate != null;
    public override string ToString() => Title;
}

public sealed class SideHintRow(string text) : SideRow
{
    public string Text { get; } = text;
    public override string ToString() => Text;
}

/// <summary>V2pn: a subscription's banner (V2pnSubDefaults.Banner), under its row or at the top of its servers.</summary>
public sealed class SideBannerRow(SubBanner banner, string subId) : SideRow
{
    public SubBanner Banner { get; } = banner;
    public string SubId { get; } = subId;
    public override string ToString() => Banner.Text;
}

/// <summary>V2pn: the provider's announcement above its servers, with "More" when it sent announce-url.</summary>
public sealed class SideAnnounceRow(string text, string? url) : SideRow
{
    public string Text { get; } = text;
    public string? Url { get; } = url;
    public bool HasUrl => Url.IsNotEmpty();
    public override string ToString() => Text;
}

public sealed class SideServerRow : SideRow
{
    /// <param name="memberOf">Set for a row inside an opened pool: it is titled by its number.</param>
    public SideServerRow(ProfileItemModel model, bool showSubscription, bool indent, ServerSeries? memberOf = null)
    {
        Model = model;
        // V2pn: the partner's word about its server (pack and API): its country wins over a guess from the name.
        var partnerNote = V2pnPartnerLive.Instance.Describe(model.Subid, model.Remarks, model.Address);
        Code = partnerNote?.Country ?? CountryResolver.Resolve(model.Remarks);
        Flag = V2pnConverters.Flag(Code);
        var country = WorldMap.Instance.Get(Code)?.Name;
        var protocol = V2pnConverters.ProtocolName.Convert(model.ConfigType, typeof(string), null, CultureInfo.CurrentCulture) as string;
        var sub = showSubscription && model.SubRemarks.IsNotEmpty() ? model.SubRemarks : null;
        var (place, detail) = ServerNames.SplitPlace(ServerNames.TrimLeadingSymbols(CountryResolver.ServerName(model)), Code);
        if (memberOf != null && ServerNames.TryParseCounter(model.Remarks, out _, out var number, out _))
        {
            // "AUTO — Обход БС · 15/31" under the pool's own row: the number is all that tells them apart.
            Title = string.Format(L.SeriesMember, number);
            Subtitle = string.Join(" · ", new[] { protocol, model.Network }.Where(t => t.IsNotEmpty()));
        }
        else if (detail != null)
        {
            // "Germany (2) - Vless TCP 1/GBIT" does not fit a row. Under its country's row the variant is
            // what matters; in a flat list the place leads and the variant goes below.
            Title = indent ? detail : place;
            // Under "Germany" a plain "Germany" says nothing, so the subscription goes there instead;
            // "Germany (2)" does say which one, and stays.
            Subtitle = !indent ? string.Join(" · ", new[] { detail, sub }.Where(t => t.IsNotEmpty()))
                : place.Any(char.IsDigit) ? place
                : sub ?? protocol ?? string.Empty;
        }
        else
        {
            Title = place;
            // A name that is the country itself ("Швеция") needs no "Швеция" under it.
            var namesCountry = Code.IsNotEmpty()
                && string.Equals(CountryResolver.Resolve(CountryResolver.StripFlag(model.Remarks)), Code, StringComparison.OrdinalIgnoreCase);
            Subtitle = sub ?? string.Join(" · ", new[] { namesCountry ? null : country, protocol }.Where(t => t.IsNotEmpty()));
        }
        if (partnerNote?.City is { } city && !Title.Contains(city, StringComparison.CurrentCultureIgnoreCase))
        {
            Subtitle = Subtitle.IsNullOrEmpty() ? city : $"{city} · {Subtitle}";
        }
        Note = partnerNote?.Note;
        Purposes = partnerNote?.Purposes.Select(V2pnPurposes.Label).ToList() ?? [];
        Indent = indent;
        Monogram = V2pnConverters.ProtocolMonogram.Convert(model.ConfigType, typeof(string), null, CultureInfo.CurrentCulture) as string ?? string.Empty;
    }

    public ProfileItemModel Model { get; }
    public string Monogram { get; }

    /// <summary>The partner's short note about the server, under its name; null without one.</summary>
    public string? Note { get; }
    public bool HasNote => Note != null;

    /// <summary>What the partner says the server is for ("YouTube", "Games"), as chips on the row.</summary>
    public IReadOnlyList<string> Purposes { get; }
    public bool HasPurposes => Purposes.Count > 0;

    /// <summary>Taller than a plain row (a note or chips under the name): it gets padding above and below.</summary>
    public bool IsRich => HasNote || HasPurposes;

    // Inside a country group the flag repeats the group's own, so those rows show the protocol instead.
    public bool ShowFlag => !Indent && HasFlag;
    public bool ShowGlobe => !Indent && !HasFlag;
    public string? Code { get; }
    public Bitmap? Flag { get; }
    public bool HasFlag => Flag != null;
    public string Title { get; }
    public string Subtitle { get; }
    public bool IsActive => Model.IsActive;
    public bool Indent { get; }
    public override string ToString() => Title;
}

public sealed class SideCountryRow : SideRow
{
    public SideCountryRow(string code, string name, IReadOnlyList<ProfileItemModel> servers, bool expanded)
    {
        Code = code;
        Name = name;
        Flag = V2pnConverters.Flag(code);
        Servers = servers;
        IsExpanded = expanded;
        HasActive = servers.Any(s => s.IsActive);
        var best = servers.Where(s => s.Delay > 0).MinBy(s => s.Delay);
        BestDelay = best?.Delay ?? 0;
        BestId = best?.IndexId;
        Info = L.ServerCount(servers.Count);
    }

    /// <summary>
    /// A provider's pool shown the way a country is: one row that opens into its servers and connects to
    /// the quickest. <see cref="Code"/> holds the pool's key, which only has to be unique.
    /// </summary>
    public SideCountryRow(ServerSeries series, IReadOnlyList<ProfileItemModel> servers, bool expanded)
        : this(series.Key, series.Name, servers, expanded)
    {
        Flag = V2pnConverters.Flag(series.Code);
        IsSeries = true;
        // Once anything was tested, how many answer says more than how many there are.
        var tested = servers.Count(s => s.Delay != 0);
        Info = tested > 0
            ? string.Format(L.SeriesOnline, servers.Count(s => s.Delay > 0), servers.Count)
            : L.ServerCount(servers.Count);
    }

    public bool IsSeries { get; }

    public int BestDelay { get; }
    public string? BestId { get; }

    public string Code { get; }
    public string Name { get; }
    public Bitmap? Flag { get; private init; }
    public bool HasFlag => Flag != null;
    public IReadOnlyList<ProfileItemModel> Servers { get; }
    public bool IsExpanded { get; }
    public bool HasActive { get; }
    public string Info { get; private init; }
    public override string ToString() => Name;
}

public sealed class SideSubRow : SideRow
{
    // Tile hues shared with the settings menu; a subscription keeps its colour across restarts.
    private static readonly string[] Tints = ["V2TileBlue", "V2TileGreen", "V2TilePurple", "V2TileAmber", "V2TileTeal", "V2TileRed"];

    public SideSubRow(SubItemModel sub, BusyState busy, bool updateFailed = false)
    {
        Sub = sub;
        Busy = busy;
        UpdateFailed = updateFailed;
        DisplayName = NameOf(sub.Item);
        var name = DisplayName;
        var letter = name.FirstOrDefault(char.IsLetterOrDigit);
        Monogram = letter == default ? "?" : char.ToUpper(letter, CultureInfo.CurrentCulture).ToString();
        var hash = 0;
        foreach (var c in sub.Id ?? name)
        {
            hash = unchecked((hash * 31) + c);
        }
        Tint = Application.Current?.TryGetResource(Tints[Math.Abs(hash % Tints.Length)], null, out var brush) == true ? brush as IBrush : null;
        // V2pn: a verified partner's subscription wears the partner's mark on its own ink, not a letter.
        if (V2pnPartners.Instance.ForSubscription(sub.Item) is { } partner && MarkOf(partner) is { } mark)
        {
            Mark = mark;
            Tint = new SolidColorBrush(Color.Parse("#171716"));
            PartnerTip = string.Format(L.SubPartnerMarkTip, partner.Name);
        }

        var updated = sub.Item.UpdateTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(sub.Item.UpdateTime).LocalDateTime : (DateTime?)null;
        var age = updated.HasValue ? DateTime.Now - updated.Value : (TimeSpan?)null;
        IsFresh = !updateFailed && age < TimeSpan.FromDays(1);
        IsStale = !updateFailed && age >= TimeSpan.FromDays(1);
        Info = $"{L.ServerCount(sub.Count)} · {(updateFailed ? L.SubUpdateFailedShort : Fmt.SubUpdatedShort(sub.Item))}";
        Freshness = updateFailed ? L.SubUpdateFailedTip : age == null ? L.SubNeverUpdated : IsFresh ? L.SubFresh : L.SubStale;
        Usage = SubUsage.From(SubInfoManager.Instance.Get(sub.Id));
        var locked = V2pnLocks.Instance.ForSub(sub.Id);
        LockText = locked.Settings ? string.Format(L.LockedBy, locked.PartnerName) : locked.HideUrl ? string.Format(L.LockedUrl, locked.PartnerName) : null;
    }

    /// <summary>"Settings closed by Kestrel" when the partner closed them (V2pnLocks); null otherwise.</summary>
    public string? LockText { get; }
    public bool IsLocked => LockText != null;

    /// <summary>
    /// The name to show: v2rayN's "import_sub" placeholder and an empty name read as the link's host, so an
    /// existing row from v2rayN stays (it may be a real subscription) but gets a readable title.
    /// </summary>
    public static string NameOf(SubItem? item) => (item == null ? null : SubInfoManager.ReadableName(item)) ?? L.NewSubscription;

    public SubItemModel Sub { get; }
    public string DisplayName { get; }

    /// <summary>The last update brought no servers: the provider's site was unreachable or the link is wrong.</summary>
    public bool UpdateFailed { get; }

    /// <summary>Traffic and expiry the provider reported; null keeps the plain two-line row.</summary>
    public SubUsage? Usage { get; }
    public bool HasUsage => Usage != null;
    public BusyState Busy { get; }
    public string Monogram { get; }

    /// <summary>The partner's square mark, when the subscription comes from a verified partner whose pack has one.</summary>
    public Bitmap? Mark { get; }
    public bool HasMark => Mark != null;
    public bool ShowMonogram => Mark == null && !Busy.IsBusy;
    public string? PartnerTip { get; }

    private static readonly Dictionary<string, Bitmap?> Marks = [];

    private static Bitmap? MarkOf(V2pnPartner partner)
    {
        var path = partner.AssetPath(partner.Pack.Mark);
        if (path == null)
        {
            return null;
        }
        lock (Marks)
        {
            if (!Marks.TryGetValue(path, out var bitmap))
            {
                bitmap = Views.EditionLooks.Scene.Picture(partner, partner.Pack.Mark);
                Marks[path] = bitmap;
            }
            return bitmap;
        }
    }
    public IBrush? Tint { get; }
    public string Info { get; }
    public bool IsFresh { get; }
    public bool IsStale { get; }
    public string Freshness { get; }
    public override string ToString() => DisplayName;
}

public sealed class SideActionRow(SideActionKind kind, string title, string subtitle, string? targetId = null, PingState? ping = null) : SideRow
{
    public SideActionKind Kind { get; } = kind;
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public string? TargetId { get; } = targetId;

    /// <summary>Set on the fastest-server row: with nothing tested yet, clicking it runs the ping.</summary>
    public PingState? Ping { get; } = ping;
    public bool IsFastest => Kind == SideActionKind.Fastest;
    public override string ToString() => Title;
    public bool IsBack => Kind == SideActionKind.Back;

    /// <summary>Back out of a subscription, which carries its update button; the statistics' back row does not.</summary>
    public bool IsBackToSub => IsBack && TargetId != null;
    public bool IsAdd => Kind == SideActionKind.AddSubscription;
    public bool IsRefresh => Kind is SideActionKind.UpdateAll or SideActionKind.UpdateSub;
    public bool IsClearSearch => Kind == SideActionKind.ClearSearch;
    public bool IsBrowse => Kind == SideActionKind.BrowseCountries;
    public bool IsMore => Kind is SideActionKind.ShowAllFavorites or SideActionKind.ShowFewerFavorites or SideActionKind.OpenStats;
    public bool IsStats => Kind is SideActionKind.EnableStats or SideActionKind.DisableStats;
    public bool IsClearStats => Kind == SideActionKind.ClearStats;
}

/// <summary>
/// One line of the statistics: a server (connects on click) or a country (opens it), with its number on
/// the right and a bar for its share of the leader's.
/// </summary>
public sealed class SideStatRow : SideRow
{
    public SideStatRow(ProfileItemModel? model, string? code, string title, string subtitle, string value, double fraction)
    {
        Model = model;
        Code = code;
        Flag = V2pnConverters.Flag(code);
        Title = title;
        Subtitle = subtitle;
        Value = value;
        Fraction = fraction;
    }

    public ProfileItemModel? Model { get; }
    public string? Code { get; }
    public Bitmap? Flag { get; }
    public bool HasFlag => Flag != null;
    public string Title { get; }
    public string Subtitle { get; }
    public string Value { get; }
    public double Fraction { get; }
    public bool IsActive => Model?.IsActive == true;
    public override string ToString() => $"{Title}, {Value}";
}

public sealed record ChartBar(double Height, string Tip, bool IsToday);

/// <summary>Connected time per day as small bars, with the total above.</summary>
public sealed class SideChartRow(string title, IReadOnlyList<ChartBar> bars) : SideRow
{
    public const double MaxBarHeight = 44;
    public string Title { get; } = title;
    public IReadOnlyList<ChartBar> Bars { get; } = bars;
    public override string ToString() => Title;
}

/// <summary>
/// The left panel: favorites, servers grouped by country, subscriptions and search,
/// built from the lists ProfilesViewModel already maintains.
/// </summary>
public sealed partial class SidebarViewModel : ReactiveObject
{
    private readonly ProfilesViewModel _profiles;
    private readonly HashSet<string> _expanded = [];
    private bool _rebuildQueued;
    private List<ProfileItemModel> _pingScope = [];

    public PingState Ping { get; } = new();

    /// <summary>"Update all subscriptions" running (driven by the window from the update command).</summary>
    public BusyState SubsUpdate { get; } = new();

    // Per-subscription update spinners outlive the rows, which are rebuilt when counts change.
    private readonly Dictionary<string, BusyState> _subBusy = [];

    public BusyState SubBusy(string? id) => _subBusy.TryGetValue(id ?? string.Empty, out var b) ? b : _subBusy[id ?? string.Empty] = new BusyState();

    // Subscriptions whose last update in this session brought nothing; cleared by the next one that works.
    private readonly HashSet<string> _failedSubs = [];

    /// <summary>Records an update of <paramref name="tried"/>: the ones in <paramref name="failed"/> get the failed state.</summary>
    public void SetUpdateResults(IEnumerable<string> tried, IEnumerable<string> failed)
    {
        var before = _failedSubs.ToHashSet();
        _failedSubs.ExceptWith(tried);
        _failedSubs.UnionWith(failed);
        if (!_failedSubs.SetEquals(before))
        {
            QueueRebuild();
        }
    }

    public BulkObservableCollection<SideRow> Rows { get; } = [];

    [Reactive]
    public partial SidebarTab Tab { get; set; }

    [Reactive]
    public partial string Search { get; set; }

    [Reactive]
    public partial SubItemModel? OpenedSub { get; set; }

    /// <summary>The statistics view, opened from the home tab.</summary>
    [Reactive]
    public partial bool StatsOpen { get; set; }

    // Home shows the first few favorites; "show all" opens the rest until the user folds them again.
    private const int FavoritesShown = 5;
    private bool _favoritesExpanded;
    private DateTime _statsRebuilt;

    private static UIItem Ui => AppManager.Instance.Config.UiItem;

    // V2pn: in a partner's tab the list holds that partner's subscriptions and their servers only (Common/Editions.cs);
    // null in V2pn's tab, which lists everything, a partner's subscriptions included.
    private HashSet<string>? _scope;

    private void UpdateScope()
    {
        var partner = Editions.Partner;
        _scope = partner == null
            ? null
            : _profiles.SubNavItems.Where(s => V2pnPartners.Instance.ForSubscription(s.Item)?.Id == partner.Id).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
    }

    private bool InScope(ProfileItemModel m) => _scope == null || (m.Subid != null && _scope.Contains(m.Subid));

    private List<ProfileItemModel> Scoped(IEnumerable<ProfileItemModel> items) => _scope == null ? items.ToList() : items.Where(InScope).ToList();

    private List<ProfileItemModel> Favorites => Scoped(_profiles.FavoriteItems);

    // Nothing to list: no server at all, or in a partner's tab no subscription of that partner yet.
    private bool NoServers => _scope == null ? _profiles.AllServersCount == 0 : _scope.Count == 0;

    private string NoServersText => _scope != null && Editions.Partner is { } partner ? string.Format(L.TabNoServers, partner.Name) : L.NoServersYet;

    /// <summary>Another tab opened: the list shows its subscriptions; one opened from another tab closes.</summary>
    private void OnTabChanged()
    {
        UpdateScope();
        if (OpenedSub is { } opened && _scope != null && !_scope.Contains(opened.Id))
        {
            // Setting it rebuilds the list.
            OpenedSub = null;
            return;
        }
        QueueRebuild();
    }

    public static bool StatsEnabled => Ui.V2pnStats;

    public static SideSort CountrySort => Ui.V2pnSort is int v && Enum.IsDefined((SideSort)v) ? (SideSort)v : SideSort.Name;

    // By use needs statistics: with them off, the list (and its button) says by ping instead.
    public static SideSort ServerSort => Ui.V2pnServerSort is int v && Enum.IsDefined((SideSort)v) && ((SideSort)v != SideSort.Usage || StatsEnabled) ? (SideSort)v : SideSort.Ping;

    public void SetSort(SideSortTarget target, SideSort mode)
    {
        if (target == SideSortTarget.Countries)
        {
            Ui.V2pnSort = (int)mode;
        }
        else
        {
            Ui.V2pnServerSort = (int)mode;
        }
        Rebuild();
    }

    public void SetStatsEnabled(bool on)
    {
        Ui.V2pnStats = on;
        if (!on)
        {
            StatsOpen = false;
        }
        Rebuild();
    }

    public SidebarViewModel(ProfilesViewModel profiles)
    {
        _profiles = profiles;
        Search = string.Empty;

        profiles.ProfileItems.CollectionChanged += (_, _) => QueueRebuild();
        profiles.FavoriteItems.CollectionChanged += (_, _) => QueueRebuild();
        // V2pn: a partner verified or dropped changes the mark on its subscription.
        V2pnPartners.Instance.Changed += () => Dispatcher.UIThread.Post(QueueRebuild);
        // V2pn: what a partner closed, and its live word about its servers (groups, purposes, notes).
        V2pnLocks.Instance.Changed += () => Dispatcher.UIThread.Post(QueueRebuild);
        V2pnPartnerLive.Instance.Changed += () => Dispatcher.UIThread.Post(QueueRebuild);
        profiles.SubNavItems.CollectionChanged += (_, _) => QueueRebuild();
        Editions.Changed += OnTabChanged;
        // Fastest server and the best ping per country are computed on rebuild.
        profiles.PingTestFinished.AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => QueueRebuild());
        profiles.WhenAnyValue(x => x.IsPingTesting, x => x.PingDone, x => x.PingTotal, (testing, done, total) => (testing, done, total))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(t =>
            {
                Ping.IsTesting = t.testing;
                // Figure spaces keep "9/40" as wide as "10/40", so the button does not twitch on every result.
                Ping.Label = t.testing ? string.Format(L.PingProgress, t.done.ToString().PadLeft(t.total.ToString().Length, '\u2007'), t.total) : L.TestPing;
                Ping.Progress = t.testing ? string.Format(L.FindProgress, t.done, t.total) : string.Empty;
            });

        this.WhenAnyValue(x => x.Tab, x => x.Search, x => x.OpenedSub, x => x.StatsOpen)
            .Subscribe(_ => ApplyNavigation());

        // The connected time grows every few seconds; the open statistics follow it, at most once a minute.
        ServerUsageManager.Instance.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            if (StatsOpen && Tab == SidebarTab.Home && Search.IsNullOrEmpty() && DateTime.Now - _statsRebuilt > TimeSpan.FromMinutes(1))
            {
                QueueRebuild();
            }
        });
        ServerUsageManager.Instance.LoadAsync().ContinueWith(_ => Dispatcher.UIThread.Post(QueueRebuild), TaskScheduler.Default);

        SubInfoManager.Instance.Changed += id => Dispatcher.UIThread.Post(async () => await OnSubInfoChangedAsync(id));
        SubInfoManager.Instance.LoadAsync().ContinueWith(_ => Dispatcher.UIThread.Post(QueueRebuild), TaskScheduler.Default);
    }

    // An update may have renamed the subscription from its panel; the cached items still hold the old name.
    private async Task OnSubInfoChangedAsync(string id)
    {
        try
        {
            var stored = await AppManager.Instance.GetSubItem(id);
            var cached = _profiles.SubNavItems.FirstOrDefault(s => s.Id == id)?.Item;
            if (stored != null && cached != null)
            {
                cached.Remarks = stored.Remarks;
                cached.AutoUpdateInterval = stored.AutoUpdateInterval;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Sidebar", ex);
        }
        QueueRebuild();
    }

    /// <summary>Pings <paramref name="scope"/> (what the list shows when null), or stops a run in progress.</summary>
    public async Task TogglePingAsync(IReadOnlyList<ProfileItemModel>? scope = null)
    {
        if (Ping.IsTesting)
        {
            _profiles.ServerSpeedtestStop();
            return;
        }
        await _profiles.PingProfilesAsync(scope ?? _pingScope);
    }

    /// <summary>
    /// The quickest server by the last results. With statistics on, a server that failed most of its
    /// tests is passed over even when its last answer was quick: it is likely to drop again.
    /// </summary>
    public static ProfileItemModel? PickFastest(IEnumerable<ProfileItemModel> items)
    {
        var answered = items.Where(m => m.Delay > 0).ToList();
        return answered.Where(m => !Unreliable(m)).MinBy(m => m.Delay) ?? answered.MinBy(m => m.Delay);
    }

    private static bool Unreliable(ProfileItemModel m) =>
        StatsEnabled && ServerUsageManager.Instance.Get(m.IndexId) is { Tests: >= 4 } u && u.AnswerRate < 0.5;

    /// <summary>The last ping run is older than this: "fastest" tests its candidates again before connecting.</summary>
    public static readonly TimeSpan FastestFresh = TimeSpan.FromMinutes(30);

    public static DateTime? LastPing => Ui.V2pnLastPing > 0 ? DateTimeOffset.FromUnixTimeSeconds(Ui.V2pnLastPing).LocalDateTime : null;

    public static bool PingIsStale => LastPing is not { } last || DateTime.Now - last > FastestFresh;

    /// <summary>
    /// What a quick recheck before "fastest" tests: the servers that were quickest last time, plus
    /// favorites and recent ones. A couple of dozen answer in seconds; a big subscription takes a minute.
    /// </summary>
    public List<ProfileItemModel> FastestCandidates()
    {
        var all = Scoped(_profiles.ProfileItems);
        var recent = (Ui.V2pnRecent ?? []).ToHashSet();
        return ServerNames.DistinctConnections(all.Where(m => m.Delay > 0).OrderBy(m => m.Delay).Take(12)
                .Concat(Favorites)
                .Concat(all.Where(m => recent.Contains(m.IndexId))))
            .GroupBy(m => m.IndexId)
            .Select(g => g.First())
            .Take(24)
            .ToList();
    }

    /// <summary>Starts a ping of every server in the list; does nothing while a run is already going.</summary>
    public Task PingAllAsync() => Ping.IsTesting ? Task.CompletedTask : _profiles.PingProfilesAsync(Scoped(_profiles.ProfileItems));

    /// <summary>Opens the Countries tab with only this country expanded; returns its row.</summary>
    public SideCountryRow? RevealCountry(string code)
    {
        Search = string.Empty;
        OpenedSub = null;
        _expanded.Clear();
        _expanded.Add(code);
        Tab = SidebarTab.Countries;
        Rebuild();
        return Rows.OfType<SideCountryRow>().FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public void ToggleCountry(string code)
    {
        if (!_expanded.Add(code))
        {
            _expanded.Remove(code);
        }
        Rebuild();
    }

    public void QueueRebuild()
    {
        if (_rebuildQueued)
        {
            return;
        }
        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        }, DispatcherPriority.Background);
    }

    private void ApplyNavigation()
    {
        var wantSub = Search.IsNullOrEmpty() && Tab == SidebarTab.Subscriptions ? OpenedSub?.Item : null;
        var currentSub = _profiles.SelectedSub?.Id;
        if (wantSub != null)
        {
            if (currentSub != wantSub.Id || _profiles.FavoritesOnly)
            {
                _profiles.SelectNav(wantSub, false);
            }
        }
        else if (currentSub.IsNotEmpty() || _profiles.FavoritesOnly)
        {
            // Countries, favorites and search all work from the full server list.
            _profiles.SelectNav(null, false);
        }
        if (Tab != SidebarTab.Home && StatsOpen)
        {
            // Setting it runs this method again.
            StatsOpen = false;
            return;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        var rows = new List<SideRow>();
        UpdateScope();
        var all = Scoped(_profiles.ProfileItems);

        if (Search.IsNotEmpty())
        {
            var q = Search.Trim();
            var found = all.Where(m =>
                    m.Remarks?.Contains(q, StringComparison.OrdinalIgnoreCase) == true
                    || (m.Address?.Contains(q, StringComparison.OrdinalIgnoreCase) == true && !V2pnLocks.Instance.Hides(m.Subid))
                    || (WorldMap.Instance.Get(CountryResolver.Resolve(m.Remarks))?.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            _pingScope = found;
            rows.Add(new SideHeaderRow(Counted(L.SearchResults, found.Count), found.Count > 0 ? Ping : null)
            {
                Sort = found.Count > 1 ? new SortState(SideSortTarget.Servers, ServerSort) : null,
            });
            rows.AddRange(SortServers(found, ServerSort).Take(400).Select(m => new SideServerRow(m, true, false)));
            if (found.Count == 0)
            {
                rows.Add(new SideHintRow(L.NothingFoundServers));
                rows.Add(new SideActionRow(SideActionKind.ClearSearch, L.ClearSearch, string.Empty));
            }
        }
        else
        {
            switch (Tab)
            {
                case SidebarTab.Home when StatsOpen:
                    BuildStats(rows, all);
                    _statsRebuilt = DateTime.Now;
                    break;

                case SidebarTab.Home:
                    BuildHome(rows, all);
                    break;

                case SidebarTab.Countries:
                    BuildCountries(rows, all);
                    break;

                case SidebarTab.Subscriptions:
                    BuildSubscriptions(rows, all);
                    break;
            }
        }

        Rows.ReplaceRange(rows);
        Rebuilt?.Invoke();
    }

    /// <summary>Raised after the rows were replaced; the list puts keyboard focus back on the same row.</summary>
    public event Action? Rebuilt;

    private void BuildHome(List<SideRow> rows, List<ProfileItemModel> all)
    {
        // Nothing added yet: "fastest server" and favorites hints would point at servers that don't exist.
        if (NoServers && Favorites.Count == 0)
        {
            _pingScope = [];
            rows.Add(new SideHeaderRow(L.TabHome));
            rows.Add(new SideHintRow(NoServersText));
            rows.Add(new SideActionRow(SideActionKind.AddSubscription, L.AddSubscription, L.SubsEmptyAction));
            return;
        }
        var everything = all.Concat(Favorites).GroupBy(m => m.IndexId).Select(g => g.First()).ToList();
        _pingScope = everything;
        var fastest = PickFastest(everything);
        if (fastest == null)
        {
            rows.Add(new SideActionRow(SideActionKind.Fastest, L.Fastest, L.FastestNone, ping: Ping));
        }
        else
        {
            // An old result is still shown, with a word that the click tests again first.
            var age = LastPing is { } last ? Fmt.Ago(last) : null;
            var subtitle = PingIsStale
                ? $"{CountryResolver.ServerName(fastest)} · {string.Format(L.FastestRecheck, age ?? L.Ms(fastest.Delay))}"
                : string.Format(L.FastestAge, CountryResolver.ServerName(fastest), L.Ms(fastest.Delay), age ?? L.JustNow);
            rows.Add(new SideActionRow(SideActionKind.Fastest, L.Fastest, subtitle, fastest.IndexId, Ping));
        }

        // Favorites: the first few; the rest one click away, so the other sections stay in view.
        var favorites = Favorites;
        rows.Add(new SideHeaderRow(Counted(L.TabFavorites, favorites.Count), favorites.Count > 0 ? Ping : null) { PingScope = favorites });
        var shown = _favoritesExpanded ? favorites : favorites.Take(FavoritesShown).ToList();
        rows.AddRange(shown.Select(m => new SideServerRow(m, true, false)));
        if (favorites.Count > FavoritesShown)
        {
            rows.Add(_favoritesExpanded
                ? new SideActionRow(SideActionKind.ShowFewerFavorites, L.ShowFewer, string.Empty)
                : new SideActionRow(SideActionKind.ShowAllFavorites, L.ShowAllFavorites, string.Format(L.MoreCount, favorites.Count - FavoritesShown)));
        }
        if (favorites.Count == 0)
        {
            rows.Add(new SideHintRow(L.FavoritesEmpty));
            // The home tab has no servers to star; Countries does.
            rows.Add(new SideActionRow(SideActionKind.BrowseCountries, L.BrowseCountries, string.Empty));
        }

        // Recent connections, Proton-style: the last few servers, newest first, unless they are favorites already.
        var byId = everything.ToDictionary(m => m.IndexId);
        var favoriteIds = favorites.Select(m => m.IndexId).ToHashSet();
        var recent = (Ui.V2pnRecent ?? [])
            .Where(id => !favoriteIds.Contains(id) && byId.ContainsKey(id))
            .Take(3)
            .Select(id => byId[id])
            .ToList();
        if (recent.Count > 0)
        {
            rows.Add(new SideHeaderRow(L.Recent));
            rows.AddRange(recent.Select(m => new SideServerRow(m, true, false)));
        }

        // The quickest five right now: what to pick from when the very fastest is not the one wanted.
        var quick = ServerNames.DistinctConnections(everything).Where(m => m.Delay > 0).OrderBy(m => m.Delay).Take(5).ToList();
        rows.Add(new SideHeaderRow(L.FastestList, Ping) { PingScope = everything });
        if (quick.Count > 0)
        {
            rows.AddRange(quick.Select(m => new SideServerRow(m, true, false)));
        }
        else
        {
            rows.Add(new SideHintRow(L.FastestListEmpty));
        }

        BuildStatsSummary(rows, byId);
    }

    private static void BuildStatsSummary(List<SideRow> rows, Dictionary<string, ProfileItemModel> byId)
    {
        if (!StatsEnabled)
        {
            rows.Add(new SideHeaderRow(L.Stats));
            rows.Add(new SideHintRow(L.StatsOffHint));
            rows.Add(new SideActionRow(SideActionKind.EnableStats, L.StatsEnable, L.StatsEnableSub));
            return;
        }
        rows.Add(new SideHeaderRow(L.Stats) { Link = L.StatsMore, LinkKind = SideActionKind.OpenStats });
        var used = ServerUsageBook.MostUsed(ServerUsageManager.Instance.All().Where(t => byId.ContainsKey(t.IndexId)), 3);
        if (used.Count == 0)
        {
            rows.Add(new SideHintRow(L.StatsEmpty));
            return;
        }
        rows.AddRange(UsedRows(used, byId));
    }

    private void BuildStats(List<SideRow> rows, List<ProfileItemModel> all)
    {
        _pingScope = [];
        var byId = all.Concat(Favorites).GroupBy(m => m.IndexId).ToDictionary(g => g.Key, g => g.First());
        var usage = ServerUsageManager.Instance.All().Where(t => byId.ContainsKey(t.IndexId)).ToList();
        var days = ServerUsageManager.Instance.LastDays(14);
        var total = days.Sum(d => d.Seconds);
        rows.Add(new SideActionRow(SideActionKind.Back, L.Stats, string.Format(L.StatsDays, L.Spent(total))));

        var max = Math.Max(1, days.Max(d => d.Seconds));
        var today = DateTime.Today;
        rows.Add(new SideChartRow(L.StatsDaysTip, days.Select(d => new ChartBar(
            d.Seconds <= 0 ? 2 : Math.Max(4, SideChartRow.MaxBarHeight * d.Seconds / max),
            $"{d.Day.ToString("d MMM", CultureInfo.CurrentUICulture)}: {L.Spent(d.Seconds)}",
            d.Day == today)).ToList()));

        var used = ServerUsageBook.MostUsed(usage, 10);
        rows.Add(new SideHeaderRow(L.StatsMostUsed));
        if (used.Count == 0)
        {
            rows.Add(new SideHintRow(L.StatsEmpty));
        }
        rows.AddRange(UsedRows(used, byId));

        var reliable = ServerUsageBook.MostReliable(usage, 10);
        rows.Add(new SideHeaderRow(L.StatsMostReliable));
        if (reliable.Count == 0)
        {
            rows.Add(new SideHintRow(L.StatsReliableEmpty));
        }
        foreach (var t in reliable)
        {
            var m = byId[t.IndexId];
            rows.Add(new SideStatRow(m, CountryResolver.Resolve(m.Remarks), StatName(m),
                string.Format(L.StatsAnswered, t.Answers, t.Tests, L.Ms(t.AverageDelay)),
                L.Percent(t.AnswerRate ?? 0), t.AnswerRate ?? 0));
        }

        // Countries by time: where the user actually goes, whichever server took them there.
        var countries = usage.Where(t => t.ConnectedSeconds > 0)
            .GroupBy(t => CountryResolver.Resolve(byId[t.IndexId].Remarks) ?? string.Empty)
            .Where(g => g.Key.IsNotEmpty())
            .Select(g => (Code: g.Key, Seconds: g.Sum(t => t.ConnectedSeconds), Sessions: g.Sum(t => t.Sessions)))
            .OrderByDescending(c => c.Seconds)
            .Take(5)
            .ToList();
        if (countries.Count > 0)
        {
            rows.Add(new SideHeaderRow(L.StatsCountries));
            var top = Math.Max(1, countries[0].Seconds);
            rows.AddRange(countries.Select(c => new SideStatRow(null, c.Code, CountryName(c.Code), L.Connections(c.Sessions),
                L.Spent(c.Seconds), (double)c.Seconds / top)));
        }

        rows.Add(new SideHeaderRow(string.Empty));
        rows.Add(new SideActionRow(SideActionKind.DisableStats, L.StatsDisable, L.StatsDisableSub));
        rows.Add(new SideActionRow(SideActionKind.ClearStats, L.StatsClear, string.Empty));
    }

    private static IEnumerable<SideStatRow> UsedRows(List<ServerUsageItem> used, Dictionary<string, ProfileItemModel> byId)
    {
        var top = Math.Max(1, used.FirstOrDefault()?.ConnectedSeconds ?? 1);
        foreach (var t in used)
        {
            var m = byId[t.IndexId];
            var last = t.LastConnected > 0 ? Fmt.Ago(DateTimeOffset.FromUnixTimeSeconds(t.LastConnected).LocalDateTime) : null;
            yield return new SideStatRow(m, CountryResolver.Resolve(m.Remarks), StatName(m),
                string.Join(" · ", new[] { L.Connections(t.Sessions), last }.Where(x => x.IsNotEmpty())),
                L.Spent(t.ConnectedSeconds), (double)t.ConnectedSeconds / top);
        }
    }

    // The name as the server rows show it: without the flag emoji the row draws as a picture already.
    private static string StatName(ProfileItemModel m) => ServerNames.TrimLeadingSymbols(CountryResolver.ServerName(m));

    public void ToggleFavoritesExpanded()
    {
        _favoritesExpanded = !_favoritesExpanded;
        Rebuild();
    }

    /// <summary>
    /// The provider's pools ("AUTO · 1/34" … "34/34"), each as one row that opens into its servers.
    /// Without this a subscription of two pools and sixteen countries is eighty rows, sixty-five of them
    /// with the same name cut off before the number.
    /// </summary>
    private void AddSeries(List<SideRow> rows, List<ServerSeries> series)
    {
        foreach (var pool in series)
        {
            var items = ByPing(pool.Members).ToList();
            var expanded = _expanded.Contains(pool.Key);
            rows.Add(new SideCountryRow(pool, items, expanded));
            if (expanded)
            {
                rows.AddRange(items.Select(m => new SideServerRow(m, false, true, pool)));
            }
        }
    }

    private void BuildCountries(List<SideRow> rows, List<ProfileItemModel> all)
    {
        var (series, rest) = ServerNames.SplitSeries(all);
        var sort = CountrySort == SideSort.Usage && !StatsEnabled ? SideSort.Name : CountrySort;
        // V2pn: a partner's own country for a server wins over a guess from its name.
        var groups = SortCountries(ServerNames.DistinctConnections(rest)
            .GroupBy(m => V2pnPartnerLive.Instance.Describe(m.Subid, m.Remarks, m.Address)?.Country ?? CountryResolver.Resolve(m.Remarks) ?? string.Empty)
            .Select(g => (Code: g.Key, Name: CountryName(g.Key), Items: SortServers(g, sort == SideSort.Usage ? SideSort.Usage : SideSort.Ping).ToList())), sort)
            .ToList();

        _pingScope = all;
        if (series.Count > 0)
        {
            // Pools cross borders, so they are not listed under a country; the ping button moves up with them.
            rows.Add(new SideHeaderRow(Counted(L.SeriesHeader, series.Count), Ping));
            AddSeries(rows, series);
        }
        rows.Add(new SideHeaderRow(Counted(L.TabCountries, groups.Count(g => g.Code.IsNotEmpty())), all.Count > 0 && series.Count == 0 ? Ping : null)
        {
            Sort = groups.Count > 1 ? new SortState(SideSortTarget.Countries, sort) : null,
        });
        foreach (var (code, name, items) in groups)
        {
            var expanded = _expanded.Contains(code);
            rows.Add(new SideCountryRow(code, name, items, expanded));
            if (expanded)
            {
                rows.AddRange(items.Select(m => new SideServerRow(m, true, true)));
            }
        }
        if (groups.Count == 0)
        {
            rows.Add(new SideHintRow(NoServers ? NoServersText : L.SubEmpty));
        }
    }

    /// <summary>
    /// The servers of a partner's subscription by the groups its pack and API give them, in the partner's order (the
    /// first server named in each group), the ungrouped ones last. Null when the partner names no group.
    /// </summary>
    private static List<(string? Group, List<ProfileItemModel> Members)>? PartnerGroups(string? subId, List<ProfileItemModel> servers)
    {
        if (V2pnLocks.Instance.PartnerOf(subId) is not { } partner || servers.Count == 0)
        {
            return null;
        }
        var byServer = servers.Select(m => (Model: m, Group: V2pnPartnerLive.Instance.Describe(partner, m.Remarks, m.Address)?.Group)).ToList();
        if (byServer.All(t => t.Group == null))
        {
            return null;
        }
        var named = partner.Pack.ServerNotes.Concat(V2pnPartnerLive.Instance.Servers(partner.Id) ?? [])
            .Select(n => n.Group).Where(g => g != null).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        int Rank(string? group)
        {
            if (group == null)
            {
                return int.MaxValue;
            }
            var i = named.FindIndex(g => string.Equals(g, group, StringComparison.CurrentCultureIgnoreCase));
            return i < 0 ? int.MaxValue - 1 : i;
        }
        return byServer
            .GroupBy(t => t.Group, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(g => Rank(g.Key))
            .Select(g => (g.Key, g.Select(t => t.Model).ToList()))
            .ToList();
    }

    // One count format for every list header; no "(0)" above an empty-state hint.
    private static string Counted(string title, int n) => n > 0 ? $"{title} · {n}" : title;

    // Picking a server is mostly picking the fastest one: answered servers first, quickest on top, then
    // the untested ones, and the ones that did not answer last; each group keeps its own order.
    private static IEnumerable<ProfileItemModel> ByPing(IEnumerable<ProfileItemModel> items) =>
        items.OrderBy(m => m.Delay > 0 ? 0 : m.Delay == 0 ? 1 : 2).ThenBy(m => m.Delay > 0 ? m.Delay : 0);

    private static long Seconds(ProfileItemModel m) => ServerUsageManager.Instance.Get(m.IndexId)?.ConnectedSeconds ?? 0;

    /// <summary>A flat list of servers in the chosen order; by use needs statistics and falls back to ping.</summary>
    private static IEnumerable<ProfileItemModel> SortServers(IEnumerable<ProfileItemModel> items, SideSort sort) => sort switch
    {
        SideSort.Name => items.OrderBy(m => CountryResolver.ServerName(m), NaturalComparer.Instance),
        SideSort.Usage when StatsEnabled => ByPing(items).OrderByDescending(Seconds),
        _ => ByPing(items),
    };

    private static IEnumerable<(string Code, string Name, List<ProfileItemModel> Items)> SortCountries(
        IEnumerable<(string Code, string Name, List<ProfileItemModel> Items)> groups, SideSort sort)
    {
        // "Unknown location" always last: it is not a place to pick.
        var ordered = groups.OrderBy(g => g.Code.IsNullOrEmpty() ? 1 : 0);
        return sort switch
        {
            SideSort.Ping => ordered
                .ThenBy(g => g.Items.Any(m => m.Delay > 0) ? 0 : g.Items.Any(m => m.Delay == 0) ? 1 : 2)
                .ThenBy(g => g.Items.Where(m => m.Delay > 0).Select(m => m.Delay).DefaultIfEmpty(0).Min())
                .ThenBy(g => g.Name, StringComparer.CurrentCulture),
            SideSort.Usage => ordered
                .ThenByDescending(g => g.Items.Sum(Seconds))
                .ThenBy(g => g.Name, StringComparer.CurrentCulture),
            _ => ordered.ThenBy(g => g.Name, StringComparer.CurrentCulture),
        };
    }

    private static string CountryName(string code) => code switch
    {
        "" => L.NoCountry,
        "EU" => L.Europe,
        _ => WorldMap.Instance.Get(code)?.Name ?? code,
    };

    private void BuildSubscriptions(List<SideRow> rows, List<ProfileItemModel> all)
    {
        if (OpenedSub != null)
        {
            _pingScope = all;
            var updated = _failedSubs.Contains(OpenedSub.Id) ? L.SubUpdateFailedShort : Fmt.SubUpdatedShort(OpenedSub.Item);
            rows.Add(new SideActionRow(SideActionKind.Back, SideSubRow.NameOf(OpenedSub.Item), $"{L.ServerCount(all.Count)} · {updated}", OpenedSub.Id));
            // V2pn: what the provider says about this subscription, above its servers.
            var info = SubInfoManager.Instance.Get(OpenedSub.Id);
            if (V2pnSubDefaults.Banner(info, DateTimeOffset.Now) is { } openedBanner)
            {
                rows.Add(new SideBannerRow(openedBanner, OpenedSub.Id));
            }
            if (info?.Announce.IsNotEmpty() == true)
            {
                rows.Add(new SideAnnounceRow(info.Announce!, info.AnnounceUrl));
            }
            // The order the person chose for this subscription, else the provider's, else fastest first.
            var sort = V2pnSubDefaults.SortOf(info, V2pnLocks.Instance.Hides(OpenedSub.Id));
            // V2pn: a partner that sorts its servers into groups gets a header per group, in its own order.
            var groups = PartnerGroups(OpenedSub.Id, all);
            if (groups != null)
            {
                for (var i = 0; i < groups.Count; i++)
                {
                    var (group, members) = groups[i];
                    rows.Add(new SideHeaderRow(Counted(group ?? L.GroupOther, members.Count), i == 0 ? Ping : null));
                    rows.AddRange(V2pnSubDefaults.Order(members, sort, m => m.Remarks, m => m.Delay).Select(m => new SideServerRow(m, false, false)));
                }
                return;
            }
            if (all.Count > 0)
            {
                rows.Add(new SideHeaderRow(L.TabServers, Ping)
                {
                    Sort = all.Count > 1 ? new SortState(SideSortTarget.Servers, ServerSort) : null,
                });
            }
            var (series, rest) = ServerNames.SplitSeries(all);
            AddSeries(rows, series);
            // The order the person picked in the list's header wins; until then the subscription's own.
            var order = Ui.V2pnServerSort != null ? SortServers(rest, ServerSort) : V2pnSubDefaults.Order(rest, sort, m => m.Remarks, m => m.Delay);
            rows.AddRange(order.Select(m => new SideServerRow(m, false, false)));
            if (all.Count == 0)
            {
                rows.Add(new SideHintRow(L.SubEmpty));
                rows.Add(new SideActionRow(SideActionKind.UpdateSub, L.UpdateSubscription, string.Empty, OpenedSub.Id));
            }
            return;
        }

        _pingScope = [];
        var subs = _profiles.SubNavItems.Where(s => _scope == null || _scope.Contains(s.Id)).ToList();
        if (subs.Count == 0)
        {
            rows.Add(new SideHeaderRow(L.TabSubscriptions));
            rows.Add(new SideHintRow(_scope == null ? L.SubsEmpty : NoServersText));
            rows.Add(new SideActionRow(SideActionKind.AddSubscription, L.AddSubscription, L.SubsEmptyAction));
            return;
        }
        rows.Add(new SideHeaderRow(Counted(L.TabSubscriptions, subs.Count), subsUpdate: SubsUpdate));
        var now = DateTimeOffset.Now;
        foreach (var sub in subs)
        {
            rows.Add(new SideSubRow(sub, SubBusy(sub.Id), _failedSubs.Contains(sub.Id)));
            // V2pn: the provider's banner, or the reminder that the subscription runs out, right under it.
            if (V2pnSubDefaults.Banner(SubInfoManager.Instance.Get(sub.Id), now) is { } banner)
            {
                rows.Add(new SideBannerRow(banner, sub.Id));
            }
        }
    }
}

/// <summary>Compares names with their numbers as numbers: "Server 2" before "Server 10".</summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x == null || y == null)
        {
            return x == null ? (y == null ? 0 : -1) : 1;
        }
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsDigit(x[i]))
                {
                    i++;
                }
                while (j < y.Length && char.IsDigit(y[j]))
                {
                    j++;
                }
                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                var c = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (c != 0)
                {
                    return c;
                }
                continue;
            }
            var d = string.Compare(x[i].ToString(), y[j].ToString(), StringComparison.CurrentCultureIgnoreCase);
            if (d != 0)
            {
                return d;
            }
            i++;
            j++;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
