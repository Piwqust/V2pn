namespace v2rayN.Desktop;

// A provider's looks (Views/Editions/NeonScene.cs, the Pass card): their blocks and rows.
public static partial class L
{
    public static string ThemePingValue => T("{0} ms", "{0} мс");


    /// <summary>"days" after a number on the Pass card, in the plural form the language needs.</summary>
    public static string DaysWord(int n)
    {
        if (Ru)
        {
            var (m10, m100) = (n % 10, n % 100);
            return m10 == 1 && m100 != 11 ? "день" : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? "дня" : "дней";
        }
        return n == 1 ? "day" : "days";
    }

    public static string PassTraffic => T("Traffic", "Трафик");
    public static string SceneServersTitle => T("Servers", "Серверы");
    public static string ThemePingDown => T("down", "нет связи");
    public static string SceneMore => T("More", "Подробнее");
}
