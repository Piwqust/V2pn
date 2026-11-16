using v2rayN.Desktop.Common;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

/// <summary>
/// The parts of the Appearance page that rearrange the main window rather than recolour it:
/// which side the sidebar is on and how wide, the map, the status card and row density.
/// </summary>
public partial class MainWindow
{
    private ThemeSettingViewModel? _themeSettings;
    private TextBlock? _connectLabel;

    // Built once: changing only the text keeps the button from re-creating its content on every refresh.
    private void SetConnectLabel(string text)
    {
        if (_connectLabel == null)
        {
            _connectLabel = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var power = new PathIcon();
            power.Bind(PathIcon.DataProperty, power.GetResourceObservable("SemiIconPower"));
            btnConnect.Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 9,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Children = { power, _connectLabel },
            };
        }
        _connectLabel.Text = text;
        Avalonia.Automation.AutomationProperties.SetName(btnConnect, text);
    }

    // The sidebar width now applied; a resize re-runs ApplyLayout only when it would change.
    private int _sidebarPixels;

    // Below 1100 px the "wide" sidebar leaves the stage too narrow for the welcome card next to the
    // rail, so it uses the default width for as long as the window stays small. The setting is kept.
    private int SidebarPixels() => SidebarPixels(CurrentWidth());

    private static int SidebarPixels(double windowWidth)
    {
        var a = Appearance.Current;
        return a.SidebarWidth switch
        {
            "narrow" => 272,
            "wide" when windowWidth is > 0 and < 1100 => 312,
            "wide" => 360,
            _ => 312,
        };
    }

    private void ApplyLayout()
    {
        var a = Appearance.Current;
        ArrangeForWidth();
        if (_tier != BodyTier.Stacked)
        {
            var width = SidebarPixels();
            _sidebarPixels = width;
            var right = a.SidebarSide == "right";
            gridBody.ColumnDefinitions = right
                ? new ColumnDefinitions($"*,{width}")
                : new ColumnDefinitions($"{width},*");
            Grid.SetColumn(borderSidebar, right ? 1 : 0);
            Grid.SetColumn(borderStage, right ? 0 : 1);
            borderSidebar.Margin = right ? new Thickness(10, 0, 0, 0) : new Thickness(0, 0, 10, 0);
        }

        // A hidden map is also a cheaper one: nothing under the header has to be redrawn. The one-column
        // layout shows only a strip of it, with the pin under the mode bar, so it goes there too.
        ApplyMapVisibility();
        // The map reads its colours once per paint, not through resource bindings.
        mapView.Repaint();
        // The welcome card replaces the statistics while there are no servers.
        // The one-column layout has no room for it.
        contentStatusBarView.IsVisible = a.ShowStatusCard && !cardWelcome.IsVisible && _tier != BodyTier.Stacked;
        _statusView?.ShowSpeedChart(a.ShowSpeedChart);

        Classes.Set("dense", a.Density == "compact");
    }
}
