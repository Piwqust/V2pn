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
    public static string SceneRenew => T("Renew", "Продлить");
    public static string SceneSupport => T("Support", "Поддержка");
    public static string SceneForeign => T("The server in use is not from {0}", "Сейчас сервер не от {0}");
    public static string SceneForeignFrom => T(": it is from «{0}».", " — из «{0}».");
    public static string SceneForeignAscii => T("! not a {0} server: {1}", "! сервер не от {0}: {1}");
    public static string SceneToPartner => T("Switch to a {0} server", "Перейти на сервер {0}");
    public static string SceneToPartnerAscii => T("[ {0} server ]", "[ сервер {0} ]");

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
    public static string SceneChange => T("Change server", "Сменить сервер");
    public static string SceneProxy => T("Proxy", "Прокси");

    // The cabinet look.

    // The studio look.
    public static string SceneRouteHint => T("what goes through the VPN and what goes direct", "что идёт через VPN, а что напрямую");

    // The ASCII look.
}
