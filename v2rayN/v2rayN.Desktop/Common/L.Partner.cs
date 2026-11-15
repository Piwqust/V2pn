namespace v2rayN.Desktop;

// Partners (providers that dress V2pn in their look) and the v2pn:// links of their sites.
public static partial class L
{
    public static string LinkAddTitle => T("Add a subscription", "Добавить подписку");
    public static string LinkAddBody => T("Add the subscription «{0}» from {1}?\n\nV2pn downloads its server list. If the provider is a V2pn partner, its look becomes available once its signature checks out. The provider does not see your traffic.",
        "Добавить подписку «{0}» с {1}?\n\nV2pn скачает список серверов. Если провайдер — партнёр V2pn, его оформление станет доступно после проверки подписи. Провайдер не видит ваш трафик.");
    public static string LinkAddYes => T("Add", "Добавить");
    public static string LinkAddNo => T("Cancel", "Отмена");

    // The edition switcher behind the name in the title bar.
    public static string EditionSwitchTip => T("Switch between V2pn and your providers' looks (Ctrl+1…9). The connection stays as it is.",
        "Переключение между V2pn и оформлением провайдеров (Ctrl+1…9). Подключение не прерывается.");
    public static string EditionOn => T("on", "на");
    public static string EditionLook => T("{0} look", "Вид {0}");
    // The Appearance page in a partner's look.
    public static string EditionApHint => T(
        "Settings of the {0} look by {1}. Plain V2pn's appearance stays as it is.",
        "Настройки вида «{0}» от {1}. Оформление обычного V2pn они не меняют.");
    public static string EditionApLook => T("Look", "Вид");
    public static string EditionApPartners => T("Provider looks", "Оформление провайдера");
    public static string EditionApAccentHint => T("{0}'s colour or your own.", "Цвет {0} или свой.");
    public static string EditionApPartnerAccent => T("{0}'s colour", "Цвет {0}");
    public static string EditionApCornersSquare => T("Square", "Прямые");
    public static string EditionApTextSizeHint => T("Menus, dialogs and settings.", "Меню, окна и настройки.");
    public static string EditionApReset => T("Back to {0}'s look", "Вернуть как у {0}");

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
