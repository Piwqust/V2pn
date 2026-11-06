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

public sealed class SideHeaderRow(string title) : SideRow
{
    public string Title { get; } = title;
}

public sealed class SideHintRow(string text) : SideRow
{
    public string Text { get; } = text;
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
    }

    public ProfileItemModel Model { get; }
    public string? Code { get; }
    public Bitmap? Flag { get; }
    public bool HasFlag => Flag != null;
    public string Title { get; }
    public string Subtitle { get; }
    public bool IsActive => Model.IsActive;
    public bool Indent { get; }
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
        var best = servers.Where(s => s.Delay > 0).Select(s => s.Delay).DefaultIfEmpty(0).Min();
        Info = best > 0 ? $"{servers.Count} · {best} ms" : $"{servers.Count}";
    }

    public string Code { get; }
    public string Name { get; }
    public Bitmap? Flag { get; }
    public bool HasFlag => Flag != null;
    public IReadOnlyList<ProfileItemModel> Servers { get; }
    public bool IsExpanded { get; }
    public bool HasActive { get; }
    public string Info { get; }
}

public sealed class SideSubRow(SubItemModel sub, string info) : SideRow
{
    public SubItemModel Sub { get; } = sub;
    public string Info { get; } = info;
}

public sealed class SideActionRow(SideActionKind kind, string title, string subtitle, string? targetId = null) : SideRow
{
    public SideActionKind Kind { get; } = kind;
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public string? TargetId { get; } = targetId;
    public bool IsFastest => Kind == SideActionKind.Fastest;
    public bool IsBack => Kind == SideActionKind.Back;
    public bool IsPlus => Kind is SideActionKind.AddSubscription or SideActionKind.UpdateAll;
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

        this.WhenAnyValue(x => x.Tab, x => x.Search, x => x.OpenedSub)
            .Subscribe(_ => ApplyNavigation());
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
            rows.Add(new SideHeaderRow($"{L.SearchResults} ({found.Count})"));
            rows.AddRange(found.Take(400).Select(m => new SideServerRow(m, true, false)));
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
            ? new SideActionRow(SideActionKind.Fastest, L.Fastest, $"{CountryResolver.StripFlag(fastest.Remarks)} · {fastest.Delay} ms", fastest.IndexId)
            : new SideActionRow(SideActionKind.Fastest, L.Fastest, L.FastestNone));

        var favorites = _profiles.FavoriteItems.ToList();
        rows.Add(new SideHeaderRow($"{L.TabFavorites} ({favorites.Count})"));
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
            .Select(g => (Code: g.Key, Name: CountryName(g.Key), Items: g.ToList()))
            .OrderBy(g => g.Code.IsNullOrEmpty() ? 1 : 0)
            .ThenBy(g => g.Name, StringComparer.CurrentCulture)
            .ToList();

        rows.Add(new SideHeaderRow($"{L.TabCountries} ({groups.Count(g => g.Code.IsNotEmpty())})"));
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
            rows.Add(new SideActionRow(SideActionKind.Back, OpenedSub.Remarks, $"{all.Count} {L.ServersCount} · {Fmt.SubUpdated(OpenedSub.Item)}"));
            rows.AddRange(all.Select(m => new SideServerRow(m, false, false)));
            if (all.Count == 0)
            {
                rows.Add(new SideHintRow(L.SubEmpty));
            }
            return;
        }

        rows.Add(new SideHeaderRow($"{L.TabSubscriptions} ({_profiles.SubNavItems.Count})"));
        foreach (var sub in _profiles.SubNavItems)
        {
            rows.Add(new SideSubRow(sub, $"{sub.Count} {L.ServersCount} · {Fmt.SubUpdated(sub.Item)}"));
        }
        rows.Add(new SideActionRow(SideActionKind.AddSubscription, L.AddSubscription, string.Empty));
        if (_profiles.SubNavItems.Count > 0)
        {
            rows.Add(new SideActionRow(SideActionKind.UpdateAll, L.UpdateAll, string.Empty));
        }
    }
}
