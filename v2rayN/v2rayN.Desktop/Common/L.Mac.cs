namespace v2rayN.Desktop;

// macOS: the menu bar, the status item, the Dock menu and the About panel. The standard commands use the words
// macOS itself uses in each language.
public static partial class L
{
    public static string MacAbout => T("About V2pn", "О программе V2pn");
    public static string MacSettings => T("Settings…", "Настройки…");
    public static string MacCheckUpdates => T("Check for Updates…", "Проверить обновления…");
    public static string MacServices => T("Services", "Службы");
    public static string MacHide => T("Hide V2pn", "Скрыть V2pn");
    public static string MacHideOthers => T("Hide Others", "Скрыть остальные");
    public static string MacShowAll => T("Show All", "Показать все");
    public static string MacQuit => T("Quit V2pn", "Завершить V2pn");

    public static string MacFile => T("File", "Файл");
    public static string MacAddFromClipboard => T("Add from Clipboard", "Добавить из буфера обмена");
    public static string MacAddSubscription => T("Add Subscription…", "Добавить подписку…");
    public static string MacQrFromImage => T("QR Code from an Image…", "QR-код из картинки…");
    public static string MacCloseWindow => T("Close Window", "Закрыть окно");

    // "Edit" alone is the verb of the server menus in the other languages; the menu has its own word.
    public static string MacEdit => Tk("Edit menu", "Edit", "Правка");
    public static string MacUndo => T("Undo", "Отменить");
    public static string MacRedo => T("Redo", "Повторить");
    public static string MacCut => T("Cut", "Вырезать");
    public static string MacCopy => T("Copy", "Скопировать");
    public static string MacPaste => T("Paste", "Вставить");
    public static string MacSelectAll => T("Select All", "Выбрать все");
    public static string MacFindServer => T("Find a Server", "Найти сервер");

    public static string MacMode => T("Mode", "Режим");
    public static string MacRoute => T("Route", "Маршрут");
    public static string MacRouteSettings => T("Route Settings…", "Настроить маршруты…");
    public static string MacServers => T("Servers", "Серверы");
    public static string MacNoServers => T("No servers yet", "Серверов пока нет");
    // {0}: how many servers of the country the menu leaves out.
    public static string MacMoreServers => T("{0} more in the window…", "Ещё {0} в окне…");
    public static string MacSubscriptions => T("Subscriptions", "Подписки");
    public static string MacAllSubscriptions => T("All Subscriptions…", "Все подписки…");
    // {0}: the subscription's name.
    public static string MacUpdateSub => T("Update “{0}”", "Обновить «{0}»");

    public static string MacWindow => T("Window", "Окно");
    public static string MacMinimize => T("Minimize", "Свернуть");
    public static string MacZoom => T("Zoom", "Изменить масштаб");
    public static string MacMainWindow => T("V2pn Window", "Окно V2pn");

    public static string MacHelp => T("Help", "Справка");
    public static string MacDocs => T("V2pn Documentation", "Документация V2pn");
    public static string MacWhatsNew => T("What’s New", "Что нового");

    // The About panel, under the version. {0}: "Xray 25.10.15, sing-box 1.12.0".
    public static string MacAboutTagline => T("A VPN and proxy client for macOS and Windows.", "Клиент VPN и прокси для macOS и Windows.");
    public static string MacAboutCores => T("Cores: {0}", "Ядра: {0}");
    public static string MacAboutSite => T("Website", "Сайт");
    public static string MacAboutDocs => T("Documentation", "Документация");
    public static string MacAboutSource => T("Source code", "Исходный код");
    public static string MacAboutLicense => T("Licensed under GPL-3.0", "Лицензия GPL-3.0");
    public static string MacAboutBasedOn => T("Based on v2rayN by 2dust", "Сделано на основе v2rayN (2dust)");
    public static string MacAboutThanks => T("Built with", "Использованы");

    // Notifications while the window is not in front; the text under the title is the window's own verdict.
    public static string MacNotifyLost => T("Connection lost", "Соединение прервано");
    public static string MacNotifyBack => T("Connection restored", "Соединение восстановлено");

    // What the settings call "the tray" on Windows is the menu bar on a Mac.
    public static string MacStartHidden => T("At sign-in, start in the menu bar only", "При входе в систему — только значок в строке меню");
}
