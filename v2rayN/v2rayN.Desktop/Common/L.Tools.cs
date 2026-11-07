namespace v2rayN.Desktop;

// Server group / chain, subscription list, hotkeys, backup and updates, in plain words.
public static partial class L
{
    public static string TlColName => T("Name", "Название");
    public static string TlColType => T("Type", "Тип");
    public static string TlColAddress => T("Address", "Адрес");

    public static string TlGroupTitle => T("Server group", "Группа серверов");
    public static string TlGroupHint => T("V2pn picks a server from the group by itself, for example the fastest one.", "V2pn сам выбирает сервер из группы, например самый быстрый.");
    public static string TlChainTitle => T("Server chain", "Цепочка серверов");
    public static string TlChainHint => T("Traffic goes through every server of the list in turn.", "Трафик идёт через все серверы списка по очереди.");
    public static string TlGrpMain => T("Main", "Основное");
    public static string TlGroupName => T("Name", "Название");
    public static string TlGroupNamePlaceholder => T("Fastest", "Самый быстрый");
    public static string TlPolicy => T("How to pick a server", "Как выбирать сервер");
    public static string TlPolicyHint => T("\"Fastest\" suits almost everyone.", "«Самый быстрый» подходит почти всегда.");
    public static string TlPolicyFastest => T("Fastest", "Самый быстрый");
    public static string TlPolicyFallback => T("First that works", "Первый рабочий");
    public static string TlPolicyRandom => T("Random", "Случайный");
    public static string TlPolicyRoundRobin => T("Take turns", "По очереди");
    public static string TlPolicyStable => T("Most stable", "Самый стабильный");
    public static string TlCore => T("Core", "Ядро");
    public static string TlCoreHint => T("What runs the group. Xray suits most cases.", "Чем запускать группу. Обычно подходит Xray.");
    public static string TlGrpServers => T("Servers", "Серверы");
    public static string TlTabFromSub => T("From a subscription", "Из подписки");
    public static string TlTabByHand => T("Picked by hand", "Выбранные вручную");
    public static string TlTabResult => T("Final list", "Итоговый список");
    public static string TlFromSub => T("Subscription", "Подписка");
    public static string TlFromSubHint => T("All its servers join the group, new ones too.", "Все её серверы войдут в группу, и новые тоже.");
    public static string TlFilter => T("Only servers named", "Только серверы с именем");
    public static string TlFilterHint => T("A regular expression on the server name.", "Регулярное выражение по имени сервера.");
    public static string TlAddServers => T("Add servers", "Добавить серверы");
    public static string TlRemoveServers => T("Remove", "Убрать");
    public static string TlByHandHint => T("Right-click a server to move it up or down.", "Чтобы передвинуть сервер, щёлкните по нему правой кнопкой.");

    public static string TlSubsTitle => T("Subscriptions", "Подписки");
    public static string TlSubsHint => T("A subscription is a link from your provider: V2pn downloads servers from it and keeps them up to date.", "Подписка — ссылка от провайдера: V2pn скачивает по ней серверы и обновляет их.");
    public static string TlSubAdd => T("Add", "Добавить");
    public static string TlSubEdit => T("Edit", "Изменить");
    public static string TlSubShare => T("Share", "Поделиться");
    public static string TlSubDelete => T("Delete", "Удалить");
    public static string TlSubClose => T("Close", "Закрыть");
    public static string TlColLink => T("Link", "Ссылка");
    public static string TlColAutoUpdate => Tk("Auto-update", "Update with all", "Обновлять со всеми");

    public static string TlKeysTitle => T("Hotkeys", "Горячие клавиши");
    public static string TlKeysHint => T("Click a field and press the keys you want. They work after V2pn restarts.", "Щёлкните по полю и нажмите нужное сочетание клавиш. Заработает после перезапуска V2pn.");
    public static string TlGrpActions => T("Actions", "Действия");
    public static string TlKeyShow => T("Show the V2pn window", "Показать окно V2pn");
    public static string TlKeyProxyOff => T("Turn the system proxy off", "Выключить системный прокси");
    public static string TlKeyProxyOn => T("Turn the system proxy on", "Включить системный прокси");
    public static string TlKeyProxyKeep => T("Stop managing the system proxy", "Не управлять системным прокси");
    public static string TlKeyPac => T("System proxy by site list (PAC)", "Системный прокси по списку сайтов (PAC)");
    public static string TlKeysClear => T("Clear all", "Очистить все");

    public static string TlBackupTitle => T("Backup", "Резервная копия");
    public static string TlBackupHint => T("Settings, servers and subscriptions in one file: to move to another computer or to recover after a reinstall.", "Настройки, серверы и подписки в одном файле — для переноса на другой компьютер или после переустановки.");
    public static string TlGrpThisPc => T("On this computer", "На этом компьютере");
    public static string TlSaveToFile => T("Save to a file", "Сохранить в файл");
    public static string TlSaveToFileHint => T("Makes a .zip archive with everything.", "Создаст архив .zip со всеми настройками.");
    public static string TlSaveFile => T("Save…", "Сохранить…");
    public static string TlRestoreFromFile => T("Restore from a file", "Восстановить из файла");
    public static string TlRestoreFromFileHint => T("The current settings are replaced by the ones in the archive.", "Текущие настройки заменятся настройками из архива.");
    public static string TlChooseFile => T("Choose a file…", "Выбрать файл…");
    public static string TlGrpCloud => T("In the cloud (WebDAV)", "В облаке (WebDAV)");
    public static string TlCloudConnect => T("Cloud connection", "Подключение к облаку");
    public static string TlCloudConnectHint => T("Your WebDAV server address, login and password.", "Адрес вашего сервера WebDAV, логин и пароль.");
    public static string TlSetUp => T("Set up", "Настроить");
    public static string TlSaveToCloud => T("Save to the cloud", "Сохранить в облако");
    public static string TlSaveToCloudHint => T("Uploads a copy to your WebDAV server.", "Загрузит копию на ваш сервер WebDAV.");
    public static string TlSaveNow => T("Save", "Сохранить");
    public static string TlRestoreFromCloud => T("Restore from the cloud", "Восстановить из облака");
    public static string TlRestoreFromCloudHint => T("Takes the copy from your WebDAV server and replaces the current settings.", "Возьмёт копию с сервера WebDAV и заменит текущие настройки.");
    public static string TlRestore => T("Restore", "Восстановить");
    public static string TlDavUrl => T("Server address", "Адрес сервера");
    public static string TlDavUser => T("Login", "Логин");
    public static string TlDavPassword => T("Password", "Пароль");
    public static string TlDavFolder => T("Folder", "Папка");
    public static string TlDavCheck => T("Check connection", "Проверить подключение");

    public static string TlUpdTitle => T("Updates", "Обновления");
    public static string TlUpdHint => T("Turn on what to check, then press Update.", "Отметьте, что проверять, и нажмите «Обновить».");
    public static string TlUpdViaVpn => T("Download through the VPN", "Скачивать через VPN");
    public static string TlUpdCheck => Tk("Check", "Check selected", "Проверить отмеченное");
    public static string TlUpdUpdate => Tk("Update", "Update selected", "Обновить отмеченное");
    public static string TlUpdBeta => T("Beta versions", "Бета-версии");
    public static string TlUpdApp => T("The V2pn app", "Приложение V2pn");
    public static string TlUpdXray => T("Xray core", "Ядро Xray");
    public static string TlUpdMihomo => T("mihomo core", "Ядро mihomo");
    public static string TlUpdSingBox => T("sing-box core", "Ядро sing-box");
    public static string TlUpdGeo => T("Country and site lists", "Списки стран и сайтов");
}
