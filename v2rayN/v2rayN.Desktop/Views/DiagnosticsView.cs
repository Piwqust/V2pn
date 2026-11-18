using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// "Copy diagnostics": the report for support, shown in full before anything is copied. It is built from an
/// allow list (V2pnDiagnostics), so it carries no links, keys, server addresses or names.
/// </summary>
public sealed class DiagnosticsView : UserControl
{
    public DiagnosticsView(string report)
    {
        Width = 640;

        var close = new Button { Classes = { "ghost" }, Content = L.Close };
        close.Bind(ThemeProperty, close.GetResourceObservable("V2Button"));
        close.Click += (_, _) => DialogHost.Close(null);

        var copied = new TextBlock { Classes = { "srowhint" }, VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Text = L.DiagCopied };
        AutomationProperties.SetLiveSetting(copied, AutomationLiveSetting.Polite);
        var copy = new Button { Classes = { "accent" }, Content = L.DiagCopy };
        copy.Bind(ThemeProperty, copy.GetResourceObservable("V2Button"));
        copy.Click += async (_, _) =>
        {
            await AvaUtils.SetClipboardData(this, report);
            copied.IsVisible = true;
        };

        var text = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, avares://V2pn/Assets/Fonts#Geist Mono, monospace"),
            FontSize = 12,
            MaxHeight = 380,
        };
        AutomationProperties.SetName(text, L.DiagTitle);

        Content = new StackPanel
        {
            Margin = new Thickness(28, 22, 28, 22),
            Spacing = 12,
            Children =
            {
                new TextBlock { Classes = { "spagetitle" }, Text = L.DiagTitle },
                new TextBlock { Classes = { "spagehint" }, Text = L.DiagLead, TextWrapping = TextWrapping.Wrap },
                text,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { copied, close, copy },
                },
            },
        };
    }
}
