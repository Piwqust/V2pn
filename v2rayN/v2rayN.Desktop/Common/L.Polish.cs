namespace v2rayN.Desktop;

// Plain words for places that still showed v2rayN's wording, and the first-run route choice.
public static partial class L
{
    public static string LocalOnlyTip => T("The system proxy stays as it was: only apps set to 127.0.0.1:{0} go through the server.",
        "Системный прокси не меняется: через сервер идут только программы, настроенные на 127.0.0.1:{0}.");

    // First run: what goes through the VPN, chosen before the first connection.
    public static string WelcomeRouteAllHint => T("All traffic goes through the server. Works right away.", "Весь трафик идёт через сервер. Работает сразу.");
    public static string WelcomeRouteRuHint => T("Banks and Gosuslugi open directly. V2pn downloads the lists of Russian sites when you pick this.",
        "Банки и Госуслуги открываются напрямую. V2pn скачает списки российских сайтов, когда вы это выберете.");
    public static string WelcomeRouteLater => T("You can change it any time under the Connect button.", "Это можно поменять в любой момент под кнопкой «Подключиться».");

    public static string Save => T("Save", "Сохранить");
    public static string Cancel => T("Cancel", "Отмена");
    // v2rayN reports deletes, copies, saves and core updates with one "Operation successful".
    public static string Done => T("Done", "Готово");
    public static string Close => T("Close", "Закрыть");
    // The window's own close button is also "Close"; a screen reader has to tell the two apart.
    public static string CloseAppearance => T("Close Appearance", "Закрыть «Оформление»");
    public static string Minimize => T("Minimize", "Свернуть");
    public static string Maximize => T("Maximize", "Развернуть");
    public static string Quit => T("Quit", "Выйти");
    public static string QuitConfirm => T("Quit V2pn? If you are connected, the connection turns off.", "Выйти из V2pn? Если подключение включено, оно отключится.");
    public static string Restart => T("Restart", "Перезапустить");
    public static string RemoveConfirm => T("Delete? This can't be undone.", "Удалить? Это нельзя отменить.");
    public static string LanguageAfterRestart => T("The new language appears after V2pn restarts.", "Новый язык появится после перезапуска V2pn.");
    public static string RoutingChanged => T("Routing changed", "Маршруты изменены");

    // App menu.
    public static string MenuSubUpdateViaVpn => T("Update through the VPN", "Обновить через VPN");
    public static string MenuSubUpdateViaVpnDesc => T("When the provider's site is blocked", "Если сайт провайдера заблокирован");
    public static string MenuSubManage => T("All subscriptions…", "Все подписки…");
    public static string MenuRestartConnection => T("Restart connection", "Перезапустить подключение");
    public static string HelpSite => T("V2pn website", "Сайт V2pn");
    public static string HelpReport => T("Report a problem", "Сообщить о проблеме");
    public static string HelpBasedOn => T("Built on v2rayN", "Сделано на основе v2rayN");
    public static string HelpVersion => T("Version {0}", "Версия {0}");

    // Server and subscription menus in the sidebar.
    public static string MenuTcping => T("TCP ping", "TCP-пинг");
    public static string MenuDelete => T("Delete", "Удалить");

    // Tray menu.
    public static string TrayDisconnected => T("Proxy: off", "Прокси: выключен");
    public static string TraySubUpdateViaVpn => T("Update subscriptions through the VPN", "Обновить подписки через VPN");

    public static string NoServersYet => T("No servers yet. Add a subscription from your provider or paste a server link.",
        "Серверов пока нет. Добавьте подписку от провайдера или вставьте ссылку на сервер.");
    public static string NewSubscription => T("New subscription", "Новая подписка");
    public static string HotkeyNotSet => T("Not set", "Не задано");

    // The subscriptions window as a list beside the chosen subscription.
    public static string SubsNone => T("No subscriptions yet. Add the link your provider gave you.", "Подписок пока нет. Добавьте ссылку от провайдера.");
    public static string SubManual => T("Servers added by hand", "Серверы, добавленные вручную");
    public static string SubSaveAsk => T("Save the changes to “{0}”?", "Сохранить изменения в «{0}»?");
    public static string SubDontSave => T("Don't save", "Не сохранять");
    public static string SubDeleteAsk => T("Delete the subscription “{0}” and its servers?", "Удалить подписку «{0}» вместе с её серверами?");
}
