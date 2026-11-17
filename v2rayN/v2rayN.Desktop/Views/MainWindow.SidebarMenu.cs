using DialogHostAvalonia;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

/// <summary>
/// Right-click on the server list changes it in place: the tabs, the buttons, a home section, or the
/// list as a whole. Every item writes the same settings the Appearance page does, so both stay in step.
/// </summary>
public partial class MainWindow
{
    private void InitSidebarMenus()
    {
        lstTabs.ContextRequested += (_, e) => ShowSidebarMenu(e, lstTabs, TabMenuItems());
        railHost.ContextRequested += (_, e) => ShowSidebarMenu(e, railHost, RailMenuItems());
    }

    private static void Look(Action<AppearanceSettings> edit)
    {
        var next = Appearance.Current.Clone();
        edit(next);
        Appearance.Set(next);
    }

    /// <summary>What was clicked first, then the whole list in a submenu (or on its own for an empty spot).</summary>
    private void ShowSidebarMenu(ContextRequestedEventArgs e, Control target, List<object>? specific)
    {
        e.Handled = true;
        var flyout = new MenuFlyout { Placement = ContextPlacement(e, target) };
        if (specific is { Count: > 0 })
        {
            foreach (var item in specific)
            {
                flyout.Items.Add(item);
            }
            flyout.Items.Add(new Separator());
            flyout.Items.Add(Sub(L.ApGrpSidebar, ListMenuItems()));
        }
        else
        {
            foreach (var item in ListMenuItems())
            {
                flyout.Items.Add(item);
            }
        }
        flyout.ShowAt(target, flyout.Placement == PlacementMode.Pointer);
    }

    private List<object> TabMenuItems()
    {
        var a = Appearance.Current;
        return
        [
            Radio(L.ApTabsBoth, a.TabsStyle == "both", () => Look(s => s.TabsStyle = "both")),
            Radio(L.ApTabsIcons, a.TabsStyle == "icons", () => Look(s => s.TabsStyle = "icons")),
            Radio(L.ApTabsLabels, a.TabsStyle == "labels", () => Look(s => s.TabsStyle = "labels")),
            new Separator(),
            Radio(L.ApTop, a.TabsPosition != "bottom", () => Look(s => s.TabsPosition = "top")),
            Radio(L.ApBottom, a.TabsPosition == "bottom", () => Look(s => s.TabsPosition = "bottom")),
            new Separator(),
            Sub(L.ApStartTab, [.. Appearance.TabIds.Select(id => Radio(TabName(id), a.StartTab == id, () => Look(s => s.StartTab = id)))]),
        ];
    }

    private static string TabName(string id) => id switch
    {
        "countries" => L.TabCountries,
        "subscriptions" => L.TabSubscriptions,
        _ => L.TabHome,
    };

    private List<object> RailMenuItems()
    {
        var a = Appearance.Current;
        var dns = a.RailDns ?? _config.UiItem.V2pnSettingsAdvanced;
        return
        [
            Radio(L.ApBottom, a.RailPosition is not ("top" or "hidden"), () => Look(s => s.RailPosition = "bottom")),
            Radio(L.ApTop, a.RailPosition == "top", () => Look(s => s.RailPosition = "top")),
            Radio(L.ApHidden, a.RailPosition == "hidden", () => Look(s => s.RailPosition = "hidden")),
            new Separator(),
            Radio(L.ApButtonsRow, a.RailLayout is not ("column" or "icons"), () => Look(s => s.RailLayout = "row")),
            Radio(L.ApButtonsColumn, a.RailLayout == "column", () => Look(s => s.RailLayout = "column")),
            Radio(L.ApButtonsIcons, a.RailLayout == "icons", () => Look(s => s.RailLayout = "icons")),
            new Separator(),
            Check(L.RailUpdate, a.RailUpdate, v => Look(s => s.RailUpdate = v)),
            Check(L.RailDns, dns, v => Look(s => s.RailDns = v)),
            Check(L.RailSettings, a.RailSettings, v => Look(s => s.RailSettings = v)),
        ];
    }

    /// <summary>A home section: move it, size it, hide it, and bring back one that was hidden.</summary>
    private static List<object> SectionMenuItems(string id)
    {
        var a = Appearance.Current;
        var shown = Appearance.FullOrder(a.HomeSections, Appearance.HomeSectionIds).Where(a.HomeSections.Contains).ToList();
        var at = shown.IndexOf(id);
        var items = new List<object>();
        if (at > 0)
        {
            items.Add(Item(L.ApMoveUp, () => { MoveSection(id, at - 1); return Task.CompletedTask; }, "SemiIconArrowUp"));
        }
        if (at >= 0 && at < shown.Count - 1)
        {
            items.Add(Item(L.ApMoveDown, () => { MoveSection(id, at + 1); return Task.CompletedTask; }, "SemiIconArrowDown"));
        }
        int[]? counts = id switch
        {
            "favorites" => [3, 5, 10, 0],
            "recent" or "quick" => [3, 5, 10],
            _ => null,
        };
        if (counts != null)
        {
            var now = id switch { "favorites" => a.HomeFavorites, "recent" => a.HomeRecent, _ => a.HomeQuick };
            items.Add(Sub(L.SbCount, [.. counts.Select(n => Radio(n == 0 ? L.ApAll : n.ToString(), n == now, () => Look(s =>
            {
                switch (id)
                {
                    case "favorites": s.HomeFavorites = n; break;
                    case "recent": s.HomeRecent = n; break;
                    default: s.HomeQuick = n; break;
                }
            })))]));
        }
        items.Add(Item(L.SbHideSection, () => { Look(s => s.HomeSections.Remove(id)); return Task.CompletedTask; }, "SemiIconMinusCircleStroked"));
        if (HiddenSectionsItem() is { } hidden)
        {
            items.Add(hidden);
        }
        return items;
    }

    private static MenuItem? HiddenSectionsItem()
    {
        var hidden = Appearance.HomeSectionIds.Except(Appearance.Current.HomeSections).ToList();
        return hidden.Count == 0
            ? null
            : Sub(L.SbShowSection, [.. hidden.Select(id => Item(L.ApHomeSection(id), () => { Look(s => s.HomeSections.Add(id)); return Task.CompletedTask; }))]);
    }

    /// <summary>Puts a shown section at <paramref name="index"/> among the shown ones.</summary>
    private static void MoveSection(string id, int index) => Look(s =>
    {
        var shown = s.HomeSections.Where(Appearance.HomeSectionIds.Contains).Distinct().ToList();
        if (!shown.Remove(id))
        {
            return;
        }
        shown.Insert(Math.Clamp(index, 0, shown.Count), id);
        s.HomeSections = shown;
    });

    private List<object> ListMenuItems()
    {
        var a = Appearance.Current;
        var items = new List<object>
        {
            Sub(L.SbTabs, TabMenuItems()),
            Sub(L.SbButtons, RailMenuItems()),
        };
        if (lstTabs.SelectedItem is { } tab && TabOf(tab) == SidebarTab.Home && HiddenSectionsItem() is { } hidden)
        {
            items.Add(hidden);
        }
        items.Add(new Separator());
        items.Add(Check(L.ApFlags, a.ShowFlags, v => Look(s => s.ShowFlags = v)));
        items.Add(Check(L.ApSubtitles, a.ShowSubtitles, v => Look(s => s.ShowSubtitles = v)));
        items.Add(Check(L.SbCompact, a.Density == "compact", v => Look(s => s.Density = v ? "compact" : "comfortable")));
        items.Add(Sub(L.ApSidebar,
        [
            Radio(L.ApSideLeft, a.SidebarSide != "right", () => Look(s => s.SidebarSide = "left")),
            Radio(L.ApSideRight, a.SidebarSide == "right", () => Look(s => s.SidebarSide = "right")),
        ]));
        items.Add(Sub(L.ApWidth,
        [
            Radio(L.ApWidthNarrow, a.SidebarWidth == "narrow", () => Look(s => s.SidebarWidth = "narrow")),
            Radio(L.ApWidthNormal, a.SidebarWidth is not ("narrow" or "wide"), () => Look(s => s.SidebarWidth = "normal")),
            Radio(L.ApWidthWide, a.SidebarWidth == "wide", () => Look(s => s.SidebarWidth = "wide")),
        ]));
        items.Add(new Separator());
        items.Add(new MenuItem { Header = L.SbDragHint, IsEnabled = false });
        items.Add(Item(L.SbAllAppearance, async () => await DialogHost.Show(new AppearanceView(_themeSettings)), "SemiIconColorPalette"));
        items.Add(Item(L.SbReset, () => { Look(s => s.ResetSidebar()); return Task.CompletedTask; }, "SemiIconHistory"));
        return items;
    }

    private static MenuItem Radio(string header, bool on, Action act)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = on };
        Avalonia.Automation.AutomationProperties.SetName(item, on ? header + L.OptionSelected : header);
        item.Click += (_, _) =>
        {
            if (!on)
            {
                act();
            }
        };
        return item;
    }

    private static MenuItem Check(string header, bool on, Action<bool> act)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
        item.Click += (_, _) => act(!on);
        return item;
    }

    private static MenuItem Sub(string header, List<object> items)
    {
        var sub = new MenuItem { Header = header };
        foreach (var item in items)
        {
            sub.Items.Add(item);
        }
        return sub;
    }
}
