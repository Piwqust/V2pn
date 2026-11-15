using Avalonia.Layout;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// What a provider theme (V2pnTheme) is drawn with: its brand area (a colour, a gradient or a picture under a scrim)
/// and a thumbnail of each look for the Appearance page.
/// </summary>
internal static class ThemeKit
{
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
            // A brush, not an Image: the picture fills whatever the area is and never makes the area bigger.
            panel.Children.Add(new Border
            {
                Background = new ImageBrush(picture)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                },
            });
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

    /// <summary>A small picture of a look (Neon or the Pass) in the theme's colours (104×60), for the Appearance page.</summary>
    public static Control Preview(string look, V2pnTheme theme, bool dark)
    {
        var c = V2pnThemePalette.Derive(theme, dark);
        Border Bar(string hex, double width, double height = 4, double radius = 2, HorizontalAlignment align = HorizontalAlignment.Left) =>
            new() { Width = width, Height = height, CornerRadius = new CornerRadius(radius), Background = Brush(hex), HorizontalAlignment = align };

        if (look == V2pnPartnerVariants.Pass)
        {
            // V2pn's map with the list beside it and the pass card at the bottom.
            var grid = new Grid { ColumnDefinitions = new("30,*"), Background = Brush(c.Ground) };
            var list = new StackPanel { Margin = new Thickness(4, 6), Spacing = 4 };
            foreach (var w in new[] { 20, 16, 20, 14 })
            {
                list.Children.Add(Bar(c.Line, w, 3));
            }
            grid.Children.Add(new Border { Background = Brush(c.Surface), Child = list });
            var stage = new Panel { Background = Brush(c.Raised), [Grid.ColumnProperty] = 1 };
            stage.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 6),
                Spacing = 3,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { Bar(c.Ink, 26, 4, 2, HorizontalAlignment.Center), Bar(c.Accent, 36, 7, 4, HorizontalAlignment.Center) },
            });
            stage.Children.Add(new Border
            {
                Height = 18,
                Margin = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(4),
                Background = AreaFill(c.Area),
                Child = new StackPanel { Margin = new Thickness(5, 4), Spacing = 3, Children = { Bar(c.Area.Ink, 18, 3), Bar(c.Area.Ink, 30, 2) } },
            });
            grid.Children.Add(stage);
            return grid;
        }

        // Neon: one column with the round button (or the switch) and its ring, then the list.
        var column = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6), HorizontalAlignment = HorizontalAlignment.Center, Width = 46 };
        if (theme.Connect == V2pnThemeConnect.Toggle)
        {
            column.Children.Add(new Border
            {
                Height = 12,
                CornerRadius = new CornerRadius(4),
                Background = Brush(c.Surface),
                Child = new Border { Width = 10, Height = 6, CornerRadius = new CornerRadius(3), Background = Brush(c.Accent), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(3, 0) },
            });
        }
        else
        {
            column.Children.Add(new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1.5),
                BorderBrush = Brush(c.AccentText),
                Background = Brush(c.Surface),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
        var rows = new StackPanel { Spacing = 3, Background = Brush(c.Surface) };
        foreach (var w in new[] { 30, 24, 34 })
        {
            rows.Children.Add(Bar(c.Line, w, 3).Also(b => b.Margin = new Thickness(4, 2)));
        }
        column.Children.Add(rows);
        return new Panel
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.2, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.2, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(V2pnColor.Parse(c.Accent).WithAlpha(dark ? 0.3 : 0.18)), 0), new GradientStop(Color.Parse(c.Ground), 1) },
            },
            Children = { column },
        };
    }
}
