using Avalonia.Controls.Shapes;
using Avalonia.Layout;

namespace v2rayN.Desktop.Views.EditionLooks;

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

    public const string Plan = "plan";
    public const string Traffic = "traffic";
    public const string Actions = "actions";
    public const string Banner = "banner";
    public const string News = "news";

    /// <param name="fill">The card's fill; <paramref name="fieldFill"/> is for the buttons and the bar's track inside it.</param>
    public ThemeBlock(string kind, V2pnThemeColors c, string fill, string fieldFill, FontFamily font, IEditionActions actions, Func<double, CornerRadius> r)
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
            case Plan:
                _body.Children.Add(_a);
                _body.Children.Add(_b);
                _body.Children.Add(_cText);
                break;
            case Traffic:
                _body.Children.Add(_b);
                _body.Children.Add(_barTrack);
                break;
            case Actions:
                _body.Children.Add(_buttons);
                break;
            case Banner:
                _body.Spacing = 10;
                _body.Children.Add(_a);
                _body.Children.Add(_banner);
                _a.FontSize = 13.5;
                break;
            case News:
                _body.Children.Add(_b);
                _body.Children.Add(_link);
                break;
        }
        Content = _body;
        IsVisible = false;
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
            case Plan:
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
            case Traffic:
                _b.Text = account?.Traffic ?? string.Empty;
                _barTrack.IsVisible = account?.ShowBar == true;
                _fraction = Math.Clamp(account?.Fraction ?? 0, 0, 1);
                _bar.Width = Math.Max(0, _barTrack.Bounds.Width * _fraction);
                _bar.Background = Scene.B(account?.Bad == true ? _c.Bad : account?.Warn == true ? _c.Warn : _c.Secondary);
                IsVisible = account?.Traffic != null;
                break;
            case Actions:
                UpdateButtons(s.Actions, s.ActionRunning);
                IsVisible = s.Actions.Count > 0;
                break;
            case Banner:
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
            case News:
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
