using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using v2rayN.Desktop.Converters;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>
/// A partner look that replaces the sidebar and the map: it reads a snapshot and calls back into the window.
/// It follows its own width the way the plain window does: wide, medium (one column less) and narrow (one
/// column, the list under the controls), and scrolls as a whole when the window is too short. It is built for
/// one <see cref="SceneLook"/> (light or dark, accent, corners, type) and rebuilt when the person changes it.
/// </summary>
public abstract class EditionScene : UserControl
{
    protected enum Fit { Narrow, Medium, Wide }

    private const double WideMin = 980;
    private const double MediumMin = 700;
    private Fit? _fit;

    protected EditionScene(V2pnPartner partner, IEditionActions actions, SceneLook look)
    {
        Partner = partner;
        Actions = actions;
        Look = look;
        Scene.AddButtonStyles(this);
        SizeChanged += (_, e) =>
        {
            var width = e.NewSize.Width;
            var fit = width >= WideMin ? Fit.Wide : width >= MediumMin ? Fit.Medium : Fit.Narrow;
            if (width > 0 && fit != _fit)
            {
                _fit = fit;
                Reflow(fit);
            }
        };
    }

    protected V2pnPartner Partner { get; }
    protected IEditionActions Actions { get; }
    protected SceneLook Look { get; }

    /// <summary>The layout in use; wide until the first measure.</summary>
    protected Fit CurrentFit => _fit ?? Fit.Wide;

    public abstract void Update(EditionSnapshot s);

    /// <summary>Puts the look's parts into the layout for this width. The parts themselves are kept.</summary>
    protected abstract void Reflow(Fit fit);

    /// <summary>A flyout of the servers the snapshot lists, for "Change server".</summary>
    protected void ShowServers(Control anchor, EditionSnapshot? s)
    {
        if (s == null || s.Servers.Count == 0)
        {
            return;
        }
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var server in s.Servers)
        {
            var item = new MenuItem
            {
                Header = server.Delay > 0 ? $"{server.Name}   {server.Delay} {L.SceneMs}" : server.Name,
                Icon = Scene.Flag(server.Code, 18, 13),
                FontWeight = server.Selected ? FontWeight.SemiBold : FontWeight.Normal,
            };
            var id = server.IndexId;
            item.Click += (_, _) => Actions.Select(id);
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }

    /// <summary>Takes a part out of the layout it was in, so the next layout can hold it.</summary>
    protected static T Take<T>(T part) where T : Control
    {
        switch (part.Parent)
        {
            case Panel panel:
                panel.Children.Remove(part);
                break;
            case Decorator decorator when decorator.Child == part:
                decorator.Child = null;
                break;
            case ContentControl host when host.Content == part:
                host.Content = null;
                break;
            case Viewbox box when box.Child == part:
                box.Child = null;
                break;
        }
        return part;
    }

    protected static void Put(Grid grid, Control part, int row, int column, int columnSpan = 1) =>
        ServerBrowser.Put(grid, Take(part), row, column, columnSpan);

    /// <summary>The server list, or how to add the first server when there is none yet.</summary>
    protected static void ShowListOrEmpty(ContentControl host, ServerBrowser browser, EmptyState empty, EditionSnapshot s)
    {
        browser.Update(s);
        empty.Update(s);
        Control body = s.AnyServers ? browser : empty;
        if (host.Content != body)
        {
            host.Content = body;
        }
    }
}
