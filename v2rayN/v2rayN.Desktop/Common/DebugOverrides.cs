using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Common;

/// <summary>
/// What the debug window lays over the real data of a partner's look (Views/DebugWindow.cs): the state, the server
/// counts, the subscription block, another provider's server, no servers at all. Kept in memory only, so a restart
/// shows the truth again. Never applied in a plain release (V2pnPartnerTrust.DebugAllowed).
/// </summary>
public static class DebugOverrides
{
    /// <summary>null: as it is; else safe, warn, checking or unsafe.</summary>
    public static string? StateKind { get; private set; }

    /// <summary>real, partner (11 of 14 by the partner), allok, alldown, none (not pinged).</summary>
    public static string Nodes { get; private set; } = "real";

    /// <summary>real, panel, full, warn, bad, incident, major, empty.</summary>
    public static string Account { get; private set; } = "real";

    /// <summary>null: as it is; true or false: the "not the partner's server" note forced on or off.</summary>
    public static bool? Foreign { get; private set; }

    public static bool ForceEmpty { get; private set; }

    public static event Action? Changed;

    public static bool Any => StateKind != null || Nodes != "real" || Account != "real" || Foreign != null || ForceEmpty;

    public static void SetState(string? kind) => Change(() => StateKind = kind);

    public static void SetNodes(string nodes) => Change(() => Nodes = nodes);

    public static void SetAccount(string account) => Change(() => Account = account);

    public static void SetForeign(bool? foreign) => Change(() => Foreign = foreign);

    public static void SetEmpty(bool empty) => Change(() => ForceEmpty = empty);

    public static void Reset() => Change(() =>
    {
        StateKind = null;
        Nodes = "real";
        Account = "real";
        Foreign = null;
        ForceEmpty = false;
    });

    private static void Change(Action edit)
    {
        edit();
        Changed?.Invoke();
    }

    /// <summary>The snapshot as the overrides make it; the snapshot itself when none is set or debugging is off.</summary>
    public static EditionSnapshot Apply(EditionSnapshot s)
    {
        if (!Any || !V2pnPartnerTrust.DebugAllowed)
        {
            return s;
        }
        if (StateKind is { } kind)
        {
            s = s with
            {
                StateKind = kind,
                Protected = kind == "safe",
                StateLabel = kind switch
                {
                    "safe" => "Защищено",
                    "warn" => "Внимание: сайты открываются медленно",
                    "checking" => "Проверка соединения…",
                    _ => "Не защищено",
                },
            };
        }
        s = Nodes switch
        {
            "partner" => s with { NodesOk = 11, NodesTotal = 14, NodesByPartner = true, PartnerName = s.PartnerName ?? "Провайдер" },
            "allok" => s with { NodesOk = 14, NodesTotal = 14, NodesByPartner = false },
            "alldown" => s with { NodesOk = 0, NodesTotal = 14, NodesByPartner = false },
            "none" => s with { NodesOk = 0, NodesTotal = 0, NodesByPartner = false },
            _ => s,
        };
        if (Foreign is { } foreign)
        {
            s = s with
            {
                ForeignServer = foreign,
                PartnerName = s.PartnerName ?? "Провайдер",
                BestPartnerServer = s.BestPartnerServer ?? s.Catalog.FirstOrDefault()?.IndexId,
                Subscription = foreign ? s.Subscription ?? "другой провайдер" : s.Subscription,
            };
        }
        if (ForceEmpty)
        {
            s = s with { AnyServers = false, Catalog = [], Servers = [], Subs = [] };
        }
        const string cabinet = "https://example.com/cabinet";
        const string support = "https://t.me/example_support";
        var full = new SceneAccount("12,4 ГБ из 50 ГБ", 0.25, true, "Оплачено до 31 января 2027 · 77 дней", false, false,
            "Плановые работы в воскресенье с 3:00 до 4:00.", cabinet, support, "Личный доступ", "Устройств: 2 из 5",
            AnnounceUrl: "https://example.com/news",
            Banner: new SubBanner("Скидка 30% на год до пятницы.", SubPanelHeaders.ToneInfo, "Продлить", cabinet, false), DaysLeft: 77);
        return Account switch
        {
            "panel" => s with { Account = new SceneAccount(null, 0, false, null, false, false, "Объявление провайдера: всё работает.", cabinet, support) },
            "full" => s with { Account = full },
            "warn" => s with { Account = full with { Traffic = "46,1 ГБ из 50 ГБ", Fraction = 0.92, Expiry = "Оплачено до 18 ноября · 3 дня", Warn = true } },
            "bad" => s with { Account = full with { Traffic = "50 ГБ из 50 ГБ", Fraction = 1, Expiry = "Подписка закончилась 2 дня назад", Bad = true } },
            "incident" => s with { Account = full with { Incident = "Германия не отвечает. Уже чиним." } },
            "major" => s with { Account = full with { Incident = "Все серверы недоступны. Подробности в канале.", IncidentSevere = true } },
            "empty" => s with { Account = null },
            _ => s,
        };
    }
}
