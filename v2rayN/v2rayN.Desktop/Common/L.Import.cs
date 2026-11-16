namespace v2rayN.Desktop;

// Moving to V2pn from v2rayN and other clients.
public static partial class L
{
    // The whole-folder move from v2rayN.
    public static string ImportFound => T("Servers from other apps", "Серверы из других приложений");
    public static string ImportButton => T("Move from v2rayN", "Перенести из v2rayN");
    public static string ImportNone => T("No v2rayN with servers was found on this computer.", "На этом компьютере не найден v2rayN с серверами.");
    public static string ImportConfirm => T("Replace V2pn's servers, subscriptions and settings with those of v2rayN in {0}? v2rayN closes and no longer starts at sign-in; its own files stay as they are. V2pn's current settings are kept in guiTemps.", "Заменить серверы, подписки и настройки V2pn данными v2rayN из {0}? v2rayN закроется и перестанет запускаться при входе в систему, его файлы не изменятся. Текущие настройки V2pn сохранятся в guiTemps.");
    public static string ImportConfirmYes => T("Move", "Перенести");
    public static string ImportCloseFirst => T("V2pn could not close v2rayN: it runs as administrator. Quit v2rayN from its tray icon and try again.", "V2pn не смог закрыть v2rayN: он запущен от администратора. Выйдите из v2rayN через значок в трее и попробуйте снова.");
    public static string ImportRestarting => T("Moving from v2rayN, V2pn restarts…", "Перенос из v2rayN, V2pn перезапускается…");
    public static string ImportRestartFailed => T("V2pn could not restart. Close it and start it again to finish the move.", "V2pn не смог перезапуститься. Закройте его и запустите снова, перенос завершится при запуске.");
    public static string ImportDone => T("Moved from v2rayN: servers {0}, subscriptions {1}.", "Перенесено из v2rayN: серверов {0}, подписок {1}.");

    // The first-run card and the menu, for every client.
    public static string ImportTitle => T("Move your servers to V2pn", "Перенесите серверы в V2pn");
    public static string ImportLead => T("Found on this computer. The apps themselves stay as they are.", "Нашлись на этом компьютере. Сами приложения останутся как были.");
    public static string ImportFoundV2rayN => T("with routing and settings", "с маршрутами и настройками");
    public static string ImportOrByHand => T("or add a server by hand", "или добавьте сервер вручную");
    public static string ImportMoreApps => T("{0} more", "Ещё {0}");
    // The three ways to add by hand share one row under the move, so their labels are short.
    public static string HandPaste => T("Paste link", "Вставить");
    public static string HandSubscription => T("Subscription", "Подписка");
    public static string HandQr => T("QR code", "QR-код");
    public static string ImportLockedShort => T("Encrypted: copy the link in Happ and paste it below", "Зашифровано: скопируйте ссылку в Happ и вставьте ниже");
    public static string ImportMoveFrom => T("Move from {0}", "Перенести из {0}");
    public static string ImportMoveSelected => T("Move to V2pn", "Перенести в V2pn");
    public static string ImportMenu => T("Move from another app", "Перенести из другого приложения");
    public static string ImportMenuSearching => T("Looking for apps…", "Поиск приложений…");
    public static string ImportMenuNoneFound => T("No other apps with servers found", "Других приложений с серверами не найдено");
    public static string ImportMenuFolder => T("Choose the app's folder…", "Указать папку приложения…");
    public static string ImportFolderTitle => T("The folder of the app to move from", "Папка приложения, из которого перенести серверы");
    public static string ImportFolderNothing => T("No subscriptions or servers V2pn can read were found in this folder.", "В этой папке не нашлось подписок и серверов, которые V2pn умеет читать.");
    public static string ImportForeignDone => T("Moved from {0}: {1}.", "Перенесено из {0}: {1}.");
    public static string ImportForeignNothingNew => T("Everything from {0} is already in V2pn.", "Всё из {0} уже есть в V2pn.");
    public static string ImportForeignSkipped => T("{0} of kinds V2pn does not support stayed behind.", "Не перенесено: {0}, V2pn такие не поддерживает.");
    public static string ImportLockedHint => T("Keeps its servers encrypted. Copy the subscription link in Happ and paste it into V2pn.", "Хранит серверы в зашифрованном виде. Скопируйте ссылку на подписку в Happ и вставьте её в V2pn.");
    public static string ImportForeignFailed => T("Couldn't move from {0}. The details are in the log.", "Не удалось перенести из {0}. Подробности в журнале.");

    /// <summary>"Move from 3 apps", with the plural form the language needs.</summary>
    public static string ImportMoveApps(int n)
    {
        if (Ru)
        {
            return n % 10 == 1 && n % 100 != 11 ? $"Перенести из {n} приложения" : $"Перенести из {n} приложений";
        }
        return n == 1 ? "Move from 1 app" : $"Move from {n} apps";
    }

    /// <summary>"2 subscriptions" with the plural form the language needs.</summary>
    public static string SubscriptionCount(int n)
    {
        if (Ru)
        {
            var (m10, m100) = (n % 10, n % 100);
            var word = m10 == 1 && m100 != 11 ? "подписка"
                : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? "подписки"
                : "подписок";
            return $"{n} {word}";
        }
        return n == 1 ? "1 subscription" : $"{n} subscriptions";
    }

    /// <summary>"2 subscriptions · 5 servers", leaving out what there is none of.</summary>
    public static string ImportCounts(int subscriptions, int servers)
    {
        var parts = new List<string>();
        if (subscriptions > 0)
        {
            parts.Add(SubscriptionCount(subscriptions));
        }
        if (servers > 0 || parts.Count == 0)
        {
            parts.Add(ServerCount(servers));
        }
        return string.Join(" · ", parts);
    }
}
