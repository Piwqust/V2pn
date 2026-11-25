namespace ServiceLib.Tests.V2pn;

/// <summary>A whole config is made ready for a ping: one local SOCKS port of the test's, and the servers it reaches.</summary>
public class V2pnCustomPingTests
{
    private const string Xray = """
        { "remarks": "chain",
          "api": { "tag": "api", "services": [ "StatsService" ] },
          "inbounds": [
            { "tag": "socks-in", "port": 10808, "listen": "127.0.0.1", "protocol": "socks", "sniffing": { "enabled": true, "destOverride": [ "http", "tls", "quic" ] } },
            { "tag": "http", "port": 10809, "listen": "127.0.0.1", "protocol": "http" }
          ],
          "outbounds": [
            { "tag": "proxy", "protocol": "vless", "settings": { "vnext": [ { "address": "exit.example.net", "port": 443, "users": [ { "id": "x" } ] } ] }, "proxySettings": { "tag": "front" } },
            { "tag": "front", "protocol": "trojan", "settings": { "servers": [ { "address": "203.0.113.9", "port": 8443, "password": "p" } ] } },
            { "tag": "notice", "protocol": "shadowsocks", "settings": { "servers": [ { "address": "0.0.0.0", "port": 1 } ] } },
            { "tag": "direct", "protocol": "freedom" }
          ],
          "routing": { "rules": [ { "inboundTag": [ "socks-in" ], "outboundTag": "proxy" } ] } }
        """;

    private const string Singbox = """
        { "inbounds": [ { "type": "tun", "tag": "tun-in", "auto_route": true } ],
          "outbounds": [
            { "type": "vless", "tag": "proxy", "server": "nl.example.net", "server_port": 443 },
            { "type": "urltest", "tag": "auto", "outbounds": [ "proxy" ] },
            { "type": "direct", "tag": "direct" }
          ],
          "endpoints": [ { "type": "wireguard", "tag": "wg", "peers": [ { "address": "198.51.100.3", "port": 51820 } ] } ],
          "experimental": { "clash_api": { "external_controller": "0.0.0.0:9090" } },
          "route": { "auto_detect_interface": true } }
        """;

    [Test]
    public async Task Xray_GetsOneSocksPortUnderTheFirstInboundsTag()
    {
        var config = JsonNode.Parse(V2pnCustomPing.TestConfig(Xray, ECoreType.Xray, 23456)!)!;

        var inbounds = config["inbounds"]!.AsArray();
        await inbounds.Count.Should().BeEqualTo(1);
        await inbounds[0]!["tag"]!.ToString().Should().BeEqualTo("socks-in");
        await inbounds[0]!["port"]!.GetValue<int>().Should().BeEqualTo(23456);
        await inbounds[0]!["listen"]!.ToString().Should().BeEqualTo("127.0.0.1");
        await inbounds[0]!["sniffing"]!["destOverride"]!.AsArray().Count.Should().BeEqualTo(3);
        await config["api"].Should().BeNull();
        await config["routing"]!["rules"]![0]!["inboundTag"]![0]!.ToString().Should().BeEqualTo("socks-in");
    }

    [Test]
    public async Task Singbox_LosesItsTunAndControllerForAMixedPort()
    {
        var config = JsonNode.Parse(V2pnCustomPing.TestConfig(Singbox, ECoreType.sing_box, 23457)!)!;

        var inbound = config["inbounds"]![0]!;
        await config["inbounds"]!.AsArray().Count.Should().BeEqualTo(1);
        await inbound["type"]!.ToString().Should().BeEqualTo("mixed");
        await inbound["listen_port"]!.GetValue<int>().Should().BeEqualTo(23457);
        await (config["experimental"]?["clash_api"]).Should().BeNull();
        await (config["route"]?["auto_detect_interface"]).Should().BeNull();
    }

    [Test]
    public async Task Endpoints_AreTheRealServers()
    {
        await V2pnCustomPing.Endpoints(Xray, ECoreType.Xray).Should().BeEquivalentTo([("exit.example.net", 443), ("203.0.113.9", 8443)]);
        await V2pnCustomPing.Endpoints(Singbox, ECoreType.sing_box).Should().BeEquivalentTo([("nl.example.net", 443), ("198.51.100.3", 51820)]);
    }

    [Test]
    public async Task NotAConfig_IsNotTested()
    {
        await V2pnCustomPing.TestConfig("proxies: []", ECoreType.Xray, 1000).Should().BeNull();
        await V2pnCustomPing.CanTest(new ProfileItem { ConfigType = EConfigType.Custom, CoreType = ECoreType.mihomo }).Should().BeFalse();
        await V2pnCustomPing.CanTest(new ProfileItem { ConfigType = EConfigType.Custom, CoreType = ECoreType.Xray }).Should().BeTrue();
    }
}
