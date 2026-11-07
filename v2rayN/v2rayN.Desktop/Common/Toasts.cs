using Avalonia.Controls.Notifications;
using Avalonia.Layout;
using v2rayN.Desktop.Converters;

namespace v2rayN.Desktop.Common;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// V2pn toasts: a tinted icon badge (or the server's flag), an optional title and the message.
/// v2rayN sends plain strings, so the kind is guessed from the wording; the "core started"
/// message (a server summary like "[VLESS] (B) Sweden(194***231:443)") becomes a server card.
/// </summary>
internal static class Toasts
{
    private static readonly string[] ErrorWords =
        ["fail", "error", "invalid", "not found", "cannot", "can't", "unable", "失败", "错误", "无效", "ошибк", "не удалось", "не найден", "недейств", "невозможно"];

    private static readonly string[] SuccessWords =
        ["success", "succeeded", "done", "成功", "успешн", "готово", "сохранен"];

    private static readonly string[] WarningWords =
        ["please", "warning", "请", "警告", "внимание", "сначала"];

    public static ToastKind Guess(string message)
    {
        bool Has(string[] words) => words.Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (Has(ErrorWords))
        {
            return ToastKind.Error;
        }
        if (Has(SuccessWords))
        {
            return ToastKind.Success;
        }
        return Has(WarningWords) ? ToastKind.Warning : ToastKind.Info;
    }

    public static NotificationType TypeOf(ToastKind kind) => kind switch
    {
        ToastKind.Success => NotificationType.Success,
        ToastKind.Warning => NotificationType.Warning,
        ToastKind.Error => NotificationType.Error,
        _ => NotificationType.Information,
    };

    public static Control Message(ToastKind kind, string? title, string message)
    {
        var (icon, brush, soft) = kind switch
        {
            ToastKind.Success => ("SemiIconTickCircle", "V2Safe", "V2SafeSoft"),
            ToastKind.Warning => ("SemiIconAlertTriangle", "V2PingMid", "V2PingMidSoft"),
            ToastKind.Error => ("SemiIconAlertCircle", "V2Unsafe", "V2UnsafeSoft"),
            _ => ("SemiIconInfoCircle", "V2ToastInfo", "V2ToastInfoSoft"),
        };
        var glyph = new PathIcon { Width = 16, Height = 16 };
        glyph.Bind(PathIcon.DataProperty, glyph.GetResourceObservable(icon));
        glyph.Bind(PathIcon.ForegroundProperty, glyph.GetResourceObservable(brush));
        var badge = new Border { Classes = { "toastbadge" }, Child = glyph };
        badge.Bind(Border.BackgroundProperty, badge.GetResourceObservable(soft));
        return Layout(badge, title, Trim(message));
    }

    /// <summary>The core started with this server.</summary>
    public static Control Server(ProfileItem item)
    {
        var code = CountryResolver.Resolve(item.Remarks);
        var country = WorldMap.Instance.Get(code);
        var name = CountryResolver.ServerName(item);
        var flag = V2pnConverters.Flag(code);

        Control badge;
        if (flag != null)
        {
            badge = new Border
            {
                Classes = { "toastbadge" },
                Child = new Image { Source = flag, Stretch = Stretch.UniformToFill },
            };
        }
        else
        {
            var glyph = new PathIcon { Width = 16, Height = 16 };
            glyph.Bind(PathIcon.DataProperty, glyph.GetResourceObservable("SemiIconGlobeStroke"));
            glyph.Bind(PathIcon.ForegroundProperty, glyph.GetResourceObservable("V2Safe"));
            var b = new Border { Classes = { "toastbadge" }, Child = glyph };
            b.Bind(Border.BackgroundProperty, b.GetResourceObservable("V2SafeSoft"));
            badge = b;
        }

        var transport = string.Join(" · ", new[] { item.ConfigType.ToString(), item.StreamSecurity }.Where(t => t.IsNotEmpty()));
        var title = country?.Name ?? name;
        var message = country != null ? $"{name} · {transport}" : transport;
        return Layout(badge, $"{L.ToastServerStarted} · {title}", message);
    }

    private static Control Layout(Control badge, string? title, string message)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 300 };
        if (title.IsNotEmpty())
        {
            text.Children.Add(new TextBlock { Classes = { "toasttitle" }, Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
        }
        text.Children.Add(new TextBlock { Classes = { "toastmsg" }, Text = message });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        badge.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(badge);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private static string Trim(string message)
    {
        message = message.Trim();
        return message.Length > 240 ? message[..239] + "…" : message;
    }
}
