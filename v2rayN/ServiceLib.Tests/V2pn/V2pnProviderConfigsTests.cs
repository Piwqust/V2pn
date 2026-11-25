namespace ServiceLib.Tests.V2pn;

/// <summary>
/// A Remnawave xray-json answer (the shape a real provider sent on 2026-11-25, with made-up hosts and keys) is taken
/// apart into servers, a group and notices instead of whole "Custom" configs.
/// </summary>
public class V2pnProviderConfigsTests
{
    private const string Id = "11111111-2222-3333-4444-555555555555";

    private static string Inbounds => """
        "inbounds": [
          { "tag": "socks", "port": 10808, "listen": "127.0.0.1", "protocol": "socks", "settings": { "udp": true, "auth": "noauth" } },
          { "tag": "http", "port": 10809, "listen": "127.0.0.1", "protocol": "http", "settings": {} }
        ]
        """;

    private static string Builtins => """
        { "tag": "direct", "protocol": "freedom" },
        { "tag": "block", "protocol": "blackhole" }
        """;

    private const string Ws = $$"""
        { "tag": "proxy", "protocol": "vless",
          "settings": { "vnext": [ { "address": "nl.example.net", "port": 443, "users": [ { "id": "{{Id}}", "encryption": "none", "flow": "" } ] } ] },
          "streamSettings": { "network": "ws", "wsSettings": { "path": "/websocket", "host": "", "headers": { "X-Version": "1.9.1" } },
            "security": "tls", "tlsSettings": { "serverName": "nl.example.net", "fingerprint": "firefox", "alpn": [ "http/1.1" ] } } }
        """;

    private const string Reality = $$"""
        { "tag": "proxy", "protocol": "vless",
          "settings": { "vnext": [ { "address": "198.51.100.7", "port": 8443, "users": [ { "id": "{{Id}}", "encryption": "none", "flow": "" } ] } ] },
          "streamSettings": { "network": "xhttp", "xhttpSettings": { "mode": "auto", "host": "", "path": "/assets" },
            "security": "reality", "realitySettings": { "serverName": "cdn.example.org", "publicKey": "PUBKEY", "shortId": "0123abcd", "fingerprint": "chrome" } } }
        """;

    private const string Hysteria = $$"""
        { "tag": "proxy", "protocol": "hysteria", "settings": { "address": "hy.example.net", "port": 32520, "version": 2 },
          "streamSettings": { "network": "hysteria", "hysteriaSettings": { "version": 2, "auth": "{{Id}}" },
            "security": "tls", "tlsSettings": { "serverName": "hy.example.net", "alpn": [ "h3" ] } } }
        """;

    private const string Notice = """
        { "tag": "proxy", "protocol": "shadowsocks", "settings": { "servers": [ { "address": "zvONO4EK", "port": 1234, "method": "chacha20-ietf-poly1305", "password": "x" } ] } }
        """;

    private static string Single(string name, string outbound, string rules = """[ { "type": "field", "protocol": [ "bittorrent" ], "outboundTag": "block" } ]""") => $$"""
        { "remarks": "{{name}}", "dns": { "servers": [ "1.1.1.1" ] }, {{Inbounds}},
          "outbounds": [ {{outbound}}, {{Builtins}} ],
          "routing": { "domainStrategy": "IPIfNonMatch", "rules": {{rules}} } }
        """;

    private static string WithTag(string outbound, string tag) => outbound.Replace("\"tag\": \"proxy\"", $"\"tag\": \"{tag}\"");

    private static string Auto => $$"""
        { "remarks": "🇪🇺 AUTO", {{Inbounds}},
          "outbounds": [ {{Hysteria}}, {{WithTag(Ws, "proxy-2")}}, {{WithTag(Reality, "proxy-3")}}, {{Builtins}} ],
          "burstObservatory": { "subjectSelector": [ "proxy" ] },
          "routing": {
            "rules": [
              { "type": "field", "inboundTag": [ "socks" ], "port": "53", "outboundTag": "direct" },
              { "domain": [ "geosite:category-ru" ], "outboundTag": "direct" },
              { "type": "field", "network": "tcp,udp", "balancerTag": "Super_Balancer" }
            ],
            "balancers": [ { "tag": "Super_Balancer", "selector": [ "proxy" ], "strategy": { "type": "leastLoad" }, "fallbackTag": "direct" } ] } }
        """;

    internal static string Answer => $$"""
        [
          {{Single("🇸🇴 Проверяйте отклик нажимая на 🕔", Notice)}},
          {{Auto}},
          {{Single("🇳🇱 Нидерланды 2", Ws)}},
          {{Single("🇸🇪 Швеция 1", Reality)}},
          {{Single("🇳🇱 Нидерланды 1 ", Hysteria)}}
        ]
        """;

    [Test]
    public async Task Answer_BecomesServersGroupAndNotice()
    {
        var unpacked = V2pnProviderConfigs.Unpack(Answer);

        await unpacked.Should().NotBeNull();
        await unpacked!.Notices.Should().BeEquivalentTo(["🇸🇴 Проверяйте отклик нажимая на 🕔"]);
        await unpacked.Servers.Select(s => s.Name).ToList().Should().BeEquivalentTo(["🇳🇱 Нидерланды 2", "🇸🇪 Швеция 1", "🇳🇱 Нидерланды 1"]);
        await unpacked.Rest.Count.Should().BeEqualTo(0);

        var group = unpacked.Groups.Single();
        await group.Name.Should().BeEqualTo("🇪🇺 AUTO");
        await group.Load.Should().BeEqualTo(EMultipleLoad.LeastLoad);
        await group.Members.Should().BeEquivalentTo(["🇳🇱 Нидерланды 1", "🇳🇱 Нидерланды 2", "🇸🇪 Швеция 1"]);
    }

    [Test]
    public async Task Servers_ReadBackAsTheirProtocols_AndKeepTheProvidersOutbound()
    {
        var servers = V2pnProviderConfigs.Unpack(Answer)!.Servers;

        var ws = FmtHandler.ResolveConfig(servers[0].Link, out _)!;
        await ws.ConfigType.Should().BeEqualTo(EConfigType.VLESS);
        await ws.Remarks.Should().BeEqualTo("🇳🇱 Нидерланды 2");
        await ws.Address.Should().BeEqualTo("nl.example.net");
        await ws.Port.Should().BeEqualTo(443);
        await ws.Network.Should().BeEqualTo("ws");
        await ws.StreamSecurity.Should().BeEqualTo("tls");
        await ws.GetTransportExtra().Path.Should().BeEqualTo("/websocket");
        await ws.Fingerprint.Should().BeEqualTo("firefox");
        // The header no link holds stays in the provider's outbound.
        await servers[0].Outbound.Should().Contain("X-Version");

        var reality = FmtHandler.ResolveConfig(servers[1].Link, out _)!;
        await reality.StreamSecurity.Should().BeEqualTo("reality");
        await reality.Network.Should().BeEqualTo("xhttp");
        await reality.PublicKey.Should().BeEqualTo("PUBKEY");
        await reality.ShortId.Should().BeEqualTo("0123abcd");
        await reality.GetTransportExtra().XhttpMode.Should().BeEqualTo("auto");

        var hysteria = FmtHandler.ResolveConfig(servers[2].Link, out _)!;
        await hysteria.ConfigType.Should().BeEqualTo(EConfigType.Hysteria2);
        await hysteria.Address.Should().BeEqualTo("hy.example.net");
        await hysteria.Port.Should().BeEqualTo(32520);
        await hysteria.Password.Should().BeEqualTo(Id);
        await hysteria.Sni.Should().BeEqualTo("hy.example.net");
    }

    [Test]
    public async Task ProviderRules_SpeakV2pnsTags_AndDropTheirInboundsAndDirectFallback()
    {
        var unpacked = V2pnProviderConfigs.Unpack(Answer)!;

        var single = JsonUtils.ParseJson(unpacked.Servers[0].Rules!)!;
        await single["domainStrategy"]!.ToString().Should().BeEqualTo("IPIfNonMatch");
        await single["rules"]!.AsArray().Count.Should().BeEqualTo(1);
        await single["rules"]![0]!["outboundTag"]!.ToString().Should().BeEqualTo("block");

        var group = JsonUtils.ParseJson(unpacked.Groups[0].Rules!)!["rules"]!.AsArray();
        // The inbound's rule goes, the balancer becomes "proxy".
        await group.Count.Should().BeEqualTo(2);
        await group[0]!["outboundTag"]!.ToString().Should().BeEqualTo("direct");
        await group[1]!["outboundTag"]!.ToString().Should().BeEqualTo("proxy");
        await group[1]!["balancerTag"].Should().BeNull();
    }

    [Test]
    public async Task BalancerOverAServerThatDidNotComeAlone_StaysWhole()
    {
        var other = Hysteria.Replace("hy.example.net", "other.example.net");
        var auto = Auto.Replace(Hysteria, other);
        var unpacked = V2pnProviderConfigs.Unpack($"[ {auto}, {Single("NL", Ws)}, {Single("SE", Reality)}, {Single("HY", Hysteria)} ]")!;

        await unpacked.Groups.Count.Should().BeEqualTo(0);
        await unpacked.Rest.Count.Should().BeEqualTo(1);
    }

    [Test]
    public async Task ChainedOrUnknownServers_StayWhole()
    {
        var chained = Ws.Replace("\"security\": \"tls\"", "\"sockopt\": { \"dialerProxy\": \"frag\" }, \"security\": \"tls\"");
        var kcp = Ws.Replace("\"network\": \"ws\"", "\"network\": \"kcp\"");
        var unpacked = V2pnProviderConfigs.Unpack($"[ {Single("chained", chained)}, {Single("kcp", kcp)}, {Single("ok", Reality)} ]")!;

        await unpacked.Servers.Count.Should().BeEqualTo(1);
        await unpacked.Rest.Count.Should().BeEqualTo(2);
    }

    [Test]
    public async Task NotWholeConfigs_AreNotTouched()
    {
        await V2pnProviderConfigs.Unpack("vless://x@example.com:443#a").Should().BeNull();
        await V2pnProviderConfigs.Unpack(Ws).Should().BeNull();
        await V2pnProviderConfigs.Unpack("""{ "outbounds": [ { "type": "vless" } ] }""").Should().BeNull();
    }

    [Test]
    [Arguments("0.0.0.0", 1, null, true)]
    [Arguments("zvONO4EK", 1234, null, true)]
    [Arguments("example.com", 1, null, true)]
    [Arguments("example.com", 443, "00000000-0000-0000-0000-000000000000", true)]
    [Arguments("example.com", 443, Id, false)]
    [Arguments("203.0.113.5", 443, null, false)]
    [Arguments("2001:db8::1", 443, null, false)]
    [Arguments("localhost", 1080, null, false)]
    public async Task MadeUpServers_AreTold(string host, int port, string? id, bool madeUp)
    {
        await V2pnProviderConfigs.IsMadeUp(host, port, id).Should().BeEqualTo(madeUp);
    }

    [Test]
    public async Task Notices_ComeFromLinksToo()
    {
        const string links = """
            vless://00000000-0000-0000-0000-000000000000@0.0.0.0:1?encryption=none&type=tcp&security=none#App%20not%20supported%20%5BNo%20HWID%5D
            vless://11111111-2222-3333-4444-555555555555@nl.example.net:443?encryption=none&type=tcp&security=tls#NL
            """;

        await V2pnProviderConfigs.Notices(links).Should().BeEquivalentTo(["App not supported [No HWID]"]);
        await V2pnProviderConfigs.Notices(Answer).Should().BeEquivalentTo(["🇸🇴 Проверяйте отклик нажимая на 🕔"]);
    }
}
