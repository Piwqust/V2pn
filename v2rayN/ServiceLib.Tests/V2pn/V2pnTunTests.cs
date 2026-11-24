using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>On a network with global IPv6 the sing-box tunnel takes IPv6 too, so it cannot go past the VPN.</summary>
public class V2pnTunTests
{
    private static async Task<List<string>> TunAddresses(bool enableIPv6Address, bool hasGlobalIPv6Address)
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        config.TunModeItem.EnableTun = true;
        config.TunModeItem.EnableIPv6Address = enableIPv6Address;
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.sing_box);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box, hasGlobalIPv6Address) with
        {
            IsTunEnabled = true,
        };

        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();

        await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");
        var cfg = JsonUtils.Deserialize<SingboxConfig>(result.Data!.ToString())!;
        return cfg.inbounds.First(i => i.type == "tun").address!;
    }

    [Test]
    public async Task SingboxTunnel_TakesIPv6_WhenTheHostHasGlobalIPv6()
    {
        var addresses = await TunAddresses(enableIPv6Address: false, hasGlobalIPv6Address: true);

        await addresses.Any(a => a.Contains(':')).Should().BeTrue();
        await addresses.Any(a => !a.Contains(':')).Should().BeTrue();
    }

    [Test]
    public async Task SingboxTunnel_StaysIPv4_WithoutGlobalIPv6()
    {
        var addresses = await TunAddresses(enableIPv6Address: false, hasGlobalIPv6Address: false);

        await addresses.Any(a => a.Contains(':')).Should().BeFalse();
    }

    [Test]
    public async Task SingboxTunnel_TakesIPv6_WhenTheSettingIsOn()
    {
        var addresses = await TunAddresses(enableIPv6Address: true, hasGlobalIPv6Address: false);

        await addresses.Any(a => a.Contains(':')).Should().BeTrue();
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, true, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    public async Task UsesIPv6_FollowsTheSettingOrTheNetwork(bool setting, bool globalIPv6, bool expected)
    {
        var config = new Config { TunModeItem = new TunModeItem { EnableIPv6Address = setting } };

        await V2pnTun.UsesIPv6(config, globalIPv6).Should().BeEqualTo(expected);
    }
}
