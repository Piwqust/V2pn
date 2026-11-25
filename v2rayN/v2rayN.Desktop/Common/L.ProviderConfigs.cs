namespace v2rayN.Desktop;

// Subscriptions that answer with whole configs (ServiceLib/V2pn/V2pnProviderConfigs.cs, V2pnProviderRouting.cs).
public static partial class L
{
    public static string ProtocolGroup => T("Group", "Группа");
    public static string ProtocolCustom => T("Config file", "Файл конфигурации");

    public static string SubProviderRules => T("Provider's rules", "Правила провайдера");
    public static string SubProviderRulesHint => T(
        "Sites and apps go the way the provider set in its configs instead of your routing in V2pn. What you pinned stays as it is.",
        "Сайты и программы идут так, как провайдер задал в своих конфигах, а не по вашим маршрутам в V2pn. Закреплённое вами не меняется.");
}
