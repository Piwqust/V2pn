using Avalonia.Controls.Shapes;
using Avalonia.Layout;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>Proxy or VPN: two halves, the one in use filled with the ink of the page.</summary>
public sealed class ThemeSegment : UserControl
{
    private readonly Button _proxy;
    private readonly Button _vpn;
    private readonly string _on;
    private readonly string _onText;
    private readonly string _offText;

    public ThemeSegment(string track, string on, string onText, string offText, FontFamily font, Func<double, CornerRadius> r, IEditionActions actions, double height)
    {
        _on = on;
        _onText = onText;
        _offText = offText;
        Button Half(string text, bool vpn)
        {
            var button = Scene.Button(Scene.Text(text, 13, offText, font, FontWeight.Medium), null, offText, 10, new Thickness(14, 0), height: height - 6);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Click += (_, _) => actions.SetVpn(vpn);
            return button;
        }
        _proxy = Half(L.SceneProxy, false);
        _vpn = Half("VPN", true);
        var grid = new Grid { ColumnDefinitions = new("*,*") };
        ServerBrowser.Put(grid, _proxy, 0, 0);
        ServerBrowser.Put(grid, _vpn, 0, 1);
        Content = new Border { Background = Scene.B(track), CornerRadius = r(12), Padding = new Thickness(3), Child = grid, Height = height };
    }

    public void Update(bool vpn)
    {
        foreach (var (button, on) in new[] { (_proxy, !vpn), (_vpn, vpn) })
        {
            if (button.Content is Border { Child: TextBlock label } border)
            {
                border.Background = on ? Scene.B(_on) : Brushes.Transparent;
                label.Foreground = Scene.B(on ? _onText : _offText);
            }
        }
    }
}
