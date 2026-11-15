using Avalonia.Controls.Shapes;
using Avalonia.Layout;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>The colours a connect control is drawn with on the ground it stands on.</summary>
/// <param name="Fill">Its fill (the brand's action colour); <paramref name="Label"/> reads on it.</param>
/// <param name="Outer">Text beside or under it, on the ground.</param>
/// <param name="Track">A switch that is off.</param>
public sealed record ConnectInk(string Fill, string Label, string Outer, string Track, string Good, string Warn, string Checking);

/// <summary>
/// The theme's connect control: round, capsule, bar or toggle. Its shape is the provider's; what it does and the state
/// colours are V2pn's (the protected green, attention amber), the same as everywhere else in the app.
/// </summary>
public sealed class ThemeConnect : UserControl
{
    private readonly string _style;
    private readonly ConnectInk _ink;
    private readonly IEditionActions _actions;
    private readonly Button _button;
    private readonly Border _fill;
    private readonly TextBlock _label;
    private readonly TextBlock _caption;
    private readonly Ellipse _dot = new() { Width = 9, Height = 9, StrokeThickness = 2 };
    private readonly Ellipse _ring = new() { StrokeThickness = 3, IsHitTestVisible = false };
    private readonly Avalonia.Controls.Shapes.Path _power;
    private readonly Border _thumb = new();
    private double _scale = 1;
    private EditionSnapshot? _last;

    public ThemeConnect(string style, ConnectInk ink, IEditionActions actions, FontFamily font)
    {
        _style = style;
        _ink = ink;
        _actions = actions;
        _label = Scene.Text("", 15, ink.Label, font, FontWeight.SemiBold);
        _caption = Scene.Text("", 15, ink.Outer, font, FontWeight.SemiBold);
        _caption.HorizontalAlignment = HorizontalAlignment.Center;
        _power = Scene.Power(ink.Label, 18);
        _dot.Stroke = Scene.B(ink.Label);

        Control content = style switch
        {
            V2pnThemeConnect.Round => _power,
            V2pnThemeConnect.Toggle => _thumb,
            _ => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _dot, _power, _label } },
        };
        _button = Scene.Button(content, ink.Fill, ink.Label, 14, default);
        _fill = (Border)_button.Content!;
        _button.Click += (_, _) => _actions.ToggleConnect();

        switch (style)
        {
            case V2pnThemeConnect.Round:
                _power.HorizontalAlignment = HorizontalAlignment.Center;
                Content = new StackPanel
                {
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { new Panel { HorizontalAlignment = HorizontalAlignment.Center, Children = { _ring, _button } }, _caption },
                };
                break;
            case V2pnThemeConnect.Toggle:
                _fill.Child = _thumb;
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { _button, _caption } };
                _caption.HorizontalAlignment = HorizontalAlignment.Left;
                break;
            default:
                Content = _button;
                break;
        }
        Resize(1);
    }

    /// <summary>The connect control is as wide as its place (a bar always is).</summary>
    public bool Stretch
    {
        set
        {
            if (_style is V2pnThemeConnect.Bar or V2pnThemeConnect.Capsule)
            {
                _button.HorizontalAlignment = value ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            }
        }
    }

    /// <summary>The label under a round button or beside a switch; off where the row around it says it already.</summary>
    public bool ShowCaption
    {
        set => _caption.IsVisible = value;
    }

    /// <summary>Grows or shrinks the control with the window (1 is the wide window's size).</summary>
    public void Resize(double scale)
    {
        _scale = scale;
        switch (_style)
        {
            case V2pnThemeConnect.Round:
            {
                var size = Math.Round(148 * scale);
                _button.Width = _button.Height = size;
                _button.CornerRadius = new CornerRadius(size / 2);
                _fill.CornerRadius = new CornerRadius(size / 2);
                _ring.Width = _ring.Height = size + 14;
                _ring.HorizontalAlignment = HorizontalAlignment.Center;
                _ring.VerticalAlignment = VerticalAlignment.Center;
                _power.Width = _power.Height = Math.Round(size * 0.3);
                _power.StrokeThickness = Math.Max(2.4, size / 34);
                _caption.FontSize = scale < 0.85 ? 14 : 16;
                break;
            }
            case V2pnThemeConnect.Capsule:
                _button.Height = Math.Round(50 * Math.Max(scale, 0.92));
                _button.CornerRadius = new CornerRadius(999);
                _fill.CornerRadius = new CornerRadius(999);
                _fill.Padding = new Thickness(26, 0);
                _button.MinWidth = 200;
                break;
            case V2pnThemeConnect.Bar:
                _button.Height = Math.Round(54 * Math.Max(scale, 0.9));
                _fill.Padding = new Thickness(20, 0);
                _button.HorizontalAlignment = HorizontalAlignment.Stretch;
                break;
            case V2pnThemeConnect.Toggle:
            {
                var height = Math.Round(40 * Math.Max(scale, 0.85));
                _button.Width = Math.Round(height * 1.8);
                _button.Height = height;
                _button.CornerRadius = new CornerRadius(height / 2);
                _fill.CornerRadius = new CornerRadius(height / 2);
                _fill.Padding = new Thickness(4);
                _thumb.Width = _thumb.Height = height - 8;
                _thumb.CornerRadius = new CornerRadius((height - 8) / 2);
                _thumb.VerticalAlignment = VerticalAlignment.Center;
                _caption.VerticalAlignment = VerticalAlignment.Center;
                break;
            }
        }
        if (_last != null)
        {
            Update(_last);
        }
    }

    public void Update(EditionSnapshot s)
    {
        _last = s;
        var on = s.ConnectLabel != L.Connect;
        var state = s.StateKind switch
        {
            "safe" => _ink.Good,
            "warn" => _ink.Warn,
            "checking" => _ink.Checking,
            _ => null,
        };
        _label.Text = s.ConnectLabel;
        _caption.Text = s.ConnectLabel;
        _button.IsEnabled = s.HasServer;
        Avalonia.Automation.AutomationProperties.SetName(_button, s.ConnectLabel);
        ToolTip.SetTip(_button, s.StateLabel);
        _dot.IsVisible = state != null;
        if (state != null)
        {
            _dot.Fill = Scene.B(state);
        }
        switch (_style)
        {
            case V2pnThemeConnect.Round:
                // V2pn's state as a ring around the provider's button: green while it protects.
                _ring.Stroke = state == null ? Brushes.Transparent : Scene.B(state);
                break;
            case V2pnThemeConnect.Toggle:
                _fill.Background = Scene.B(on ? state ?? _ink.Checking : _ink.Track);
                _thumb.Background = Scene.B(on ? "#FFFFFF" : _ink.Fill);
                _thumb.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                break;
        }
    }
}

/// <summary>Proxy or VPN: two halves, the one in use filled with the ink of the page.</summary>
public sealed class ThemeSegment : UserControl
{
    private readonly Button _proxy;
    private readonly Button _vpn;
    private readonly string _on;
    private readonly string _onText;
    private readonly string _offText;

    public ThemeSegment(string track, string on, string onText, string offText, FontFamily font, Func<double, CornerRadius> r, IEditionActions actions, double height)
    {
        _on = on;
        _onText = onText;
        _offText = offText;
        Button Half(string text, bool vpn)
        {
            var button = Scene.Button(Scene.Text(text, 13, offText, font, FontWeight.Medium), null, offText, 10, new Thickness(14, 0), height: height - 6);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Click += (_, _) => actions.SetVpn(vpn);
            return button;
        }
        _proxy = Half(L.SceneProxy, false);
        _vpn = Half("VPN", true);
        var grid = new Grid { ColumnDefinitions = new("*,*") };
        ServerBrowser.Put(grid, _proxy, 0, 0);
        ServerBrowser.Put(grid, _vpn, 0, 1);
        Content = new Border { Background = Scene.B(track), CornerRadius = r(12), Padding = new Thickness(3), Child = grid, Height = height };
    }

    public void Update(bool vpn)
    {
        foreach (var (button, on) in new[] { (_proxy, !vpn), (_vpn, vpn) })
        {
            if (button.Content is Border { Child: TextBlock label } border)
            {
                border.Background = on ? Scene.B(_on) : Brushes.Transparent;
                label.Foreground = Scene.B(on ? _onText : _offText);
            }
        }
    }
}

/// <summary>
/// One part of a theme's blocks (plan, traffic, actions, banner, news, art): its own content, shown only when the
/// subscription has something for it. The card around it is the template's.
/// </summary>
public sealed class ThemeBlock : UserControl
{
    private readonly string _kind;
    private readonly V2pnThemeColors _c;
    private readonly string _fill;
    private readonly string _fieldFill;
    private readonly FontFamily _font;
    private readonly IEditionActions _actions;
    private readonly StackPanel _body = new() { Spacing = 6 };
    private readonly TextBlock _a;
    private readonly TextBlock _b;
    private readonly TextBlock _cText;
    private readonly Border _barTrack;
    private readonly Border _bar;
    private readonly Border _banner;
    private readonly TextBlock _bannerText;
    private readonly Button _bannerButton;
    private readonly TextBlock _bannerButtonText;
    private readonly Button _link;
    private readonly WrapPanel _buttons = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
    private string _buttonsKey = string.Empty;
    private SceneAccount? _account;
    private double _fraction;

    /// <param name="fill">The card's fill; <paramref name="fieldFill"/> is for the buttons and the bar's track inside it.</param>
    public ThemeBlock(string kind, V2pnThemeColors c, string fill, string fieldFill, FontFamily font, IEditionActions actions, Bitmap? picture, Func<double, CornerRadius> r)
    {
        _kind = kind;
        _c = c;
        _fill = fill;
        _fieldFill = fieldFill;
        _font = font;
        _actions = actions;
        _a = Wrap(Scene.Text("", 15, c.Ink, font, FontWeight.SemiBold));
        _b = Wrap(Scene.Text("", 13, c.Soft, font));
        _cText = Wrap(Scene.Text("", 13, c.Soft, font));
        _bar = new Border { CornerRadius = r(4), Background = Scene.B(c.Secondary), HorizontalAlignment = HorizontalAlignment.Left };
        _barTrack = new Border { Height = 6, CornerRadius = r(4), Background = Scene.B(c.Line), Child = _bar, ClipToBounds = true, Margin = new Thickness(0, 4, 0, 2) };
        _barTrack.SizeChanged += (_, _) => _bar.Width = Math.Max(0, _barTrack.Bounds.Width * _fraction);
        _bannerText = Wrap(Scene.Text("", 13, c.Ink, font));
        _bannerButtonText = Scene.Text("", 13, c.AccentText, font, FontWeight.SemiBold);
        _bannerButton = Scene.Button(_bannerButtonText, null, c.AccentText, 6, new Thickness(0, 2));
        _bannerButton.HorizontalAlignment = HorizontalAlignment.Left;
        _bannerButton.Click += (_, _) => Open(_account?.Banner?.ButtonUrl);
        _banner = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 2, 0, 2),
            Child = new StackPanel { Spacing = 6, Children = { _bannerText, _bannerButton } },
        };
        var linkText = Scene.Text(L.ThemeMore + " ↗", 13, c.AccentText, font, FontWeight.SemiBold);
        _link = Scene.Button(linkText, null, c.AccentText, 6, new Thickness(0, 2));
        _link.HorizontalAlignment = HorizontalAlignment.Left;
        _link.Click += (_, _) => Open(_account?.AnnounceUrl);

        switch (kind)
        {
            case V2pnThemeBlocks.Plan:
                _body.Children.Add(_a);
                _body.Children.Add(_b);
                _body.Children.Add(_cText);
                break;
            case V2pnThemeBlocks.Traffic:
                _body.Children.Add(_b);
                _body.Children.Add(_barTrack);
                break;
            case V2pnThemeBlocks.Actions:
                _body.Children.Add(_buttons);
                break;
            case V2pnThemeBlocks.Banner:
                _body.Spacing = 10;
                _body.Children.Add(_a);
                _body.Children.Add(_banner);
                _a.FontSize = 13.5;
                break;
            case V2pnThemeBlocks.News:
                _body.Children.Add(_b);
                _body.Children.Add(_link);
                break;
            case V2pnThemeBlocks.Art:
                if (picture != null)
                {
                    _body.Children.Add(new Border
                    {
                        CornerRadius = r(12),
                        ClipToBounds = true,
                        Child = new Image { Source = picture, Stretch = Stretch.UniformToFill, MaxHeight = 180 },
                    });
                }
                break;
        }
        Content = _body;
        IsVisible = kind == V2pnThemeBlocks.Art && picture != null;
    }

    private static TextBlock Wrap(TextBlock text)
    {
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        return text;
    }

    private void Open(string? url)
    {
        if (url.IsNotEmpty())
        {
            _actions.OpenUrl(url!);
        }
    }

    /// <summary>Shows what the snapshot has for this block; hides the block when it has nothing.</summary>
    public void Update(EditionSnapshot s)
    {
        var account = s.Account;
        _account = account;
        switch (_kind)
        {
            case V2pnThemeBlocks.Plan:
            {
                _a.Text = account?.Plan ?? string.Empty;
                _a.IsVisible = account?.Plan != null;
                _b.Text = account?.Devices ?? string.Empty;
                _b.IsVisible = account?.Devices != null;
                _cText.Text = account?.Expiry ?? string.Empty;
                _cText.IsVisible = account?.Expiry != null;
                _cText.Foreground = Scene.B(account?.Bad == true ? _c.Bad : account?.Warn == true ? _c.Warn : _c.Soft);
                IsVisible = _a.IsVisible || _b.IsVisible || _cText.IsVisible;
                break;
            }
            case V2pnThemeBlocks.Traffic:
                _b.Text = account?.Traffic ?? string.Empty;
                _barTrack.IsVisible = account?.ShowBar == true;
                _fraction = Math.Clamp(account?.Fraction ?? 0, 0, 1);
                _bar.Width = Math.Max(0, _barTrack.Bounds.Width * _fraction);
                _bar.Background = Scene.B(account?.Bad == true ? _c.Bad : account?.Warn == true ? _c.Warn : _c.Secondary);
                IsVisible = account?.Traffic != null;
                break;
            case V2pnThemeBlocks.Actions:
                UpdateButtons(s.Actions, s.ActionRunning);
                IsVisible = s.Actions.Count > 0;
                break;
            case V2pnThemeBlocks.Banner:
            {
                _a.Text = account?.Incident ?? string.Empty;
                _a.IsVisible = account?.Incident != null;
                _a.Foreground = Scene.B(account?.IncidentSevere == true ? _c.Bad : _c.Warn);
                var banner = account?.Banner;
                _banner.IsVisible = banner != null;
                if (banner != null)
                {
                    _banner.BorderBrush = Scene.B(banner.Tone switch
                    {
                        SubPanelHeaders.ToneAttention => _c.Bad,
                        SubPanelHeaders.ToneOk => _c.Good,
                        _ => _c.Secondary,
                    });
                    _bannerText.Text = banner.Text;
                    _bannerButtonText.Text = banner.ButtonText is { } label ? label + " ↗" : string.Empty;
                    _bannerButton.IsVisible = banner.ButtonText != null && banner.ButtonUrl != null;
                }
                IsVisible = _a.IsVisible || _banner.IsVisible;
                break;
            }
            case V2pnThemeBlocks.News:
                _b.Text = account?.Announce ?? string.Empty;
                _link.IsVisible = account?.AnnounceUrl != null;
                IsVisible = account?.Announce != null;
                break;
        }
    }

    /// <summary>The provider's buttons: real buttons in a row that wraps, so four fit the narrow window too.</summary>
    private void UpdateButtons(IReadOnlyList<V2pnPartnerAction> actions, string? running)
    {
        var key = running + "|" + string.Join("|", actions.Select(a => a.Id + a.Label + a.Icon));
        if (key == _buttonsKey)
        {
            return;
        }
        _buttonsKey = key;
        _buttons.Children.Clear();
        foreach (var action in actions)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (PartnerActionIcons.Of(action.Icon) is { } glyph)
            {
                content.Children.Add(new PathIcon { Width = 15, Height = 15, Data = glyph, Foreground = Scene.B(_c.AccentText), VerticalAlignment = VerticalAlignment.Center });
            }
            content.Children.Add(Scene.Text(action.Id == running ? action.Label + " …" : action.Label, 13.5, _c.Ink, _font, FontWeight.Medium));
            var button = Scene.Button(content, _fieldFill, _c.Ink, 12, new Thickness(14, 0), height: 40);
            button.IsEnabled = running == null || action.Id == running;
            Avalonia.Automation.AutomationProperties.SetName(button, action.Label);
            var id = action.Id;
            button.Click += (_, _) => _actions.RunAction(id, button);
            _buttons.Children.Add(button);
        }
    }
}

/// <summary>
/// A theme's column of parts: they stack top to bottom at their own height, except one (the server list) that takes
/// what the window leaves, never less than its minimum; when that does not fit, the whole column scrolls.
/// </summary>
public sealed class ThemeStack : ScrollViewer
{
    private readonly FillPanel _panel = new();

    public ThemeStack(double spacing, double fillMin, Thickness margin)
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _panel.Spacing = spacing;
        _panel.FillMin = fillMin;
        _panel.Margin = margin;
        Content = _panel;
        SizeChanged += (_, e) =>
        {
            var viewport = Math.Max(0, e.NewSize.Height - margin.Top - margin.Bottom);
            if (Math.Abs(viewport - _panel.Viewport) > 0.5)
            {
                _panel.Viewport = viewport;
                _panel.InvalidateMeasure();
            }
        };
    }

    public Avalonia.Controls.Controls Parts => _panel.Children;

    /// <summary>The part that fills the rest; null stacks every part at its own height.</summary>
    public Control? Fill
    {
        get => _panel.Fill;
        set
        {
            _panel.Fill = value;
            _panel.InvalidateMeasure();
        }
    }

    private sealed class FillPanel : Panel
    {
        public double Spacing { get; set; }
        public double FillMin { get; set; }
        public double Viewport { get; set; }
        public Control? Fill { get; set; }

        private double FillHeight(IReadOnlyList<Control> parts, double used) =>
            Fill != null && parts.Contains(Fill) ? Math.Floor(Math.Max(FillMin, Viewport - used)) : 0;

        protected override Size MeasureOverride(Size availableSize)
        {
            var parts = Children.Where(c => c.IsVisible).ToList();
            var width = availableSize.Width;
            double used = 0;
            foreach (var part in parts.Where(p => p != Fill))
            {
                part.Measure(new Size(width, double.PositiveInfinity));
                used += part.DesiredSize.Height;
            }
            used += Math.Max(0, parts.Count - 1) * Spacing;
            var fill = FillHeight(parts, used);
            if (Fill != null && parts.Contains(Fill))
            {
                Fill.Measure(new Size(width, fill));
            }
            var desiredWidth = double.IsInfinity(width) ? parts.Select(p => p.DesiredSize.Width).DefaultIfEmpty(0).Max() : width;
            return new Size(desiredWidth, used + fill);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var parts = Children.Where(c => c.IsVisible).ToList();
            var used = parts.Where(p => p != Fill).Sum(p => p.DesiredSize.Height) + (Math.Max(0, parts.Count - 1) * Spacing);
            var fill = Fill != null && parts.Contains(Fill) ? Math.Max(FillMin, finalSize.Height - used) : 0;
            double y = 0;
            foreach (var part in parts)
            {
                var height = part == Fill ? fill : part.DesiredSize.Height;
                part.Arrange(new Rect(0, y, finalSize.Width, height));
                y += height + Spacing;
            }
            return finalSize;
        }
    }
}
