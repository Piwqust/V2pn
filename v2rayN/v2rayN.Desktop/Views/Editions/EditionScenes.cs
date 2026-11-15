using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using v2rayN.Desktop.Converters;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// A partner look that replaces the sidebar and the map: it reads a snapshot and calls back into the window.
/// It follows its own width the way the plain window does: wide, medium (one column less) and narrow (one
/// column, the list under the controls), and scrolls as a whole when the window is too short. It is built for
/// one <see cref="SceneLook"/> (light or dark, accent, corners, type) and rebuilt when the person changes it.
/// </summary>
public abstract class EditionScene : UserControl
{
    protected enum Fit { Narrow, Medium, Wide }

    private const double WideMin = 980;
    private const double MediumMin = 700;
    private Fit? _fit;

    protected EditionScene(V2pnPartner partner, IEditionActions actions, SceneLook look)
    {
        Partner = partner;
        Actions = actions;
        Look = look;
        Scene.AddButtonStyles(this);
        SizeChanged += (_, e) =>
        {
            var width = e.NewSize.Width;
            var fit = width >= WideMin ? Fit.Wide : width >= MediumMin ? Fit.Medium : Fit.Narrow;
            if (width > 0 && fit != _fit)
            {
                _fit = fit;
                Reflow(fit);
            }
        };
    }

    protected V2pnPartner Partner { get; }
    protected IEditionActions Actions { get; }
    protected SceneLook Look { get; }

    /// <summary>The layout in use; wide until the first measure.</summary>
    protected Fit CurrentFit => _fit ?? Fit.Wide;

    /// <summary>Ping colours that read on this look's ground.</summary>
    protected (string Good, string Warn, string Bad) PingColours =>
        Look.Dark ? ("#5BD08F", "#E9B04F", "#E5684F") : ("#16804F", "#B07508", "#C8381F");

    public abstract void Update(EditionSnapshot s);

    /// <summary>Puts the look's parts into the layout for this width. The parts themselves are kept.</summary>
    protected abstract void Reflow(Fit fit);

    /// <summary>A flyout of the servers the snapshot lists, for "Change server".</summary>
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
                Header = server.Delay > 0 ? $"{server.Name}   {server.Delay} {L.SceneMs}" : server.Name,
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
        s.NodesTotal > 0 ? string.Format(L.SceneOf, s.NodesOk, s.NodesTotal) : "—";

    /// <summary>Takes a part out of the layout it was in, so the next layout can hold it.</summary>
    protected static T Take<T>(T part) where T : Control
    {
        switch (part.Parent)
        {
            case Panel panel:
                panel.Children.Remove(part);
                break;
            case Decorator decorator when decorator.Child == part:
                decorator.Child = null;
                break;
            case ContentControl host when host.Content == part:
                host.Content = null;
                break;
            case Viewbox box when box.Child == part:
                box.Child = null;
                break;
        }
        return part;
    }

    protected static void Put(Grid grid, Control part, int row, int column, int columnSpan = 1) =>
        ServerBrowser.Put(grid, Take(part), row, column, columnSpan);

    /// <summary>The window's IP card and settings, as quiet links.</summary>
    protected StackPanel Extras(string color, bool ascii)
    {
        Button Link(string text, Action click)
        {
            var button = Scene.Button(Scene.Text(text, 12.5, color), null, color, 6, new Thickness(ascii ? 0 : 2, 4));
            button.Click += (_, _) => click();
            return button;
        }
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = ascii ? 14 : 16,
            Children =
            {
                Link(ascii ? "[ ip ]" : L.SceneIp + " ↗", Actions.OpenIp),
                Link(ascii ? L.SceneSettingsAscii : L.SceneSettings, Actions.OpenSettings),
            },
        };
    }

    /// <summary>The server list, or how to add the first server when there is none yet.</summary>
    protected static void ShowListOrEmpty(ContentControl host, ServerBrowser browser, EmptyState empty, EditionSnapshot s)
    {
        browser.Update(s);
        empty.Update(s);
        Control body = s.AnyServers ? browser : empty;
        if (host.Content != body)
        {
            host.Content = body;
        }
    }
}

/// <summary>
/// The cabinet look (Configra's personal cabinet): a peach card with the place, the state and the node squares;
/// below, the servers and what goes through the VPN, on dark panels or, in the light mode, paper ones.
/// </summary>
public sealed class CabinetScene : EditionScene
{
    private sealed record Ink(string Ground, string Panel, string Raised, string Text, string Soft, string Muted, string Doodle, string SegmentOn, string SegmentOnText);

    private static readonly Ink DarkInk = new("#0E0E0D", "#171716", "#232321", "#F3F2EE", "#DAD7D0", "#8E8C86", "#6A6862", "#F3F2EE", "#121211");
    private static readonly Ink LightInk = new("#E7E5E1", "#F7F6F3", "#ECEAE5", "#121211", "#3E3C38", "#6A6862", "#A19E97", "#121211", "#F3F2EE");

    private readonly Ink _c;
    private readonly Image _flag = Scene.Flag(null, 34, 24);
    // A server whose country V2pn does not know (a group, a host name): a globe, as in the plain list.
    private readonly PathIcon _globe = Scene.Globe("#5B5750", 26);
    private readonly TextBlock _title = Scene.Text("", 44, "#121211");
    private readonly TextBlock _subtitle = Scene.Text("", 13, "#55524C");
    private readonly TextBlock _stateText = Scene.Text("", 18, "#121211");
    private readonly TextBlock _nodes = Scene.Text("", 18, "#121211");
    private readonly TextBlock _stateNote = Scene.Text("", 12.5, "#5B5750");
    private readonly NodeMeter _meter;
    private readonly Border _stateDot = new() { Width = 10, Height = 10 };
    private readonly TextBlock _connectLabel = Scene.Text("", 14, "#F3F2EE", weight: FontWeight.Medium);
    private readonly TextBlock _pillText = new();
    private readonly Border _pill;
    private readonly MatrixText _ping = new() { Cell = 5.6, Foreground = Scene.B("#121211") };
    private readonly MatrixText _count = new() { Cell = 4.2, Foreground = Scene.B("#121211") };
    private readonly TextBlock _serversCount;
    private readonly TextBlock _route;
    private readonly TextBlock _countUnit = new() { Text = L.SceneServersUnit, FontSize = 13, Foreground = Scene.B("#5B5750"), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _changeLabel = Scene.Text(L.SceneChange, 14, "#121211", weight: FontWeight.Medium);
    private readonly Button _proxy;
    private readonly Button _vpn;
    private readonly Button _change;
    private readonly Button _connect;
    private readonly Button _routeButton;
    private readonly Border _avatar;
    private readonly StackPanel _who;
    private readonly Border _state;
    private readonly Border _pingTile;
    private readonly Border _countTile;
    private readonly Border _hero;
    private readonly Border _segment;
    private readonly Grid _modes = new();
    private readonly TextBlock _doodle;
    private readonly Border _serversPanel;
    private readonly Border _vpnPanel;
    private readonly ServerBrowser _browser;
    private readonly EmptyState _empty;
    private readonly ContentControl _serversBody = new();
    private readonly ThemeVariantScope _frame;
    private readonly AccountPanel _account;
    private readonly ForeignNote _foreign;
    private EditionSnapshot? _last;

    public CabinetScene(V2pnPartner partner, IEditionActions actions, SceneLook look) : base(partner, actions, look)
    {
        _c = look.Dark ? DarkInk : LightInk;
        _frame = new ThemeVariantScope { RequestedThemeVariant = look.Dark ? ThemeVariant.Dark : ThemeVariant.Light };
        _meter = new NodeMeter(11, look.Accent, "#CFC8BF");
        _serversCount = Scene.Text("", 12, _c.Muted);
        _route = Scene.Text("", 13, _c.Soft);
        _countUnit.FontFamily = look.Text;
        _title.FontWeight = FontWeight.Normal;
        _stateDot.CornerRadius = look.R(2);
        _pill = Scene.Pill(_pillText, light: true);

        _avatar = new Border
        {
            Width = 62,
            Height = 62,
            Background = Scene.B("#F7F6F3"),
            Child = new Panel
            {
                Children =
                {
                    new Border { CornerRadius = look.R(4), ClipToBounds = true, Width = 34, Height = 24, Child = _flag },
                    _globe,
                },
            },
        };
        _who = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Children = { _avatar, new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _subtitle } } },
        };

        // The state can be a long sentence ("proxy on port 20828"); it trims before it reaches the count.
        var stateRow = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 9 };
        stateRow.Children.Add(_stateDot);
        Put(stateRow, _stateText, 0, 1);
        Put(stateRow, _nodes, 0, 2);
        _stateNote.TextWrapping = TextWrapping.Wrap;
        _stateNote.TextTrimming = TextTrimming.None;
        _state = new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Background = Scene.B("#80FFFFFF"),
            BorderBrush = Scene.B("#A6FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = look.R(18),
            Padding = new Thickness(16, 12),
            Child = new StackPanel { Spacing = 9, Children = { stateRow, _meter, _stateNote } },
        };

        _connect = Scene.Button(
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Children = { Scene.Power(look.Accent), _connectLabel } },
            "#1B1715", "#F3F2EE", 14, new Thickness(22, 0), height: 48);
        _connect.Click += (_, _) => Actions.ToggleConnect();
        _change = Scene.Button(_changeLabel, null, "#121211", 14, new Thickness(22, 0), "#40121211", height: 48);
        _change.Click += (_, _) => ShowServers(_change, _last);

        _pingTile = Tile(L.SceneLatency, _ping, new TextBlock { Text = L.SceneMs, FontFamily = look.Text, FontSize = 13, Foreground = Scene.B("#5B5750"), VerticalAlignment = VerticalAlignment.Bottom });
        _countTile = Tile(L.SceneAnswering, _count, _countUnit);
        _hero = new Border
        {
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
        };

        _proxy = Segment(L.SceneProxy, () => Actions.SetVpn(false));
        _vpn = Segment("VPN", () => Actions.SetVpn(true));
        var segmentGrid = new Grid { ColumnDefinitions = new("*,*") };
        Put(segmentGrid, _proxy, 0, 0);
        Put(segmentGrid, _vpn, 0, 1);
        _segment = new Border { Background = Scene.B(_c.Raised), CornerRadius = look.R(12), Padding = new Thickness(3), Child = segmentGrid };
        _routeButton = Scene.Button(
            new Grid
            {
                ColumnDefinitions = new("*,Auto"),
                Children = { _route, new TextBlock { Text = "▾", Foreground = Scene.B(_c.Muted), [Grid.ColumnProperty] = 1, VerticalAlignment = VerticalAlignment.Center } },
            },
            _c.Raised, _c.Soft, 12, new Thickness(14, 0), height: 41);
        _routeButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _routeButton.Click += (_, _) => Actions.OpenRoute(_routeButton);

        _doodle = new TextBlock
        {
            FontFamily = Scene.Mono,
            FontSize = 11,
            LineHeight = 12.5,
            Foreground = Scene.B(_c.Doodle),
            Text = L.SceneDoodle,
        };
        var note = new TextBlock
        {
            Text = string.Format(L.SceneTrafficNote, partner.Name),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = look.Text,
            FontSize = 12,
            Foreground = Scene.B(_c.Muted),
        };
        var (good, warn, bad) = PingColours;
        var palette = new ScenePalette(_c.Panel, _c.Raised, _c.Text, _c.Soft, _c.Muted, look.Accent, Scene.OnAccent(look.Accent), 12, look.Text, Good: good, Warn: warn, Bad: bad);
        _account = new AccountPanel(palette, actions, string.Format(L.SceneSubscriptionOf, partner.Name));
        _vpnPanel = Panel(new StackPanel { Spacing = 14, Children = { Header(L.SceneThroughVpn, null), _modes, _account, _doodle, note, Extras(_c.Soft, ascii: false) } });
        // On the peach card: ink, not the accent, so the way back to the partner's servers reads on any accent.
        _foreign = new ForeignNote(new ScenePalette("#F7F6F3", "#E8E3DC", "#121211", "#3E3C38", "#5B5750", "#121211", "#F3F2EE", 12, look.Text), actions);
        _foreign.Margin = new Thickness(2, 10, 0, 0);
        _browser = new ServerBrowser(palette, actions);
        _empty = new EmptyState(palette, actions, partner.Name);
        var serversGrid = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12 };
        Put(serversGrid, Header(L.SceneServers, _serversCount), 0, 0);
        Put(serversGrid, _serversBody, 1, 0);
        _serversPanel = Panel(serversGrid);

        Content = new Border { Background = Scene.B(_c.Ground), Child = _frame };
        Reflow(Fit.Wide);
    }

    protected override void Reflow(Fit fit)
    {
        var wide = fit == Fit.Wide;
        var narrow = fit == Fit.Narrow;
        _title.FontSize = wide ? 44 : narrow ? 30 : 36;
        _title.LetterSpacing = narrow ? -1 : -1.6;
        _avatar.Width = _avatar.Height = narrow ? 50 : 62;
        _avatar.CornerRadius = Look.R(narrow ? 13 : 16);
        _ping.Cell = wide ? 5.6 : narrow ? 3.6 : 4.4;
        _count.Cell = wide ? 4.2 : narrow ? 3.2 : 3.6;
        _connect.MinWidth = narrow ? 0 : 200;
        // Two tiles and two buttons share a narrow card: the count says "servers" in its label already.
        _countUnit.IsVisible = !narrow;
        _changeLabel.Text = narrow ? L.SceneChangeShort : L.SceneChange;
        _doodle.IsVisible = wide;
        _state.MaxWidth = narrow ? double.PositiveInfinity : 560;
        _state.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        _hero.CornerRadius = Look.R(narrow ? 22 : 26);
        _hero.Padding = narrow ? new Thickness(18, 16) : new Thickness(26, 22);

        // Mode and route side by side where the panel is wide enough, else the route under the mode.
        _modes.Children.Clear();
        _modes.ColumnDefinitions = new(wide ? "Auto,*" : "*");
        _modes.RowDefinitions = new(wide ? "Auto" : "Auto,Auto");
        _modes.ColumnSpacing = 10;
        _modes.RowSpacing = 8;
        _segment.HorizontalAlignment = wide ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        Put(_modes, _segment, 0, 0);
        Put(_modes, _routeButton, wide ? 0 : 1, wide ? 1 : 0);

        var actions = new Grid { ColumnDefinitions = new(narrow ? "*,*" : "Auto,Auto"), ColumnSpacing = 10, Margin = new Thickness(0, 14, 0, 0) };
        Put(actions, _connect, 0, 0);
        Put(actions, _change, 0, 1);
        var pill = new Border { Child = Take(_pill), HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right };

        if (narrow)
        {
            var tiles = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 10, Margin = new Thickness(0, 12, 0, 0) };
            Put(tiles, _pingTile, 0, 0);
            Put(tiles, _countTile, 0, 1);
            _who.Margin = new Thickness(0, 12, 0, 0);
            _hero.Child = new StackPanel { Children = { pill, Take(_who), Take(_state), Take(_foreign), tiles, actions } };
        }
        else
        {
            _who.Margin = default;
            var grid = new Grid { ColumnDefinitions = new(wide ? "*,300" : "*,230"), ColumnSpacing = 20 };
            ServerBrowser.Put(grid, new StackPanel { Children = { Take(_who), Take(_state), Take(_foreign), actions } }, 0, 0);
            ServerBrowser.Put(grid, new StackPanel { Spacing = 10, Children = { pill, Take(_pingTile), Take(_countTile) } }, 0, 1);
            _hero.Child = grid;
        }

        var column = new SceneColumn(10, narrow ? 360 : 300, default);
        column.Parts.Add(Take(_hero));
        if (narrow)
        {
            column.Parts.Add(Take(_vpnPanel));
            column.Parts.Add(Take(_serversPanel));
        }
        else
        {
            var low = new Grid { ColumnDefinitions = new(wide ? "1.15*,*" : "*,290"), ColumnSpacing = 10 };
            Put(low, _serversPanel, 0, 0);
            Put(low, _vpnPanel, 0, 1);
            column.Parts.Add(low);
        }
        _vpnPanel.Padding = narrow ? new Thickness(16, 14) : new Thickness(20, 18);
        _serversPanel.Padding = _vpnPanel.Padding;
        _frame.Child = column;
    }

    private Border Tile(string label, Control value, TextBlock unit) => new()
    {
        Background = Scene.B("#80FFFFFF"),
        BorderBrush = Scene.B("#A6FFFFFF"),
        BorderThickness = new Thickness(1),
        CornerRadius = Look.R(18),
        Padding = new Thickness(16, 14),
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Scene.Text(label, 12, "#5B5750"),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { value, unit } },
            },
        },
    };

    private Border Panel(Control child) => new()
    {
        Background = Scene.B(_c.Panel),
        CornerRadius = Look.R(24),
        Padding = new Thickness(20, 18),
        Child = child,
    };

    private Control Header(string title, TextBlock? count)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var text = Scene.Text(title, 20, _c.Text);
        text.LetterSpacing = -0.6;
        row.Children.Add(text);
        if (count != null)
        {
            row.Children.Add(new Border { Background = Scene.B(_c.Raised), CornerRadius = Look.R(999), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center, Child = count });
        }
        return row;
    }

    private Button Segment(string text, Action click)
    {
        var button = Scene.Button(Scene.Text(text, 13, _c.Muted), null, _c.Muted, 9, new Thickness(16, 8));
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Click += (_, _) => click();
        return button;
    }

    private void SetSegment(Button button, bool on)
    {
        if (button.Content is Border { Child: TextBlock label } border)
        {
            border.Background = on ? Scene.B(_c.SegmentOn) : Brushes.Transparent;
            label.Foreground = Scene.B(on ? _c.SegmentOnText : _c.Muted);
        }
    }

    public override void Update(EditionSnapshot s)
    {
        _last = s;
        _flag.Source = V2pnConverters.Flag(s.CountryCode);
        _flag.IsVisible = _flag.Source != null;
        _globe.IsVisible = _flag.Source == null;
        _title.Text = s.Title;
        _subtitle.Text = s.Subscription.IsNotEmpty() ? $"{s.Subtitle} · {s.Subscription}" : s.Subtitle;
        _stateText.Text = s.Protected ? L.SceneWorking : s.StateLabel;
        _stateDot.Background = Scene.B(s.StateKind switch { "safe" => "#3DD68C", "warn" => "#E9B04F", "checking" => "#9B9BA8", _ => "#FF5A3D" });
        _nodes.Text = NodesText(s);
        _meter.Set(s.NodesOk, s.NodesTotal);
        _stateNote.Text = s.NodesByPartner
            ? s.NodesTotal > s.NodesOk
                ? string.Format(L.SceneDownByPartner, s.NodesTotal - s.NodesOk, s.PartnerName)
                : string.Format(L.SceneAllUpByPartner, s.PartnerName)
            : s.NodesTotal > s.NodesOk && s.NodesTotal > 0
                ? string.Format(L.SceneDownByPing, s.NodesTotal - s.NodesOk)
                : s.NodesTotal > 0 ? L.SceneAllUpByPing : L.ScenePingFirst;
        _connectLabel.Text = s.ConnectLabel;
        _connect.IsEnabled = s.HasServer;
        Scene.SetPill(_pill, _pillText, s.StateKind, s.StateLabel);
        _ping.Text = s.Ping > 0 ? s.Ping.ToString(CultureInfo.InvariantCulture) : "—";
        _count.Text = s.NodesTotal > 0 ? $"{s.NodesOk}/{s.NodesTotal}" : "—";
        _route.Text = s.Route;
        SetSegment(_proxy, !s.Vpn);
        SetSegment(_vpn, s.Vpn);
        _serversCount.Text = s.Catalog.Count.ToString(CultureInfo.CurrentCulture);
        _change.IsEnabled = s.Servers.Count > 0;
        _account.Update(s.Account);
        _foreign.Update(s);
        ShowListOrEmpty(_serversBody, _browser, _empty, s);
    }
}

/// <summary>
/// The studio look (Configra's website): studio grey (or, in the dark mode, a dark studio) with a dot field, the place
/// in bitmap letters, a capsule with the one accent action, and the partner's lamp, lit while protected. It reads
/// like a page and scrolls like one.
/// </summary>
public sealed class StudioScene : EditionScene
{
    private sealed record Ink(
        string Ground, string Dots, string Text, string Soft, string Muted, string Note, string Card, string CardLine,
        string Capsule, string CapsuleOff, string CapsuleOn, string CapsuleOnText, string ChangeLine, string MeterOff, string Panel, string Raised);

    private static readonly Ink LightInk = new("#E2E2E0", "#22121211", "#121211", "#3E3C38", "#6A6862", "#5D5B56", "#94FFFFFF", "#CCFFFFFF",
        "#1A1918", "#9C9A94", "#2E2D2B", "#F3F2EE", "#59121211", "#CFCDC8", "#F4F3F0", "#FFFFFF");
    private static readonly Ink DarkInk = new("#161615", "#1CF3F2EE", "#F3F2EE", "#CFCDC8", "#8E8C86", "#9C9A94", "#12FFFFFF", "#1FFFFFFF",
        "#0B0B0A", "#8E8C86", "#2E2D2B", "#F3F2EE", "#59F3F2EE", "#3A3936", "#1C1B19", "#34332F");

    private readonly Ink _c;
    private readonly TextBlock _pillText = new();
    private readonly Border _pill;
    private readonly MatrixText _word = new() { Square = true, Cell = 9 };
    private readonly Image _flag = Scene.Flag(null, 22, 16);
    private readonly TextBlock _sub;
    private readonly Image _lamp = new() { Stretch = Stretch.Uniform };
    private readonly Control _lampPart;
    private readonly Border _flagBox;
    private readonly TextBlock _connectLabel;
    private readonly Button _proxy;
    private readonly Button _vpn;
    private readonly Button _connect;
    private readonly Button _change;
    private readonly Border _capsule;
    private readonly StackPanel _heroLeft;
    private readonly TextBlock _nodesKey;
    private readonly TextBlock _nodesTitle;
    private readonly NodeMeter _meter;
    private readonly TextBlock _modeKey;
    private readonly TextBlock _modeTitle;
    private readonly TextBlock _modeNote;
    private readonly TextBlock _routeTitle;
    private readonly Border _nodesCard;
    private readonly Border _modeCard;
    private readonly Button _routeButton;
    private readonly TextBlock _serversCount;
    private readonly Border _servers;
    private readonly ServerBrowser _browser;
    private readonly EmptyState _empty;
    private readonly ContentControl _serversBody = new();
    private readonly ContentControl _host = new();
    private readonly AccountPanel _account;
    private readonly Border _accountCard;
    private readonly ForeignNote _foreign;
    private readonly Grid _cards = new() { ColumnSpacing = 10, RowSpacing = 10 };
    private EditionSnapshot? _last;

    public StudioScene(V2pnPartner partner, IEditionActions actions, SceneLook look) : base(partner, actions, look)
    {
        _c = look.Dark ? DarkInk : LightInk;
        _word.Foreground = Scene.B(_c.Text);
        _sub = Scene.Text("", 15, _c.Soft);
        _connectLabel = Scene.Text("", 13, Scene.OnAccent(look.Accent), weight: FontWeight.Medium);
        _nodesKey = Scene.Text("", 12, _c.Muted, Scene.Mono);
        _nodesTitle = Scene.Text("", 18, _c.Text);
        _meter = new NodeMeter(11, look.Accent, _c.MeterOff);
        _modeKey = Scene.Text("", 12, _c.Muted, Scene.Mono);
        _modeTitle = Scene.Text("", 18, _c.Text);
        _modeNote = Scene.Text("", 12.5, _c.Note);
        _routeTitle = Scene.Text("", 18, _c.Text);
        _serversCount = Scene.Text("", 12, _c.Muted, Scene.Mono);
        _pill = Scene.Pill(_pillText, light: !look.Dark);
        _flagBox = new Border { CornerRadius = look.R(3), ClipToBounds = true, Child = _flag };
        _lamp.Source = Scene.Picture(partner, partner.Pack.Art.GetValueOrDefault("lamp"));
        // A picture cut out on a transparent ground stands on the page as it is; one shot on its own ground (opaque
        // corners) hangs as a framed photo, so its ground never reads as a stray box on ours.
        _lampPart = _lamp.Source is Bitmap art && !Scene.HasTransparentCorners(art)
            ? new Border { CornerRadius = look.R(18), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _lamp }
            : _lamp;

        _proxy = Capsule(L.SceneProxy, () => Actions.SetVpn(false));
        _vpn = Capsule("VPN", () => Actions.SetVpn(true));
        _connect = Scene.Button(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Scene.Power(Scene.OnAccent(look.Accent)), _connectLabel } }, look.Accent, Scene.OnAccent(look.Accent), 999, new Thickness(18, 9));
        _connect.Click += (_, _) => Actions.ToggleConnect();
        _capsule = new Border
        {
            Background = Scene.B(_c.Capsule),
            CornerRadius = look.R(999),
            Padding = new Thickness(5),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _proxy, _vpn, _connect } },
        };
        _change = Scene.Button(Scene.Text(L.SceneChange, 13, _c.Text, weight: FontWeight.Medium), null, _c.Text, 999, new Thickness(18, 10), _c.ChangeLine);
        _change.HorizontalAlignment = HorizontalAlignment.Right;
        _change.VerticalAlignment = VerticalAlignment.Center;
        _change.Click += (_, _) => ShowServers(_change, _last);

        _heroLeft = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 14,
            Children =
            {
                Scene.Text(L.SceneYouAreIn, 13, _c.Note, Scene.Mono),
                new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Child = _word },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0), Children = { _flagBox, _sub } },
            },
        };

        _modeNote.TextWrapping = TextWrapping.Wrap;
        _nodesCard = Card(_nodesTitle, _meter, Key(L.SceneKeyServers), _nodesKey);
        _modeCard = Card(_modeTitle, _modeNote, Key(L.SceneKeyMode), _modeKey);
        var routeCard = Card(_routeTitle, Scene.Text(L.SceneRouteHint, 12.5, _c.Note), Key(L.SceneKeyRoute), Key(L.SceneChangeArrow));
        _routeButton = Scene.Button(routeCard, null, _c.Text, 18, new Thickness(0));
        _routeButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _routeButton.Click += (_, _) => Actions.OpenRoute(_routeButton);

        var (good, warn, bad) = PingColours;
        var palette = new ScenePalette(_c.Panel, _c.Raised, _c.Text, _c.Soft, _c.Muted, look.Accent, Scene.OnAccent(look.Accent), 14, look.Text, Good: good, Warn: warn, Bad: bad);
        _browser = new ServerBrowser(palette, actions);
        _empty = new EmptyState(palette, actions, partner.Name);
        _account = new AccountPanel(palette, actions, null);
        _accountCard = Card(Scene.Text(string.Format(L.SceneSubscriptionOf, partner.Name), 18, _c.Text), _account, Key(L.SceneKeyAccount), Key(""));
        _foreign = new ForeignNote(palette, actions);
        _heroLeft.Children.Add(_foreign);
        var top = new Grid { ColumnDefinitions = new("*,Auto") };
        Put(top, Key(L.SceneKeyList), 0, 0);
        Put(top, _serversCount, 0, 1);
        var serversGrid = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12 };
        Put(serversGrid, top, 0, 0);
        Put(serversGrid, _serversBody, 1, 0);
        Put(serversGrid, Extras(_c.Soft, ascii: false), 2, 0);
        _servers = new Border
        {
            Background = Scene.B(_c.Card),
            BorderBrush = Scene.B(_c.CardLine),
            BorderThickness = new Thickness(1),
            CornerRadius = look.R(18),
            Padding = new Thickness(16, 14),
            Child = serversGrid,
        };

        Content = new ThemeVariantScope
        {
            RequestedThemeVariant = look.Dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Child = new Panel { Background = Scene.B(_c.Ground), Children = { new DotField(_c.Dots, 14), _host } },
        };
        Reflow(Fit.Wide);
    }

    private TextBlock Key(string text) => Scene.Text(text, 12, _c.Muted, Scene.Mono);

    protected override void Reflow(Fit fit)
    {
        var narrow = fit == Fit.Narrow;
        _word.Cell = fit == Fit.Wide ? 9 : narrow ? 6 : 7.5;

        Control nav;
        if (narrow)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            ServerBrowser.Put(row, new Border { Child = Take(_pill), VerticalAlignment = VerticalAlignment.Center }, 0, 0);
            Put(row, _change, 0, 1);
            nav = new StackPanel { Spacing = 10, Children = { row, Take(_capsule) } };
        }
        else
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto,*") };
            ServerBrowser.Put(row, new Border { Child = Take(_pill), VerticalAlignment = VerticalAlignment.Center }, 0, 0);
            Put(row, _capsule, 0, 1);
            Put(row, _change, 0, 2);
            nav = row;
        }

        Control hero;
        if (narrow || _lamp.Source == null)
        {
            hero = Take(_heroLeft);
        }
        else
        {
            var lamp = fit == Fit.Wide ? 300 : 200;
            _lamp.MaxHeight = lamp;
            var grid = new Grid { ColumnDefinitions = new($"*,{lamp}"), ColumnSpacing = 10 };
            Put(grid, _heroLeft, 0, 0);
            Put(grid, _lampPart, 0, 1);
            hero = grid;
        }
        _heroLeft.Margin = narrow ? new Thickness(0, 10, 0, 6) : default;

        LayoutCards();
        var cards = Take(_cards);

        var column = new SceneColumn(12, 340, narrow ? new Thickness(14, 6, 14, 14) : new Thickness(26, 6, 26, 22));
        column.Parts.Add(nav);
        column.Parts.Add(hero);
        column.Parts.Add(cards);
        column.Parts.Add(Take(_servers));
        _host.Content = column;
    }

    /// <summary>Three cards in a row, four with the subscription; two by two in a medium window, one under another in a narrow one.</summary>
    private void LayoutCards()
    {
        var parts = new List<Control> { _nodesCard, _modeCard, _routeButton };
        if (_accountCard.IsVisible)
        {
            parts.Add(_accountCard);
        }
        var columns = CurrentFit switch
        {
            Fit.Narrow => 1,
            Fit.Medium => parts.Count == 4 ? 2 : 3,
            _ => parts.Count,
        };
        _cards.Children.Clear();
        _cards.ColumnDefinitions = new(string.Join(",", Enumerable.Repeat("*", columns)));
        _cards.RowDefinitions = new(string.Join(",", Enumerable.Repeat("Auto", (parts.Count + columns - 1) / columns)));
        for (var i = 0; i < parts.Count; i++)
        {
            Put(_cards, parts[i], i / columns, i % columns);
        }
    }

    private Button Capsule(string text, Action click)
    {
        var button = Scene.Button(Scene.Text(text, 13, _c.CapsuleOff, weight: FontWeight.Medium), null, _c.CapsuleOff, 999, new Thickness(14, 8));
        button.Click += (_, _) => click();
        return button;
    }

    private Border Card(Control title, Control detail, TextBlock key, TextBlock value)
    {
        var top = new Grid { ColumnDefinitions = new("*,Auto") };
        top.Children.Add(key);
        Grid.SetColumn(value, 1);
        top.Children.Add(value);
        return new Border
        {
            Background = Scene.B(_c.Card),
            BorderBrush = Scene.B(_c.CardLine),
            BorderThickness = new Thickness(1),
            CornerRadius = Look.R(18),
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
        _flagBox.IsVisible = _flag.Source != null;
        _sub.Text = string.Join(" · ", new[] { s.Subtitle, s.Subscription, s.Ping > 0 ? $"{s.Ping} {L.SceneMs}" : null }.Where(t => t.IsNotEmpty()));
        // The lamp is lit while the connection protects; dim otherwise, so the picture says the state too.
        _lamp.Opacity = s.Protected ? 1 : 0.35;
        _connectLabel.Text = s.ConnectLabel;
        _connect.IsEnabled = s.HasServer;
        foreach (var (button, on) in new[] { (_proxy, !s.Vpn), (_vpn, s.Vpn) })
        {
            if (button.Content is Border { Child: TextBlock label } border)
            {
                border.Background = on ? Scene.B(_c.CapsuleOn) : Brushes.Transparent;
                label.Foreground = Scene.B(on ? _c.CapsuleOnText : _c.CapsuleOff);
            }
        }
        _change.IsEnabled = s.Servers.Count > 0;
        _nodesKey.Text = s.NodesTotal > 0 ? $"{s.NodesOk}/{s.NodesTotal}" : "—";
        _nodesTitle.Text = s.NodesTotal > 0 ? string.Format(L.SceneAnsweringOf, NodesText(s)) : L.SceneNotPinged;
        _meter.Set(s.NodesOk, s.NodesTotal);
        _modeKey.Text = s.Vpn ? "vpn" : L.SceneProxyLower;
        _modeTitle.Text = s.Vpn ? L.SceneWholeComputer : L.SceneBrowsersApps;
        _modeNote.Text = s.Vpn ? L.SceneAllThrough : L.SceneWithSystemProxy;
        _routeTitle.Text = s.Route;
        _serversCount.Text = s.Catalog.Count.ToString(CultureInfo.CurrentCulture);
        _account.Update(s.Account);
        var hasAccount = _account.IsVisible;
        if (_accountCard.IsVisible != hasAccount)
        {
            _accountCard.IsVisible = hasAccount;
            LayoutCards();
        }
        _foreign.Update(s);
        ShowListOrEmpty(_serversBody, _browser, _empty, s);
    }
}

/// <summary>
/// The ASCII look: text all the way (monospace unless the person picked other type), the map typed in characters,
/// buttons in brackets. A black terminal, or paper in the light mode.
/// </summary>
public sealed class AsciiScene : EditionScene
{
    private sealed record Ink(
        string Ground, string Map, string MapLine, string Legend, string Key, string Value, string Big, string Note,
        string ButtonFill, string ButtonText, string Here, string Raised, string Soft, string Text, string Muted);

    private static readonly Ink DarkInk = new("#121211", "#4A4843", "#2A2926", "#6E6C66", "#6E6C66", "#E9E6DF", "#F3F2EE", "#8E8C86",
        "#F3F2EE", "#121211", "#3DD68C", "#1C1B19", "#E9E6DF", "#F3F2EE", "#8E8C86");
    private static readonly Ink LightInk = new("#F3F2EE", "#B5B2AA", "#D5D2CA", "#8E8C86", "#8E8C86", "#1A1918", "#121211", "#6A6862",
        "#121211", "#F3F2EE", "#16804F", "#E7E5DF", "#2A2926", "#121211", "#6A6862");

    private readonly Ink _c;
    private readonly TextBlock _map = new() { FontFamily = Scene.Mono, FontSize = 12, LineHeight = 13 };
    private readonly TextBlock _pillText = new();
    private readonly Border _pill;
    private readonly MatrixText _ping = new() { Cell = 8 };
    private readonly TextBlock _pingNote;
    private readonly TextBlock _server;
    private readonly TextBlock _nodes;
    private readonly TextBlock _mode;
    private readonly TextBlock _route;
    private readonly TextBlock _connectLabel;
    private readonly Button _connect;
    private readonly Border _mapBox;
    private readonly StackPanel _pingRow;
    private readonly Grid _kv;
    private readonly WrapPanel _buttons;
    private readonly ServerBrowser _browser;
    private readonly EmptyState _empty;
    private readonly ContentControl _listBody = new();
    private readonly ThemeVariantScope _frame;
    private readonly AccountPanel _account;
    private readonly ForeignNote _foreign;
    private string? _mapFor;
    private EditionSnapshot? _last;

    public AsciiScene(V2pnPartner partner, IEditionActions actions, SceneLook look) : base(partner, actions, look)
    {
        _c = look.Dark ? DarkInk : LightInk;
        _frame = new ThemeVariantScope { RequestedThemeVariant = look.Dark ? ThemeVariant.Dark : ThemeVariant.Light };
        _map.Foreground = Scene.B(_c.Map);
        _ping.Foreground = Scene.B(_c.Big);
        _pingNote = Scene.Text("", 12, _c.Note);
        _server = Value();
        _nodes = Value();
        _mode = Value();
        _route = Value();
        _connectLabel = Scene.Text("", 12.5, _c.ButtonText);
        _pill = Scene.Pill(_pillText, light: !look.Dark);
        _mapBox = new Border
        {
            BorderBrush = Scene.B(_c.MapLine),
            BorderThickness = new Thickness(1),
            CornerRadius = look.R(4),
            Padding = new Thickness(16, 14),
            ClipToBounds = true,
            Child = new DockPanel
            {
                Children =
                {
                    new TextBlock { Text = L.SceneMapLegend, FontFamily = look.Text, FontSize = 11, Foreground = Scene.B(_c.Legend), TextTrimming = TextTrimming.CharacterEllipsis, [DockPanel.DockProperty] = Dock.Bottom },
                    new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = _map },
                },
            },
        };

        _pingRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _ping, _pingNote } };
        _kv = new Grid { ColumnDefinitions = new("110,*"), RowDefinitions = new("Auto,Auto,Auto,Auto"), RowSpacing = 8 };
        void Row(int r, string key, Control value)
        {
            var k = Scene.Text(key, 12, _c.Key);
            k.LetterSpacing = 0.8;
            Put(_kv, k, r, 0);
            Put(_kv, value, r, 1);
        }
        var modeButton = Scene.Button(_mode, null, _c.Value, 2, new Thickness(0));
        modeButton.HorizontalAlignment = HorizontalAlignment.Left;
        modeButton.Click += (_, _) => Actions.SetVpn(_last is { Vpn: false });
        var routeButton = Scene.Button(_route, null, _c.Value, 2, new Thickness(0));
        routeButton.HorizontalAlignment = HorizontalAlignment.Left;
        routeButton.Click += (_, _) => Actions.OpenRoute(routeButton);
        Row(0, L.SceneKeyServer, _server);
        Row(1, L.SceneKeyServersAscii, _nodes);
        Row(2, L.SceneKeyModeAscii, modeButton);
        Row(3, L.SceneKeyRouteAscii, routeButton);

        _connect = Scene.Button(_connectLabel, _c.ButtonFill, _c.ButtonText, 2, new Thickness(14, 9));
        _connect.Click += (_, _) => Actions.ToggleConnect();
        _connect.Margin = new Thickness(0, 0, 14, 6);
        var extras = Extras(_c.Muted, ascii: true);
        extras.VerticalAlignment = VerticalAlignment.Center;
        extras.Margin = new Thickness(0, 0, 0, 6);
        _buttons = new WrapPanel { Children = { _connect, extras } };

        var (good, warn, bad) = PingColours;
        var palette = new ScenePalette(_c.Ground, _c.Raised, _c.Text, _c.Soft, _c.Muted, look.Accent, _c.Ground, 2, look.Text, Ascii: true, Good: good, Warn: warn, Bad: bad);
        _browser = new ServerBrowser(palette, actions);
        _empty = new EmptyState(palette, actions, partner.Name);
        _account = new AccountPanel(palette, actions, string.Format(L.SceneSubscriptionAscii, partner.Name.ToUpperInvariant()));
        _foreign = new ForeignNote(palette, actions);
        Content = new Border { Background = Scene.B(_c.Ground), Child = _frame };
        Reflow(Fit.Wide);
    }

    protected override void Reflow(Fit fit)
    {
        var narrow = fit == Fit.Narrow;
        _ping.Cell = fit == Fit.Wide ? 8 : narrow ? 6 : 7;
        _kv.ColumnDefinitions[0].Width = new GridLength(narrow ? 92 : 110);

        if (narrow)
        {
            _mapBox.Height = 200;
            var column = new SceneColumn(14, 300, new Thickness(14, 4, 14, 14));
            foreach (var part in new Control[] { _pill, _pingRow, _foreign, _kv, _buttons, _account, _mapBox, _listBody })
            {
                column.Parts.Add(Take(part));
            }
            _frame.Child = column;
            return;
        }

        _mapBox.Height = double.NaN;
        var side = new SceneColumn(16, 260, default);
        foreach (var part in new Control[] { _pill, _pingRow, _foreign, _kv, _buttons, _account, _listBody })
        {
            side.Parts.Add(Take(part));
        }
        var root = new Grid { ColumnDefinitions = new(fit == Fit.Wide ? "1.15*,*" : "*,*"), ColumnSpacing = 18, Margin = new Thickness(22, 4, 22, 20) };
        Put(root, _mapBox, 0, 0);
        ServerBrowser.Put(root, side, 0, 1);
        _frame.Child = root;
    }

    private TextBlock Value()
    {
        var text = Scene.Text("", 12.5, _c.Value);
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        return text;
    }

    public override void Update(EditionSnapshot s)
    {
        _last = s;
        Scene.SetPill(_pill, _pillText, s.StateKind, s.StateLabel);
        _ping.Text = s.Ping > 0 ? s.Ping.ToString(CultureInfo.InvariantCulture) : "—";
        _pingNote.Text = s.Ping > 0 ? string.Format(L.SceneMsTo, s.Subtitle) : s.Subtitle;
        _server.Text = s.Subscription.IsNotEmpty() ? $"{s.Subtitle} · {s.Subscription}" : s.Subtitle;
        var lit = s.NodesTotal > 0 ? (int)Math.Round(s.NodesOk * 14.0 / s.NodesTotal) : 0;
        _nodes.Text = s.NodesTotal > 0 ? $"{new string('■', lit)}{new string('□', 14 - lit)}  {s.NodesOk}/{s.NodesTotal}" : L.SceneNotPingedAscii;
        _mode.Text = s.Vpn ? L.SceneModeVpnAscii : L.SceneModeProxyAscii;
        _route.Text = $"{s.Route.ToLower(CultureInfo.CurrentUICulture)}  {L.SceneChangeAscii}";
        _connectLabel.Text = $"[ {s.ConnectLabel.ToLower(CultureInfo.CurrentUICulture)} ]";
        _connect.IsEnabled = s.HasServer;
        _account.Update(s.Account);
        _foreign.Update(s);
        ShowListOrEmpty(_listBody, _browser, _empty, s);

        var centre = s.CountryCode ?? "DE";
        // Rebuilt when the centre or the set of countries changes: the first refresh comes before the servers load.
        var others = s.Catalog.Where(x => x.IndexId != s.SelectedId).Select(x => x.Code).Where(c => c != null).Distinct().Order().ToList();
        var mapKey = centre + ":" + string.Join(",", others);
        if (_mapFor == mapKey)
        {
            return;
        }
        _mapFor = mapKey;
        var lines = AsciiMap.Lines(centre).Select(l => l.ToCharArray()).ToArray();
        foreach (var code in others)
        {
            if (AsciiMap.Place(centre, code) is { } at)
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
                inlines.Add(new Run("@") { Foreground = Scene.B(_c.Here) });
                inlines.Add(new Run(text[(h.Column + 1)..]));
            }
            else
            {
                // The partner's other servers in the accent, the land in the ink of the page.
                var parts = text.Split('*');
                for (var i = 0; i < parts.Length; i++)
                {
                    if (i > 0)
                    {
                        inlines.Add(new Run("*") { Foreground = Scene.B(Look.Accent) });
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
}
