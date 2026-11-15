namespace v2rayN.Desktop;

// A provider's theme (Views/Editions/ThemeScene.cs): its blocks and rows.
public static partial class L
{
    public static string ThemeConnection => T("Connection", "Подключение");
    public static string ThemeServer => T("Server", "Сервер");
    public static string ThemeMode => T("Mode", "Режим");
    public static string ThemeTraffic => T("Traffic", "Трафик");
    public static string ThemeNews => T("News from {0}", "Новости {0}");
    public static string ThemeMore => T("More", "Подробнее");
    public static string ThemePingValue => T("{0} ms", "{0} мс");
    public static string ThemeNoPing => T("Ping: —", "Пинг: —");
    public static string ThemeAnswering => T("{0} answering", "Отвечают {0}");

    /// <summary>A theme template's name, for the debug window and the guide's pictures.</summary>
    public static string ThemeTemplate(string template) => template switch
    {
        V2pnThemeTemplates.Center => T("Centre", "Центр"),
        V2pnThemeTemplates.Half => T("Half", "Половина"),
        V2pnThemeTemplates.Split => T("Split", "Две колонки"),
        V2pnThemeTemplates.List => T("List", "Список"),
        V2pnThemeTemplates.Map => T("Map", "Карта"),
        V2pnThemeTemplates.Compact => T("Card", "Карточка"),
        _ => template,
    };
}
