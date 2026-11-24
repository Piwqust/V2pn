using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>The cores' control API answers only to V2pn: it carries a secret no other program knows.</summary>
public class V2pnLocalApiTests
{
    [Test]
    public async Task SingboxConfig_SetsTheSecret()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.sing_box);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box);

        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();

        await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");
        var cfg = JsonUtils.Deserialize<SingboxConfig>(result.Data!.ToString())!;
        await cfg.experimental!.clash_api!.secret.Should().BeEqualTo(V2pnLocalApi.Secret);
        await cfg.experimental.clash_api.external_controller!.StartsWith(Global.Loopback).Should().BeTrue();
    }

    [Test]
    public async Task Secret_IsLongAndStableForTheRun()
    {
        await V2pnLocalApi.Secret.Length.Should().BeGreaterThanOrEqualTo(32);
        await V2pnLocalApi.Secret.Should().BeEqualTo(V2pnLocalApi.Secret);
        await V2pnLocalApi.Authorization.Parameter.Should().BeEqualTo(V2pnLocalApi.Secret);
    }
}
