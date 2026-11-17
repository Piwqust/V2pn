using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// The server list as the Appearance page sets it: the tabs (icons, names or both; on top or at the
/// bottom), the rows (flags, second line), and the feature rail (where it sits, row, column or icons,
/// which buttons). The home tab's sections are built by SidebarViewModel from the same settings.
/// </summary>
public partial class MainWindow
{
    private static readonly string[] StartTabIds = ["home", "countries", "subscriptions"];

    private static int StartTabIndex() => Math.Max(0, Array.IndexOf(StartTabIds, Appearance.Current.StartTab));

    private void ArrangeSidebar()
    {
        var a = Appearance.Current;
        Classes.Set("noflags", !a.ShowFlags);
        Classes.Set("nosubtitles", !a.ShowSubtitles);
        lstTabs.Classes.Set("icons", a.TabsStyle == "icons");
        lstTabs.Classes.Set("labels", a.TabsStyle == "labels");
        // One line needs less height than icon over name. Set here: a style setter lost to the sidetabs one.
        foreach (var tab in lstTabs.Items.OfType<ListBoxItem>())
        {
            if (a.TabsStyle is "icons" or "labels")
            {
                tab.MinHeight = 34;
            }
            else
            {
                tab.ClearValue(MinHeightProperty);
            }
        }

        var tabsDock = a.TabsPosition == "bottom" ? Dock.Bottom : Dock.Top;
        var railDock = a.RailPosition == "top" ? Dock.Top : Dock.Bottom;
        // Moving a control re-attaches it (the tab switch loses its thumb animation), so only on a change.
        if (lstTabs.Parent is DockPanel dock && (DockPanel.GetDock(lstTabs) != tabsDock || DockPanel.GetDock(railHost) != railDock))
        {
            dock.Children.Remove(lstTabs);
            dock.Children.Remove(railHost);
            DockPanel.SetDock(lstTabs, tabsDock);
            DockPanel.SetDock(railHost, railDock);
            // Docked on top they follow the search bar, tabs first. Docked at the bottom they go just before
            // the list, which fills what is left; the first of them is the lowest, so the tabs stay at the edge.
            var top = dock.Children.IndexOf(gridSearch) + 1;
            if (tabsDock == Dock.Top)
            {
                dock.Children.Insert(top++, lstTabs);
            }
            if (railDock == Dock.Top)
            {
                dock.Children.Insert(top, railHost);
            }
            if (tabsDock == Dock.Bottom)
            {
                dock.Children.Insert(dock.Children.IndexOf(lstRows), lstTabs);
            }
            if (railDock == Dock.Bottom)
            {
                dock.Children.Insert(dock.Children.IndexOf(lstRows), railHost);
            }
        }
        lstTabs.Margin = tabsDock == Dock.Top ? new Thickness(10, 0, 10, 6) : new Thickness(10, 6, 10, 10);
        // The hairline faces the list.
        railHost.BorderThickness = railDock == Dock.Top ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0);
        railHost.Margin = railDock == Dock.Top ? new Thickness(0, 0, 0, 2) : default;

        var column = a.RailLayout == "column";
        railHost.Classes.Set("column", column);
        railHost.Classes.Set("icons", a.RailLayout == "icons");
        gridRail.Rows = column ? 0 : 1;
        gridRail.Columns = column ? 1 : 0;
        btnRailUpdate.IsVisible = a.RailUpdate;
        btnRailDns.IsVisible = a.RailDns ?? _config.UiItem.V2pnSettingsAdvanced;
        btnRailSettings.IsVisible = a.RailSettings;
        // The one-column window is short on height; its actions are in the ☰ menu.
        railHost.IsVisible = _tier != BodyTier.Stacked
            && a.RailPosition != "hidden"
            && (btnRailUpdate.IsVisible || btnRailDns.IsVisible || btnRailSettings.IsVisible);
    }
}
