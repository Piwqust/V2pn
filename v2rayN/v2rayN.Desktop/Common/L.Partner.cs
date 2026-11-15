namespace v2rayN.Desktop;

// Partners (providers that dress V2pn in their look) and the v2pn:// links of their sites.
public static partial class L
{
    public static string LinkAddTitle => T("Add a subscription", "Добавить подписку");
    public static string LinkAddBody => T("Add the subscription «{0}» from {1}?\n\nV2pn downloads its server list. If the provider is a V2pn partner, its look becomes available once its signature checks out. The provider does not see your traffic.",
        "Добавить подписку «{0}» с {1}?\n\nV2pn скачает список серверов. Если провайдер — партнёр V2pn, его оформление станет доступно после проверки подписи. Провайдер не видит ваш трафик.");
    public static string LinkAddYes => T("Add", "Добавить");
    public static string LinkAddNo => T("Cancel", "Отмена");
    public static string LinkAlreadyAdded => T("The subscription «{0}» is already in V2pn.", "Подписка «{0}» уже есть в V2pn.");
    public static string LinkAddBodyPartner => T(
        "Add the subscription «{0}» from {1}?\n\n{2} is a V2pn partner: V2pn checked its certificate. The servers come into the list, and {2}'s look is there to open. {2} does not see your traffic.",
        "Добавить подписку «{0}» с {1}?\n\n{2} — партнёр V2pn: V2pn проверил его сертификат. Серверы появятся в списке, а оформление {2} можно будет открыть. {2} не видит ваш трафик.");
    // The edition switcher behind the name in the title bar.
    public static string EditionSwitchTip => T("Switch between V2pn and your providers' looks (Ctrl+1…9). The connection stays as it is.",
        "Переключение между V2pn и оформлением провайдеров (Ctrl+1…9). Подключение не прерывается.");
    public static string EditionOn => T("on", "на");
    public static string EditionLook => T("{0} look", "Вид {0}");
    // What a partner's API says about the subscription (its look's subscription block).
    public static string PartnerUntil => T("Paid until {0}", "Оплачено до {0}");
    public static string PartnerDevices => T("Devices: {0}", "Устройств: {0}");
    public static string PartnerDevicesOf => T("Devices: {0} of {1}", "Устройств: {0} из {1}");

    // The partner's mark on its subscription in the list.
    public static string SubPartnerMarkTip => T("{0}, a V2pn partner", "{0} — партнёр V2pn");

    // The window title and the tray in a partner's look.
    public static string EditionTitleOn => T("{0} on {1}", "{0} на {1}");
    public static string TrayEditions => T("Look", "Оформление");

    // The intro, once per partner, when its look first becomes available.
    public static string IntroTitle => T("V2pn in {0}'s look", "V2pn в оформлении {0}");
    public static string IntroBody => T(
        "{0} is a V2pn partner: its subscription offers its own look of V2pn, and V2pn checked its signature. The servers, the connection and the settings stay the same; only the look changes. Back to plain V2pn: the name in the title bar or Ctrl+1.",
        "{0} — партнёр V2pn: его подписка предлагает своё оформление V2pn, и V2pn проверил подпись. Серверы, подключение и настройки те же, меняется только вид. Вернуться к обычному V2pn — название в заголовке окна или Ctrl+1.");
    public static string IntroPrivacy => T(
        "{0} does not see your traffic and gets nothing about your computer from the look.",
        "{0} не видит ваш трафик и ничего не узнаёт о вашем компьютере через оформление.");
    public static string IntroOpen => T("Open {0}'s look", "Открыть оформление {0}");
    public static string IntroStay => T("Stay in plain V2pn", "Остаться в обычном V2pn");
    public static string IntroOpenLook => T("Open the {0} look", "Открыть вид «{0}»");

    // The subscription window: the provider's own look of V2pn.
    public static string SubPartnerTitle => T("Provider's look", "Оформление провайдера");
    public static string SubPartnerConfirmed => T("{0}, confirmed by V2pn until {1}.", "{0}, подтверждено V2pn до {1}.");
    public static string SubPartnerIsOpen => T(" Open now.", " Сейчас открыто.");
    public static string SubPartnerChecking => T("The provider offers its look; V2pn is checking its signature.", "Провайдер предлагает своё оформление, V2pn проверяет подпись.");
    public static string SubPartnerRefused => T(
        "The provider offers its look, but it did not pass V2pn's check, so V2pn keeps its own. The servers work as usual.",
        "Провайдер предлагает своё оформление, но оно не прошло проверку V2pn, поэтому V2pn остаётся обычным. Серверы работают как обычно.");
    public static string SubPartnerOpen => T("Open the look", "Открыть оформление");

    // The open partner look went away; plain V2pn opened instead.
    public static string EditionGoneUnlinked => T(
        "{0}'s look is off: no subscription comes from {0} any more.",
        "Оформление {0} выключено: подписок от {0} больше нет.");
    public static string EditionGoneExpired => T(
        "{0}'s look is off: its confirmation by V2pn ran out. The connection is as it was.",
        "Оформление {0} выключено: подтверждение от V2pn истекло. Подключение не изменилось.");
    public static string EditionGoneRevoked => T(
        "{0}'s look is off: V2pn withdrew its confirmation of {0}. The connection is as it was.",
        "Оформление {0} выключено: V2pn отозвал подтверждение {0}. Подключение не изменилось.");

    // The Appearance page in a partner's look.
    public static string EditionApHint => T(
        "Settings of the {0} look by {1}. Plain V2pn's appearance stays as it is.",
        "Настройки вида «{0}» от {1}. Оформление обычного V2pn они не меняют.");
    public static string EditionApLook => T("Look", "Вид");
    public static string EditionApPartners => T("Provider looks", "Оформление провайдера");
    public static string EditionApAccentHint => T("{0}'s colour or your own.", "Цвет {0} или свой.");
    public static string EditionApPartnerAccent => T("{0}'s colour", "Цвет {0}");
    public static string EditionApTextSizeHint => T("Menus, dialogs and settings.", "Меню, окна и настройки.");
    public static string EditionApReset => T("Back to {0}'s look", "Вернуть как у {0}");
    public static string EditionApShapeNote => T(
        "Type and corners belong to the look: switch the look to change them.",
        "Шрифт и скругления — часть вида: чтобы их поменять, выберите другой вид.");

    public static string EditionVariantShort(string variant) => variant switch
    {
        V2pnPartnerVariants.Signature => T("Signature", "Подпись"),
        V2pnPartnerVariants.Warm => T("Warm", "Тёплый"),
        V2pnPartnerVariants.Cabinet => T("Cabinet", "Кабинет"),
        V2pnPartnerVariants.Studio => T("Studio", "Студия"),
        V2pnPartnerVariants.Ascii => T("ASCII", "ASCII"),
        _ => variant,
    };

    public static string EditionVariant(string variant) => variant switch
    {
        V2pnPartnerVariants.Signature => T("Signature: V2pn with the logo", "Подпись: V2pn с логотипом"),
        V2pnPartnerVariants.Warm => T("Warm: V2pn in its colours", "Тёплый: V2pn в его цветах"),
        V2pnPartnerVariants.Cabinet => T("Cabinet", "Кабинет"),
        V2pnPartnerVariants.Studio => T("Studio", "Студия"),
        V2pnPartnerVariants.Ascii => T("ASCII", "ASCII"),
        _ => variant,
    };
}
