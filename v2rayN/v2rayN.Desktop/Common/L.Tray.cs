namespace v2rayN.Desktop;

// The panel a right click on the tray icon opens on Windows (Views/TrayPanel.axaml).
public static partial class L
{
    public static string TrayServers => T("Servers", "Серверы");
    public static string TrayAllServers => T("All", "Все");
    public static string TrayMethodSystem => T("System", "Системный");
    public static string TrayMethodPort => T("Port only", "Порт");
    public static string TrayMethodPac => T("PAC", "PAC");
    public static string TrayTilePaste => T("Paste", "Вставить");
    public static string TrayTileUpdate => T("Update", "Обновить");
    public static string TrayTileUpdateVpn => T("Via VPN", "Через VPN");
    public static string TrayTileTerminal => T("Terminal", "Терминал");
    public static string TrayTileCopied => T("Copied", "Скопировано");
    public static string TrayQuit => T("Quit", "Выйти");
    public static string TrayNoServers => T("No servers yet. Paste a link or a subscription.", "Серверов пока нет. Вставьте ссылку или подписку.");
}
