using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

/// <summary>
/// Dragging in the server list: the rail's buttons and the tabs change places, and taken to the other
/// end of the list they move there; a home section moves by its first row (its title). A picture of what
/// is dragged follows the pointer and a line shows where it lands; Escape or a lost pointer cancels.
/// Nothing moves before the pointer has gone a few pixels, so a click is still a click.
/// </summary>
public partial class MainWindow
{
    private const double DragThreshold = 6;
    private const double LineThickness = 3;

    /// <summary>One drag: what is dragged, where it would land for a pointer position, and how to apply that.</summary>
    private sealed class DragSpec(Control source, Func<Point, Rect?> target, Action commit)
    {
        public Control Source { get; } = source;

        /// <summary>The line for this pointer position (layer coordinates), or null where a drop changes nothing.</summary>
        public Func<Point, Rect?> Target { get; } = target;

        public Action Commit { get; } = commit;
        public bool CanDrop { get; set; }
    }

    private DragSpec? _drag;
    private Control? _dragHost;
    private IPointer? _dragPointer;
    private Point _dragStart;
    private Vector _dragGrab;
    private double _dragSourceOpacity = 1;
    private bool _dragActive;

    private void InitSidebarDrag()
    {
        Draggable(railHost, BeginRailDrag);
        Draggable(lstTabs, BeginTabDrag);
        Draggable(lstRows, BeginSectionDrag);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (_dragActive && e.Key == Key.Escape)
            {
                e.Handled = true;
                EndDrag(commit: false);
            }
        }, RoutingStrategies.Tunnel);
    }

    // Tunnel handlers with handledEventsToo: buttons and list items handle the press themselves.
    private void Draggable(Control host, Func<Visual, DragSpec?> begin)
    {
        host.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_dragActive || !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            {
                return;
            }
            _drag = e.Source is Visual source ? begin(source) : null;
            _dragHost = host;
            _dragStart = e.GetPosition(sidebarDragLayer);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        host.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (_drag == null || _dragHost != host)
            {
                return;
            }
            var p = e.GetPosition(sidebarDragLayer);
            if (!_dragActive)
            {
                if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
                {
                    _drag = null;
                    return;
                }
                if (Math.Abs(p.X - _dragStart.X) < DragThreshold && Math.Abs(p.Y - _dragStart.Y) < DragThreshold)
                {
                    return;
                }
                StartDrag(e.Pointer);
            }
            e.Handled = true;
            MoveDrag(p);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        host.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (_dragHost != host)
            {
                return;
            }
            if (_dragActive)
            {
                // Handled here, the button under the pointer gets no click.
                e.Handled = true;
                EndDrag(commit: true);
            }
            _drag = null;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        host.AddHandler(PointerCaptureLostEvent, (_, _) =>
        {
            if (_dragActive && _dragHost == host)
            {
                EndDrag(commit: false);
            }
        });
    }

    private void StartDrag(IPointer pointer)
    {
        if (_drag == null || _dragHost == null)
        {
            return;
        }
        _dragActive = true;
        _dragPointer = pointer;
        // Taken from the button or item, which then neither stays pressed nor clicks.
        pointer.Capture(_dragHost);

        var source = _drag.Source;
        var origin = source.TranslatePoint(default, sidebarDragLayer) ?? default;
        _dragGrab = _dragStart - origin;
        try
        {
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(source.Bounds.Width * scale)), Math.Max(1, (int)Math.Ceiling(source.Bounds.Height * scale)));
            var picture = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            picture.Render(source);
            dragGhostImage.Source = picture;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Sidebar drag", ex);
        }
        dragGhost.Width = source.Bounds.Width;
        dragGhost.Height = source.Bounds.Height;
        dragGhost.IsVisible = true;
        _dragSourceOpacity = source.Opacity;
        source.Opacity = 0.35;
        _dragHost.Cursor = new Cursor(StandardCursorType.DragMove);
    }

    private void MoveDrag(Point p)
    {
        if (_drag == null)
        {
            return;
        }
        Canvas.SetLeft(dragGhost, p.X - _dragGrab.X);
        Canvas.SetTop(dragGhost, p.Y - _dragGrab.Y);
        var line = _drag.Target(p);
        _drag.CanDrop = line != null;
        dragLine.IsVisible = line != null;
        if (line is { } r)
        {
            Canvas.SetLeft(dragLine, r.X);
            Canvas.SetTop(dragLine, r.Y);
            dragLine.Width = r.Width;
            dragLine.Height = r.Height;
        }
    }

    private void EndDrag(bool commit)
    {
        if (!_dragActive)
        {
            return;
        }
        // First, so the capture released below does not end it a second time.
        _dragActive = false;
        var drag = _drag;
        _drag = null;
        dragGhost.IsVisible = false;
        (dragGhostImage.Source as IDisposable)?.Dispose();
        dragGhostImage.Source = null;
        dragLine.IsVisible = false;
        if (drag != null)
        {
            drag.Source.Opacity = _dragSourceOpacity;
        }
        _dragHost?.ClearValue(CursorProperty);
        _dragPointer?.Capture(null);
        _dragPointer = null;
        if (commit && drag is { CanDrop: true })
        {
            drag.Commit();
        }
    }

    private Rect InLayer(Control c) => new(c.TranslatePoint(default, sidebarDragLayer) ?? default, c.Bounds.Size);

    /// <summary>
    /// Where among <paramref name="items"/> (in their order on screen) the pointer would put the dragged one:
    /// the index to insert before, and the line there. Null when that is where it already is.
    /// </summary>
    private (int Index, Rect Line)? Slot(IReadOnlyList<Control> items, Control dragged, Point p, bool vertical)
    {
        if (items.Count == 0)
        {
            return null;
        }
        var rects = items.Select(InLayer).ToList();
        var index = rects.Count;
        for (var i = 0; i < rects.Count; i++)
        {
            if (vertical ? p.Y < rects[i].Center.Y : p.X < rects[i].Center.X)
            {
                index = i;
                break;
            }
        }
        var from = items.ToList().IndexOf(dragged);
        if (index == from || index == from + 1)
        {
            return null;
        }
        Rect line;
        if (vertical)
        {
            var y = index < rects.Count ? rects[index].Top : rects[^1].Bottom;
            var x = rects.Min(r => r.Left);
            line = new Rect(x + 4, y - (LineThickness / 2), Math.Max(8, rects.Max(r => r.Right) - x - 8), LineThickness);
        }
        else
        {
            var x = index < rects.Count ? rects[index].Left : rects[^1].Right;
            var r0 = index < rects.Count ? rects[index] : rects[^1];
            line = new Rect(x - (LineThickness / 2), r0.Top + 4, LineThickness, Math.Max(8, r0.Height - 8));
        }
        return (index, line);
    }

    /// <summary>
    /// Taken toward the other end of the list (its outer third), a bar shows the button row or the tabs would
    /// move there. Returns "top", "bottom" or null.
    /// </summary>
    private (string Edge, Rect Line)? EdgeSlot(Point p, Dock now)
    {
        var area = InLayer(dockSidebar);
        var third = area.Height / 3;
        var edge = p.Y < area.Top + third ? Dock.Top : p.Y > area.Bottom - third ? Dock.Bottom : (Dock?)null;
        if (edge == null || edge == now)
        {
            return null;
        }
        var y = edge == Dock.Top ? InLayer(gridSearch).Bottom + 2 : area.Bottom - 8;
        return (edge == Dock.Top ? "top" : "bottom", new Rect(area.Left + 12, y, area.Width - 24, LineThickness + 1));
    }

    /// <summary>
    /// <paramref name="id"/> moved to <paramref name="index"/> among the shown ids (before the move). Hidden ids
    /// keep their places in the full order.
    /// </summary>
    private static List<string> Reorder(List<string> full, IReadOnlyList<string> shown, string id, int index)
    {
        var moved = shown.ToList();
        var from = moved.IndexOf(id);
        if (from < 0)
        {
            return full;
        }
        if (index > from)
        {
            index--;
        }
        moved.RemoveAt(from);
        moved.Insert(Math.Clamp(index, 0, moved.Count), id);
        var queue = new Queue<string>(moved);
        return [.. full.Select(x => shown.Contains(x) ? queue.Dequeue() : x)];
    }

    private DragSpec? BeginRailDrag(Visual source)
    {
        if (source.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("rail")) is not { } button)
        {
            return null;
        }
        var ids = RailButtons().ToDictionary(kv => kv.Value, kv => kv.Key);
        var id = ids[button];
        int? index = null;
        string? edge = null;
        return new DragSpec(button, p =>
        {
            (index, edge) = (null, null);
            var visible = gridRail.Children.OfType<Button>().Where(b => b.IsVisible).Cast<Control>().ToList();
            if (InLayer(railHost).Inflate(new Thickness(0, 20)).Contains(p))
            {
                var slot = Slot(visible, button, p, Appearance.Current.RailLayout == "column");
                index = slot?.Index;
                return slot?.Line;
            }
            var to = EdgeSlot(p, DockPanel.GetDock(railHost));
            edge = to?.Edge;
            return to?.Line;
        }, () =>
        {
            if (edge != null)
            {
                Look(s => s.RailPosition = edge);
            }
            else if (index is { } i)
            {
                var shown = gridRail.Children.OfType<Button>().Where(b => b.IsVisible).Select(b => ids[b]).ToList();
                Look(s => s.RailOrder = Reorder(Appearance.FullOrder(s.RailOrder, Appearance.RailIds), shown, id, i));
            }
        });
    }

    private DragSpec? BeginTabDrag(Visual source)
    {
        if (source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is not { } tab || tab.Parent != lstTabs)
        {
            return null;
        }
        var id = TabId(TabOf(tab));
        int? index = null;
        string? edge = null;
        return new DragSpec(tab, p =>
        {
            (index, edge) = (null, null);
            if (InLayer(lstTabs).Inflate(new Thickness(0, 20)).Contains(p))
            {
                var slot = Slot([.. TabItems()], tab, p, vertical: false);
                index = slot?.Index;
                return slot?.Line;
            }
            var to = EdgeSlot(p, DockPanel.GetDock(lstTabs));
            edge = to?.Edge;
            return to?.Line;
        }, () =>
        {
            if (edge != null)
            {
                Look(s => s.TabsPosition = edge);
            }
            else if (index is { } i)
            {
                var shown = TabItems().Select(t => TabId(TabOf(t))).ToList();
                Look(s => s.TabOrder = Reorder(Appearance.FullOrder(s.TabOrder, Appearance.TabIds), shown, id, i));
            }
        });
    }

    // A home section moves by its first row: its title, or the fastest-server row that has none.
    private DragSpec? BeginSectionDrag(Visual source)
    {
        if (_sidebar is not { } sidebar || sidebar.Search.IsNotEmpty()
            || source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is not { DataContext: SideRow { Section: { } id } row } container
            || sidebar.Rows.FirstOrDefault(r => r.Section == id) != row)
        {
            return null;
        }
        int? index = null;
        return new DragSpec(container, p =>
        {
            index = null;
            // Each section as one block, from its first row to its last, in the order shown.
            var blocks = new List<Control>();
            var order = new List<string>();
            foreach (var group in sidebar.Rows.Where(r => r.Section != null).GroupBy(r => r.Section!))
            {
                if (lstRows.ContainerFromItem(group.First()) is Control first)
                {
                    blocks.Add(first);
                    order.Add(group.Key);
                }
            }
            var slot = Slot(blocks, container, p, vertical: true);
            // Past the last title the drop goes to the end, under the last section's rows.
            if (slot is { } s && s.Index == blocks.Count && sidebar.Rows.LastOrDefault(r => r.Section == order[^1]) is { } last
                && lstRows.ContainerFromItem(last) is Control lastRow)
            {
                if (p.Y < InLayer(blocks[^1]).Bottom)
                {
                    return null;
                }
                var bottom = InLayer(lastRow).Bottom;
                slot = (s.Index, s.Line.WithY(bottom - (LineThickness / 2)));
            }
            index = slot?.Index;
            return slot?.Line;
        }, () =>
        {
            if (index is not { } i)
            {
                return;
            }
            var shown = sidebar.Rows.Where(r => r.Section != null).Select(r => r.Section!).Distinct().ToList();
            Look(s =>
            {
                var current = s.HomeSections.Where(Appearance.HomeSectionIds.Contains).Distinct().ToList();
                s.HomeSections = Reorder(current, shown, id, i);
            });
        });
    }
}
