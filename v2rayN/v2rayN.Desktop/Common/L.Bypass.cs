namespace v2rayN.Desktop;

// "Обход блокировок": the settings page, the network check window and what the automatic search says.
public static partial class L
{
    public static string SetBypass => T("Blocking bypass", "Обход блокировок");
    public static string SetBypassHint => T(
        "For when Russian filtering blocks the connection: shows what exactly is blocked and tries ways around it. While it is off, V2pn connects as before.",
        "Нужен, когда в России блокируют подключение: показывает, что именно режут, и пробует способы обхода. Пока выключено, V2pn подключается как раньше.");
    public static string GrpBypassMain => T("Bypass", "Обход");
    public static string BypassEnable => T("Turn on the blocking bypass", "Включить обход блокировок");
    public static string BypassEnableHint => T(
        "The switches below only work while this one is on.",
        "Переключатели ниже работают, только пока включён этот.");
    public static string BypassCalm => T("Careful connections", "Бережные подключения");
    public static string BypassCalmHint => T(
        "Ping tests go a few servers at a time, and the core introduces itself as a browser. Many connections to one address at once get it frozen for minutes.",
        "Пинг проверяется по нескольку серверов за раз, а ядро представляется браузером. Если открыть много соединений к одному адресу сразу, его замораживают на несколько минут.");
    public static string BypassAuto => T("Find a way automatically", "Подбирать способ автоматически");
    public static string BypassAutoHint => T(
        "When a server stops answering or downloads freeze, V2pn checks the network and tries several ways of splitting the TLS handshake.",
        "Если сервер перестал отвечать или загрузки замерзают, V2pn проверит сеть и попробует несколько вариантов дробления TLS-рукопожатия.");
    public static string BypassPerNetwork => T("Remember for each network", "Запоминать для каждой сети");
    public static string BypassPerNetworkHint => T(
        "A way found on home Wi-Fi is not used on a phone's hotspot, and back.",
        "Способ, найденный для домашнего Wi-Fi, не включается на точке доступа с телефона, и наоборот.");
    public static string BypassDirect => T("Split the handshake of direct sites", "Дробить рукопожатие у прямых сайтов");
    public static string BypassDirectHint => T(
        "For sites that open past the VPN but load slowly because of the filter.",
        "Для сайтов, которые открываются без VPN, но тормозят из-за фильтра.");
    public static string GrpBypassTools => T("Tools", "Инструменты");
    public static string BypassCheckNetwork => T("Check the network", "Проверить сеть");
    public static string BypassCheckNetworkHint => T(
        "Shows what is blocked: the server's address, foreign sites, large downloads or QUIC.",
        "Покажет, что режут: адрес сервера, зарубежные сайты, большие загрузки или QUIC.");
    public static string BypassOpen => T("Open", "Открыть");
    public static string BypassForgetAll => T("Forget the ways found", "Забыть найденные способы");
    public static string BypassKept => T("Kept: {0}", "Сохранено: {0}");
    public static string BypassNoneKept => T("Nothing kept yet", "Пока ничего не сохранено");
    public static string BypassForget => T("Forget", "Забыть");

    public static string BypassButton => T("Blocking bypass", "Обход блокировок");
    public static string BypassTitle => T("Blocking bypass", "Обход блокировок");
    public static string BypassChecking => T("Checking the network…", "Проверяю сеть…");
    public static string BypassStepRussian => T("Russian sites", "Российские сайты");
    public static string BypassStepForeign => T("Foreign sites", "Зарубежные сайты");
    public static string BypassStepServer => T("Server port", "Порт сервера");
    public static string BypassStepQuic => T("QUIC (UDP)", "QUIC (UDP)");
    public static string BypassStepSmall => T("Through the server", "Через сервер");
    public static string BypassStepLarge => T("Large download through the server", "Большая загрузка через сервер");
    public static string BypassOk => T("works", "работает");
    public static string BypassFail => T("does not work", "не работает");
    public static string BypassFrozenAt => T("stops at {0} KB", "обрывается на {0} КБ");
    public static string BypassNotChecked => T("not checked", "не проверялось");
    public static string BypassTunNote => T(
        "In VPN mode direct checks would go through the tunnel, so only the connection itself was checked.",
        "В режиме VPN прямые проверки ушли бы в туннель, поэтому проверено только само подключение.");
    public static string BypassOffNote => T(
        "V2pn is not connected, so only the network and the server's port were checked.",
        "V2pn не подключён, поэтому проверены только сеть и порт сервера.");

    public static string VerdictFine => T("Nothing is blocked", "Блокировок не видно");
    public static string VerdictFineText => T(
        "The network works, and both small and large downloads go through the server.",
        "Сеть работает, через сервер проходят и маленькие, и большие загрузки.");
    public static string VerdictFineOffText => T(
        "The network works and the server's port opens. Connect to check the server itself.",
        "Сеть работает, порт сервера открывается. Подключитесь, чтобы проверить сам сервер.");
    public static string VerdictNoInternet => T("No internet", "Нет интернета");
    public static string VerdictNoInternetText => T(
        "Neither Russian nor foreign sites open. Check the cable, Wi-Fi or mobile data.",
        "Не открываются ни российские, ни зарубежные сайты. Проверьте кабель, Wi-Fi или мобильные данные.");
    public static string VerdictWhitelist => T("Looks like a whitelist", "Похоже на белые списки");
    public static string VerdictWhitelistText => T(
        "Only Russian sites open: the operator lets through approved addresses only. A server abroad cannot be reached directly; you need a server with an entry in Russia.",
        "Открываются только российские сайты: оператор пропускает лишь разрешённые адреса. До зарубежного сервера напрямую не достучаться, нужен сервер со входом в России.");
    public static string VerdictServerBlocked => T("The server's address is blocked", "Адрес сервера заблокирован");
    public static string VerdictServerBlockedText => T(
        "Foreign sites open, the server does not. Often the whole network of its hosting is blocked. Pick another server or ask the provider for a new address.",
        "Зарубежные сайты открываются, а сервер нет. Часто блокируют всю сеть хостинга. Выберите другой сервер или попросите у провайдера новый адрес.");
    public static string VerdictFreeze => T("Downloads freeze", "Загрузки замерзают");
    public static string VerdictFreezeText => T(
        "Small requests go through the server, larger ones stop after the first {0} KB. This is how the filter freezes connections. Splitting the handshake or a server with XHTTP helps.",
        "Маленькие запросы через сервер проходят, большие обрываются после первых {0} КБ. Так фильтр «замораживает» соединения. Помогает дробление рукопожатия или сервер с XHTTP.");
    public static string VerdictProxyCut => T("The connection is cut", "Соединение режут");
    public static string VerdictProxyCutText => T(
        "The server's port opens, but nothing gets through. Try finding a way, or another server.",
        "Порт сервера открывается, но данные не проходят. Попробуйте подобрать способ или другой сервер.");
    public static string VerdictUnknown => T("No clear answer", "Однозначного ответа нет");
    public static string VerdictUnknownText => T(
        "The checks disagree. See the details below or check again in a minute.",
        "Проверки расходятся. Посмотрите подробности ниже или проверьте ещё раз через минуту.");
    public static string BypassQuicCut => T(
        "QUIC (UDP) is cut on this network: Hysteria2, TUIC and WireGuard servers will not work here.",
        "В этой сети режут QUIC (UDP): серверы Hysteria2, TUIC и WireGuard здесь работать не будут.");

    public static string BypassTune => T("Find a way", "Подобрать способ");
    public static string BypassTuneStop => T("Stop", "Остановить");
    public static string BypassRetry => T("Check again", "Проверить снова");
    public static string BypassUseRussian => T("Connect via a server in Russia", "Подключиться через сервер в России");
    public static string BypassMakeChain => T("Chain through my server in Russia", "Цепочка через мой сервер в России");
    public static string BypassForgetServer => T("Forget this server's way", "Забыть способ для этого сервера");
    public static string BypassWayNow => T("For this server on this network: {0}.", "Для этого сервера в этой сети: {0}.");
    public static string BypassTuning => T("Finding a way: {0} ({1} of {2})…", "Подбираю способ: {0} ({1} из {2})…");
    public static string BypassFound => T("Found a way for this server: {0}. V2pn keeps it for this network.", "Нашёлся способ для этого сервера: {0}. V2pn запомнил его для этой сети.");
    public static string BypassFoundNone => T("The server works again without a way; the kept one is dropped.", "Сервер снова работает без обхода, сохранённый способ убран.");
    public static string BypassNotFound => T("None of the ways helped. Check the network or pick another server.", "Ни один способ не помог. Проверьте сеть или выберите другой сервер.");
    public static string BypassNothingToTry => T(
        "Splitting the handshake cannot help this server: it does not use TLS over TCP.",
        "Дробление рукопожатия этому серверу не поможет: он не использует TLS поверх TCP.");
    public static string BypassAutoVerdict => T("{0}. Details are in \"Blocking bypass\".", "{0}. Подробности — в окне «Обход блокировок».");

    public static string BypassWayName(string? id) => id switch
    {
        "fragment" => T("handshake split", "дробление рукопожатия"),
        "fragment-fine" => T("fine handshake split", "мелкое дробление рукопожатия"),
        "fragment-tcp" => T("first packets split", "дробление первых пакетов"),
        _ => T("no way", "без обхода"),
    };

    public static string GrpBypassServer => T("This server's settings", "Настройки этого сервера");
    public static string AdviceRealityRawTcp => T(
        "REALITY over plain TCP: since February 2026 such connections often freeze after ~16 KB. Ask the provider for XHTTP.",
        "REALITY поверх обычного TCP: с февраля 2026 года такие соединения часто замерзают после ~16 КБ. Попросите у провайдера XHTTP.");
    public static string AdviceBigBrandSni => T(
        "The server poses as a big foreign site ({0}). The filter compares the name with the address and cuts such connections.",
        "Сервер маскируется под крупный зарубежный сайт ({0}). Фильтр сверяет имя с адресом и режет такие соединения.");
    public static string AdviceNonStandardPort => T(
        "TLS on port {0}: real HTTPS lives on 443, another port stands out.",
        "TLS на порту {0}: настоящий HTTPS работает на 443, другой порт заметен.");
    public static string AdviceNoTls => T("No TLS: the protocol is seen as it is and is easy to block.", "Без TLS: протокол виден как есть, его легко заблокировать.");
    public static string AdviceKnownProtocol => T(
        "Plain WireGuard and Shadowsocks have been blocked across Russia since 2025.",
        "Обычные WireGuard и Shadowsocks блокируют по всей России с 2025 года.");
    public static string AdviceUdp => T("A UDP protocol: on mobile networks UDP and QUIC are often cut.", "Протокол на UDP: в мобильных сетях UDP и QUIC часто режут.");
    public static string AdviceEch => T("ECH is blocked in Russia, and the connection may fail because of it.", "ECH в России блокируют, из-за него соединение может не установиться.");
    public static string AdviceNone => T("Nothing in the settings stands out.", "В настройках ничего подозрительного.");

    public static string BypassRussianTesting => T("Checking servers in Russia…", "Проверяю серверы в России…");
    public static string BypassRussianNoneListed => T(
        "Your subscriptions have no servers with an entry in Russia. Ask the provider, or add your own server in Russia and build a chain.",
        "В подписках нет серверов со входом в России. Спросите у провайдера или добавьте свой сервер в России и соберите цепочку.");
    public static string BypassRussianNoneAnswered => T("None of the servers in Russia answered.", "Ни один сервер в России не ответил.");
    public static string BypassRussianConnected => T("Switched to {0}.", "Переключено на {0}.");
    public static string BypassChainLead => T(
        "The chain goes to your server in Russia first, then on to \"{0}\". If your server is not in the list, add it first.",
        "Цепочка сначала пойдёт на ваш сервер в России, потом на «{0}». Если вашего сервера нет в списке, сначала добавьте его.");
    public static string BypassChainPick => T("Server in Russia", "Сервер в России");
    public static string BypassChainCreate => T("Create and connect", "Создать и подключиться");
    public static string BypassChainMade => T("The chain is saved as a server: {0}.", "Цепочка сохранена как сервер: {0}.");
    public static string BypassChainFailed => Tk("Could not save the chain.",
        "Could not save the chain. Press “Create and connect” again, or pick another server in Russia.",
        "Не удалось сохранить цепочку. Нажмите «Создать и подключиться» ещё раз или выберите другой сервер в России.");
    public static string BypassChainNoServers => T("Add your server in Russia first: there is nothing to chain yet.", "Сначала добавьте свой сервер в России: пока не из чего собрать цепочку.");
    public static string BypassNoServer => T("Pick a server first.", "Сначала выберите сервер.");
}
