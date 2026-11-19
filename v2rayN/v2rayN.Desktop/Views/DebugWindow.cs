using Avalonia.Layout;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// Every scenario of the partners' tabs (and a few of the app's own) at a click, for trying them without a provider, a
/// broken site or a week of waiting: a made-up partner the person "came from" until V2pn closes (so it gets a tab; for a
/// person who already came from a partner, by a marker or as tools/sandbox.ps1 -Partner records it, it stands in for that
/// partner), any subscription counted as its, V2pn's tab or the
/// partner's, with or without its background, the partner leaving, light and dark, every window size, what its panel
/// says laid over, links, the walkthrough and the notices. tools/edition-matrix.ps1 drives it. Development builds and
/// developer mode only. Stays open beside the window; the window shows the result at once.
/// </summary>
public sealed class DebugWindow : Window
{
    private readonly MainWindow _main;
    private readonly TextBlock _status = new() { FontFamily = new FontFamily("Consolas, avares://V2pn/Assets/Fonts#Geist Mono, monospace"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly List<(Button Button, Func<bool> IsOn)> _marks = [];
    private readonly ComboBox _partners = new() { MinWidth = 220 };
    private readonly ComboBox _subs = new() { MinWidth = 220 };
    private readonly TextBox _testName = new() { Text = "Тест-провайдер", Width = 170 };
    private readonly TextBox _testAccent = new() { Text = "#3B82F6", Width = 100 };
    // A folder with a partner.json (brand, theme) and its pictures; V2PN_DEBUG_THEME fills it for scripted checks.
    private readonly TextBox _themeFolder = new() { Text = Environment.GetEnvironmentVariable("V2PN_DEBUG_THEME") ?? string.Empty, Watermark = "папка с partner.json и картинками", MinWidth = 300 };
    private readonly TextBox _link = new() { Watermark = "v2pn://add?url=…", MinWidth = 360 };
    private readonly DispatcherTimer _timer;
    private string _partnersKey = string.Empty;
    // The partner the list selects at its next refresh (the test partner, once added).
    private string? _pick;

    public DebugWindow(MainWindow main)
    {
        _main = main;
        Title = "Отладка V2pn";
        Width = 640;
        Height = 860;
        MinWidth = 460;
        MinHeight = 400;
        Background = Application.Current?.FindResource("V2DialogBackground") as IBrush;
        OffscreenMode.Attach(this);

        var body = new StackPanel { Margin = new Thickness(20, 16, 20, 24), Spacing = 16 };
        body.Children.Add(Card("Сейчас", _status));
        body.Children.Add(PartnersSection());
        body.Children.Add(LookSection());
        body.Children.Add(DataSection());
        body.Children.Add(LinksSection());
        body.Children.Add(AppSection());
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        Editions.Changed += Refresh;
        DebugOverrides.Changed += Refresh;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) => Refresh());
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            Editions.Changed -= Refresh;
            DebugOverrides.Changed -= Refresh;
        };
        Refresh();
    }

    // ------------------------------------------------------------------ sections

    private Control PartnersSection()
    {
        var add = Action("Добавить тестового", () =>
        {
            var accent = Color.TryParse(_testAccent.Text?.Trim(), out _) ? _testAccent.Text!.Trim().ToUpperInvariant() : null;
            var folder = _themeFolder.Text?.Trim();
            var name = (_testName.Text ?? "Тест-провайдер").Trim();
            string? themeId = null;
            // A theme folder's own name, so the shots show the provider they are of.
            if (Directory.Exists(folder) && File.Exists(Path.Combine(folder, "partner.json")))
            {
                try
                {
                    var pack = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "partner.json")));
                    if (pack?["name"]?.GetValue<string>() is { Length: > 0 } own)
                    {
                        name = own;
                    }
                    themeId = pack?["id"]?.GetValue<string>();
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or IOException)
                {
                    Logging.SaveLog("V2pn debug theme", ex);
                }
            }
            // The partner the person came from when there is one (tools/sandbox.ps1 -Partner records it), else the theme's
            // id: the tab is the one the person would have.
            var id = V2pnEdition.Instance.Marker ?? V2pnEdition.Instance.CameFrom.FirstOrDefault(c => !V2pnPartners.Instance.IsDebug(c))
                ?? (V2pnPartnerTrust.ValidId(themeId ?? string.Empty) ? themeId! : "test-provider");
            if (V2pnPartners.Instance.AddDebugPartner(id, name, accent, Directory.Exists(folder) ? folder : null) == null)
            {
                _main.DebugToast(ToastKind.Warning, "Тестовый партнёр есть только в сборке разработчика или в режиме разработчика.");
                return;
            }
            // The buttons below act on it next.
            _pick = id;
            _partnersKey = string.Empty;
            Refresh();
        });
        var link = Action("Считать подпиской партнёра", () =>
        {
            if (_subs.SelectedItem is SubItem sub && Selected() is { } partner)
            {
                V2pnPartners.Instance.SetDebugSubscription(sub.Id, partner.Id);
            }
        });
        var unlink = Action("Как есть", () =>
        {
            if (_subs.SelectedItem is SubItem sub)
            {
                V2pnPartners.Instance.SetDebugSubscription(sub.Id, null);
            }
        });
        _subs.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SubItem>((s, _) => new TextBlock { Text = s == null ? "" : $"{ViewModels.SideSubRow.NameOf(s)}{(V2pnPartners.Instance.DebugPartnerOf(s.Id) is { } p ? $"  → {p}" : "")}" });
        _partners.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<V2pnPartner>((p, _) => new TextBlock { Text = p == null ? "" : $"{p.Name} ({p.Id}{(V2pnPartners.Instance.IsDebug(p.Id) ? ", тестовый" : "")})" });

        return Card("Партнёры",
            Row("Тестовый партнёр с вкладкой (без API, только в памяти; с меткой — вместо партнёра метки)", _testName, _testAccent, add),
            Row("Тема тестового партнёра (необязательно)", _themeFolder),
            Row("Партнёр для действий ниже", _partners),
            Row("Подписка", _subs, link, unlink),
            Row("Партнёр ушёл",
                Action("подписки нет", () => Gone(V2pnPartnerGone.Unlinked)),
                Action("срок истёк", () => Gone(V2pnPartnerGone.Expired)),
                Action("отозван", () => Gone(V2pnPartnerGone.Revoked))),
            Row("Проверки",
                Action("Спросить список партнёров", () => _ = V2pnPartners.Instance.RefreshAsync(forceIndex: true)),
                Action("Обновить подписки (и пакеты)", _main.DebugUpdateSubscriptions),
                Action("Спросить API сейчас", _main.DebugRefreshApi)));
    }

    private Control LookSection()
    {
        // The debug window speaks Russian whatever the app's language, so tools/edition-matrix.ps1 finds its buttons.
        return Card("Вид",
            Row("Вкладка в заголовке (как щелчок по ней)",
                Mark(Action("вкладка V2pn", () => Editions.Open(null)), () => Editions.Partner == null),
                Mark(Action("вкладка партнёра", ShowSelected), () => Editions.Partner != null)),
            Row("Фон партнёра (Сцена, если он есть в теме)",
                Mark(Action("как в теме", () => DebugOverrides.SetNoStage(false)), () => !DebugOverrides.NoStage),
                Mark(Action("без фона", () => DebugOverrides.SetNoStage(true)), () => DebugOverrides.NoStage)),
            Row("Режим V2pn",
                Mark(Action("Светлый", () => _main.DebugSetMode(light: true)), () => _main.ActualThemeVariant == ThemeVariant.Light),
                Mark(Action("Тёмный", () => _main.DebugSetMode(light: false)), () => _main.ActualThemeVariant == ThemeVariant.Dark),
                Action("Страница «Оформление»", _main.DebugOpenAppearance)),
            Row("Окно",
                Action("Мини 380×640", () => _main.DebugResize(380, 640)),
                Action("Узкое 560×760", () => _main.DebugResize(560, 760)),
                Action("Среднее 820×700", () => _main.DebugResize(820, 700)),
                Action("Широкое 1280×800", () => _main.DebugResize(1280, 800)),
                Action("Низкое 1100×560", () => _main.DebugResize(1100, 560))),
            Row("Анимация",
                Mark(Action("Системная", () => SetMotion("system")), () => Appearance.Current.Motion == "system"),
                Mark(Action("Вкл", () => SetMotion("on")), () => Appearance.Current.Motion == "on"),
                Mark(Action("Выкл (затухание)", () => SetMotion("off")), () => Appearance.Current.Motion == "off")));
    }

    private Control DataSection()
    {
        Control[] Options(Func<string> current, Action<string> set, params (string Id, string Label)[] options) =>
            options.Select(o => (Control)Mark(Action(o.Label, () => set(o.Id)), () => current() == o.Id)).ToArray();

        return Card("Данные партнёра (поверх настоящих, до перезапуска)",
            Row("Подписка", Options(() => DebugOverrides.Account, DebugOverrides.SetAccount,
                ("real", "как есть"), ("panel", "только панель"), ("full", "всё (API)"), ("warn", "заканчивается"), ("bad", "закончилась"),
                ("incident", "сбой"), ("major", "серьёзный сбой"), ("empty", "ничего"))),
            Row(null, Action("Сбросить всё", () =>
            {
                DebugOverrides.Reset();
            })));
    }

    private Control LinksSection()
    {
        var demo = Uri.EscapeDataString("http://127.0.0.1:32620/s/demo-token");
        var family = Uri.EscapeDataString("http://127.0.0.1:32620/s/family-token");
        void Fill(string text) => _link.Text = text;
        return Card("Ссылки (как клик по ссылке на сайте)",
            Row(null, _link, Action("Открыть", () => _main.DebugOpenLink(_link.Text ?? string.Empty))),
            Row("Примеры (демо Configra на 127.0.0.1:32620)",
                Action("v2pn: демо", () => Fill($"v2pn://add?url={demo}&name=Configra")),
                Action("v2pn: семейная", () => Fill($"v2pn://add?url={family}&name=Configra%20Family")),
                Action("happ", () => Fill("happ://add/http://127.0.0.1:32620/s/demo-token")),
                Action("v2raytun", () => Fill($"v2raytun://import/{demo}")),
                Action("hiddify", () => Fill($"hiddify://import/{demo}")),
                Action("не ссылка", () => Fill("v2pn://add?url=ftp://nowhere"))));
    }

    private Control AppSection() => Card("Приложение",
        Row("Мастер первого запуска", Action("Запустить с начала", _main.DebugStartOnboarding)),
        Row("Уведомления",
            Action("инфо", () => _main.DebugToast(ToastKind.Info, "Проверка: обычное уведомление.")),
            Action("успех", () => _main.DebugToast(ToastKind.Success, "Проверка: всё получилось.")),
            Action("внимание", () => _main.DebugToast(ToastKind.Warning, "Проверка: на это стоит посмотреть.")),
            Action("ошибка", () => _main.DebugToast(ToastKind.Error, "Проверка: что-то сломалось."))),
        Row("Окна", Action("Карточка IP", _main.DebugOpenIp), Action("Оформление", _main.DebugOpenAppearance),
            Action("Обход блокировок", _main.OpenBypass),
            Action("Наборы правил", () => _main.ViewModel?.RoutingSettingCmd.Execute().Subscribe()),
            // Partners whose pack says "login" (tools/demo/demo_partner.py answers login/start and login/poll).
            Action("Вход через провайдера", () => _main.OpenProviderLogin())),
        // The windows V2pn took over from v2rayN, one click each, for scripted checks of their layout.
        Row("Окна v2rayN", Run("Подписки", vm => vm.SubSettingCmd), Run("DNS", vm => vm.DNSSettingCmd),
            Run("Шаблон конфига", vm => vm.FullConfigTemplateCmd), Run("Горячие клавиши", vm => vm.GlobalHotkeySettingCmd),
            Run("Настройки", vm => vm.OptionSettingCmd)),
        Row("Серверы v2rayN", Run("VLESS", vm => vm.AddVlessServerCmd), Run("Shadowsocks", vm => vm.AddShadowsocksServerCmd),
            Run("WireGuard", vm => vm.AddWireguardServerCmd), Run("Свой конфиг", vm => vm.AddCustomServerCmd),
            Run("Группа", vm => vm.AddPolicyGroupServerCmd), Run("Цепочка", vm => vm.AddProxyChainServerCmd),
            Action("Выбор сервера", () => _ = AppManager.Instance.WindowDialog.ShowDialogAsync(new ProfilesSelectViewModel()))));

    private Control Run(string text, Func<MainWindowViewModel, ReactiveCommand<RxVoid, RxVoid>> command) =>
        Action(text, () =>
        {
            if (_main.ViewModel is { } vm)
            {
                command(vm).Execute().Subscribe();
            }
        });

    // ------------------------------------------------------------------ actions

    private V2pnPartner? Selected() => _partners.SelectedItem as V2pnPartner ?? Editions.Partner ?? Editions.Tabs.FirstOrDefault();

    private const string NoTab = "Вкладки партнёра нет: добавьте тестового или дождитесь пакета партнёра, от которого пришёл человек.";

    private void ShowSelected()
    {
        if (Selected() is not { } partner)
        {
            _main.DebugToast(ToastKind.Warning, NoTab);
            return;
        }
        Editions.Open(partner.Id);
    }

    private void Gone(V2pnPartnerGone why)
    {
        if (Selected() is not { } partner)
        {
            _main.DebugToast(ToastKind.Warning, NoTab);
            return;
        }
        if (V2pnPartners.Instance.IsDebug(partner.Id))
        {
            // A made-up partner goes for real, from its open tab; it is added again with one click.
            Editions.Open(partner.Id);
            V2pnPartners.Instance.RemoveDebugPartner(partner.Id, why);
        }
        else
        {
            // A real one only leaves the window as it would; its files stay, so the next start brings it back.
            Editions.DebugClose(partner, why);
        }
    }

    private static void SetMotion(string motion)
    {
        var next = Appearance.Current.Clone();
        next.Motion = motion;
        Appearance.Set(next);
        if (Application.Current is { } app)
        {
            Motion.Refresh(app);
        }
    }

    private void Refresh()
    {
        var partners = Editions.Tabs.ToList();
        var key = string.Join("|", partners.Select(p => p.Id));
        if (key != _partnersKey)
        {
            _partnersKey = key;
            var keep = (_partners.SelectedItem as V2pnPartner)?.Id;
            _partners.ItemsSource = partners;
            _partners.SelectedItem = partners.FirstOrDefault(p => p.Id == (_pick ?? keep)) ?? Editions.Partner ?? partners.FirstOrDefault();
            _pick = null;
        }
        var subs = _main.DebugSubscriptions();
        var keepSub = (_subs.SelectedItem as SubItem)?.Id;
        if (_subs.ItemsSource is not IReadOnlyList<SubItem> shown || shown.Count != subs.Count || !shown.Zip(subs).All(z => z.First.Id == z.Second.Id))
        {
            _subs.ItemsSource = subs;
            _subs.SelectedItem = subs.FirstOrDefault(s => s.Id == keepSub) ?? subs.FirstOrDefault();
        }

        var lines = new List<string>
        {
            $"сборка: {(V2pnInfo.IsRelease ? "релиз, режим разработчика" : "разработчика")} · V2pn {V2pnInfo.Version} · метка старой сборки: {(V2pnEdition.Instance.Marker is { } marker ? $"{marker} (из {V2pnEdition.FileName})" : "нет")}",
            $"пришёл от: {string.Join(", ", V2pnEdition.Instance.CameFrom).IfEmpty("никого")} · вход по коду: {string.Join(", ", V2pnEdition.Instance.SignedIn).IfEmpty("нет")}",
            $"вкладки: V2pn{string.Concat(partners.Select(p => $", {p.Name}"))} · открыта: {Editions.Partner?.Name ?? "V2pn"}",
        };
        if (Editions.Look != null && Editions.Theme is { } theme)
        {
            var stage = Editions.Stage(_main.ActualThemeVariant != ThemeVariant.Light);
            lines.Add($"вид: {(stage == null ? "Акцент" : "Сцена")} · акцент {Editions.PartnerAccent} · бренд {string.Join(" ", theme.Brand)} · фон {theme.Background.Kind}"
                + (stage == null ? string.Empty : $" · текст {stage.Ink}{(stage.Scrim != null ? $" · затемнение {stage.Scrim}" : string.Empty)}"));
            foreach (var note in theme.Notes)
            {
                lines.Add("  " + note);
            }
        }
        foreach (var partner in partners)
        {
            var linked = subs.Where(s => V2pnPartners.Instance.ForSubscription(s)?.Id == partner.Id).Select(s => ViewModels.SideSubRow.NameOf(s));
            lines.Add($"партнёр {partner.Id}{(V2pnPartners.Instance.IsDebug(partner.Id) ? " (тестовый)" : "")}: до {partner.Listing.Until:yyyy-MM-dd}, права {string.Join(",", partner.Listing.Grants)}, API {(partner.Pack.ApiUrl ?? "нет")}, подписки: {string.Join(", ", linked).IfEmpty("нет")}");
        }
        if (partners.Count == 0)
        {
            lines.Add(V2pnEdition.Instance.Any ? "пакета партнёра ещё нет: добавьте тестового или дождитесь списка партнёров" : "вкладок нет: человек не пришёл ни от одного партнёра");
        }
        var index = V2pnPartners.Instance.Index;
        lines.Add(index == null
            ? $"список партнёров: нет{(V2pnPartners.Instance.IndexError is { } why ? $" ({why})" : string.Empty)}"
            : $"список партнёров: {string.Join(", ", index.Partners.Select(p => p.Id)).IfEmpty("пустой")}{(V2pnPartners.Instance.IndexError is { } err ? $" · последний запрос не удался: {err}" : string.Empty)}");
        lines.Add(_main.DebugPlanState());
        lines.Add(_main.DebugApiState());
        lines.Add(DebugOverrides.Any ? "поверх данных: включено (раздел ниже)" : "поверх данных: ничего");
        _status.Text = string.Join("\n", lines);

        foreach (var (button, isOn) in _marks)
        {
            button.Classes.Set("accent", isOn());
        }
    }

    // ------------------------------------------------------------------ building blocks

    private static Border Card(string title, params Control[] rows)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold });
        foreach (var row in rows)
        {
            stack.Children.Add(row);
        }
        var card = new Border { Padding = new Thickness(16, 14), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = stack };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("V2CardBackground"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("V2Border"));
        return card;
    }

    private static Control Row(string? title, params Control[] controls)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 8, 8);
            control.VerticalAlignment = VerticalAlignment.Center;
            wrap.Children.Add(control);
        }
        if (title == null)
        {
            return wrap;
        }
        return new StackPanel
        {
            Spacing = 4,
            Children = { new TextBlock { Text = title, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap }, wrap },
        };
    }

    private static Button Action(string label, Action act)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 6) };
        button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
        button.Click += (_, _) =>
        {
            try
            {
                act();
            }
            catch (Exception ex)
            {
                Logging.SaveLog("V2pn debug", ex);
            }
        };
        return button;
    }

    /// <summary>A button that lights up while its option is the one in effect.</summary>
    private Button Mark(Button button, Func<bool> isOn)
    {
        _marks.Add((button, isOn));
        return button;
    }
}

internal static class DebugExtensions
{
    public static string IfEmpty(this string text, string fallback) => text.Length == 0 ? fallback : text;
}
