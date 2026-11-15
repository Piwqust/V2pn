using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// How the person tuned the open look on the Appearance page: light or dark, the accent, the corners and the type.
/// A scene is built for one of these and rebuilt when it changes.
/// </summary>
public sealed record SceneLook(bool Dark, string Accent, string Corners, string DesignedCorners, string Font)
{
    public FontFamily Text => Font switch
    {
        "geistmono" => Scene.Mono,
        "inter" => Scene.Inter,
        "segoe" => new FontFamily("Segoe UI Variable Text, Segoe UI, avares://V2pn/Assets/Fonts#Inter"),
        _ => Scene.Geist,
    };

    private static double Factor(string corners) => corners switch
    {
        "square" => 0.12,
        "sharp" => 0.35,
        "round" => 1.45,
        _ => 1.0,
    };

    /// <summary>A radius as the look draws it, scaled by how much rounder or squarer the person asked for.</summary>
    public double Radius(double designed)
    {
        var ratio = Factor(Corners) / Factor(DesignedCorners);
        if (designed >= 99)
        {
            return ratio >= 0.6 ? 999 : Math.Round(Math.Clamp(24 * ratio, 2, 24));
        }
        return Math.Round(Math.Clamp(designed * ratio, ratio < 0.2 ? 0 : 1, 30));
    }

    public CornerRadius R(double designed) => new(Radius(designed));
}

/// <summary>The colours and shapes one look gives the shared parts (the server list, the empty state).</summary>
public sealed record ScenePalette(
    string Ground,
    string Raised,
    string Ink,
    string Soft,
    string Muted,
    string Accent,
    string OnAccent,
    double Radius,
    FontFamily Font,
    bool Ascii = false,
    string Good = "#5BD08F",
    string Warn = "#E9B04F",
    string Bad = "#E5684F")
{
    /// <summary>The accent where it is text on this look's ground: readable at 4.5:1, never the raw brand colour.</summary>
    public string AccentText => Scene.Readable(Accent, Ground);
}

/// <summary>
/// Every server V2pn has, as the plain sidebar lists them: search, a chip per subscription and favourites, ping,
/// update and "+". Built for any width; it fills the height it gets and scrolls inside it.
/// </summary>
public sealed class ServerBrowser : UserControl
{
    private const int MaxRows = 150;
    private const string Favourites = "★";
    private readonly ScenePalette _p;
    private readonly IEditionActions _a;
    private readonly TextBox _search;
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly TextBlock _note;
    private readonly ScrollViewer _list;
    private const string PartnerChip = "@partner";
    private string? _sub;
    // Until the person picks a chip, a partner's look opens on the partner's own servers.
    private bool _picked;
    private EditionSnapshot? _last;
    private int _rowsKey;
    private int _chipsKey;

    public ServerBrowser(ScenePalette p, IEditionActions a)
    {
        _p = p;
        _a = a;
        _search = new TextBox
        {
            Watermark = p.Ascii ? L.SceneSearchAscii : L.SceneSearch,
            FontFamily = p.Font,
            FontSize = 13,
            Foreground = Scene.B(p.Ink),
            Background = Scene.B(p.Raised),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(Math.Min(p.Radius, 12)),
            Padding = new Thickness(12, 0),
            MinHeight = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _search.TextChanged += (_, _) => Render();

        var ping = Tool(p.Ascii ? L.ScenePingAscii : L.ScenePing, L.ScenePingTip);
        ping.Click += (_, _) => _a.PingAll();
        var update = Tool(p.Ascii ? "[↻]" : "↻", L.SceneUpdateTip);
        update.Click += (_, _) => _a.UpdateSubscriptions();
        var add = Tool(p.Ascii ? "[+]" : "+", L.SceneAddTip);
        add.Click += (_, _) => _a.Add(add);
        var tools = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), ColumnSpacing = 6 };
        Put(tools, _search, 0, 0);
        Put(tools, ping, 0, 1);
        Put(tools, update, 0, 2);
        Put(tools, add, 0, 3);

        _note = Scene.Text("", 12, p.Muted, p.Font);
        _note.TextWrapping = TextWrapping.Wrap;
        _note.TextTrimming = TextTrimming.None;
        _note.Margin = new Thickness(6, 8);
        var chips = new ScrollViewer
        {
            Content = _chips,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _list = new ScrollViewer
        {
            Content = new StackPanel { Children = { _rows, _note } },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var root = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 10 };
        Put(root, tools, 0, 0);
        Put(root, chips, 1, 0);
        Put(root, _list, 2, 0);
        Content = root;
    }

    public void Update(EditionSnapshot s)
    {
        _last = s;
        Render();
    }

    private Button Tool(string label, string tip)
    {
        var text = Scene.Text(label, 13, _p.Soft, _p.Font, FontWeight.Medium);
        var button = Scene.Button(text, _p.Raised, _p.Soft, Math.Min(_p.Radius, 12), new Thickness(12, 0), height: 39);
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }

    private void Render()
    {
        if (_last is not { } s)
        {
            return;
        }
        RenderChips(s);

        var query = _search.Text?.Trim() ?? string.Empty;
        IEnumerable<SceneServer> items = s.Catalog;
        if (_sub == PartnerChip)
        {
            items = items.Where(x => x.SubId != null && s.PartnerSubIds.Contains(x.SubId));
        }
        else if (_sub == Favourites)
        {
            items = items.Where(x => x.Favorite);
        }
        else if (_sub != null)
        {
            items = items.Where(x => x.SubId == _sub);
        }
        if (query.Length > 0)
        {
            items = items.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (x.Sub?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        var list = items.ToList();

        var key = new HashCode();
        key.Add(query);
        key.Add(_sub);
        key.Add(s.SelectedId);
        foreach (var x in list.Take(MaxRows))
        {
            key.Add(x.IndexId);
            key.Add(x.Delay);
            key.Add(x.Group);
            key.Add(x.Detail);
        }
        key.Add(list.Count);
        var hash = key.ToHashCode();
        if (hash == _rowsKey)
        {
            return;
        }
        _rowsKey = hash;

        _rows.Children.Clear();
        var showSub = _sub is null or Favourites || (_sub == PartnerChip && s.PartnerSubIds.Count > 1);
        // The partner's own servers by its groups (pack or API), in the order the list has them; the rest under "Other".
        var grouped = _sub != null && _sub != Favourites && query.Length == 0 && list.Any(x => x.Group != null);
        var rows = grouped
            ? list.Take(MaxRows).GroupBy(x => x.Group, StringComparer.CurrentCultureIgnoreCase).OrderBy(g => g.Key == null ? 1 : 0).SelectMany(g => g.Select((x, i) => (Server: x, Header: i == 0 ? g.Key ?? L.GroupOther : null)))
            : list.Take(MaxRows).Select(x => (Server: x, Header: (string?)null));
        foreach (var (server, header) in rows)
        {
            if (header != null)
            {
                var title = Scene.Text(_p.Ascii ? header.ToLower(CultureInfo.CurrentUICulture) : header, 12, _p.Muted, _p.Font, FontWeight.Medium);
                title.Margin = new Thickness(10, _rows.Children.Count == 0 ? 2 : 12, 0, 4);
                _rows.Children.Add(title);
            }
            _rows.Children.Add(Row(server, server.IndexId == s.SelectedId, showSub));
        }
        _note.Text = list.Count == 0
            ? _sub == Favourites && query.Length == 0 ? L.SceneNoFavourites : L.SceneNothingFound
            : list.Count > MaxRows ? string.Format(L.SceneFirstRows, MaxRows, list.Count) : string.Empty;
        _note.IsVisible = _note.Text.Length > 0;
    }

    private void RenderChips(EditionSnapshot s)
    {
        var favourites = s.Catalog.Count(x => x.Favorite);
        var partnerCount = s.Catalog.Count(x => x.SubId != null && s.PartnerSubIds.Contains(x.SubId));
        if (!_picked)
        {
            _sub = partnerCount > 0 ? PartnerChip : null;
        }
        if (_sub == PartnerChip ? partnerCount == 0 : _sub != null && _sub != Favourites && s.Subs.All(x => x.Id != _sub))
        {
            _sub = null;
        }
        var key = HashCode.Combine(_sub, favourites, partnerCount, s.Catalog.Count, string.Join("|", s.Subs.Select(x => $"{x.Id}:{x.Name}:{x.Count}")));
        if (key == _chipsKey)
        {
            return;
        }
        _chipsKey = key;
        _chips.Children.Clear();
        if (partnerCount > 0 && s.PartnerName != null)
        {
            _chips.Children.Add(Chip(PartnerChip, _p.Ascii ? $"{s.PartnerName.ToLowerInvariant()} {partnerCount}" : $"{s.PartnerName}  {partnerCount}"));
        }
        _chips.Children.Add(Chip(null, _p.Ascii ? $"{L.SceneAllAscii} {s.Catalog.Count}" : $"{L.SceneAll}  {s.Catalog.Count}"));
        if (favourites > 0)
        {
            _chips.Children.Add(Chip(Favourites, _p.Ascii ? $"* {L.SceneFavouritesAscii} {favourites}" : $"★ {L.SceneFavourites}  {favourites}"));
        }
        // One subscription is the same list as "All".
        if (s.Subs.Count > 1)
        {
            foreach (var sub in s.Subs)
            {
                _chips.Children.Add(Chip(sub.Id, $"{sub.Name}  {sub.Count}"));
            }
        }
        _chips.IsVisible = _chips.Children.Count > 1;
    }

    private Button Chip(string? id, string label)
    {
        var on = _sub == id;
        var text = Scene.Text(_p.Ascii ? (on ? $"[{label}]" : $" {label} ") : label, 12, on ? (_p.Ascii ? _p.AccentText : _p.Ground) : _p.Muted, _p.Font, FontWeight.Medium);
        var fill = on && !_p.Ascii ? _p.Ink : _p.Ascii ? null : _p.Raised;
        var chip = Scene.Button(text, fill, _p.Muted, _p.Ascii ? 2 : 999, new Thickness(_p.Ascii ? 2 : 12, 6));
        chip.Click += (_, _) =>
        {
            _sub = id;
            _picked = true;
            _chipsKey = 0;
            Render();
            _list.Offset = default;
        };
        return chip;
    }

    private Button Row(SceneServer server, bool selected, bool showSub)
    {
        var grid = new Grid { ColumnDefinitions = new(_p.Ascii ? "14,*,Auto" : "24,*,Auto"), ColumnSpacing = 10 };
        if (_p.Ascii)
        {
            grid.Children.Add(Scene.Text(selected ? ">" : "", 12, _p.AccentText, _p.Font));
        }
        else if (v2rayN.Desktop.Converters.V2pnConverters.Flag(server.Code) != null)
        {
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                ClipToBounds = true,
                Width = 22,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Child = Scene.Flag(server.Code, 22, 16),
            });
        }
        else
        {
            // No country known (a group, a server named by its host): a globe, as in the plain list.
            grid.Children.Add(new PathIcon
            {
                Width = 15,
                Height = 15,
                Data = Application.Current?.FindResource("SemiIconGlobeStroke") as Geometry,
                Foreground = Scene.B(_p.Muted),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
        var name = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(Scene.Text(server.Name, 13, selected ? _p.Ink : _p.Soft, _p.Font));
        var detail = string.Join(" · ", new[] { showSub ? server.Sub : null, server.Detail }.Where(t => t.IsNotEmpty()));
        if (detail.Length > 0)
        {
            name.Children.Add(Scene.Text(detail, 11, _p.Muted, _p.Font));
        }
        Put(grid, name, 0, 1);
        var (ping, color) = server.Delay switch
        {
            > 0 and < 150 => ($"{server.Delay} {L.SceneMs}", _p.Good),
            > 0 => ($"{server.Delay} {L.SceneMs}", _p.Warn),
            < 0 => (L.SceneNo, _p.Bad),
            _ => ("—", _p.Muted),
        };
        Put(grid, Scene.Text(ping, 12, color, server.Delay > 0 ? Scene.Mono : _p.Font), 0, 2);

        var accentLine = selected ? "#59" + _p.Accent.TrimStart('#') : null;
        var button = Scene.Button(grid, selected ? _p.Raised : null, _p.Soft, Math.Min(_p.Radius, 12), new Thickness(10, 7), accentLine);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var id = server.IndexId;
        button.Click += (_, _) => _a.Select(id);
        Avalonia.Automation.AutomationProperties.SetName(button, server.Delay < 0 ? $"{server.Name}, {L.SceneNoAnswer}" : $"{server.Name}, {ping}");
        return button;
    }

    internal static void Put(Grid grid, Control child, int row, int column, int columnSpan = 1)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        Grid.SetColumnSpan(child, columnSpan);
        grid.Children.Add(child);
    }
}

/// <summary>
/// The server in use belongs to another provider: said in the look's words, with a way to the partner's best server.
/// The state pill keeps telling the truth either way.
/// </summary>
public sealed class ForeignNote : UserControl
{
    private readonly TextBlock _text;
    private readonly Button _switch;
    private string? _target;

    public ForeignNote(ScenePalette p, IEditionActions a)
    {
        _text = Scene.Text("", 12.5, p.Soft, p.Font);
        _text.TextWrapping = TextWrapping.Wrap;
        _text.TextTrimming = TextTrimming.None;
        var label = Scene.Text("", 12.5, p.AccentText, p.Font, FontWeight.Medium);
        _switch = Scene.Button(label, null, p.AccentText, 6, new Thickness(0, 2));
        _switch.HorizontalAlignment = HorizontalAlignment.Left;
        _switch.Click += (_, _) =>
        {
            if (_target != null)
            {
                a.Select(_target);
            }
        };
        Content = new StackPanel { Spacing = 2, Children = { _text, _switch } };
        IsVisible = false;
        Tag = (p, label);
    }

    public void Update(EditionSnapshot s)
    {
        IsVisible = s.ForeignServer && s.PartnerName != null;
        if (!IsVisible)
        {
            return;
        }
        _target = s.BestPartnerServer;
        var (p, label) = ((ScenePalette, TextBlock))Tag!;
        _text.Text = p.Ascii
            ? string.Format(L.SceneForeignAscii, s.PartnerName, s.Subscription)
            : string.Format(L.SceneForeign, s.PartnerName) + (s.Subscription.IsNotEmpty() ? string.Format(L.SceneForeignFrom, s.Subscription) : ".");
        label.Text = p.Ascii ? string.Format(L.SceneToPartnerAscii, s.PartnerName) : string.Format(L.SceneToPartner, s.PartnerName) + " \u2192";
        _switch.IsVisible = _target != null;
    }
}

/// <summary>Before the first server: what to do instead of an empty list, in the look's own colours.</summary>
public sealed class EmptyState : UserControl
{
    private readonly Button _import;

    public EmptyState(ScenePalette p, IEditionActions a, string partnerName)
    {
        var title = Scene.Text(p.Ascii ? L.SceneEmptyTitleAscii : L.SceneEmptyTitle, p.Ascii ? 15 : 20, p.Ink, p.Font);
        var lead = Scene.Text(string.Format(L.SceneEmptyLead, partnerName), 13, p.Muted, p.Font);
        lead.TextWrapping = TextWrapping.Wrap;
        lead.TextTrimming = TextTrimming.None;

        var paste = Action(p, p.Ascii ? L.ScenePasteAscii : L.ScenePaste, primary: true);
        paste.Click += (_, _) => a.Paste();
        var sub = Action(p, p.Ascii ? L.SceneAddSubAscii : L.SceneAddSub, primary: false);
        sub.Click += (_, _) => a.AddSubscription();
        _import = Action(p, p.Ascii ? L.SceneImportAscii : L.SceneImport, primary: false);
        _import.Click += (_, _) => a.Import();
        _import.IsVisible = false;

        Content = new StackPanel
        {
            Spacing = 10,
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { title, lead, new Border { Height = 4 }, paste, sub, _import },
        };
    }

    public void Update(EditionSnapshot s) => _import.IsVisible = s.CanImport;

    private static Button Action(ScenePalette p, string label, bool primary)
    {
        var text = Scene.Text(label, 13.5, primary && !p.Ascii ? p.OnAccent : p.Soft, p.Font, FontWeight.Medium);
        var button = Scene.Button(text, primary && !p.Ascii ? p.Accent : p.Raised, p.Soft, Math.Min(p.Radius, 14), new Thickness(16, 0), height: 44);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        return button;
    }
}
