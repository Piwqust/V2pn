namespace v2rayN.Desktop;

// DNS settings and the full config template, in plain words.
public static partial class L
{
    public static string DnTabMain => T("Basics", "Основное");
    public static string DnTabRay => T("Custom DNS: Xray", "Свой DNS: Xray");
    public static string DnTabSingbox => T("Custom DNS: sing-box", "Свой DNS: sing-box");
    public static string DnTitle => T("DNS", "DNS");
    public static string DnHint => T("DNS finds a site's address by its name. The defaults suit most people.", "DNS находит адрес сайта по его имени. Большинству подходят настройки по умолчанию.");
    public static string DnDefault => T("Default", "По умолчанию");

    public static string DnGrpSites => T("Site addresses", "Адреса сайтов");
    public static string DnDirect => T("DNS for direct sites", "DNS для сайтов без VPN");
    public static string DnDirectHint => T("Looks up sites that open directly, such as Russian ones.", "Ищет адреса сайтов, которые открываются напрямую, например российских.");
    public static string DnRemote => T("DNS for sites through the VPN", "DNS для сайтов через VPN");
    public static string DnRemoteHint => T("Looks up sites that open through the VPN, asking via the server.", "Ищет адреса сайтов, которые открываются через VPN, и спрашивает через сервер.");
    public static string DnBootstrap => T("Bootstrap DNS", "Стартовый DNS");
    public static string DnBootstrapHint => T("Finds the addresses of the DNS servers above; only an IP address works.", "Находит адреса DNS-серверов выше; подходит только IP-адрес.");

    public static string DnGrpSpeed => T("Speed", "Скорость");
    public static string DnFakeIp => T("Open sites faster (Fake IP)", "Быстрее открывать сайты (Fake IP)");
    public static string DnFakeIpHint => T("Sites get a stand-in address at once, without waiting for DNS.", "Сайты сразу получают подставной адрес, без ожидания ответа DNS.");
    public static string DnFakeIpRange => T("Fake IP range", "Диапазон Fake IP");
    public static string DnFakeIpRangeHint => T("Stand-in addresses are taken from here.", "Из него берутся подставные адреса.");
    public static string DnFakeIpv6 => T("Fake IP for IPv6", "Fake IP для IPv6");
    public static string DnFakeIpv6Hint => T("Stand-in IPv6 addresses too; needs Fake IP on and IPv6 lookups allowed.", "Подставные адреса и для IPv6; нужны Fake IP и разрешённые IPv6-запросы.");
    public static string DnParallel => T("Ask DNS servers at once", "Спрашивать DNS-серверы одновременно");
    public static string DnParallelHint => T("The fastest answer wins.", "Используется самый быстрый ответ.");
    public static string DnStale => T("Answer from an old cache", "Отвечать из старого кэша");
    public static string DnStaleHint => T("Expired answers are used at once and refreshed in the background.", "Устаревшие ответы отдаются сразу и обновляются в фоне.");
    public static string DnHappy => T("IPv4 and IPv6 at once (Happy Eyeballs)", "IPv4 и IPv6 одновременно (Happy Eyeballs)");
    public static string DnHappyHint => T("Tries both and keeps the faster one; needs the UseIP strategy.", "Пробует оба и оставляет более быстрый; нужна стратегия UseIP.");

    public static string DnGrpLookup => T("How addresses are looked up", "Как узнавать адреса");
    public static string DnLookupDirect => T("For direct connections", "Для прямых соединений");
    public static string DnLookupDirectHint => T("Empty or AsIs: the system DNS, otherwise the built-in one.", "Пусто или AsIs — системный DNS, иначе встроенный.");
    public static string DnLookupProxy => T("For connections through the VPN", "Для соединений через VPN");
    public static string DnLookupProxyHint => T("Empty or AsIs: the server's DNS, otherwise the built-in one.", "Пусто или AsIs — DNS сервера, иначе встроенный.");
    public static string DnLookupDial => T("For the VPN server's own address", "Для адреса самого VPN-сервера");
    public static string DnLookupDialHint => T("Better leave empty: it can cause routing loops.", "Лучше оставить пустым: возможны петли маршрутизации.");

    public static string DnGrpFilters => T("Filters", "Фильтры");
    public static string DnBlockHttps => T("Block HTTPS records (SVCB)", "Блокировать HTTPS-записи (SVCB)");
    public static string DnBlockHttpsHint => T("Turns off ECH and HTTP/3 checks; always on in Xray.", "Отключает проверки ECH и HTTP/3; в Xray включено всегда.");
    public static string DnBlockIpv6 => T("Don't look up IPv6 addresses", "Не искать IPv6-адреса");
    public static string DnBlockIpv6Hint => T("DNS returns only IPv4 addresses.", "DNS будет возвращать только IPv4-адреса.");
    public static string DnExpected => T("Check answers for direct sites", "Проверять ответы для прямых сайтов");
    public static string DnExpectedHint => T("Keeps only addresses from the expected country.", "Оставляет только адреса из ожидаемой страны.");

    public static string DnGrpHosts => T("Hosts", "Файл hosts");
    public static string DnSysHosts => T("Use the system hosts file", "Учитывать системный файл hosts");
    public static string DnSysHostsHint => T("Entries from the system's hosts file apply too.", "Записи из системного файла hosts тоже действуют.");
    public static string DnCommonHosts => T("Known DNS server addresses", "Известные адреса DNS-серверов");
    public static string DnCommonHostsHint => T("Popular DNS servers work without an extra lookup.", "Популярные DNS-серверы работают без лишнего запроса.");
    public static string DnHosts => T("Your entries", "Свои записи");
    public static string DnHostsHint => T("One per line: a name, then an address.", "По одной на строку: имя, затем адрес.");

    public static string DnUseRay => T("Use my DNS for Xray", "Использовать свой DNS для Xray");
    public static string DnUseSingbox => T("Use my DNS for sing-box", "Использовать свой DNS для sing-box");
    public static string DnUseHint => T("Replaces the Basics tab for this core.", "Заменяет настройки вкладки «Основное» для этого ядра.");
    public static string DnReady => T("Default config", "Готовая настройка");
    public static string DnReadyHint => T("Fills both fields with the standard DNS config.", "Заполняет оба поля стандартной настройкой DNS.");
    public static string DnInsert => T("Insert", "Вставить");
    public static string DnFormatDoc => T("Format reference", "Описание формата");
    public static string DnProxyMode => T("For proxy mode", "Для режима прокси");
    public static string DnTunMode => T("For TUN mode", "Для режима TUN");
    public static string DnDirectLookup => T("Direct connections lookup", "Адреса прямых соединений");
    public static string DnDefaultLookup => T("Default lookup", "Адреса по умолчанию");
    public static string DnServerDns => T("DNS for server addresses", "DNS для адресов серверов");

    public static string DnFcTitle => T("Your own core config", "Своя конфигурация ядра");
    public static string DnFcHint => T("For experts; you don't need it. When on, V2pn ignores its own core, DNS and routing settings.", "Для опытных, обычно это не нужно. Если включить, V2pn не применяет свои настройки ядра, DNS и маршрутизации.");
    public static string DnFcUseRay => T("Use my template for Xray", "Использовать свой шаблон для Xray");
    public static string DnFcUseSingbox => T("Use my template for sing-box", "Использовать свой шаблон для sing-box");
    public static string DnFcUseHint => T("V2pn adds only server connections to your template.", "V2pn добавит в шаблон только подключения к серверам.");
    public static string DnFcHowItWorks => T("How it works", "Как это работает");
    public static string DnFcProxyOnly => T("Only proxy servers", "Только прокси-серверы");
    public static string DnFcProxyOnlyHint => T("Direct and block exits are not added to the template.", "Выходы «напрямую» и «блокировать» в шаблон не добавляются.");
    public static string DnFcDetour => T("Connect through", "Подключаться через");
    public static string DnFcDetourHint => T("Servers go through the exit with this tag in your template.", "Серверы подключаются через выход с этим тегом из шаблона.");
    public static string DnFcProxyMode => T("Template for proxy mode", "Шаблон для режима прокси");
    public static string DnFcTunMode => T("Template for TUN mode", "Шаблон для режима TUN");
}
