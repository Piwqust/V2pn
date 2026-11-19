using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media.Transformation;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Converters;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// theme.layout "stickers": a sticker book over the whole window. The pack's scene stands full height on the right, its
/// heroes saying what is going on in a speech bubble; on the left, the brass plate, a meme caption over V2pn's own state
/// pill, Connect as a die-cut sticker and the partner's buttons as stickers; the servers are hero cards with a brass
/// name plate. A narrow window gets the same parts in one column under a band of the scene.
/// </summary>
public sealed class StickerScene : EditionScene
{
    private const string Night = "#0B1430";
    private const string Ink = "#E9EEF9";
    private const string Soft = "#A9B8DA";
    private const string Bronze = "#3A2410";
    private const double NarrowBelow = 760;

    private readonly string _accent;
    private readonly Bitmap? _artBitmap;
    private readonly Image _art = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Border _seam = new() { HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    private readonly Border _scrim = new() { IsHitTestVisible = false };
    private readonly Panel _root = new() { ClipToBounds = true };

    private readonly StackPanel _column = new() { Spacing = 16 };
    private readonly StrokeText _caption = new() { Size = 44, Outline = 3.5, Drop = 4 };
    private readonly (Border Pill, TextBlock Text) _pill = StatePill();
    private readonly Image _flag = new() { Width = 24, Height = 17, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _where = Scene.Text("", 14, Soft, FontWeight.Medium);
    private readonly StrokeText _connectLabel = new() { Size = 30, Outline = 3, Drop = 3 };
    private readonly TextBlock _connectNote = Scene.Text("", 12.5, Ink, FontWeight.Medium);
    private readonly Border _connectFace = new();
    private readonly Button _connect;
    private readonly Border _vpnFace = new();
    private readonly Border _proxyFace = new();
    private readonly TextBlock _vpnText = new() { Text = "VPN" };
    private readonly PathIcon _vpnLock = Scene.Icon("SemiIconLock", Soft, 12);
    private Button? _vpn;
    private readonly TextBlock _proxyText = new() { Text = L.StickerProxy };
    private readonly TextBlock _routeText = Scene.Text("", 13, Ink, FontWeight.SemiBold);
    private readonly WrapPanel _actions = new() { ItemSpacing = 10, LineSpacing = 12 };
    private readonly ContentControl _plan = new();

    private readonly StackPanel _serverBlock = new() { Spacing = 12 };
    private readonly WrapPanel _cards = new() { ItemSpacing = 16, LineSpacing = 16 };
    private readonly SpeechBubble _bubble = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };

    private readonly Border _cardsPad = new() { Padding = new Thickness(6, 24, 24, 14) };
    private double _cardWidth = 156;
    // How many cards the wide window shows (two rows at most); the rest are behind the "more" card.
    private int _cardLimit = int.MaxValue;
    private bool? _narrow;
    private string? _cardsKey;
    private string? _actionsKey;
    private string? _planKey;
    private EditionSnapshot? _last;

    public StickerScene(V2pnPartner partner, IEditionActions actions) : base(partner, actions)
    {
        _accent = Editions.PartnerAccent;
        AddStyles();
        var theme = Editions.ThemeOf(partner);
        _artBitmap = Scene.Picture(partner, theme.Background.Art);
        _art.Source = _artBitmap;
        _seam.Background = Fade(horizontal: true, 0.0, 1, 1.0, 0);

        // The brass plate, as on a hero's card.
        if (Scene.Logo(partner, onLight: false) is { } logo)
        {
            _column.Children.Add(new Image
            {
                Source = logo,
                Height = 58,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                RenderTransform = TransformOperations.Parse("rotate(-2deg)"),
                [AutomationProperties.NameProperty] = partner.Name,
            });
        }
        else
        {
            _column.Children.Add(new StrokeText { Text = partner.Name.ToUpper(CultureInfo.CurrentUICulture), Size = 34, Fill = Scene.B("#E7C27D") });
        }

        _caption.HorizontalAlignment = HorizontalAlignment.Left;
        _caption.RenderTransform = TransformOperations.Parse("rotate(-2deg)");
        var state = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _caption,
                _pill.Pill,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _flag, _where } },
            },
        };
        _column.Children.Add(state);

        _connect = BuildConnect(partner);
        _column.Children.Add(_connect);
        _column.Children.Add(BuildModeAndRoute());
        _column.Children.Add(_actions);
        _column.Children.Add(_plan);

        var plain = Scene.Button(Scene.Text(L.StickerPlain, 12.5, Soft), null, Soft, 6, new Thickness(2, 4));
        plain.Classes.Add("link");
        plain.Click += (_, _) => Actions.ShowPlain();
        _column.Children.Add(plain);

        var title = new StrokeText { Text = L.StickerServers, Size = 22, Outline = 2.5, Drop = 2.5, HorizontalAlignment = HorizontalAlignment.Left };
        _serverBlock.Children.Add(title);
        _cardsPad.Child = _cards;
        _serverBlock.Children.Add(_cardsPad);

        Content = _root;
        Background = Scene.B(Night);
        SizeChanged += (_, _) => Place();
    }

    // ---- building ----

    private void AddStyles()
    {
        // Stickers lean a little and straighten up under the pointer; a press pushes them in.
        Styles.Add(new Style(x => x.OfType<Button>().Class("sticker"))
        {
            Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("rotate(-2deg)")) },
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class("sticker").Class("r"))
        {
            Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("rotate(1.8deg)")) },
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class("sticker").Class(":pointerover"))
        {
            Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("rotate(0deg) scale(1.04)")) },
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class("sticker").Class(":pressed"))
        {
            Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("rotate(0deg) scale(0.96)")) },
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class("link").Class(":pointerover"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.75) },
        });
        // Keyboard focus: the sticker's outline turns the partner's colour; a click does not.
        Styles.Add(new Style(x => x.OfType<Button>().Class("scene").Class(":focus-visible"))
        {
            Setters = { new Setter(TemplatedControl.BorderBrushProperty, Scene.B(_accent)) },
        });
    }

    /// <summary>A die-cut sticker: a white outline round the face, a hard shadow under it, tilted.</summary>
    private static Button Sticker(Border face, bool right, string name)
    {
        face.BorderBrush ??= Brushes.White;
        face.BorderThickness = new Thickness(3);
        face.BoxShadow = BoxShadows.Parse("0 5 0 0 #8C000000");
        var button = Scene.Button(face, null, Ink, 14, new Thickness(0));
        button.Classes.Add("sticker");
        if (right)
        {
            button.Classes.Add("r");
        }
        button.CornerRadius = new CornerRadius(18);
        button.Transitions =
        [
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(140) },
        ];
        AutomationProperties.SetName(button, name);
        return button;
    }

    private Button BuildConnect(V2pnPartner partner)
    {
        var mark = Scene.Picture(partner, partner.Pack.Mark);
        var words = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Children = { _connectLabel, _connectNote } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        if (mark != null)
        {
            row.Children.Add(new Image { Source = mark, Width = 74, Height = 74, Margin = new Thickness(-6, -10, 0, -10) });
        }
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        _connectFace.CornerRadius = new CornerRadius(20);
        _connectFace.Padding = new Thickness(14, 10, 22, 10);
        _connectFace.MinWidth = 300;
        _connectFace.Child = row;
        var button = Sticker(_connectFace, right: false, L.Connect);
        button.Click += (_, _) => Actions.ToggleConnect();
        return button;
    }

    private Control BuildModeAndRoute()
    {
        Border Segment(Border face, TextBlock text)
        {
            text.FontFamily = StrokeText.Heavy;
            text.FontSize = 16;
            text.VerticalAlignment = VerticalAlignment.Center;
            face.CornerRadius = new CornerRadius(12);
            face.Padding = new Thickness(14, 6);
            face.Child = text;
            return face;
        }
        var vpnFace = Segment(_vpnFace, _vpnText);
        vpnFace.Child = null;
        vpnFace.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _vpnText, _vpnLock } };
        var vpn = Sticker(vpnFace, right: false, "VPN");
        _vpn = vpn;
        vpn.Click += (_, _) => Actions.SetVpn(true);
        var proxy = Sticker(Segment(_proxyFace, _proxyText), right: true, L.StickerProxy);
        proxy.Click += (_, _) => Actions.SetVpn(false);

        var routeFace = new Border
        {
            Background = Scene.B("#16264A"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 6),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    Scene.Icon("SemiIconRoute", Soft, 14),
                    _routeText,
                    Scene.Icon("SemiIconChevronDown", Soft, 12),
                },
            },
        };
        var route = Sticker(routeFace, right: false, L.StickerRoute);
        route.Click += (_, _) => Actions.OpenRoute(route);
        return new WrapPanel { ItemSpacing = 10, LineSpacing = 10, Children = { vpn, proxy, route } };
    }

    // ---- state ----

    public override void Update(EditionSnapshot s)
    {
        _last = s;
        var (caption, fill) = s.StateKind switch
        {
            "safe" => (L.StickerOn, "#7CF0B2"),
            "checking" => (L.StickerGoing, "#FFFFFF"),
            "warn" => (L.StickerTrouble, "#FFD36B"),
            _ => (L.StickerIdle, "#FFFFFF"),
        };
        _caption.Text = caption;
        _caption.Fill = Scene.B(fill);
        SetPill(_pill.Pill, _pill.Text, s.StateKind, s.StateLabel);

        _flag.Source = V2pnConverters.Flag(s.CountryCode);
        _flag.IsVisible = _flag.Source != null;
        var where = s.Subtitle.IsNotEmpty() && s.Subtitle != s.Title ? $"{s.Title} · {s.Subtitle}" : s.Title;
        _where.Text = s.Ping > 0 ? $"{where} · {s.Ping} {L.StickerMs}" : where;
        _where.IsVisible = s.HasServer;

        _connectLabel.Text = s.On ? L.StickerLeave : L.StickerGo;
        _connectNote.Text = s.On ? L.Disconnect : L.Connect;
        _connectFace.Background = s.On ? Scene.B("#1A2A52") : Gradient(_accent, Shade(_accent, 0.72));
        _connect.IsEnabled = s.HasServer || s.On;
        AutomationProperties.SetName(_connect, s.On ? L.Disconnect : L.Connect);

        PaintSegment(_vpnFace, _vpnText, s.Vpn);
        _vpnLock.IsVisible = s.VpnNeedsAdmin;
        _vpnLock.Foreground = _vpnText.Foreground;
        if (_vpn != null)
        {
            ToolTip.SetTip(_vpn, s.VpnTip);
        }
        PaintSegment(_proxyFace, _proxyText, !s.Vpn);
        _routeText.Text = s.Route;

        UpdateCards(s);
        UpdateActions(s);
        UpdatePlan(s.Account);

        _bubble.Text = s.StateKind switch
        {
            "safe" => s.Ping > 0 ? string.Format(L.StickerSaysOn, s.Ping) : L.StickerSaysOnNoPing,
            "checking" => L.StickerSaysGoing,
            "warn" => string.Format(L.StickerSaysTrouble, s.StateLabel),
            _ => s.HasServer ? L.StickerSaysIdle : L.StickerSaysNoServer,
        };
        Place();
    }

    private static void PaintSegment(Border face, TextBlock text, bool on)
    {
        face.Background = on ? Brushes.White : Scene.B("#16264A");
        text.Foreground = on ? Scene.B(Night) : Scene.B(Soft);
    }

    private void UpdateCards(EditionSnapshot s)
    {
        var key = $"{_cardWidth}|{_cardLimit}|" + string.Join("|", s.Servers.Select(x => $"{x.IndexId}:{x.Delay}:{x.Selected}:{x.Name}"));
        if (key == _cardsKey)
        {
            return;
        }
        _cardsKey = key;
        _cards.Children.Clear();
        var shown = s.Servers.ToList();
        var rest = new List<SceneServer>();
        if (shown.Count > _cardLimit)
        {
            // The last place goes to the "more" card; the server in use always keeps a card of its own.
            var keep = Math.Max(1, _cardLimit - 1);
            rest = shown.Skip(keep).ToList();
            shown = shown.Take(keep).ToList();
            if (rest.FirstOrDefault(x => x.Selected) is { } selected)
            {
                rest.Remove(selected);
                rest.Insert(0, shown[^1]);
                shown[^1] = selected;
            }
        }
        for (var i = 0; i < shown.Count; i++)
        {
            _cards.Children.Add(Card(shown[i], i));
        }
        if (rest.Count > 0)
        {
            _cards.Children.Add(More(rest, shown.Count));
        }
        _serverBlock.IsVisible = s.Servers.Count > 0;
    }

    /// <summary>The servers that did not get a card, in a menu behind a card of their own.</summary>
    private Button More(IReadOnlyList<SceneServer> rest, int index)
    {
        var face = new Border
        {
            Width = _cardWidth,
            MinHeight = 120,
            CornerRadius = new CornerRadius(16),
            Background = Gradient("#1B2E5C", "#0E1936"),
            Child = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new StrokeText { Text = $"+{rest.Count}", Size = 34, Outline = 2.5, Drop = 2.5, HorizontalAlignment = HorizontalAlignment.Center },
                    Scene.Text(L.StickerMore, 12.5, Soft, FontWeight.SemiBold),
                },
            },
        };
        var button = Sticker(face, right: index % 2 == 1, string.Format(L.StickerMoreName, rest.Count));
        button.Click += (_, _) =>
        {
            var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            foreach (var server in rest)
            {
                var item = new MenuItem
                {
                    Header = server.Delay > 0 ? $"{server.Name}   {server.Delay} {L.StickerMs}" : server.Name,
                    Icon = V2pnConverters.Flag(server.Code) is { } flag ? new Image { Source = flag, Width = 18, Height = 13 } : null,
                };
                var id = server.IndexId;
                item.Click += (_, _) => Actions.Select(id);
                menu.Items.Add(item);
            }
            menu.ShowAt(button);
        };
        return button;
    }

    private Control Card(SceneServer server, int index)
    {
        var (place, rest) = SplitName(server.Name);
        var plate = new Border
        {
            Background = Brass(),
            BorderBrush = Scene.B("#6B4420"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 3),
            Child = new TextBlock
            {
                Text = (rest.IsNotEmpty() ? rest : place).ToUpper(CultureInfo.CurrentUICulture),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                LetterSpacing = 0.5,
                Foreground = Scene.B(Bronze),
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        var flag = V2pnConverters.Flag(server.Code);
        var placeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        if (flag != null)
        {
            placeRow.Children.Add(new Image { Source = flag, Width = 20, Height = 14, VerticalAlignment = VerticalAlignment.Center });
        }
        placeRow.Children.Add(Scene.Text(rest.IsNotEmpty() ? place : string.Empty, 12, Soft));

        var (ping, colour) = server.Delay switch
        {
            < 0 => (L.StickerMiss, "#FF6B6B"),
            0 => (string.Empty, Soft),
            <= 150 => (server.Delay.ToString(CultureInfo.InvariantCulture), "#7CF0B2"),
            <= 300 => (server.Delay.ToString(CultureInfo.InvariantCulture), "#FFD36B"),
            _ => (server.Delay.ToString(CultureInfo.InvariantCulture), "#FF9F6B"),
        };
        var pingRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            MinHeight = 42,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (server.Delay == 0)
        {
            var unknown = Scene.Text(L.StickerNoPing, 12, Soft);
            unknown.Margin = new Thickness(0, 0, 0, 6);
            pingRow.Children.Add(unknown);
        }
        else
        {
            pingRow.Children.Add(new StrokeText { Text = ping, Size = 30, Outline = 2.5, Drop = 2.5, Fill = Scene.B(colour) });
        }
        if (server.Delay > 0)
        {
            var ms = Scene.Text(L.StickerMs, 12, Soft, FontWeight.SemiBold);
            ms.VerticalAlignment = VerticalAlignment.Bottom;
            ms.Margin = new Thickness(0, 0, 0, 8);
            pingRow.Children.Add(ms);
        }

        var body = new StackPanel { Spacing = 6, Children = { plate, placeRow, pingRow } };
        var face = new Border
        {
            Width = _cardWidth,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(8, 8, 8, 4),
            Background = Gradient("#1B2E5C", "#0E1936"),
            BorderBrush = server.Selected ? Scene.B(_accent) : null,
        };
        face.Child = body;
        var card = Sticker(face, right: index % 2 == 1, string.Format(L.StickerConnectTo, server.Name));
        if (server.Selected)
        {
            face.BorderThickness = new Thickness(3.5);
            face.BoxShadow = BoxShadows.Parse($"0 0 22 2 {Glow(_accent)}, 0 5 0 0 #8C000000");
        }
        ToolTip.SetTip(card, string.Format(L.StickerConnectTo, server.Name));
        var id = server.IndexId;
        card.Click += (_, _) => Actions.Select(id);
        if (!server.Selected)
        {
            return card;
        }
        // The card the person sits on gets a tag stuck over its corner.
        return new Panel
        {
            Children =
            {
                card,
                new Border
            {
                Background = Scene.B(_accent),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -14, -12, 0),
                IsHitTestVisible = false,
                RenderTransform = TransformOperations.Parse("rotate(8deg)"),
                Child = new TextBlock { Text = L.StickerHere, FontFamily = StrokeText.Heavy, FontSize = 13, Foreground = Brushes.White },
            },
            },
        };
    }

    private void UpdateActions(EditionSnapshot s)
    {
        var list = Partner.Pack.ActionList;
        var key = $"{s.BusyAction}|{string.Join("|", list.Select(a => a.Id + a.Label + a.Icon))}";
        if (key == _actionsKey)
        {
            return;
        }
        _actionsKey = key;
        _actions.Children.Clear();
        for (var i = 0; i < list.Count; i++)
        {
            var action = list[i];
            var (caption, does) = SplitLabel(action.Label);
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (action.Id == s.BusyAction)
            {
                head.Children.Add(new Avalonia.Controls.Shapes.Path { Classes = { "spinner" }, Width = 13, Height = 13, Stroke = Scene.B(Ink), VerticalAlignment = VerticalAlignment.Center });
            }
            else if (PartnerActionIcons.Of(action.Icon) is { } glyph)
            {
                head.Children.Add(new PathIcon { Width = 14, Height = 14, Data = glyph, Foreground = Scene.B(_accent), VerticalAlignment = VerticalAlignment.Center });
            }
            head.Children.Add(new TextBlock
            {
                Text = caption.ToUpper(CultureInfo.CurrentUICulture),
                FontFamily = StrokeText.Heavy,
                FontSize = 17,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var words = new StackPanel { Spacing = 1, Children = { head } };
            if (does.IsNotEmpty())
            {
                words.Children.Add(Scene.Text(does, 11.5, Soft));
            }
            var face = new Border
            {
                Background = Scene.B("#16264A"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 7, 14, 8),
                Child = words,
            };
            var button = Sticker(face, right: i % 2 == 1, action.Label);
            button.IsEnabled = s.BusyAction == null || s.BusyAction == action.Id;
            button.Click += (_, _) => Actions.RunAction(action, button);
            _actions.Children.Add(button);
        }
        _actions.IsVisible = list.Count > 0;
    }

    /// <summary>The term and traffic on a brass ticket; a press opens where to renew, when the provider gave it.</summary>
    private void UpdatePlan(SceneAccount? account)
    {
        var key = account is { HasPlan: true } a ? $"{a.DaysLeft}|{a.Expiry}|{a.Traffic}|{a.Warn}|{a.Bad}|{a.RenewUrl}" : null;
        if (key == _planKey)
        {
            return;
        }
        _planKey = key;
        if (account is not { HasPlan: true })
        {
            _plan.Content = null;
            return;
        }
        var lines = new StackPanel { Spacing = 1 };
        lines.Children.Add(new TextBlock { Text = L.StickerPlan, FontFamily = StrokeText.Heavy, FontSize = 13, LetterSpacing = 1, Foreground = Scene.B(Bronze) });
        var term = account.DaysLeft is { } days ? string.Format(L.StickerDaysLeft, days) : account.Expiry;
        foreach (var line in new[] { term, account.Traffic }.Where(t => t.IsNotEmpty()))
        {
            lines.Children.Add(Scene.Text(line!, 13, account.Bad ? "#8A1C1C" : Bronze, FontWeight.SemiBold));
        }
        var face = new Border
        {
            Background = Brass(),
            BorderBrush = Scene.B("#6B4420"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 8, 18, 9),
            Child = lines,
        };
        var ticket = Sticker(face, right: true, L.StickerPlan);
        ticket.HorizontalAlignment = HorizontalAlignment.Left;
        if (account.RenewUrl is { } url)
        {
            ticket.Click += (_, _) => Actions.Open(url);
        }
        else
        {
            ticket.IsHitTestVisible = false;
            ticket.Focusable = false;
        }
        _plan.Content = ticket;
    }

    // ---- layout ----

    /// <summary>Wide: the scene on the right at full height, the column on a scrim on the left. Narrow: one scrolling column.</summary>
    private void Place()
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }
        var narrow = w < NarrowBelow || h < 440;
        if (narrow != _narrow)
        {
            _narrow = narrow;
            Rebuild(narrow);
        }
        if (narrow)
        {
            return;
        }

        var left = Math.Clamp(w * 0.4, 340, 440);
        _column.Width = left - 56;
        var ratio = _artBitmap is { } bmp && bmp.Size.Height > 0 ? bmp.Size.Width / bmp.Size.Height : 1.28;
        var artH = h;
        var artW = artH * ratio;
        if (artW < w * 0.62)
        {
            artW = w * 0.62;
            artH = artW / ratio;
        }
        _art.Width = artW;
        _art.Height = artH;
        var x0 = w - artW;
        var y0 = h - artH;
        // Where the art starts inside the window, its edge melts into the night.
        _seam.IsVisible = x0 > 0;
        _seam.Margin = new Thickness(Math.Max(0, x0 - 1), 0, 0, 0);
        _seam.Width = 180;
        var solid = left / w;
        _scrim.Background = Fade(horizontal: true, 0, 0.97, solid, 0.9, Math.Min(1, (left + 170) / w), 0);

        _serverBlock.Margin = new Thickness(left + 8, 22, 18, 0);
        // The cards share a row, narrower before they wrap; a second row only when it ends above the heroes' heads
        // (in the pack's scene about 44% down).
        const double min = 124, max = 156, cardHeight = 150, cardsTop = 22 + 34 + 12 + 24;
        var gap = _cards.ItemSpacing;
        var count = Math.Max(1, _last?.Servers.Count ?? 1);
        // Slack: the tilted corners and the buttons' focus rings reach past a card's width.
        var room = w - left - 8 - 18 - _cardsPad.Padding.Left - _cardsPad.Padding.Right - 24;
        var perRow = Math.Max(1, (int)Math.Floor((room + gap) / (min + 3 + gap)));
        var inRow = Math.Min(count, perRow);
        var heads = y0 + (0.44 * artH);
        var rows = Math.Clamp((int)Math.Floor((heads - cardsTop + gap) / (cardHeight + gap)), 1, 2);
        SetCards(Math.Floor(Math.Clamp(((room - ((inRow - 1) * gap)) / inRow) - 3, min, max)), perRow * rows);

        // The bubble comes from the heroes' heads (in the pack's scene about 45% across and 44% down): it sits to their
        // upper left when there is room between the column and them, under the server cards.
        var size = _bubble.SizeFor(260);
        var headX = x0 + (0.44 * artW);
        var headY = y0 + (0.47 * artH);
        var cardsBottom = _serverBlock.IsVisible ? _serverBlock.Margin.Top + _serverBlock.Bounds.Height : 0;
        var bx = headX - size.Width;
        var by = headY - size.Height;
        _bubble.IsVisible = size.Width > 0 && bx >= left + 4 && by >= cardsBottom + 6;
        _bubble.Margin = new Thickness(bx, by, 0, 0);
    }

    private void SetCards(double width, int limit)
    {
        if ((width, limit) == (_cardWidth, _cardLimit))
        {
            return;
        }
        (_cardWidth, _cardLimit) = (width, limit);
        if (_last != null)
        {
            UpdateCards(_last);
        }
    }

    private void Rebuild(bool narrow)
    {
        foreach (var part in new Control[] { _column, _serverBlock, _art })
        {
            Detach(part);
        }
        _root.Children.Clear();

        if (narrow)
        {
            _column.Width = double.NaN;
            _art.Width = double.NaN;
            _art.Height = double.NaN;
            _art.Stretch = Stretch.UniformToFill;
            _art.HorizontalAlignment = HorizontalAlignment.Center;
            _art.VerticalAlignment = VerticalAlignment.Center;
            var band = new Border { Height = 210, ClipToBounds = true, Child = _art };
            _serverBlock.Margin = default;
            // One scrolling column: every server gets its card.
            SetCards(140, int.MaxValue);
            var stack = new StackPanel
            {
                Spacing = 18,
                Children =
                {
                    band,
                    new Border { Padding = new Thickness(18, 0, 18, 24), Child = new StackPanel { Spacing = 22, Children = { _column, _serverBlock } } },
                },
            };
            _root.Children.Add(new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            return;
        }

        _art.Stretch = Stretch.Uniform;
        _art.HorizontalAlignment = HorizontalAlignment.Right;
        _art.VerticalAlignment = VerticalAlignment.Bottom;
        _serverBlock.VerticalAlignment = VerticalAlignment.Top;
        var columnScroll = new ScrollViewer
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border { Padding = new Thickness(28, 22, 28, 22), Child = _column },
        };
        _root.Children.Add(_art);
        _root.Children.Add(_seam);
        _root.Children.Add(_scrim);
        _root.Children.Add(_serverBlock);
        _root.Children.Add(_bubble);
        _root.Children.Add(columnScroll);
    }

    private static void Detach(Control part)
    {
        switch (part.Parent)
        {
            case Panel panel:
                panel.Children.Remove(part);
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
            case ContentControl content:
                content.Content = null;
                break;
        }
    }

    // ---- paint ----

    private static IBrush Brass() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#F5E0A8"), 0),
            new GradientStop(Color.Parse("#D6AC68"), 0.45),
            new GradientStop(Color.Parse("#AD7B40"), 0.6),
            new GradientStop(Color.Parse("#CF9F5E"), 1),
        },
    };

    private static IBrush Gradient(string top, string bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(top), 0), new GradientStop(Color.Parse(bottom), 1) },
    };

    /// <summary>The night from opaque to clear: pairs of (offset, alpha), left to right or top to bottom.</summary>
    private static IBrush Fade(bool horizontal, params double[] stops)
    {
        var night = Color.Parse(Night);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = horizontal ? new RelativePoint(1, 0, RelativeUnit.Relative) : new RelativePoint(0, 1, RelativeUnit.Relative),
        };
        for (var i = 0; i + 1 < stops.Length; i += 2)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(stops[i + 1] * 255), night.R, night.G, night.B), stops[i]));
        }
        return brush;
    }

    private static string Shade(string hex, double factor)
    {
        var c = Color.Parse(hex);
        return $"#{(byte)(c.R * factor):X2}{(byte)(c.G * factor):X2}{(byte)(c.B * factor):X2}";
    }

    private static string Glow(string hex) => $"#B3{hex.TrimStart('#')}";
}

/// <summary>A comic speech bubble with a black outline and a tail down to the right, toward whoever says it.</summary>
public sealed class SpeechBubble : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<SpeechBubble, string?>(nameof(Text));
    private const double Tail = 22;
    private const double PadX = 16;
    private const double PadY = 11;
    private FormattedText? _text;

    static SpeechBubble()
    {
        AffectsMeasure<SpeechBubble>(TextProperty);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _text = Format(availableSize.Width);
        return _text == null ? default : Outer(_text);
    }

    /// <summary>The bubble's size at most <paramref name="maxWidth"/> wide, outside of layout (for placing it).</summary>
    public Size SizeFor(double maxWidth) => Format(maxWidth) is { } text ? Outer(text) : default;

    private FormattedText? Format(double maxWidth) => string.IsNullOrEmpty(Text)
        ? null
        : new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 15, new SolidColorBrush(Color.Parse("#0B1430")))
        {
            MaxTextWidth = Math.Max(80, Math.Min(240, maxWidth) - (PadX * 2)),
        };

    private static Size Outer(FormattedText text) => new(text.Width + (PadX * 2) + 3, text.Height + (PadY * 2) + Tail + 3);

    public override void Render(DrawingContext context)
    {
        if (_text == null)
        {
            return;
        }
        var w = Bounds.Width;
        var h = Bounds.Height;
        var box = new Rect(1.5, 1.5, w - 3, h - Tail - 3);
        var tail = new StreamGeometry();
        using (var g = tail.Open())
        {
            g.BeginFigure(new Point(box.Right - 58, box.Bottom - 6), true);
            g.LineTo(new Point(box.Right - 26, box.Bottom - 6));
            g.LineTo(new Point(w - 2, h - 2));
            g.EndFigure(true);
        }
        var shape = new CombinedGeometry(GeometryCombineMode.Union, new RectangleGeometry(box, 18, 18), tail);
        using (context.PushTransform(Matrix.CreateTranslation(0, 4)))
        {
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)), null, shape);
        }
        context.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 3, lineJoin: PenLineJoin.Round), shape);
        context.DrawText(_text, new Point(PadX + 1.5, PadY + 1.5));
    }
}
