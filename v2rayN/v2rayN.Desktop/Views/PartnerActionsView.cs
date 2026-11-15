using Avalonia.Automation;
using Avalonia.Layout;
using DialogHostAvalonia;

namespace v2rayN.Desktop.Views;

/// <summary>The glyphs a partner's button may wear (V2pnActionIcons): names of V2pn's own icons, never a provider's picture.</summary>
public static class PartnerActionIcons
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["bolt"] = "SemiIconBolt",
        ["shield"] = "SemiIconShieldStroked",
        ["play"] = "SemiIconPlayCircle",
        ["game"] = "V2pnIconGame",
        ["card"] = "V2pnIconCard",
        ["chat"] = "SemiIconComment",
        ["globe"] = "SemiIconGlobeStroke",
        ["star"] = "SemiIconStarStroked",
        ["refresh"] = "SemiIconRefresh",
        ["check"] = "SemiIconTickCircle",
        ["info"] = "SemiIconInfoCircle",
        ["gift"] = "V2pnIconGift",
    };

    public static Geometry? Of(string? icon) =>
        icon != null && Keys.TryGetValue(icon, out var key) ? Application.Current?.FindResource(key) as Geometry : null;
}

/// <summary>
/// The partner's buttons in plain V2pn, under Connect while a server of the partner's subscription is in use: a row
/// that wraps, so four buttons fit any width down to the mini window. The labels are the partner's; what they do is V2pn's.
/// </summary>
public sealed class PartnerActionsBar : UserControl
{
    private readonly WrapPanel _panel = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        ItemsAlignment = WrapPanelItemsAlignment.Center,
        ItemSpacing = 8,
        LineSpacing = 8,
    };
    private string _key = string.Empty;

    /// <summary>Raised with the action and the button pressed.</summary>
    public event Action<V2pnPartnerAction, Control>? Run;

    public PartnerActionsBar()
    {
        Content = _panel;
        IsVisible = false;
    }

    public void Show(V2pnPartner? partner, string? busyId)
    {
        var actions = partner?.Pack.ActionList ?? [];
        var key = partner == null ? string.Empty : $"{partner.Id}|{busyId}|{string.Join("|", actions.Select(a => a.Id + a.Label + a.Icon))}";
        IsVisible = actions.Count > 0;
        if (key == _key)
        {
            return;
        }
        _key = key;
        _panel.Children.Clear();
        if (partner == null)
        {
            return;
        }
        AutomationProperties.SetName(this, string.Format(L.ActionsOf, partner.Name));
        foreach (var action in actions)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (action.Id == busyId)
            {
                content.Children.Add(new Avalonia.Controls.Shapes.Path { Classes = { "spinner" }, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center });
            }
            else if (PartnerActionIcons.Of(action.Icon) is { } glyph)
            {
                content.Children.Add(new PathIcon { Classes = { "i14" }, Data = glyph });
            }
            content.Children.Add(new TextBlock { Text = action.Label, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Classes = { "small" }, Content = content, IsEnabled = busyId == null || action.Id == busyId };
            button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
            AutomationProperties.SetName(button, action.Label);
            ToolTip.SetTip(button, partner.Name);
            var a = action;
            button.Click += (_, _) => Run?.Invoke(a, button);
            _panel.Children.Add(button);
        }
    }
}

/// <summary>
/// A short message from a partner (info.card) or V2pn's check of the services, in a frame that names who it comes from:
/// nothing a partner sends can pass for a window of V2pn or of the system.
/// </summary>
public sealed class PartnerCardView : UserControl
{
    public PartnerCardView(string from, string? title, Control body)
    {
        MaxWidth = 440;
        MinWidth = 280;
        var close = new Button { Content = L.CardClose, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Bind(ThemeProperty, close.GetResourceObservable("V2Button"));
        close.Click += (_, _) => DialogHost.Close(null);
        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 18), Spacing = 10 };
        stack.Children.Add(new TextBlock { Classes = { "srowhint" }, Text = from, Margin = new Thickness(0) });
        if (title.IsNotEmpty())
        {
            stack.Children.Add(new TextBlock { Classes = { "spagetitle" }, Text = title, TextWrapping = TextWrapping.Wrap });
        }
        stack.Children.Add(body);
        stack.Children.Add(close);
        Content = stack;
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => close.Focus(), DispatcherPriority.Background);
    }

    /// <summary>Plain text of the partner, lines kept, selectable.</summary>
    public static Control Text(string text) => new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxHeight = 320 };
}
