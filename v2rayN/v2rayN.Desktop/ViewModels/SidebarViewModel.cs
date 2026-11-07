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
}

public abstract class SideRow : ReactiveObject;

/// <summary>Progress of the ping run started from the sidebar, shared by every list header.</summary>
public sealed partial class PingState : ReactiveObject
{
    [Reactive]
    public partial bool IsTesting { get; set; }

    [Reactive]
    public partial string Label { get; set; } = L.TestPing;
}

public sealed class SideHeaderRow(string title, PingState? ping = null) : SideRow
{
    public string Title { get; } = title;
    public PingState? Ping { get; } = ping;
    public bool ShowPing => Ping != null;
    public override string ToString() => Title;
}

public sealed class SideHintRow(string text) : SideRow
{
    public string Text { get; } = text;
    public override string ToString() => Text;
}

public sealed class SideServerRow : SideRow
{
    public SideServerRow(ProfileItemModel model, bool showSubscription, bool indent)
    {
        Model = model;
        Code = CountryResolver.Resolve(model.Remarks);
        Flag = V2pnConverters.Flag(Code);
        Title = CountryResolver.StripFlag(model.Remarks);
        var country = WorldMap.Instance.Get(Code)?.Name;
        var protocol = V2pnConverters.ProtocolName.Convert(model.ConfigType, typeof(string), null, CultureInfo.CurrentCulture) as string;
        Subtitle = showSubscription && model.SubRemarks.IsNotEmpty()
            ? model.SubRemarks
            : string.Join(" · ", new[] { country, protocol }.Where(t => t.IsNotEmpty()));
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

    public int BestDelay { get; }
    public string? BestId { get; }

    public string Code { get; }
    public string Name { get; }
    public Bitmap? Flag { get; }
    public bool HasFlag => Flag != null;
    public IReadOnlyList<ProfileItemModel> Servers { get; }
    public bool IsExpanded { get; }
    public bool HasActive { get; }
    public string Info { get; }
    public override string ToString() => Name;
}

public sealed class SideSubRow(SubItemModel sub, string info) : SideRow
{
    public SubItemModel Sub { get; } = sub;
    public string Info { get; } = info;
    public override string ToString() => Sub.Remarks;
}

public sealed class SideActionRow(SideActionKind kind, string title, string subtitle, string? targetId = null) : SideRow
{
    public SideActionKind Kind { get; } = kind;
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public string? TargetId { get; } = targetId;
    public bool IsFastest => Kind == SideActionKind.Fastest;
    public override string ToString() => Title;
    public bool IsBack => Kind == SideActionKind.Back;
    public bool IsAdd => Kind == SideActionKind.AddSubscription;
    public bool IsRefresh => Kind == SideActionKind.UpdateAll;
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
                Ping.Label = t.testing ? string.Format(L.PingProgress, t.done, t.total) : L.TestPing;
            });

        this.WhenAnyValue(x => x.Tab, x => x.Search, x => x.OpenedSub)
            .Subscribe(_ => ApplyNavigation());
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
            rows.Add(new SideHeaderRow($"{L.SearchResults} ({found.Count})", found.Count > 0 ? Ping : null));
            rows.AddRange(ByPing(found).Take(400).Select(m => new SideServerRow(m, true, false)));
            if (found.Count == 0)
            {
                rows.Add(new SideHintRow(L.NothingFound));
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
    }

    private void BuildFavorites(List<SideRow> rows, List<ProfileItemModel> all)
    {
        var fastest = all.Concat(_profiles.FavoriteItems).Where(m => m.Delay > 0).MinBy(m => m.Delay);
        rows.Add(fastest != null
            ? new SideActionRow(SideActionKind.Fastest, L.Fastest, $"{CountryResolver.StripFlag(fastest.Remarks)} · {L.Ms(fastest.Delay)}", fastest.IndexId)
            : new SideActionRow(SideActionKind.Fastest, L.Fastest, L.FastestNone));

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
        rows.Add(new SideHeaderRow($"{L.TabFavorites} ({favorites.Count})", favorites.Count > 0 ? Ping : null));
        rows.AddRange(favorites.Select(m => new SideServerRow(m, true, false)));
        if (favorites.Count == 0)
        {
            rows.Add(new SideHintRow(L.FavoritesEmpty));
        }
    }

    private void BuildCountries(List<SideRow> rows, List<ProfileItemModel> all)
    {
        var groups = all
            .GroupBy(m => CountryResolver.Resolve(m.Remarks) ?? string.Empty)
            .Select(g => (Code: g.Key, Name: CountryName(g.Key), Items: ByPing(g).ToList()))
            .OrderBy(g => g.Code.IsNullOrEmpty() ? 1 : 0)
            .ThenBy(g => g.Name, StringComparer.CurrentCulture)
            .ToList();

        _pingScope = all;
        rows.Add(new SideHeaderRow($"{L.TabCountries} ({groups.Count(g => g.Code.IsNotEmpty())})", all.Count > 0 ? Ping : null));
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
            rows.Add(new SideHintRow(L.SubEmpty));
        }
    }

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
            rows.Add(new SideActionRow(SideActionKind.Back, OpenedSub.Remarks, $"{L.ServerCount(all.Count)} · {Fmt.SubUpdated(OpenedSub.Item)}", OpenedSub.Id));
            if (all.Count > 0)
            {
                rows.Add(new SideHeaderRow(L.TabServers, Ping));
            }
            rows.AddRange(ByPing(all).Select(m => new SideServerRow(m, false, false)));
            if (all.Count == 0)
            {
                rows.Add(new SideHintRow(L.SubEmpty));
            }
            return;
        }

        _pingScope = [];
        rows.Add(new SideHeaderRow($"{L.TabSubscriptions} ({_profiles.SubNavItems.Count})"));
        foreach (var sub in _profiles.SubNavItems)
        {
            rows.Add(new SideSubRow(sub, $"{L.ServerCount(sub.Count)} · {Fmt.SubUpdated(sub.Item)}"));
        }
        rows.Add(new SideActionRow(SideActionKind.AddSubscription, L.AddSubscription, string.Empty));
        if (_profiles.SubNavItems.Count > 0)
        {
            rows.Add(new SideActionRow(SideActionKind.UpdateAll, L.UpdateAll, string.Empty));
        }
    }
}
