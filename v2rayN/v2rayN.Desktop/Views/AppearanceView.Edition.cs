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
        var body = new StackPanel { Margin = new Thickness(28, 6, 28, 24) };
        body.Children.Add(Group(partner.Name,
            Stacked(L.EditionApLook, LookCards()),
            Row(L.ApMode, null, Choice([L.ApModeLight, L.ApModeDark], Editions.Mode == "light" ? 0 : 1,
                i => Editions.Tune(p => p.Mode = i == 0 ? "light" : "dark"))),
            Row(L.ApAccent, string.Format(L.EditionApAccentHint, partner.Name), EditionAccents(partner)),
            // Corners and type belong to the look; said once, instead of offering choices that break it.
            new Border { Classes = { "srow" }, Child = new TextBlock { Classes = { "srowhint" }, TextWrapping = TextWrapping.Wrap, Text = L.EditionApShapeNote } }));

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

    /// <summary>
    /// The partner's own colour first, then V2pn's presets that cannot pass for a state colour. No free colour: every
    /// choice here was looked at in every look, light and dark.
    /// </summary>
    private Control EditionAccents(V2pnPartner partner)
    {
        var chosen = Editions.AccentChoices().Any(a => a.Id != null && a.Id == Editions.Prefs.Accent) ? Editions.Prefs.Accent : null;
        var swatches = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (id, color) in Editions.AccentChoices())
        {
            var name = id == null ? string.Format(L.EditionApPartnerAccent, partner.Name) : L.ApAccentName(id);
            swatches.Children.Add(Swatch(color, chosen == id, name, () => Editions.Tune(p => p.Accent = id)));
        }
        return swatches;
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
        var accent = partner == null ? Color.Parse("#FF3D1F") : Color.Parse(partner.Pack.Accent ?? partner.Pack.Theme?.Brand.FirstOrDefault() ?? "#FF3D1F");
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

        if (partner != null && V2pnPartnerVariants.All.Contains(look))
        {
            // The partner's look in its own colours, light or dark as the app is now.
            return ThemeKit.Preview(look, ThemeKit.Of(partner), Application.Current?.ActualThemeVariant != ThemeVariant.Light);
        }

        // Plain V2pn.
        var preview = new Grid { ColumnDefinitions = new("30,*"), Background = B("#1C1C23") };
        preview.Children.Add(new Border { Background = B("#24242C") });
        var content = new StackPanel { Margin = new Thickness(7, 8), Spacing = 5 };
        content.Children.Add(Bar(B("#3A3A46"), 40));
        content.Children.Add(Bar(B("#3A3A46"), 26));
        content.Children.Add(Bar(B("#FF3D1F"), 30, 9, 5));
        Grid.SetColumn(content, 1);
        preview.Children.Add(content);
        return preview;
    }
}
