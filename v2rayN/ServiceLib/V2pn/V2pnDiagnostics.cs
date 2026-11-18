namespace ServiceLib;

/// <summary>
/// V2pn: the report behind "Copy diagnostics". Only fields on an allow list: versions, the system, the mode,
/// states and codes V2pn itself defines, times and counts. Free text is never copied in (the core's messages,
/// the log, names, links, addresses, paths), because any of it can carry a subscription link, a key, a
/// server address or the user name in a path. A detail is kept only when it has a fixed shape ("HTTP 403").
/// </summary>
public static class V2pnDiagnostics
{
    private static (string Reason, DateTime Time)? _network;

    /// <summary>The last network change or wake the connection was checked after. The reason is V2pn's own words.</summary>
    public static void NoteNetworkEvent(string reason) => _network = (reason, DateTime.UtcNow);

    public sealed record SubLine(EV2pnSubUpdateResult Result, string? Detail, long LastAttempt, long LastGood, long Expire,
        long Total, long Used, int Servers);

    public sealed record Snapshot
    {
        public string Version { get; init; } = V2pnInfo.DisplayVersion;
        public string Upstream { get; init; } = string.Empty;
        public bool Release { get; init; }
        public string? Partner { get; init; }
        public string Os { get; init; } = string.Empty;
        public string Arch { get; init; } = string.Empty;
        public string Runtime { get; init; } = string.Empty;
        public bool Admin { get; init; }
        public bool Connected { get; init; }
        public bool Tun { get; init; }
        public string? TunStack { get; init; }
        public ESysProxyType ProxyType { get; init; }
        public int LocalPort { get; init; }
        public string Route { get; init; } = string.Empty;
        public ECoreType? Core { get; init; }
        public EConfigType? ServerType { get; init; }
        public string? Transport { get; init; }
        public string? Security { get; init; }
        public EV2pnCoreState CoreState { get; init; }
        public EV2pnFailure Failure { get; init; }
        public EV2pnFailureCause FailureCause { get; init; }
        public int? LastExitCode { get; init; }
        public AvailabilityCheckResult? Check { get; init; }
        public DateTime? CheckedAt { get; init; }
        public bool CustomCheckUrl { get; init; }
        public (string Reason, DateTime Time)? Network { get; init; }
        public int ServerCount { get; init; }
        public IReadOnlyList<SubLine> Subs { get; init; } = [];
        public DateTime Now { get; init; } = DateTime.UtcNow;
    }

    // Free-text details that are safe because of their shape.
    private static readonly Regex SafeDetail = new(@"^(HTTP \d{3}|timeout)$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> KnownTransports = new(StringComparer.OrdinalIgnoreCase)
        { "tcp", "raw", "kcp", "ws", "h2", "http", "quic", "grpc", "httpupgrade", "xhttp", "splithttp" };

    private static readonly HashSet<string> KnownSecurity = new(StringComparer.OrdinalIgnoreCase) { "", "none", "tls", "reality" };

    private static readonly HashSet<string> KnownStacks = new(StringComparer.OrdinalIgnoreCase) { "system", "gvisor", "mixed" };

    /// <summary>Gathers the snapshot from the running app. <paramref name="route"/> is a preset id or "custom".</summary>
    public static async Task<Snapshot> CollectAsync(Config config, AvailabilityCheckResult? check, DateTime? checkedAt, string route)
    {
        var server = await AppManager.Instance.GetProfileItem(config.IndexId);
        var subs = await AppManager.Instance.SubItems() ?? [];
        var profiles = await AppManager.Instance.ProfileItems(string.Empty) ?? [];
        var counts = profiles.GroupBy(p => p.Subid ?? string.Empty).ToDictionary(g => g.Key, g => g.Count());
        var lines = subs.Select(s =>
        {
            var info = SubInfoManager.Instance.Get(s.Id);
            return new SubLine(info?.LastResult ?? EV2pnSubUpdateResult.None, info?.LastResultDetail,
                info?.LastAttemptTime ?? 0, info?.LastGoodTime > 0 ? info.LastGoodTime : s.UpdateTime,
                info?.HasUserInfo == true ? info.Expire : 0, info?.HasUserInfo == true ? info.Total : 0,
                info?.HasUserInfo == true ? info.Upload + info.Download : 0, counts.GetValueOrDefault(s.Id));
        }).ToList();
        return new Snapshot
        {
            Upstream = V2pnInfo.UpstreamVersion,
            Release = V2pnInfo.IsRelease,
            Partner = V2pnEdition.PartnerId,
            Os = RuntimeInformation.OSDescription,
            Arch = RuntimeInformation.OSArchitecture.ToString(),
            Runtime = RuntimeInformation.FrameworkDescription,
            Admin = Utils.IsAdministrator(),
            Connected = !V2pnConnection.IsOff(config),
            Tun = config.TunModeItem.EnableTun,
            TunStack = config.TunModeItem.Stack,
            ProxyType = config.SystemProxyItem.SysProxyType,
            LocalPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks),
            Route = route,
            Core = CoreManager.Instance.IsCoreAlive ? AppManager.Instance.RunningCoreType : null,
            ServerType = server?.ConfigType,
            Transport = server?.GetNetwork(),
            Security = server?.StreamSecurity,
            CoreState = V2pnCoreWatch.State,
            Failure = V2pnCoreWatch.Failure,
            FailureCause = V2pnCoreWatch.FailureCause,
            LastExitCode = V2pnCoreWatch.LastExitCode,
            Check = check,
            CheckedAt = checkedAt?.ToUniversalTime(),
            CustomCheckUrl = !Global.SpeedPingTestUrls.Contains(config.SpeedTestItem.SpeedPingTestUrl),
            Network = _network,
            ServerCount = profiles.Count,
            Subs = lines,
        };
    }

    public static string Format(Snapshot s)
    {
        var sb = new StringBuilder();
        string Ago(DateTime utc)
        {
            var span = s.Now - utc;
            return span.TotalSeconds < 0 ? "just now"
                : span.TotalMinutes < 1 ? $"{(int)span.TotalSeconds} s ago"
                : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
                : span.TotalDays < 2 ? $"{(int)span.TotalHours} h ago"
                : $"{(int)span.TotalDays} days ago";
        }
        string AgoUnix(long unix) => unix > 0 ? Ago(DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime) : "never";
        static string Pick(string? value, HashSet<string> known) =>
            value == null ? "-" : known.Contains(value) ? (value.Length == 0 ? "none" : value.ToLowerInvariant()) : "other";

        sb.AppendLine("V2pn diagnostics");
        sb.AppendLine($"Generated: {s.Now:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"App: V2pn {s.Version} (v2rayN {s.Upstream}), {(s.Release ? "release" : "local build")}, {(s.Partner is { } p ? $"partner build {p}" : "plain")}");
        sb.AppendLine($"System: {s.Os}, {s.Arch}, {s.Runtime}, administrator: {(s.Admin ? "yes" : "no")}");
        var mode = !s.Connected ? "off"
            : s.Tun ? $"VPN (TUN, stack {Pick(s.TunStack, KnownStacks)})"
            : s.ProxyType switch
            {
                ESysProxyType.ForcedChange => "proxy (system proxy)",
                ESysProxyType.Pac => "proxy (PAC)",
                ESysProxyType.Unchanged => "local proxy only",
                _ => s.ProxyType.ToString(),
            };
        sb.AppendLine($"Mode: {mode}; local port {s.LocalPort}; route {s.Route}");
        sb.AppendLine($"Server: {(s.ServerType is { } t ? $"{t}, transport {Pick(s.Transport, KnownTransports)}, security {Pick(s.Security, KnownSecurity)}" : "none selected")}");
        sb.Append($"Core: {(s.Core is { } core ? core.ToString() : "not running")}, state {s.CoreState}");
        if (s.Failure != EV2pnFailure.None)
        {
            sb.Append($", failure {s.Failure}, cause {s.FailureCause}");
        }
        if (s.LastExitCode is { } code)
        {
            sb.Append($", last exit code {code}");
        }
        sb.AppendLine();
        if (s.Check is { State: not AvailabilityState.Unchecked } check)
        {
            sb.Append($"Check: {check.State}");
            if (check.HttpStatus is { } http)
            {
                sb.Append($", HTTP {http}");
            }
            if (check.IsAvailable)
            {
                sb.Append($", {check.Time} ms");
            }
            if (check.CheckUrlFailed)
            {
                sb.Append(", check address silent, second site answered");
            }
            sb.Append($", {(s.CustomCheckUrl ? "custom" : "bundled")} check address");
            if (s.CheckedAt is { } at)
            {
                sb.Append($", {Ago(at)}");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine($"Check: not checked yet, {(s.CustomCheckUrl ? "custom" : "bundled")} check address");
        }
        if (s.Network is { } net)
        {
            sb.AppendLine($"Network: {net.Reason}, {Ago(net.Time)}");
        }
        sb.AppendLine($"Servers: {s.ServerCount}; subscriptions: {s.Subs.Count}");
        for (var i = 0; i < s.Subs.Count; i++)
        {
            var sub = s.Subs[i];
            var detail = sub.Detail != null && SafeDetail.IsMatch(sub.Detail) ? $" ({sub.Detail})" : string.Empty;
            sb.Append($"  #{i + 1}: {sub.Servers} servers, last update {sub.Result}{detail} {AgoUnix(sub.LastAttempt)}, servers from {AgoUnix(sub.LastGood)}");
            if (sub.Expire > 0)
            {
                var left = DateTimeOffset.FromUnixTimeSeconds(sub.Expire).UtcDateTime - s.Now;
                sb.Append(left.TotalSeconds < 0 ? ", expired by the provider's data" : $", expires in {(int)left.TotalDays} days");
            }
            if (sub.Total > 0)
            {
                sb.Append($", traffic {Math.Min(100, sub.Used * 100 / sub.Total)}% used");
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
