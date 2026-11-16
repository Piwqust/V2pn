namespace v2rayN.Desktop;

// The IP card: the address sites see, where it is, whose network, and whether it gives V2pn away.
public static partial class L
{
    public static string IpButton => T("My IP", "Мой IP");
    public static string IpButtonTip => T("Full details of the address sites see", "Подробно об адресе, который видят сайты");
    public static string IpTitle => T("Your IP address", "Ваш IP-адрес");
    public static string IpHintVia => T("This is how sites see you through V2pn.", "Так вас видят сайты через V2pn.");
    public static string IpHintOff => T("V2pn is not connected: this is your own address.", "V2pn не подключён: это ваш собственный адрес.");
    public static string IpLoading => T("Looking up the address…", "Узнаём адрес…");
    public static string IpFailed => T("Could not find out the address: the lookup services did not answer.", "Не удалось узнать адрес: сервисы проверки не ответили.");
    public static string IpRetry => T("Try again", "Повторить");
    public static string IpRefresh => T("Refresh", "Обновить");
    public static string IpOpenBrowser => T("Open 2ip.io", "Открыть 2ip.io");
    public static string IpOpenBrowserTip => T("The same in the browser, plus DNS and WebRTC leak checks", "То же в браузере, плюс проверка утечек DNS и WebRTC");
    public static string IpCopy => T("Copy the address", "Скопировать адрес");
    public static string IpCopied => T("Copied", "Скопировано");
    public static string IpSources => T("Data: {0}. Asked only when you open this card.", "Данные: {0}. Запрашиваются только при открытии этого окна.");

    public static string IpHidden => T("Your address is hidden", "Ваш адрес скрыт");
    public static string IpLeak => T("This site sees your real address", "Этот сайт видит ваш настоящий адрес");
    public static string IpLeakHint => T(
        "The routing rules send this lookup service past V2pn, so the sites under the same rule see your own address too. Pick \"Everything through VPN\" to hide it everywhere.",
        "Правила маршрутизации пускают этот сервис мимо V2pn, поэтому и другие сайты под этим правилом видят ваш настоящий адрес. Чтобы скрыть его везде, выберите «Всё через VPN».");
    public static string IpSeenAsVpn => T("Sites can tell it is a VPN", "Сайты понимают, что это VPN");
    public static string IpSeenAsVpnHint => T(
        "Anti-fraud databases list this address as {0}. Some sites (banks, streaming) may ask for a captcha or refuse.",
        "В антифрод-базах этот адрес отмечен как {0}. Некоторые сайты (банки, стриминги) могут показать капчу или отказать.");
    public static string IpSeenAsHome => T("Looks like an ordinary address", "Похож на обычный адрес");
    public static string IpRisk => T("risk {0}/100", "риск {0}/100");

    public static string IpGrpPlace => T("Location", "Местоположение");
    public static string IpGrpNetwork => T("Network", "Сеть");
    public static string IpGrpDirect => T("Without V2pn", "Без V2pn");
    public static string IpCountry => T("Country", "Страна");
    public static string IpRegion => T("Region", "Регион");
    public static string IpCity => T("City", "Город");
    public static string IpPostal => T("Postal code", "Индекс");
    public static string IpCoordinates => T("Coordinates", "Координаты");
    public static string IpCoordinatesTip => T("Approximate: the network's location, not yours. Opens the map.", "Приблизительно: это место сети, а не ваше. Откроет карту.");
    public static string IpTimeZone => T("Time zone", "Часовой пояс");
    public static string IpTimeNow => T("now {0}", "сейчас {0}");
    public static string IpIsp => T("Provider", "Провайдер");
    public static string IpOrg => T("Network owner", "Владелец сети");
    public static string IpAsn => T("AS number", "Номер AS");
    public static string IpDomain => T("Domain", "Домен");
    public static string IpVersion => T("Protocol", "Протокол");
    public static string IpKind => T("Address type", "Тип адреса");
    public static string IpAddress => T("Address", "Адрес");
    public static string IpDirectTunNote => T(
        "In VPN mode every request goes through the tunnel, so your own address is not shown.",
        "В режиме VPN все запросы идут через туннель, поэтому ваш собственный адрес не показан.");
    public static string IpDirectFailed => T("Could not find out your own address.", "Не удалось узнать ваш собственный адрес.");

    /// <summary>proxycheck.io's address types in plain words.</summary>
    public static string IpKindName(string? kind) => kind?.ToLowerInvariant() switch
    {
        null or "" => "—",
        "vpn" => "VPN",
        "hosting" => T("hosting / data centre", "хостинг / дата-центр"),
        "residential" => T("home connection", "домашний интернет"),
        "business" => T("business network", "сеть организации"),
        "wireless" => T("mobile network", "мобильная сеть"),
        "tor" => "Tor",
        "compromised server" => T("compromised server", "взломанный сервер"),
        "public proxy" or "socks" or "socks4" or "socks5" or "http" or "https" => T("public proxy", "публичный прокси"),
        _ => kind,
    };
}
