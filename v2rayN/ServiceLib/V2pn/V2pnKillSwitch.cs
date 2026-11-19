namespace ServiceLib;

/// <summary>
/// V2pn: the kill switch. Without it, a connection that cannot be brought back switches V2pn off, and programs go
/// on to the internet directly (MainWindowViewModel.StopAfterFailureAsync). With it, V2pn stays "on" and holds the
/// internet closed until the person reconnects or presses Disconnect:
///
///  - VPN mode (Windows, administrator): a Windows Filtering Platform block (<see cref="V2pnFirewall"/>) is in place
///    for as long as the person wants to be connected, before the core starts and through its restarts, so nothing
///    slips out in the gaps either. Only V2pn, its cores, the tunnel, DHCP and (optionally) the local network pass.
///  - Proxy mode: the system proxy is left pointing at V2pn's own port with no core behind it, so every program
///    that uses the system proxy gets no connection instead of a direct one. Programs that ignore the system proxy
///    were never covered by proxy mode; VPN mode is the one that covers them.
/// </summary>
public static class V2pnKillSwitch
{
    public static readonly EventChannel<bool> HoldingChanged = new();

    /// <summary>A connection failed and the kill switch keeps the internet closed instead of switching off.</summary>
    public static bool Holding { get; private set; }

    /// <summary>Why the VPN-mode block could not be put in place, if it could not.</summary>
    public static string? FirewallError { get; private set; }

    private static string? _appliedPlan;

    public static V2pnKillSwitchItem Item(Config config) => config.V2pnKillSwitchItem ??= new();

    public static bool On(Config? config) => config?.V2pnKillSwitchItem?.Enabled == true;

    public static void SetHolding(bool holding)
    {
        if (Holding == holding)
        {
            return;
        }
        Holding = holding;
        HoldingChanged.Publish(holding);
    }

    /// <summary>
    /// Whether a failed connection is held closed rather than switched off. Only when the hold is real: in VPN mode
    /// the block must be in place; in proxy mode V2pn's port must not belong to another program, which would then
    /// receive everything (<see cref="EV2pnFailureCause.PortBusy"/>).
    /// </summary>
    public static bool CanHold(Config config, EV2pnFailureCause cause)
    {
        if (!On(config) || V2pnConnection.IsOff(config))
        {
            return false;
        }
        return config.TunModeItem.EnableTun ? V2pnFirewall.IsActive : cause != EV2pnFailureCause.PortBusy;
    }

    /// <summary>The VPN-mode block is wanted: kill switch on, VPN mode on, V2pn connected or trying to be.</summary>
    public static bool WantsFirewall(Config config) =>
        OperatingSystem.IsWindows() && On(config) && config.TunModeItem.EnableTun && !V2pnConnection.IsOff(config) && Utils.IsAdministrator();

    /// <summary>
    /// Puts the VPN-mode block in place, refreshes it (a tunnel adapter that came up, a setting that changed), or
    /// lifts it, whichever the settings and the connection call for now. Cheap when nothing changed.
    /// </summary>
    public static void Sync(Config config)
    {
        if (!WantsFirewall(config))
        {
            if (V2pnFirewall.IsActive)
            {
                V2pnFirewall.Remove();
            }
            _appliedPlan = null;
            FirewallError = null;
            return;
        }
        var plan = BuildPlan(config);
        var signature = Signature(plan);
        if (V2pnFirewall.IsActive && signature == _appliedPlan)
        {
            return;
        }
        FirewallError = V2pnFirewall.Apply(plan);
        _appliedPlan = FirewallError == null ? signature : _appliedPlan;
    }

    public static V2pnFirewall.Plan BuildPlan(Config config)
    {
        var programs = new List<string> { Utils.GetExePath() };
        var bin = Path.Combine(Utils.StartupPath(), "bin");
        if (Directory.Exists(bin))
        {
            programs.AddRange(Directory.GetFiles(bin, "*.exe", SearchOption.AllDirectories));
        }
        var tunnel = new List<(System.Net.IPAddress, int)>();
        if (V2pnFirewall.ParseNetwork(config.TunModeItem.IPv4Address.NullIfEmpty() ?? Global.TunIPv4Address.First()) is { } v4)
        {
            tunnel.Add(v4);
        }
        if (config.TunModeItem.EnableIPv6Address
            && V2pnFirewall.ParseNetwork(config.TunModeItem.IPv6Address.NullIfEmpty() ?? Global.TunIPv6Address.First()) is { } v6)
        {
            tunnel.Add(v6);
        }
        return new V2pnFirewall.Plan(programs, tunnel, V2pnFirewall.TunnelAdapters(), Item(config).AllowLan);
    }

    private static string Signature(V2pnFirewall.Plan plan) => string.Join("|",
        string.Join(",", plan.Programs.Order(StringComparer.OrdinalIgnoreCase)),
        string.Join(",", plan.TunNetworks.Select(n => $"{n.Address}/{n.Prefix}")),
        string.Join(",", plan.TunInterfaces.Order()),
        plan.AllowLan);
}
