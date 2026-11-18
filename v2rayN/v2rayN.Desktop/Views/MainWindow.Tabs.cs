using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Chrome;
using Avalonia.Layout;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Views;

// The title bar's tabs (Common/Editions.cs), as in a browser: "≡  [V2pn]  [✸ Configra]  …  – □ ×". V2pn's tab is plain
// V2pn; a partner's puts the window into that partner's space. Only a person who came from a partner has them; for
// anyone else the title bar shows V2pn's name as before. A click switches at once, without animation; Ctrl+Tab and
// Ctrl+Shift+Tab go round them (on a Mac also ⌘⇧] and ⌘⇧[, since ⌘Tab is the system's). In the mini window a
// partner's tab shows only its mark, with its name as the tooltip.
public partial class MainWindow
{
    private readonly List<(string? Id, Button Button)> _tabButtons = [];
    private string? _tabsKey;

    private void InitTabs()
    {
        // Tunnel: ahead of the focus moving on Tab.
        AddHandler(KeyDownEvent, OnTabKeys, RoutingStrategies.Tunnel);
    }

    private void OnTabKeys(object? sender, KeyEventArgs e)
    {
        if (Editions.Tabs.Count == 0 || IsDialogOpen())
        {
            return;
        }
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        int? step = e.Key == Key.Tab && command ? e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1
            : OperatingSystem.IsMacOS() && e.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Shift) && e.Key is Key.OemCloseBrackets or Key.OemOpenBrackets
                ? e.Key == Key.OemCloseBrackets ? 1 : -1
                : null;
        if (step is not { } by)
        {
            return;
        }
        e.Handled = true;
        var focused = _tabButtons.Any(t => t.Button.IsFocused);
        Editions.Cycle(by);
        if (focused)
        {
            // Keyboard focus moves with the open tab when it was on the strip.
            _tabButtons.FirstOrDefault(t => t.Id == Editions.Partner?.Id).Button?.Focus(NavigationMethod.Tab);
        }
    }

    /// <summary>The tabs as Editions has them; called whenever the tabs, the open one, the window's size class or light and dark change.</summary>
    private void RefreshTabs()
    {
        var tabs = Editions.Tabs;
        borderBrand.IsVisible = tabs.Count == 0;
        borderTabs.IsVisible = tabs.Count > 0;
        if (tabs.Count == 0)
        {
            panelTabs.Children.Clear();
            _tabButtons.Clear();
            _tabsKey = null;
            return;
        }
        var mini = _tier == BodyTier.Stacked;
        var light = ActualThemeVariant == ThemeVariant.Light;
        var key = $"{string.Join(";", tabs.Select(p => $"{p.Id}|{p.Folder}|{p.Pack.Name}|{p.Pack.Mark}|{p.Pack.Accent}"))}|{mini}|{light}";
        if (key != _tabsKey)
        {
            _tabsKey = key;
            panelTabs.Children.Clear();
            _tabButtons.Clear();
            AddTab(null, V2pnTabContent(light), L.AppName, L.TabV2pnTip, tabs.Count + 1);
            foreach (var partner in tabs)
            {
                AddTab(partner.Id, PartnerTabContent(partner, mini), partner.Name, mini ? partner.Name : string.Format(L.TabPartnerTip, partner.Name), tabs.Count + 1);
            }
        }
        var open = Editions.Partner?.Id;
        foreach (var (id, button) in _tabButtons)
        {
            var isOpen = id == open;
            button.Classes.Set("open", isOpen);
            AutomationProperties.SetItemStatus(button, isOpen ? L.TabOpen : string.Empty);
        }
    }

    private void AddTab(string? partnerId, Control content, string name, string tip, int count)
    {
        var button = new Button
        {
            Theme = this.FindResource("V2TitleTab") as ControlTheme,
            Content = content,
            Focusable = true,
        };
        ToolTip.SetTip(button, tip);
        WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetControlTypeOverride(button, AutomationControlType.TabItem);
        AutomationProperties.SetPositionInSet(button, _tabButtons.Count + 1);
        AutomationProperties.SetSizeOfSet(button, count);
        button.Click += (_, _) => Editions.Open(partnerId);
        panelTabs.Children.Add(button);
        _tabButtons.Add((partnerId, button));
    }

    /// <summary>V2pn's fork in V2pn's own red, whatever accent a partner's tab gave the window, and its name.</summary>
    private Control V2pnTabContent(bool light) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 7,
        Children =
        {
            new Avalonia.Controls.Shapes.Path
            {
                Width = 14,
                Height = 15,
                Data = this.FindResource("V2BrandFork") as Geometry,
                Fill = new SolidColorBrush(Color.Parse(light ? "#E8391B" : "#FF3D1F")),
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
            },
            new TextBlock { Text = L.AppName, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    /// <summary>The partner's mark from its pack (or its initial on its colour) and its name; the mark alone in the mini window.</summary>
    private static Control PartnerTabContent(V2pnPartner partner, bool mini)
    {
        var accent = Editions.AccentOf(partner);
        var onAccent = Appearance.OnAccent(Color.Parse(accent));
        var mark = Scene.Mark(partner, accent, $"#{onAccent.R:X2}{onAccent.G:X2}{onAccent.B:X2}", 18);
        if (mini)
        {
            return mark;
        }
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children =
            {
                mark,
                new TextBlock { Text = partner.Name, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }
}
