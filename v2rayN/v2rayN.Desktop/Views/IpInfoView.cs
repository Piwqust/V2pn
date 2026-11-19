using Avalonia.Automation;
using Avalonia.Layout;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// "My IP" from the statistics card: the address sites see, where it is, whose network it is in,
/// whether databases take it for a VPN, and the person's own address beside it to show it is hidden.
/// </summary>
public sealed class IpInfoView : UserControl
{
    private readonly ContentControl _body = new();
    private readonly TextBlock _hint = new() { Classes = { "spagehint" } };
    private readonly Button _refresh;
    private CancellationTokenSource? _lookup;

    public IpInfoView()
    {
        Width = 600;

        var close = IconButton("SemiIconClose", L.Close);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, _) => DialogHost.Close(null);

        var header = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(28, 22, 18, 4) };
        header.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children = { new TextBlock { Classes = { "spagetitle" }, Text = L.IpTitle }, _hint },
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        _refresh = TextButton(L.IpRefresh, "SemiIconRefresh");
        _refresh.Click += (_, _) => _ = LoadAsync();
        var browser = TextButton(L.IpOpenBrowser, "SemiIconExternalOpenStroked");
        ToolTip.SetTip(browser, L.IpOpenBrowserTip);
        browser.Click += (_, _) => ProcUtils.ProcessStart(V2pnIpLookup.BrowserUrl);
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(28, 12, 28, 22),
            Children = { _refresh, browser },
        };

        var scroll = new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(28, 6, 28, 0), Child = _body },
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var root = new DockPanel { MaxHeight = 720 };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(scroll);
        Content = root;

        AttachedToVisualTree += (_, _) => _ = LoadAsync();
        // Closing the card drops a lookup still under way; its answer has nowhere to go.
        DetachedFromVisualTree += (_, _) => _lookup?.Cancel();
    }

    private async Task LoadAsync()
    {
        _lookup?.Cancel();
        var cts = _lookup = new CancellationTokenSource();
        _refresh.IsEnabled = false;
        _hint.Text = string.Empty;
        _body.Content = Loading();
        try
        {
            // In VPN mode a request "past V2pn" still enters the tunnel and would show the VPN address
            // as the person's own.
            var askDirect = !ConfigTextHandler.Config.TunModeItem.EnableTun;
            var report = await Task.Run(() => V2pnIpLookup.LookUpAsync(askDirect, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }
            _hint.Text = report.Exit != null ? L.IpHintVia : L.IpHintOff;
            _body.Content = report.Main == null ? Failed() : Result(report);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(IpInfoView), ex);
            _body.Content = Failed();
        }
        finally
        {
            if (_lookup == cts)
            {
                _refresh.IsEnabled = true;
            }
        }
    }

    private static Control Loading() => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        Margin = new Thickness(0, 28, 0, 28),
        HorizontalAlignment = HorizontalAlignment.Center,
        Children =
        {
            new Avalonia.Controls.Shapes.Path { Classes = { "spinner" }, Width = 16, Height = 16 },
            new TextBlock { Classes = { "muted" }, Text = L.IpLoading, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    private Control Failed()
    {
        var retry = TextButton(L.IpRetry, "SemiIconRefresh");
        retry.HorizontalAlignment = HorizontalAlignment.Center;
        retry.Click += (_, _) => _ = LoadAsync();
        return new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 24, 0, 12),
            Children =
            {
                new TextBlock { Text = L.IpFailed, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                retry,
            },
        };
    }

    private Control Result(V2pnIpLookup.Report report)
    {
        var main = report.Main!;
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(Hero(main));

        var notes = new StackPanel { Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
        if (report.Exit != null && report.Direct != null)
        {
            notes.Children.Add(report.Leaks
                ? Note(false, "SemiIconAlertTriangle", L.IpLeak, L.IpLeakHint)
                : Note(true, "SemiIconShield", L.IpHidden, null));
        }
        if (report.ExitReputation is { } rep && rep.Proxy != null)
        {
            var kind = L.IpKindName(rep.Kind);
            var risk = rep.Risk is { } r ? string.Format(L.IpRisk, r) : null;
            notes.Children.Add(rep.Proxy == true
                ? Note(false, "SemiIconInfoCircle", Join(L.IpSeenAsVpn, risk), string.Format(L.IpSeenAsVpnHint, kind))
                : Note(true, "SemiIconTickCircle", Join(L.IpSeenAsHome, risk), null));
        }
        if (notes.Children.Count > 0)
        {
            panel.Children.Add(notes);
        }

        panel.Children.Add(Group(L.IpGrpPlace,
            Row(L.IpCountry, CountryText(main)),
            Row(L.IpRegion, main.Region),
            Row(L.IpCity, main.City),
            Row(L.IpPostal, main.Postal),
            CoordinatesRow(main),
            Row(L.IpTimeZone, ZoneText(main))));

        var kindText = report.Exit != null && report.ExitReputation?.Kind is { } k ? L.IpKindName(k) : null;
        panel.Children.Add(Group(L.IpGrpNetwork,
            Row(L.IpIsp, main.Isp),
            Row(L.IpOrg, main.AsnOrg != main.Isp ? main.AsnOrg : null),
            Row(L.IpAsn, main.Asn is { } asn ? $"AS{asn}" : null),
            Row(L.IpDomain, main.Domain),
            Row(L.IpKind, kindText),
            Row(L.IpVersion, main.Version)));

        if (report.Exit != null)
        {
            if (report.Direct is { } direct && !report.Leaks)
            {
                panel.Children.Add(Group(L.IpGrpDirect,
                    Row(L.IpAddress, direct.Ip, copy: true),
                    Row(L.IpCountry, Join(CountryText(direct), direct.City, " · ")),
                    Row(L.IpIsp, direct.Isp ?? direct.AsnOrg)));
            }
            else if (!report.DirectAsked)
            {
                panel.Children.Add(Footnote(L.IpDirectTunNote));
            }
            else if (report.Direct == null)
            {
                panel.Children.Add(Footnote(L.IpDirectFailed));
            }
        }

        var sources = new List<string> { main.Source };
        if (report.ExitReputation != null)
        {
            sources.Add("proxycheck.io");
        }
        panel.Children.Add(Footnote(string.Format(L.IpSources, string.Join(", ", sources))));
        return panel;
    }

    private Control Hero(V2pnIpLookup.Details d)
    {
        var ip = new SelectableTextBlock
        {
            Text = d.Ip,
            FontSize = d.Ip.Length > 24 ? 20 : 28,
            FontWeight = FontWeight.SemiBold,
            FontFeatures = [FontFeature.Parse("+tnum")],
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var copy = CopyButton(d.Ip);
        var line = new Grid { ColumnDefinitions = new("*,Auto") };
        line.Children.Add(ip);
        Grid.SetColumn(copy, 1);
        line.Children.Add(copy);

        var where = Join(Join(CountryText(d), d.City, " · "), d.Isp, " · ");
        return new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                line,
                new TextBlock { Classes = { "muted" }, Text = where ?? string.Empty, TextWrapping = TextWrapping.Wrap },
            },
        };
    }

    private Button CopyButton(string text)
    {
        var button = IconButton("SemiIconCopy", L.IpCopy);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Click += async (_, _) =>
        {
            await AvaUtils.SetClipboardData(this, text);
            ToolTip.SetTip(button, L.IpCopied);
            ToolTip.SetIsOpen(button, true);
            await Task.Delay(1200);
            ToolTip.SetIsOpen(button, false);
            ToolTip.SetTip(button, L.IpCopy);
        };
        return button;
    }

    private static Control Note(bool good, string icon, string title, string? hint)
    {
        var chipIcon = new PathIcon { Classes = { "i12" } };
        chipIcon.Bind(PathIcon.DataProperty, chipIcon.GetResourceObservable(icon));
        var chip = new Border
        {
            Classes = { "check", good ? "ok" : "fail" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { chipIcon, new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        if (hint == null)
        {
            return chip;
        }
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                chip,
                new TextBlock { Classes = { "srowhint" }, Text = hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 0) },
            },
        };
    }

    private static Control Group(string title, params Control?[] rows)
    {
        var list = new StackPanel();
        foreach (var row in rows.OfType<Control>())
        {
            list.Children.Add(row);
        }
        return new StackPanel
        {
            Classes = { "sgroup" },
            IsVisible = list.Children.Count > 0,
            Children =
            {
                new TextBlock { Classes = { "sgrouplabel" }, Text = title },
                new Border { Classes = { "sgroupcard" }, Child = list },
            },
        };
    }

    // A row only for a fact the service gave: an empty "Postal code —" row says nothing.
    private Control? Row(string title, string? value, bool copy = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var text = new SelectableTextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right,
            FontFeatures = [FontFeature.Parse("+tnum")],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Control control = text;
        if (copy)
        {
            control = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { text, CopyButton(value) },
            };
        }
        return RowOf(title, control);
    }

    private static Control RowOf(string title, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new("Auto,*") };
        // A fact sheet, not a settings page: muted labels and tighter rows, so the whole card fits one screen.
        var label = new TextBlock { Classes = { "srowtitle" }, Text = title, FontWeight = FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("V2Muted"));
        grid.Children.Add(label);
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(16, 0, 0, 0);
        AutomationProperties.SetName(control, title);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return new Border { Classes = { "srow" }, Child = grid, MinHeight = 38, Padding = new Thickness(16, 8) };
    }

    private static Control? CoordinatesRow(V2pnIpLookup.Details d)
    {
        if (d.Latitude is not { } lat || d.Longitude is not { } lon)
        {
            return null;
        }
        var text = string.Create(CultureInfo.InvariantCulture, $"{lat:0.####}, {lon:0.####}");
        var icon = new PathIcon { Classes = { "i12" } };
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable("SemiIconExternalOpenStroked"));
        var link = new Button
        {
            Classes = { "ghost", "tiny" },
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }, icon },
            },
        };
        link.Bind(ThemeProperty, link.GetResourceObservable("V2Button"));
        ToolTip.SetTip(link, L.IpCoordinatesTip);
        link.Click += (_, _) => ProcUtils.ProcessStart(string.Create(CultureInfo.InvariantCulture,
            $"https://www.openstreetmap.org/?mlat={lat}&mlon={lon}#map=10/{lat}/{lon}"));
        var row = RowOf(L.IpCoordinates, link);
        // The button's own padding would push its text off the column the other values end on.
        link.Margin = new Thickness(16, -4, -8, -4);
        return row;
    }

    private static string? CountryText(V2pnIpLookup.Details d)
    {
        var code = d.CountryCode?.ToUpperInvariant();
        var name = code != null ? WorldMap.Instance.Get(code)?.Name : null;
        name ??= d.Country;
        return name != null && code != null ? $"{name} ({code})" : name ?? code;
    }

    private static string? ZoneText(V2pnIpLookup.Details d)
    {
        if (d.TimeZone == null && d.UtcOffset == null)
        {
            return null;
        }
        string? now = null;
        if (d.TimeZone != null)
        {
            try
            {
                var local = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, TimeZoneInfo.FindSystemTimeZoneById(d.TimeZone));
                now = string.Format(L.IpTimeNow, local.ToString("HH:mm", CultureInfo.CurrentCulture));
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }
        var utc = d.UtcOffset != null ? $"UTC{d.UtcOffset}" : null;
        return Join(Join(d.TimeZone, utc, " · "), now, " · ");
    }

    private static Control Footnote(string text) => new TextBlock
    {
        Classes = { "srowhint" },
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 14, 0, 0),
    };

    private static string Join(string first, string? second) => second == null ? first : $"{first} · {second}";

    private static string? Join(string? first, string? second, string separator) =>
        first == null ? second : second == null ? first : first + separator + second;

    private static Button IconButton(string icon, string name)
    {
        var path = new PathIcon { Classes = { "i14" } };
        path.Bind(PathIcon.DataProperty, path.GetResourceObservable(icon));
        var button = new Button { Classes = { "ghost", "icon" }, Content = path };
        button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static Button TextButton(string text, string icon)
    {
        var path = new PathIcon { Classes = { "i12" } };
        path.Bind(PathIcon.DataProperty, path.GetResourceObservable(icon));
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { path, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
        return button;
    }
}
