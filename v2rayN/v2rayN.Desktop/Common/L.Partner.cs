namespace v2rayN.Desktop;

// Partners (providers that dress V2pn in their look) and the v2pn:// links of their sites.
public static partial class L
{
    public static string LinkAddTitle => T("Add a subscription", "Добавить подписку");
    public static string LinkAddBody => T("Add the subscription «{0}» from {1}?\n\nV2pn downloads its server list. If the provider is a V2pn partner, its look becomes available once its signature checks out. The provider does not see your traffic.",
        "Добавить подписку «{0}» с {1}?\n\nV2pn скачает список серверов. Если провайдер — партнёр V2pn, его оформление станет доступно после проверки подписи. Провайдер не видит ваш трафик.");
    public static string LinkAddYes => T("Add", "Добавить");
    public static string LinkAddNo => T("Cancel", "Отмена");
}
