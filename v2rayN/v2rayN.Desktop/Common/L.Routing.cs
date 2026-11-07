namespace v2rayN.Desktop;

// Routing editors ("Your rules"): the list of rule sets, one set, one rule.
public static partial class L
{
    // List of rule sets.
    public static string RtListTitle => T("Your rule sets", "Свои наборы правил");
    public static string RtListHint => T("The three modes in the main window cover most needs; here you build your own sets for special cases.",
        "Обычно хватает трёх режимов главного окна, а здесь собирают свои наборы для особых случаев.");
    public static string RtSetsTab => T("Rule sets", "Наборы");
    public static string RtNewSet => T("New set", "Новый набор");
    public static string RtAddBuiltin => T("Add the built-in sets", "Добавить стандартные наборы");
    public static string RtUseSet => T("Use this set", "Включить этот набор");
    public static string RtDelete => T("Delete", "Удалить");
    public static string RtSelectAll => T("Select all", "Выделить все");
    public static string RtColName => T("Name", "Название");
    public static string RtColRules => T("Rules", "Правил");
    public static string RtColOrder => T("Order", "Порядок");
    public static string RtColLink => T("Link", "Ссылка");
    public static string RtGrpDomains => T("Matching domains", "Сопоставление доменов");
    public static string RtDomainStrategy => T("How to match domains (Xray)", "Как сопоставлять домены (Xray)");
    public static string RtDomainStrategyHint => T("AsIs checks only the name; IPIfNonMatch also checks the site's IP address.",
        "AsIs смотрит только на имя, IPIfNonMatch — ещё и на IP-адрес сайта.");
    public static string RtDomainStrategySb => T("How to resolve domains (sing-box)", "Как сопоставлять домены (sing-box)");
    public static string RtDomainStrategySbHint => T("Which IP version to prefer. Empty: the default.", "Какую версию IP предпочитать. Пусто — по умолчанию.");
    public static string RtLearnMore => T("Learn more", "Подробнее");

    // One rule set.
    public static string RtSetTitle => T("Rule set", "Набор правил");
    public static string RtSetHint => T("Rules are checked from top to bottom; the first match decides where the traffic goes.",
        "Правила проверяются сверху вниз: первое подходящее решает, куда пойдёт трафик.");
    public static string RtGrpSet => T("Set", "Набор");
    public static string RtSetName => T("Name", "Название");
    public static string RtSetNamePlaceholder => T("My sites", "Мои сайты");
    public static string RtSetOrder => T("Position in the list", "Место в списке");
    public static string RtSetOrderHint => T("Smaller numbers go higher.", "Чем меньше число, тем выше.");
    public static string RtSetDomainStrategyHint => T("For this set only. Empty: the general setting.", "Только для этого набора. Пусто — как в общих настройках.");
    public static string RtSetUrl => T("Rules link", "Ссылка на правила");
    public static string RtSetUrlHint => T("Import → From the link downloads rules from here.", "Отсюда пункт «Импорт → По ссылке» скачивает правила.");
    public static string RtSetRuleset => T("sing-box rule-set file", "Файл rule-set для sing-box");
    public static string RtSetRulesetHint => T("Your own rule-set definitions for sing-box.", "Свои описания rule-set для sing-box.");
    public static string RtExample => T("Example", "Пример");
    public static string RtRulesTab => T("Rules", "Правила");
    public static string RtAddRule => T("Add rule", "Добавить правило");
    public static string RtImport => T("Import", "Импорт");
    public static string RtFromFile => T("From a file…", "Из файла…");
    public static string RtFromClipboard => T("From the clipboard", "Из буфера обмена");
    public static string RtFromUrl => T("From the link", "По ссылке");
    public static string RtCopySelected => T("Copy selected", "Скопировать выбранные");
    public static string RtMoveTop => T("Move to the top", "В самый верх");
    public static string RtMoveUp => T("Move up", "Выше");
    public static string RtMoveDown => T("Move down", "Ниже");
    public static string RtMoveBottom => T("Move to the bottom", "В самый низ");
    public static string RtColWhere => T("Where to", "Куда");
    public static string RtColMatch => T("Sites, addresses, programs", "Сайты, адреса, программы");
    public static string RtColApplies => T("Applies to", "Где действует");
    public static string RtColPort => T("Port", "Порт");
    public static string RtColProtocol => T("Protocol", "Протокол");
    public static string RtColEntry => T("Entry", "Вход");
    public static string RtColNetwork => T("Network", "Сеть");

    // One rule.
    public static string RtRuleTitle => T("Rule", "Правило");
    public static string RtRuleHint => T("Choose what to do and list the sites, addresses or programs it applies to.",
        "Выберите, что делать, и перечислите сайты, адреса или программы, к которым это относится.");
    public static string RtGrpAction => T("What to do", "Что делать");
    public static string RtAction => T("Send the traffic", "Куда направить трафик");
    public static string RtActionHint => T("Through the VPN, directly past it, or nowhere.", "Через VPN, напрямую в обход него или никуда.");
    public static string RtThroughVpn => T("Through VPN", "Через VPN");
    public static string RtDirect => T("Directly", "Напрямую");
    public static string RtBlock => T("Block", "Блокировать");
    public static string RtEnabled => T("Rule is on", "Правило включено");
    public static string RtEnabledHint => T("Turn off to keep the rule without using it.", "Выключите, чтобы правило сохранилось, но не работало.");
    public static string RtOutbound => T("A specific server or tag", "Конкретный сервер или тег");
    public static string RtOutboundHint => T("Used instead of the choice above: a server's name or an outbound tag.", "Вместо выбора выше: имя сервера или тег выхода.");
    public static string RtChooseServer => T("Choose a server", "Выбрать сервер");
    public static string RtGrpMatch => T("What it applies to", "К чему относится");
    public static string RtSites => T("Sites", "Сайты");
    public static string RtSitesHint => T("Domains or geosite lists, one per line.", "Домены или списки geosite, по одному на строку.");
    public static string RtAddresses => T("IP addresses", "IP-адреса");
    public static string RtAddressesHint => T("Addresses, subnets or geoip lists, one per line.", "Адреса, подсети или списки geoip, по одному на строку.");
    public static string RtPrograms => T("Programs", "Программы");
    public static string RtProgramsHint => T("Program file names, one per line.", "Имена файлов программ, по одному на строку.");
    public static string RtMatchNote => T("A line that starts with # is ignored.", "Строка с # в начале не учитывается.");
    public static string RtName => T("Name", "Название");
    public static string RtNameHint => T("Helps to find the rule in the list.", "Чтобы легко найти правило в списке.");
    public static string RtNamePlaceholder => T("Video", "Видео");
    public static string RtPort => T("Ports", "Порты");
    public static string RtPortHint => T("Only connections to these ports.", "Только соединения на эти порты.");
    public static string RtNetwork => T("Connection type", "Тип соединений");
    public static string RtNetworkHint => T("TCP, UDP or both. Empty: any.", "TCP, UDP или оба. Пусто — любые.");
    public static string RtProtocol => T("Protocol", "Протокол");
    public static string RtProtocolHint => T("Recognised from the traffic itself.", "Определяется по самому трафику.");
    public static string RtEntry => T("Where the traffic came in", "Откуда пришёл трафик");
    public static string RtEntryHint => T("tun is the VPN mode; socks, socks2 and socks3 are the proxy ports.", "tun — режим VPN; socks, socks2 и socks3 — порты прокси.");
    public static string RtRuleType => T("Applies to", "Где действует");
    public static string RtRuleTypeHint => T("Routing, DNS or both (ALL).", "Маршрутизация (Routing), DNS или оба (ALL).");
    public static string RtAutoSort => T("Sort the lists on save", "Сортировать списки при сохранении");
}
