namespace v2rayN.Desktop;

// What a provider sets for its subscription with the headers Happ, INCY and v2RayTun already read: banners, the
// announcement's link, connecting at start, the order of its servers (ServiceLib/V2pn/V2pnSubDefaults.cs).
public static partial class L
{
    public static string SubAnnounceMore => T("More", "Подробнее");

    public static string SubStartConnect => T("Connect at start", "Подключаться при запуске");
    public static string SubStartConnectProvider => T("As the provider advises", "Как советует провайдер");
    public static string SubStartConnectOwn => T("I'll choose myself", "Сам выберу");
    public static string SubStartConnectLastUsed => T("The provider advises connecting to the last server you used when V2pn starts switched off.",
        "Провайдер советует подключаться к последнему серверу, если V2pn запускается выключенным.");
    public static string SubStartConnectLowestDelay => T("The provider advises connecting to the fastest server when V2pn starts switched off.",
        "Провайдер советует подключаться к самому быстрому серверу, если V2pn запускается выключенным.");
    public static string SubStartConnectRandom => T("The provider advises connecting to a random server when V2pn starts switched off.",
        "Провайдер советует подключаться к случайному серверу, если V2pn запускается выключенным.");

    public static string SubOrder => T("Server order", "Порядок серверов");
    public static string SubOrderProvider => T("As the provider advises", "Как советует провайдер");
    public static string SubOrderPing => T("Fastest first", "Быстрые сверху");
    public static string SubOrderName => T("By name", "По имени");
    public static string SubOrderAsIs => T("As in the subscription", "Как в подписке");
    public static string SubOrderAdvice => T("The provider advises: {0}.", "Провайдер советует: {0}.");

    public static string SubOnStartUpdate => T("When V2pn starts, it updates this subscription: the provider asks for it.",
        "При запуске V2pn обновляет эту подписку: так просит провайдер.");
    public static string SubOnStartPing => T("When V2pn starts, it tests this subscription's servers: the provider asks for it.",
        "При запуске V2pn проверяет серверы этой подписки: так просит провайдер.");
    public static string SubOnStartBoth => T("When V2pn starts, it updates this subscription and tests its servers: the provider asks for it.",
        "При запуске V2pn обновляет эту подписку и проверяет её серверы: так просит провайдер.");

    public static string AutoconnectToast => T("Connecting to {0}: {1} advises it at start", "Подключение к {0}: так советует {1} при запуске");
    public static string AutoconnectNone => T("{0} advises connecting at start, but none of its servers answered", "{0} советует подключаться при запуске, но ни один его сервер не ответил");
}
