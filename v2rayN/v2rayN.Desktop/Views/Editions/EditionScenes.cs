using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media.Transformation;
using v2rayN.Desktop.Converters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>A partner look that replaces the sidebar and the map: it reads a snapshot and calls back into the window.</summary>
public abstract class EditionScene : UserControl
{
    protected EditionScene(V2pnPartner partner, IEditionActions actions)
    {
        Partner = partner;
        Actions = actions;
        Scene.AddButtonStyles(this);
    }

    protected V2pnPartner Partner { get; }
    protected IEditionActions Actions { get; }

    public abstract void Update(EditionSnapshot s);

    /// <summary>A flyout of the servers the snapshot lists, for "Change server" in looks without a sidebar.</summary>
    protected void ShowServers(Control anchor, EditionSnapshot? s)
    {
        if (s == null || s.Servers.Count == 0)
        {
            return;
        }
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var server in s.Servers)
        {
            var item = new MenuItem
            {
                Header = server.Delay > 0 ? $"{server.Name}   {server.Delay} мс" : server.Name,
                Icon = Scene.Flag(server.Code, 18, 13),
                FontWeight = server.Selected ? FontWeight.SemiBold : FontWeight.Normal,
            };
            var id = server.IndexId;
            item.Click += (_, _) => Actions.Select(id);
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }

    protected static string NodesText(EditionSnapshot s) =>
        s.NodesTotal > 0 ? $"{s.NodesOk} из {s.NodesTotal}" : "—";
}

/// <summary>
/// The cabinet look (Configra's personal cabinet): a peach card with the place, the state and the node squares;
/// below, the servers and what goes through the VPN.
/// </summary>
public sealed class CabinetScene : EditionScene
{
    private readonly Image _flag = Scene.Flag(null, 34, 24);
    private readonly TextBlock _title = Scene.Text("", 44, "#121211");
    private readonly TextBlock _subtitle = Scene.Text("", 13, "#55524C");
    private readonly TextBlock _stateText = Scene.Text("", 18, "#121211");
    private readonly TextBlock _nodes = Scene.Text("", 18, "#121211");
    private readonly TextBlock _stateNote = Scene.Text("", 12.5, "#5B5750");
    private readonly NodeMeter _meter = new(11, "#FF7A2F", "#CFC8BF");
    private readonly Border _stateDot = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(2) };
    private readonly TextBlock _connectLabel = Scene.Text("", 14, "#F3F2EE", weight: FontWeight.Medium);
    private readonly TextBlock _pillText = new();
    private readonly Border _pill;
    private readonly MatrixText _ping = new() { Cell = 5.6, Foreground = Scene.B("#121211") };
    private readonly MatrixText _count = new() { Cell = 4.2, Foreground = Scene.B("#121211") };
    private readonly StackPanel _servers = new() { Spacing = 2 };
    private readonly TextBlock _serversCount = Scene.Text("", 12, "#8E8C86");
    private readonly TextBlock _route = Scene.Text("", 13, "#DAD7D0");
    private readonly Button _proxy;
    private readonly Button _vpn;
    private readonly Button _change;
    private EditionSnapshot? _last;

    public CabinetScene(V2pnPartner partner, IEditionActions actions) : base(partner, actions)
    {
        _title.FontWeight = FontWeight.Normal;
        _title.LetterSpacing = -1.6;
        _pill = Scene.Pill(_pillText, light: true);

        var avatar = new Border
        {
            Width = 62,
            Height = 62,
            CornerRadius = new CornerRadius(16),
            Background = Scene.B("#F7F6F3"),
            Child = new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Width = 34, Height = 24, Child = _flag },
        };
        var who = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Children = { avatar, new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _subtitle } } },
        };

        // The state can be a long sentence ("proxy on port 20828"); it trims before it reaches the count.
        var stateRow = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 9 };
        stateRow.Children.Add(_stateDot);
        Grid.SetColumn(_stateText, 1);
        stateRow.Children.Add(_stateText);
        Grid.SetColumn(_nodes, 2);
        stateRow.Children.Add(_nodes);
        var state = new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Scene.B("#80FFFFFF"),
            BorderBrush = Scene.B("#A6FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 12),
            Child = new StackPanel { Spacing = 9, Children = { stateRow, _meter, _stateNote } },
        };

        var connect = Scene.Button(
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Children = { Scene.Power("#FF7A2F"), _connectLabel } },
            "#1B1715", "#F3F2EE", 14, new Thickness(22, 0), height: 46);
        connect.MinWidth = 230;
        connect.Click += (_, _) => Actions.ToggleConnect();
        _change = Scene.Button(Scene.Text("Сменить сервер", 14, "#121211", weight: FontWeight.Medium), null, "#121211", 14, new Thickness(22, 0), "#40121211", height: 46);
        _change.Click += (_, _) => ShowServers(_change, _last);
        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 14, 0, 0), Children = { connect, _change } };

        var left = new StackPanel { Children = { who, state, actionsRow } };
        var right = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new Border { HorizontalAlignment = HorizontalAlignment.Right, Child = _pill },
                Tile("Отклик", _ping, "мс"),
                Tile("Отвечают", _count, "серверов"),
            },
        };
        var heroGrid = new Grid { ColumnDefinitions = new("*,300"), ColumnSpacing = 20 };
        heroGrid.Children.Add(left);
        Grid.SetColumn(right, 1);
        heroGrid.Children.Add(right);
        var hero = new Border
        {
            CornerRadius = new CornerRadius(26),
            Padding = new Thickness(26, 22),
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0, 0, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0, 0, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(1.3, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(1.6, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#ECEAE6"), 0),
                    new GradientStop(Color.Parse("#E8E3DC"), 0.35),
                    new GradientStop(Color.Parse("#F0C9AA"), 0.75),
                    new GradientStop(Color.Parse("#EDAA80"), 1),
                },
            },
            Child = heroGrid,
        };

        _proxy = Segment("Прокси", () => Actions.SetVpn(false));
        _vpn = Segment("VPN", () => Actions.SetVpn(true));
        var routeButton = Scene.Button(
            new Grid
            {
                ColumnDefinitions = new("*,Auto"),
                Children = { _route, new TextBlock { Text = "▾", Foreground = Scene.B("#8E8C86"), [Grid.ColumnProperty] = 1, VerticalAlignment = VerticalAlignment.Center } },
            },
            "#232321", "#DAD7D0", 12, new Thickness(14, 0), height: 38);
        routeButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        routeButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        routeButton.Click += (_, _) => Actions.OpenRoute(routeButton);
        var modes = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 };
        modes.Children.Add(new Border { Background = Scene.B("#232321"), CornerRadius = new CornerRadius(12), Padding = new Thickness(3), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _proxy, _vpn } } });
        Grid.SetColumn(routeButton, 1);
        modes.Children.Add(routeButton);

        var ascii = new TextBlock
        {
            FontFamily = Scene.Mono,
            FontSize = 11,
            LineHeight = 12.5,
            Foreground = Scene.B("#6A6862"),
            Text = "  .--------.                      .----------.\n  | ###### |---·····---·····---| ######## |\n  | ###### |       сервер       | ######## |\n  '---..---'                      | ####-### |\n    ==''==     ваш ПК → сервер    '----------'",
        };

        var serversPanel = Panel(new StackPanel
        {
            Spacing = 12,
            Children = { Header("Серверы", _serversCount), new ScrollViewer { Content = _servers, MaxHeight = 260 } },
        });
        var vpnPanel = Panel(new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Header("Через VPN", null), modes, ascii,
                new TextBlock { Text = $"{partner.Name} отдаёт список серверов. Трафик идёт с вашего компьютера прямо на сервер — {partner.Name} его не видит.", TextWrapping = TextWrapping.Wrap, FontFamily = Scene.Geist, FontSize = 12, Foreground = Scene.B("#8E8C86") },
            },
        });
        var low = new Grid { ColumnDefinitions = new("1.15*,*"), ColumnSpacing = 10 };
        low.Children.Add(serversPanel);
        Grid.SetColumn(vpnPanel, 1);
        low.Children.Add(vpnPanel);

        var root = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 10 };
        root.Children.Add(hero);
        Grid.SetRow(low, 1);
        root.Children.Add(low);
        Content = new Border { Background = Scene.B("#0E0E0D"), Child = root };
    }

    private static Border Tile(string label, Control value, string unit) => new()
    {
        Background = Scene.B("#80FFFFFF"),
        BorderBrush = Scene.B("#A6FFFFFF"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(18),
        Padding = new Thickness(16, 14),
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Scene.Text(label, 12, "#5B5750"),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { value, new TextBlock { Text = unit, FontFamily = Scene.Geist, FontSize = 13, Foreground = Scene.B("#5B5750"), VerticalAlignment = VerticalAlignment.Bottom } } },
            },
        },
    };

    private static Border Panel(Control child) => new()
    {
        Background = Scene.B("#171716"),
        CornerRadius = new CornerRadius(24),
        Padding = new Thickness(20, 18),
        Child = child,
    };

    private static Control Header(string title, TextBlock? count)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var text = Scene.Text(title, 20, "#F3F2EE");
        text.LetterSpacing = -0.6;
        row.Children.Add(text);
        if (count != null)
        {
            row.Children.Add(new Border { Background = Scene.B("#232321"), CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center, Child = count });
        }
        return row;
    }

    private Button Segment(string text, Action click)
    {
        var button = Scene.Button(Scene.Text(text, 13, "#8E8C86"), null, "#8E8C86", 9, new Thickness(14, 7));
        button.Click += (_, _) => click();
        return button;
    }

    private static void SetSegment(Button button, bool on)
    {
        if (button.Content is Border { Child: TextBlock label } border)
        {
            border.Background = on ? Scene.B("#F3F2EE") : Brushes.Transparent;
            label.Foreground = Scene.B(on ? "#121211" : "#8E8C86");
        }
    }

    public override void Update(EditionSnapshot s)
    {
        _last = s;
        _flag.Source = V2pnConverters.Flag(s.CountryCode);
        _title.Text = s.Title;
        _subtitle.Text = s.Subscription.IsNotEmpty() ? $"{s.Subtitle} · {s.Subscription}" : s.Subtitle;
        _stateText.Text = s.Protected ? "Работает" : s.StateLabel;
        _stateDot.Background = Scene.B(s.StateKind switch { "safe" => "#3DD68C", "warn" => "#E9B04F", "checking" => "#9B9BA8", _ => "#FF5A3D" });
        _nodes.Text = NodesText(s);
        _meter.Set(s.NodesOk, s.NodesTotal);
        _stateNote.Text = s.NodesTotal > s.NodesOk && s.NodesTotal > 0
            ? $"{s.NodesTotal - s.NodesOk} не ответили на последнюю проверку пинга."
            : s.NodesTotal > 0 ? "Все серверы ответили на последнюю проверку." : "Проверьте пинг, чтобы узнать, какие серверы отвечают.";
        _connectLabel.Text = s.ConnectLabel;
        Scene.SetPill(_pill, _pillText, s.StateKind, s.StateLabel);
        _ping.Text = s.Ping > 0 ? s.Ping.ToString(CultureInfo.InvariantCulture) : "—";
        _count.Text = s.NodesTotal > 0 ? $"{s.NodesOk}/{s.NodesTotal}" : "—";
        _route.Text = s.Route;
        SetSegment(_proxy, !s.Vpn);
        SetSegment(_vpn, s.Vpn);
        _serversCount.Text = s.Servers.Count.ToString(CultureInfo.CurrentCulture);
        _change.IsEnabled = s.Servers.Count > 0;

        _servers.Children.Clear();
        foreach (var server in s.Servers.Take(8))
        {
            var row = new Grid { ColumnDefinitions = new("22,*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, Width = 22, Height = 16, Child = Scene.Flag(server.Code, 22, 16) });
            var name = Scene.Text(server.Name, 13, "#DAD7D0");
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var ping = Scene.Text(server.Delay > 0 ? $"{server.Delay} мс" : "—", 12, server.Delay is > 0 and < 150 ? "#5BD08F" : server.Delay > 0 ? "#E9B04F" : "#6E6C66", Scene.Mono);
            Grid.SetColumn(ping, 2);
            row.Children.Add(ping);
            var button = Scene.Button(row, server.Selected ? "#232321" : null, "#DAD7D0", 10, new Thickness(10, 7), server.Selected ? "#59FF7A2F" : null);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var id = server.IndexId;
            button.Click += (_, _) => Actions.Select(id);
            _servers.Children.Add(button);
        }
    }
}

/// <summary>
/// The studio look (Configra's website): studio grey with a dot field, the place in bitmap letters, a dark capsule
/// with the one orange action, and the partner's lamp, lit while protected.
/// </summary>
public sealed class StudioScene : EditionScene
{
    private readonly TextBlock _pillText = new();
    private readonly Border _pill;
    private readonly MatrixText _word = new() { Square = true, Cell = 9, Foreground = Scene.B("#121211") };
    private readonly Image _flag = Scene.Flag(null, 22, 16);
    private readonly TextBlock _sub = Scene.Text("", 15, "#3E3C38");
    private readonly Image _lamp = new() { Stretch = Stretch.Uniform, MaxHeight = 360 };
    private readonly TextBlock _connectLabel = Scene.Text("", 13, "#170B03", weight: FontWeight.Medium);
    private readonly Button _proxy;
    private readonly Button _vpn;
    private readonly Button _change;
    private readonly TextBlock _nodesKey = Scene.Text("", 12, "#6A6862", Scene.Mono);
    private readonly TextBlock _nodesTitle = Scene.Text("", 18, "#121211");
    private readonly NodeMeter _meter = new(11, "#FF7A2F", "#CFCDC8");
    private readonly TextBlock _modeKey = Scene.Text("", 12, "#6A6862", Scene.Mono);
    private readonly TextBlock _modeTitle = Scene.Text("", 18, "#121211");
    private readonly TextBlock _modeNote = Scene.Text("", 12.5, "#5D5B56");
    private readonly TextBlock _routeTitle = Scene.Text("", 18, "#121211");
    private EditionSnapshot? _last;

    public StudioScene(V2pnPartner partner, IEditionActions actions) : base(partner, actions)
    {
        _pill = Scene.Pill(_pillText, light: true);
        _lamp.Source = Scene.Picture(partner, partner.Pack.Art.GetValueOrDefault("lamp"));
        _lamp.IsVisible = _lamp.Source != null;

        _proxy = Capsule("Прокси", () => Actions.SetVpn(false));
        _vpn = Capsule("VPN", () => Actions.SetVpn(true));
        var connect = Scene.Button(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Scene.Power("#170B03"), _connectLabel } }, "#FF7A2F", "#170B03", 999, new Thickness(18, 9));
        connect.Click += (_, _) => Actions.ToggleConnect();
        var capsule = new Border
        {
            Background = Scene.B("#1A1918"),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(5),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _proxy, _vpn, connect } },
        };
        _change = Scene.Button(Scene.Text("Сменить сервер", 13, "#121211", weight: FontWeight.Medium), null, "#121211", 999, new Thickness(18, 10), "#59121211");
        _change.HorizontalAlignment = HorizontalAlignment.Right;
        _change.Click += (_, _) => ShowServers(_change, _last);
        var nav = new Grid { ColumnDefinitions = new("*,Auto,*") };
        nav.Children.Add(new Border { Child = _pill, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(capsule, 1);
        nav.Children.Add(capsule);
        Grid.SetColumn(_change, 2);
        nav.Children.Add(_change);

        var heroLeft = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 14,
            Children =
            {
                Scene.Text("сейчас вы в", 13, "#5D5B56", Scene.Mono),
                new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Child = _word },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0), Children = { new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = _flag }, _sub } },
            },
        };
        var hero = new Grid { ColumnDefinitions = new("*,360"), ColumnSpacing = 10 };
        hero.Children.Add(heroLeft);
        Grid.SetColumn(_lamp, 1);
        hero.Children.Add(_lamp);

        var routeCard = Card(_routeTitle, Scene.Text("банки и Госуслуги — напрямую", 12.5, "#5D5B56"), Scene.Text("03 · через VPN", 12, "#6A6862", Scene.Mono), Scene.Text("изменить ▾", 12, "#6A6862", Scene.Mono));
        var routeButton = Scene.Button(routeCard, null, "#121211", 18, new Thickness(0));
        routeButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        routeButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        routeButton.Click += (_, _) => Actions.OpenRoute(routeButton);
        var cards = new UniformGrid { Columns = 3, ColumnSpacing = 10 };
        cards.Children.Add(Card(_nodesTitle, _meter, Scene.Text("01 · серверы", 12, "#6A6862", Scene.Mono), _nodesKey));
        cards.Children.Add(Card(_modeTitle, _modeNote, Scene.Text("02 · режим", 12, "#6A6862", Scene.Mono), _modeKey));
        cards.Children.Add(routeButton);

        var body = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(26, 6, 26, 22) };
        body.Children.Add(nav);
        Grid.SetRow(hero, 1);
        body.Children.Add(hero);
        Grid.SetRow(cards, 2);
        body.Children.Add(cards);
        Content = new Panel { Background = Scene.B("#E2E2E0"), Children = { new DotField("#22121211", 14), body } };
    }

    private Button Capsule(string text, Action click)
    {
        var button = Scene.Button(Scene.Text(text, 13, "#9C9A94", weight: FontWeight.Medium), null, "#9C9A94", 999, new Thickness(14, 8));
        button.Click += (_, _) => click();
        return button;
    }

    private static Border Card(Control title, Control detail, TextBlock key, TextBlock value)
    {
        var top = new Grid { ColumnDefinitions = new("*,Auto") };
        top.Children.Add(key);
        Grid.SetColumn(value, 1);
        top.Children.Add(value);
        return new Border
        {
            Background = Scene.B("#94FFFFFF"),
            BorderBrush = Scene.B("#CCFFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 14),
            Child = new StackPanel { Spacing = 8, Children = { top, title, detail } },
        };
    }

    public override void Update(EditionSnapshot s)
    {
        _last = s;
        Scene.SetPill(_pill, _pillText, s.StateKind, s.StateLabel);
        _word.Text = s.Title.ToUpper(CultureInfo.CurrentUICulture);
        _flag.Source = V2pnConverters.Flag(s.CountryCode);
        _sub.Text = string.Join(" · ", new[] { s.Subtitle, s.Subscription, s.Ping > 0 ? $"{s.Ping} мс" : null }.Where(t => t.IsNotEmpty()));
        // The lamp is lit while the connection protects; dim otherwise, so the picture says the state too.
        _lamp.Opacity = s.Protected ? 1 : 0.35;
        _connectLabel.Text = s.ConnectLabel;
        foreach (var (button, on) in new[] { (_proxy, !s.Vpn), (_vpn, s.Vpn) })
        {
            if (button.Content is Border { Child: TextBlock label } border)
            {
                border.Background = on ? Scene.B("#2E2D2B") : Brushes.Transparent;
                label.Foreground = Scene.B(on ? "#F3F2EE" : "#9C9A94");
            }
        }
        _change.IsEnabled = s.Servers.Count > 0;
        _nodesKey.Text = s.NodesTotal > 0 ? $"{s.NodesOk}/{s.NodesTotal}" : "—";
        _nodesTitle.Text = s.NodesTotal > 0 ? $"Отвечают {NodesText(s)}" : "Пинг не проверялся";
        _meter.Set(s.NodesOk, s.NodesTotal);
        _modeKey.Text = s.Vpn ? "vpn" : "прокси";
        _modeTitle.Text = s.Vpn ? "Весь компьютер" : "Браузеры и программы";
        _modeNote.Text = s.Vpn ? "весь трафик через сервер" : "с системным прокси";
        _routeTitle.Text = s.Route;
    }
}

/// <summary>The ASCII look: monospace text all the way, the map typed in characters, buttons in brackets.</summary>
public sealed class AsciiScene : EditionScene
{
    private readonly TextBlock _map = new() { FontFamily = Scene.Mono, FontSize = 12, LineHeight = 13, Foreground = Scene.B("#4A4843") };
    private readonly TextBlock _pillText = new();
    private readonly Border _pill;
    private readonly MatrixText _ping = new() { Cell = 8, Foreground = Scene.B("#F3F2EE") };
    private readonly TextBlock _pingNote = Scene.Text("", 12, "#8E8C86", Scene.Mono);
    private readonly TextBlock _server = Value();
    private readonly TextBlock _nodes = Value();
    private readonly TextBlock _mode = Value();
    private readonly TextBlock _route = Value();
    private readonly TextBlock _connectLabel = Scene.Text("", 12.5, "#121211", Scene.Mono);
    private readonly StackPanel _list = new() { Spacing = 4 };
    private string? _mapFor;
    private EditionSnapshot? _last;

    public AsciiScene(V2pnPartner partner, IEditionActions actions) : base(partner, actions)
    {
        _pill = Scene.Pill(_pillText, light: false);
        var mapBox = new Border
        {
            BorderBrush = Scene.B("#2A2926"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 14),
            ClipToBounds = true,
            Child = new DockPanel
            {
                Children =
                {
                    new TextBlock { Text = "карта V2pn · @ ваш сервер · * другие серверы", FontFamily = Scene.Mono, FontSize = 11, Foreground = Scene.B("#6E6C66"), [DockPanel.DockProperty] = Dock.Bottom },
                    _map,
                },
            },
        };

        var kv = new Grid { ColumnDefinitions = new("110,*"), RowDefinitions = new("Auto,Auto,Auto,Auto"), RowSpacing = 8 };
        void Row(int r, string key, Control value)
        {
            var k = Scene.Text(key, 12, "#6E6C66", Scene.Mono);
            k.LetterSpacing = 0.8;
            Grid.SetRow(k, r);
            kv.Children.Add(k);
            Grid.SetRow(value, r);
            Grid.SetColumn(value, 1);
            kv.Children.Add(value);
        }
        var modeButton = Scene.Button(_mode, null, "#E9E6DF", 2, new Thickness(0));
        modeButton.HorizontalAlignment = HorizontalAlignment.Left;
        modeButton.Click += (_, _) => Actions.SetVpn(_last is { Vpn: false });
        var routeButton = Scene.Button(_route, null, "#E9E6DF", 2, new Thickness(0));
        routeButton.HorizontalAlignment = HorizontalAlignment.Left;
        routeButton.Click += (_, _) => Actions.OpenRoute(routeButton);
        Row(0, "СЕРВЕР", _server);
        Row(1, "СЕРВЕРЫ", _nodes);
        Row(2, "РЕЖИМ", modeButton);
        Row(3, "ЧЕРЕЗ VPN", routeButton);

        var connect = Scene.Button(_connectLabel, "#F3F2EE", "#121211", 2, new Thickness(14, 9));
        connect.Click += (_, _) => Actions.ToggleConnect();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { connect } };

        var side = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                _pill,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _ping, _pingNote } },
                kv,
                buttons,
                new Border { BorderBrush = Scene.B("#2E2D29"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 0), Child = _list },
            },
        };
        var root = new Grid { ColumnDefinitions = new("1.15*,*"), ColumnSpacing = 18, Margin = new Thickness(22, 4, 22, 20) };
        root.Children.Add(mapBox);
        Grid.SetColumn(side, 1);
        root.Children.Add(side);
        Content = new Border { Background = Scene.B("#121211"), Child = root };
    }

    private static TextBlock Value() => Scene.Text("", 12.5, "#E9E6DF", Scene.Mono);

    public override void Update(EditionSnapshot s)
    {
        _last = s;
        Scene.SetPill(_pill, _pillText, s.StateKind, s.StateLabel);
        _ping.Text = s.Ping > 0 ? s.Ping.ToString(CultureInfo.InvariantCulture) : "—";
        _pingNote.Text = s.Ping > 0 ? $"мс до {s.Subtitle}" : s.Subtitle;
        _server.Text = s.Subscription.IsNotEmpty() ? $"{s.Subtitle} · {s.Subscription}" : s.Subtitle;
        var lit = s.NodesTotal > 0 ? (int)Math.Round(s.NodesOk * 14.0 / s.NodesTotal) : 0;
        _nodes.Text = s.NodesTotal > 0 ? $"{new string('■', lit)}{new string('□', 14 - lit)}  {s.NodesOk}/{s.NodesTotal}" : "пинг не проверялся";
        _mode.Text = s.Vpn ? "( ) прокси  (•) vpn" : "(•) прокси  ( ) vpn";
        _route.Text = $"{s.Route.ToLower(CultureInfo.CurrentUICulture)}  [изменить]";
        _connectLabel.Text = $"[ {s.ConnectLabel.ToLower(CultureInfo.CurrentUICulture)} ]";

        var centre = s.CountryCode ?? "DE";
        // Rebuilt when the centre or the set of countries changes: the first refresh comes before the servers load.
        var mapKey = centre + ":" + string.Join(",", s.Servers.Where(x => !x.Selected).Select(x => x.Code).Distinct().Order());
        if (_mapFor != mapKey)
        {
            _mapFor = mapKey;
            var lines = AsciiMap.Lines(centre).Select(l => l.ToCharArray()).ToArray();
            foreach (var server in s.Servers.Where(x => !x.Selected))
            {
                if (AsciiMap.Place(centre, server.Code) is { } at)
                {
                    lines[at.Row][at.Column] = '*';
                }
            }
            var inlines = new InlineCollection();
            var here = AsciiMap.Place(centre, centre);
            for (var r = 0; r < lines.Length; r++)
            {
                var text = new string(lines[r]);
                if (here is { } h && h.Row == r)
                {
                    inlines.Add(new Run(text[..h.Column]));
                    inlines.Add(new Run("@") { Foreground = Scene.B("#3DD68C") });
                    inlines.Add(new Run(text[(h.Column + 1)..]));
                }
                else
                {
                    // The partner's other servers in its accent, the land in the ink of the page.
                    var parts = text.Split('*');
                    for (var i = 0; i < parts.Length; i++)
                    {
                        if (i > 0)
                        {
                            inlines.Add(new Run("*") { Foreground = Scene.B("#FF7A2F") });
                        }
                        inlines.Add(new Run(parts[i]));
                    }
                }
                if (r < lines.Length - 1)
                {
                    inlines.Add(new LineBreak());
                }
            }
            _map.Inlines = inlines;
        }

        _list.Children.Clear();
        foreach (var server in s.Servers.Take(6))
        {
            var row = new Grid { ColumnDefinitions = new("16,*,64") };
            row.Children.Add(Scene.Text(server.Selected ? ">" : "", 12, "#FF7A2F", Scene.Mono));
            var name = Scene.Text(server.Name, 12, server.Selected ? "#F3F2EE" : "#8E8C86", Scene.Mono);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var ping = Scene.Text(server.Delay > 0 ? $"{server.Delay} мс" : "—", 12, server.Selected ? "#F3F2EE" : "#8E8C86", Scene.Mono);
            ping.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(ping, 2);
            row.Children.Add(ping);
            var button = Scene.Button(row, null, "#8E8C86", 2, new Thickness(0, 2));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var id = server.IndexId;
            button.Click += (_, _) => Actions.Select(id);
            _list.Children.Add(button);
        }
    }
}
