namespace v2rayN.Desktop;

// Partners (providers with a tab of their own in the title bar for the people who came from them) and the v2pn:// links
// of their sites.
public static partial class L
{
    public static string LinkAddTitle => T("Add a subscription", "Добавить подписку");
    public static string LinkAddBody => T("Add the subscription «{0}» from {1}?\n\nV2pn downloads its server list. The provider does not see your traffic.",
        "Добавить подписку «{0}» с {1}?\n\nV2pn скачает список серверов. Провайдер не видит ваш трафик.");
    public static string LinkAddYes => T("Add", "Добавить");
    public static string LinkAddNo => T("Cancel", "Отмена");
    public static string LinkAlreadyAdded => T("The subscription «{0}» is already in V2pn.", "Подписка «{0}» уже есть в V2pn.");
    public static string LinkAddBodyPartner => T(
        "Add the subscription «{0}» from {1}?\n\n{2} is a V2pn partner: it is on V2pn's list of partners. The servers come into {2}'s tab. {2} does not see your traffic.",
        "Добавить подписку «{0}» с {1}?\n\n{2} — партнёр V2pn: он есть в списке партнёров V2pn. Серверы появятся во вкладке {2}. {2} не видит ваш трафик.");
    // What a partner's API says about the subscription (its card and its plan on the stage).
    public static string PartnerUntil => T("Paid until {0}", "Оплачено до {0}");
    public static string PartnerDevices => T("Devices: {0}", "Устройств: {0}");
    public static string PartnerDevicesOf => T("Devices: {0} of {1}", "Устройств: {0} из {1}");

    // The partner's mark on its subscription in the list.
    public static string SubPartnerMarkTip => T("{0}, a V2pn partner", "{0} — партнёр V2pn");

    // The subscription window: the provider's own look of V2pn.
    public static string SubPartnerTitle => T("Provider's look", "Оформление провайдера");
    public static string SubPartnerConfirmed => T("{0}, confirmed by V2pn until {1}.", "{0}, подтверждено V2pn до {1}.");
    public static string SubPartnerChecking => T("{0} is a V2pn partner; V2pn is getting its look.", "{0} — партнёр V2pn, V2pn загружает его оформление.");
    public static string SubPartnerRefused => T(
        "{0} is a V2pn partner, but its look did not download or pass V2pn's check, so V2pn keeps its own for now. The servers work as usual.",
        "{0} — партнёр V2pn, но его оформление не загрузилось или не прошло проверку V2pn, поэтому пока V2pn обычный. Серверы работают как обычно.");

    // The partner whose tab was open went away; V2pn's tab is open now.
    public static string EditionGoneUnlinked => T(
        "{0}'s tab is closed: no subscription from {0} is left.",
        "Вкладка {0} закрыта: подписок от {0} больше нет.");
    public static string EditionGoneExpired => T(
        "{0}'s tab is closed: its confirmation by V2pn ran out. The connection is as it was.",
        "Вкладка {0} закрыта: подтверждение от V2pn истекло. Подключение не изменилось.");
    public static string EditionGoneRevoked => T(
        "{0}'s tab is closed: V2pn withdrew its confirmation of {0}. The connection is as it was.",
        "Вкладка {0} закрыта: V2pn отозвал подтверждение {0}. Подключение не изменилось.");

    // The Appearance page in a partner's tab.
    public static string ApPartnerNote => T(
        "In {0}'s tab, Connect and the links are in {0}'s colour.",
        "Во вкладке {0} кнопка подключения и ссылки в цвете {0}.");

    // The title bar's tabs: V2pn's and each partner's the person came from (MainWindow.Tabs.cs).
    public static string Tabs => T("Tabs", "Вкладки");
    public static string TabOpen => T("open", "открыта");
    public static string TabV2pnTip => T("V2pn: all your servers, no provider's look", "V2pn: все ваши серверы, без оформления провайдера");
    public static string TabPartnerTip => T("{0}: its servers, plan and look", "{0}: его серверы, тариф и оформление");

    // The list in a partner's tab before any of its subscriptions is here.
    public static string TabNoServers => T(
        "No servers from {0} yet. Sign in with {0} or add its subscription.",
        "Серверов от {0} пока нет. Войдите через {0} или добавьте его подписку.");
}
