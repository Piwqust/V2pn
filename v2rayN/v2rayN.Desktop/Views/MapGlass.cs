using Avalonia.Animation;
using Avalonia.Animation.Easings;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// The brand's ribbed glass, laid over the map while the connection is protected: what goes
/// through a server is shown behind glass, light gets through and the details don't. It slides
/// in from the left on connect and back out on disconnect (640 ms, cubic-bezier(0.7, 0, 0.2, 1)).
/// The countries are photographed into a small bitmap once per map position; each rib then draws
/// a squeezed slice of it. A frame costs a few dozen image draws instead of thousands of paths,
/// and the small bitmap is the blur.
/// </summary>
internal sealed class MapGlass : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<MapGlass, double>(nameof(Progress));

    // About 40 ribs across a wide window; each shows a slice 1.8x its own width.
    private const double RibWidth = 34;
    private const double Refraction = 1.8;

    // The snapshot is this many times smaller than the view.
    private const int Softness = 6;

    private static readonly Color Flare = Color.Parse("#FF8A2A");

    private readonly Visual _subject;
    private Bitmap? _snapshot;
    private bool _dirty = true;
    private bool _on;
    private bool _capturePosted;
    private bool _slideAfterCapture;

    static MapGlass() => AffectsRender<MapGlass>(ProgressProperty);

    public MapGlass(Visual subject)
    {
        _subject = subject;
        IsHitTestVisible = false;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        if (Motion.Enabled)
        {
            Transitions =
            [
                new DoubleTransition { Property = ProgressProperty, Duration = TimeSpan.FromMilliseconds(640), Easing = new SplineEasing(0.7, 0, 0.2, 1) },
            ];
        }
    }

    /// <summary>How much of the view the pane covers, from the left: 0 is gone, 1 is closed.</summary>
    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Where the light comes from (the pin), as a fraction of the view.</summary>
    public Point LightAt { get; set; } = new(0.5, 0.47);

    /// <summary>What shows between the countries: the map's ocean.</summary>
    public IBrush Ground { get; set; } = Brushes.Black;

    /// <summary>The colour of the light inside the glass: the accent.</summary>
    public Color Tint { get; set; } = Color.Parse("#FF3D1F");

    public bool Dark { get; set; } = true;

    /// <summary>Slides the pane in or out.</summary>
    public void SetOn(bool on)
    {
        if (on == _on)
        {
            return;
        }
        _on = on;
        if (!on)
        {
            Progress = 0;
            return;
        }
        if (_dirty || _snapshot == null)
        {
            // Photograph the map first, so the pane never slides in empty.
            PostCapture(slideIn: true);
        }
        else
        {
            Progress = 1;
        }
    }

    /// <summary>The map moved or changed colour; take a new picture the next time it is needed.</summary>
    public void Invalidate()
    {
        _dirty = true;
        if (_on)
        {
            PostCapture(slideIn: false);
        }
    }

    private void PostCapture(bool slideIn)
    {
        _slideAfterCapture |= slideIn;
        if (_capturePosted)
        {
            return;
        }
        _capturePosted = true;
        // After layout and the map's own update in this pass.
        Dispatcher.UIThread.Post(Capture, DispatcherPriority.Background);
    }

    private void Capture()
    {
        _capturePosted = false;
        var w = (int)Math.Ceiling(Bounds.Width);
        var h = (int)Math.Ceiling(Bounds.Height);
        if (w > 0 && h > 0)
        {
            try
            {
                using var full = new RenderTargetBitmap(new PixelSize(w, h));
                full.Render(_subject);
                // Bitmap.CreateScaledBitmap refuses render targets, so draw the photo into a smaller one.
                var sw = Math.Max(1, w / Softness);
                var sh = Math.Max(1, h / Softness);
                var small = new RenderTargetBitmap(new PixelSize(sw, sh));
                using (var dc = small.CreateDrawingContext())
                using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                {
                    dc.DrawImage(full, new Rect(0, 0, w, h), new Rect(0, 0, sw, sh));
                }
                _snapshot?.Dispose();
                _snapshot = small;
                _dirty = false;
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(MapGlass), ex);
            }
        }
        InvalidateVisual();
        if (_slideAfterCapture)
        {
            _slideAfterCapture = false;
            if (_on)
            {
                Progress = 1;
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        var p = Progress;
        var snap = _snapshot;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (p <= 0.001 || snap == null || w <= 0 || h <= 0)
        {
            return;
        }

        var all = new Rect(0, 0, w, h);
        var ribs = Math.Max(12, (int)Math.Round(w / RibWidth));
        var rib = w / ribs;
        var slice = rib * Refraction;
        var kx = snap.Size.Width / w;

        using (context.PushClip(new Rect(0, 0, w * p, h)))
        {
            context.FillRectangle(Ground, all);
            for (var i = 0; i < ribs; i++)
            {
                var x0 = i * rib;
                // A slight wander keeps neighbouring ribs from repeating the same slice.
                var from = Math.Clamp(x0 + (rib / 2) - (slice / 2) + (Math.Sin(i * 1.37) * rib * 0.1), 0, Math.Max(0, w - slice));
                context.DrawImage(snap, new Rect(from * kx, 0, slice * kx, snap.Size.Height), new Rect(x0, 0, rib + 0.5, h));
            }

            context.FillRectangle(new SolidColorBrush(Tint, Dark ? 0.14 : 0.08), all);
            context.FillRectangle(new RadialGradientBrush
            {
                Center = new RelativePoint(LightAt, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(LightAt, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(Dark ? 115 : 70), Flare.R, Flare.G, Flare.B), 0),
                    new GradientStop(Color.FromArgb((byte)(Dark ? 56 : 30), Tint.R, Tint.G, Tint.B), 0.35),
                    new GradientStop(Color.FromArgb(0, Tint.R, Tint.G, Tint.B), 1),
                },
            }, all);

            // Each rib: a highlight on its left edge, shade toward its right, a 1 px glint between ribs.
            var shade = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(36, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(10, 255, 255, 255), 0.1),
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.5),
                    new GradientStop(Color.FromArgb((byte)(Dark ? 51 : 26), 0, 0, 0), 0.86),
                    new GradientStop(Color.FromArgb((byte)(Dark ? 107 : 56), 0, 0, 0), 1),
                },
            };
            var glint = new SolidColorBrush(Colors.White, 0.1);
            for (var i = 0; i < ribs; i++)
            {
                context.FillRectangle(shade, new Rect(i * rib, 0, rib + 0.5, h));
                if (i > 0)
                {
                    context.FillRectangle(glint, new Rect(Math.Round(i * rib), 0, 1, h));
                }
            }
        }

        if (p < 0.999)
        {
            // The pane's leading edge while it moves.
            context.FillRectangle(new SolidColorBrush(Colors.White, 0.35), new Rect((w * p) - 1, 0, 1.5, h));
        }
    }
}
