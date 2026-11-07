namespace v2rayN.Desktop.Views;

/// <summary>
/// A thin rounded meter for a subscription's used traffic. Colours come from styles
/// (UsageBar, UsageBar.warn, UsageBar.bad), so it follows the theme.
/// </summary>
public sealed class UsageBar : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<UsageBar, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<UsageBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<UsageBar, IBrush?>(nameof(Track));

    static UsageBar()
    {
        AffectsRender<UsageBar>(ValueProperty, FillProperty, TrackProperty);
    }

    /// <summary>Used share, 0 to 1.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }
        var radius = size.Height / 2;
        context.DrawRectangle(Track, null, new RoundedRect(new Rect(size), radius));
        var value = double.IsFinite(Value) ? Math.Clamp(Value, 0, 1) : 0;
        if (value <= 0)
        {
            return;
        }
        // Never thinner than it is tall, so a sliver of usage still reads as a dot, not a hairline.
        var width = Math.Max(size.Height, size.Width * value);
        context.DrawRectangle(Fill, null, new RoundedRect(new Rect(0, 0, width, size.Height), radius));
    }
}
