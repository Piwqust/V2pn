namespace v2rayN.Desktop;

/// <summary>
/// Strings introduced by the V2pn interface: Russian inline, the other v2rayN languages in
/// <see cref="LTranslations"/>, English as the fallback. Everything inherited from v2rayN keeps using
/// <see cref="ResUI"/>.
/// </summary>
public static partial class L
{
    private static bool Ru => CultureInfo.CurrentUICulture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase);

    private static string T(string en, string ru) => Ru ? ru : LTranslations.Get(CultureInfo.CurrentUICulture, en) ?? en;

    // A reworded string: the other languages keep their translation of the earlier English text (key).
    private static string Tk(string key, string en, string ru) =>
        Ru ? ru : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? en : LTranslations.Get(CultureInfo.CurrentUICulture, key) ?? en;

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
    public static string NeverUpdated => T("never updated", "не обновлялась");
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
    public static string QuickConnect => T("Quick connect", "Быстрое подключение");
    // The row is one line in a 300 px sidebar: the longer wording ended in "…".
    public static string FastestNone => Tk("Run a ping test to find it", "Test ping to find it", "Проверить пинг и найти его");
    public static string NoCountry => T("Unknown location", "Страна не определена");
    public static string Back => T("Back", "Назад");
    public static string UpdateAll => T("Update all subscriptions", "Обновить все подписки");
    public static string ServersCount => T("servers", "серверов");
    public static string FavoritesEmpty => Tk("Hover a server and click ☆ to keep it here. Favorites survive subscription updates.",
        "Click ☆ next to a server (or press Ctrl+B) to keep it here. Favorites survive subscription updates.",
        "Нажмите ☆ у сервера (или Ctrl+B), чтобы закрепить его здесь. Избранное не пропадает при обновлении подписок.");
    public static string NothingFound => T("Nothing found", "Ничего не найдено");
    // The settings search shares NothingFound; this one says what the server search looks at.
    public static string NothingFoundServers => Tk("Nothing found", "Nothing found. Search looks at names, addresses and countries.", "Ничего не найдено. Поиск идёт по названию, адресу и стране.");
    public static string SubEmpty => T("No servers yet. Update the subscription to load them.", "Серверов пока нет. Обновите подписку, чтобы загрузить их.");
    public static string Protected => T("Protected", "Защищено");
    public static string Unprotected => T("Unprotected", "Не защищено");
    public static string LocalOnly => string.Format(T("Local proxy on port {0}", "Локальный прокси на порту {0}"), AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
    public static string Disconnect => T("Disconnect", "Отключиться");
    public static string ServerIp => T("Server", "Сервер");
    public static string Protocol => T("Protocol", "Протокол");
    public static string ExitAddress => T("Your address online", "Ваш адрес в сети");
    public static string ServerSource => T("Subscription", "Подписка");
    public static string OwnServer => T("Added by hand", "Добавлен вручную");
    // A bare "Update" read as updating the app itself; the tile refreshes subscriptions (two lines on the tile).
    public static string RailUpdate => Tk("Update", "Update subscriptions", "Обновить подписки");
    public static string CoreUpdatesBadge => T("Core updates", "Обновления ядер");
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
    public static string WelcomeText => Tk("Copy a subscription link or a vless://, vmess://, trojan://… link from your provider and paste it here.",
        "Copy the link from your provider and press Paste from clipboard. A subscription link or a vless://, vmess://, trojan:// link works.",
        "Скопируйте ссылку от провайдера и нажмите «Вставить из буфера». Подойдёт ссылка на подписку или на сервер (vless://, vmess://, trojan://).");
    public static string WelcomePaste => T("Paste from clipboard", "Вставить из буфера");
    public static string WelcomeQr => T("Scan QR code", "Сканировать QR-код");
    public static string WelcomeHint => T("Subscriptions update on their own; favorites survive the updates.",
        "Подписки обновляются сами, избранное сохраняется после обновлений.");
    // The core has started; whether the server answers is the check's job, so this promises nothing more.
    public static string ToastServerStarted => T("Connecting", "Подключение");
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
    public static string MenuOptionsDesc => Tk("Modes, ports, cores, updates", "Startup, modes, ports, updates", "Запуск, режимы, порты, обновления");
    public static string MenuRouting => T("Routing rules", "Правила маршрутизации");
    public static string MenuRoutingDesc => T("Rule sets: domains, IPs, apps", "Наборы правил: домены, IP, программы");
    public static string MenuDnsDesc => T("DNS servers and their rules", "DNS-серверы и их правила");
    public static string MenuHotkeys => T("Hotkeys", "Горячие клавиши");
    public static string MenuHotkeysDesc => T("Show the window, switch the proxy", "Показ окна, переключение прокси");
    public static string MenuTemplate => T("Config template", "Шаблон конфигурации");
    public static string MenuTemplateDesc => Tk("Raw core config, for experts", "The whole core config, for experts", "Вся конфигурация ядра, для опытных");
    public static string MenuPolicyGroup => T("Server group", "Группа серверов");
    public static string MenuProxyChain => T("Proxy chain", "Цепочка прокси");
    public static string MenuCustomConfig => T("Custom config", "Своя конфигурация");
    public static string MenuCustomOutbound => Tk("Custom outbound", "Custom outbound (JSON)", "Своё исходящее подключение (JSON)");
    public static string MenuPasteDesc => T("A subscription or vless://, vmess://… links", "Подписка или ссылки vless://, vmess://…");
    public static string MenuSubDesc => T("A link that updates the server list", "Ссылка, по которой обновляется список");
    public static string MenuServerDesc => T("Enter the parameters by hand", "Ввести параметры вручную");
    public static string MenuGroupDesc => T("Picks the fastest of several servers", "Выбирает самый быстрый из нескольких");
    public static string MenuChainDesc => T("Traffic goes through servers in turn", "Трафик идёт через серверы по очереди");
    public static string MenuGrpSystem => T("System tools", "Система");
    public static string MenuRestartAdmin => T("Restart as administrator", "Перезапустить от администратора");
    public static string MenuUwp => T("Allow Microsoft Store apps", "Пустить приложения Microsoft Store");
    public static string MenuUwpDesc => Tk("Loopback exemption for UWP apps", "If Store apps don't go through the proxy", "Если приложения из Store не идут через прокси");
    public static string MenuRegion => T("Region", "Регион");
    public static string MenuRegionDesc => T("Geo files, rules and DNS for a country", "Геофайлы, правила и DNS для страны");
    // The app menu's submenu for the system and data tools that used to sit behind the Settings tile.
    public static string MenuTools => T("Tools", "Сервис");
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
    public static string DiagTitle => T("Diagnostics for support", "Диагностика для поддержки");
    public static string DiagMenu => T("Copy diagnostics…", "Скопировать диагностику…");
    public static string DiagButton => T("Copy diagnostics", "Скопировать диагностику");
    public static string DiagLead => T("Exactly this text is copied. It has the version, system, mode, states and error codes, and no links, keys, server addresses or names.",
        "Скопируется ровно этот текст. В нём версия, система, режим, состояния и коды ошибок, без ссылок, ключей, адресов серверов и имён.");
    public static string DiagCopy => T("Copy", "Скопировать");
    public static string DiagCopied => T("Copied", "Скопировано");
    public static string FixChangePort => T("Change port", "Сменить порт");
    public static string FixShowLog => T("Show log", "Открыть журнал");
    public static string FixTryVpnAgain => T("Ask for rights again", "Запросить права снова");
    public static string FixUseProxy => T("Connect as proxy", "Подключиться как прокси");
    public static string TunDeniedExplain => T("VPN mode needs administrator rights, and they weren't granted. Ask again, or connect in proxy mode.",
        "Для режима VPN нужны права администратора, их не выдали. Запросите снова или подключитесь в режиме прокси.");
    public static string UnknownFailureExplain => T("The reason isn't known. The log has the core's own messages.",
        "Причина неизвестна. В журнале есть сообщения ядра.");
    public static string VpnNeedsSudo =>T("VPN mode needs the administrator password.", "Для режима VPN нужен пароль администратора.");
    public static string ModeLocalPort => T("Local port only", "Только локальный порт");

    public static string RouteTitle => T("What goes through the VPN", "Что идёт через VPN");
    public static string RouteSubtitle => T("Sites that go direct see your real IP address. Changes apply right away.",
        "Сайты, которые идут напрямую, видят ваш настоящий IP. Изменения применяются сразу.");
    public static string RouteRussiaDirect => T("Russian sites direct", "Российские сайты напрямую");
    public static string RouteRussiaDirectHint => Tk("Sites in .ru and .рф, banks, Gosuslugi and Russian IPs open directly. Everything else goes through the VPN, including blocked .ru sites.",
        "Banks, Gosuslugi and .ru sites open directly. The rest goes through the VPN, blocked .ru sites too.",
        "Банки, Госуслуги и сайты в\u00A0.ru\u00A0— напрямую, остальное и заблокированные .ru\u00A0— через VPN.");
    public static string RouteBlockedOnly => T("Only blocked sites", "Только заблокированное");
    public static string RouteBlockedOnlyHint => Tk("Only sites blocked in Russia, YouTube, Discord, Telegram and AI services go through the VPN. The rest opens directly and faster.",
        "Only blocked sites, YouTube, Discord, Telegram and AI go through the VPN. The rest opens directly.",
        "Через VPN\u00A0— только заблокированное, YouTube, Discord, Telegram и ИИ. Остальное\u00A0— напрямую.");
    public static string RouteAll => T("Everything through VPN", "Всё через VPN");
    public static string RouteBuiltinWhitelist => T("Everything except China (v2rayN)", "Всё, кроме Китая (v2rayN)");
    public static string RouteBuiltinBlacklist => T("Sites blocked in China (v2rayN)", "Заблокированное в Китае (v2rayN)");
    public static string RouteBuiltinGlobal => T("Everything, v2rayN rules", "Всё, правила v2rayN");
    public static string RouteAllHint => Tk("All traffic except the local network goes through the server. Some Russian banks and Gosuslugi may refuse a foreign IP.",
        "All traffic goes through the server. Some banks and Gosuslugi refuse a foreign IP.",
        "Весь трафик через сервер. Некоторые банки и Госуслуги не пускают с иностранного IP.");
    public static string Recommended => T("Recommended", "Рекомендуем");
    public static string BlockAds => T("Block ads and trackers", "Блокировать рекламу и трекеры");
    public static string BlockAdsHint => Tk("By the category-ads-all list, in the three modes above.", "Hides ads on sites and in apps, in any mode.", "Скрывает рекламу на сайтах и в приложениях, в любом режиме.");
    public static string RouteCustom => T("Your rules", "Свои правила");
    public static string RouteCustomHint => T("Sets from the routing settings.", "Наборы из настройки маршрутов.");
    public static string RouteCustomPick => T("Choose…", "Выбрать…");
    public static string RouteDownloading => T("Downloading the rule lists for Russia…", "Загрузка списков правил для России…");
    public static string RouteApplying => T("Applying…", "Применение…");
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
    public static string SeriesHeader => T("Auto-select", "Автовыбор");
    public static string SeriesOnline => T("{0} of {1} online", "{0} из {1} на связи");
    public static string SeriesMember => T("Server {0}", "Сервер {0}");
    public static string ConnectFastestIn => T("Connect to the fastest server here", "Подключиться к самому быстрому здесь");
    public static string UpdateSubscription => T("Update subscription", "Обновить подписку");
    public static string ClearSearch => T("Clear search", "Очистить поиск");
    public static string AppMenu => T("Menu", "Меню");
    public static string MiniWindow => T("Mini window", "Мини-окно");
    public static string FullWindow => T("Full window", "Полное окно");

    public static string SubEditHint => T("Where V2pn gets the servers from.", "Откуда V2pn берёт серверы.");
    public static string ServerEditHint => T("Connection details of the server. The fields depend on the protocol.", "Параметры подключения к серверу. Набор полей зависит от протокола.");
    public static string HotkeysHint => T("Click a field and press the key combination; it applies after a restart.", "Щёлкните по полю и нажмите сочетание клавиш. Заработает после перезапуска.");

    public static string GrpSource => T("Source", "Источник");
    // An empty picker read as a broken field.
    public static string GrpPickSub => T("Choose a subscription", "Выберите подписку");
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
    // Simple settings: the everyday rows, in plain words.
    public static string SetAutoRun => T("Start with the system", "Запускать вместе с системой");
    // Whether the sign-in start shows the window is the next row's choice, so this hint promises no tray.
    public static string SetAutoRunHint => T("V2pn starts when you sign in.", "V2pn запустится при входе в систему.");
    public static string SetStartHidden => Tk("Start minimized", "Start in the tray at sign-in", "При входе в систему — только значок в трее");
    public static string SetStartHiddenHint => Tk("Only the tray icon appears; the window stays closed.",
        "The window stays closed at sign-in. Opening V2pn yourself shows it as usual.",
        "Окно не откроется само. Если запустить V2pn вручную, оно появится как обычно.");
    // v2rayN's tip only warned about desktops without a tray; Windows and macOS always have one, so this says
    // what closing does. Other languages keep v2rayN's translated tip.
    public static string SetHideToTrayHint => Ru
        ? "Крестик прячет окно, а V2pn продолжает работать. Выйти можно из меню в трее."
        : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
            ? "Closing the window keeps V2pn running. Quit from the tray menu."
            : ResUI.TbSettingsHide2TrayWhenCloseTip;
    public static string SetShowSpeed => T("Show connection speed", "Показывать скорость соединения");
    public static string SetShowSpeedHint => T("The speed chart under the map. Takes effect after a restart.", "График скорости под картой. Включится после перезапуска.");
    public static string SetPort => T("Local port", "Локальный порт");
    public static string SetPortHint => T("Apps that ignore the system proxy can be pointed at 127.0.0.1 and this port.", "Программы, которые не видят системный прокси, можно направить на 127.0.0.1 и этот порт.");
    public static string SetLan => T("Allow devices on your network", "Пускать устройства из локальной сети");
    public static string SetLanHint => T("A phone or another computer on the same Wi-Fi can go online through V2pn.", "Телефон или другой компьютер в той же Wi-Fi-сети смогут выходить в интернет через V2pn.");
    public static string LanOpenWarning => T(
        "Right now any device on your network can use this proxy without a password. Turn on the separate port for the network and set a user name and password.",
        "Сейчас любое устройство в вашей сети может пользоваться этим прокси без пароля. Включите отдельный порт для сети и задайте логин и пароль.");
    public static string LanOpenToast => T(
        "The proxy is open to every device on your network, without a password. See Settings → Local proxy.",
        "Прокси открыт для всех устройств в вашей сети без пароля. Это настраивается в «Настройки → Локальный прокси».");
    public static string SetGeoInterval => T("Update rule lists every, hours", "Обновлять списки правил, часов");
    public static string SetGeoIntervalHint => T("Lists of sites and addresses the routing modes use. 0 turns updates off.", "Списки сайтов и адресов для режимов маршрутизации. 0 — не обновлять.");
    public static string SubEditTitle => T("Subscription", "Подписка");
    public static string SubName => T("Name", "Название");
    public static string SubNamePlaceholder => T("For example, the provider's name", "Например, название провайдера");
    public static string SubLink => T("Subscription link", "Ссылка на подписку");
    public static string SubLinkHint => T("The link from your provider. Leave it empty for a group of servers you add by hand.", "Ссылка от провайдера. Оставьте пустой, если это группа для серверов, добавленных вручную.");
    public static string SubAutoUpdate => T("Include in updates", "Обновлять вместе со всеми");
    public static string SubInterval => T("Update every, minutes", "Обновлять каждые, мин");
    public static string MoreFields => T("Show more fields", "Показать дополнительные поля");
    public static string FewerFields => T("Hide extra fields", "Скрыть дополнительные поля");
    public static string SetAdvanced => T("Advanced settings", "Расширенные настройки");
    public static string SetAdvancedHint => T("Ports, cores, TUN, traffic tricks", "Порты, ядра, TUN, обработка трафика");

    // Settings with windows of their own, linked from the bottom of the Settings navigation.
    public static string SetMore => T("More settings", "Другие настройки");
    public static string SetMoreTip => T("Opens in its own window", "Откроется в отдельном окне");
    public static string SetGeneral => T("General", "Общие");
    public static string SetGeneralHint => T("How V2pn starts and what the window shows.", "Как запускается V2pn и что показывает окно.");
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
    // Not "Updates": people looked here for app updates, which live in their own window.
    public static string SetUpdates => Tk("Updates", "Rule lists", "Списки правил");
    public static string SetUpdatesHint => Tk("How often V2pn refreshes the lists behind the routing modes.",
        "How often V2pn downloads fresh lists for the routing modes. V2pn itself updates from Menu → Check for updates.",
        "Как часто V2pn скачивает свежие списки для режимов маршрутизации. Сам V2pn обновляется из меню: «Проверить обновления».");
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
    public static string HintTunDnsMode => T("How sing-box handles DNS inside the VPN. Change it only if sites stop opening.", "Как sing-box обрабатывает DNS внутри VPN. Меняйте, только если перестали открываться сайты.");
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
    public static string Checking => T("Checking…", "Проверка…");
    public static string CheckWorks => T("Works · {0} ms", "Работает · {0} мс");

    /// <summary>"85 ms", with the unit Russian readers expect.</summary>
    public static string Ms(int value) => Ru ? $"{value} мс" : $"{value} ms";
    public static string CheckFailed => T("No response", "Нет ответа");
    public static string NoTraffic => T("No traffic yet", "Трафика пока нет");
    public static string StateChecking => T("Checking connection…", "Проверка соединения…");
    public static string StateUnchecked => T("Connection not checked", "Подключение ещё не проверено");
    public static string StateProxyConnected => T("Proxy connected", "Прокси подключён");
    public static string StateVpnConnected => T("VPN connected", "VPN подключён");
    public static string SystemProxyScope => T("For apps that use the system proxy", "Для программ, использующих системный прокси");
    public static string RetryConnectionCheck => T("Check again", "Повторить проверку");
    public static string CheckStatus(AvailabilityCheckResult check) => check.State switch
    {
        AvailabilityState.Unsupported => T("Connection check unavailable for this configuration", "Проверка недоступна для этой конфигурации"),
        AvailabilityState.UnexpectedStatus => string.Format(T("Check site returned HTTP {0}", "Проверочный сайт вернул HTTP {0}"), check.HttpStatus),
        AvailabilityState.Redirected => T("Check site redirected", "Проверочный сайт перенаправил запрос"),
        AvailabilityState.TimedOut => T("Connection check timed out", "Истекло время проверки"),
        AvailabilityState.Unreachable => T("Check site unavailable", "Проверочный сайт недоступен"),
        AvailabilityState.NoResponse => StateNoResponse,
        _ => StateUnchecked,
    };
    public static string CheckUrlFailedTip => T("The check address didn't answer, another site did through this server.",
        "Проверочный адрес не ответил, другой сайт через этот сервер открылся.");
    public static string CheckFailureHint => T("The check could not confirm this connection. Try the check again.",
        "Проверка не смогла подтвердить подключение. Повторите проверку.");
    public static string StateNoResponse => T("Server not responding", "Сервер не отвечает");
    public static string ClickToConnect => Tk("Click to connect · right-click for more", "Right-click for more actions", "Правой кнопкой\u00A0— другие действия");

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
    // The sidebar row has room for "19 servers · 11 min ago"; with "updated" in front the time itself was cut.
    public static string SubAgoShort => Tk("updated {0}", "{0}", "{0}");
    public static string DurationSec => T("{0} sec", "{0} сек");
    public static string DurationMinSec => T("{0} min {1} sec", "{0} мин {1} сек");

    // Paste from clipboard: what happened and what to do next.
    public static string ClipboardEmpty => T("The clipboard is empty. Copy the link from your provider first.", "Буфер обмена пуст. Сначала скопируйте ссылку у провайдера.");
    public static string PasteNotRecognized => T("No server link found. Paste a subscription link (https://…) or a vless://, vmess://, trojan://… link.",
        "Ссылка на сервер не найдена. Вставьте ссылку на подписку (https://…) или ссылку вида vless://, vmess://, trojan://…");
    public static string SubAddedUpdating => T("Subscription added, downloading its servers…", "Подписка добавлена. Загрузка серверов…");
    public static string SubAlreadyAdded => T("This subscription is already added. Updating it.", "Эта подписка уже добавлена, сейчас она обновится.");

    // A subscription update that brought no servers.
    public static string SubUpdateFailed => T("Couldn't update \"{0}\". The provider's site may be blocked, or the link is wrong.",
        "Не удалось обновить «{0}». Сайт провайдера может быть заблокирован или ссылка неверна.");
    public static string SubsUpdateFailed => T("Couldn't update: {0}. The providers' sites may be blocked, or the links are wrong.",
        "Не удалось обновить: {0}. Сайты провайдеров могут быть заблокированы или ссылки неверны.");
    public static string SubUpdateTryVpn => T("Try \"{0}\" in the subscription's menu.", "Попробуйте «{0}» в меню подписки.");
    public static string SubUpdateFailedShort => T("update failed", "не обновилась");
    public static string SubUpdateFailedTip => T("The last update failed", "Последнее обновление не удалось");

    // When something fails, the next step.
    public static string NoneResponding => T("None of these servers answer. Update the subscription or ask your provider.",
        "Ни один из этих серверов не отвечает. Обновите подписку или напишите провайдеру.");
    // Nothing to test: the current server is the only one of its country and subscription.
    public static string NoOtherServers => T("There are no other servers in this country or subscription. Update the subscription or ask your provider.",
        "Других серверов в этой стране или подписке нет. Обновите подписку или напишите провайдеру.");
    public static string FindWorkingServer => T("Find a working server", "Найти рабочий сервер");
    public static string CancelServerSearch => T("Cancel server search", "Отменить поиск серверов");
    public static string CheckOtherCountries => T("Check other countries in this subscription", "Проверить другие страны этой подписки");
    public static string NoServersInCountryResponded => T("No other server in this country answered the check.", "Другие серверы этой страны не ответили на проверку.");
    public static string UnknownServerCountry => T("unknown country", "страна неизвестна");
    public static string ConfirmReplacement => T("Switch to {0} ({1})? V2pn will check the new connection.", "Переключиться на {0} ({1})? V2pn проверит новое подключение.");
    public static string ConfirmReplacementOtherCountry => T("No server in {0} answered. Switch to {1}? Your country changes from {0} to {2}. V2pn will check the new connection.",
        "Серверы страны «{0}» не ответили. Переключиться на {1}? Страна сменится с «{0}» на «{2}». V2pn проверит новое подключение.");
    public static string SwitchServer => T("Switch server", "Переключиться");
    public static string FindProgress => T("Checking {0} of {1}…", "Проверка {0} из {1}…");
    public static string StateNoResponseTip => T("The server didn't answer the check. Try another server.", "Сервер не ответил на проверку. Попробуйте другой сервер.");
    public static string StateCoreFailed => T("Couldn't start the connection", "Не удалось запустить подключение");
    public static string StateCoreStopped => T("The connection dropped", "Подключение прервалось");
    public static string StateReconnecting => T("Reconnecting…", "Переподключение…");
    public static string StateConnecting => T("Connecting…", "Подключение…");
    public static string CoreKeepsStoppingToast => T("The connection kept dropping. V2pn switched off so you are not left without internet.",
        "Подключение обрывалось раз за разом. V2pn выключился, чтобы не оставить вас без интернета.");
    public static string StateCoreFailedTip => T("The problem is on this computer, not the server. Click to open the log.",
        "Проблема на этом компьютере, а не на сервере. Нажмите, чтобы открыть журнал.");
    public static string BrowseCountries => T("Browse countries", "Открыть список стран");
    public static string RouteDownloadFailed => T("Couldn't download the Russian site lists, so the routing hasn't changed. Try again after connecting.",
        "Не удалось скачать списки российских сайтов, маршруты не изменились. Попробуйте ещё раз после подключения.");
    // v2rayN's own wording says "invalid" and breaks the Russian plural.
    public static string RemoveDeadDone => T("Unresponsive servers removed: {0}", "Удалено серверов без ответа: {0}");
    // v2rayN's "invalid configuration, check or reselect" while there are servers: the config for the picked one failed.
    public static string ServerConfigBroken => T("Couldn't prepare a connection to this server. Pick another server or check its settings.",
        "Не удалось подготовить подключение к этому серверу. Выберите другой сервер или проверьте его настройки.");

    // Keyboard and screen readers. Appended to the name of the chosen option, whose tick is visual only.
    public static string OptionSelected => T(", selected", ", выбрано");
    public static string TestPingAll => T("Test ping of all servers", "Проверить пинг всех серверов");

    // Tray: the same verdict and actions as the window.
    public static string TrayOpen => T("Open V2pn", "Открыть V2pn");
    public static string TrayProxySystem => T("Proxy: system", "Прокси: системный");
    public static string TrayProxyKeep => T("Proxy: local port only", "Прокси: только локальный порт");
    public static string TrayProxyPac => T("Proxy: PAC script", "Прокси: PAC-скрипт");
    public static string TrayCopyProxyCmd => T("Copy proxy command for the terminal", "Скопировать команду для терминала");

    // Close (X) on Windows: asked once.
    public static string CloseToTrayTitle => T("Minimize V2pn to the tray or quit?", "Свернуть V2pn в трей или выйти?");
    public static string CloseToTrayBody => T("In the tray V2pn keeps running and doesn't drop the connection. Open the window from the icon next to the clock. You can change this in Settings.",
        "В трее V2pn продолжает работать и не разрывает подключение. Окно открывается значком рядом с часами. Выбор можно изменить в настройках.");
    public static string CloseToTrayHide => T("Minimize to tray", "Свернуть в трей");

    // Window lifecycle.
    public static string Restore => T("Restore", "Восстановить");
    public static string ShutdownCleaning => T("Restoring network settings…", "Восстановление сетевых настроек…");
}
