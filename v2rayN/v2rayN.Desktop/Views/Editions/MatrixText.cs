using Avalonia.Media;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// 7-row matrix type for partner looks: round dots for numbers (a dashboard's figures) and square pixels
/// for display words (a website's bitmap headline). Drawn, never set from a font, so it needs no files.
/// </summary>
public sealed class MatrixText : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<MatrixText, string?>(nameof(Text));
    public static readonly StyledProperty<double> CellProperty = AvaloniaProperty.Register<MatrixText, double>(nameof(Cell), 6);
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<MatrixText, IBrush?>(nameof(Foreground), Brushes.White);
    public static readonly StyledProperty<bool> SquareProperty = AvaloniaProperty.Register<MatrixText, bool>(nameof(Square));

    static MatrixText()
    {
        AffectsMeasure<MatrixText>(TextProperty, CellProperty, SquareProperty);
        AffectsRender<MatrixText>(TextProperty, CellProperty, ForegroundProperty, SquareProperty);
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double Cell { get => GetValue(CellProperty); set => SetValue(CellProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>Square pixels and the bitmap alphabet (Cyrillic, digits) instead of round number dots.</summary>
    public bool Square { get => GetValue(SquareProperty); set => SetValue(SquareProperty, value); }

    private string[] Glyph(char c)
    {
        var table = Square ? Pixels : Dots;
        if (table.TryGetValue(char.ToUpperInvariant(c), out var g))
        {
            return g;
        }
        if (Square && Dots.TryGetValue(c, out var digit))
        {
            return digit;
        }
        return table[' '];
    }

    /// <summary>
    /// True when every character has a glyph. A name in Latin letters, a dash for "no ping" and the like do not: the
    /// text is then set in the look's own type at the same height instead of coming out blank or as stray dots.
    /// </summary>
    private bool Drawable
    {
        get
        {
            foreach (var c in Text ?? string.Empty)
            {
                var upper = char.ToUpperInvariant(c);
                var known = c == ' ' || (Square ? Pixels.ContainsKey(upper) || Dots.ContainsKey(c) : Dots.ContainsKey(upper));
                if (!known)
                {
                    return false;
                }
            }
            return true;
        }
    }

    // Cap height is about 0.72 of the size: the fallback stands as tall as the seven rows of dots.
    private FormattedText Fallback() => new(
        Text ?? string.Empty,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface(Scene.Current.Text, FontStyle.Normal, Square ? FontWeight.Bold : FontWeight.SemiBold),
        7 * Cell / 0.72,
        Foreground ?? Brushes.White)
    { MaxLineCount = 1 };

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!Drawable)
        {
            var text = Fallback();
            return new Size(text.WidthIncludingTrailingWhitespace, 7 * Cell);
        }
        var columns = 0;
        foreach (var c in Text ?? string.Empty)
        {
            columns += Glyph(c)[0].Length + 1;
        }
        return new Size(Math.Max(0, columns - 1) * Cell, 7 * Cell);
    }

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush)
        {
            return;
        }
        if (!Drawable)
        {
            var text = Fallback();
            // Baseline on the bottom row of where the dots would be.
            context.DrawText(text, new Point(0, 7 * Cell - text.Baseline));
            return;
        }
        var x = 0.0;
        foreach (var c in Text ?? string.Empty)
        {
            var glyph = Glyph(c);
            for (var row = 0; row < glyph.Length; row++)
            {
                for (var col = 0; col < glyph[row].Length; col++)
                {
                    if (glyph[row][col] != '1')
                    {
                        continue;
                    }
                    var cx = x + col * Cell;
                    var cy = row * Cell;
                    if (Square)
                    {
                        context.FillRectangle(brush, new Rect(cx, cy, Cell + 0.4, Cell + 0.4));
                    }
                    else
                    {
                        var r = Cell * 0.42;
                        context.DrawEllipse(brush, null, new Point(cx + Cell / 2, cy + Cell / 2), r, r);
                    }
                }
            }
            x += (glyph[0].Length + 1) * Cell;
        }
    }

    private static readonly Dictionary<char, string[]> Dots = new()
    {
        ['0'] = ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
        ['3'] = ["01110", "10001", "00001", "00110", "00001", "10001", "01110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['5'] = ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ['6'] = ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ['9'] = ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
        ['.'] = ["0", "0", "0", "0", "0", "0", "1"],
        [','] = ["0", "0", "0", "0", "0", "1", "1"],
        [':'] = ["0", "1", "0", "0", "0", "1", "0"],
        ['-'] = ["000", "000", "000", "111", "000", "000", "000"],
        ['/'] = ["00001", "00001", "00010", "00100", "01000", "10000", "10000"],
        ['%'] = ["11001", "11010", "00010", "00100", "01000", "01011", "10011"],
        [' '] = ["00", "00", "00", "00", "00", "00", "00"],
        ['—'] = ["0000", "0000", "0000", "1111", "0000", "0000", "0000"],
    };

    private static readonly Dictionary<char, string[]> Pixels = new()
    {
        ['А'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['Б'] = ["11111", "10000", "10000", "11110", "10001", "10001", "11110"],
        ['В'] = ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
        ['Г'] = ["11111", "10000", "10000", "10000", "10000", "10000", "10000"],
        ['Д'] = ["00110", "01010", "01010", "01010", "01010", "11111", "10001"],
        ['Е'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
        ['Ж'] = ["10101", "10101", "10101", "01110", "10101", "10101", "10101"],
        ['З'] = ["01110", "10001", "00001", "00110", "00001", "10001", "01110"],
        ['И'] = ["10001", "10001", "10011", "10101", "11001", "10001", "10001"],
        ['Й'] = ["01010", "10001", "10011", "10101", "11001", "10001", "10001"],
        ['К'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
        ['Л'] = ["00111", "01001", "01001", "01001", "01001", "01001", "11001"],
        ['М'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
        ['Н'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['О'] = ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['П'] = ["11111", "10001", "10001", "10001", "10001", "10001", "10001"],
        ['Р'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
        ['С'] = ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
        ['Т'] = ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
        ['У'] = ["10001", "10001", "10001", "01111", "00001", "00001", "01110"],
        ['Ф'] = ["00100", "01110", "10101", "10101", "01110", "00100", "00100"],
        ['Х'] = ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
        ['Ц'] = ["10010", "10010", "10010", "10010", "10010", "11111", "00001"],
        ['Ч'] = ["10001", "10001", "10001", "01111", "00001", "00001", "00001"],
        ['Ш'] = ["10101", "10101", "10101", "10101", "10101", "10101", "11111"],
        ['Щ'] = ["101010", "101010", "101010", "101010", "101010", "111111", "000001"],
        ['Ъ'] = ["11000", "01000", "01000", "01110", "01001", "01001", "01110"],
        ['Ы'] = ["100001", "100001", "100001", "111001", "100101", "100101", "111001"],
        ['Ь'] = ["10000", "10000", "10000", "11110", "10001", "10001", "11110"],
        ['Э'] = ["11110", "00001", "00001", "01111", "00001", "00001", "11110"],
        ['Ю'] = ["100110", "101001", "101001", "111001", "101001", "101001", "100110"],
        ['Я'] = ["01111", "10001", "10001", "01111", "00101", "01001", "10001"],
        ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
        ['G'] = ["01111", "10000", "10000", "10111", "10001", "10001", "01111"],
        ['I'] = ["111", "010", "010", "010", "010", "010", "111"],
        ['L'] = ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
        ['N'] = ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
        ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
        ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
        ['U'] = ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['V'] = ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
        ['0'] = ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
        ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['5'] = ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ['6'] = ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ['9'] = ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
        ['→'] = ["0000000", "0000100", "0000010", "1111111", "0000010", "0000100", "0000000"],
        ['∞'] = ["000000000000000", "011100000001110", "100011000110001", "100000111000001", "100011000110001", "011100000001110", "000000000000000"],
        ['+'] = ["00000", "00100", "00100", "11111", "00100", "00100", "00000"],
        ['-'] = ["0000", "0000", "0000", "1111", "0000", "0000", "0000"],
        ['.'] = ["0", "0", "0", "0", "0", "0", "1"],
        [' '] = ["000", "000", "000", "000", "000", "000", "000"],
    };
}
