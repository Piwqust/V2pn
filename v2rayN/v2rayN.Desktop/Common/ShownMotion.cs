namespace v2rayN.Desktop.Common;

/// <summary>
/// Keeps a style class on a control only while it is really on screen: attached, visible, and inside visible
/// parents and a shown window. Styles key their endless or replayed animations on that class.
/// </summary>
/// <remarks>
/// Avalonia pauses an animation whose control is hidden by a parent, but the paused animation still holds the
/// frame clock, which then wakes the UI thread about 60 times a second. One spinner left visible inside the
/// hidden part of the walkthrough cost 1.7 % of a core with nothing on screen moving, minimized too; a map
/// ripple started while the window sat in the tray would have done the same until the window was opened.
/// Without the class the style no longer matches, the animation ends and the clock stops.
/// </remarks>
public sealed class ShownMotion
{
    /// <summary>The class to set while the control is shown, e.g. "spin" on Path.spinner.</summary>
    public static readonly AttachedProperty<string?> ClassProperty =
        AvaloniaProperty.RegisterAttached<ShownMotion, Visual, string?>("Class");

    private static readonly List<Visual> _tracked = [];
    private static bool _queued;

    static ShownMotion()
    {
        ClassProperty.Changed.AddClassHandler<Visual>(OnClassChanged);
        // Any visibility change may hide or show a tracked control through one of its parents or its window.
        Visual.IsVisibleProperty.Changed.AddClassHandler<Visual>((_, _) => Queue());
    }

    private ShownMotion()
    {
    }

    public static string? GetClass(Visual element) => element.GetValue(ClassProperty);

    public static void SetClass(Visual element, string? value) => element.SetValue(ClassProperty, value);

    private static void OnClassChanged(Visual visual, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is string { Length: > 0 } old)
        {
            visual.Classes.Remove(old);
        }
        visual.AttachedToVisualTree -= OnAttached;
        visual.DetachedFromVisualTree -= OnDetached;
        _tracked.Remove(visual);
        if (e.NewValue is not string { Length: > 0 })
        {
            return;
        }
        visual.AttachedToVisualTree += OnAttached;
        visual.DetachedFromVisualTree += OnDetached;
        if (TopLevel.GetTopLevel(visual) != null)
        {
            _tracked.Add(visual);
            Update(visual);
        }
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Visual visual && !_tracked.Contains(visual))
        {
            _tracked.Add(visual);
            Update(visual);
        }
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Visual visual)
        {
            _tracked.Remove(visual);
            if (GetClass(visual) is { Length: > 0 } cls)
            {
                visual.Classes.Remove(cls);
            }
        }
    }

    // A burst of changes (a list rebuilt, a page shown) ends in one pass, after the visibility has settled.
    private static void Queue()
    {
        if (_queued || _tracked.Count == 0)
        {
            return;
        }
        _queued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _queued = false;
            foreach (var visual in _tracked.ToArray())
            {
                Update(visual);
            }
        });
    }

    private static void Update(Visual visual)
    {
        if (GetClass(visual) is { Length: > 0 } cls)
        {
            visual.Classes.Set(cls, visual.IsEffectivelyVisible);
        }
    }
}
