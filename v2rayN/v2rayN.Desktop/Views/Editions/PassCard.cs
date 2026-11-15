using Avalonia.Layout;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// The Pass: the provider's card on V2pn's own window, like a bank card in Wallet. Its brand area (the theme's colour,
/// gradient or picture) carries the logo, the plan, the devices and the term, the days left in big type, the traffic as
/// a bar and the provider's links. Everything else on the window stays plain V2pn. It hides when the provider's panel
/// sends nothing about the subscription.
/// </summary>
public sealed class PassCard : UserControl
{
    private readonly V2pnThemeArea _area;
    private readonly Action<string> _open;
    private readonly TextBlock _plan;
    private readonly TextBlock _meta;
    private readonly TextBlock _tag;
    private readonly Border _tagBox;
    private readonly TextBlock _days;
    private readonly TextBlock _daysWord;
    private readonly StackPanel _daysBox;
    private readonly Border _track;
    private readonly Border _bar;
    private readonly TextBlock _traffic;
    private readonly Grid _trafficBox;
    private readonly Button _renew;
    private readonly Button _cabinet;
    private readonly Grid _layout;
    private readonly StackPanel _left;
    private readonly StackPanel _right;
    private double _fraction;
    private string? _renewUrl;
    private string? _cabinetUrl;

    public PassCard(V2pnPartner partner, V2pnThemeColors c, Action<string> open)
    {
        _area = c.Area;
        _open = open;
        var ink = _area.Ink;
        var soft = _area.Soft;
        var light = V2pnColor.Parse(ink).Luminance > 0.4;
        var veil = V2pnColor.Parse(ink).WithAlpha(light ? 0.16 : 0.10);

        // The logo made for the card's ground, or the provider's mark and name.
        var logoFile = light ? partner.Pack.Logo : partner.Pack.LogoOnLight ?? partner.Pack.Logo;
        Control brand = Scene.Picture(partner, logoFile) is { } logo
            ? new Image { Source = logo, Height = 20, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left }
            : new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new Border
                    {
                        Width = 22,
                        Height = 22,
                        CornerRadius = new CornerRadius(7),
                        Background = Scene.B(ink),
                        Child = Scene.Text(partner.Name[..1].ToUpperInvariant(), 12, _area.Action, Scene.Geist, FontWeight.Bold).Also(t =>
                        {
                            t.HorizontalAlignment = HorizontalAlignment.Center;
                            t.VerticalAlignment = VerticalAlignment.Center;
                        }),
                    },
                    Scene.Text(partner.Name, 15, ink, Scene.Geist, FontWeight.SemiBold),
                },
            };

        _tag = Scene.Text("", 11.5, ink, Scene.Geist, FontWeight.SemiBold);
        _tag.TextTrimming = TextTrimming.CharacterEllipsis;
        _tagBox = new Border { Background = Scene.B(veil), CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 3), HorizontalAlignment = HorizontalAlignment.Left, Child = _tag };
        _plan = Scene.Text("", 20, ink, Scene.Geist, FontWeight.SemiBold);
        _plan.TextTrimming = TextTrimming.CharacterEllipsis;
        _meta = Scene.Text("", 12.5, soft, Scene.Geist);
        _meta.TextTrimming = TextTrimming.CharacterEllipsis;
        _left = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Stretch };
        _left.Children.Add(brand.Also(b => b.Margin = new Thickness(0, 0, 0, 8)));
        _left.Children.Add(_tagBox);
        _left.Children.Add(_plan);
        _left.Children.Add(_meta);

        _days = Scene.Text("", 38, ink, Scene.Geist, FontWeight.Medium);
        _days.LetterSpacing = -1;
        _daysWord = Scene.Text("", 14, soft, Scene.Geist, FontWeight.Medium);
        _daysWord.VerticalAlignment = VerticalAlignment.Bottom;
        _daysWord.Margin = new Thickness(0, 0, 0, 6);
        _daysBox = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _days, _daysWord } };

        _bar = new Border { CornerRadius = new CornerRadius(3), Background = Scene.B(ink), HorizontalAlignment = HorizontalAlignment.Left };
        _track = new Border { Height = 5, CornerRadius = new CornerRadius(3), Background = Scene.B(V2pnColor.Parse(ink).WithAlpha(0.24)), ClipToBounds = true, Child = _bar };
        _track.SizeChanged += (_, _) => SizeBar();
        _traffic = Scene.Text("", 12, soft, Scene.Geist);
        _traffic.HorizontalAlignment = HorizontalAlignment.Right;
        _trafficBox = new Grid { RowDefinitions = new("Auto,Auto"), RowSpacing = 6 };
        _trafficBox.Children.Add(_track);
        var label = new Grid { ColumnDefinitions = new("Auto,*"), [Grid.RowProperty] = 1 };
        label.Children.Add(Scene.Text(L.PassTraffic, 12, soft, Scene.Geist));
        label.Children.Add(_traffic.Also(t => Grid.SetColumn(t, 1)));
        _trafficBox.Children.Add(label);

        Button Link(string text, Action click)
        {
            var button = Scene.Button(Scene.Text(text + " ↗", 12.5, ink, Scene.Geist, FontWeight.SemiBold), veil, ink, 999, new Thickness(12, 0), height: 30);
            button.Click += (_, _) => click();
            return button;
        }
        _renew = Link(L.SceneRenew, () => Open(_renewUrl));
        _cabinet = Link(L.SceneCabinet, () => Open(_cabinetUrl));
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _renew, _cabinet } };
        _right = new StackPanel { Spacing = 10, Children = { _daysBox, _trafficBox, links } };

        _layout = new Grid { Margin = new Thickness(18, 16) };
        var card = new Border
        {
            CornerRadius = new CornerRadius(18),
            ClipToBounds = true,
            BoxShadow = BoxShadows.Parse($"0 16 36 -18 {V2pnColor.Parse(_area.Action).WithAlpha(0.8)}"),
            Child = ThemeKit.Area(_area, _area.Kind == V2pnThemeBackgroundKinds.Image && partner.Pack.Theme?.Background.Art is { } art ? Scene.Picture(partner, art) : null, _layout),
        };
        Content = card;
        Arrange(wide: true);
        IsVisible = false;
    }

    /// <summary>Side by side where the stage is wide, the term and traffic under the plan where it is narrow.</summary>
    public void Arrange(bool wide)
    {
        _layout.Children.Clear();
        _layout.ColumnDefinitions.Clear();
        _layout.RowDefinitions.Clear();
        if (wide)
        {
            _layout.ColumnDefinitions = new("*,Auto");
            _layout.ColumnSpacing = 24;
            _right.Width = 230;
            _layout.Children.Add(_left);
            Grid.SetColumn(_right, 1);
            Grid.SetRow(_right, 0);
            _layout.Children.Add(_right);
        }
        else
        {
            _layout.RowDefinitions = new("Auto,Auto");
            _layout.RowSpacing = 12;
            _right.Width = double.NaN;
            _layout.Children.Add(_left);
            Grid.SetColumn(_right, 0);
            Grid.SetRow(_right, 1);
            _layout.Children.Add(_right);
        }
    }

    private void SizeBar() => _bar.Width = Math.Max(_fraction > 0 ? 6 : 0, _track.Bounds.Width * _fraction);

    private void Open(string? url)
    {
        if (url.IsNotEmpty())
        {
            _open(url!);
        }
    }

    public void Update(SceneAccount? account, string partnerName)
    {
        IsVisible = account != null && !account.IsEmpty;
        if (account == null)
        {
            return;
        }
        _plan.Text = account.Plan ?? partnerName;
        _meta.Text = string.Join(" · ", new[] { account.Devices, account.DaysLeft == null ? account.Expiry : null }.Where(x => x.IsNotEmpty()));
        _meta.IsVisible = _meta.Text.Length > 0;
        // The banner first (the provider's offer), else its incident, else the panel's announcement.
        var tag = account.Banner?.Text ?? account.Incident ?? account.Announce;
        _tag.Text = tag ?? string.Empty;
        _tagBox.IsVisible = tag.IsNotEmpty();
        ToolTip.SetTip(_tagBox, tag);
        _daysBox.IsVisible = account.DaysLeft != null;
        if (account.DaysLeft is { } days)
        {
            _days.Text = days.ToString(CultureInfo.CurrentCulture);
            _daysWord.Text = L.DaysWord(days);
            ToolTip.SetTip(_daysBox, account.Expiry);
        }
        _trafficBox.IsVisible = account.Traffic != null;
        _traffic.Text = account.Traffic ?? string.Empty;
        _fraction = account.ShowBar ? Math.Clamp(account.Fraction, 0, 1) : 0;
        _track.IsVisible = account.ShowBar;
        SizeBar();
        _renewUrl = account.Banner?.ButtonUrl ?? account.CabinetUrl;
        _cabinetUrl = account.CabinetUrl;
        _renew.IsVisible = _renewUrl != null && account.Banner?.ButtonUrl != null;
        if (account.Banner?.ButtonText is { } renewText && _renew.Content is Border { Child: TextBlock t })
        {
            t.Text = renewText + " ↗";
        }
        _cabinet.IsVisible = _cabinetUrl != null;
    }
}
