using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Views;

/// <summary>
/// A colour of a theme: a swatch that opens hue, saturation and lightness sliders with a few starting colours, and the
/// "#RRGGBB" beside it to type or paste. Only whole colours leave it: a half-typed hex changes nothing.
/// </summary>
internal sealed class ColourField : UserControl
{
    private static readonly string[] Starters =
    [
        "#2F7BFF", "#3B6CF6", "#6D28D9", "#A21CAF", "#E11D48", "#EA580C", "#0E7490", "#0F766E",
        "#1E3A8A", "#111827", "#7C3AED", "#DB2777", "#0EA5E9", "#14B8A6", "#78716C", "#F5F5F4",
    ];

    private readonly Border _swatch = new() { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
    private readonly TextBox _hex = new() { Width = 96, MaxLength = 7, FontFamily = new FontFamily("Consolas, avares://V2pn/Assets/Fonts#Geist Mono, monospace") };
    private readonly Slider _h = new() { Minimum = 0, Maximum = 360, Width = 220 };
    private readonly Slider _s = new() { Minimum = 0, Maximum = 100, Width = 220 };
    private readonly Slider _l = new() { Minimum = 0, Maximum = 100, Width = 220 };
    private bool _quiet;

    public event Action<string>? Changed;

    public ColourField(string hex, string name)
    {
        Hex = V2pnColor.TryParse(hex)?.Hex ?? "#3B6CF6";
        _swatch.Bind(Border.BorderBrushProperty, _swatch.GetResourceObservable("V2Border"));
        var button = new Button { Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Content = _swatch };
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetName(_hex, name);
        ToolTip.SetTip(button, name);

        var presets = new WrapPanel { Orientation = Orientation.Horizontal, Width = 236, ItemSpacing = 6, LineSpacing = 6 };
        foreach (var starter in Starters)
        {
            var dot = new Button
            {
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.Parse(starter)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
            };
            AutomationProperties.SetName(dot, starter);
            dot.Click += (_, _) => Set(starter, fromSliders: false);
            presets.Children.Add(dot);
        }
        var sliders = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(4),
            Children =
            {
                Caption(L.BuilderHue), _h,
                Caption(L.BuilderSaturation), _s,
                Caption(L.BuilderLightness), _l,
                presets,
            },
        };
        button.Flyout = new Flyout { Content = sliders, Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var slider in new[] { _h, _s, _l })
        {
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty && !_quiet)
                {
                    Set(V2pnColor.FromHsl(_h.Value, _s.Value / 100, _l.Value / 100).Hex, fromSliders: true);
                }
            };
        }
        _hex.TextChanged += (_, _) =>
        {
            if (!_quiet && V2pnColor.TryParse(_hex.Text?.Trim()) is { } typed && typed.Hex != Hex)
            {
                Set(typed.Hex, fromSliders: false, fromText: true);
            }
        };
        Show(fromSliders: false, fromText: false);
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { button, _hex } };
    }

    public string Hex { get; private set; }

    private static TextBlock Caption(string text) => new() { Text = text, FontSize = 11.5, Opacity = 0.7 };

    private void Set(string hex, bool fromSliders, bool fromText = false)
    {
        Hex = hex;
        Show(fromSliders, fromText);
        Changed?.Invoke(hex);
    }

    private void Show(bool fromSliders, bool fromText)
    {
        _quiet = true;
        try
        {
            _swatch.Background = new SolidColorBrush(Color.Parse(Hex));
            if (!fromText)
            {
                _hex.Text = Hex;
            }
            if (!fromSliders)
            {
                var (h, s, l) = V2pnColor.Parse(Hex).Hsl;
                _h.Value = h;
                _s.Value = s * 100;
                _l.Value = l * 100;
            }
        }
        finally
        {
            _quiet = false;
        }
    }
}

/// <summary>The made-up data the builder's preview draws: a subscription of six servers with a plan, a banner and news.</summary>
internal static class BuilderSample
{
    public static EditionSnapshot Snapshot(V2pnPartner partner)
    {
        const string sub = "builder-sample";
        var servers = new List<SceneServer>
        {
            new("s1", "Германия 1", "DE", 38, false, sub, partner.Name, Group: "Основные"),
            new("s2", "Нидерланды", "NL", 44, false, sub, partner.Name, Group: "Основные"),
            new("s3", "Финляндия", "FI", 52, false, sub, partner.Name, Group: "Основные"),
            new("s4", "Турция", "TR", 71, false, sub, partner.Name, Group: "Для YouTube", Detail: "YouTube"),
            new("s5", "Швеция", "SE", -1, false, sub, partner.Name, Group: "Для YouTube"),
            new("s6", "США", "US", 0, false, sub, partner.Name, Group: "Резерв"),
        };
        const string cabinet = "https://example.com/cabinet";
        var account = new SceneAccount("12,4 ГБ из 50 ГБ", 0.25, true, "Оплачено до 31 января 2027 · 77 дней", false, false,
            "Плановые работы в воскресенье с 3:00 до 4:00.", cabinet, "https://t.me/example_support", "Личный доступ", "Устройств: 2 из 5",
            AnnounceUrl: "https://example.com/news",
            Banner: new SubBanner("Скидка 30% на год до пятницы.", SubPanelHeaders.ToneInfo, "Продлить", cabinet, false), DaysLeft: 77);
        return new EditionSnapshot
        {
            StateKind = "safe",
            StateLabel = L.Protected,
            Protected = true,
            HasServer = true,
            Title = "Германия 1",
            Subtitle = $"{partner.Name} · VLESS Reality",
            CountryCode = "DE",
            Subscription = partner.Name,
            Ping = 38,
            Vpn = true,
            Route = L.RouteAll,
            NodesOk = 4,
            NodesTotal = 5,
            ConnectLabel = L.Disconnect,
            Servers = servers.Select(s => s with { Selected = s.IndexId == "s1" }).ToList(),
            Catalog = servers,
            Subs = [new SceneSub(sub, partner.Name, servers.Count)],
            SelectedId = "s1",
            AnyServers = true,
            PartnerName = partner.Name,
            PartnerSubIds = [sub],
            Account = account,
            Actions = partner.Pack.ActionList,
        };
    }
}

/// <summary>The preview's controls do nothing: it is a picture of the look, not a second window of V2pn.</summary>
internal sealed class PreviewActions : IEditionActions
{
    public void ToggleConnect() { }

    public void SetVpn(bool vpn) { }

    public void OpenRoute(Control anchor) { }

    public void Select(string indexId) { }

    public void PingAll() { }

    public void UpdateSubscriptions() { }

    public void Add(Control anchor) { }

    public void Paste() { }

    public void AddSubscription() { }

    public void Import() { }

    public void OpenSettings() { }

    public void OpenIp() { }

    public void OpenUrl(string url) { }

    public void RunAction(string id, Control anchor) { }
}
