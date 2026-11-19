namespace v2rayN.Desktop;

// The live pages: connections, proxy groups and the log, which still showed v2rayN's wording.
public static partial class L
{
    public static string LiveAutoRefresh => T("Auto refresh", "Автообновление");
    public static string LiveNeedsSingBox => T("This list fills in while V2pn is connected through the sing-box core.",
        "Список заполняется, когда V2pn подключён через ядро sing-box.");

    // Connections
    public static string ConnSearch => T("Search by address", "Поиск по адресу");
    public static string ConnCloseAll => T("Close all", "Закрыть все");
    public static string ConnCloseAllTip => T("Close every connection; apps open new ones on their own",
        "Закрыть все соединения — приложения откроют новые сами");
    public static string ConnClose => T("Close connection", "Закрыть соединение");
    public static string ConnColHost => T("Address", "Адрес");
    public static string ConnColApp => T("App", "Приложение");
    public static string ConnColType => T("Type", "Тип");
    public static string ConnColTime => T("Time", "Время");
    public static string ConnEmptyTitle => T("No connections yet", "Соединений пока нет");
    public static string ConnEmptyHint => T("They appear here once something goes through V2pn.",
        "Они появятся здесь, когда через V2pn что-нибудь пойдёт.");
    public static string ConnNoMatchTitle => T("Nothing found", "Ничего не нашлось");
    public static string ConnNoMatchHint => T("No connection has “{0}” in its address. Change or clear the search.",
        "Ни в одном адресе нет «{0}». Измените или очистите поиск.");
    public static string ConnPaused => T("Auto refresh is off: the list stays as it is.", "Автообновление выключено, список не меняется.");

    public static string ConnCount(int n)
    {
        if (Ru)
        {
            var (m10, m100) = (n % 10, n % 100);
            return m10 == 1 && m100 != 11 ? $"{n} соединение"
                : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? $"{n} соединения"
                : $"{n} соединений";
        }
        return n == 1 ? T("1 connection", "1 соединение") : string.Format(T("{0} connections", "{0} соединений"), n);
    }
}
