using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using v2rayN.Desktop.Converters;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// Neon: one column like a phone VPN app (INCY, Happ). At the top the provider and Proxy/VPN, then Connect (a big round
/// button with a glowing ring in the provider's colours, or a row with a switch), the state and what goes through the
/// VPN, the provider's buttons, and a card with the subscription (plan, traffic, banner) over the server list. The mini
/// window (380×640) is the look as designed; a wider window puts the card beside the column. The provider gives the
/// colours and the kind of Connect; V2pn derives every other colour (V2pnThemePalette) and keeps the state colours.
/// </summary>
public sealed class NeonScene : EditionScene
{
    private readonly V2pnTheme _t;
    private readonly V2pnThemeColors _c;
    private readonly FontFamily _font;

    private readonly Control _brand;
    private readonly ThemeSegment _mode;
    private readonly NeonConnect _connect;
    private readonly TextBlock _pillText = new();
    private readonly Border _pill;
    private readonly Button _route;
    private readonly TextBlock _routeText;
    private readonly WrapPanel _buttons = new() { Orientation = Orientation.Horizontal, ItemSpacing = 18, LineSpacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
    private string _buttonsKey = string.Empty;
    private readonly ForeignNote _foreign;
    private readonly Border _foreignCard;

    private readonly TextBlock _subTitle;
    private readonly TextBlock _subMeta;
    private readonly Border _trafficTrack;
    private readonly Border _trafficBar;
    private readonly TextBlock _trafficText;
    private readonly Grid _traffic;
    private double _fraction;
    private readonly ThemeBlock _banner;
    private readonly Border _bannerBox;
    private readonly ServerBrowser _browser;
    private readonly EmptyState _empty;
    private readonly ContentControl _serversBody = new();
    private readonly Border _card;

    private readonly ContentControl _host = new();
    private EditionSnapshot? _last;

    public NeonScene(V2pnPartner partner, IEditionActions actions, SceneLook look, V2pnTheme theme, V2pnThemeColors colours) : base(partner, actions, look)
    {
        _t = theme;
        _c = colours;
        _font = Scene.Geist;

        var logoOnDark = Scene.Picture(partner, partner.Pack.Logo);
        var logoOnLight = Scene.Picture(partner, partner.Pack.LogoOnLight) ?? logoOnDark;
        var logo = _c.Dark ? logoOnDark : logoOnLight;
        _brand = logo != null
            ? new Image { Source = logo, Height = 22, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center }
            : new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 9,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Border
                    {
                        Width = 24,
                        Height = 24,
                        CornerRadius = new CornerRadius(7),
                        Background = Scene.B(_c.Accent),
                        Child = Scene.Text(partner.Name[..1].ToUpperInvariant(), 12, _c.OnAccent, _font, FontWeight.Bold).Also(t =>
                        {
                            t.HorizontalAlignment = HorizontalAlignment.Center;
                            t.VerticalAlignment = VerticalAlignment.Center;
                        }),
                    },
                    Scene.Text(partner.Name, 14.5, _c.Ink, _font, FontWeight.SemiBold),
                },
            };

        _mode = new ThemeSegment(_c.Raised, _c.Accent, _c.OnAccent, _c.Soft, _font, _ => new CornerRadius(999), actions, 32);
        _connect = new NeonConnect(theme.Connect, _c, actions, _font);
        _connect.RowClicked += anchor => ShowServers(anchor, _last);

        _pill = Scene.Pill(_pillText, light: !_c.Dark);
        _routeText = Scene.Text("", 12.5, _c.Soft, _font, FontWeight.Medium);
        _routeText.TextTrimming = TextTrimming.CharacterEllipsis;
        _route = Scene.Button(
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { Scene.Text("⇄", 12.5, _c.AccentText, _font), _routeText, Scene.Text("▾", 11, _c.Soft, _font) },
            },
            null, _c.Soft, 8, new Thickness(4, 4));
        _route.MaxWidth = 250;
        _route.Click += (_, _) => Actions.OpenRoute(_route);
        ToolTip.SetTip(_route, L.SceneRouteHint);

        var surface = Palette(_c.Surface, _c.Raised);
        _foreign = new ForeignNote(surface, actions);
        _foreignCard = new Border { Background = Scene.B(_c.Surface), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10), Child = _foreign };

        // The subscription card: who and what plan, the traffic as a bar with its numbers in it, the banner, the servers.
        _subTitle = Scene.Text(partner.Name, 14, _c.Ink, _font, FontWeight.SemiBold);
        _subTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _subMeta = Scene.Text("", 11.5, _c.Muted, _font);
        _subMeta.TextTrimming = TextTrimming.CharacterEllipsis;
        _trafficBar = new Border { CornerRadius = new CornerRadius(999), Background = Scene.B(_c.Accent), HorizontalAlignment = HorizontalAlignment.Left };
        _trafficText = Scene.Text("", 10.5, _c.Ink, _font, FontWeight.SemiBold);
        _trafficText.HorizontalAlignment = HorizontalAlignment.Center;
        _trafficText.VerticalAlignment = VerticalAlignment.Center;
        _trafficTrack = new Border
        {
            Height = 16,
            CornerRadius = new CornerRadius(999),
            Background = Scene.B(_c.Raised),
            ClipToBounds = true,
            Child = new Panel { Children = { _trafficBar, _trafficText } },
        };
        _trafficTrack.SizeChanged += (_, _) => SizeBar();
        _traffic = new Grid { Margin = new Thickness(14, 0, 14, 10), Children = { _trafficTrack } };

        _banner = new ThemeBlock(ThemeBlock.Banner, _c, _c.Surface, _c.Raised, _font, actions, _ => new CornerRadius(8));
        _bannerBox = new Border
        {
            Background = Scene.B(V2pnColor.Parse(_c.Accent).WithAlpha(_c.Dark ? 0.12 : 0.08)),
            Padding = new Thickness(14, 10),
            Child = _banner,
        };

        var listPalette = Palette(_c.Surface, _c.Raised);
        _browser = new ServerBrowser(listPalette, actions);
        _empty = new EmptyState(listPalette, actions, partner.Name);
        _serversBody.Margin = new Thickness(10, 10, 10, 6);

        var head = new StackPanel { Spacing = 2, Margin = new Thickness(14, 12, 14, 8), Children = { _subTitle, _subMeta } };
        var cardGrid = new Grid { RowDefinitions = new("Auto,Auto,Auto,*") };
        ServerBrowser.Put(cardGrid, head, 0, 0);
        ServerBrowser.Put(cardGrid, _traffic, 1, 0);
        ServerBrowser.Put(cardGrid, _bannerBox, 2, 0);
        ServerBrowser.Put(cardGrid, _serversBody, 3, 0);
        _card = new Border { Background = Scene.B(_c.Surface), ClipToBounds = true, Child = cardGrid };

        Content = new ThemeVariantScope { RequestedThemeVariant = _c.Dark ? ThemeVariant.Dark : ThemeVariant.Light, Child = _host };
        Reflow(Fit.Narrow);
    }

    private ScenePalette Palette(string ground, string raised) =>
        new(ground, raised, _c.Ink, _c.Soft, _c.Muted, _c.Accent, _c.OnAccent, 12, _font, Good: _c.Good, Warn: _c.Warn, Bad: _c.Bad);

    /// <summary>The ground with the accent's glow behind Connect, as INCY draws it.</summary>
    private IBrush Glow(double y) => new RadialGradientBrush
    {
        Center = new RelativePoint(0.5, y, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.5, y, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.42, RelativeUnit.Relative),
        GradientStops =
        {
            // Opaque: the plain window lies under the scene and must never show through.
            new GradientStop(Color.Parse(V2pnColor.Parse(_c.Accent).Over(V2pnColor.Parse(_c.Ground), _c.Dark ? 0.22 : 0.14).Hex), 0),
            new GradientStop(Color.Parse(_c.Ground), 1),
        },
    };

    private void SizeBar() => _trafficBar.Width = Math.Max(_fraction > 0 ? 16 : 0, _trafficTrack.Bounds.Width * _fraction);

    private Grid Head()
    {
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Margin = new Thickness(16, 6, 16, 0) };
        Put(row, _brand, 0, 0);
        _mode.VerticalAlignment = VerticalAlignment.Center;
        Put(row, _mode, 0, 1);
        return row;
    }

    /// <summary>The state and the route under Connect.</summary>
    private Control Under() => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        HorizontalAlignment = HorizontalAlignment.Center,
        Children = { Take(_pill).Also(p => p.VerticalAlignment = VerticalAlignment.Center), Take(_route) },
    };

    /// <summary>Connect with the state and the route under it, and the provider's buttons.</summary>
    private Control Hero(double scale)
    {
        _connect.Resize(scale);
        var stack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (_t.Connect == V2pnThemeConnect.Toggle)
        {
            stack.Children.Add(Take(_connect).Also(c => c.Margin = new Thickness(12, 0)));
        }
        else
        {
            stack.Children.Add(Take(_connect));
        }
        stack.Children.Add(Under());
        stack.Children.Add(Take(_buttons));
        stack.Children.Add(Take(_foreignCard).Also(f => f.Margin = new Thickness(12, 0)));
        return stack;
    }

    protected override void Reflow(Fit fit)
    {
        var round = _t.Connect == V2pnThemeConnect.Round;
        if (fit == Fit.Narrow)
        {
            // The phone: the column from top to bottom, the card to the window's bottom edge.
            _card.CornerRadius = new CornerRadius(18, 18, 0, 0);
            _card.Margin = new Thickness(12, 0);
            var page = new Grid
            {
                RowDefinitions = new("Auto,Auto,*"),
                RowSpacing = 12,
                Background = Glow(round ? 0.22 : 0.12),
            };
            ServerBrowser.Put(page, Head(), 0, 0);
            var hero = Hero(round ? 0.82 : 1);
            hero.Margin = new Thickness(0, round ? 6 : 2, 0, 0);
            ServerBrowser.Put(page, hero, 1, 0);
            Put(page, _card, 2, 0);
            _host.Content = page;
        }
        else
        {
            // Wider: the column on the left with Connect in its middle, the card beside it.
            _card.CornerRadius = new CornerRadius(18);
            _card.Margin = new Thickness(0, 6, 14, 14);
            var left = new Grid { RowDefinitions = new("Auto,*"), Background = Glow(0.45) };
            ServerBrowser.Put(left, Head(), 0, 0);
            var hero = Hero(fit == Fit.Wide ? 1.05 : 0.95);
            hero.VerticalAlignment = VerticalAlignment.Center;
            hero.MaxWidth = 440;
            ServerBrowser.Put(left, hero, 1, 0);
            var grid = new Grid { ColumnDefinitions = new(fit == Fit.Wide ? "*,460" : "*,400"), Background = Scene.B(_c.Ground) };
            ServerBrowser.Put(grid, left, 0, 0);
            Put(grid, _card, 0, 1);
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
        Scene.SetPill(_pill, _pillText, s.StateKind, s.StateLabel);
        _connect.Update(s);
        _mode.Update(s.Vpn);
        _routeText.Text = s.Route;
        UpdateButtons(s);
        _foreign.Update(s);
        _foreignCard.IsVisible = _foreign.IsVisible;

        var account = s.Account;
        _subTitle.Text = account?.Plan is { } plan ? $"{Partner.Name} · {plan}" : Partner.Name;
        var meta = new[] { account?.Devices, account?.Expiry }.Where(x => x.IsNotEmpty()).ToList();
        _subMeta.Text = meta.Count > 0 ? string.Join(" · ", meta) : string.Format(L.ThemeServerCount, s.Catalog.Count(x => s.PartnerSubIds.Contains(x.SubId ?? string.Empty)));
        _subMeta.Foreground = Scene.B(account?.Bad == true ? _c.Bad : account?.Warn == true ? _c.Warn : _c.Muted);
        _traffic.IsVisible = account?.Traffic != null;
        _trafficText.Text = account?.Traffic ?? string.Empty;
        _fraction = account?.ShowBar == true ? Math.Clamp(account.Fraction, 0, 1) : 0;
        _trafficBar.Background = Scene.B(account?.Bad == true ? _c.Bad : account?.Warn == true ? _c.Warn : _c.Accent);
        SizeBar();
        _banner.Update(s);
        _bannerBox.IsVisible = _banner.IsVisible;
        ShowListOrEmpty(_serversBody, _browser, _empty, s);
    }

    /// <summary>The provider's buttons and its cabinet and support, as quiet links under Connect.</summary>
    private void UpdateButtons(EditionSnapshot s)
    {
        var cabinet = s.Account?.CabinetUrl;
        var support = s.Account?.SupportUrl;
        var key = s.ActionRunning + "|" + cabinet + "|" + support + "|" + string.Join("|", s.Actions.Select(a => a.Id + a.Label + a.Icon));
        if (key == _buttonsKey)
        {
            return;
        }
        _buttonsKey = key;
        _buttons.Children.Clear();
        Button Link(Geometry? glyph, string label, Action<Button> click)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (glyph != null)
            {
                content.Children.Add(new PathIcon { Width = 13, Height = 13, Data = glyph, Foreground = Scene.B(_c.AccentText), VerticalAlignment = VerticalAlignment.Center });
            }
            content.Children.Add(Scene.Text(label, 12.5, _c.Soft, _font, FontWeight.Medium));
            var button = Scene.Button(content, null, _c.Soft, 8, new Thickness(2, 4));
            Avalonia.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) => click(button);
            return button;
        }
        foreach (var action in s.Actions)
        {
            var id = action.Id;
            var button = Link(PartnerActionIcons.Of(action.Icon), action.Id == s.ActionRunning ? action.Label + " …" : action.Label, b => Actions.RunAction(id, b));
            button.IsEnabled = s.ActionRunning == null || action.Id == s.ActionRunning;
            _buttons.Children.Add(button);
        }
        if (cabinet != null)
        {
            _buttons.Children.Add(Link(null, L.SceneCabinet + " ↗", _ => Actions.OpenUrl(cabinet)));
        }
        if (support != null)
        {
            _buttons.Children.Add(Link(null, L.SceneSupport + " ↗", _ => Actions.OpenUrl(support)));
        }
        _buttons.IsVisible = _buttons.Children.Count > 0;
    }
}

/// <summary>
/// Neon's Connect. Round: a button in the card colour inside a ring of the provider's colours, the accent's glow behind
/// it, the power sign and the place in it. Toggle: a row with the flag, the place and a switch. What it does and the
/// state colours (the switch turns V2pn's green) are V2pn's.
/// </summary>
public sealed class NeonConnect : UserControl
{
    private readonly string _style;
    private readonly V2pnThemeColors _c;
    private readonly IEditionActions _actions;
    private readonly Button _button;
    private readonly Border _fill;
    private readonly Ellipse _ring = new() { StrokeThickness = 3, IsHitTestVisible = false };
    private readonly Ellipse _glow = new() { IsHitTestVisible = false };
    private readonly Panel _round;
    private readonly Avalonia.Controls.Shapes.Path _power;
    private readonly TextBlock _caption;
    private readonly TextBlock _place;
    private readonly Border _thumb = new();
    private readonly Image _flag = Scene.Flag(null, 34, 24);
    private readonly Border _flagBox;
    private readonly PathIcon _globe;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly Button? _row;

    /// <summary>The toggle row was clicked away from its switch: the scene shows the servers.</summary>
    public event Action<Control>? RowClicked;

    public NeonConnect(string style, V2pnThemeColors c, IEditionActions actions, FontFamily font)
    {
        _style = style;
        _c = c;
        _actions = actions;
        _power = Scene.Power(c.AccentText, 34);
        _caption = Scene.Text("", 11, c.Ink, font, FontWeight.SemiBold);
        _caption.HorizontalAlignment = HorizontalAlignment.Center;
        _caption.LetterSpacing = 1;
        _place = Scene.Text("", 11.5, c.Soft, font);
        _place.HorizontalAlignment = HorizontalAlignment.Center;
        _place.TextTrimming = TextTrimming.CharacterEllipsis;
        _globe = Scene.Globe(c.Soft, 20);
        _flagBox = new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Width = 34, Height = 24, Child = _flag, VerticalAlignment = VerticalAlignment.Center };
        _title = Scene.Text("", 16.5, c.Ink, font, FontWeight.SemiBold);
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _subtitle = Scene.Text("", 12, c.Soft, font);
        _subtitle.TextTrimming = TextTrimming.CharacterEllipsis;

        var accent = V2pnColor.Parse(c.AccentText);
        var second = V2pnColor.Parse(c.Secondary);
        if (style == V2pnThemeConnect.Toggle)
        {
            _button = Scene.Button(_thumb, c.Raised, c.Ink, 999, new Thickness(3));
            _fill = (Border)_button.Content!;
            _button.Width = 58;
            _button.Height = 34;
            _thumb.Width = _thumb.Height = 28;
            _thumb.CornerRadius = new CornerRadius(14);
            _thumb.VerticalAlignment = VerticalAlignment.Center;
            _thumb.BoxShadow = BoxShadows.Parse("0 2 4 0 #40000000");
            _button.Click += (_, _) => _actions.ToggleConnect();
            var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 14 };
            grid.Children.Add(new Panel { VerticalAlignment = VerticalAlignment.Center, Children = { _flagBox, _globe } });
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _subtitle } };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var place = Scene.Button(grid, null, c.Ink, 12, default);
            place.HorizontalAlignment = HorizontalAlignment.Stretch;
            place.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            place.Click += (_, _) => RowClicked?.Invoke(place);
            _row = place;
            var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(place);
            Grid.SetColumn(_button, 1);
            _button.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(_button);
            _round = new Panel();
            Content = new Border { Background = Scene.B(c.Surface), CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 14), Child = row };
            return;
        }

        var inner = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { _power, _caption, _place } };
        _power.HorizontalAlignment = HorizontalAlignment.Center;
        _button = Scene.Button(inner, c.Surface, c.Ink, 999, default, border: accent.WithAlpha(c.Dark ? 0.45 : 0.35));
        _fill = (Border)_button.Content!;
        _button.Click += (_, _) => _actions.ToggleConnect();
        _ring.Stroke = new ConicGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Angle = 200,
            GradientStops =
            {
                new GradientStop(Color.Parse(accent.Hex), 0),
                new GradientStop(Color.Parse(second.Hex), 0.45),
                new GradientStop(Color.Parse(accent.WithAlpha(0.25)), 0.75),
                new GradientStop(Color.Parse(accent.Hex), 1),
            },
        };
        _glow.Fill = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.Parse(accent.WithAlpha(c.Dark ? 0.42 : 0.26)), 0),
                new GradientStop(Color.Parse(accent.WithAlpha(0)), 1),
            },
        };
        _round = new Panel { HorizontalAlignment = HorizontalAlignment.Center, Children = { _glow, _ring, _button } };
        _glow.HorizontalAlignment = _ring.HorizontalAlignment = _button.HorizontalAlignment = HorizontalAlignment.Center;
        _glow.VerticalAlignment = _ring.VerticalAlignment = _button.VerticalAlignment = VerticalAlignment.Center;
        Content = _round;
        Resize(1);
    }

    /// <summary>Grows or shrinks the round button with the window (1 is 146 px); the toggle row keeps its size.</summary>
    public void Resize(double scale)
    {
        if (_style == V2pnThemeConnect.Toggle)
        {
            return;
        }
        var size = Math.Round(146 * scale);
        _button.Width = _button.Height = size;
        _button.CornerRadius = _fill.CornerRadius = new CornerRadius(size / 2);
        _ring.Width = _ring.Height = size + 32;
        _glow.Width = _glow.Height = size + 120;
        // The glow spills over the parts around it without pushing them away.
        _glow.Margin = new Thickness(-44);
        _round.Width = _round.Height = size + 32;
        _power.Width = _power.Height = Math.Round(size * 0.24);
        _power.StrokeThickness = Math.Max(2.4, size / 50);
        _place.MaxWidth = size - 24;
    }

    public void Update(EditionSnapshot s)
    {
        var on = s.ConnectLabel != L.Connect;
        _button.IsEnabled = s.HasServer;
        Avalonia.Automation.AutomationProperties.SetName(_button, s.ConnectLabel);
        ToolTip.SetTip(_button, s.StateLabel);
        var place = s.Ping > 0 ? $"{s.Title} · {string.Format(L.ThemePingValue, s.Ping)}" : s.Title;
        if (_style == V2pnThemeConnect.Toggle)
        {
            _flag.Source = V2pnConverters.Flag(s.CountryCode);
            _flagBox.IsVisible = _flag.Source != null;
            _globe.IsVisible = _flag.Source == null;
            _title.Text = s.Title;
            _subtitle.Text = s.Ping > 0 ? $"{s.StateLabel} · {string.Format(L.ThemePingValue, s.Ping)}" : s.Subtitle;
            // The switch is V2pn's green while it protects, its amber while it checks or protects only in part.
            var state = s.StateKind switch { "safe" => _c.Good, "warn" => _c.Warn, "checking" => _c.Muted, _ => null };
            _fill.Background = Scene.B(on ? state ?? _c.Muted : _c.Line);
            _thumb.Background = Brushes.White;
            _thumb.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            if (_row != null)
            {
                ToolTip.SetTip(_row, L.SceneChange);
            }
            return;
        }
        _caption.Text = s.ConnectLabel.ToUpperInvariant();
        _place.Text = place;
        _place.IsVisible = s.HasServer;
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
