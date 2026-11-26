namespace v2rayN.Desktop;

// The panel the tray icon opens (Views/TrayPanel.axaml).
public static partial class L
{
    public static string TrayAllServersTip => T("All servers", "Все серверы");
    // Short names of the three routes for the panel's switch; the full ones are in its tooltips.
    public static string TrayRouteRussia => T("Except RU", "Кроме РФ");
    public static string TrayRouteBlocked => T("Blocked", "Блокировки");
    public static string TrayRouteAll => T("Everything", "Всё");
    public static string TrayFastest => T("Fastest", "Самый быстрый");
    public static string TrayCopied => T("Copied", "Скопировано");
    public static string TrayNoServers => T("No servers yet. Paste a link or a subscription.", "Серверов пока нет. Вставьте ссылку или подписку.");
}
