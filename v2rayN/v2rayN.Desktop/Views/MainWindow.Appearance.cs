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

    private void ApplyLayout()
    {
        var a = Appearance.Current;
        var width = a.SidebarWidth switch
        {
            "narrow" => 272,
            "wide" => 360,
            _ => 312,
        };
        var right = a.SidebarSide == "right";
        gridBody.ColumnDefinitions = right
            ? new ColumnDefinitions($"*,{width}")
            : new ColumnDefinitions($"{width},*");
        Grid.SetColumn(borderSidebar, right ? 1 : 0);
        Grid.SetColumn(borderStage, right ? 0 : 1);
        borderSidebar.Margin = right ? new Thickness(10, 0, 0, 0) : new Thickness(0, 0, 10, 0);

        // A hidden map is also a cheaper one: nothing under the header has to be redrawn.
        mapView.IsVisible = a.ShowMap;
        // The map reads its colours once per paint, not through resource bindings.
        mapView.Repaint();
        contentStatusBarView.IsVisible = a.ShowStatusCard;
        _statusView?.ShowSpeedChart(a.ShowSpeedChart);

        Classes.Set("dense", a.Density == "compact");
    }
}
