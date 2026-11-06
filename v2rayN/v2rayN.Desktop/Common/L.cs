namespace v2rayN.Desktop;

/// <summary>
/// Strings introduced by the V2pn interface: Russian inline, the other v2rayN languages in
/// <see cref="LTranslations"/>, English as the fallback. Everything inherited from v2rayN keeps using
/// <see cref="ResUI"/>.
/// </summary>
public static class L
{
    private static bool Ru => CultureInfo.CurrentUICulture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase);

    private static string T(string en, string ru) => Ru ? ru : LTranslations.Get(CultureInfo.CurrentUICulture, en) ?? en;

    public static string AppName => "V2pn";

    public static string NavServers => T("Servers", "Серверы");
    public static string NavLog => T("Log", "Журнал");
    public static string NavProxies => T("Proxies", "Прокси");
    public static string NavConnections => T("Connections", "Соединения");

    public static string Library => T("Library", "Библиотека");
    public static string AllServers => T("All servers", "Все серверы");
    public static string Favorites => T("Favorites", "Избранное");
    public static string Subscriptions => T("Subscriptions", "Подписки");
    public static string AddSubscription => T("Add subscription", "Добавить подписку");
    public static string SubscriptionActions => T("Subscription actions", "Действия с подписками");
    public static string Settings => T("Settings", "Настройки");
    public static string Appearance => T("Appearance", "Оформление");

    public static string Servers => T("servers", "серв.");
    public static string Updated => T("updated", "обновлено");
    public static string NeverUpdated => T("never updated", "ещё не обновлялась");
    public static string AutoUpdateEvery => T("auto-update every {0} min", "автообновление каждые {0} мин");
    public static string LocalServers => T("Servers you added by hand and from every subscription.", "Серверы, добавленные вручную, и серверы из всех подписок.");
    public static string FavoritesHint => T("Star a server to keep it here. Favorites survive subscription updates.", "Отметьте сервер звёздочкой, чтобы он появился здесь. Избранное сохраняется после обновления подписок.");

    public static string Update => T("Update", "Обновить");
    public static string TestPing => T("Test ping", "Проверить пинг");
    public static string MoreTests => T("More tests", "Другие проверки");
    public static string StopTests => T("Stop tests", "Остановить проверку");
    public static string SortByPing => T("Sort by ping", "Сортировать по пингу");
    public static string AddServer => T("Add", "Добавить");
    public static string Search => T("Search by name or address", "Поиск по имени или адресу");

    public static string ColName => T("Name", "Название");
    public static string ColProtocol => T("Protocol", "Протокол");
    public static string ColSubscription => T("Subscription", "Подписка");
    public static string ColPing => T("Ping", "Пинг");

    public static string Active => T("Active", "Активен");
    public static string Timeout => T("timeout", "нет ответа");
    public static string Favorite => T("Add to favorites", "В избранное");
    public static string Unfavorite => T("Remove from favorites", "Убрать из избранного");
    public static string ToggleFavorite => T("Favorite / unfavorite", "В избранное / убрать");
    public static string Ping => T("Ping", "Пинг");
    public static string More => T("More", "Ещё");

    public static string EmptyTitle => T("Nothing here yet.", "Пока пусто.");
    public static string EmptyHint => T("Add a subscription link or paste a server from the clipboard.", "Добавьте ссылку на подписку или вставьте сервер из буфера обмена.");
    public static string EmptyFavoritesTitle => T("No favorites yet.", "В избранном пусто.");
    public static string PasteFromClipboard => T("Paste from clipboard", "Вставить из буфера");

    public static string Connection => T("Connection", "Подключение");
    public static string Connect => T("Connect", "Подключиться");
    public static string Connected => T("Connected", "Подключено");
    public static string SystemProxy => T("System proxy", "Системный прокси");
    public static string ProxyOff => T("Off", "Выкл");
    public static string ProxyOn => T("On", "Вкл");
    public static string ProxyKeep => T("Keep", "Не трогать");
    public static string ProxyPac => T("PAC", "PAC");
    public static string Routing => T("Routing", "Маршрутизация");
    public static string TunMode => T("TUN mode", "Режим TUN");
    public static string TunHint => T("Route all system traffic", "Весь трафик системы");
    public static string Traffic => T("Traffic", "Трафик");
    public static string Inbound => T("Local ports", "Локальные порты");
    public static string CheckAvailability => T("Click to check that the connection works", "Нажмите, чтобы проверить, что подключение работает");

    public static string Selected => T("Selected server", "Выбранный сервер");
    public static string NothingSelected => T("Select a server to see its details.", "Выберите сервер, чтобы увидеть подробности.");
    public static string Share => T("Share", "Поделиться");
    public static string Edit => T("Edit", "Изменить");
    public static string CopyLink => T("Copy link", "Копировать ссылку");
    public static string Today => T("Today", "Сегодня");
    public static string Total => T("Total", "Всего");

    public static string NewVersion => T("New version available", "Доступна новая версия");
    public static string Help => T("Help", "Помощь");
    public static string Exit => T("Exit", "Выход");

    // Proton-style shell
    public static string TabFavorites => T("Favorites", "Избранное");
    public static string TabCountries => T("Countries", "Страны");
    public static string TabSubscriptions => T("Subscriptions", "Подписки");
    public static string SearchServers => T("Search", "Поиск");
    public static string SearchResults => T("Results", "Найдено");
    public static string Fastest => T("Fastest server", "Самый быстрый сервер");
    public static string FastestNone => T("Run a ping test to find it", "Проверьте пинг, чтобы найти");
    public static string NoCountry => T("Unknown location", "Страна не определена");
    public static string Back => T("Back", "Назад");
    public static string UpdateAll => T("Update all subscriptions", "Обновить все подписки");
    public static string ServersCount => T("servers", "серверов");
    public static string FavoritesEmpty => T("Hover a server and click ☆ to keep it here. Favorites survive subscription updates.", "Наведите на сервер и нажмите ☆, чтобы закрепить его здесь. Избранное сохраняется после обновления подписок.");
    public static string NothingFound => T("Nothing found", "Ничего не найдено");
    public static string SubEmpty => T("No servers yet. Update the subscription to load them.", "Серверов пока нет. Обновите подписку, чтобы загрузить их.");
    public static string Protected => T("Protected", "Защищено");
    public static string Unprotected => T("Unprotected", "Не защищено");
    public static string LocalOnly => T("Local proxy only", "Только локальный прокси");
    public static string Disconnect => T("Disconnect", "Отключиться");
    public static string ServerIp => T("Server", "Сервер");
    public static string Protocol => T("Protocol", "Протокол");
    public static string SessionTraffic => T("Session traffic", "Трафик за сессию");
    public static string CurrentSpeed => T("Current speed", "Текущая скорость");
    public static string NoServer => T("No server selected", "Сервер не выбран");
    public static string PickServer => T("Pick a server on the left", "Выберите сервер слева");
    public static string RailRouting => T("Routing", "Маршруты");
    public static string RailTun => T("TUN mode", "Режим TUN");
    public static string RailProxy => T("System proxy", "Сист. прокси");
    public static string RailDns => T("DNS", "DNS");
    public static string RailSettings => T("Settings", "Настройки");
    public static string ProxyModeOff => T("Off — don't route system traffic", "Выключен — система не идёт через VPN");
    public static string ProxyModeOn => T("On — route system traffic", "Включён — система идёт через VPN");
    public static string ProxyModeKeep => T("Keep current OS setting", "Не менять настройки системы");
    public static string ProxyModePac => T("PAC — automatic per site", "PAC — автоматически по сайтам");
    public static string RoutingSettings => T("Routing settings…", "Настроить маршруты…");
    public static string MenuLog => T("Log", "Журнал");
    public static string MenuTable => T("Server table", "Таблица серверов");
    public static string MenuClashProxies => T("Clash proxies", "Прокси Clash");
    public static string MenuClashConnections => T("Clash connections", "Соединения Clash");
    public static string AddMenu => T("Add server or subscription", "Добавить сервер или подписку");
    public static string Remove => T("Delete", "Удалить");
    public static string TestSpeed => T("Speed test", "Тест скорости");
    public static string Europe => T("Europe", "Европа");
    public static string Seconds => T("s", "с");

    public static string PingProgress => T("Stop · {0}/{1}", "Стоп · {0}/{1}");
    public static string TabServers => T("Servers", "Серверы");
    public static string ConnectFastestIn => T("Connect to the fastest server here", "Подключиться к самому быстрому здесь");
    public static string UpdateSubscription => T("Update subscription", "Обновить подписку");
    public static string ClearSearch => T("Clear search", "Очистить поиск");
    public static string AppMenu => T("Menu", "Меню");

    public static string ThemeName(string? name) => name switch
    {
        nameof(ETheme.FollowSystem) => T("System", "Как в системе"),
        nameof(ETheme.Dark) => T("Dark", "Тёмная"),
        nameof(ETheme.Light) => T("Light", "Светлая"),
        _ => name ?? string.Empty,
    };
    public static string CheckConnection => T("Check connection", "Проверить соединение");
    public static string Checking => T("Checking…", "Проверяю…");
    public static string CheckWorks => T("Works · {0} ms", "Работает · {0} мс");

    /// <summary>"85 ms", with the unit Russian readers expect.</summary>
    public static string Ms(int value) => Ru ? $"{value} мс" : $"{value} ms";
    public static string CheckFailed => T("No response", "Нет ответа");
    public static string NoTraffic => T("No traffic yet", "Трафика пока нет");
    public static string StateChecking => T("Checking connection…", "Проверяю соединение…");
    public static string StateNoResponse => T("Server not responding", "Сервер не отвечает");
    public static string ClickToConnect => T("Click to connect · right-click for more", "Нажмите, чтобы подключиться · правая кнопка — ещё");

    /// <summary>"6 servers" with the plural form the language needs.</summary>
    public static string ServerCount(int n)
    {
        if (Ru)
        {
            var (m10, m100) = (n % 10, n % 100);
            var word = m10 == 1 && m100 != 11 ? "сервер"
                : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? "сервера"
                : "серверов";
            return $"{n} {word}";
        }
        if (CultureInfo.CurrentUICulture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return n == 1 ? "1 server" : $"{n} servers";
        }
        return $"{n} {ServersCount}";
    }

    // Relative times and durations, used through Fmt.
    public static string JustNow => T("just now", "только что");
    public static string MinutesAgo => T("{0} min ago", "{0} мин назад");
    public static string HoursAgo => T("{0} h ago", "{0} ч назад");
    public static string SubUpdatedAgo => T("updated {0}", "обновлена {0}");
    public static string DurationSec => T("{0} sec", "{0} сек");
    public static string DurationMinSec => T("{0} min {1} sec", "{0} мин {1} сек");
}
