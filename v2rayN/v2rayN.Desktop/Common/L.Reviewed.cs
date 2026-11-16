namespace v2rayN.Desktop;

// A partner's reviewed files: its routes, connection settings and chains (ServiceLib/V2pn/V2pnPartnerReview.cs).
public static partial class L
{
    // What the certificate allows, after "Kestrel may: ".
    public static string GrantRouting => T("its routes", "свои маршруты");
    public static string GrantTransport => T("connection settings", "настройки соединения");
    public static string GrantCore => T("chains and balancing", "цепочки и балансировку");

    // The mode in "What goes through the VPN".
    public static string RoutePartnerHint => T("{0} picked them for its servers; V2pn reviewed them.", "{0} подобрал их для своих серверов, V2pn их проверил.");
    public static string RoutePartnerOffered => T("{0} suggests its own routes: “{1}” in What goes through the VPN.",
        "{0} предлагает свои маршруты: «{1}» в меню «Что идёт через VPN».");

    // The subscription window.
    public static string SubReviewedTitle => T("Reviewed by V2pn", "Проверено V2pn");
    public static string ReviewedRouting => T("{0} routes", "Маршруты {0}");
    public static string ReviewedTransport => T("{0} connection settings", "Настройки соединения {0}");
    public static string ReviewedCore => T("{0} chains and balancing", "Цепочки и балансировка {0}");
    public static string ReviewedUsed => T("{0} are in use.", "{0} используются.");
    public static string ReviewedPickable => T("{0} can be picked in What goes through the VPN.", "{0}: можно выбрать в меню «Что идёт через VPN».");
    public static string ReviewedOff => T("{0} are turned off by you.", "{0} выключены вами.");
    public static string ReviewedChanged => T("{0} changed after the review and are not used.", "{0} изменились после проверки и не используются.");
    public static string ReviewedNot => T("{0} were not reviewed by V2pn and are not used.", "{0} не проверены V2pn и не используются.");
    public static string ReviewedInvalid => T("{0} don't fit this version of V2pn and are not used.", "{0} не подходят этой версии V2pn и не используются.");
    public static string SubPartnerConnHint => T("Fragmentation, chains and balancing for {0}'s servers, as V2pn reviewed them. Your other connections never use them.",
        "Фрагментация, цепочки и балансировка для серверов {0} в том виде, как их проверил V2pn. Другие подключения их не используют.");
}
