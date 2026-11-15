using v2rayN.Desktop.Common;
using v2rayN.Desktop.Converters;

namespace v2rayN.Desktop.ViewModels;

public enum SidebarTab
{
    Favorites,
    Countries,
    Subscriptions,
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

public sealed class SideHeaderRow(string title, PingState? ping = null, BusyState? subsUpdate = null) : SideRow
{
    public string Title { get; } = title;
    public PingState? Ping { get; } = ping;
    public bool ShowPing => Ping != null;

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
        Code = CountryResolver.Resolve(model.Remarks);
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
        Indent = indent;
        Monogram = V2pnConverters.ProtocolMonogram.Convert(model.ConfigType, typeof(string), null, CultureInfo.CurrentCulture) as string ?? string.Empty;
    }

    public ProfileItemModel Model { get; }
    public string Monogram { get; }

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
    }

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
    public bool IsAdd => Kind == SideActionKind.AddSubscription;
    public bool IsRefresh => Kind is SideActionKind.UpdateAll or SideActionKind.UpdateSub;
    public bool IsClearSearch => Kind == SideActionKind.ClearSearch;
    public bool IsBrowse => Kind == SideActionKind.BrowseCountries;
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

    public SidebarViewModel(ProfilesViewModel profiles)
    {
        _profiles = profiles;
        Search = string.Empty;

        profiles.ProfileItems.CollectionChanged += (_, _) => QueueRebuild();
        profiles.FavoriteItems.CollectionChanged += (_, _) => QueueRebuild();
        // V2pn: a partner verified or dropped changes the mark on its subscription.
        V2pnPartners.Instance.Changed += () => Dispatcher.UIThread.Post(QueueRebuild);
        profiles.SubNavItems.CollectionChanged += (_, _) => QueueRebuild();
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

        this.WhenAnyValue(x => x.Tab, x => x.Search, x => x.OpenedSub)
            .Subscribe(_ => ApplyNavigation());

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

    /// <summary>Pings what the list shows now, or stops a run in progress.</summary>
    public async Task TogglePingAsync()
    {
        if (Ping.IsTesting)
        {
            _profiles.ServerSpeedtestStop();
            return;
        }
        await _profiles.PingProfilesAsync(_pingScope);
    }

    /// <summary>Starts a ping of every server in the list; does nothing while a run is already going.</summary>
    public Task PingAllAsync() => Ping.IsTesting ? Task.CompletedTask : _profiles.PingProfilesAsync(_profiles.ProfileItems.ToList());

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
        Rebuild();
    }

    private void Rebuild()
    {
        var rows = new List<SideRow>();
        var all = _profiles.ProfileItems.ToList();

        if (Search.IsNotEmpty())
        {
            var q = Search.Trim();
            var found = all.Where(m =>
                    m.Remarks?.Contains(q, StringComparison.OrdinalIgnoreCase) == true
                    || m.Address?.Contains(q, StringComparison.OrdinalIgnoreCase) == true
                    || (WorldMap.Instance.Get(CountryResolver.Resolve(m.Remarks))?.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            _pingScope = found;
            rows.Add(new SideHeaderRow(Counted(L.SearchResults, found.Count), found.Count > 0 ? Ping : null));
            rows.AddRange(ByPing(found).Take(400).Select(m => new SideServerRow(m, true, false)));
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
                case SidebarTab.Favorites:
                    BuildFavorites(rows, all);
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

    private void BuildFavorites(List<SideRow> rows, List<ProfileItemModel> all)
    {
        // Nothing added yet: "fastest server" and favorites hints would point at servers that don't exist.
        if (_profiles.AllServersCount == 0 && _profiles.FavoriteItems.Count == 0)
        {
            _pingScope = [];
            rows.Add(new SideHeaderRow(L.TabFavorites));
            rows.Add(new SideHintRow(L.NoServersYet));
            rows.Add(new SideActionRow(SideActionKind.AddSubscription, L.AddSubscription, L.SubsEmptyAction));
            return;
        }
        var fastest = all.Concat(_profiles.FavoriteItems).Where(m => m.Delay > 0).MinBy(m => m.Delay);
        rows.Add(fastest != null
            ? new SideActionRow(SideActionKind.Fastest, L.Fastest, $"{CountryResolver.ServerName(fastest)} · {L.Ms(fastest.Delay)}", fastest.IndexId, Ping)
            : new SideActionRow(SideActionKind.Fastest, L.Fastest, L.FastestNone, ping: Ping));

        // Recent connections, Proton-style: the last few servers, newest first, unless they are favorites already.
        var byId = all.Concat(_profiles.FavoriteItems).GroupBy(m => m.IndexId).ToDictionary(g => g.Key, g => g.First());
        var favoriteIds = _profiles.FavoriteItems.Select(m => m.IndexId).ToHashSet();
        var recent = (AppManager.Instance.Config.UiItem.V2pnRecent ?? [])
            .Where(id => !favoriteIds.Contains(id) && byId.ContainsKey(id))
            .Take(3)
            .Select(id => byId[id])
            .ToList();
        if (recent.Count > 0)
        {
            rows.Add(new SideHeaderRow(L.Recent));
            rows.AddRange(recent.Select(m => new SideServerRow(m, true, false)));
        }

        var favorites = _profiles.FavoriteItems.ToList();
        _pingScope = favorites;
        rows.Add(new SideHeaderRow(Counted(L.TabFavorites, favorites.Count), favorites.Count > 0 ? Ping : null));
        rows.AddRange(favorites.Select(m => new SideServerRow(m, true, false)));
        if (favorites.Count == 0)
        {
            rows.Add(new SideHintRow(L.FavoritesEmpty));
            // The Favorites tab has no servers to star; Countries does.
            rows.Add(new SideActionRow(SideActionKind.BrowseCountries, L.BrowseCountries, string.Empty));
        }
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
        var groups = ServerNames.DistinctConnections(rest)
            .GroupBy(m => CountryResolver.Resolve(m.Remarks) ?? string.Empty)
            .Select(g => (Code: g.Key, Name: CountryName(g.Key), Items: ByPing(g).ToList()))
            .OrderBy(g => g.Code.IsNullOrEmpty() ? 1 : 0)
            .ThenBy(g => g.Name, StringComparer.CurrentCulture)
            .ToList();

        _pingScope = all;
        if (series.Count > 0)
        {
            // Pools cross borders, so they are not listed under a country; the ping button moves up with them.
            rows.Add(new SideHeaderRow(Counted(L.SeriesHeader, series.Count), Ping));
            AddSeries(rows, series);
        }
        rows.Add(new SideHeaderRow(Counted(L.TabCountries, groups.Count(g => g.Code.IsNotEmpty())), all.Count > 0 && series.Count == 0 ? Ping : null));
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
            rows.Add(new SideHintRow(_profiles.AllServersCount == 0 ? L.NoServersYet : L.SubEmpty));
        }
    }

    // One count format for every list header; no "(0)" above an empty-state hint.
    private static string Counted(string title, int n) => n > 0 ? $"{title} · {n}" : title;

    // Picking a server is mostly picking the fastest one: tested servers first, quickest on top,
    // the rest keep their own order.
    private static IEnumerable<ProfileItemModel> ByPing(IEnumerable<ProfileItemModel> items) =>
        items.OrderBy(m => m.Delay > 0 ? m.Delay : int.MaxValue);

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
            if (all.Count > 0)
            {
                rows.Add(new SideHeaderRow(L.TabServers, Ping));
            }
            var (series, rest) = ServerNames.SplitSeries(all);
            AddSeries(rows, series);
            // The order the person chose for this subscription, else the provider's, else fastest first.
            var order = V2pnSubDefaults.Order(rest, V2pnSubDefaults.SortOf(info), m => m.Remarks, m => m.Delay);
            rows.AddRange(order.Select(m => new SideServerRow(m, false, false)));
            if (all.Count == 0)
            {
                rows.Add(new SideHintRow(L.SubEmpty));
                rows.Add(new SideActionRow(SideActionKind.UpdateSub, L.UpdateSubscription, string.Empty, OpenedSub.Id));
            }
            return;
        }

        _pingScope = [];
        var subs = _profiles.SubNavItems.ToList();
        if (subs.Count == 0)
        {
            rows.Add(new SideHeaderRow(L.TabSubscriptions));
            rows.Add(new SideHintRow(L.SubsEmpty));
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
