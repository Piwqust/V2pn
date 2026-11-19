using Avalonia.Automation;
using Avalonia.Layout;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// "Обход блокировок": the network check on open, what it found in one sentence, and the actions that fit it:
/// find a way for the server, go through a server in Russia, build a chain through the person's own one. Below,
/// what in the selected server's settings makes it easy to block.
/// </summary>
public sealed class BypassView : UserControl
{
    private readonly MainWindowViewModel _vm;
    private readonly Func<ProfileItem, string?> _country;
    private readonly Func<List<ProfileItem>, CancellationToken, Task<ProfileItem?>> _fastest;
    private readonly Func<string, Task> _select;

    private readonly StackPanel _body = new() { Spacing = 12 };
    private readonly TextBlock _status = new() { Classes = { "srowhint" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _retry;
    private CancellationTokenSource? _work;

    public BypassView(MainWindowViewModel vm, Func<ProfileItem, string?> country,
        Func<List<ProfileItem>, CancellationToken, Task<ProfileItem?>> fastest, Func<string, Task> select)
    {
        _vm = vm;
        _country = country;
        _fastest = fastest;
        _select = select;
        Width = 620;
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        var close = IconButton("SemiIconClose", L.Close);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, _) => DialogHost.Close(null);
        var header = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(28, 22, 18, 4) };
        header.Children.Add(new TextBlock { Classes = { "spagetitle" }, Text = L.BypassTitle });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        _retry = TextButton(L.BypassRetry, "SemiIconRefresh");
        _retry.Click += (_, _) => _ = CheckAsync();
        var footer = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(28, 12, 28, 22),
            Children = { _status, _retry },
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

        AttachedToVisualTree += (_, _) => _ = CheckAsync();
        DetachedFromVisualTree += (_, _) => _work?.Cancel();
    }

    private async Task CheckAsync()
    {
        _work?.Cancel();
        var cts = _work = new CancellationTokenSource();
        _retry.IsEnabled = false;
        var steps = new TextBlock { Classes = { "muted" }, Text = L.BypassChecking, VerticalAlignment = VerticalAlignment.Center };
        _body.Children.Clear();
        _body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 24, 0, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { new Avalonia.Controls.Shapes.Path { Classes = { "spinner" }, Width = 16, Height = 16 }, steps },
        });
        try
        {
            var progress = new Progress<string>(step => steps.Text = $"{L.BypassChecking} {StepName(step)}");
            var report = await _vm.CheckNetworkAsync(progress, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }
            await ShowAsync(report);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(BypassView), ex);
            _body.Children.Clear();
            _body.Children.Add(new TextBlock { Text = L.VerdictUnknownText, TextWrapping = TextWrapping.Wrap });
        }
        finally
        {
            if (_work == cts)
            {
                _retry.IsEnabled = true;
            }
        }
    }

    private async Task ShowAsync(V2pnBlockReport report)
    {
        var server = await ConfigHandler.GetDefaultServer(AppManager.Instance.Config);
        var connected = report.ProxySmall != null;
        _body.Children.Clear();

        var (title, text) = Verdict(report, connected);
        var verdict = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Classes = { "srowtitle" }, Text = title, FontSize = 16, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            },
        };
        if (report.QuicWorks == false)
        {
            verdict.Children.Add(new TextBlock { Classes = { "srowhint" }, Text = L.BypassQuicCut, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        }
        _body.Children.Add(new Border { Classes = { "sgroupcard" }, Padding = new Thickness(16, 12), Child = verdict });

        if (await _vm.BypassWayForServerAsync() is { } way)
        {
            _body.Children.Add(new TextBlock { Classes = { "srowhint" }, Text = string.Format(L.BypassWayNow, L.BypassWayName(way.Id)), TextWrapping = TextWrapping.Wrap });
        }
        _body.Children.Add(Actions(report, server, connected));

        var rows = new StackPanel();
        if (report.DirectChecked)
        {
            rows.Children.Add(Row(L.BypassStepRussian, report.RussianSites));
            rows.Children.Add(Row(L.BypassStepForeign, report.ForeignSites));
            rows.Children.Add(Row(L.BypassStepServer, report.ServerReachable));
            rows.Children.Add(Row(L.BypassStepQuic, report.QuicWorks));
        }
        if (connected)
        {
            rows.Children.Add(Row(L.BypassStepSmall, report.ProxySmall));
            rows.Children.Add(Row(L.BypassStepLarge, report.ProxyLarge,
                report.ProxyLarge == false && report.LargeBytes > 0 ? string.Format(L.BypassFrozenAt, report.LargeBytes / 1024) : null));
        }
        _body.Children.Add(new Border { Classes = { "sgroupcard" }, Child = rows });
        if (!report.DirectChecked)
        {
            _body.Children.Add(Footnote(L.BypassTunNote));
        }
        else if (!connected)
        {
            _body.Children.Add(Footnote(L.BypassOffNote));
        }

        if (server != null)
        {
            _body.Children.Add(new TextBlock { Classes = { "sgrouplabel" }, Text = L.GrpBypassServer, Margin = new Thickness(0, 8, 0, 0) });
            var advice = V2pnServerAdvice.Assess(server);
            var list = new StackPanel { Spacing = 6 };
            foreach (var item in advice)
            {
                list.Children.Add(new TextBlock { Text = "• " + AdviceText(item, server), TextWrapping = TextWrapping.Wrap });
            }
            if (advice.Count == 0)
            {
                list.Children.Add(new TextBlock { Classes = { "muted" }, Text = L.AdviceNone, TextWrapping = TextWrapping.Wrap });
            }
            _body.Children.Add(list);
        }
    }

    private Control Actions(V2pnBlockReport report, ProfileItem? server, bool connected)
    {
        var panel = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        var core = server != null ? AppManager.Instance.GetCoreType(server, server.ConfigType) : ECoreType.Xray;
        var canTune = connected && server != null && V2pnBypass.On(AppManager.Instance.Config)
                      && V2pnBypass.CandidatesFor(server, core).Count > 0;
        // Splitting the handshake cannot reach an address that does not answer, or one a whitelist keeps out.
        if (canTune && report.Verdict is not (EV2pnBlockVerdict.NoInternet or EV2pnBlockVerdict.Whitelist or EV2pnBlockVerdict.ServerBlocked))
        {
            var tune = TextButton(L.BypassTune, "SemiIconBolt", accent: report.Verdict is EV2pnBlockVerdict.Freeze or EV2pnBlockVerdict.ProxyCut);
            tune.Click += async (_, _) => await TuneAsync(tune);
            panel.Children.Add(tune);
        }
        if (report.Verdict is EV2pnBlockVerdict.Whitelist or EV2pnBlockVerdict.ServerBlocked)
        {
            var russian = TextButton(L.BypassUseRussian, "SemiIconRoute", accent: report.Verdict == EV2pnBlockVerdict.Whitelist);
            russian.Click += async (_, _) => await UseRussianAsync(russian);
            panel.Children.Add(russian);
        }
        if (server != null && !server.ConfigType.IsGroupType())
        {
            var chain = TextButton(L.BypassMakeChain, "SemiIconChainStroked");
            chain.Click += async (_, _) => await ChainAsync(server);
            panel.Children.Add(chain);
        }
        if (server != null && V2pnBypass.Learned(AppManager.Instance.Config, server.IndexId, V2pnBypass.NetworkKey(AppManager.Instance.Config)) != null)
        {
            var forget = TextButton(L.BypassForgetServer, "SemiIconDeleteStroked");
            forget.Click += async (_, _) =>
            {
                forget.IsEnabled = false;
                await _vm.ForgetBypassForServerAsync();
                await CheckAsync();
            };
            panel.Children.Add(forget);
        }
        return panel;
    }

    private async Task TuneAsync(Button button)
    {
        if (_vm.BypassTuning)
        {
            _vm.CancelBypassTune();
            return;
        }
        SetStatus(L.BypassChecking);
        var idle = button.Content;
        button.Content = L.BypassTuneStop;
        using var progress = _vm.WhenAnyValue(v => v.BypassTune).Subscribe(p =>
        {
            if (p != null)
            {
                SetStatus(string.Format(L.BypassTuning, L.BypassWayName(p.Variant?.Id), p.Index, p.Count));
            }
        });
        var outcome = await _vm.TuneBypassAsync(manual: true);
        SetStatus(outcome switch
        {
            null => L.BypassNothingToTry,
            { Worked: true, Variant: { } v } => string.Format(L.BypassFound, L.BypassWayName(v.Id)),
            { Worked: true } => L.BypassFoundNone,
            { Cancelled: true } => null,
            _ => L.BypassNotFound,
        });
        if (outcome?.Worked == true)
        {
            await CheckAsync();
        }
        else
        {
            button.Content = idle;
        }
    }

    private async Task UseRussianAsync(Button button)
    {
        button.IsEnabled = false;
        try
        {
            var current = AppManager.Instance.Config.IndexId;
            var all = await AppManager.Instance.ProfileItems(string.Empty) ?? [];
            var entries = all.Where(p => p.IndexId != current && !p.ConfigType.IsGroupType() && V2pnRussianEntry.LooksLikeEntry(p, _country(p))).ToList();
            if (entries.Count == 0)
            {
                SetStatus(L.BypassRussianNoneListed);
                return;
            }
            SetStatus(L.BypassRussianTesting);
            _work?.Cancel();
            var cts = _work = new CancellationTokenSource();
            var best = await _fastest(entries, cts.Token);
            if (best == null)
            {
                SetStatus(L.BypassRussianNoneAnswered);
                return;
            }
            await _select(best.IndexId);
            SetStatus(string.Format(L.BypassRussianConnected, CountryResolver.ServerName(best)));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>The chain form in place of the results: the person's server in Russia, then the selected one.</summary>
    private async Task ChainAsync(ProfileItem exit)
    {
        var all = await AppManager.Instance.ProfileItems(string.Empty) ?? [];
        var choices = all.Where(p => p.IndexId != exit.IndexId && !p.ConfigType.IsGroupType())
            .OrderByDescending(p => V2pnRussianEntry.LooksLikeEntry(p, _country(p)))
            .ThenBy(p => p.Subid.IsNotEmpty())
            .ToList();
        if (choices.Count == 0)
        {
            SetStatus(L.BypassChainNoServers);
            return;
        }
        var exitName = CountryResolver.ServerName(exit);
        var picker = new ComboBox
        {
            ItemsSource = choices.Select(CountryResolver.ServerName).ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(picker, L.BypassChainPick);
        var create = TextButton(L.BypassChainCreate, "SemiIconChainStroked", accent: true);
        var back = TextButton(L.BypassRetry, "SemiIconRefresh");
        back.Click += (_, _) => _ = CheckAsync();
        create.Click += async (_, _) =>
        {
            if (picker.SelectedIndex < 0)
            {
                return;
            }
            create.IsEnabled = false;
            var entry = choices[picker.SelectedIndex];
            var name = $"🇷🇺 → {exitName}";
            var id = await V2pnRussianEntry.CreateChainAsync(AppManager.Instance.Config, entry, exit, name);
            if (id == null)
            {
                SetStatus(L.BypassChainFailed);
                create.IsEnabled = true;
                return;
            }
            await _select(id);
            SetStatus(string.Format(L.BypassChainMade, name));
        };
        _body.Children.Clear();
        _body.Children.Add(new TextBlock { Text = string.Format(L.BypassChainLead, exitName), TextWrapping = TextWrapping.Wrap });
        _body.Children.Add(new TextBlock { Classes = { "sgrouplabel" }, Text = L.BypassChainPick });
        _body.Children.Add(picker);
        _body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { create, back } });
    }

    private void SetStatus(string? text)
    {
        _status.Text = text;
        _status.IsVisible = text.IsNotEmpty();
    }

    private static (string Title, string Text) Verdict(V2pnBlockReport report, bool connected) => report.Verdict switch
    {
        EV2pnBlockVerdict.Fine => (L.VerdictFine, connected ? L.VerdictFineText : L.VerdictFineOffText),
        EV2pnBlockVerdict.NoInternet => (L.VerdictNoInternet, L.VerdictNoInternetText),
        EV2pnBlockVerdict.Whitelist => (L.VerdictWhitelist, L.VerdictWhitelistText),
        EV2pnBlockVerdict.ServerBlocked => (L.VerdictServerBlocked, L.VerdictServerBlockedText),
        EV2pnBlockVerdict.Freeze => (L.VerdictFreeze, string.Format(L.VerdictFreezeText, Math.Max(1, report.LargeBytes / 1024))),
        EV2pnBlockVerdict.ProxyCut => (L.VerdictProxyCut, L.VerdictProxyCutText),
        _ => (L.VerdictUnknown, L.VerdictUnknownText),
    };

    /// <summary>The verdict as one line, for a notice outside this window.</summary>
    public static string? Headline(V2pnBlockReport report) => report.Verdict switch
    {
        EV2pnBlockVerdict.NoInternet => L.VerdictNoInternet,
        EV2pnBlockVerdict.Whitelist => L.VerdictWhitelist,
        EV2pnBlockVerdict.ServerBlocked => L.VerdictServerBlocked,
        _ => null,
    };

    private static string AdviceText(EV2pnAdvice advice, ProfileItem server) => advice switch
    {
        EV2pnAdvice.RealityRawTcp => L.AdviceRealityRawTcp,
        EV2pnAdvice.BigBrandSni => string.Format(L.AdviceBigBrandSni, server.Sni),
        EV2pnAdvice.NonStandardPort => string.Format(L.AdviceNonStandardPort, server.Port),
        EV2pnAdvice.NoTls => L.AdviceNoTls,
        EV2pnAdvice.KnownProtocol => L.AdviceKnownProtocol,
        EV2pnAdvice.Udp => L.AdviceUdp,
        EV2pnAdvice.Ech => L.AdviceEch,
        _ => advice.ToString(),
    };

    private static string StepName(string step) => step switch
    {
        "russian" => L.BypassStepRussian,
        "foreign" => L.BypassStepForeign,
        "server" => L.BypassStepServer,
        "quic" => L.BypassStepQuic,
        "proxy" => L.BypassStepSmall,
        "large" => L.BypassStepLarge,
        _ => string.Empty,
    };

    private static Control Row(string title, bool? ok, string? detail = null)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto") };
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(label);
        var value = new TextBlock
        {
            Text = detail ?? (ok switch { true => L.BypassOk, false => L.BypassFail, null => L.BypassNotChecked }),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };
        value.Bind(TextBlock.ForegroundProperty, value.GetResourceObservable(ok switch
        {
            true => "V2Safe",
            false => "V2Unsafe",
            null => "V2Muted",
        }));
        AutomationProperties.SetName(value, title);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return new Border { Classes = { "srow" }, Child = grid, MinHeight = 38, Padding = new Thickness(16, 8) };
    }

    private static Control Footnote(string text) => new TextBlock
    {
        Classes = { "srowhint" },
        Text = text,
        TextWrapping = TextWrapping.Wrap,
    };

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

    private static Button TextButton(string text, string icon, bool accent = false)
    {
        var path = new PathIcon { Classes = { "i12" } };
        path.Bind(PathIcon.DataProperty, path.GetResourceObservable(icon));
        var button = new Button
        {
            Classes = { accent ? "accent" : "ghost" },
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { path, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
        AutomationProperties.SetName(button, text);
        return button;
    }
}
