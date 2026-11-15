using Avalonia.Layout;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Converters;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// A provider's theme (V2pnTheme): one of six templates, assembled from V2pn's parts in the provider's colours, font,
/// shape, background, blocks and pictures. Every control of the other looks is here (Connect, Proxy/VPN, what goes
/// through the VPN, the server list with search, filters, ping, update and "+", the subscription's banners, the
/// provider's buttons). The provider sets no sizes and no colours of its own: V2pn derives them (V2pnThemePalette) and
/// each template decides where every part goes at every window width.
/// </summary>
public sealed class ThemeScene : EditionScene
{
    private readonly V2pnTheme _t;
    private readonly V2pnThemeColors _c;
    private readonly double _d;
    private readonly bool _onArea;
    private readonly bool _grouped;
    private readonly Bitmap? _backdrop;
    private readonly FontFamily _font;
    private readonly bool _actionsOnGlass;

    private readonly TextBlock _pillText = new();
    private readonly Border _pill;
    private readonly Border _pillChip;
    private readonly Control _brand;
    private readonly Bitmap? _logoOnDark;
    private readonly Bitmap? _logoOnLight;
    private readonly Image _flag = Scene.Flag(null, 28, 20);
    private readonly Border _flagBox;
    private readonly PathIcon _globe;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly StackPanel _place;
    private readonly ThemeConnect _connect;
    private readonly ThemeSegment _mode;
    private readonly Button _route;
    private readonly TextBlock _routeText;
    private readonly TextBlock _pingText;
    private readonly TextBlock _nodesText;
    private readonly WrapPanel _stats;
    private readonly Button _change;
    private readonly Button _cabinet;
    private readonly Button _support;
    private readonly WrapPanel _links;
    private readonly ForeignNote _foreign;
    private readonly Border _foreignCard;
    private readonly Control? _heroArt;
    private readonly Image? _heroImage;

    private readonly Dictionary<string, ThemeBlock> _blocks = [];
    private readonly Dictionary<string, Control> _cards = [];
    private readonly ServerBrowser _browser;
    private readonly EmptyState _empty;
    private readonly ContentControl _serversBody = new();
    private readonly TextBlock _serversCount;
    private readonly Control _serversCard;

    // The list template's rows.
    private readonly Button? _serverRow;
    private readonly TextBlock? _rowState;
    private readonly Control? _rowTitle;

    private readonly ContentControl _host = new();
    private readonly MapView? _map;
    private string _mapKey = string.Empty;
    private string _visibleKey = string.Empty;
    private EditionSnapshot? _last;

    public ThemeScene(V2pnPartner partner, IEditionActions actions, SceneLook look, V2pnTheme theme, V2pnThemeColors colours) : base(partner, actions, look)
    {
        _t = theme;
        _c = colours;
        _d = theme.Density == V2pnThemeDensity.Compact ? 0.78 : 1.0;
        _font = look.Text;
        _onArea = theme.Template is V2pnThemeTemplates.Center or V2pnThemeTemplates.Half or V2pnThemeTemplates.Split;
        _grouped = theme.Template == V2pnThemeTemplates.List;
        _backdrop = theme.Background.Art is { } backdrop ? Scene.Picture(partner, backdrop) : null;

        var heroInk = _onArea ? _c.Area.Ink : _c.Ink;
        var heroSoft = _onArea ? _c.Area.Soft : _c.Soft;
        // In the half template a picture background never shows under the controls: they stand on a glass card there,
        // and their own fills are opaque on it.
        _actionsOnGlass = theme.Template == V2pnThemeTemplates.Half && _backdrop != null && _c.Area.Scrim != null;
        var control = _onArea && !_actionsOnGlass ? _c.Area.Glass : _c.Raised;

        _pill = Scene.Pill(_pillText, light: !_c.Dark);
        _pillChip = new Border { Background = Scene.B(_c.Surface), CornerRadius = new CornerRadius(999), Child = _pill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };

        // The provider's logo, in its version for the ground it stands on (BrandOn).
        _logoOnDark = Scene.Picture(partner, partner.Pack.Logo);
        _logoOnLight = Scene.Picture(partner, partner.Pack.LogoOnLight) ?? _logoOnDark;
        _brand = _logoOnDark != null
            ? new Image { Height = 24, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center }
            : Scene.Text(partner.Name, 16, _c.Ink, _font, FontWeight.SemiBold);
        BrandOn(theme.Template != V2pnThemeTemplates.Map);

        _globe = Scene.Globe(heroSoft, 20);
        _flagBox = new Border { CornerRadius = look.R(4), ClipToBounds = true, Width = 28, Height = 20, Child = _flag, VerticalAlignment = VerticalAlignment.Center };
        _title = Scene.Text("", 32, heroInk, _font, FontWeight.SemiBold);
        _subtitle = Scene.Text("", 14, heroSoft, _font);
        _place = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { new Panel { Children = { _flagBox, _globe }, VerticalAlignment = VerticalAlignment.Center }, _title } },
                _subtitle,
            },
        };

        var states = (_c.Good, _c.Warn, _c.Muted);
        var ink = theme.Connect == V2pnThemeConnect.Toggle
            ? new ConnectInk(_c.Ink, _c.Surface, heroInk, control, states.Good, states.Warn, states.Muted)
            : _onArea
                ? new ConnectInk(_c.Area.Action, _c.Area.OnAction, heroInk, control, states.Good, states.Warn, states.Muted)
                : new ConnectInk(_c.Accent, _c.OnAccent, heroInk, control, states.Good, states.Warn, states.Muted);
        _connect = new ThemeConnect(theme.Connect, ink, actions, _font);

        _mode = new ThemeSegment(control, _c.Ink, _c.Surface, _c.Soft, _font, look.R, actions, Math.Round(42 * Math.Max(_d, 0.9)));
        _routeText = Scene.Text("", 13.5, _c.Ink, _font, FontWeight.Medium);
        _route = Scene.Button(
            new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Children = { _routeText, Scene.Text("▾", 12, _c.Soft, _font).Also(t => Grid.SetColumn(t, 1)) } },
            control, _c.Ink, 12, new Thickness(14, 0), height: Math.Round(42 * Math.Max(_d, 0.9)));
        _route.HorizontalAlignment = HorizontalAlignment.Stretch;
        _route.Click += (_, _) => Actions.OpenRoute(_route);
        ToolTip.SetTip(_route, L.SceneRouteHint);

        _pingText = Scene.Text("", 12.5, _c.Soft, _font, FontWeight.Medium);
        _nodesText = Scene.Text("", 12.5, _c.Soft, _font, FontWeight.Medium);
        _stats = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        _stats.Children.Add(Chip(_pingText, control));
        _stats.Children.Add(Chip(_nodesText, control));

        _change = Scene.Button(Scene.Text(L.SceneChange, 13, _c.Ink, _font, FontWeight.Medium), control, _c.Ink, 999, new Thickness(14, 0), height: 34);
        _change.VerticalAlignment = VerticalAlignment.Center;
        _change.Click += (_, _) => ShowServers(_change, _last);

        _cabinet = Link(L.SceneCabinet + " ↗", heroSoft, () => Open(_last?.Account?.CabinetUrl));
        _support = Link(L.SceneSupport + " ↗", heroSoft, () => Open(_last?.Account?.SupportUrl));
        _links = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 18, LineSpacing = 4 };
        _links.Children.Add(_cabinet);
        _links.Children.Add(_support);
        _links.Children.Add(Link(L.SceneIp + " ↗", heroSoft, Actions.OpenIp));
        _links.Children.Add(Link(L.SceneSettings, heroSoft, Actions.OpenSettings));

        var surfacePalette = Palette(_c.Surface, _c.Raised);
        _foreign = new ForeignNote(surfacePalette, actions);
        _foreignCard = new Border { Background = Scene.B(_c.Surface), CornerRadius = look.R(14), Padding = Pad(14, 10), Child = _foreign };

        if (theme.Picture(V2pnThemeArt.Hero) is { } heroFile && Scene.Picture(partner, heroFile) is { } hero)
        {
            _heroImage = new Image { Source = hero, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
            // A cut-out stands on the area as it is; a picture with its own ground is framed like a photo.
            _heroArt = Scene.HasTransparentCorners(hero)
                ? _heroImage
                : new Border { CornerRadius = look.R(18), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, Child = _heroImage };
        }

        // The blocks in the theme's order, each in the card its template gives it.
        var bannerArt = theme.Picture(V2pnThemeArt.Banner) is { } bannerFile ? Scene.Picture(partner, bannerFile) : null;
        foreach (var kind in theme.Blocks.Where(b => b != V2pnThemeBlocks.Servers))
        {
            var fill = CardFill(kind);
            var block = new ThemeBlock(kind, _c, fill, FieldFill(fill), _font, actions, bannerArt, look.R);
            _blocks[kind] = block;
            _cards[kind] = Card(block, kind switch
            {
                V2pnThemeBlocks.Plan => string.Format(L.SceneSubscriptionOf, partner.Name),
                V2pnThemeBlocks.Traffic => L.ThemeTraffic,
                V2pnThemeBlocks.News => string.Format(L.ThemeNews, partner.Name),
                _ => null,
            }, fill);
        }

        var serversFill = theme.Template == V2pnThemeTemplates.Map ? _c.Area.Glass : CardFill(V2pnThemeBlocks.Servers);
        var listPalette = Palette(serversFill == _c.Area.Glass ? _c.Surface : serversFill, FieldFill(serversFill));
        _browser = new ServerBrowser(listPalette, actions);
        _empty = new EmptyState(listPalette, actions, partner.Name);
        _serversCount = Scene.Text("", 12, _c.Soft, _font, FontWeight.Medium);
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                Scene.Text(L.SceneServers, 16, _c.Ink, _font, FontWeight.SemiBold),
                new Border { Background = Scene.B(FieldFill(serversFill)), CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center, Child = _serversCount },
            },
        };
        var serversGrid = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12 * _d };
        ServerBrowser.Put(serversGrid, _grouped ? new Border() : header, 0, 0);
        ServerBrowser.Put(serversGrid, _serversBody, 1, 0);
        _serversCard = _grouped
            ? Group(L.SceneServers, new Border { Padding = Pad(14, 12), Child = serversGrid }, fill: true)
            : new Border { Background = Scene.B(serversFill), CornerRadius = look.R(18), Padding = Pad(16, 14), Child = serversGrid };

        if (_grouped)
        {
            _rowState = Scene.Text("", 13, _c.Soft, _font);
            _rowTitle = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { Scene.Text(L.ThemeConnection, 15, _c.Ink, _font, FontWeight.SemiBold), _rowState } };
            _serverRow = Scene.Button(new Border(), null, _c.Ink, 0, Pad(16, 12));
            _serverRow.HorizontalAlignment = HorizontalAlignment.Stretch;
            _serverRow.Click += (_, _) => ShowServers(_serverRow!, _last);
        }

        if (theme.Template == V2pnThemeTemplates.Map)
        {
            _map = new MapView();
            _map.LocationClicked += code => ShowCountry(code);
        }

        Content = new ThemeVariantScope { RequestedThemeVariant = _c.Dark ? ThemeVariant.Dark : ThemeVariant.Light, Child = _host };
        Reflow(Fit.Wide);
    }

    // ------------------------------------------------------------------ pieces

    private Thickness Pad(double h, double v) => new(Math.Round(h * _d), Math.Round(v * _d));

    private ScenePalette Palette(string ground, string raised) =>
        new(ground, raised, _c.Ink, _c.Soft, _c.Muted, _c.Accent, _c.OnAccent, 12, _font, Good: _c.Good, Warn: _c.Warn, Bad: _c.Bad);

    /// <summary>The fill of a block's card: on the ground a surface; inside a card (compact) or a panel over the map a raised one; on the split's brand column glass.</summary>
    private string CardFill(string block) => _t.Template switch
    {
        V2pnThemeTemplates.Compact or V2pnThemeTemplates.Map => _c.Raised,
        V2pnThemeTemplates.Split when block is V2pnThemeBlocks.Plan or V2pnThemeBlocks.Traffic => _c.Area.Glass,
        _ => _c.Surface,
    };

    private string FieldFill(string fill) => fill == _c.Raised ? _c.Surface : _c.Raised;

    private Border Chip(TextBlock text, string fill) => new()
    {
        Background = Scene.B(fill),
        CornerRadius = new CornerRadius(999),
        Padding = new Thickness(12, 6),
        Child = text,
    };

    private Button Link(string text, string colour, Action click)
    {
        var button = Scene.Button(Scene.Text(text, 13, colour, _font), null, colour, 6, new Thickness(0, 4));
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>The provider's name or logo for the brand area (<paramref name="area"/>) or the page and its cards.</summary>
    private void BrandOn(bool area)
    {
        var ink = area ? _c.Area.Ink : _c.Ink;
        switch (_brand)
        {
            case Image image:
                // A light ink means a dark ground: the logo made for dark grounds.
                image.Source = V2pnColor.Parse(ink).Luminance > 0.4 ? _logoOnDark : _logoOnLight;
                break;
            case TextBlock text:
                text.Foreground = Scene.B(ink);
                break;
        }
    }

    /// <summary>The quiet links in the ink of the ground they are on: the brand area's or the page's.</summary>
    private Control Links(bool onArea)
    {
        var colour = onArea ? _c.Area.Soft : _c.Soft;
        foreach (var link in _links.Children.OfType<Button>())
        {
            if (link.Content is Border { Child: TextBlock text })
            {
                text.Foreground = Scene.B(colour);
            }
        }
        return Take(_links);
    }

    private void Open(string? url)
    {
        if (url.IsNotEmpty())
        {
            Actions.OpenUrl(url!);
        }
    }

    /// <summary>A block in its card; in the list template a titled group, as in settings.</summary>
    private Control Card(Control body, string? title, string fill)
    {
        if (_grouped)
        {
            return Group(title, new Border { Padding = Pad(16, 14), Child = body }, fill: false);
        }
        var content = title == null
            ? body
            : new StackPanel { Spacing = 10 * _d, Children = { Scene.Text(title, 12.5, _c.Muted, _font, FontWeight.Medium), body } };
        return new Border { Background = Scene.B(fill), CornerRadius = Look.R(18), Padding = Pad(18, 16), Child = content };
    }

    /// <summary>A settings-style group: its title over a surface with the rows.</summary>
    private Control Group(string? title, Control rows, bool fill)
    {
        var box = new Border { Background = Scene.B(_c.Surface), CornerRadius = Look.R(16), ClipToBounds = true, Child = rows };
        var grid = new Grid { RowDefinitions = new(fill ? "Auto,*" : "Auto,Auto"), RowSpacing = 6 };
        if (title != null)
        {
            var label = Scene.Text(title, 12.5, _c.Soft, _font, FontWeight.Medium);
            label.Margin = new Thickness(6, 0, 0, 0);
            ServerBrowser.Put(grid, label, 0, 0);
        }
        ServerBrowser.Put(grid, box, 1, 0);
        return grid;
    }

    private Control Hairline() => new Border { Height = 1, Background = Scene.B(_c.Line), Margin = new Thickness(16, 0, 0, 0) };

    /// <summary>The servers of one country on the map, as a menu.</summary>
    private void ShowCountry(string code)
    {
        if (_last == null || _map == null)
        {
            return;
        }
        var servers = _last.Catalog.Where(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)).Take(30).ToList();
        if (servers.Count == 0)
        {
            return;
        }
        var menu = new MenuFlyout { Placement = PlacementMode.Pointer };
        foreach (var server in servers)
        {
            var item = new MenuItem
            {
                Header = server.Delay > 0 ? $"{server.Name}   {server.Delay} {L.SceneMs}" : server.Name,
                Icon = Scene.Flag(server.Code, 18, 13),
                FontWeight = server.IndexId == _last.SelectedId ? FontWeight.SemiBold : FontWeight.Normal,
            };
            var id = server.IndexId;
            item.Click += (_, _) => Actions.Select(id);
            menu.Items.Add(item);
        }
        menu.ShowAt(_map, showAtPointer: true);
    }

    /// <summary>Mode and route side by side where there is room, one under the other where not.</summary>
    private Control Controls(bool stacked, double maxWidth = double.PositiveInfinity)
    {
        var grid = new Grid { RowSpacing = 8, ColumnSpacing = 8, MaxWidth = maxWidth };
        if (stacked)
        {
            grid.RowDefinitions = new("Auto,Auto");
            Put(grid, _mode, 0, 0);
            Put(grid, _route, 1, 0);
        }
        else
        {
            grid.ColumnDefinitions = new("Auto,*");
            _mode.MinWidth = 190;
            Put(grid, _mode, 0, 0);
            Put(grid, _route, 0, 1);
        }
        return grid;
    }

    private Grid TopRow(bool brand, bool change = true)
    {
        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 12 };
        if (brand)
        {
            Put(row, _brand, 0, 0);
            var pill = Take(_pillChip);
            pill.HorizontalAlignment = HorizontalAlignment.Left;
            ServerBrowser.Put(row, pill, 0, 1);
        }
        else
        {
            ServerBrowser.Put(row, Take(_pillChip), 0, 0, 2);
        }
        if (change)
        {
            Put(row, _change, 0, 2);
        }
        return row;
    }

    private Control? HeroArt(double maxHeight)
    {
        if (_heroArt == null || _heroImage == null)
        {
            return null;
        }
        _heroImage.MaxHeight = maxHeight;
        return Take(_heroArt);
    }

    /// <summary>The blocks of the theme in its order (the servers among them), skipping what <paramref name="skip"/> keeps elsewhere.</summary>
    private IEnumerable<Control> BlockCards(Func<string, bool>? skip = null)
    {
        foreach (var kind in _t.Blocks)
        {
            if (skip?.Invoke(kind) == true)
            {
                continue;
            }
            yield return Take(kind == V2pnThemeBlocks.Servers ? _serversCard : _cards[kind]);
        }
    }

    private bool AnyVisible(Func<string, bool> which) =>
        _t.Blocks.Where(b => b != V2pnThemeBlocks.Servers && which(b)).Any(b => _blocks[b].IsVisible);

    private ThemeStack Stack(double spacing, double fillMin, Thickness margin, IEnumerable<Control?> parts)
    {
        var stack = new ThemeStack(spacing * _d, fillMin, margin);
        foreach (var part in parts)
        {
            if (part != null)
            {
                stack.Parts.Add(Take(part));
            }
        }
        if (stack.Parts.Contains(_serversCard))
        {
            stack.Fill = _serversCard;
        }
        return stack;
    }

    private Control Area(Control content, CornerRadius radius = default) => ThemeKit.Area(_c.Area, _backdrop, content, radius);

    /// <summary>The controls as they are, or on a glass card where the area behind them is a picture.</summary>
    private Control ActionCard(Control actions) => !_actionsOnGlass ? actions : new Border
    {
        Background = Scene.B(_c.Area.Glass),
        CornerRadius = Look.R(20),
        Padding = Pad(16, 16),
        Child = actions,
        VerticalAlignment = actions.VerticalAlignment,
    };

    // ------------------------------------------------------------------ layouts

    protected override void Reflow(Fit fit)
    {
        var narrow = fit == Fit.Narrow;
        var scale = fit switch { Fit.Wide => 1.0, Fit.Medium => 0.9, _ => 0.76 };
        // A round button is the hero of the centre template; elsewhere it stands beside other parts and is smaller.
        if (_t.Connect == V2pnThemeConnect.Round)
        {
            scale *= _t.Template switch
            {
                V2pnThemeTemplates.Half => 0.82,
                V2pnThemeTemplates.Split => 0.72,
                V2pnThemeTemplates.Map or V2pnThemeTemplates.Compact => 0.66,
                _ => 1,
            };
        }
        _connect.Resize(scale);
        _connect.ShowCaption = !_grouped;
        _connect.Stretch = narrow || _t.Template is V2pnThemeTemplates.Split or V2pnThemeTemplates.Compact or V2pnThemeTemplates.Map;
        foreach (var part in _cards.Values.Append(_serversCard).Append(_foreignCard))
        {
            part.Margin = default;
        }
        _host.Content = _t.Template switch
        {
            V2pnThemeTemplates.Half => Half(fit),
            V2pnThemeTemplates.Split => Split(fit),
            V2pnThemeTemplates.List => ListLayout(fit),
            V2pnThemeTemplates.Map => MapLayout(fit),
            V2pnThemeTemplates.Compact => Compact(fit),
            _ => Center(fit),
        };
        if (_last != null)
        {
            Update(_last);
        }
    }

    /// <summary>Connect big in the middle of the brand area, the place and the state under it; the blocks in a column beside it.</summary>
    private Control Center(Fit fit)
    {
        var narrow = fit == Fit.Narrow;
        _title.FontSize = fit switch { Fit.Wide => 38, Fit.Medium => 32, _ => 26 };
        _title.TextAlignment = TextAlignment.Center;
        _subtitle.TextAlignment = TextAlignment.Center;
        _place.HorizontalAlignment = HorizontalAlignment.Center;
        ((StackPanel)_place.Children[0]).HorizontalAlignment = HorizontalAlignment.Center;
        _subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        _stats.HorizontalAlignment = HorizontalAlignment.Center;
        _links.HorizontalAlignment = HorizontalAlignment.Center;

        var hero = new StackPanel
        {
            Spacing = (narrow ? 14 : 20) * _d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460,
        };
        foreach (var part in new[] { HeroArt(narrow ? 84 : fit == Fit.Medium ? 130 : 160), Take(_connect), Take(_place), Take(_stats), Controls(stacked: narrow, 420), Take(_foreignCard) })
        {
            if (part != null)
            {
                hero.Children.Add(part);
            }
        }

        if (narrow)
        {
            var parts = new List<Control?> { TopRow(brand: false), hero };
            parts.AddRange(BlockCards());
            parts.Add(Links(onArea: true));
            return Area(Stack(14, 380, new Thickness(14, 12, 14, 16), parts));
        }

        var left = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(24, 18, 24, 14) };
        ServerBrowser.Put(left, TopRow(brand: true), 0, 0);
        ServerBrowser.Put(left, new ScrollViewer { Content = hero, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }, 1, 0);
        ServerBrowser.Put(left, Links(onArea: true), 2, 0);
        var right = Stack(12, 320, new Thickness(0, 16, 16, 16), BlockCards());
        var grid = new Grid { ColumnDefinitions = new(fit == Fit.Wide ? "*,410" : "*,340") };
        ServerBrowser.Put(grid, left, 0, 0);
        ServerBrowser.Put(grid, right, 0, 1);
        return Area(grid);
    }

    /// <summary>The brand area across the top with the place and Connect; the blocks and the servers below on the page.</summary>
    private Control Half(Fit fit)
    {
        var narrow = fit == Fit.Narrow;
        _title.FontSize = fit switch { Fit.Wide => 40, Fit.Medium => 34, _ => 28 };
        ResetAlignment();

        if (narrow)
        {
            var top = new StackPanel { Spacing = 14 * _d, Margin = new Thickness(18, 16, 18, 20) };
            top.Children.Add(TopRow(brand: true, change: false));
            top.Children.Add(Take(_place));
            top.Children.Add(Take(_stats));
            top.Children.Add(ActionCard(new StackPanel { Spacing = 12, Children = { Take(_connect), Controls(stacked: true) } }));
            var parts = new List<Control?> { Area(top), Take(_foreignCard) };
            parts.AddRange(BlockCards());
            parts.Add(Links(onArea: false));
            var stack = Stack(12, 380, new Thickness(0, 0, 0, 16), parts);
            foreach (var part in stack.Parts.Skip(1))
            {
                part.Margin = new Thickness(14, 0);
            }
            return new Panel { Background = Scene.B(_c.Ground), Children = { stack } };
        }

        var info = new StackPanel { Spacing = 14 * _d, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(TopRow(brand: true, change: false));
        info.Children.Add(Take(_place));
        info.Children.Add(Take(_stats));
        var act = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Width = fit == Fit.Wide ? 340 : 280 };
        act.Children.Add(Take(_connect));
        act.Children.Add(Controls(stacked: true));
        var band = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 28, Margin = new Thickness(30, 22, 30, 26) };
        ServerBrowser.Put(band, info, 0, 0);
        if (fit == Fit.Wide && HeroArt(170) is { } art)
        {
            art.VerticalAlignment = VerticalAlignment.Center;
            ServerBrowser.Put(band, art, 0, 1);
        }
        ServerBrowser.Put(band, ActionCard(act), 0, 2);

        var others = AnyVisible(_ => true) || _foreign.IsVisible;
        var low = new Grid { Margin = new Thickness(16), ColumnSpacing = 14 };
        if (others)
        {
            low.ColumnDefinitions = new(fit == Fit.Wide ? "*,1.3*" : "300,*");
            var side = new List<Control?> { Take(_foreignCard) };
            side.AddRange(BlockCards(b => b == V2pnThemeBlocks.Servers));
            side.Add(Links(onArea: false));
            ServerBrowser.Put(low, Stack(12, 0, default, side), 0, 0);
            Put(low, _serversCard, 0, 1);
        }
        else
        {
            low.RowDefinitions = new("*,Auto");
            low.RowSpacing = 10;
            Put(low, _serversCard, 0, 0);
            ServerBrowser.Put(low, Links(onArea: false), 1, 0);
        }
        var root = new Grid { RowDefinitions = new("Auto,*"), Background = Scene.B(_c.Ground) };
        ServerBrowser.Put(root, Area(band), 0, 0);
        ServerBrowser.Put(root, low, 1, 0);
        return root;
    }

    /// <summary>A brand column on the left with the state, Connect and the plan; the servers and other blocks on the right.</summary>
    private Control Split(Fit fit)
    {
        var narrow = fit == Fit.Narrow;
        _title.FontSize = narrow ? 26 : 28;
        ResetAlignment();
        bool InColumn(string block) => block is V2pnThemeBlocks.Plan or V2pnThemeBlocks.Traffic;

        var column = new List<Control?>
        {
            TopRow(brand: true, change: false),
            HeroArt(narrow ? 100 : 130),
            Take(_place),
            Take(_stats),
            Take(_connect),
            Controls(stacked: true),
        };
        column.AddRange(_t.Blocks.Where(InColumn).Select(b => Take(_cards[b])));
        var rest = new List<Control?> { Take(_foreignCard) };
        rest.AddRange(BlockCards(InColumn));

        if (narrow)
        {
            var top = new StackPanel { Spacing = 14 * _d, Margin = new Thickness(18, 16, 18, 20) };
            foreach (var part in column.OfType<Control>())
            {
                top.Children.Add(part);
            }
            var parts = new List<Control?> { Area(top) };
            parts.AddRange(rest);
            parts.Add(Links(onArea: false));
            var stack = Stack(12, 380, new Thickness(0, 0, 0, 16), parts);
            foreach (var part in stack.Parts.Skip(1))
            {
                part.Margin = new Thickness(14, 0);
            }
            return new Panel { Background = Scene.B(_c.Ground), Children = { stack } };
        }

        column.Add(Links(onArea: true));
        var left = Area(Stack(14, 0, new Thickness(22, 20, 22, 18), column));
        var right = Stack(12, 320, new Thickness(16), rest);
        var grid = new Grid { ColumnDefinitions = new(fit == Fit.Wide ? "360,*" : "310,*"), Background = Scene.B(_c.Ground) };
        ServerBrowser.Put(grid, left, 0, 0);
        ServerBrowser.Put(grid, right, 0, 1);
        return grid;
    }

    /// <summary>Settings-style groups: Connect is the first row, then the server, the mode and the route; the blocks follow as groups.</summary>
    private Control ListLayout(Fit fit)
    {
        var narrow = fit == Fit.Narrow;
        _title.FontSize = 15;
        _subtitle.FontSize = 12.5;
        ResetAlignment();

        var header = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 14, Margin = new Thickness(20, 16) };
        Put(header, _brand, 0, 0);
        var pill = Take(_pillChip);
        pill.HorizontalAlignment = HorizontalAlignment.Right;
        ServerBrowser.Put(header, pill, 0, 1);

        // Row one: Connect. A switch or a round button sits at the end of the row; a capsule or a bar takes the row.
        var rows = new StackPanel();
        var wideControl = _t.Connect is V2pnThemeConnect.Bar or V2pnThemeConnect.Capsule;
        _connect.Stretch = wideControl;
        if (_t.Connect == V2pnThemeConnect.Round)
        {
            _connect.Resize(0.42);
        }
        var title = Take(_rowTitle!);
        if (wideControl)
        {
            rows.Children.Add(new StackPanel { Spacing = 12, Margin = Pad(16, 14), Children = { title, Take(_connect) } });
        }
        else
        {
            var connectRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Margin = Pad(16, 12) };
            ServerBrowser.Put(connectRow, title, 0, 0);
            Put(connectRow, _connect, 0, 1);
            rows.Children.Add(connectRow);
        }
        rows.Children.Add(Hairline());
        var serverRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        ServerBrowser.Put(serverRow, Take(_place), 0, 0);
        ServerBrowser.Put(serverRow, Scene.Text("›", 20, _c.Soft, _font), 0, 1);
        ((Border)_serverRow!.Content!).Child = serverRow;
        rows.Children.Add(Take(_serverRow));
        rows.Children.Add(Hairline());
        var modeRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Margin = Pad(16, 10) };
        ServerBrowser.Put(modeRow, Scene.Text(L.ThemeMode, 14, _c.Ink, _font, FontWeight.Medium), 0, 0);
        _mode.MinWidth = narrow ? 160 : 200;
        Put(modeRow, _mode, 0, 1);
        rows.Children.Add(modeRow);
        rows.Children.Add(Hairline());
        var routeRow = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12, Margin = Pad(16, 10) };
        ServerBrowser.Put(routeRow, Scene.Text(L.SceneThroughVpn, 14, _c.Ink, _font, FontWeight.Medium), 0, 0);
        _route.MaxWidth = 280;
        _route.HorizontalAlignment = HorizontalAlignment.Right;
        Put(routeRow, _route, 0, 1);
        rows.Children.Add(routeRow);
        var connection = Group(null, rows, fill: false);

        var left = new List<Control?> { connection, Take(_stats), Take(_foreignCard) };
        if (narrow)
        {
            left.AddRange(BlockCards());
            left.Add(Links(onArea: false));
            var stack = Stack(14, 360, new Thickness(14, 14, 14, 16), left);
            var root = new Grid { RowDefinitions = new("Auto,*"), Background = Scene.B(_c.Ground) };
            ServerBrowser.Put(root, Area(header), 0, 0);
            ServerBrowser.Put(root, stack, 1, 0);
            return root;
        }
        left.AddRange(BlockCards(b => b == V2pnThemeBlocks.Servers));
        left.Add(Links(onArea: false));
        var columns = new Grid { ColumnDefinitions = new(fit == Fit.Wide ? "440,*" : "360,*"), ColumnSpacing = 18, Margin = new Thickness(18, 16, 18, 16) };
        ServerBrowser.Put(columns, Stack(16, 0, default, left), 0, 0);
        Put(columns, _serversCard, 0, 1);
        var page = new Grid { RowDefinitions = new("Auto,*"), Background = Scene.B(_c.Ground) };
        ServerBrowser.Put(page, Area(header), 0, 0);
        ServerBrowser.Put(page, columns, 1, 0);
        return page;
    }

    /// <summary>V2pn's map edge to edge; the state, Connect and the blocks on a floating panel, the servers on another.</summary>
    private Control MapLayout(Fit fit)
    {
        var narrow = fit == Fit.Narrow;
        _title.FontSize = narrow ? 22 : 26;
        ResetAlignment();
        var map = Take(_map!);

        var parts = new List<Control?> { TopRow(brand: true, change: false), Take(_place), Take(_stats), Take(_connect), Controls(stacked: true), Take(_foreignCard) };
        if (narrow)
        {
            parts.AddRange(BlockCards());
            parts.Add(Links(onArea: false));
            var sheet = new Border
            {
                Background = Scene.B(_c.Surface),
                CornerRadius = new CornerRadius(Look.Radius(22), Look.Radius(22), 0, 0),
                Margin = new Thickness(0, -22, 0, 0),
                Child = Stack(12, 340, new Thickness(16, 18, 16, 16), parts),
            };
            var grid = new Grid { RowDefinitions = new("230,*") };
            ServerBrowser.Put(grid, map, 0, 0);
            ServerBrowser.Put(grid, sheet, 1, 0);
            return grid;
        }

        parts.AddRange(BlockCards(b => b == V2pnThemeBlocks.Servers));
        parts.Add(Links(onArea: false));
        var panel = new Border
        {
            Background = Scene.B(_c.Area.Glass),
            CornerRadius = Look.R(20),
            VerticalAlignment = VerticalAlignment.Top,
            Child = Stack(12, 0, new Thickness(18, 16, 18, 16), parts),
        };
        var overlay = new Grid { ColumnDefinitions = new(fit == Fit.Wide ? "370,*,350" : "330,*,300"), Margin = new Thickness(16) };
        ServerBrowser.Put(overlay, panel, 0, 0);
        Put(overlay, _serversCard, 0, 2);
        return new Panel { Children = { map, overlay } };
    }

    /// <summary>One tall card on the brand area: everything in one column, made for small windows.</summary>
    private Control Compact(Fit fit)
    {
        var narrow = fit == Fit.Narrow;
        _title.FontSize = 24;
        ResetAlignment();

        // In a small window the card nearly fills it and its head is a band of the brand area; in a larger one the
        // card floats on the brand area and its head is part of the card.
        BrandOn(narrow);
        var head = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Margin = narrow ? new Thickness(20, 16) : new Thickness(22, 20, 22, 0) };
        var headLeft = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        headLeft.Children.Add(Take(_brand));
        var pill = Take(_pillChip);
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        headLeft.Children.Add(pill);
        ServerBrowser.Put(head, headLeft, 0, 0);
        if (HeroArt(narrow ? 76 : 96) is { } art)
        {
            art.VerticalAlignment = VerticalAlignment.Center;
            ServerBrowser.Put(head, art, 0, 1);
        }
        else
        {
            Put(head, _change, 0, 1);
        }

        var parts = new List<Control?> { Take(_place), Take(_connect), Controls(stacked: true), Take(_stats), Take(_foreignCard) };
        parts.AddRange(BlockCards());
        parts.Add(Links(onArea: false));
        var body = Stack(12, 300, new Thickness(18, 16, 18, 16), parts);
        var radius = Look.R(narrow ? 20 : 26);
        var card = new Border
        {
            Background = Scene.B(_c.Surface),
            CornerRadius = radius,
            ClipToBounds = true,
            Child = new Grid { RowDefinitions = new("Auto,*"), Children = { narrow ? Area(head) : head, body.Also(b => Grid.SetRow(b, 1)) } },
            Margin = narrow ? new Thickness(10) : new Thickness(0, 22),
            Width = narrow ? double.NaN : 460,
            HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
        };
        return Area(card);
    }

    private void ResetAlignment()
    {
        _title.TextAlignment = TextAlignment.Left;
        _subtitle.TextAlignment = TextAlignment.Left;
        _place.HorizontalAlignment = HorizontalAlignment.Stretch;
        ((StackPanel)_place.Children[0]).HorizontalAlignment = HorizontalAlignment.Left;
        _subtitle.HorizontalAlignment = HorizontalAlignment.Left;
        _stats.HorizontalAlignment = HorizontalAlignment.Left;
        _links.HorizontalAlignment = HorizontalAlignment.Left;
        _route.MaxWidth = double.PositiveInfinity;
        _route.HorizontalAlignment = HorizontalAlignment.Stretch;
        _mode.MinWidth = 0;
    }

    // ------------------------------------------------------------------ state

    public override void Update(EditionSnapshot s)
    {
        _last = s;
        Scene.SetPill(_pill, _pillText, s.StateKind, s.StateLabel);
        _flag.Source = V2pnConverters.Flag(s.CountryCode);
        _flagBox.IsVisible = _flag.Source != null;
        _globe.IsVisible = _flag.Source == null;
        _title.Text = s.Title;
        _subtitle.Text = s.Subscription.IsNotEmpty() ? $"{s.Subtitle} · {s.Subscription}" : s.Subtitle;
        _connect.Update(s);
        _mode.Update(s.Vpn);
        _routeText.Text = s.Route;
        _pingText.Text = s.Ping > 0 ? $"{L.ScenePing} {string.Format(L.ThemePingValue, s.Ping)}" : L.ThemeNoPing;
        _nodesText.Text = s.NodesTotal > 0 ? string.Format(L.ThemeAnswering, NodesText(s)) : L.SceneNotPinged;
        _change.IsEnabled = s.Servers.Count > 0;
        if (_rowState != null)
        {
            _rowState.Text = s.StateLabel;
        }
        _cabinet.IsVisible = s.Account?.CabinetUrl != null;
        _support.IsVisible = s.Account?.SupportUrl != null;
        _foreign.Update(s);
        _foreignCard.IsVisible = _foreign.IsVisible;
        foreach (var (kind, block) in _blocks)
        {
            block.Update(s);
            _cards[kind].IsVisible = block.IsVisible;
        }
        _serversCount.Text = s.Catalog.Count.ToString(CultureInfo.CurrentCulture);
        ShowListOrEmpty(_serversBody, _browser, _empty, s);
        UpdateMap(s);

        // A block that appears or goes can change the layout: the half template drops a side column with nothing in it.
        var key = string.Join(",", _blocks.Where(b => b.Value.IsVisible).Select(b => b.Key)) + _foreign.IsVisible;
        if (key != _visibleKey)
        {
            _visibleKey = key;
            if (_t.Template == V2pnThemeTemplates.Half)
            {
                Dispatcher.UIThread.Post(() => Reflow(CurrentFit), DispatcherPriority.Background);
            }
        }
    }

    private void UpdateMap(EditionSnapshot s)
    {
        if (_map == null)
        {
            return;
        }
        var locations = s.Catalog.Where(x => x.Code != null && !string.Equals(x.Code, "EU", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.Code!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MapLocation(g.Key, WorldMap.Instance.Get(g.Key)?.Name ?? g.Key, g.Count(), g.Where(x => x.Delay > 0).Select(x => x.Delay).DefaultIfEmpty(0).Min()))
            .ToList();
        var key = string.Join(",", locations.Select(l => $"{l.Code}{l.Count}{l.BestDelay}"));
        if (key != _mapKey)
        {
            _mapKey = key;
            _map.SetLocations(locations);
        }
        _map.Focus(s.CountryCode, s.StateKind switch
        {
            "safe" => ProtectionState.Protected,
            "warn" => ProtectionState.LocalOnly,
            _ => ProtectionState.Unprotected,
        });
    }
}

internal static class ThemeSceneExtensions
{
    public static T Also<T>(this T value, Action<T> act)
    {
        act(value);
        return value;
    }
}
