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

    // The last update attempt (SubInfoItem.LastResult).
    public static string SubResultShort(EV2pnSubUpdateResult result) => result switch
    {
        EV2pnSubUpdateResult.Unreachable => T("site unreachable", "сайт недоступен"),
        EV2pnSubUpdateResult.Empty => T("no servers sent", "серверов не прислали"),
        EV2pnSubUpdateResult.Invalid => T("not a subscription", "это не подписка"),
        EV2pnSubUpdateResult.DeviceLimit => T("device limit reached", "лимит устройств"),
        EV2pnSubUpdateResult.DeviceNotAccepted => T("device not accepted", "устройство не принято"),
        _ => T("update failed", "не обновилась"),
    };

    public static string SubResultLong(EV2pnSubUpdateResult result, string? detail) => result switch
    {
        EV2pnSubUpdateResult.Unreachable when detail == SubscriptionHandler.SubTimeoutDetail =>
            T("The provider's site didn't answer in time.", "Сайт провайдера не ответил вовремя."),
        EV2pnSubUpdateResult.Unreachable => detail.IsNotEmpty()
            ? string.Format(T("The provider's site didn't answer ({0}).", "Сайт провайдера не ответил ({0})."), detail)
            : T("The provider's site didn't answer.", "Сайт провайдера не ответил."),
        EV2pnSubUpdateResult.Empty => T("The provider's site answered with no servers.", "Сайт провайдера ответил, но не прислал серверов."),
        EV2pnSubUpdateResult.Invalid => T("The provider's site answered, but not with servers. Check the link.",
            "Сайт провайдера ответил, но не списком серверов. Проверьте ссылку."),
        EV2pnSubUpdateResult.DeviceLimit => T("This subscription is already on as many devices as your plan allows. Remove a device you no longer use in your account with the provider (its bot or site), or ask the provider to raise the limit.",
            "Подписка уже работает на стольких устройствах, сколько позволяет тариф. Удалите устройство, которым не пользуетесь, в личном кабинете провайдера (в боте или на сайте) или попросите провайдера поднять лимит."),
        EV2pnSubUpdateResult.DeviceNotAccepted => T("The provider's panel did not accept this device. Ask the provider to allow it.",
            "Панель провайдера не приняла это устройство. Попросите провайдера разрешить его."),
        _ => detail.IsNotEmpty()
            ? string.Format(T("The update failed: {0}", "Обновление не удалось: {0}"), detail)
            : T("The update failed.", "Обновление не удалось."),
    };

    public static string SubLastAttempt => T("Last update {0}: {1}", "Последнее обновление {0}: {1}");
    public static string SubLastGood => T("The servers you have came {0} and still work for connecting.",
        "Сохранённые серверы получены {0}, ими можно пользоваться.");
    public static string SubLastGoodUnknown => T("The servers you have stay available.", "Сохранённые серверы остаются доступны.");
    public static string SubUpdateFailedOne => T("Couldn't update \"{0}\": {1}", "Не удалось обновить «{0}»: {1}");

    // Near Connect when the selected server's subscription is over, by the provider's own data.
    public static string SubExpiredNotice => T("{0}: the provider reports the subscription expired on {1}.", "{0}: по данным провайдера подписка истекла {1}.");
    public static string SubTrafficOutNotice => T("{0}: the provider reports the traffic is used up.", "{0}: по данным провайдера трафик закончился.");
    public static string SubUpdateNow => T("Update subscription", "Обновить подписку");

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
