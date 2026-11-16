using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using v2rayN.Desktop.Converters;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// Neon: one column like a phone VPN app (INCY, Happ), one idea per part. At the top the provider and Proxy/VPN; in the
/// middle Connect alone (a round button in a glowing ring of the brand colours, or a row with a switch), the place and
/// the state under it and what goes through the VPN; at the bottom a sheet with the plan, the traffic, the provider's
/// offer and one list: the provider's buttons first, then its servers. The mini window (380×640) is the look as
/// designed; a wider one puts the sheet beside Connect. Colours come from the theme (V2pnThemePalette); the state
/// colours are V2pn's.
/// </summary>
public sealed class NeonScene : EditionScene
{
    private readonly V2pnTheme _t;
    private readonly V2pnThemeColors _c;
    private readonly FontFamily _font = Scene.Geist;

    private readonly Control _brand;
    private readonly ThemeSegment _mode;
    private readonly NeonConnect _connect;
    private readonly Image _flag = Scene.Flag(null, 24, 17);
    private readonly Border _flagBox;
    private readonly TextBlock _title;
    private readonly Ellipse _stateDot = new() { Width = 7, Height = 7 };
    private readonly TextBlock _stateText;
    private readonly TextBlock _pingText;
    private readonly Button _route;
    private readonly TextBlock _routeText;
    private readonly StackPanel _place;

    private readonly Button _planRow;
    private readonly TextBlock _plan;
    private readonly TextBlock _days;
    private readonly Border _track;
    private readonly Border _bar;
    private readonly TextBlock _traffic;
    private readonly TextBlock _devices;
    private readonly StackPanel _account;
    private double _fraction;
    private readonly Border _offer;
    private readonly TextBlock _offerText;
    private readonly Button _offerButton;
    private readonly TextBlock _offerButtonText;
    private readonly ForeignNote _foreign;
    private readonly TextBlock _listCount;
    private readonly Button _support;
    private readonly StackPanel _rows = new();
    private readonly ContentControl _listBody = new();
    private readonly ScrollViewer _list;
    private readonly EmptyState _empty;
    private readonly Border _sheet;
    private string _rowsKey = string.Empty;

    private readonly ContentControl _host = new();
    private EditionSnapshot? _last;

    public NeonScene(V2pnPartner partner, IEditionActions actions, SceneLook look, V2pnTheme theme, V2pnThemeColors colours) : base(partner, actions, look)
    {
        _t = theme;
        _c = colours;

        var logo = Scene.Picture(partner, _c.Dark ? partner.Pack.Logo : partner.Pack.LogoOnLight ?? partner.Pack.Logo);
        _brand = logo != null
            ? new Image { Source = logo, Height = 22, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center }
            : new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { Mark(partner.Name, 26), Scene.Text(partner.Name, 15, _c.Ink, _font, FontWeight.SemiBold) },
            };
        _mode = new ThemeSegment(_c.Raised, _c.Accent, _c.OnAccent, _c.Soft, _font, _ => new CornerRadius(999), actions, 32);
        _connect = new NeonConnect(theme.Connect, _c, actions, _font);
        _connect.RowClicked += anchor => ShowServers(anchor, _last);

        // The place and the state under Connect: big name, then a coloured dot with the state and the ping.
        _flagBox = new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Width = 24, Height = 17, Child = _flag, VerticalAlignment = VerticalAlignment.Center };
        _title = Scene.Text("", 21, _c.Ink, _font, FontWeight.SemiBold);
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.MaxWidth = 280;
        _stateText = Scene.Text("", 12.5, _c.Soft, _font, FontWeight.Medium);
        _pingText = Scene.Text("", 12.5, _c.Muted, _font);
        _stateDot.VerticalAlignment = VerticalAlignment.Center;
        _routeText = Scene.Text("", 12.5, _c.Ink, _font, FontWeight.Medium);
        _routeText.TextTrimming = TextTrimming.CharacterEllipsis;
        _routeText.MaxWidth = 230;
        _route = Scene.Button(
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { Scene.Text("⇄", 13, _c.AccentText, _font, FontWeight.SemiBold), _routeText, Scene.Text("▾", 10, _c.Muted, _font) },
            },
            _c.Raised, _c.Ink, 999, new Thickness(14, 0), height: 32);
        _route.HorizontalAlignment = HorizontalAlignment.Center;
        _route.Click += (_, _) => Actions.OpenRoute(_route);
        ToolTip.SetTip(_route, L.SceneRouteHint);
        _place = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Children = { _flagBox, _title } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center, Children = { _stateDot, _stateText, _pingText } },
            },
        };

        // The sheet: the plan and its days, the traffic as a thin line, the provider's offer, one list.
        _plan = Scene.Text(partner.Name, 15, _c.Ink, _font, FontWeight.SemiBold);
        _plan.TextTrimming = TextTrimming.CharacterEllipsis;
        _days = Scene.Text("", 15, _c.AccentText, _font, FontWeight.SemiBold);
        var planGrid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        planGrid.Children.Add(_plan);
        planGrid.Children.Add(_days.Also(d => Grid.SetColumn(d, 1)));
        _planRow = Scene.Button(planGrid, null, _c.Ink, 8, default);
        _planRow.HorizontalAlignment = HorizontalAlignment.Stretch;
        _planRow.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _planRow.Click += (_, _) => Open(_last?.Account?.CabinetUrl);
        _bar = new Border { CornerRadius = new CornerRadius(2), Background = Scene.B(_c.Accent), HorizontalAlignment = HorizontalAlignment.Left };
        _track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Scene.B(_c.Raised), ClipToBounds = true, Child = _bar };
        _track.SizeChanged += (_, _) => SizeBar();
        _traffic = Scene.Text("", 11.5, _c.Muted, _font);
        _devices = Scene.Text("", 11.5, _c.Muted, _font);
        _devices.HorizontalAlignment = HorizontalAlignment.Right;
        var under = new Grid { ColumnDefinitions = new("*,Auto") };
        under.Children.Add(_traffic);
        under.Children.Add(_devices.Also(d => Grid.SetColumn(d, 1)));
        _account = new StackPanel { Spacing = 8, Children = { _planRow, _track, under } };

        _offerText = Scene.Text("", 12.5, _c.Ink, _font, FontWeight.Medium);
        _offerText.TextWrapping = TextWrapping.Wrap;
        _offerText.VerticalAlignment = VerticalAlignment.Center;
        _offerButtonText = Scene.Text("", 12, _c.OnAccent, _font, FontWeight.SemiBold);
        _offerButton = Scene.Button(_offerButtonText, _c.Accent, _c.OnAccent, 999, new Thickness(12, 0), height: 28);
        _offerButton.VerticalAlignment = VerticalAlignment.Center;
        _offerButton.Click += (_, _) => Open(_last?.Account?.Banner?.ButtonUrl ?? _last?.Account?.AnnounceUrl);
        var offerGrid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
        offerGrid.Children.Add(_offerText);
        offerGrid.Children.Add(_offerButton.Also(b => Grid.SetColumn(b, 1)));
        _offer = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 10, 10, 10),
            Background = Scene.B(V2pnColor.Parse(_c.Accent).Over(V2pnColor.Parse(_c.Surface), _c.Dark ? 0.16 : 0.10).Hex),
            Child = offerGrid,
        };

        var palette = new ScenePalette(_c.Surface, _c.Raised, _c.Ink, _c.Soft, _c.Muted, _c.Accent, _c.OnAccent, 12, _font, Good: _c.Good, Warn: _c.Warn, Bad: _c.Bad);
        _foreign = new ForeignNote(palette, actions);
        _empty = new EmptyState(palette, actions, partner.Name);

        _listCount = Scene.Text("", 12, _c.Muted, _font);
        _support = IconButton(Icon("SemiIconHelpCircleStroked"), L.SceneSupport, b => Open(_last?.Account?.SupportUrl));
        var listHead = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8 };
        listHead.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Scene.Text(L.SceneServersTitle, 12.5, _c.Soft, _font, FontWeight.SemiBold), _listCount },
        });
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children =
            {
                IconButton(Icon("SemiIconPulse"), L.TestPingAll, _ => Actions.PingAll()),
                IconButton(Icon("SemiIconRefresh"), L.UpdateAll, _ => Actions.UpdateSubscriptions()),
                IconButton(Icon("SemiIconPlus"), L.SceneAddTip, b => Actions.Add(b)),
                _support,
            },
        };
        Grid.SetColumn(tools, 2);
        listHead.Children.Add(tools);

        _list = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Margin = new Thickness(-6, 0) };
        _listBody.Content = _list;

        var sheetGrid = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*"), RowSpacing = 12, Margin = new Thickness(18, 16, 18, 6) };
        ServerBrowser.Put(sheetGrid, _account, 0, 0);
        ServerBrowser.Put(sheetGrid, _offer, 1, 0);
        ServerBrowser.Put(sheetGrid, _foreign, 2, 0);
        ServerBrowser.Put(sheetGrid, listHead.Also(h => h.Margin = new Thickness(0, 0, -6, -6)), 3, 0);
        ServerBrowser.Put(sheetGrid, _listBody, 4, 0);
        _sheet = new Border { Background = Scene.B(_c.Surface), ClipToBounds = true, Child = sheetGrid };

        Content = new ThemeVariantScope { RequestedThemeVariant = _c.Dark ? ThemeVariant.Dark : ThemeVariant.Light, Child = _host };
        Reflow(Fit.Narrow);
    }

    private Border Mark(string name, double size) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.3),
        Background = Scene.B(_c.Accent),
        Child = Scene.Text(name[..1].ToUpperInvariant(), size * 0.48, _c.OnAccent, _font, FontWeight.Bold).Also(t =>
        {
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
        }),
    };

    private static Geometry? Icon(string key) => Application.Current?.FindResource(key) as Geometry;

    private Button IconButton(Geometry? glyph, string tip, Action<Button> click)
    {
        Control content = glyph != null
            ? new PathIcon { Width = 15, Height = 15, Data = glyph, Foreground = Scene.B(_c.Soft) }
            : Scene.Text("•", 14, _c.Soft, _font);
        var button = Scene.Button(content, null, _c.Soft, 999, default, height: 32);
        button.Width = 32;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click(button);
        return button;
    }

    private void Open(string? url)
    {
        if (url.IsNotEmpty())
        {
            Actions.OpenUrl(url!);
        }
    }

    private void SizeBar() => _bar.Width = Math.Max(_fraction > 0 ? 6 : 0, _track.Bounds.Width * _fraction);

    /// <summary>The ground: opaque (the plain window lies under the scene), with the accent's glow where Connect is.</summary>
    private IBrush Ground(double x, double y, double radius) => new RadialGradientBrush
    {
        Center = new RelativePoint(x, y, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(radius, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(radius * 0.62, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(V2pnColor.Parse(_c.Accent).Over(V2pnColor.Parse(_c.Ground), _c.Dark ? 0.30 : 0.18).Hex), 0),
            new GradientStop(Color.Parse(V2pnColor.Parse(_c.Accent).Over(V2pnColor.Parse(_c.Ground), _c.Dark ? 0.08 : 0.05).Hex), 0.45),
            new GradientStop(Color.Parse(_c.Ground), 1),
        },
    };

    private Grid Head(double side)
    {
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Margin = new Thickness(side, 10, side, 0) };
        Put(row, _brand, 0, 0);
        _mode.VerticalAlignment = VerticalAlignment.Center;
        Put(row, _mode, 0, 1);
        return row;
    }

    /// <summary>Connect with the place, the state and the route under it.</summary>
    private StackPanel Hero(double scale, bool compact = false)
    {
        _connect.Resize(scale, compact);
        var hero = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch };
        hero.Children.Add(Take(_connect).Also(c => c.Margin = _t.Connect == V2pnThemeConnect.Toggle ? new Thickness(16, 0) : default));
        if (_t.Connect == V2pnThemeConnect.Round)
        {
            hero.Children.Add(Take(_place));
        }
        hero.Children.Add(Take(_route));
        return hero;
    }

    protected override void Reflow(Fit fit)
    {
        var round = _t.Connect == V2pnThemeConnect.Round;
        if (fit == Fit.Narrow)
        {
            // The phone: Connect in the upper half, the sheet rising from the bottom edge.
            _sheet.CornerRadius = new CornerRadius(26, 26, 0, 0);
            _sheet.Margin = default;
            var page = new Grid { RowDefinitions = new("Auto,Auto,*"), Background = Ground(0.5, round ? 0.24 : 0.15, 0.75) };
            ServerBrowser.Put(page, Head(18), 0, 0);
            var hero = Hero(0.72, compact: true);
            hero.Spacing = 10;
            hero.Margin = new Thickness(0, round ? 4 : 18, 0, 16);
            ServerBrowser.Put(page, hero, 1, 0);
            Put(page, _sheet, 2, 0);
            _host.Content = page;
        }
        else
        {
            // Wider: Connect in the middle of its own half, the sheet as a panel beside it.
            _sheet.CornerRadius = new CornerRadius(24);
            _sheet.Margin = new Thickness(0, 12, 12, 12);
            var left = new Grid { RowDefinitions = new("Auto,*") };
            ServerBrowser.Put(left, Head(24), 0, 0);
            var hero = Hero(fit == Fit.Wide ? 1.1 : 1.0);
            hero.VerticalAlignment = VerticalAlignment.Center;
            hero.MaxWidth = 420;
            hero.Margin = new Thickness(0, 0, 0, 40);
            ServerBrowser.Put(left, hero, 1, 0);
            var grid = new Grid { ColumnDefinitions = new(fit == Fit.Wide ? "*,440" : "*,390"), Background = Ground(fit == Fit.Wide ? 0.33 : 0.26, 0.45, 0.5) };
            ServerBrowser.Put(grid, left, 0, 0);
            Put(grid, _sheet, 0, 1);
            _host.Content = grid;
        }
        if (_last != null)
        {
            Update(_last);
        }
    }

    public override void Update(EditionSnapshot s)
    {
        _last = s;
        _connect.Update(s);
        _mode.Update(s.Vpn);
        _flag.Source = V2pnConverters.Flag(s.CountryCode);
        _flagBox.IsVisible = _flag.Source != null;
        _title.Text = s.Title;
        var state = StateColour(s.StateKind);
        _stateDot.Fill = Scene.B(state);
        _stateText.Text = s.StateLabel;
        _stateText.Foreground = Scene.B(s.StateKind == "safe" ? _c.Good : _c.Soft);
        _pingText.Text = s.Ping > 0 ? "· " + string.Format(L.ThemePingValue, s.Ping) : string.Empty;
        _routeText.Text = s.Route;

        var account = s.Account;
        _account.IsVisible = account != null;
        _plan.Text = account?.Plan ?? Partner.Name;
        // The days left in a word or two; a long term goes under the bar instead, so the plan keeps its room.
        _days.Text = account?.DaysLeft is { } days ? $"{days} {L.DaysWord(days)}" : string.Empty;
        _days.Foreground = Scene.B(account?.Bad == true ? _c.Bad : account?.Warn == true ? _c.Warn : _c.AccentText);
        ToolTip.SetTip(_planRow, account?.CabinetUrl != null ? L.SceneCabinet : null);
        _track.IsVisible = account?.ShowBar == true;
        _fraction = account?.ShowBar == true ? Math.Clamp(account.Fraction, 0, 1) : 0;
        _bar.Background = Scene.B(account?.Bad == true ? _c.Bad : account?.Warn == true ? _c.Warn : _c.Accent);
        SizeBar();
        _traffic.Text = account?.Traffic ?? (account?.DaysLeft == null ? account?.Expiry : null) ?? string.Empty;
        _devices.Text = account?.Devices ?? string.Empty;
        ToolTip.SetTip(_days, account?.Expiry);

        // One offer at a time: the provider's incident, else its banner, else the panel's announcement.
        var offer = account?.Incident ?? account?.Banner?.Text ?? account?.Announce;
        _offer.IsVisible = offer.IsNotEmpty();
        _offerText.Text = offer ?? string.Empty;
        _offerText.Foreground = Scene.B(account?.Incident != null ? (account.IncidentSevere ? _c.Bad : _c.Warn) : _c.Ink);
        var buttonText = account?.Incident != null ? null
            : account?.Banner?.ButtonUrl != null ? account.Banner.ButtonText ?? L.SceneRenew
            : account?.AnnounceUrl != null ? L.SceneMore : null;
        _offerButton.IsVisible = buttonText != null;
        _offerButtonText.Text = buttonText ?? string.Empty;

        _foreign.Update(s);
        _support.IsVisible = account?.SupportUrl != null;
        UpdateRows(s);
    }

    private string StateColour(string kind) => kind switch
    {
        "safe" => _c.Good,
        "warn" or "checking" => _c.Warn,
        _ => _c.Bad,
    };

    private string PingColour(int delay) => delay <= 0 ? _c.Muted : delay < 150 ? _c.Good : delay < 400 ? _c.Warn : _c.Bad;

    /// <summary>The provider's buttons as the first rows, then its servers (every server when it has none of its own).</summary>
    private void UpdateRows(EditionSnapshot s)
    {
        if (!s.AnyServers)
        {
            _empty.Update(s);
            _listBody.Content = _empty;
            _listCount.Text = string.Empty;
            return;
        }
        _listBody.Content = _list;
        var own = s.Catalog.Where(x => x.SubId != null && s.PartnerSubIds.Contains(x.SubId)).ToList();
        var servers = (own.Count > 0 ? own : s.Catalog).Take(120).ToList();
        _listCount.Text = servers.Count.ToString(CultureInfo.CurrentCulture);
        var key = s.SelectedId + "|" + s.ActionRunning + "|" + string.Join(",", s.Actions.Select(a => a.Id + a.Label))
            + "|" + string.Join(",", servers.Select(x => x.IndexId + ":" + x.Delay));
        if (key == _rowsKey)
        {
            return;
        }
        _rowsKey = key;
        _rows.Children.Clear();
        foreach (var action in s.Actions)
        {
            var id = action.Id;
            var glyph = PartnerActionIcons.Of(action.Icon);
            Control icon = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(8),
                Background = Scene.B(V2pnColor.Parse(_c.Accent).Over(V2pnColor.Parse(_c.Surface), _c.Dark ? 0.2 : 0.12).Hex),
                Child = glyph != null
                    ? new PathIcon { Width = 13, Height = 13, Data = glyph, Foreground = Scene.B(_c.AccentText) }
                    : Scene.Text("›", 14, _c.AccentText, _font),
            };
            var row = Row(icon, action.Id == s.ActionRunning ? action.Label + " …" : action.Label, null, null, false, b => Actions.RunAction(id, b));
            row.IsEnabled = s.ActionRunning == null || action.Id == s.ActionRunning;
            _rows.Children.Add(row);
        }
        if (s.Actions.Count > 0)
        {
            _rows.Children.Add(new Border { Height = 1, Background = Scene.B(_c.Line), Margin = new Thickness(12, 6) });
        }
        foreach (var server in servers)
        {
            var id = server.IndexId;
            Control flag = new Border
            {
                Width = 26,
                Height = 26,
                Child = new Border
                {
                    Width = 22,
                    Height = 16,
                    CornerRadius = new CornerRadius(3),
                    ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = V2pnConverters.Flag(server.Code) != null ? Scene.Flag(server.Code, 22, 16) : Scene.Globe(_c.Muted, 16).Also(g => g.IsVisible = true),
                },
            };
            var ping = server.Delay > 0 ? string.Format(L.ThemePingValue, server.Delay) : server.Delay < 0 ? L.ThemePingDown : "—";
            _rows.Children.Add(Row(flag, server.Name, server.Detail ?? server.Group, (ping, PingColour(server.Delay)), server.IndexId == s.SelectedId, _ => Actions.Select(id)));
        }
    }

    private Button Row(Control icon, string title, string? detail, (string Text, string Colour)? end, bool selected, Action<Button> click)
    {
        var name = Scene.Text(title, 13.5, _c.Ink, _font, selected ? FontWeight.SemiBold : FontWeight.Medium);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name } };
        if (detail.IsNotEmpty())
        {
            text.Children.Add(Scene.Text(detail!, 11, _c.Muted, _font).Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis));
        }
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 12, MinHeight = 32 };
        grid.Children.Add(icon.Also(i => i.VerticalAlignment = VerticalAlignment.Center));
        grid.Children.Add(text.Also(t => Grid.SetColumn(t, 1)));
        var tail = end is { } e
            ? Scene.Text(e.Text, 12, e.Colour, Scene.Mono, FontWeight.Medium)
            : Scene.Text("›", 16, _c.Muted, _font);
        tail.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(tail.Also(t => Grid.SetColumn(t, 2)));
        var fill = selected ? V2pnColor.Parse(_c.Accent).Over(V2pnColor.Parse(_c.Surface), _c.Dark ? 0.16 : 0.09).Hex : null;
        var button = Scene.Button(grid, fill, _c.Ink, 12, new Thickness(8, 6, 12, 6));
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(button, title);
        button.Click += (_, _) => click(button);
        return button;
    }
}

/// <summary>
/// Neon's Connect. Round: a disc in two rings, the inner one in the brand colours, a soft glow behind, the power sign and
/// what a press does inside. Toggle: a row with the flag, the place, the state and a switch that turns V2pn's green.
/// </summary>
public sealed class NeonConnect : UserControl
{
    private readonly string _style;
    private readonly V2pnThemeColors _c;
    private readonly IEditionActions _actions;
    private readonly Button _button;
    private readonly Border _fill;
    private readonly Ellipse _ring = new() { StrokeThickness = 3, IsHitTestVisible = false };
    private readonly Ellipse _halo = new() { StrokeThickness = 1, IsHitTestVisible = false };
    private readonly Panel _round = new();
    private readonly Avalonia.Controls.Shapes.Path _power;
    private readonly TextBlock _caption;
    private readonly Border _thumb = new();
    private readonly Image _flag = Scene.Flag(null, 34, 24);
    private readonly Border _flagBox;
    private readonly PathIcon _globe;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly Ellipse _dot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The toggle row was clicked away from its switch: the scene shows the servers.</summary>
    public event Action<Control>? RowClicked;

    public NeonConnect(string style, V2pnThemeColors c, IEditionActions actions, FontFamily font)
    {
        _style = style;
        _c = c;
        _actions = actions;
        var accent = V2pnColor.Parse(c.AccentText);
        var second = V2pnColor.Parse(c.Secondary);
        _power = Scene.Power(c.AccentText, 34);
        _power.HorizontalAlignment = HorizontalAlignment.Center;
        _caption = Scene.Text("", 10.5, c.Soft, font, FontWeight.SemiBold);
        _caption.HorizontalAlignment = HorizontalAlignment.Center;
        _caption.LetterSpacing = 1.6;
        _globe = Scene.Globe(c.Soft, 22);
        _flagBox = new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Width = 34, Height = 24, Child = _flag, VerticalAlignment = VerticalAlignment.Center };
        _title = Scene.Text("", 17, c.Ink, font, FontWeight.SemiBold);
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _subtitle = Scene.Text("", 12, c.Soft, font);
        _subtitle.TextTrimming = TextTrimming.CharacterEllipsis;

        if (style == V2pnThemeConnect.Toggle)
        {
            _button = Scene.Button(_thumb, c.Raised, c.Ink, 999, new Thickness(3));
            _fill = (Border)_button.Content!;
            _button.Width = 60;
            _button.Height = 34;
            _button.VerticalAlignment = VerticalAlignment.Center;
            _thumb.Width = _thumb.Height = 28;
            _thumb.CornerRadius = new CornerRadius(14);
            _thumb.VerticalAlignment = VerticalAlignment.Center;
            _thumb.Background = Brushes.White;
            _thumb.BoxShadow = BoxShadows.Parse("0 2 5 0 #40000000");
            _button.Click += (_, _) => _actions.ToggleConnect();
            var grid = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 14 };
            grid.Children.Add(new Panel { VerticalAlignment = VerticalAlignment.Center, Children = { _flagBox, _globe } });
            var text = new StackPanel
            {
                Spacing = 3,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _title, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _dot, _subtitle } } },
            };
            grid.Children.Add(text.Also(t => Grid.SetColumn(t, 1)));
            var place = Scene.Button(grid, null, c.Ink, 12, default);
            place.HorizontalAlignment = HorizontalAlignment.Stretch;
            place.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            place.Click += (_, _) => RowClicked?.Invoke(place);
            ToolTip.SetTip(place, L.SceneChange);
            var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(place);
            row.Children.Add(_button.Also(b => Grid.SetColumn(b, 1)));
            Content = new Border
            {
                Background = Scene.B(c.Surface),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(16, 16),
                BoxShadow = BoxShadows.Parse(c.Dark ? "0 10 30 -12 #80000000" : "0 10 30 -14 #40202040"),
                Child = row,
            };
            return;
        }

        var inner = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { _power, _caption } };
        var top = V2pnColor.Parse(c.Surface).Mix(V2pnColor.White, c.Dark ? 0.06 : 0.0);
        var bottom = V2pnColor.Parse(c.Surface).Mix(V2pnColor.Black, c.Dark ? 0.12 : 0.05);
        _button = Scene.Button(inner, c.Surface, c.Ink, 999, default, border: accent.WithAlpha(c.Dark ? 0.28 : 0.2));
        _fill = (Border)_button.Content!;
        _fill.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse(top.Hex), 0), new GradientStop(Color.Parse(bottom.Hex), 1) },
        };
        _fill.BoxShadow = BoxShadows.Parse(c.Dark ? "0 18 40 -12 #A0000000" : "0 18 36 -16 #50202040");
        _button.Click += (_, _) => _actions.ToggleConnect();
        _ring.Stroke = new ConicGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Angle = 210,
            GradientStops =
            {
                new GradientStop(Color.Parse(accent.Hex), 0),
                new GradientStop(Color.Parse(second.Hex), 0.5),
                new GradientStop(Color.Parse(accent.WithAlpha(0.15)), 0.8),
                new GradientStop(Color.Parse(accent.Hex), 1),
            },
        };
        _halo.Stroke = Scene.B(accent.WithAlpha(c.Dark ? 0.16 : 0.14));
        // The glow is the scene's ground (NeonScene.Ground), so nothing here can clip it.
        foreach (var part in new Control[] { _halo, _ring, _button })
        {
            part.HorizontalAlignment = HorizontalAlignment.Center;
            part.VerticalAlignment = VerticalAlignment.Center;
            _round.Children.Add(part);
        }
        _round.HorizontalAlignment = HorizontalAlignment.Center;
        Content = _round;
        Resize(1);
    }

    /// <summary>
    /// Grows or shrinks the round button with the window (1 is 150 px); <paramref name="compact"/> (the mini window)
    /// drops the outer halo to give the list below its room. The toggle row keeps its size.
    /// </summary>
    public void Resize(double scale, bool compact = false)
    {
        if (_style == V2pnThemeConnect.Toggle)
        {
            return;
        }
        var size = Math.Round(150 * scale);
        _button.Width = _button.Height = size;
        _button.CornerRadius = _fill.CornerRadius = new CornerRadius(size / 2);
        _ring.Width = _ring.Height = size + 24;
        _halo.Width = _halo.Height = size + 54;
        _halo.IsVisible = !compact;
        _round.Width = _round.Height = compact ? size + 28 : size + 54;
        _power.Width = _power.Height = Math.Round(size * 0.26);
        _power.StrokeThickness = Math.Max(2.4, size / 48);
    }

    public void Update(EditionSnapshot s)
    {
        var on = s.ConnectLabel != L.Connect;
        _button.IsEnabled = s.HasServer;
        Avalonia.Automation.AutomationProperties.SetName(_button, s.ConnectLabel);
        ToolTip.SetTip(_button, s.StateLabel);
        var state = s.StateKind switch { "safe" => _c.Good, "warn" or "checking" => _c.Warn, _ => (string?)null };
        if (_style == V2pnThemeConnect.Toggle)
        {
            _flag.Source = V2pnConverters.Flag(s.CountryCode);
            _flagBox.IsVisible = _flag.Source != null;
            _globe.IsVisible = _flag.Source == null;
            _title.Text = s.Title;
            _subtitle.Text = s.Ping > 0 ? $"{s.StateLabel} · {string.Format(L.ThemePingValue, s.Ping)}" : s.StateLabel;
            _dot.Fill = Scene.B(state ?? _c.Bad);
            _fill.Background = Scene.B(on ? state ?? _c.Muted : _c.Line);
            _thumb.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            return;
        }
        _caption.Text = s.ConnectLabel.ToUpperInvariant();
        // While it protects, the power sign takes V2pn's green: the brand ring stays, the state is V2pn's.
        _power.Stroke = Scene.B(state == _c.Good ? _c.Good : _c.AccentText);
    }
}

internal static class NeonSceneExtensions
{
    public static T Also<T>(this T value, Action<T> act)
    {
        act(value);
        return value;
    }
}
