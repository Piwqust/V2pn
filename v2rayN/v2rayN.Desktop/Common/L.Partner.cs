namespace v2rayN.Desktop;

// Partners (providers whose colours V2pn wears while their server is selected) and the v2pn:// links of their sites.
public static partial class L
{
    public static string LinkAddTitle => T("Add a subscription", "Добавить подписку");
    public static string LinkAddBody => T("Add the subscription «{0}» from {1}?\n\nV2pn downloads its server list. If the provider is a V2pn partner and its signature checks out, V2pn wears its colours while one of its servers is selected. The provider does not see your traffic.",
        "Добавить подписку «{0}» с {1}?\n\nV2pn скачает список серверов. Если провайдер — партнёр V2pn и его подпись верна, V2pn будет в его цветах, пока выбран его сервер. Провайдер не видит ваш трафик.");
    public static string LinkAddYes => T("Add", "Добавить");
    public static string LinkAddNo => T("Cancel", "Отмена");
    public static string LinkAlreadyAdded => T("The subscription «{0}» is already in V2pn.", "Подписка «{0}» уже есть в V2pn.");
    public static string LinkAddBodyPartner => T(
        "Add the subscription «{0}» from {1}?\n\n{2} is a V2pn partner: V2pn checked its certificate. The servers come into the list, and while one of them is selected V2pn wears {2}'s colours. {2} does not see your traffic.",
        "Добавить подписку «{0}» с {1}?\n\n{2} — партнёр V2pn: V2pn проверил его сертификат. Серверы появятся в списке, и пока выбран один из них, V2pn будет в цветах {2}. {2} не видит ваш трафик.");
    // What a partner's API says about the subscription (its card and its plan on the stage).
    public static string PartnerUntil => T("Paid until {0}", "Оплачено до {0}");
    public static string PartnerDevices => T("Devices: {0}", "Устройств: {0}");
    public static string PartnerDevicesOf => T("Devices: {0} of {1}", "Устройств: {0} из {1}");

    // The partner's mark on its subscription in the list.
    public static string SubPartnerMarkTip => T("{0}, a V2pn partner", "{0} — партнёр V2pn");

    // The subscription window: the provider's own look of V2pn.
    public static string SubPartnerTitle => T("Provider's look", "Оформление провайдера");
    public static string SubPartnerConfirmed => T("{0}, confirmed by V2pn until {1}.", "{0}, подтверждено V2pn до {1}.");
    public static string SubPartnerChecking => T("The provider offers its look; V2pn is checking its signature.", "Провайдер предлагает своё оформление, V2pn проверяет подпись.");
    public static string SubPartnerRefused => T(
        "The provider offers its look, but it did not pass V2pn's check, so V2pn keeps its own. The servers work as usual.",
        "Провайдер предлагает своё оформление, но оно не прошло проверку V2pn, поэтому V2pn остаётся обычным. Серверы работают как обычно.");

    // The partner whose colours V2pn wore went away; plain V2pn is back.
    public static string EditionGoneUnlinked => T(
        "{0}'s look is off: no subscription comes from {0} any more.",
        "Оформление {0} выключено: подписок от {0} больше нет.");
    public static string EditionGoneExpired => T(
        "{0}'s look is off: its confirmation by V2pn ran out. The connection is as it was.",
        "Оформление {0} выключено: подтверждение от V2pn истекло. Подключение не изменилось.");
    public static string EditionGoneRevoked => T(
        "{0}'s look is off: V2pn withdrew its confirmation of {0}. The connection is as it was.",
        "Оформление {0} выключено: V2pn отозвал подтверждение {0}. Подключение не изменилось.");

    // The Appearance page while a partner's server is selected.
    public static string ApPartnerNote => T(
        "While a server from {0} is selected, Connect and the links are in {0}'s colour.",
        "Пока выбран сервер {0}, кнопка подключения и ссылки в цвете {0}.");
}
