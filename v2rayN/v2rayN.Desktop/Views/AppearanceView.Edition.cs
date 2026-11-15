using Avalonia.Automation;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Views;

// The Appearance page in a partner's look. Plain V2pn's palette, accent and type do nothing under a partner look,
// so the page then tunes the look itself: which look, light or dark, its accent, corners and type. Plain V2pn's own
// settings are kept as they were and come back with plain V2pn.
public sealed partial class AppearanceView
{
    private static readonly string[] EditionCornerIds = ["square", "sharp", "normal", "round"];
    private static readonly string[] EditionFontIds = ["geist", "geistmono", "inter", "segoe"];

    private readonly TextBlock _hint = new() { Classes = { "spagehint" }, TextWrapping = TextWrapping.Wrap };
    private readonly ContentControl _looksHost = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private StackPanel _normalBody = null!;
    private int _baseSegments;
    private int _baseSwitches;
    private bool _bodyQueued;

    private void OnEditionChanged()
    {
        // Several changes can come at once (a look and its theme); the page is built again once.
        if (_bodyQueued)
        {
            return;
        }
        _bodyQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _bodyQueued = false;
            ShowBody();
        }, DispatcherPriority.Background);
    }

    /// <summary>Plain V2pn's settings, or the open partner look's; the scroll position stays where it was.</summary>
    private void ShowBody()
    {
        var offset = _scroll.Offset;
        if (_segments.Count > _baseSegments)
        {
            _segments.RemoveRange(_baseSegments, _segments.Count - _baseSegments);
        }
        if (_switches.Count > _baseSwitches)
        {
            _switches.RemoveRange(_baseSwitches, _switches.Count - _baseSwitches);
        }
        if (Editions.IsTuned && Editions.Partner is { } partner)
        {
            _hint.Text = string.Format(L.EditionApHint, L.EditionVariantShort(Editions.Variant), partner.Name);
            _looksHost.Content = null;
            _scroll.Content = BuildEditionBody(partner);
        }
        else
        {
            _hint.Text = L.ApHint;
            _looksHost.Content = Editions.Available.Count > 0
                ? Group(Editions.Partner?.Name ?? L.EditionApPartners, Stacked(L.EditionApLook, LookCards()))
                : null;
            _scroll.Content = _normalBody;
        }
        Refresh();
        Dispatcher.UIThread.Post(() => _scroll.Offset = offset, DispatcherPriority.Background);
    }

    private Control BuildEditionBody(V2pnPartner partner)
    {
        var look = Editions.Variant;
        var body = new StackPanel { Margin = new Thickness(28, 6, 28, 24) };
        body.Children.Add(Group(partner.Name,
            Stacked(L.EditionApLook, LookCards()),
            Row(L.ApMode, null, Choice([L.ApModeLight, L.ApModeDark], Editions.Mode == "light" ? 0 : 1,
                i => Editions.Tune(p => p.Mode = i == 0 ? "light" : "dark"))),
            Row(L.ApAccent, string.Format(L.EditionApAccentHint, partner.Name), EditionAccents(partner)),
            Row(L.ApCorners, null, Choice([L.EditionApCornersSquare, L.ApCornersSharp, L.ApCornersNormal, L.ApCornersRound],
                Array.IndexOf(EditionCornerIds, Editions.Corners), i => Editions.Tune(p => p.Corners = EditionCornerIds[i]))),
            Row(L.ApFont, null, Choice(["Geist", "Geist Mono", "Inter", "Segoe UI"],
                Array.IndexOf(EditionFontIds, Editions.Font), i => Editions.Tune(p => p.Font = EditionFontIds[i])))));

        // The warm look is V2pn's own layout in the partner's colours, so the layout settings work there too.
        if (look == V2pnPartnerVariants.Warm)
        {
            body.Children.Add(Group(L.ApGrpLayout,
                Row(L.ApSidebar, null, Segment([L.ApSideLeft, L.ApSideRight],
                    s => s.SidebarSide == "right" ? 1 : 0, (s, i) => s.SidebarSide = i == 1 ? "right" : "left")),
                Row(L.ApWidth, null, Segment([L.ApWidthNarrow, L.ApWidthNormal, L.ApWidthWide],
                    s => Index(WidthIds, s.SidebarWidth, 1), (s, i) => s.SidebarWidth = WidthIds[i])),
                Row(L.ApDensity, null, Segment([L.ApDensityComfortable, L.ApDensityCompact],
                    s => s.Density == "compact" ? 1 : 0, (s, i) => s.Density = i == 1 ? "compact" : "comfortable")),
                Row(L.ApMap, L.ApMapHint, Switch(s => s.ShowMap, (s, v) => s.ShowMap = v)),
                Row(L.ApStatusCard, null, Switch(s => s.ShowStatusCard, (s, v) => s.ShowStatusCard = v)),
                Row(L.ApSpeedChart, null, Switch(s => s.ShowSpeedChart, (s, v) => s.ShowSpeedChart = v))));
        }

        body.Children.Add(Group(L.ApGrpMotion,
            Row(L.ApTextSize, L.EditionApTextSizeHint, FontSizeBox()),
            Row(L.ApMotion, L.ApMotionHint, Segment([L.ApMotionSystem, L.ApMotionOn, L.ApMotionOff],
                s => Index(MotionIds, s.Motion, 0), (s, i) => s.Motion = MotionIds[i],
                applied: () => Motion.Refresh(Application.Current!))),
            Row(L.ApLanguage, null, LanguageBox())));

        var reset = new Button { Content = string.Format(L.EditionApReset, partner.Name), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
        reset.Bind(ThemeProperty, reset.GetResourceObservable("V2Button"));
        reset.Click += (_, _) => Editions.ResetTuning();
        body.Children.Add(reset);
        return body;
    }

    /// <summary>A segmented choice for the look's tuning: it shows the value in effect and changes only on a pick.</summary>
    private static Control Choice(string[] labels, int selected, Action<int> pick)
    {
        var box = SegmentBox(labels);
        box.SelectedIndex = selected;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0 && box.SelectedIndex != selected)
            {
                pick(box.SelectedIndex);
            }
        };
        return box;
    }

    /// <summary>The partner's own colour first, then V2pn's accents and a custom one.</summary>
    private Control EditionAccents(V2pnPartner partner)
    {
        var chosen = Editions.Prefs.Accent;
        var swatches = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        swatches.Children.Add(Swatch(Color.Parse(Editions.PartnerAccent), chosen == null, string.Format(L.EditionApPartnerAccent, partner.Name),
            () => Editions.Tune(p => p.Accent = null)));
        foreach (var (id, color) in Appearance.Accents)
        {
            swatches.Children.Add(Swatch(color, chosen == id, L.ApAccentName(id), () => Editions.Tune(p => p.Accent = id)));
        }
        var custom = new TextBox { Width = 110, Watermark = "#RRGGBB", MaxLength = 7, Text = chosen?.StartsWith('#') == true ? chosen : string.Empty };
        void Apply()
        {
            var text = custom.Text?.Trim() ?? string.Empty;
            if (!text.StartsWith('#'))
            {
                text = "#" + text;
            }
            if (text.Length == 7 && Color.TryParse(text, out _) && text != chosen)
            {
                Editions.Tune(p => p.Accent = text.ToUpperInvariant());
            }
        }
        custom.LostFocus += (_, _) => Apply();
        custom.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Apply();
            }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { swatches, custom } };
        // The same warning plain V2pn gives: an accent near green or amber makes Connect read as a state.
        return Appearance.NearHue(Color.Parse(Editions.AccentHex), Appearance.StatusHues)
            ? new StackPanel { Spacing = 4, Children = { row, new TextBlock { Classes = { "srowhint" }, MaxWidth = 420, Text = L.ApAccentNearStatus, TextWrapping = TextWrapping.Wrap } } }
            : row;
    }

    private static Button Swatch(Color color, bool selected, string name, Action click)
    {
        var button = new Button
        {
            Classes = { "ghost", "icon" },
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 4, 0),
            CornerRadius = new CornerRadius(16),
            Content = new Panel
            {
                Children =
                {
                    new Ellipse { Width = 30, Height = 30, StrokeThickness = selected ? 2 : 0, Stroke = new SolidColorBrush(color) },
                    new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(color) },
                },
            },
        };
        button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, selected ? name + L.OptionSelected : name);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>
    /// Plain V2pn and the looks there are, each as a small picture of itself: in plain V2pn one card per partner, in
    /// a partner's edition one card per look the partner allows.
    /// </summary>
    private Control LookCards()
    {
        var cards = new WrapPanel { Orientation = Orientation.Horizontal };
        var partner = Editions.Partner;
        cards.Children.Add(LookCard("v2pn", null, L.AppName, partner == null, () => Editions.Select(null)));
        if (partner == null)
        {
            foreach (var p in Editions.Available)
            {
                var id = p.Id;
                cards.Children.Add(LookCard(Editions.VariantOf(p), p, p.Name, false, () => Editions.Select(id)));
            }
            return cards;
        }
        foreach (var variant in partner.Pack.Variants)
        {
            var v = variant;
            cards.Children.Add(LookCard(variant, partner, L.EditionVariantShort(variant), variant == Editions.Variant, () => Editions.SetVariant(v)));
        }
        return cards;
    }

    private Button LookCard(string look, V2pnPartner? partner, string label, bool selected, Action pick)
    {
        var accent = partner == null ? Color.Parse("#FF3D1F") : Color.Parse(partner.Pack.Accent ?? "#FF3D1F");
        var frame = new Border
        {
            Width = 104,
            Height = 60,
            ClipToBounds = true,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            BorderBrush = selected ? new SolidColorBrush(Appearance.AccentColor) : new SolidColorBrush(Color.Parse("#40808080")),
            Child = LookPreview(look, partner, accent),
        };
        var card = new Button
        {
            Classes = { "ghost" },
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 6, 6),
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    frame,
                    new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Classes = { selected ? "srowtitle" : "muted" } },
                },
            },
        };
        card.Bind(ThemeProperty, card.GetResourceObservable("V2Button"));
        AutomationProperties.SetName(card, selected ? label + L.OptionSelected : label);
        card.Click += (_, _) =>
        {
            if (!selected)
            {
                pick();
            }
        };
        return card;
    }

    /// <summary>A thumbnail of a look in its own colours, so the cards say what each one is before a click.</summary>
    internal static Control LookPreview(string look, V2pnPartner? partner, Color accent)
    {
        static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
        static Border Bar(IBrush fill, double width, double height = 5, double radius = 3) =>
            new() { Width = width, Height = height, CornerRadius = new CornerRadius(radius), Background = fill, HorizontalAlignment = HorizontalAlignment.Left };
        var accentBrush = new SolidColorBrush(accent);

        switch (look)
        {
            case V2pnPartnerVariants.Cabinet:
            {
                var grid = new Grid { RowDefinitions = new("*,*"), Background = B("#0E0E0D") };
                grid.Children.Add(new Border
                {
                    Margin = new Thickness(4, 4, 4, 1),
                    CornerRadius = new CornerRadius(6),
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.Parse("#ECEAE6"), 0), new GradientStop(Color.Parse("#EDAA80"), 1) },
                    },
                    Child = new StackPanel { Margin = new Thickness(6, 5), Spacing = 3, Children = { Bar(B("#121211"), 30, 4), Bar(accentBrush, 40, 3, 1) } },
                });
                var low = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 3, Margin = new Thickness(4, 1, 4, 4) };
                low.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = B("#1C1B19") });
                low.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = B("#1C1B19"), [Grid.ColumnProperty] = 1 });
                Grid.SetRow(low, 1);
                grid.Children.Add(low);
                return grid;
            }
            case V2pnPartnerVariants.Studio:
                return new Panel
                {
                    Background = B("#E2E2E0"),
                    Children =
                    {
                        new StackPanel
                        {
                            Margin = new Thickness(8, 7),
                            Spacing = 6,
                            Children =
                            {
                                new Border
                                {
                                    Background = B("#1A1918"),
                                    CornerRadius = new CornerRadius(8),
                                    Padding = new Thickness(3),
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Children = { Bar(B("#3A3936"), 12, 6), Bar(accentBrush, 18, 6) } },
                                },
                                Bar(B("#121211"), 52, 8, 1),
                                Bar(B("#8E8C86"), 34, 3),
                            },
                        },
                    },
                };
            case V2pnPartnerVariants.Ascii:
                return new Panel
                {
                    Background = B("#121211"),
                    Children =
                    {
                        new TextBlock
                        {
                            Margin = new Thickness(7, 5),
                            FontFamily = Scene.Mono,
                            FontSize = 8.5,
                            LineHeight = 10,
                            Foreground = B("#6E6C66"),
                            Inlines =
                            {
                                new Avalonia.Controls.Documents.Run("> v2pn  "),
                                new Avalonia.Controls.Documents.Run("*") { Foreground = accentBrush },
                                new Avalonia.Controls.Documents.LineBreak(),
                                new Avalonia.Controls.Documents.Run(".:+##+:. @"),
                                new Avalonia.Controls.Documents.LineBreak(),
                                new Avalonia.Controls.Documents.Run("[ подкл ]"),
                            },
                        },
                    },
                };
        }

        // V2pn's own layout: plain, signature (with the partner's logo) or warm (in the partner's colours).
        var warm = look == V2pnPartnerVariants.Warm;
        var ground = warm ? "#171716" : "#1C1C23";
        var side = warm ? "#201F1D" : "#24242C";
        var line = warm ? "#3A3833" : "#3A3A46";
        var preview = new Grid { ColumnDefinitions = new("30,*"), Background = B(ground) };
        preview.Children.Add(new Border { Background = B(side) });
        var content = new StackPanel { Margin = new Thickness(7, 8), Spacing = 5 };
        content.Children.Add(Bar(B(line), 40));
        content.Children.Add(Bar(B(line), 26));
        content.Children.Add(Bar(warm ? accentBrush : B("#FF3D1F"), 30, 9, 5));
        Grid.SetColumn(content, 1);
        preview.Children.Add(content);
        if (look == V2pnPartnerVariants.Signature && partner != null && Scene.Picture(partner, partner.Pack.Logo) is { } logo)
        {
            preview.Children.Add(new Image
            {
                Source = logo,
                Height = 9,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            });
        }
        return preview;
    }
}
