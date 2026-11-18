namespace ServiceLib.Models.CoreConfigs;

public sealed record AvailabilityProbeEndpoint(int Port, string Password)
{
    public const string Tag = "v2pn-probe";
    public const string User = "probe";

    internal static AvailabilityProbeEndpoint Create()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return new(((IPEndPoint)listener.LocalEndpoint).Port,
                System.Security.Cryptography.RandomNumberGenerator.GetHexString(32));
        }
        finally
        {
            listener.Stop();
        }
    }
}

public record CoreConfigContext
{
    public required ProfileItem Node { get; init; }
    public required ECoreType RunCoreType { get; init; }
    public RoutingItem? RoutingItem { get; init; }
    public DNSItem? RawDnsItem { get; init; }
    public SimpleDNSItem SimpleDnsItem { get; init; } = new();
    public Dictionary<string, ProfileItem> AllProxiesMap { get; init; } = new();
    public Config AppConfig { get; init; } = new();
    public FullConfigTemplateItem? FullConfigTemplate { get; init; } = new();
    public AvailabilityProbeEndpoint? AvailabilityProbe { get; init; }

    public Dictionary<string, string> CustomOutboundContent { get; init; } = new();

    // Test ServerTestItem Map
    public Dictionary<string, string> ServerTestItemMap { get; init; } = new();

    // TUN Compatibility
    public bool IsTunEnabled { get; init; } = false;
    public HashSet<string> ProtectDomainList { get; init; } = [];
    // Typically, it is the core of the outbound chain
    public HashSet<ECoreType> ProtectCoreTypeList { get; init; } = [];

    public bool IsWindows { get; init; }
    public bool IsMacOS { get; init; }
    public bool IsLinux { get; init; }

    // Defaults to true so that a context built without this flag keeps routing IPv6 into the
    // tunnel; only a positive detection of the host having no global IPv6 address turns it off.
    public bool HasGlobalIPv6Address { get; init; } = true;

    // Generation Context
    public Dictionary<object, string> CustomOutboundMap { get; init; } = new();

    // V2pn: what partners' reviewed transport.json and core.json add to the connection to their own servers, by
    // subscription id (V2pnPartnerTraffics.SnapshotAsync).
    public IReadOnlyDictionary<string, V2pnPartnerTraffic> V2pnPartnerSubs { get; init; } = new Dictionary<string, V2pnPartnerTraffic>();

    // V2pn: the partners whose servers the generated config reaches; their core.json outbounds are added once.
    // Shared by every copy of the context made while one config is generated.
    public HashSet<string> V2pnPartnersUsed { get; init; } = [];

    public V2pnPartnerTraffic? V2pnTrafficFor(ProfileItem? node) =>
        node?.Subid is { Length: > 0 } subId && V2pnPartnerSubs.TryGetValue(subId, out var traffic) ? traffic : null;
}
