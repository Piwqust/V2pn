using Avalonia.Automation;
using Avalonia.Layout;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// V2pn: sites that always go one way, whatever the mode under Connect says (V2pnPresetRules): a bank direct while
/// everything else goes through the server, or one site through the server in "Only blocked". Each change is applied at
/// once; removing a site puts it back under the preset.
/// </summary>
public sealed class SiteRulesView : UserControl
{
    private readonly Config _config;
    private readonly Func<Task> _applied;
    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly TextBox _input;
    private readonly CheckBox _subdomains;
    private readonly TextBlock _problem;

    public SiteRulesView(Config config, Func<Task> applied)
    {
        _config = config;
        _applied = applied;
        Width = 600;

        _input = new TextBox { PlaceholderText = L.SitesInputHint, MinWidth = 260 };
        AutomationProperties.SetName(_input, L.SitesInputHint);
        _subdomains = new CheckBox { Content = L.SitesSubdomains, IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        _problem = new TextBlock { Classes = { "srowhint" }, IsVisible = false, Text = L.SitesNotADomain, Foreground = Brushes.IndianRed };

        var direct = Button(L.SitesDirect, accent: false);
        direct.Click += async (_, _) => await AddAsync(direct: true);
        var proxy = Button(L.SitesProxy, accent: true);
        proxy.Click += async (_, _) => await AddAsync(direct: false);
        _input.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await AddAsync(direct: false);
            }
        };

        var close = Button(L.Close, accent: false);
        close.Classes.Add("ghost");
        close.Click += (_, _) => DialogHost.Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(28, 22, 28, 22),
            Spacing = 12,
            Children =
            {
                new TextBlock { Classes = { "spagetitle" }, Text = L.SitesTitle },
                new TextBlock { Classes = { "spagehint" }, Text = L.SitesLead, TextWrapping = TextWrapping.Wrap },
                _input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { _subdomains, new Control { Width = 8 }, direct, proxy },
                },
                _problem,
                new Border { Classes = { "hairline" } },
                new ScrollViewer { MaxHeight = 300, Content = _list },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { close } },
            },
        };
        Refresh();
    }

    private Button Button(string text, bool accent)
    {
        var button = new Button { Content = text };
        if (accent)
        {
            button.Classes.Add("accent");
        }
        button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
        return button;
    }

    private async Task AddAsync(bool direct)
    {
        var domain = V2pnPresetRules.NormalizeDomain(_input.Text);
        _problem.IsVisible = domain == null;
        if (domain == null)
        {
            return;
        }
        _config.UiItem.V2pnSiteRules = V2pnPresetRules.With(_config.UiItem.V2pnSiteRules,
            new V2pnSiteRule { Domain = domain, Direct = direct, Subdomains = _subdomains.IsChecked == true });
        _input.Text = string.Empty;
        await SaveAsync();
    }

    private async Task RemoveAsync(V2pnSiteRule site)
    {
        _config.UiItem.V2pnSiteRules = (_config.UiItem.V2pnSiteRules ?? []).Where(s => s.Domain != site.Domain).ToList();
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        await ConfigTextHandler.SaveConfig();
        await _applied();
        Refresh();
    }

    private void Refresh()
    {
        _list.Children.Clear();
        var sites = _config.UiItem.V2pnSiteRules ?? [];
        if (sites.Count == 0)
        {
            _list.Children.Add(new TextBlock { Classes = { "srowhint" }, Text = L.SitesNone, TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var site in sites.OrderBy(s => s.Domain, StringComparer.Ordinal))
        {
            var remove = Button("✕", accent: false);
            remove.Classes.Add("ghost");
            AutomationProperties.SetName(remove, string.Format(L.SitesRemove, site.Domain));
            ToolTip.SetTip(remove, string.Format(L.SitesRemove, site.Domain));
            remove.Click += async (_, _) => await RemoveAsync(site);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12 };
            var name = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock { Classes = { "srowtitle" }, Text = Display(site.Domain), TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Classes = { "srowhint" }, Text = site.Subdomains ? L.SitesWithSubdomains : L.SitesExactly },
                },
            };
            var way = new TextBlock
            {
                Classes = { "srowhint" },
                VerticalAlignment = VerticalAlignment.Center,
                Text = site.Direct ? L.SitesDirect : L.SitesProxy,
            };
            Grid.SetColumn(way, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(name);
            row.Children.Add(way);
            row.Children.Add(remove);
            _list.Children.Add(row);
        }
    }

    // Punycode back to the name people know (xn--…рф → госуслуги.рф).
    private static string Display(string domain)
    {
        try
        {
            return new System.Globalization.IdnMapping().GetUnicode(domain);
        }
        catch (ArgumentException)
        {
            return domain;
        }
    }
}
