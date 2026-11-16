namespace v2rayN.Desktop;

// A partner's card in the sidebar and its plan on the stage (Views/Editions).
public static partial class L
{
    public static string SceneRenew => T("Renew", "Продлить");
    public static string SceneSupport => T("Support", "Поддержка");

    /// <summary>"days" after a number, in the plural form the language needs.</summary>
    public static string DaysWord(int n)
    {
        if (Ru)
        {
            var (m10, m100) = (n % 10, n % 100);
            return m10 == 1 && m100 != 11 ? "день" : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? "дня" : "дней";
        }
        return n == 1 ? "day" : "days";
    }

    /// <summary>"days left" under the big number of the plan on the stage.</summary>
    public static string DaysLeftWord(int n)
    {
        if (Ru)
        {
            var (m10, m100) = (n % 10, n % 100);
            return m10 == 1 && m100 != 11 ? "день остался" : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? "дня осталось" : "дней осталось";
        }
        return n == 1 ? "day left" : "days left";
    }

    public static string PartnerCardTip => T("{0}: your subscription", "{0}: ваша подписка");
}
