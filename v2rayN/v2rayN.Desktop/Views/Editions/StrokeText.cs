namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// A sticker caption: heavy letters with a thick outline and a hard drop under them, the way meme captions are set.
/// Shrinks to fit when its line is wider than the room it gets.
/// </summary>
public sealed class StrokeText : Control
{
    /// <summary>Impact where the system has it (Windows, macOS); the nearest heavy faces elsewhere.</summary>
    public static readonly FontFamily Heavy = new("Impact, Anton, Arial Black, Arial");

    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<StrokeText, string?>(nameof(Text));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<StrokeText, double>(nameof(Size), 32);
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<StrokeText, IBrush?>(nameof(Fill), Brushes.White);
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<StrokeText, IBrush?>(nameof(Stroke), Brushes.Black);
    public static readonly StyledProperty<double> OutlineProperty = AvaloniaProperty.Register<StrokeText, double>(nameof(Outline), 3);
    public static readonly StyledProperty<double> DropProperty = AvaloniaProperty.Register<StrokeText, double>(nameof(Drop), 3);

    private Geometry? _geometry;
    private double _scale = 1;

    static StrokeText()
    {
        AffectsMeasure<StrokeText>(TextProperty, SizeProperty, OutlineProperty, DropProperty);
        AffectsRender<StrokeText>(FillProperty, StrokeProperty);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Outline
    {
        get => GetValue(OutlineProperty);
        set => SetValue(OutlineProperty, value);
    }

    /// <summary>How far the black copy under the letters sits below them.</summary>
    public double Drop
    {
        get => GetValue(DropProperty);
        set => SetValue(DropProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            // Screen readers get the words in their case, not the capitals on screen.
            Avalonia.Automation.AutomationProperties.SetName(this, Text ?? string.Empty);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _geometry = null;
        _scale = 1;
        if (string.IsNullOrEmpty(Text))
        {
            return default;
        }
        var text = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Heavy, FontStyle.Normal, FontWeight.Bold), Size, Brushes.White);
        var pad = Outline;
        _geometry = text.BuildGeometry(new Point(pad, pad));
        var width = text.WidthIncludingTrailingWhitespace + (pad * 2);
        var height = text.Height + (pad * 2) + Drop;
        if (double.IsFinite(availableSize.Width) && width > availableSize.Width && availableSize.Width > 0)
        {
            _scale = Math.Max(0.45, availableSize.Width / width);
        }
        return new Size(width * _scale, height * _scale);
    }

    public override void Render(DrawingContext context)
    {
        if (_geometry == null)
        {
            return;
        }
        using var scale = context.PushTransform(Matrix.CreateScale(_scale, _scale));
        var pen = new Pen(Stroke, Outline * 2, lineJoin: PenLineJoin.Round);
        if (Drop > 0)
        {
            using (context.PushTransform(Matrix.CreateTranslation(0, Drop)))
            {
                context.DrawGeometry(Stroke, pen, _geometry);
            }
        }
        context.DrawGeometry(null, pen, _geometry);
        context.DrawGeometry(Fill, null, _geometry);
    }
}
