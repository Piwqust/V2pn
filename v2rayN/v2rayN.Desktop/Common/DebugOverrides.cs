using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Common;

/// <summary>
/// What the debug window lays over the real data of the partner in the window (Views/DebugWindow.cs): what its panel
/// says about the subscription, and its background taken away. Kept in memory only, so a restart shows the truth
/// again. Never applied in a plain release (V2pnPartnerTrust.DebugAllowed).
/// </summary>
public static class DebugOverrides
{
    /// <summary>real, panel, full, warn, bad, incident, major, empty.</summary>
    public static string Account { get; private set; } = "real";

    /// <summary>True: the partner's background is ignored, so a themed partner shows V2pn's map (Accent instead of Scene).</summary>
    public static bool NoStage => _noStage && V2pnPartnerTrust.DebugAllowed;

    private static bool _noStage;

    public static event Action? Changed;

    public static bool Any => Account != "real" || _noStage;

    public static void SetAccount(string account) => Change(() => Account = account);

    public static void SetNoStage(bool off) => Change(() => _noStage = off);

    public static void Reset() => Change(() =>
    {
        Account = "real";
        _noStage = false;
    });

    private static void Change(Action edit)
    {
        edit();
        Changed?.Invoke();
    }

    /// <summary>The account as the override makes it; the account itself when none is set or debugging is off.</summary>
    public static SceneAccount? Apply(SceneAccount? account)
    {
        if (Account == "real" || !V2pnPartnerTrust.DebugAllowed)
        {
            return account;
        }
        const string cabinet = "https://example.com/cabinet";
        const string support = "https://t.me/example_support";
        var full = new SceneAccount("12,4 ГБ из 50 ГБ", 0.25, true, "Оплачено до 31 января 2027", false, false,
            "Плановые работы в воскресенье с 3:00 до 4:00.", cabinet, support, "Личный доступ", "Устройств: 2 из 5",
            AnnounceUrl: "https://example.com/news",
            Banner: new SubBanner("Скидка 30% на год до пятницы.", SubPanelHeaders.ToneInfo, "Продлить", cabinet, false), DaysLeft: 77);
        return Account switch
        {
            "panel" => new SceneAccount(null, 0, false, null, false, false, "Объявление провайдера: всё работает.", cabinet, support),
            "full" => full,
            "warn" => full with { Traffic = "46,1 ГБ из 50 ГБ", Fraction = 0.92, Expiry = "Оплачено до 18 ноября", DaysLeft = 3, Warn = true },
            "bad" => full with { Traffic = "50 ГБ из 50 ГБ", Fraction = 1, Expiry = "Подписка закончилась 2 дня назад", DaysLeft = 0, Bad = true },
            "incident" => full with { Incident = "Германия не отвечает. Уже чиним.", Banner = null },
            "major" => full with { Incident = "Все серверы недоступны. Подробности в канале.", IncidentSevere = true, Banner = null },
            "empty" => null,
            _ => account,
        };
    }
}
