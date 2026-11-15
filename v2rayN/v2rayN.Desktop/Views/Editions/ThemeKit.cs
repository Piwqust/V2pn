using Avalonia.Layout;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// What a provider theme (V2pnTheme) is drawn with: its font from V2pn's bundled set, its brand area (a colour, a
/// gradient or a picture under a scrim), and a thumbnail of each template for the Appearance page.
/// </summary>
internal static class ThemeKit
{
    private const string FontPrefix = "theme:";
    private const string Fallback = ", avares://V2pn/Assets/Fonts#Inter";

    /// <summary>The id of a theme font in the appearance settings ("theme:Golos Text").</summary>
    public static string FontId(string family) => FontPrefix + family;

    /// <summary>The bundled family of a "theme:…" id; null for any other id.</summary>
    public static FontFamily? FontOfId(string? id) =>
        id != null && id.StartsWith(FontPrefix, StringComparison.Ordinal) ? Font(id[FontPrefix.Length..]) : null;

    private static readonly Dictionary<string, FontFamily> Families = [];

    /// <summary>A bundled font by name (V2pnThemeFonts); Geist for a name V2pn does not have. Inter fills any glyph it lacks.</summary>
    public static FontFamily Font(string? name)
    {
        var font = V2pnThemeFonts.Resolve(name);
        lock (Families)
        {
            if (!Families.TryGetValue(font.Family, out var family))
            {
                var folder = font.Folder == null ? "avares://V2pn/Assets/Fonts" : $"avares://V2pn/Assets/Fonts/Themes/{font.Folder}";
                family = new FontFamily($"{folder}#{font.Family}{Fallback}");
                Families[font.Family] = family;
            }
            return family;
        }
    }

    /// <summary>The corners of the appearance settings for a theme's shape.</summary>
    public static string Corners(string corners) => corners switch
    {
        V2pnThemeCorners.Sharp => "sharp",
        V2pnThemeCorners.Round => "round",
        _ => "normal",
    };

    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>The fill of the brand area: its colour or its gradient (corner to corner).</summary>
    public static IBrush AreaFill(V2pnThemeArea area)
    {
        if (area.Colors.Count < 2)
        {
            return Brush(area.Colors.FirstOrDefault() ?? "#202020");
        }
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        for (var i = 0; i < area.Colors.Count; i++)
        {
            brush.GradientStops.Add(new GradientStop(Color.Parse(area.Colors[i]), i / (double)(area.Colors.Count - 1)));
        }
        return brush;
    }

    /// <summary>
    /// The brand area as layers under <paramref name="content"/>: the colour or gradient, or the picture filling it with
    /// the scrim V2pn worked out over it, so the text on top reads whatever the picture holds.
    /// </summary>
    public static Control Area(V2pnThemeArea area, Bitmap? picture, Control? content, CornerRadius radius = default)
    {
        var panel = new Panel { Background = AreaFill(area), ClipToBounds = true };
        if (area.Scrim is { } scrim && picture != null)
        {
            panel.Children.Add(new Image { Source = picture, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new Border { Background = Brush(scrim) });
        }
        if (content != null)
        {
            panel.Children.Add(content);
        }
        return radius == default ? panel : new Border { CornerRadius = radius, ClipToBounds = true, Child = panel };
    }

    /// <summary>The theme of a partner's pack, or V2pn's default one in its accent.</summary>
    public static V2pnTheme Of(V2pnPartner partner) => partner.Pack.Theme ?? V2pnTheme.Default(partner.Pack.Accent);

    /// <summary>
    /// A small picture of a theme's template in its own colours (104×60), for the look cards of the Appearance page.
    /// </summary>
    public static Control Preview(V2pnTheme theme, bool dark)
    {
        var c = V2pnThemePalette.Derive(theme, dark);
        Border Bar(string hex, double width, double height = 4, double radius = 2, HorizontalAlignment align = HorizontalAlignment.Left) =>
            new() { Width = width, Height = height, CornerRadius = new CornerRadius(radius), Background = Brush(hex), HorizontalAlignment = align };
        Border Card(string hex, Thickness margin) => new() { Background = Brush(hex), CornerRadius = new CornerRadius(4), Margin = margin };
        var area = AreaFill(c.Area);
        Control Connect(HorizontalAlignment align = HorizontalAlignment.Left) => theme.Connect switch
        {
            V2pnThemeConnect.Round => new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brush(c.Area.Action), HorizontalAlignment = align },
            V2pnThemeConnect.Toggle => new Border
            {
                Width = 16,
                Height = 9,
                CornerRadius = new CornerRadius(5),
                Background = Brush(c.Accent),
                HorizontalAlignment = align,
                Child = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = Brush(c.OnAccent), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(1) },
            },
            V2pnThemeConnect.Bar => Bar(c.Area.Action, 40, 7, 2, HorizontalAlignment.Stretch),
            _ => Bar(c.Area.Action, 26, 8, 4, align),
        };

        switch (theme.Template)
        {
            case V2pnThemeTemplates.Half:
            {
                var grid = new Grid { RowDefinitions = new("26,*"), Background = Brush(c.Ground) };
                grid.Children.Add(new Panel
                {
                    Background = area,
                    Children = { new StackPanel { Margin = new Thickness(7, 5), Spacing = 3, Children = { Bar(c.Area.Ink, 30, 4), Connect() } } },
                });
                var low = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 3, Margin = new Thickness(5, 4, 5, 5), [Grid.RowProperty] = 1 };
                low.Children.Add(Card(c.Surface, default));
                low.Children.Add(Card(c.Surface, default).Also(b => Grid.SetColumn(b, 1)));
                grid.Children.Add(low);
                return grid;
            }
            case V2pnThemeTemplates.Split:
            {
                var grid = new Grid { ColumnDefinitions = new("38,*"), Background = Brush(c.Ground) };
                grid.Children.Add(new Panel
                {
                    Background = area,
                    Children = { new StackPanel { Margin = new Thickness(5, 7), Spacing = 4, Children = { Bar(c.Area.Ink, 18, 3), Bar(c.Area.Ink, 24, 4), Connect() } } },
                });
                var right = new StackPanel { Margin = new Thickness(5, 7), Spacing = 4, [Grid.ColumnProperty] = 1 };
                foreach (var w in new[] { 44, 38, 44, 30 })
                {
                    right.Children.Add(Bar(c.Line, w, 5));
                }
                grid.Children.Add(right);
                return grid;
            }
            case V2pnThemeTemplates.List:
            {
                var stack = new StackPanel { Background = Brush(c.Ground), Spacing = 3 };
                stack.Children.Add(new Panel { Height = 13, Background = area, Children = { Bar(c.Area.Ink, 22, 3).Also(b => b.Margin = new Thickness(6, 5)) } });
                var rows = new StackPanel { Margin = new Thickness(6, 0), Spacing = 2 };
                rows.Children.Add(new Grid { Background = Brush(c.Surface), Height = 12, Children = { Bar(c.Ink, 26, 3).Also(b => { b.Margin = new Thickness(4, 0); b.VerticalAlignment = VerticalAlignment.Center; }), new Border { Width = 14, Height = 8, CornerRadius = new CornerRadius(4), Background = Brush(c.Accent), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4, 0) } } });
                foreach (var w in new[] { 34, 28, 40 })
                {
                    rows.Children.Add(new Border { Background = Brush(c.Surface), Height = 9, Child = Bar(c.Muted, w, 3).Also(b => { b.Margin = new Thickness(4, 0); b.VerticalAlignment = VerticalAlignment.Center; }) });
                }
                stack.Children.Add(rows);
                return stack;
            }
            case V2pnThemeTemplates.Map:
            {
                var panel = new Panel { Background = Brush(c.Ground) };
                var dots = new Canvas();
                foreach (var (x, y, w, h) in new[] { (8, 10, 30, 14), (44, 6, 40, 20), (70, 30, 26, 16), (20, 34, 22, 14) })
                {
                    dots.Children.Add(new Border { Width = w, Height = h, CornerRadius = new CornerRadius(6), Background = Brush(c.Raised), [Canvas.LeftProperty] = (double)x, [Canvas.TopProperty] = (double)y });
                }
                panel.Children.Add(dots);
                panel.Children.Add(new Border
                {
                    Width = 38,
                    Margin = new Thickness(5),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(5),
                    Background = Brush(c.Area.Glass),
                    Child = new StackPanel { Margin = new Thickness(4), Spacing = 3, Children = { Bar(c.Ink, 22, 3), Bar(c.Accent, 28, 6, 3) } },
                });
                return panel;
            }
            case V2pnThemeTemplates.Compact:
                return new Panel
                {
                    Background = area,
                    Children =
                    {
                        new Border
                        {
                            Width = 40,
                            Margin = new Thickness(0, 5),
                            CornerRadius = new CornerRadius(5),
                            Background = Brush(c.Surface),
                            Child = new StackPanel { Margin = new Thickness(5, 6), Spacing = 4, Children = { Bar(c.Ink, 20, 3), Bar(c.Accent, 30, 6, 2), Bar(c.Line, 30, 3), Bar(c.Line, 24, 3) } },
                        },
                    },
                };
            default:
            {
                var grid = new Grid { ColumnDefinitions = new("*,34"), Background = area };
                grid.Children.Add(new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 4,
                    Children = { Connect(HorizontalAlignment.Center), Bar(c.Area.Ink, 30, 4, 2, HorizontalAlignment.Center) },
                });
                grid.Children.Add(Card(c.Surface, new Thickness(0, 5, 5, 5)).Also(b => Grid.SetColumn(b, 1)));
                return grid;
            }
        }
    }
}
