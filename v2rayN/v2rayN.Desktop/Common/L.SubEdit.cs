namespace v2rayN.Desktop;

// Subscription editor: the extra fields behind "Show more fields", in plain words.
public static partial class L
{
    public static string GrpMore => T("More", "Дополнительно");
    public static string SeChoose => T("Choose", "Выбрать");
    public static string SeMoreUrl => T("Extra links", "Дополнительные ссылки");
    public static string SeMoreUrlHint => T("Servers from these links join this subscription. One link per line.", "Серверы из этих ссылок добавятся в эту же подписку. По одной ссылке на строку.");
    public static string SeUa => T("User-Agent", "User-Agent");
    public static string SeUaHint => T("How V2pn introduces itself to the provider. Leave empty unless the provider asks.", "Как V2pn представляется провайдеру. Оставьте пустым, если провайдер не просит.");
    public static string SeHeaders => T("Request headers (JSON)", "Заголовки запроса (JSON)");
    public static string SeHeadersHint => T("For example {\"X-hwid\": \"my_device\"}. Only a few providers need this.", "Например {\"X-hwid\": \"my_device\"}. Нужно лишь некоторым провайдерам.");
    public static string SeFilter => T("Keep only servers named", "Оставлять только серверы");
    public static string SeFilterHint => T("A regular expression on the server name, for example Germany|Netherlands.", "Регулярное выражение по имени сервера, например Germany|Netherlands.");
    public static string SeConvert => T("Convert the format", "Конвертировать формат");
    public static string SeConvertHint => T("Only when the provider serves a Clash or sing-box file.", "Только если провайдер отдаёт файл Clash или sing-box.");
    public static string SePreResolve => T("Look up server addresses in advance", "Заранее узнавать адреса серверов");
    public static string SePreResolveHint => T("The DNS server to use for that. Empty: not needed.", "Через какой DNS-сервер. Пусто — не нужно.");
    public static string SePrev => T("First go through", "Сначала через сервер");
    public static string SePrevHint => T("A chain: traffic passes this server, then the subscription's.", "Цепочка: трафик идёт через этот сервер, потом через сервер подписки.");
    public static string SeNext => T("Exit through", "Выходить через сервер");
    public static string SeNextHint => T("A chain: the subscription's server first, then this one.", "Цепочка: сначала сервер подписки, потом этот.");
    public static string SeCore => T("Core", "Ядро");
    public static string SeCoreHint => T("Which core runs these servers. Empty: chosen automatically.", "Каким ядром запускать эти серверы. Пусто — автоматически.");
    public static string SePreSocks => T("SOCKS port for a custom core", "Порт SOCKS для своего ядра");
    public static string SePreSocksHint => T("Only needed with a custom core.", "Нужен только для своего ядра.");
    public static string SeSort => T("Position in the list", "Место в списке");
    public static string SeSortHint => T("Smaller numbers go higher.", "Чем меньше число, тем выше.");
    public static string SeMemo => T("Note", "Заметка");
}
