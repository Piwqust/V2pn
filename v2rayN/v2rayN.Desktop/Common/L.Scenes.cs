namespace v2rayN.Desktop;

// The partner looks' own words (Views/Editions): the cabinet, studio and ASCII looks and their shared parts.
public static partial class L
{
    // The server list.
    public static string SceneSearch => T("Search", "Поиск");
    public static string SceneSearchAscii => T("> search", "> поиск");
    public static string ScenePing => T("Ping", "Пинг");
    public static string ScenePingAscii => T("[ping]", "[пинг]");
    public static string ScenePingTip => T("Ping the servers", "Проверить пинг серверов");
    public static string SceneUpdateTip => T("Update the subscriptions", "Обновить подписки");
    public static string SceneAddTip => T("Add a server or a subscription", "Добавить сервер или подписку");
    public static string SceneNoFavourites => T("No favourites yet: star a server in plain V2pn.", "В избранном пока пусто: отметьте сервер звёздочкой в обычном V2pn.");
    public static string SceneNothingFound => T("Nothing found.", "Ничего не нашлось.");
    public static string SceneFirstRows => T("The first {0} of {1} are shown. Narrow the search.", "Показаны первые {0} из {1}. Уточните поиск.");
    public static string SceneAll => T("All", "Все");
    public static string SceneAllAscii => T("all", "все");
    public static string SceneFavourites => T("Favourites", "Избранное");
    public static string SceneFavouritesAscii => T("favourites", "избранное");
    public static string SceneMs => T("ms", "мс");
    public static string SceneNo => T("down", "нет");
    public static string SceneNoAnswer => T("no answer", "нет ответа");

    // The subscription block and the note about another provider's server.
    public static string SceneCabinet => T("Cabinet", "Кабинет");
    public static string SceneCabinetAscii => T("[ cabinet ]", "[ кабинет ]");
    public static string SceneSupport => T("Support", "Поддержка");
    public static string SceneSupportAscii => T("[ support ]", "[ поддержка ]");
    public static string SceneForeign => T("The server in use is not from {0}", "Сейчас сервер не от {0}");
    public static string SceneForeignFrom => T(": it is from «{0}».", " — из «{0}».");
    public static string SceneForeignAscii => T("! not a {0} server: {1}", "! сервер не от {0}: {1}");
    public static string SceneToPartner => T("Switch to a {0} server", "Перейти на сервер {0}");
    public static string SceneToPartnerAscii => T("[ {0} server ]", "[ сервер {0} ]");
    public static string SceneSubscriptionOf => T("{0} subscription", "Подписка {0}");
    public static string SceneSubscriptionAscii => T("{0} SUBSCRIPTION", "ПОДПИСКА {0}");

    // Before the first server.
    public static string SceneEmptyTitle => T("Add servers", "Добавьте серверы");
    public static string SceneEmptyTitleAscii => T("> no servers yet", "> серверов пока нет");
    public static string SceneEmptyLead => T(
        "Copy the subscription link in {0}'s cabinet and paste it here. Or add a subscription by hand.",
        "Скопируйте ссылку на подписку в кабинете {0} и вставьте её сюда. Или добавьте подписку вручную.");
    public static string ScenePaste => T("Paste a link", "Вставить ссылку");
    public static string ScenePasteAscii => T("[ paste a link ]", "[ вставить ссылку ]");
    public static string SceneAddSub => T("Add a subscription", "Добавить подписку");
    public static string SceneAddSubAscii => T("[ add a subscription ]", "[ добавить подписку ]");
    public static string SceneImport => T("Move from another app", "Перенести из другого приложения");
    public static string SceneImportAscii => T("[ move from another app ]", "[ перенести из другого приложения ]");

    // Shared.
    public static string SceneOf => T("{0} of {1}", "{0} из {1}");
    public static string SceneIp => T("IP address", "IP-адрес");
    public static string SceneSettings => T("V2pn settings", "Настройки V2pn");
    public static string SceneSettingsAscii => T("[ settings ]", "[ настройки ]");
    public static string SceneChange => T("Change server", "Сменить сервер");
    public static string SceneChangeShort => T("Change", "Сменить");
    public static string SceneProxy => T("Proxy", "Прокси");

    // The cabinet look.
    public static string SceneLatency => T("Latency", "Отклик");
    public static string SceneAnswering => T("Answering", "Отвечают");
    public static string SceneServersUnit => T("servers", "серверов");
    public static string SceneThroughVpn => T("Through VPN", "Через VPN");
    public static string SceneServers => T("Servers", "Серверы");
    public static string SceneWorking => T("Working", "Работает");
    public static string SceneDownByPartner => T("{0} down by {1}'s check.", "{0} не работают по проверке {1}.");
    public static string SceneAllUpByPartner => T("All servers work by {0}'s check.", "Все серверы работают по проверке {0}.");
    public static string SceneDownByPing => T("{0} did not answer the last ping.", "{0} не ответили на последнюю проверку пинга.");
    public static string SceneAllUpByPing => T("All servers answered the last ping.", "Все серверы ответили на последнюю проверку.");
    public static string ScenePingFirst => T("Ping the servers to see which ones answer.", "Проверьте пинг, чтобы узнать, какие серверы отвечают.");
    public static string SceneTrafficNote => T(
        "{0} gives the server list. Traffic goes from your computer straight to the server; {0} does not see it.",
        "{0} отдаёт список серверов. Трафик идёт с вашего компьютера прямо на сервер — {0} его не видит.");
    public static string SceneDoodle => T(
        "  .--------.                      .----------.\n  | ###### |---·····---·····---| ######## |\n  | ###### |       server       | ######## |\n  '---..---'                      | ####-### |\n    ==''==    your PC → server    '----------'",
        "  .--------.                      .----------.\n  | ###### |---·····---·····---| ######## |\n  | ###### |       сервер       | ######## |\n  '---..---'                      | ####-### |\n    ==''==     ваш ПК → сервер    '----------'");

    // The studio look.
    public static string SceneYouAreIn => T("you are in", "сейчас вы в");
    public static string SceneKeyServers => T("01 · servers", "01 · серверы");
    public static string SceneKeyMode => T("02 · mode", "02 · режим");
    public static string SceneKeyRoute => T("03 · through VPN", "03 · через VPN");
    public static string SceneKeyAccount => T("04 · subscription", "04 · подписка");
    public static string SceneKeyList => T("05 · servers", "05 · серверы");
    public static string SceneRouteHint => T("what goes through the VPN and what goes direct", "что идёт через VPN, а что напрямую");
    public static string SceneChangeArrow => T("change ▾", "изменить ▾");
    public static string SceneAnsweringOf => T("Answering: {0}", "Отвечают {0}");
    public static string SceneNotPinged => T("Not pinged yet", "Пинг не проверялся");
    public static string SceneProxyLower => T("proxy", "прокси");
    public static string SceneWholeComputer => T("The whole computer", "Весь компьютер");
    public static string SceneBrowsersApps => T("Browsers and apps", "Браузеры и программы");
    public static string SceneAllThrough => T("all traffic through the server", "весь трафик через сервер");
    public static string SceneWithSystemProxy => T("with the system proxy", "с системным прокси");

    // The ASCII look.
    public static string SceneMapLegend => T("V2pn map · @ your server · * other servers", "карта V2pn · @ ваш сервер · * другие серверы");
    public static string SceneKeyServer => T("SERVER", "СЕРВЕР");
    public static string SceneKeyServersAscii => T("SERVERS", "СЕРВЕРЫ");
    public static string SceneKeyModeAscii => T("MODE", "РЕЖИМ");
    public static string SceneKeyRouteAscii => T("THROUGH VPN", "ЧЕРЕЗ VPN");
    public static string SceneMsTo => T("ms to {0}", "мс до {0}");
    public static string SceneNotPingedAscii => T("not pinged yet", "пинг не проверялся");
    public static string SceneModeProxyAscii => T("(•) proxy  ( ) vpn", "(•) прокси  ( ) vpn");
    public static string SceneModeVpnAscii => T("( ) proxy  (•) vpn", "( ) прокси  (•) vpn");
    public static string SceneChangeAscii => T("[change]", "[изменить]");
    public static string SceneConnectAscii => T("[ connect ]", "[ подкл ]");
}
