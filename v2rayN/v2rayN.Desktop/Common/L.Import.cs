namespace v2rayN.Desktop;

// Moving to V2pn from v2rayN.
public static partial class L
{
    public static string ImportFound => T("v2rayN is on this computer", "На компьютере есть v2rayN");
    public static string ImportFoundCounts => T("Servers: {0}, subscriptions: {1}. V2pn can take them over with the routing and settings.", "Серверов: {0}, подписок: {1}. V2pn может забрать их вместе с маршрутами и настройками.");
    public static string ImportButton => T("Move from v2rayN", "Перенести из v2rayN");
    public static string ImportMenu => T("Move from v2rayN…", "Перенести из v2rayN…");
    public static string ImportNone => T("No v2rayN with servers was found on this computer.", "На этом компьютере не найден v2rayN с серверами.");
    public static string ImportConfirm => T("Replace V2pn's servers, subscriptions and settings with those of v2rayN in {0}? v2rayN closes and no longer starts at sign-in; its own files stay as they are. V2pn's current settings are kept in guiTemps.", "Заменить серверы, подписки и настройки V2pn данными v2rayN из {0}? v2rayN закроется и перестанет запускаться при входе в систему, его файлы не изменятся. Текущие настройки V2pn сохранятся в guiTemps.");
    public static string ImportConfirmYes => T("Move", "Перенести");
    public static string ImportCloseFirst => T("V2pn could not close v2rayN: it runs as administrator. Quit v2rayN from its tray icon and try again.", "V2pn не смог закрыть v2rayN: он запущен от администратора. Выйдите из v2rayN через значок в трее и попробуйте снова.");
    public static string ImportRestarting => T("Moving from v2rayN, V2pn restarts…", "Перенос из v2rayN, V2pn перезапускается…");
    public static string ImportRestartFailed => T("V2pn could not restart. Close it and start it again to finish the move.", "V2pn не смог перезапуститься. Закройте его и запустите снова, перенос завершится при запуске.");
    public static string ImportDone => T("Moved from v2rayN: servers {0}, subscriptions {1}.", "Перенесено из v2rayN: серверов {0}, подписок {1}.");
}
