namespace v2rayN.Desktop;

// The sidebar's home tab, list order and server statistics.
public static partial class L
{
    public static string TabHome => T("Home", "Главная");
    public static string FastestAge => T("{0} · {1} · {2}", "{0} · {1} · {2}");
    // A stale result is tested again before connecting; the row says so before the click.
    public static string FastestRecheck => T("{0} · will recheck", "{0} · перепроверю");
    public static string FastestList => T("Fastest now", "Самые быстрые");
    public static string FastestListEmpty => T("Test ping to see which servers answer fastest.", "Проверьте пинг, чтобы увидеть самые быстрые серверы.");
    public static string ShowAllFavorites => T("Show all favorites", "Показать всё избранное");
    public static string MoreCount => T("{0} more", "ещё {0}");
    public static string ShowFewer => T("Show fewer", "Свернуть");

    // List order.
    public static string SortBy => T("Order", "Порядок");
    public static string SortName => T("By name", "По названию");
    public static string SortPing => T("By ping", "По пингу");
    public static string SortUsage => T("By use", "По использованию");
    public static string SortUsageOff => T("By use (turn on statistics first)", "По использованию (сначала включите статистику)");

    // Statistics.
    public static string Stats => T("Statistics", "Статистика");
    public static string StatsMore => T("All statistics", "Вся статистика");
    public static string StatsOffHint => T("V2pn can remember which servers you use and which ones answer. It stays on this computer.",
        "V2pn может запоминать, через какие серверы вы подключаетесь и какие из них отвечают. Данные остаются на этом компьютере.");
    public static string StatsEnable => T("Turn on statistics", "Включить статистику");
    public static string StatsEnableSub => T("Collected from now on", "Сбор начнётся с этого момента");
    public static string StatsEmpty => T("Nothing yet: connect to a server, and the time spent on it shows here.",
        "Пока пусто: подключитесь к серверу, и здесь появится, где вы проводите больше времени.");
    public static string StatsMostUsed => T("Most time", "Больше всего времени");
    public static string StatsMostReliable => T("Answer most often", "Чаще всего отвечают");
    public static string StatsReliableEmpty => T("Test ping a few times, and the servers that answer most often show here.",
        "Проверьте пинг несколько раз, и здесь появятся серверы, которые отвечают чаще остальных.");
    public static string StatsCountries => T("Countries", "Страны");
    public static string StatsDays => T("Last 14 days: {0}", "За 14 дней: {0}");
    public static string StatsDaysTip => T("Time connected each day", "Время подключения по дням");
    public static string StatsClear => T("Clear statistics", "Очистить статистику");
    public static string StatsClearConfirm => T("Clear all server statistics? This can't be undone.", "Очистить всю статистику серверов? Это действие нельзя отменить.");
    public static string StatsDisable => T("Turn off statistics", "Выключить статистику");
    public static string StatsDisableSub => T("What was collected stays until you clear it", "Собранное останется, пока вы его не очистите");
    public static string StatsAnswered => T("{0} of {1} answered · avg {2}", "{0} из {1} ответов · ср. {2}");
    public static string Percent(double share) => $"{share * 100:0}%";

    public static string Connections(int n)
    {
        if (Ru)
        {
            var (m10, m100) = (n % 10, n % 100);
            var word = m10 == 1 && m100 != 11 ? "подключение"
                : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? "подключения"
                : "подключений";
            return $"{n} {word}";
        }
        return n == 1 ? T("1 connection", "1 подключение") : string.Format(T("{0} connections", "{0} подключений"), n);
    }

    /// <summary>Time spent, rounded for a list: "12 h 40 min", "35 min", "< 1 min".</summary>
    public static string Spent(long seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalMinutes < 1)
        {
            return T("< 1 min", "< 1 мин");
        }
        if (t.TotalHours < 1)
        {
            return string.Format(T("{0} min", "{0} мин"), (int)t.TotalMinutes);
        }
        return t.Minutes == 0 || t.TotalHours >= 100
            ? string.Format(T("{0} h", "{0} ч"), (int)t.TotalHours)
            : string.Format(T("{0} h {1} min", "{0} ч {1} мин"), (int)t.TotalHours, t.Minutes);
    }
}
