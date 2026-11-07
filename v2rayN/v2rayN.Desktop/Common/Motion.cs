using System.Runtime.InteropServices;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Transformation;

namespace v2rayN.Desktop.Common;

/// <summary>
/// V2pn motion. Animation here is feedback: things that appear (flyouts, toasts, pages, a new
/// server in the header) fade and settle in 180-260 ms with an ease-out curve; things on screen that
/// move (segment thumbs, the map) ease in and out. Nothing animates on keyboard-only paths, and
/// everything turns off when Windows' "Show animations" is off.
/// </summary>
/// <summary>
/// Jumps between a fixed number of positions instead of moving every frame. A busy spinner turned at
/// the display's rate (143 Hz here) made the compositor redraw that many frames a second; 24 steps per
/// turn look the same at 12 px and change the picture about 27 times a second.
/// </summary>
public sealed class StepEasing : Easing
{
    public int Steps { get; set; } = 24;

    public override double Ease(double progress) => Math.Floor(progress * Steps) / Steps;
}

internal static class Motion
{
    /// <summary>Strong ease-out (cubic-bezier(0.32, 0.72, 0, 1)): instant response, soft landing.</summary>
    public static readonly Easing Out = new SplineEasing(0.32, 0.72, 0, 1);

    public static readonly Easing InOut = new SplineEasing(0.65, 0, 0.35, 1);

    public static bool Enabled { get; } = DetectEnabled();

    /// <summary>Adds the style-driven motion (transitions, flyout and tooltip entrances) unless reduced.</summary>
    public static void Install(Application app)
    {
        if (Enabled)
        {
            app.Styles.Add(new StyleInclude(new Uri("avares://V2pn/"))
            {
                Source = new Uri("avares://V2pn/Assets/V2pnMotion.axaml"),
            });
        }
        if (app.Resources.TryGetResource(Enabled ? "V2ToastMotion" : "V2ToastStill", null, out var theme))
        {
            app.Resources[typeof(NotificationCard)] = theme;
        }
        SegmentThumb.Install();
    }

    /// <summary>Fades a control in from a small offset. Returns at once when motion is off.</summary>
    public static Task Enter(Visual target, double dx = 0, double dy = 6, int ms = 220, double scale = 1)
    {
        if (!Enabled)
        {
            return Task.CompletedTask;
        }
        // The resting value lives in the local value (an Exit may have left it at 0); the animation
        // only overrides it while running, so nothing flashes when it ends.
        target.Opacity = 1;
        var from = new KeyFrame { Cue = new Cue(0) };
        from.Setters.Add(new Setter(Visual.OpacityProperty, 0d));
        var to = new KeyFrame { Cue = new Cue(1) };
        to.Setters.Add(new Setter(Visual.OpacityProperty, 1d));
        if (dx != 0)
        {
            from.Setters.Add(new Setter(TranslateTransform.XProperty, dx));
            to.Setters.Add(new Setter(TranslateTransform.XProperty, 0d));
        }
        if (dy != 0)
        {
            from.Setters.Add(new Setter(TranslateTransform.YProperty, dy));
            to.Setters.Add(new Setter(TranslateTransform.YProperty, 0d));
        }
        if (scale != 1)
        {
            from.Setters.Add(new Setter(ScaleTransform.ScaleXProperty, scale));
            from.Setters.Add(new Setter(ScaleTransform.ScaleYProperty, scale));
            to.Setters.Add(new Setter(ScaleTransform.ScaleXProperty, 1d));
            to.Setters.Add(new Setter(ScaleTransform.ScaleYProperty, 1d));
        }
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            Easing = Out,
            FillMode = FillMode.None,
            Children = { from, to },
        };
        return animation.RunAsync(target);
    }

    /// <summary>Quick fade out (exits are quieter and ~25% faster than entrances).</summary>
    public static Task Exit(Visual target, double dx = 0, double dy = 0, int ms = 160)
    {
        if (!Enabled)
        {
            return Task.CompletedTask;
        }
        target.Opacity = 0;
        var from = new KeyFrame { Cue = new Cue(0) };
        from.Setters.Add(new Setter(Visual.OpacityProperty, 1d));
        var to = new KeyFrame { Cue = new Cue(1) };
        to.Setters.Add(new Setter(Visual.OpacityProperty, 0d));
        if (dx != 0)
        {
            from.Setters.Add(new Setter(TranslateTransform.XProperty, 0d));
            to.Setters.Add(new Setter(TranslateTransform.XProperty, dx));
        }
        if (dy != 0)
        {
            from.Setters.Add(new Setter(TranslateTransform.YProperty, 0d));
            to.Setters.Add(new Setter(TranslateTransform.YProperty, dy));
        }
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            Easing = new CubicEaseIn(),
            FillMode = FillMode.None,
            Children = { from, to },
        };
        return animation.RunAsync(target);
    }

    private static bool DetectEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }
        try
        {
            // SPI_GETCLIENTAREAANIMATION: Settings > Accessibility > Visual effects > Animation effects.
            var on = true;
            return !SystemParametersInfo(0x1042, 0, ref on, 0) || on;
        }
        catch
        {
            return true;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winIni);
}

/// <summary>
/// The selected chip of a V2Segmented control is one element (PART_Thumb) that slides to the
/// selected item instead of jumping, like a native segmented control.
/// </summary>
internal static class SegmentThumb
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }
        _installed = true;
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<ListBox>((list, e) =>
        {
            if (e.NameScope.Find<Border>("PART_Thumb") is { } thumb)
            {
                Track(list, thumb);
            }
        });
    }

    private static void Track(ListBox list, Border thumb)
    {
        var placed = false;
        void Update()
        {
            if (list.SelectedIndex < 0 || list.ContainerFromIndex(list.SelectedIndex) is not ListBoxItem item
                || item.Bounds.Width <= 0 || thumb.Parent is not Visual host)
            {
                thumb.IsVisible = false;
                return;
            }
            var origin = item.TranslatePoint(new Point(0, 0), host);
            if (origin == null)
            {
                return;
            }
            thumb.IsVisible = true;
            thumb.CornerRadius = item.CornerRadius;
            thumb.Width = item.Bounds.Width;
            thumb.Height = item.Bounds.Height;
            var move = new TransformOperations.Builder(1);
            move.AppendTranslate(origin.Value.X, origin.Value.Y);
            thumb.RenderTransform = move.Build();
            if (!placed)
            {
                // The first placement doesn't slide in from the corner; later changes do.
                placed = true;
                if (Motion.Enabled)
                {
                    Dispatcher.UIThread.Post(() => thumb.Transitions =
                    [
                        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = Motion.InOut },
                        new DoubleTransition { Property = Layoutable.WidthProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = Motion.InOut },
                    ], DispatcherPriority.Background);
                }
            }
        }

        list.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(Update, DispatcherPriority.Render);
        list.SizeChanged += (_, _) => Update();
        list.LayoutUpdated += (_, _) =>
        {
            if (!placed)
            {
                Update();
            }
        };
        Dispatcher.UIThread.Post(Update, DispatcherPriority.Loaded);
    }
}
