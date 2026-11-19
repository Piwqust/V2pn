using System.Net;

namespace ServiceLib.Tests.V2pn;

/// <summary>
/// Programs kept "only through V2pn": how names are read, which filters hold them, and that the end of path the
/// filters match is exactly how Windows names a program of that name, wherever it lies. Putting the persistent
/// filters in place needs administrator rights and is checked by hand.
/// </summary>
public class V2pnAppKillSwitchTests
{
    [Test]
    [Arguments("claude.exe", "claude.exe")]
    [Arguments("Claude.EXE", "claude.exe")]
    [Arguments("claude", "claude.exe")]
    [Arguments(@"C:\Program Files\WindowsApps\Claude_5.11492.5.0_x64__pzs8sxrjxfjjc\app\Claude.exe", "claude.exe")]
    [Arguments("\"C:/Users/me/.local/bin/claude.exe\"", "claude.exe")]
    [Arguments("  claude-ssh-broker.exe  ", "claude-ssh-broker.exe")]
    public async Task Normalize_KeepsTheLowerCaseFileName(string input, string expected)
    {
        await V2pnAppKillSwitch.Normalize(input).Should().BeEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(@"C:\folder\")]
    public async Task Normalize_DropsWhatIsNoProgram(string input)
    {
        await (V2pnAppKillSwitch.Normalize(input) == null).Should().BeTrue();
    }

    [Test]
    public async Task Names_AreDistinctAndSorted()
    {
        var names = V2pnAppKillSwitch.Names(["Claude.exe", "claude", "", "b.exe"]);
        await string.Join(",", names).Should().BeEqualTo("b.exe,claude.exe");
    }

    [Test]
    public async Task Wanted_IsEmptyWhileTheSwitchIsOff()
    {
        var config = new Config { V2pnKillSwitchItem = new() { AppsEnabled = false, Apps = ["claude.exe"] } };
        await V2pnAppKillSwitch.Wanted(config).Count.Should().BeEqualTo(0);
        config.V2pnKillSwitchItem.AppsEnabled = true;
        await V2pnAppKillSwitch.Wanted(config).Single().Should().BeEqualTo("claude.exe");
    }

    private static readonly List<(IPAddress, int)> Tunnel =
        [(IPAddress.Parse("172.18.0.0"), 30), (IPAddress.Parse("fc00::172:18:0:0"), 126)];

    [Test]
    public async Task Filters_LetThroughOnlyTheComputerItselfAndTheTunnel()
    {
        var filters = V2pnAppKillSwitch.Filters(["claude.exe", "claude-ssh-broker.exe"], Tunnel);
        foreach (var layer in Enum.GetValues<V2pnFirewall.Layer>())
        {
            var onLayer = filters.Where(f => f.Layer == layer).OrderByDescending(f => f.Weight).ToList();
            await onLayer.Count.Should().BeEqualTo(3);
            await onLayer[0].Conditions.Any(c => c.Field == V2pnFirewall.Field.LoopbackFlag).Should().BeTrue();
            await onLayer[1].Conditions.Any(c => c.Field == V2pnFirewall.Field.LocalAddress).Should().BeTrue();
            await onLayer[2].Permit.Should().BeFalse();
            // Every filter is about these programs only: the rest of the computer is not touched.
            await onLayer.All(f => f.Conditions.Count(c => c.Field == V2pnFirewall.Field.AppNameSuffix) == 2).Should().BeTrue();
        }
    }

    [Test]
    public async Task Filters_HaveFixedKeys_ThatRemovalKnows()
    {
        var filters = V2pnAppKillSwitch.Filters(["claude.exe"], Tunnel);
        var again = V2pnAppKillSwitch.Filters(["other.exe"], Tunnel);
        await filters.Select(f => f.Key).SequenceEqual(again.Select(f => f.Key)).Should().BeTrue();
        var all = V2pnAppKillSwitch.AllKeys().ToHashSet();
        await filters.All(f => f.Key is { } k && all.Contains(k)).Should().BeTrue();
        await all.Count.Should().BeEqualTo(12);
    }

    [Test]
    public async Task NoPrograms_NoFilters()
    {
        await V2pnAppKillSwitch.Filters([], Tunnel).Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task TunnelNetworks_CoverEveryAddressV2pnMayGiveItsTunnel()
    {
        var config = new Config { TunModeItem = new() { IPv4Address = "10.99.0.1/30" } };
        var networks = V2pnAppKillSwitch.TunnelNetworks(config).Select(n => $"{n.Address}/{n.Prefix}").ToList();
        await networks.Contains("10.99.0.0/30").Should().BeTrue();
        await networks.Contains("172.18.0.0/30").Should().BeTrue();
        await networks.Contains("fc00::172:18:0:0/126").Should().BeTrue();
    }

    [Test]
    public async Task Arguments_ReadBackTheSameNames()
    {
        var arguments = V2pnAppKillSwitch.Arguments(["claude.exe", "b.exe"], Tunnel);
        await arguments.StartsWith(V2pnAppKillSwitch.Command).Should().BeTrue();
        await arguments.Contains("--names \"claude.exe|b.exe\"").Should().BeTrue();
        await arguments.Contains("172.18.0.0/30|fc00::172:18:0:0/126").Should().BeTrue();
    }

    [Test]
    public async Task OtherCommandLines_AreNotTheCommand()
    {
        await V2pnAppKillSwitch.TryRunCommand([], out _).Should().BeFalse();
        await V2pnAppKillSwitch.TryRunCommand(["v2pn://add?url=x"], out _).Should().BeFalse();
    }

    [Test]
    public async Task TheMatchedEnd_IsHowWindowsNamesAProgramOfThatName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        // A program called Claude.exe in a folder with a version in its name, as the Microsoft Store puts it.
        var folder = Path.Combine(Path.GetTempPath(), "v2pn-appid", "Claude_5.11492.5.0_x64__pzs8sxrjxfjjc", "app");
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "Claude.exe");
        File.WriteAllText(exe, "not a program");
        try
        {
            await V2pnFirewall.Native.FwpmGetAppIdFromFileName0(exe, out var blob).Should().BeEqualTo(0u);
            var size = System.Runtime.InteropServices.Marshal.ReadInt32(blob);
            var bytes = new byte[size];
            System.Runtime.InteropServices.Marshal.Copy(System.Runtime.InteropServices.Marshal.ReadIntPtr(blob, 8), bytes, 0, size);
            V2pnFirewall.Native.FwpmFreeMemory0(ref blob);

            var suffix = V2pnFirewall.Native.SuffixBytes("claude.exe");
            await bytes.AsSpan().EndsWith(suffix).Should().BeTrue();
            await bytes.AsSpan().EndsWith(V2pnFirewall.Native.SuffixBytes("laude.exe")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(Path.Combine(Path.GetTempPath(), "v2pn-appid"), true);
        }
    }
}
