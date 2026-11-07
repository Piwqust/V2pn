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
    public static string LocalOnly => string.Format(T("Proxy on port {0} only", "Только прокси на порту {0}"), AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
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

    public static string WelcomeTitle => T("Add your first server", "Добавьте первый сервер");
    public static string WelcomeText => T("Copy a subscription link or a vless://, vmess://, trojan://… link from your provider and paste it here.",
        "Скопируйте ссылку на подписку или ссылку вида vless://, vmess://, trojan://… от вашего провайдера и вставьте её сюда.");
    public static string WelcomePaste => T("Paste from clipboard", "Вставить из буфера");
    public static string WelcomeQr => T("Scan QR code", "Сканировать QR-код");
    public static string WelcomeHint => T("Subscriptions update on their own; favorites survive the updates.",
        "Подписки обновляются сами, избранное сохраняется после обновлений.");
    public static string ToastServerStarted => T("Server running", "Сервер запущен");
    public static string SubsEmpty => T("A subscription is a link from your provider; V2pn downloads its servers and keeps them up to date.",
        "Подписка — это ссылка от провайдера. V2pn скачивает по ней серверы и сам их обновляет.");
    public static string SubsEmptyAction => T("Paste the link from your provider", "Вставьте ссылку от провайдера");
    public static string SubFresh => T("Updated within a day", "Обновлялась за последние сутки");
    public static string SubStale => T("Not updated for over a day", "Не обновлялась больше суток");
    public static string SubNeverUpdated => T("Never updated", "Ещё не обновлялась");
    public static string Recent => T("Recent", "Недавние");
    public static string RemoveDead => T("Remove servers that didn't respond", "Удалить серверы без ответа");
    public static string RemoveDeadConfirm => T("Remove the servers of “{0}” that didn't respond to the last ping? The next subscription update may bring them back.",
        "Удалить серверы подписки «{0}», которые не ответили на последнюю проверку? Следующее обновление подписки может вернуть их.");
    public static string RemoveDeadNone => T("Ping the servers first: nothing has failed a check yet.", "Сначала проверьте пинг: пока ни один сервер не провалил проверку.");

    // Menus (⋮, +, settings tile) and context menus.
    public static string MenuGrpView => T("Open", "Открыть");
    public static string MenuMore => T("Current subscription", "Текущая подписка");
    public static string MenuQuit => T("Quit V2pn", "Выйти из V2pn");
    public static string MenuGrpImport => T("Import", "Импорт");
    public static string MenuScanScreen => T("Scan QR code on screen", "QR-код с экрана");
    public static string MenuScanImage => T("QR code from an image", "QR-код из картинки");
    public static string MenuGrpManual => T("Create manually", "Создать вручную");
    public static string MenuServerByProtocol => T("Server", "Сервер");
    public static string MenuOptions => T("Settings", "Параметры");
    public static string MenuOptionsDesc => T("Modes, ports, cores, updates", "Режимы, порты, ядра, обновления");
    public static string MenuRouting => T("Routing rules", "Правила маршрутизации");
    public static string MenuRoutingDesc => T("Rule sets: domains, IPs, apps", "Наборы правил: домены, IP, программы");
    public static string MenuDnsDesc => T("DNS servers and their rules", "DNS-серверы и их правила");
    public static string MenuHotkeys => T("Hotkeys", "Горячие клавиши");
    public static string MenuHotkeysDesc => T("Show the window, switch the proxy", "Показ окна, переключение прокси");
    public static string MenuTemplate => T("Config template", "Шаблон конфигурации");
    public static string MenuTemplateDesc => T("Raw core config, for experts", "Конфиг ядра целиком, для опытных");
    public static string MenuPolicyGroup => T("Server group", "Группа серверов");
    public static string MenuProxyChain => T("Proxy chain", "Цепочка прокси");
    public static string MenuCustomConfig => T("Custom config", "Своя конфигурация");
    public static string MenuCustomOutbound => T("Custom outbound", "Свой outbound");
    public static string MenuPasteDesc => T("A subscription or vless://, vmess://… links", "Подписка или ссылки vless://, vmess://…");
    public static string MenuSubDesc => T("A link that updates the server list", "Ссылка, по которой обновляется список");
    public static string MenuServerDesc => T("Enter the parameters by hand", "Ввести параметры вручную");
    public static string MenuGroupDesc => T("Picks the fastest of several servers", "Выбирает самый быстрый из нескольких");
    public static string MenuChainDesc => T("Traffic goes through servers in turn", "Трафик идёт через серверы по очереди");
    public static string MenuGrpSystem => T("System tools", "Система");
    public static string MenuRestartAdmin => T("Restart as administrator", "Перезапустить от администратора");
    public static string MenuUwp => T("Allow Microsoft Store apps", "Пустить приложения Microsoft Store");
    public static string MenuUwpDesc => T("Loopback exemption for UWP apps", "Исключение loopback для UWP");
    public static string MenuRegion => T("Region", "Регион");
    public static string MenuRegionDesc => T("Geo files, rules and DNS for a country", "Геофайлы, правила и DNS для страны");
    public static string MenuGrpData => T("Data", "Данные");
    public static string MenuBackup => T("Backup and restore", "Резервная копия");
    public static string MenuOpenFolder => T("Open data folder", "Открыть папку с данными");
    public static string MenuClearStats => T("Clear traffic statistics", "Сбросить статистику трафика");

    // Connection mode and routing presets (the bar under Connect).
    public static string ModeProxy => T("Proxy", "Прокси");
    public static string ModeVpn => T("VPN", "VPN");
    public static string AdminShort => T("Admin", "Админ");
    public static string ModeProxyTip => T("Browsers and apps that follow the system proxy settings go through the server. No administrator rights needed.",
        "Через сервер идут браузеры и программы, которые используют системный прокси. Права администратора не нужны.");
    public static string ModeVpnTip => T("All traffic of the computer goes through a virtual network adapter (TUN), including games and apps that ignore the proxy.",
        "Весь трафик компьютера идёт через виртуальный сетевой адаптер (TUN), в том числе игры и программы, которые не видят прокси.");
    public static string ModeVpnNeedsAdmin => T("Needs administrator rights.", "Нужны права администратора.");
    public static string VpnRestartAsAdmin => T("VPN mode needs administrator rights. Restart V2pn as administrator? VPN mode turns on after the restart.",
        "Для режима VPN нужны права администратора. Перезапустить V2pn от имени администратора? Режим VPN включится после перезапуска.");
    public static string VpnNeedsSudo => T("VPN mode needs the administrator password.", "Для режима VPN нужен пароль администратора.");
    public static string ModeLocalPort => T("Local port only", "Только локальный порт");

    public static string RouteTitle => T("What goes through the VPN", "Что идёт через VPN");
    public static string RouteSubtitle => T("Sites that go direct see your real IP address. Changes apply right away.",
        "Сайты, которые идут напрямую, видят ваш настоящий IP. Изменения применяются сразу.");
    public static string RouteRussiaDirect => T("Russian sites direct", "Российские сайты напрямую");
    public static string RouteRussiaDirectHint => T("Sites in .ru and .рф, banks, Gosuslugi and Russian IPs open directly. Everything else goes through the VPN, including blocked .ru sites.",
        "Сайты в зонах .ru и .рф, банки, Госуслуги и российские IP открываются напрямую. Остальное идёт через VPN, включая заблокированные сайты в .ru.");
    public static string RouteBlockedOnly => T("Only blocked sites", "Только заблокированное");
    public static string RouteBlockedOnlyHint => T("Only sites blocked in Russia, YouTube, Discord, Telegram and AI services go through the VPN. The rest opens directly and faster.",
        "Через VPN идут только сайты, заблокированные в России, а ещё YouTube, Discord, Telegram и ИИ-сервисы. Остальное открывается напрямую и быстрее.");
    public static string RouteAll => T("Everything through VPN", "Всё через VPN");
    public static string RouteAllHint => T("All traffic except the local network goes through the server. Some Russian banks and Gosuslugi may refuse a foreign IP.",
        "Весь трафик, кроме локальной сети, идёт через сервер. Некоторые банки и Госуслуги могут не пустить с иностранного IP.");
    public static string Recommended => T("Recommended", "Рекомендуем");
    public static string BlockAds => T("Block ads and trackers", "Блокировать рекламу и трекеры");
    public static string BlockAdsHint => T("By the category-ads-all list, in the three modes above.", "По списку category-ads-all, в трёх режимах выше.");
    public static string RouteCustom => T("Your rules", "Свои правила");
    public static string RouteCustomHint => T("Sets from the routing settings.", "Наборы из настройки маршрутов.");
    public static string RouteCustomPick => T("Choose…", "Выбрать…");
    public static string RouteDownloading => T("Downloading the rule lists for Russia…", "Скачиваю списки правил для России…");
    public static string RouteApplying => T("Applying…", "Применяю…");
    public static string RouteFailed => T("Couldn't switch the routing. Details are in the log.", "Не удалось переключить маршруты. Подробности в журнале.");
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

    public static string SubEditHint => T("Where the servers come from and how they are processed.", "Откуда берутся серверы и как они обрабатываются.");
    public static string ServerEditHint => T("Connection details of the server. The fields depend on the protocol.", "Параметры подключения к серверу. Набор полей зависит от протокола.");
    public static string HotkeysHint => T("Click a field and press the key combination; it applies after a restart.", "Нажмите на поле и сочетание клавиш; заработает после перезапуска.");

    public static string GrpSource => T("Source", "Источник");
    public static string GrpUpdates => T("Updates", "Обновление");
    public static string GrpProcessing => T("Processing", "Обработка");
    public static string GrpChain => T("Proxy chain", "Цепочка прокси");
    public static string GrpOther => T("Other", "Прочее");
    public static string HintMoreUrl => T("More links merged into this subscription, one per line.", "Дополнительные ссылки, которые объединяются с этой подпиской, по одной в строке.");
    public static string HintSubEnabled => T("Turned-off subscriptions are skipped by “Update all”.", "Выключенные подписки пропускаются при «Обновить все».");
    public static string HintSubInterval => T("Minutes between automatic updates; 0 turns them off.", "Минут между автообновлениями; 0 — не обновлять.");
    public static string HintSubFilter => T("Regular expression; only servers whose names match are kept.", "Регулярное выражение; останутся только серверы с подходящими именами.");
    public static string HintConvertTarget => T("Convert the subscription through the conversion service first.", "Сначала перевести подписку через сервис конвертации.");
    public static string HintPreResolve => T("Resolve server domains to IP addresses before connecting.", "Заранее превращать домены серверов в IP-адреса.");
    public static string HintPrevProfile => T("Traffic goes through this server before the subscription server.", "Трафик сначала идёт через этот сервер, потом через сервер подписки.");
    public static string HintNextProfile => T("Traffic leaves through this server after the subscription server.", "Трафик выходит через этот сервер после сервера подписки.");
    public static string HintChainNote => T("Applies to every server of this subscription.", "Действует для всех серверов этой подписки.");

    // Settings window
    public static string SearchSettings => T("Search settings", "Поиск по настройкам");
    public static string SetGeneral => T("General", "Общие");
    public static string SetGeneralHint => T("Startup, the window and the server list.", "Запуск, окно и список серверов.");
    public static string SetInbound => T("Local proxy", "Локальный прокси");
    public static string SetInboundHint => T("The port that apps and the system connect to.", "Порт, к которому подключаются приложения и система.");
    public static string SetSysProxy => T("Proxy mode", "Режим «Прокси»");
    public static string SetSysProxyHint => T("What Connect does in Proxy mode: how the operating system hands its traffic to V2pn.", "Что делает кнопка подключения в режиме «Прокси»: как система передаёт трафик в V2pn.");
    public static string SetTun => T("VPN mode (TUN)", "Режим VPN (TUN)");
    public static string SetTunHint => T("Sends all traffic of the device through a virtual network adapter, including apps that ignore the system proxy.", "Весь трафик устройства идёт через виртуальный сетевой адаптер, даже у приложений, которые не смотрят на системный прокси.");
    public static string SetTraffic => T("Traffic handling", "Обработка трафика");
    public static string SetTrafficHint => T("Sniffing, fragmentation, multiplexing and outbound connections.", "Сниффинг, фрагментация, мультиплексирование и исходящие соединения.");
    public static string SetTests => T("Testing", "Проверки");
    public static string SetTestsHint => T("How ping, speed and connection checks run.", "Как работают пинг, тест скорости и проверка соединения.");
    public static string SetUpdates => T("Updates and sources", "Обновления и источники");
    public static string SetUpdatesHint => T("Where geo files, rule sets and subscription conversion come from.", "Откуда берутся геофайлы, наборы правил и конвертация подписок.");
    public static string SetCores => T("Cores", "Ядра");
    public static string SetCoresHint => T("Which core runs each protocol.", "Какое ядро обслуживает каждый протокол.");
    public static string SetLogs => T("Logs", "Журнал");
    public static string SetLogsHint => T("What the core records and where.", "Что и куда записывает ядро.");
    public static string GrpStartup => T("Startup", "Запуск");
    public static string GrpInterface => T("Interface", "Интерфейс");
    public static string GrpServerList => T("Server list", "Список серверов");
    public static string GrpPort => T("Port", "Порт");
    public static string GrpLan => T("Local network", "Локальная сеть");
    public static string GrpProxyRules => T("Rules", "Правила");
    public static string GrpProxyMethod => T("On connect", "При подключении");
    public static string ProxyMethod => T("Turn on", "Что включать");
    public static string ProxyMethodHint => T("With “Don't change system settings” only apps you point at the local port go through the server.",
        "При «Не менять настройки системы» через сервер пойдут только программы, которым вы сами укажете локальный порт.");
    public static string ProxyMethodSystem => T("System proxy", "Системный прокси");
    public static string ProxyMethodPac => T("PAC script", "PAC-скрипт");
    public static string GrpTunAccess => T("Access", "Доступ");
    public static string TunAdminTitle => T("Administrator rights", "Права администратора");
    public static string TunAdminYes => T("Granted, VPN mode works.", "Есть, режим VPN работает.");
    public static string TunAdminNo => T("Not granted. When you turn on VPN mode, V2pn offers to restart as administrator.",
        "Нет. При включении режима VPN V2pn предложит перезапуститься от имени администратора.");
    public static string GrpExceptions => T("Exceptions", "Исключения");
    public static string GrpFiles => T("Custom files", "Свои файлы");
    public static string GrpRouting => T("Routing", "Маршрутизация");
    public static string GrpStack => T("Network stack", "Сетевой стек");
    public static string GrpSniffing => T("Sniffing", "Сниффинг");
    public static string GrpFragment => T("Fragmentation", "Фрагментация");
    public static string GrpMux => T("Multiplexing", "Мультиплексирование");
    public static string GrpOutbound => T("Outbound connections", "Исходящие соединения");
    public static string GrpTestRun => T("Test run", "Запуск проверок");
    public static string GrpTestTargets => T("Test addresses", "Адреса для проверок");
    public static string GrpGeo => T("Geo files and rule sets", "Геофайлы и наборы правил");
    public static string GrpNetwork => T("Network requests", "Сетевые запросы");
    public static string ProxyExceptions => T("Addresses that bypass the proxy", "Адреса в обход прокси");
    public static string HintAutoHide => T("Start in the tray without opening the window.", "Запускаться в трее, не открывая окно.");
    public static string HintGridOrientation => T("Layout of the server table page.", "Расположение на странице таблицы серверов.");
    public static string HintKeepOlder => T("When a subscription brings duplicates, keep the server you already had.", "Если подписка приносит дубликаты, оставлять сервер, который уже был.");
    public static string HintSecondPort => T("Opens one more port next to the main one.", "Открывает ещё один порт рядом с основным.");
    public static string HintUdp => T("Lets apps send UDP through the proxy: games, calls, QUIC.", "Пропускает через прокси UDP: игры, звонки, QUIC.");
    public static string HintAllowLan => T("Other devices in your network can use this proxy.", "Другие устройства в вашей сети смогут пользоваться этим прокси.");
    public static string HintNewPortLan => T("Gives devices in the network their own port with a login and password.", "Для устройств сети отдельный порт с логином и паролем.");
    public static string HintNotProxyLocal => T("Intranet addresses go directly, not through V2pn.", "Адреса локальной сети идут напрямую, минуя V2pn.");
    public static string HintPacPath => T("Use your own PAC file instead of the generated one.", "Свой PAC-файл вместо созданного автоматически.");
    public static string HintScriptPath => T("A script that turns the system proxy on and off.", "Скрипт, который включает и выключает системный прокси.");
    public static string HintAutoRoute => T("Adds routes so that all traffic enters the TUN adapter.", "Добавляет маршруты, чтобы весь трафик шёл в TUN-адаптер.");
    public static string HintStrictRoute => T("Blocks traffic that tries to go around the adapter.", "Не пускает трафик в обход адаптера.");
    public static string HintStack => T("gvisor is the most compatible, system is the fastest.", "gvisor совместимее всего, system быстрее всего.");
    public static string HintSniffing => T("Reads the domain from each connection so routing rules can match it.", "Определяет домен соединения, чтобы к нему сработали правила маршрутизации.");
    public static string HintRouteOnly => T("Use the detected domain for routing only and keep connecting by IP.", "Использовать домен только для маршрутизации, а подключаться по IP.");
    public static string HintFragment => T("Splits the TLS handshake so DPI cannot read it. Needed only when a server is blocked.", "Дробит TLS-рукопожатие, чтобы его не прочитал DPI. Нужно, только если сервер блокируют.");
    public static string HintMux => T("Several connections share one tunnel to the server.", "Несколько соединений идут по одному туннелю к серверу.");
    public static string HintFingerprint => T("Which browser the TLS handshake imitates.", "Какой браузер изображает TLS-рукопожатие.");
    public static string HintHysteria => T("Your real channel speed in Mbit/s; leave empty to let the protocol decide.", "Реальная скорость канала в Мбит/с; пусто — протокол решит сам.");
    public static string HintCacheFile => T("Keeps downloaded rule sets between launches.", "Сохраняет скачанные наборы правил между запусками.");
    public static string HintGeoInterval => T("0 turns automatic updates off.", "0 — не обновлять автоматически.");
    public static string HintSubConvert => T("Service that converts subscriptions in other formats.", "Сервис, который переводит подписки из других форматов.");
    public static string HintLogToFile => T("Saves the core log to files.", "Сохранять журнал ядра в файлы.");

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
