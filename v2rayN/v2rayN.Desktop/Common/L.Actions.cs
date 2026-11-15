namespace v2rayN.Desktop;

// A partner's buttons, its description of its servers, what it may do and what it closed
// (ServiceLib/V2pn/V2pnPartnerCatalog.cs, V2pnPartnerChoice.cs, V2pnSubLock.cs).
public static partial class L
{
    // The buttons.
    public static string ActionsOf => T("{0}'s buttons", "Кнопки {0}");
    public static string ActionChecking => T("Checking servers…", "Проверяю серверы…");
    public static string ActionAsking => T("Asking {0}…", "Спрашиваю {0}…");
    public static string ActionConnected => T("{0}: {1}", "{0} — {1}");
    public static string ReasonFastest => T("the fastest", "самый быстрый");
    public static string ReasonSteadiest => T("the most stable", "самый стабильный");
    public static string ReasonLeastLoaded => T("the least loaded", "наименее загружен");
    public static string ReasonByPartner => T("by {0}'s check", "по проверке {0}");
    public static string ReasonNamed => T("{0}'s pick", "выбор {0}");
    public static string ReasonFastestIn => T("the fastest in {0}", "самый быстрый в группе {0}");
    public static string ActionNoAnswer => T("None of {0}'s servers here answered", "Ни один подходящий сервер {0} не ответил");
    public static string ActionNoServers => T("{0} has no servers for this button here", "У {0} нет серверов для этой кнопки");
    public static string ActionNoLink => T("{0} did not give a link for this", "{0} не дал ссылку для этого");
    public static string ActionPartnerSilent => T("{0} did not answer", "{0} не ответил");
    public static string ActionUpdating => T("Updating {0}…", "Обновляю {0}…");

    // check.services
    public static string ServicesTitle => T("What opens through V2pn", "Что открывается через V2pn");
    public static string ServicesChecking => T("Checking YouTube, Telegram and ChatGPT…", "Проверяю YouTube, Telegram и ChatGPT…");
    public static string ServicesOpens => T("opens · {0} ms", "открывается · {0} мс");
    public static string ServicesFails => T("does not open", "не открывается");
    public static string ServicesOff => T("Connect first: the check goes through the connection.", "Сначала подключитесь: проверка идёт через подключение.");
    public static string ServicesNote => T("Through the connection and your routing, as a browser would go.", "Через подключение и ваши маршруты, как пошёл бы браузер.");

    // info.card: the partner's words in a frame with its name.
    public static string CardFrom => T("From {0}", "От {0}");
    public static string CardClose => T("Close", "Закрыть");

    // Its servers in the list.
    public static string GroupOther => T("Other", "Другие");
    public static string ServerUnnamed => T("Server", "Сервер");

    // What the partner's certificate allows, in the subscription window.
    public static string PartnerMay => T("{0} may: {1}.", "{0} может: {1}.");
    public static string PartnerMayLittle => T("{0} shows its name and links only.", "{0} показывает только имя и ссылки.");
    public static string GrantEdition => T("its look", "оформление");
    public static string GrantAccount => T("plan details", "данные тарифа");
    public static string GrantActions => T("its own buttons", "свои кнопки");
    public static string GrantLock => T("closed settings", "закрытые настройки");

    // Closed settings (lock).
    public static string LockedBy => T("Settings closed by {0}", "Настройки закрыл провайдер {0}");
    public static string LockedHint => T("Server addresses are hidden and cannot be shared or exported. You can always connect, pick any of its servers, delete the subscription and go back to plain V2pn.",
        "Адреса серверов скрыты, их нельзя отправить или выгрузить. Подключаться, выбирать любой его сервер, удалить подписку и вернуться в обычный V2pn можно всегда.");
    public static string LockedUrl => T("The link is hidden by {0}", "Ссылку скрыл провайдер {0}");
    public static string LockedChoice => T("Set by {0}", "Задал провайдер {0}");
    public static string AddressHidden => T("address hidden", "адрес скрыт");
    public static string LockedMenu => T("Closed by {0}", "Закрыл провайдер {0}");
}
