using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
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

    public static bool Enabled { get; private set; } = DetectEnabled();

    private static StyleInclude? _styles;

    /// <summary>Adds the style-driven motion (transitions, flyout and tooltip entrances) unless reduced.</summary>
    public static void Install(Application app)
    {
        Apply(app);
        SegmentThumb.Install();
    }

    /// <summary>
    /// Reads the setting again (Appearance, then Windows' "Animation effects") and applies a change at
    /// once: the motion styles come and go, toasts take the matching theme, segment thumbs stop or
    /// start sliding. Code-driven motion (Enter, Exit, the map) asks <see cref="Enabled"/> each time.
    /// </summary>
    public static void Refresh(Application app)
    {
        var enabled = DetectEnabled();
        if (enabled == Enabled)
        {
            return;
        }
        Enabled = enabled;
        Apply(app);
        SegmentThumb.SetMotion(enabled);
    }

    private static void Apply(Application app)
    {
        _styles ??= new StyleInclude(new Uri("avares://V2pn/"))
        {
            Source = new Uri("avares://V2pn/Assets/V2pnMotion.axaml"),
        };
        if (Enabled && !app.Styles.Contains(_styles))
        {
            app.Styles.Add(_styles);
        }
        else if (!Enabled)
        {
            app.Styles.Remove(_styles);
        }
        if (app.Resources.TryGetResource(Enabled ? "V2ToastMotion" : "V2ToastStill", null, out var theme))
        {
            app.Resources[typeof(NotificationCard)] = theme;
        }
    }

    /// <summary>Fades a control in from a small offset. Returns at once when motion is off.</summary>
    public static Task Enter(Visual target, double dx = 0, double dy = 6, int ms = 220, double scale = 1)
    {
        // The resting value lives in the local value (an Exit may have left it at 0, and motion can
        // be turned off between that Exit and this Enter); the animation only overrides it while
        // running, so nothing flashes when it ends.
        target.Opacity = 1;
        if (!Enabled)
        {
            return Task.CompletedTask;
        }
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
        // The Appearance page can override Windows' setting (Refresh applies a change live).
        switch (Appearance.Current.Motion)
        {
            case "on":
                return true;
            case "off":
                return false;
        }
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
/// selected item instead of jumping, like a native segmented control. Keyboard changes and resizes
/// jump: keyboard users move fast, and a resize should not leave the chip behind its cell.
/// </summary>
internal static class SegmentThumb
{
    private static bool _installed;

    // Per list: set by keyboard input (or SnapNext), cleared by a pointer press.
    private static readonly ConditionalWeakTable<ListBox, StrongBox<bool>> Instant = new();

    // Placed thumbs, so a change of the motion setting reaches the ones already on screen.
    private static readonly List<WeakReference<Border>> Thumbs = [];

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

    /// <summary>The thumb of this list jumps until the next pointer press (for window-level shortcuts).</summary>
    public static void SnapNext(ListBox list) => InstantFlag(list).Value = true;

    /// <summary>Starts or stops the slide of every placed thumb (Motion.Refresh).</summary>
    public static void SetMotion(bool enabled)
    {
        Thumbs.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var weak in Thumbs)
        {
            if (weak.TryGetTarget(out var thumb))
            {
                thumb.Transitions = enabled ? Slide() : null;
            }
        }
    }

    private static StrongBox<bool> InstantFlag(ListBox list) => Instant.GetValue(list, _ => new StrongBox<bool>());

    // Only the transform: the cells are a UniformGrid, so the width changes only with the window.
    private static Transitions Slide() =>
    [
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = Motion.InOut },
    ];

    private static void Track(ListBox list, Border thumb)
    {
        var placed = false;
        var instant = InstantFlag(list);
        void Update(bool snap)
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
            // The Canvas measures as nothing, so a new thumb size alone does not re-arrange it.
            (host as Avalonia.Layout.Layoutable)?.InvalidateArrange();
            var move = new TransformOperations.Builder(1);
            move.AppendTranslate(origin.Value.X, origin.Value.Y);
            if (placed && (snap || instant.Value) && thumb.Transitions != null)
            {
                thumb.Transitions = null;
                thumb.RenderTransform = move.Build();
                // Back on after this change has been drawn, so the next mouse click slides again.
                Dispatcher.UIThread.Post(() =>
                {
                    if (Motion.Enabled)
                    {
                        thumb.Transitions = Slide();
                    }
                }, DispatcherPriority.Background);
                return;
            }
            thumb.RenderTransform = move.Build();
            if (!placed)
            {
                // The first placement doesn't slide in from the corner; later changes do.
                placed = true;
                Thumbs.RemoveAll(w => !w.TryGetTarget(out _));
                Thumbs.Add(new WeakReference<Border>(thumb));
                if (Motion.Enabled)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (Motion.Enabled)
                        {
                            thumb.Transitions = Slide();
                        }
                    }, DispatcherPriority.Background);
                }
            }
        }

        // The Corners setting rescales the item radius; the thumb copied the old one when it was placed.
        var watching = false;
        Action reshape = () => Dispatcher.UIThread.Post(() =>
        {
            if (list.SelectedIndex >= 0 && list.ContainerFromIndex(list.SelectedIndex) is ListBoxItem item)
            {
                thumb.CornerRadius = item.CornerRadius;
            }
        }, DispatcherPriority.Render);
        void Watch(bool on)
        {
            if (on != watching)
            {
                watching = on;
                if (on)
                {
                    Appearance.Changed += reshape;
                }
                else
                {
                    Appearance.Changed -= reshape;
                }
            }
        }
        list.AttachedToVisualTree += (_, _) => Watch(true);
        list.DetachedFromVisualTree += (_, _) => Watch(false);
        Watch(true);

        list.AddHandler(InputElement.KeyDownEvent, (_, _) => instant.Value = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(InputElement.PointerPressedEvent, (_, _) => instant.Value = false, RoutingStrategies.Tunnel, handledEventsToo: true);
        list.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(() => Update(snap: false), DispatcherPriority.Render);
        list.SizeChanged += (_, _) => Update(snap: true);
        list.LayoutUpdated += (_, _) =>
        {
            if (!placed)
            {
                Update(snap: false);
            }
        };
        Dispatcher.UIThread.Post(() => Update(snap: false), DispatcherPriority.Loaded);
    }
}
