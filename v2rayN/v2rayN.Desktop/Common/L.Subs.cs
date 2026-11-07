namespace v2rayN.Desktop;

// Subscriptions: what the provider's panel reports (traffic, expiry, links) and the self-filling name.
public static partial class L
{
    public static string SubNameHint => T("Optional: the provider's name fills in after the first update.", "Необязательно: название провайдера подставится после первого обновления.");
    public static string SubNameAuto => T("Filled in by the provider", "Подставится от провайдера");

    public static string TrafficOf => T("{0} of {1}", "{0} из {1}");
    public static string TrafficUsed => T("{0} used", "Израсходовано {0}");
    public static string TrafficUnlimited => T("Unlimited traffic", "Безлимитный трафик");
    public static string TrafficSplit => T("Uploaded {0} · downloaded {1}", "Отправлено {0} · получено {1}");
    public static string TrafficOut => T("Traffic used up", "Трафик закончился");
    public static string ValidUntil => T("until {0}", "до {0}");
    public static string HoursLeft => T("{0} h left", "осталось {0} ч");
    public static string SubExpired => T("Expired", "Истекла");
    public static string SubExpiredOn => T("Expired on {0}", "Истекла {0}");
    public static string SubNoEnd => T("No end date", "Бессрочно");

    public static string GrpProvider => T("From the provider", "От провайдера");
    public static string SubValidity => T("Valid until", "Действует до");
    public static string SubAnnounce => T("Message from the provider", "Сообщение провайдера");
    public static string SubSupport => T("Support", "Поддержка");
    public static string SubWebPage => T("Provider's page", "Страница провайдера");
    public static string SubInfoAsOf => T("Reported by the provider {0}", "Получено от провайдера {0}");
    public static string SubPanelInterval => T("The provider suggests every {0} h.", "Провайдер советует раз в {0} ч.");

    /// <summary>"3 days left" with the plural form the language needs.</summary>
    public static string DaysLeft(int n)
    {
        if (Ru)
        {
            var (m10, m100) = (n % 10, n % 100);
            return m10 == 1 && m100 != 11 ? $"остался {n} день"
                : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? $"осталось {n} дня"
                : $"осталось {n} дней";
        }
        return n == 1 ? T("1 day left", "остался 1 день") : string.Format(T("{0} days left", "осталось {0} дней"), n);
    }
}
