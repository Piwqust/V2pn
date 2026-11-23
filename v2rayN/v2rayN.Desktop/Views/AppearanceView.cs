using Avalonia.Automation;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

/// <summary>
/// The Appearance page: light or dark, palette, accent, corners, fonts, text size and the layout
/// of the main window. Every change is applied and saved at once, so the window behind the dialog
/// is the preview. A partner's brand (Editions) lays only its accent over these and never changes them.
/// </summary>
public sealed class AppearanceView : UserControl
{
    private readonly ThemeSettingViewModel _theme;
    private readonly WrapPanel _palettes = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _accents = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _customAccent = new() { Width = 120, Watermark = "#RRGGBB", MaxLength = 7 };
    private readonly TextBlock _accentNote = new() { Classes = { "srowhint" }, MaxWidth = 420, IsVisible = false, Text = L.ApAccentNearStatus };
    private readonly List<(ListBox Box, Func<AppearanceSettings, int> Read)> _segments = [];
    private readonly List<(ToggleSwitch Switch, Func<AppearanceSettings, bool> Read)> _switches = [];
    private bool _refreshing;
    // While a partner's server is selected, why Connect is not in the accent picked here.
    private readonly TextBlock _partnerNote = new() { Classes = { "spagehint" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };

    private static readonly string[] CornerIds = ["sharp", "normal", "round"];
    private static readonly string[] WidthIds = ["narrow", "normal", "wide"];
    private static readonly string[] MotionIds = ["system", "on", "off"];
    private static readonly string[] TabStyleIds = ["both", "icons", "labels"];
    private static readonly string[] StartTabIds = ["home", "countries", "subscriptions"];
    private static readonly string[] RailPositionIds = ["bottom", "top", "hidden"];
    private static readonly string[] RailLayoutIds = ["row", "column", "icons"];
    private static readonly int[] FavoriteCounts = [3, 5, 10, 0];
    private static readonly int[] ListCounts = [3, 5, 10];

    // The home tab's sections: a switch and up/down arrows for each, rebuilt on every change.
    private readonly StackPanel _homeSections = new() { Spacing = 2 };

    // The hues of V2Safe and V2PingMid (dark theme values; the light ones are darker shades of the same
    // colours). An accent close to them makes the Connect button, shown only while unprotected, read as a state.
    private static readonly Color[] StatusHues = [Color.Parse("#3DD68C"), Color.Parse("#F2B84B")];

    /// <summary>The palette button in the window header.</summary>
    public static Control HeaderButton(Action open)
    {
        var icon = new PathIcon { Classes = { "i16" } };
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable("SemiIconColorPalette"));
        var button = new Button { Classes = { "ghost", "icon" }, Content = icon };
        button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
        ToolTip.SetTip(button, L.ApTitle);
        AutomationProperties.SetName(button, L.ApTitle);
        button.Click += (_, _) => open();
        return button;
    }

    public AppearanceView(ThemeSettingViewModel theme)
    {
        _theme = theme;
        Width = 700;

        var close = new Button { Classes = { "ghost", "icon" }, VerticalAlignment = VerticalAlignment.Top };
        close.Bind(ThemeProperty, close.GetResourceObservable("V2Button"));
        var closeIcon = new PathIcon { Classes = { "i14" } };
        closeIcon.Bind(PathIcon.DataProperty, closeIcon.GetResourceObservable("SemiIconClose"));
        close.Content = closeIcon;
        ToolTip.SetTip(close, L.Close);
        AutomationProperties.SetName(close, L.CloseAppearance);
        close.Click += (_, _) => DialogHost.Close(null);

        var header = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(28, 22, 18, 4) };
        header.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Classes = { "spagetitle" }, Text = L.ApTitle },
                new TextBlock { Classes = { "spagehint" }, TextWrapping = TextWrapping.Wrap, Text = L.ApHint },
                _partnerNote,
            },
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var reset = new Button { Content = L.ApReset, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
        reset.Bind(ThemeProperty, reset.GetResourceObservable("V2Button"));
        reset.Click += (_, _) =>
        {
            Appearance.Reset();
            Motion.Refresh(Application.Current!);
            Refresh();
        };

        var body = new StackPanel
        {
            Margin = new Thickness(28, 6, 28, 24),
            Children =
            {
                Group(L.ApGrpTheme,
                    Row(L.ApMode, null, ModeSegment()),
                    Stacked(L.ApPalette, _palettes),
                    Row(L.ApAccent, L.ApAccentHint, new StackPanel
                    {
                        Spacing = 4,
                        Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _accents, _customAccent } }, _accentNote },
                    })),
                Group(L.ApGrpShape,
                    Row(L.ApCorners, null, Segment([L.ApCornersSharp, L.ApCornersNormal, L.ApCornersRound],
                        s => Index(CornerIds, s.Corners, 1), (s, i) => s.Corners = CornerIds[i])),
                    Row(L.ApFont, null, Segment([L.ApFontInter, L.ApFontSegoe],
                        s => s.Font == "segoe" ? 1 : 0, (s, i) => s.Font = i == 1 ? "segoe" : "inter")),
                    Row(L.ApHeadings, null, Segment([L.ApHeadingsSans, L.ApHeadingsSerif],
                        s => s.Headings == "serif" ? 1 : 0, (s, i) => s.Headings = i == 1 ? "serif" : "inter")),
                    Row(L.ApTextSize, null, FontSizeBox())),
                Group(L.ApGrpSidebar,
                    Row(L.ApSidebar, null, Segment([L.ApSideLeft, L.ApSideRight],
                        s => s.SidebarSide == "right" ? 1 : 0, (s, i) => s.SidebarSide = i == 1 ? "right" : "left")),
                    Row(L.ApWidth, null, Segment([L.ApWidthNarrow, L.ApWidthNormal, L.ApWidthWide],
                        s => Index(WidthIds, s.SidebarWidth, 1), (s, i) => s.SidebarWidth = WidthIds[i])),
                    Row(L.ApTabs, null, Segment([L.ApTabsBoth, L.ApTabsIcons, L.ApTabsLabels],
                        s => Index(TabStyleIds, s.TabsStyle, 0), (s, i) => s.TabsStyle = TabStyleIds[i])),
                    Row(L.ApTabsWhere, null, Segment([L.ApTop, L.ApBottom],
                        s => s.TabsPosition == "bottom" ? 1 : 0, (s, i) => s.TabsPosition = i == 1 ? "bottom" : "top")),
                    Row(L.ApStartTab, null, Segment([L.TabHome, L.TabCountries, L.TabSubscriptions],
                        s => Index(StartTabIds, s.StartTab, 0), (s, i) => s.StartTab = StartTabIds[i])),
                    Row(L.ApDensity, null, Segment([L.ApDensityComfortable, L.ApDensityCompact],
                        s => s.Density == "compact" ? 1 : 0, (s, i) => s.Density = i == 1 ? "compact" : "comfortable")),
                    Row(L.ApFlags, null, Switch(s => s.ShowFlags, (s, v) => s.ShowFlags = v)),
                    Row(L.ApSubtitles, L.ApSubtitlesHint, Switch(s => s.ShowSubtitles, (s, v) => s.ShowSubtitles = v))),
                Group(L.ApGrpButtons,
                    Row(L.ApButtonsWhere, null, Segment([L.ApBottom, L.ApTop, L.ApHidden],
                        s => Index(RailPositionIds, s.RailPosition, 0), (s, i) => s.RailPosition = RailPositionIds[i])),
                    Row(L.ApButtonsLayout, null, Segment([L.ApButtonsRow, L.ApButtonsColumn, L.ApButtonsIcons],
                        s => Index(RailLayoutIds, s.RailLayout, 0), (s, i) => s.RailLayout = RailLayoutIds[i])),
                    Row(L.RailUpdate, null, Switch(s => s.RailUpdate, (s, v) => s.RailUpdate = v)),
                    Row(L.RailDns, null, Switch(s => s.RailDns ?? ConfigTextHandler.Config.UiItem.V2pnSettingsAdvanced, (s, v) => s.RailDns = v)),
                    Row(L.RailSettings, null, Switch(s => s.RailSettings, (s, v) => s.RailSettings = v))),
                Group(L.ApGrpHome,
                    Stacked(L.ApHomeHint, _homeSections),
                    Row(L.ApHomeFavorites, null, Segment(["3", "5", "10", L.ApAll],
                        s => Index(FavoriteCounts, s.HomeFavorites, 1), (s, i) => s.HomeFavorites = FavoriteCounts[i])),
                    Row(L.ApHomeRecent, null, Segment(["3", "5", "10"],
                        s => Index(ListCounts, s.HomeRecent, 0), (s, i) => s.HomeRecent = ListCounts[i])),
                    Row(L.ApHomeQuick, null, Segment(["3", "5", "10"],
                        s => Index(ListCounts, s.HomeQuick, 1), (s, i) => s.HomeQuick = ListCounts[i]))),
                Group(L.ApGrpLayout,
                    Row(L.ApMap, L.ApMapHint, Switch(s => s.ShowMap, (s, v) => s.ShowMap = v)),
                    Row(L.ApStatusCard, null, Switch(s => s.ShowStatusCard, (s, v) => s.ShowStatusCard = v)),
                    Row(L.ApSpeedChart, null, Switch(s => s.ShowSpeedChart, (s, v) => s.ShowSpeedChart = v))),
                Group(L.ApGrpMotion,
                    Row(L.ApMotion, L.ApMotionHint, Segment([L.ApMotionSystem, L.ApMotionOn, L.ApMotionOff],
                        s => Index(MotionIds, s.Motion, 0), (s, i) => s.Motion = MotionIds[i],
                        applied: () => Motion.Refresh(Application.Current!))),
                    Row(L.ApLanguage, null, LanguageBox())),
                reset,
            },
        };

        var root = new DockPanel { MaxHeight = 720 };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = body });
        Content = root;
        AttachedToVisualTree += (_, _) => Editions.Changed += ShowPartnerNote;
        DetachedFromVisualTree += (_, _) => Editions.Changed -= ShowPartnerNote;
        ShowPartnerNote();

        _customAccent.LostFocus += (_, _) => ApplyCustomAccent();
        _customAccent.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplyCustomAccent();
            }
        };
        ActualThemeVariantChanged += (_, _) => Refresh();
        Refresh();
    }

    private void ShowPartnerNote()
    {
        _partnerNote.IsVisible = Editions.Look != null;
        _partnerNote.Text = Editions.Look is { } partner ? string.Format(L.ApPartnerNote, partner.Name) : string.Empty;
    }

    private static int Index<T>(T[] ids, T value, int fallback)
    {
        var i = Array.IndexOf(ids, value);
        return i < 0 ? fallback : i;
    }

    /// <summary>
    /// Every section in one list: those shown first, in their order, then those turned off. A switch takes a
    /// section off the home tab or puts it back at the end; the arrows move a shown one up or down.
    /// </summary>
    private void BuildHomeSections()
    {
        _homeSections.Children.Clear();
        var shown = Appearance.Current.HomeSections.Where(Appearance.HomeSectionIds.Contains).Distinct().ToList();
        var all = shown.Concat(Appearance.HomeSectionIds.Except(shown)).ToList();
        foreach (var id in all)
        {
            var on = shown.Contains(id);
            var at = shown.IndexOf(id);
            var grid = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), MinHeight = 34 };
            grid.Children.Add(new TextBlock
            {
                Text = L.ApHomeSection(id),
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { on ? "srowtitle" : "muted" },
            });
            var up = ArrowButton("SemiIconChevronUp", L.ApMoveUp, on && at > 0, () => Move(id, -1));
            var down = ArrowButton("SemiIconChevronDown", L.ApMoveDown, on && at < shown.Count - 1, () => Move(id, 1));
            // A section that is off has no place in the order; it comes back at the end.
            up.IsVisible = down.IsVisible = on;
            Grid.SetColumn(up, 1);
            Grid.SetColumn(down, 2);
            grid.Children.Add(up);
            grid.Children.Add(down);
            var toggle = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0, IsChecked = on, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(toggle, L.ApHomeSection(id));
            toggle.IsCheckedChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                Change(s =>
                {
                    s.HomeSections.Remove(id);
                    if (toggle.IsChecked == true)
                    {
                        s.HomeSections.Add(id);
                    }
                });
                BuildHomeSections();
            });
            Grid.SetColumn(toggle, 3);
            grid.Children.Add(toggle);
            _homeSections.Children.Add(grid);
        }
    }

    private void Move(string id, int step)
    {
        Change(s =>
        {
            var list = s.HomeSections.Where(Appearance.HomeSectionIds.Contains).Distinct().ToList();
            var i = list.IndexOf(id);
            var j = i + step;
            if (i < 0 || j < 0 || j >= list.Count)
            {
                return;
            }
            (list[i], list[j]) = (list[j], list[i]);
            s.HomeSections = list;
        });
        BuildHomeSections();
    }

    private static Button ArrowButton(string icon, string name, bool enabled, Action click)
    {
        var glyph = new PathIcon { Classes = { "i14" } };
        glyph.Bind(PathIcon.DataProperty, glyph.GetResourceObservable(icon));
        var button = new Button { Classes = { "ghost", "small", "icon" }, Content = glyph, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };
        button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    private static void Change(Action<AppearanceSettings> edit)
    {
        var next = Appearance.Current.Clone();
        edit(next);
        Appearance.Set(next);
    }

    private void Refresh()
    {
        _refreshing = true;
        var s = Appearance.Current;
        foreach (var (box, read) in _segments)
        {
            box.SelectedIndex = read(s);
        }
        foreach (var (toggle, read) in _switches)
        {
            toggle.IsChecked = read(s);
        }
        BuildPalettes();
        BuildAccents();
        BuildHomeSections();
        _customAccent.Text = s.Accent.StartsWith('#') ? s.Accent : string.Empty;
        _accentNote.IsVisible = Appearance.NearHue(Appearance.AccentColor, StatusHues);
        _refreshing = false;
    }

    private Control ModeSegment()
    {
        string[] themes = [nameof(ETheme.FollowSystem), nameof(ETheme.Light), nameof(ETheme.Dark)];
        var box = SegmentBox([L.ApModeSystem, L.ApModeLight, L.ApModeDark]);
        // No theme saved yet means the system's, which is what the app shows then.
        box.SelectedIndex = Math.Max(0, Array.IndexOf(themes, _theme.CurrentTheme));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0)
            {
                _theme.CurrentTheme = themes[box.SelectedIndex];
            }
        };
        return box;
    }

    private Control Segment(string[] labels, Func<AppearanceSettings, int> read, Action<AppearanceSettings, int> write, Action? applied = null)
    {
        var box = SegmentBox(labels);
        _segments.Add((box, read));
        box.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && box.SelectedIndex >= 0 && box.SelectedIndex != read(Appearance.Current))
            {
                Change(s => write(s, box.SelectedIndex));
                applied?.Invoke();
            }
        };
        return box;
    }

    private static ListBox SegmentBox(string[] labels)
    {
        // Without this, selecting the stored value scrolls the dialog to that row on open.
        var box = new ListBox { ItemsSource = labels, AutoScrollToSelectedItem = false };
        box.Bind(ThemeProperty, box.GetResourceObservable("V2Segmented"));
        return box;
    }

    private Control Switch(Func<AppearanceSettings, bool> read, Action<AppearanceSettings, bool> write)
    {
        var toggle = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0 };
        _switches.Add((toggle, read));
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing && toggle.IsChecked != read(Appearance.Current))
            {
                Change(s => write(s, toggle.IsChecked == true));
            }
        };
        return toggle;
    }

    private Control FontSizeBox()
    {
        var box = new ComboBox
        {
            Width = 120,
            ItemsSource = Enumerable.Range(Global.MinFontSize, Global.MinFontSizeCount).ToList(),
            SelectedItem = _theme.CurrentFontSize,
            // 0 in the config means the theme's own size.
            PlaceholderText = L.ApTextSizeDefault,
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is int size)
            {
                _theme.CurrentFontSize = size;
            }
        };
        return box;
    }

    private Control LanguageBox()
    {
        var box = new ComboBox
        {
            Width = 180,
            ItemsSource = Global.LanguageOptions,
            DisplayMemberBinding = new Binding(nameof(LanguageOption.Display)),
            SelectedValueBinding = new Binding(nameof(LanguageOption.Value)),
            SelectedValue = _theme.CurrentLanguage,
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedValue is string language)
            {
                _theme.CurrentLanguage = language;
            }
        };
        return box;
    }

    private void BuildPalettes()
    {
        _palettes.Children.Clear();
        // Built before the view is attached, so the app's variant is the one to ask.
        var variant = Application.Current?.ActualThemeVariant;
        var dark = variant == ThemeVariant.Dark || variant?.InheritVariant == ThemeVariant.Dark;
        var accent = Appearance.AccentColor;
        foreach (var palette in Appearance.Palettes)
        {
            var selected = palette.Id == Appearance.Current.Palette;
            var surface = dark ? palette.Swatch : palette.SwatchLight;
            var side = Shade(surface, dark ? 0.06 : -0.05);
            var line = Shade(surface, dark ? 0.16 : -0.12);

            // Every card has the same 2 px frame (selection only recolours it), so nothing shifts.
            // Children are not clipped to rounded corners: the side strip rounds its own outer corners
            // to the frame's inner radius, or its square corners poke out over the frame.
            const double radius = 10, frameWidth = 2;
            var preview = new Grid { ColumnDefinitions = new("34,*"), Height = 52 };
            preview.Children.Add(new Border
            {
                Background = new SolidColorBrush(side),
                CornerRadius = new CornerRadius(radius - frameWidth, 0, 0, radius - frameWidth),
            });
            var content = new StackPanel { Margin = new Thickness(8, 9), Spacing = 5 };
            content.Children.Add(new Border { Height = 5, Width = 40, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(line) });
            content.Children.Add(new Border { Height = 5, Width = 26, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(line) });
            content.Children.Add(new Border { Height = 10, Width = 30, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(accent), Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(content, 1);
            preview.Children.Add(content);

            var frame = new Border
            {
                Width = 96,
                ClipToBounds = true,
                CornerRadius = new CornerRadius(radius),
                Background = new SolidColorBrush(surface),
                BorderThickness = new Thickness(frameWidth),
                BorderBrush = selected ? new SolidColorBrush(accent) : new SolidColorBrush(Shade(surface, dark ? 0.12 : -0.1)),
                Child = preview,
            };
            // A ghost V2pn button: a soft rounded plate on hover instead of Semi's grey rectangle.
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
                        new TextBlock { Text = L.ApPaletteName(palette.Id), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Classes = { selected ? "srowtitle" : "muted" } },
                    },
                },
            };
            card.Bind(ThemeProperty, card.GetResourceObservable("V2Button"));
            AutomationProperties.SetName(card, L.ApPaletteName(palette.Id));
            var id = palette.Id;
            card.Click += (_, _) =>
            {
                Change(s => s.Palette = id);
                Refresh();
            };
            _palettes.Children.Add(card);
        }
    }

    private void BuildAccents()
    {
        _accents.Children.Clear();
        foreach (var (id, color) in Appearance.Accents)
        {
            var selected = Appearance.Current.Accent == id;
            var dot = new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(color) };
            var ring = new Ellipse
            {
                Width = 30,
                Height = 30,
                StrokeThickness = selected ? 2 : 0,
                Stroke = new SolidColorBrush(color),
            };
            // The V2 ghost button gives the round swatch a hover fill and the focus ring; the local
            // radius overrides .icon's 10, so the plate stays a circle around the dot.
            var button = new Button
            {
                Classes = { "ghost", "icon" },
                Width = 32,
                Height = 32,
                Margin = new Thickness(0, 0, 4, 0),
                CornerRadius = new CornerRadius(16),
                Content = new Panel { Children = { ring, dot } },
            };
            button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
            var name = L.ApAccentName(id);
            ToolTip.SetTip(button, name);
            AutomationProperties.SetName(button, selected ? name + L.OptionSelected : name);
            button.Click += (_, _) =>
            {
                Change(s => s.Accent = id);
                Refresh();
            };
            _accents.Children.Add(button);
        }
    }

    private void ApplyCustomAccent()
    {
        var text = _customAccent.Text?.Trim() ?? string.Empty;
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }
        if (text.Length == 7 && Color.TryParse(text, out _) && text != Appearance.Current.Accent)
        {
            Change(s => s.Accent = text.ToUpperInvariant());
            Refresh();
        }
    }

    private static Color Shade(Color c, double amount)
    {
        var hsl = c.ToHsl();
        return HslColor.ToRgb(hsl.H, hsl.S, Math.Clamp(hsl.L + amount, 0, 1), 1);
    }

    private static Control Group(string title, params Control[] rows)
    {
        var list = new StackPanel();
        foreach (var row in rows)
        {
            list.Children.Add(row);
        }
        // A bare group, as in the settings pages: sgrouplabel's own margins space the groups and the label.
        return new StackPanel
        {
            Classes = { "sgroup" },
            Children =
            {
                new TextBlock { Classes = { "sgrouplabel" }, Text = title },
                new Border { Classes = { "sgroupcard" }, Child = list },
            },
        };
    }

    private static Control Row(string title, string? hint, Control control)
    {
        var text = new StackPanel { Classes = { "srowtext" }, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Classes = { "srowtitle" }, Text = title });
        if (hint != null)
        {
            text.Children.Add(new TextBlock { Classes = { "srowhint" }, Text = hint });
        }
        var grid = new Grid { ColumnDefinitions = new("*,Auto") };
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(16, 0, 0, 0);
        // Screen readers (and UI tests) find each switch and list by the row it belongs to.
        AutomationProperties.SetName(control, title);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return new Border { Classes = { "srow" }, Child = grid };
    }

    private static Control Stacked(string title, Control content)
    {
        return new Border
        {
            Classes = { "srow" },
            Child = new StackPanel
            {
                Spacing = 12,
                Children = { new TextBlock { Classes = { "srowtitle" }, Text = title }, content },
            },
        };
    }
}
