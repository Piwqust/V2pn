namespace v2rayN.Desktop.Common;

/// <summary>
/// V2pn on a Mac: what the menus of the menu bar, the status item and the Dock are put together from. macOS shows a
/// native menu item as available only when something handles its click, so a line of plain text is an item with no
/// action.
/// </summary>
internal static class MacMenus
{
    public const KeyModifiers Cmd = KeyModifiers.Meta;

    public static NativeMenuItem Item(string header, Action click, KeyGesture? gesture = null, Bitmap? icon = null, string? tip = null)
    {
        var item = new NativeMenuItem(header) { Gesture = gesture, Icon = icon, ToolTip = tip };
        item.Click += (_, _) => Run(click);
        return item;
    }

    public static NativeMenuItem Item(string header, Func<Task> click, KeyGesture? gesture = null, Bitmap? icon = null, string? tip = null) =>
        Item(header, () => { _ = RunAsync(click); }, gesture, icon, tip);

    /// <summary>A line that says something and does nothing: the state, a section's name.</summary>
    public static NativeMenuItem Label(string text) => new(text) { IsEnabled = false };

    public static NativeMenuItem Submenu(string header, NativeMenu menu, Bitmap? icon = null) => new(header) { Menu = menu, Icon = icon };

    public static NativeMenuItem Choice(string header, bool chosen, Action click, Bitmap? icon = null, string? tip = null)
    {
        var item = Item(header, click, icon: icon, tip: tip);
        item.ToggleType = MenuItemToggleType.CheckBox;
        item.IsChecked = chosen;
        return item;
    }

    public static NativeMenuItem Choice(string header, bool chosen, Func<Task> click, Bitmap? icon = null, string? tip = null) =>
        Choice(header, chosen, () => { _ = RunAsync(click); }, icon, tip);

    public static NativeMenuItemSeparator Separator() => new();

    /// <summary>Replaces what a menu holds. The menu object stays: Avalonia keeps the native one tied to it.</summary>
    public static void Fill(NativeMenu menu, IEnumerable<NativeMenuItemBase> items)
    {
        menu.Items.Clear();
        NativeMenuItemBase? last = null;
        foreach (var item in items)
        {
            // Sections come and go with the data; two separators in a row or one at an end would show.
            if (item is NativeMenuItemSeparator && last is null or NativeMenuItemSeparator)
            {
                continue;
            }
            menu.Items.Add(item);
            last = item;
        }
        if (last is NativeMenuItemSeparator)
        {
            menu.Items.Remove(last);
        }
    }

    public static NativeMenu Menu(params NativeMenuItemBase[] items)
    {
        var menu = new NativeMenu();
        Fill(menu, items);
        return menu;
    }

    /// <summary>
    /// The Edit menu. A key that a menu item claims never reaches the window (macOS asks the menu bar first), so each
    /// item sends its key on to the control that has the focus: a text field copies and pastes as before, and the
    /// window's own ⌘V (add from the clipboard) and ⌘C (copy a server's link) still work when no field has it.
    /// </summary>
    public static NativeMenu EditMenu(Action? find = null)
    {
        var items = new List<NativeMenuItemBase>
        {
            Item(L.MacUndo, () => SendKey(Key.Z, Cmd), new KeyGesture(Key.Z, Cmd)),
            Item(L.MacRedo, () => SendKey(Key.Z, Cmd | KeyModifiers.Shift), new KeyGesture(Key.Z, Cmd | KeyModifiers.Shift)),
            Separator(),
            Item(L.MacCut, () => SendKey(Key.X, Cmd), new KeyGesture(Key.X, Cmd)),
            Item(L.MacCopy, () => SendKey(Key.C, Cmd), new KeyGesture(Key.C, Cmd)),
            Item(L.MacPaste, () => SendKey(Key.V, Cmd), new KeyGesture(Key.V, Cmd)),
            Item(L.MacSelectAll, () => SendKey(Key.A, Cmd), new KeyGesture(Key.A, Cmd)),
        };
        if (find != null)
        {
            items.Add(Separator());
            items.Add(Item(L.MacFindServer, find, new KeyGesture(Key.F, Cmd)));
        }
        return Menu([.. items]);
    }

    /// <summary>Minimize, Zoom and Close for whichever window is in front; <paramref name="extra"/> goes between them.</summary>
    public static NativeMenu WindowMenu(params NativeMenuItemBase[] extra)
    {
        var items = new List<NativeMenuItemBase>
        {
            Item(L.MacMinimize, () => WithActive(w => w.WindowState = WindowState.Minimized), new KeyGesture(Key.M, Cmd)),
            Item(L.MacZoom, () => WithActive(w =>
            {
                if (w.CanMaximize)
                {
                    w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                }
            })),
            Separator(),
        };
        items.AddRange(extra);
        items.Add(Separator());
        // The main window answers Close by hiding (V2pn stays in the menu bar); the others close.
        items.Add(Item(L.MacCloseWindow, () => WithActive(w => w.Close()), new KeyGesture(Key.W, Cmd)));
        return Menu([.. items]);
    }

    /// <summary>
    /// A window other than the main one (settings, a server's editor): Edit, so its fields copy and paste from the
    /// menu bar too, and Window. Without a menu of its own the menu bar would show the app menu alone.
    /// </summary>
    public static void AttachBasic(Window window)
    {
        if (!OperatingSystem.IsMacOS() || Design.IsDesignMode)
        {
            return;
        }
        var bar = new NativeMenu();
        bar.Items.Add(Submenu(L.MacEdit, EditMenu()));
        bar.Items.Add(Submenu(L.MacWindow, WindowMenu(Item(L.MacMainWindow, () =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: Views.MainWindow main })
            {
                main.ShowHideWindow(true);
            }
        }, new KeyGesture(Key.D0, Cmd)))));
        NativeMenu.SetMenu(window, bar);
    }

    /// <summary>A shortcut as macOS writes it in its own menus: the modifiers in the system's order, then the key, nothing between.</summary>
    public static string FormatGesture(KeyGesture gesture)
    {
        var sb = new StringBuilder();
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            sb.Append('⌃');
        }
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            sb.Append('⌥');
        }
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            sb.Append('⇧');
        }
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            sb.Append('⌘');
        }
        return sb.Append(gesture.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (gesture.Key - Key.D0))).ToString(),
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.Enter => "↩",
            Key.Escape => "⎋",
            Key.Tab => "⇥",
            Key.Space => "␣",
            Key.Back => "⌫",
            Key.Delete => "⌦",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Left => "←",
            Key.Right => "→",
            _ => gesture.Key.ToString(),
        }).ToString();
    }

    public static Window? ActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }
        return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
    }

    private static void WithActive(Action<Window> action)
    {
        if (ActiveWindow() is { } window)
        {
            action(window);
        }
    }

    public static void SendKey(Key key, KeyModifiers modifiers)
    {
        if (ActiveWindow() is not { IsVisible: true } window)
        {
            return;
        }
        var target = window.FocusManager?.GetFocusedElement() as Interactive ?? window;
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = target,
            Key = key,
            KeyModifiers = modifiers,
        });
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(MacMenus), ex);
        }
    }

    private static async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(MacMenus), ex);
        }
    }
}

/// <summary>
/// V2pn: the status item's pictures. macOS asks for a template image there: one colour and transparency, which the
/// system paints dark or light to suit the menu bar. So the state is told by shape, not colour: the fork in outline
/// when off, solid when connected, solid with a dot when the connection needs attention.
/// </summary>
internal static class MacStatusIcons
{
    // Four pixels a point: sharp on any display, the system scales the picture to the menu bar's height.
    private const int Size = 72;
    private static readonly WindowIcon?[] _icons = new WindowIcon?[4];

    /// <param name="index">As AvaUtils.AppIconIndex: 1 off, 2 connected, 3 needs attention.</param>
    public static WindowIcon Get(int index)
    {
        index = Math.Clamp(index, 1, 3);
        return _icons[index] ??= new WindowIcon(Render(index));
    }

    public static Bitmap Render(int index)
    {
        var fork = StreamGeometry.Parse("M0,0 L32,0 L32,40 L72,0 L104,0 L104,32 L85.255,32 L64,53.255 L64,112 L32,112 L32,72 L0,40 Z");
        const double glyph = 62;
        var scale = glyph / 112;
        var width = 104 * scale;
        var origin = new Point((Size - width) / 2, (Size - glyph) / 2);
        var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size));
        using var ctx = bitmap.CreateDrawingContext();
        var ink = Brushes.Black;
        var toGlyph = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y);
        if (index == 1)
        {
            // The outline is drawn twice as wide and cut by the shape itself, so it stays inside the fork.
            using (ctx.PushTransform(toGlyph))
            using (ctx.PushGeometryClip(fork))
            {
                ctx.DrawGeometry(null, new Pen(ink, 2 * 7 / scale), fork);
            }
            return bitmap;
        }
        if (index == 3)
        {
            // The dot sits in the lower right corner, with a ring of nothing around it.
            var dot = new Point(Size - 15, Size - 15);
            var hole = new EllipseGeometry(new Rect(dot.X - 18, dot.Y - 18, 36, 36));
            var rest = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Size, Size)), hole);
            using (ctx.PushGeometryClip(rest))
            using (ctx.PushTransform(toGlyph))
            {
                ctx.DrawGeometry(ink, null, fork);
            }
            ctx.DrawEllipse(ink, null, dot, 12, 12);
            return bitmap;
        }
        using (ctx.PushTransform(toGlyph))
        {
            ctx.DrawGeometry(ink, null, fork);
        }
        return bitmap;
    }
}
