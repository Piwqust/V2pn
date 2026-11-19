using System.Net;
using System.Runtime.InteropServices;

namespace ServiceLib.Tests.V2pn;

/// <summary>
/// The kill switch: which filters its VPN-mode block is made of, when a failed connection is held closed, and the
/// WFP structures against the sizes of the Windows headers (64-bit). Adding filters needs administrator rights and
/// would cut this computer's network, so the block itself is checked by hand (docs/internal or the PR notes).
/// </summary>
public class V2pnKillSwitchTests
{
    private static readonly V2pnFirewall.Plan Plan = new(
        [@"C:\V2pn\V2pn.exe", @"C:\V2pn\bin\xray\xray.exe"],
        [(IPAddress.Parse("172.18.0.0"), 30), (IPAddress.Parse("fc00::172:18:0:0"), 126)],
        [0x1234UL],
        AllowLan: true);

    [Test]
    public async Task EveryLayer_EndsInABlock()
    {
        var filters = V2pnFirewall.Filters(Plan);
        foreach (var layer in Enum.GetValues<V2pnFirewall.Layer>())
        {
            var block = filters.Single(f => f.Layer == layer && f.Conditions.Count == 0);
            await block.Permit.Should().BeFalse();
            await block.Weight.Should().BeEqualTo(V2pnFirewall.WeightBlockAll);
        }
    }

    [Test]
    public async Task V2pnAndItsCores_PassAboveEverything()
    {
        var filters = V2pnFirewall.Filters(Plan);
        var programs = filters.Where(f => f.Conditions.Any(c => c.Field == V2pnFirewall.Field.AppId)).ToList();
        await programs.Count.Should().BeEqualTo(2 * 4);
        await programs.All(f => f.Permit && f.Weight == filters.Max(x => x.Weight)).Should().BeTrue();
    }

    [Test]
    public async Task Dns_IsBlockedOutsideTheTunnel_EvenToTheLocalNetwork()
    {
        var filters = V2pnFirewall.Filters(Plan);
        var dns = filters.Where(f => f.Conditions.Any(c => c.Field == V2pnFirewall.Field.RemotePort && (ushort)c.Value == 53)).ToList();
        await dns.Count.Should().BeEqualTo(2); // the two connect layers
        await dns.All(f => !f.Permit).Should().BeTrue();
        var lan = filters.Where(f => f.Name.EndsWith("local network")).ToList();
        await dns.All(d => d.Weight > lan.Max(l => l.Weight)).Should().BeTrue();
        var tunnel = filters.Where(f => f.Weight == V2pnFirewall.WeightTunnel).ToList();
        await tunnel.All(t => t.Weight > dns[0].Weight).Should().BeTrue();
    }

    [Test]
    public async Task TunnelAddresses_GoToTheirOwnFamily()
    {
        var filters = V2pnFirewall.Filters(Plan);
        var v4 = filters.Single(f => f.Layer == V2pnFirewall.Layer.ConnectV4 && f.Name.EndsWith("tunnel address"));
        var v6 = filters.Single(f => f.Layer == V2pnFirewall.Layer.ConnectV6 && f.Name.EndsWith("tunnel address"));
        await (((IPAddress, int))v4.Conditions.Single().Value).Item1.ToString().Should().BeEqualTo("172.18.0.0");
        await (((IPAddress, int))v6.Conditions.Single().Value).Item1.ToString().Should().BeEqualTo("fc00::172:18:0:0");
        await filters.Count(f => f.Name.EndsWith("tunnel adapter")).Should().BeEqualTo(4);
    }

    [Test]
    public async Task LocalNetwork_OnlyWhenAllowed()
    {
        var closed = V2pnFirewall.Filters(Plan with { AllowLan = false });
        await closed.Any(f => f.Name.EndsWith("local network")).Should().BeFalse();
        var open = V2pnFirewall.Filters(Plan);
        await open.Count(f => f.Name.EndsWith("local network") && f.Permit).Should().BeEqualTo(4);
    }

    [Test]
    [Arguments("172.18.0.1/30", "172.18.0.0", 30)]
    [Arguments("10.0.0.1/30", "10.0.0.0", 30)]
    [Arguments("fc00::172:18:0:1/126", "fc00::172:18:0:0", 126)]
    [Arguments("192.168.1.7", "192.168.1.7", 32)]
    public async Task ParseNetwork_ClearsTheHostBits(string cidr, string network, int prefix)
    {
        var parsed = V2pnFirewall.ParseNetwork(cidr);
        await parsed!.Value.Address.ToString().Should().BeEqualTo(network);
        await parsed.Value.Prefix.Should().BeEqualTo(prefix);
    }

    [Test]
    public async Task ParseNetwork_RejectsNonsense()
    {
        await (V2pnFirewall.ParseNetwork("not an address") == null).Should().BeTrue();
        await (V2pnFirewall.ParseNetwork(null) == null).Should().BeTrue();
    }

    private static Config ProxyConfig(bool killSwitch) => new()
    {
        TunModeItem = new() { EnableTun = false },
        SystemProxyItem = new() { SysProxyType = ESysProxyType.ForcedChange },
        V2pnKillSwitchItem = new() { Enabled = killSwitch },
    };

    [Test]
    public async Task ProxyMode_HoldsOnlyWithTheKillSwitchOn()
    {
        await V2pnKillSwitch.CanHold(ProxyConfig(true), EV2pnFailureCause.Unknown).Should().BeTrue();
        await V2pnKillSwitch.CanHold(ProxyConfig(false), EV2pnFailureCause.Unknown).Should().BeFalse();
    }

    [Test]
    public async Task ProxyMode_DoesNotHold_WhenAnotherProgramHasThePort()
    {
        // The proxy would hand everything to that program: switching off is the honest choice.
        await V2pnKillSwitch.CanHold(ProxyConfig(true), EV2pnFailureCause.PortBusy).Should().BeFalse();
    }

    [Test]
    public async Task Disconnected_NeverHolds()
    {
        var config = ProxyConfig(true);
        config.SystemProxyItem.SysProxyType = ESysProxyType.ForcedClear;
        await V2pnKillSwitch.CanHold(config, EV2pnFailureCause.Unknown).Should().BeFalse();
    }

    [Test]
    public async Task VpnMode_HoldsOnlyWhenTheBlockIsInPlace()
    {
        var config = ProxyConfig(true);
        config.TunModeItem.EnableTun = true;
        // No block was put in place in this test process.
        await V2pnKillSwitch.CanHold(config, EV2pnFailureCause.Unknown).Should().BeFalse();
    }

    [Test]
    public async Task WfpStructures_HaveTheSizesOfTheWindowsHeaders()
    {
        await Marshal.SizeOf<V2pnFirewall.Native.Session>().Should().BeEqualTo(72);
        await Marshal.SizeOf<V2pnFirewall.Native.SubLayer>().Should().BeEqualTo(72);
        await Marshal.SizeOf<V2pnFirewall.Native.Filter>().Should().BeEqualTo(200);
        await Marshal.SizeOf<V2pnFirewall.Native.FilterCondition>().Should().BeEqualTo(40);
        await Marshal.SizeOf<V2pnFirewall.Native.Value>().Should().BeEqualTo(16);
        await Marshal.SizeOf<V2pnFirewall.Native.V4AddrMask>().Should().BeEqualTo(8);
        await Marshal.SizeOf<V2pnFirewall.Native.V6AddrMask>().Should().BeEqualTo(17);
    }

    [Test]
    public async Task WithoutAdministratorRights_TheBlockFailsCleanly()
    {
        // As administrator (GitHub's runners) this would really close the network around the test run.
        if (!OperatingSystem.IsWindows() || Utils.IsAdministrator())
        {
            return;
        }
        var error = V2pnFirewall.Apply(Plan with { Programs = [Environment.ProcessPath!], TunInterfaces = [] });
        // The session opens (its structure is right); Windows refuses the transaction to a normal user.
        await (error != null && !error.Contains("FwpmEngineOpen0")).Should().BeTrue();
        await V2pnFirewall.IsActive.Should().BeFalse();
    }

    [Test]
    public async Task AppIds_ComeFromWindows_WithoutAdministratorRights()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var exe = Environment.ProcessPath!;
        var result = V2pnFirewall.Native.FwpmGetAppIdFromFileName0(exe, out var blob);
        await result.Should().BeEqualTo(0u);
        await (blob != IntPtr.Zero).Should().BeTrue();
        // FWP_BYTE_BLOB: the size, then the data: the path in NT form, as UTF-16.
        var size = Marshal.ReadInt32(blob);
        var text = Marshal.PtrToStringUni(Marshal.ReadIntPtr(blob, 8), size / 2).TrimEnd('\0');
        await text.EndsWith(Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        V2pnFirewall.Native.FwpmFreeMemory0(ref blob);
    }
}
