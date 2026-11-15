namespace ServiceLib.Tests.V2pn;

/// <summary>Servers of other clients become links that V2pn's own importer reads back field for field.</summary>
public class V2pnForeignLinksTests
{
    private const string ClashConfig = """
        port: 7890
        mode: rule
        proxies:
          - name: "NL Reality"
            type: vless
            server: nl.example.com
            port: 443
            uuid: 11111111-2222-3333-4444-555555555555
            flow: xtls-rprx-vision
            network: tcp
            tls: true
            servername: www.microsoft.com
            client-fingerprint: chrome
            reality-opts:
              public-key: PUBKEY123
              short-id: ab12
          - name: DE ws
            type: vmess
            server: 203.0.113.7
            port: 8443
            uuid: 66666666-7777-8888-9999-000000000000
            alterId: 0
            cipher: auto
            tls: true
            network: ws
            servername: de.example.com
            ws-opts:
              path: /ray?ed=2048
              headers:
                Host: de.example.com
          - { name: "FI trojan", type: trojan, server: fi.example.com, port: 443, password: "p@ss:w/rd", sni: fi.example.com, skip-cert-verify: true, network: grpc, grpc-opts: { grpc-service-name: tun } }
          - name: SS 2022
            type: ss
            server: ss.example.com
            port: 8388
            cipher: 2022-blake3-aes-128-gcm
            password: "base64/key+=="
          - name: Obfs
            type: ss
            server: obfs.example.com
            port: 80
            cipher: aes-128-gcm
            password: secret
            plugin: obfs
            plugin-opts: { mode: http, host: bing.com }
          - name: HY2
            type: hysteria2
            server: hy.example.com
            port: 443
            password: hypass
            sni: hy.example.com
            obfs: salamander
            obfs-password: salt
            ports: 20000-30000
          - name: TUIC
            type: tuic
            server: tuic.example.com
            port: 443
            uuid: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
            password: tuicpass
            congestion-controller: bbr
            alpn: [h3]
          - { name: Socks, type: socks5, server: 10.0.0.1, port: 1080, username: user, password: pw }
          - { name: Plain HTTP, type: http, server: 10.0.0.2, port: 3128 }
          - { name: ShadowTLS, type: ss, server: st.example.com, port: 443, cipher: aes-128-gcm, password: x, plugin: shadow-tls, plugin-opts: { host: cloud.tencent.com } }
          - name: WG
            type: wireguard
            server: wg.example.com
            port: 51820
            ip: 172.16.0.2
            ipv6: fd01::2
            private-key: PRIVKEY=
            public-key: PEERKEY=
            reserved: [1, 2, 3]
            mtu: 1280
        proxy-groups:
          - { name: Auto, type: url-test, proxies: [NL Reality, DE ws] }
        rules:
          - MATCH,Auto
        """;

    private static ProfileItem Parse(string link)
    {
        var item = FmtHandler.ResolveConfig(link, out var msg);
        if (item == null)
        {
            throw new InvalidOperationException($"{link}: {msg}");
        }
        return item;
    }

    [Test]
    public async Task ClashProxies_BecomeLinksV2pnReadsBack()
    {
        var links = V2pnForeignLinks.FromClashYaml(ClashConfig, out var skipped);

        // Plain HTTP and shadow-tls have no link V2pn reads.
        await skipped.Should().BeEqualTo(2);
        await links.Count.Should().BeEqualTo(9);

        var vless = Parse(links[0]);
        await vless.ConfigType.Should().BeEqualTo(EConfigType.VLESS);
        await vless.Remarks.Should().BeEqualTo("NL Reality");
        await vless.Address.Should().BeEqualTo("nl.example.com");
        await vless.Port.Should().BeEqualTo(443);
        await vless.Password.Should().BeEqualTo("11111111-2222-3333-4444-555555555555");
        await vless.StreamSecurity.Should().BeEqualTo("reality");
        await vless.Sni.Should().BeEqualTo("www.microsoft.com");
        await vless.Fingerprint.Should().BeEqualTo("chrome");
        await vless.PublicKey.Should().BeEqualTo("PUBKEY123");
        await vless.ShortId.Should().BeEqualTo("ab12");
        await vless.GetProtocolExtra().Flow.Should().BeEqualTo("xtls-rprx-vision");

        var vmess = Parse(links[1]);
        await vmess.ConfigType.Should().BeEqualTo(EConfigType.VMess);
        await vmess.Network.Should().BeEqualTo("ws");
        await vmess.StreamSecurity.Should().BeEqualTo("tls");
        await vmess.GetTransportExtra().Path.Should().BeEqualTo("/ray?ed=2048");
        await vmess.GetTransportExtra().Host.Should().BeEqualTo("de.example.com");

        var trojan = Parse(links[2]);
        await trojan.ConfigType.Should().BeEqualTo(EConfigType.Trojan);
        await trojan.Password.Should().BeEqualTo("p@ss:w/rd");
        await trojan.Network.Should().BeEqualTo("grpc");
        await trojan.GetTransportExtra().GrpcServiceName.Should().BeEqualTo("tun");
        await trojan.GetAllowInsecure().Should().BeTrue();

        var ss = Parse(links[3]);
        await ss.ConfigType.Should().BeEqualTo(EConfigType.Shadowsocks);
        await ss.GetProtocolExtra().SsMethod.Should().BeEqualTo("2022-blake3-aes-128-gcm");
        await ss.Password.Should().BeEqualTo("base64/key+==");

        var obfs = Parse(links[4]);
        await obfs.GetTransportExtra().RawHeaderType.Should().BeEqualTo("http");
        await obfs.GetTransportExtra().Host.Should().BeEqualTo("bing.com");

        var hy2 = Parse(links[5]);
        await hy2.ConfigType.Should().BeEqualTo(EConfigType.Hysteria2);
        await hy2.Password.Should().BeEqualTo("hypass");
        await hy2.GetProtocolExtra().SalamanderPass.Should().BeEqualTo("salt");
        await hy2.GetProtocolExtra().Ports.Should().BeEqualTo("20000-30000");

        var tuic = Parse(links[6]);
        await tuic.ConfigType.Should().BeEqualTo(EConfigType.TUIC);
        await tuic.Username.Should().BeEqualTo("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        await tuic.Password.Should().BeEqualTo("tuicpass");
        await tuic.GetProtocolExtra().CongestionControl.Should().BeEqualTo("bbr");

        var socks = Parse(links[7]);
        await socks.ConfigType.Should().BeEqualTo(EConfigType.SOCKS);
        await socks.Username.Should().BeEqualTo("user");
        await socks.Password.Should().BeEqualTo("pw");

        var wg = Parse(links[8]);
        await wg.ConfigType.Should().BeEqualTo(EConfigType.WireGuard);
        await wg.Password.Should().BeEqualTo("PRIVKEY=");
        await wg.GetProtocolExtra().WgPublicKey.Should().BeEqualTo("PEERKEY=");
        await wg.GetProtocolExtra().WgReserved.Should().BeEqualTo("1,2,3");
        await wg.GetProtocolExtra().WgInterfaceAddress.Should().BeEqualTo("172.16.0.2/32,fd01::2/128");
        await wg.GetProtocolExtra().WgMtu.Should().BeEqualTo(1280);
    }

    [Test]
    public async Task NotAClashConfig_GivesNothing()
    {
        await V2pnForeignLinks.FromClashYaml("vless://id@host:443#x", out _).Count.Should().BeEqualTo(0);
        await V2pnForeignLinks.FromClashYaml("proxies: [", out _).Count.Should().BeEqualTo(0);
        await V2pnForeignLinks.FromClashYaml(null, out _).Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task SingboxOutbounds_BecomeLinks()
    {
        const string json = """
            {
              "outbounds": [
                { "type": "selector", "tag": "proxy", "outbounds": ["a"] },
                { "type": "vless", "tag": "a", "server": "a.example.com", "server_port": 443,
                  "uuid": "11111111-2222-3333-4444-555555555555", "flow": "xtls-rprx-vision",
                  "tls": { "enabled": true, "server_name": "sni.example.com",
                           "utls": { "enabled": true, "fingerprint": "firefox" },
                           "reality": { "enabled": true, "public_key": "PK", "short_id": "01" } } },
                { "type": "trojan", "tag": "b", "server": "b.example.com", "server_port": 443, "password": "pw",
                  "tls": { "enabled": true, "server_name": "b.example.com" },
                  "transport": { "type": "ws", "path": "/ws", "headers": { "Host": "cdn.example.com" } } },
                { "type": "hysteria2", "tag": "c", "server": "c.example.com", "server_port": 443, "password": "hy",
                  "obfs": { "type": "salamander", "password": "salt" }, "tls": { "enabled": true, "server_name": "c.example.com", "insecure": true } },
                { "type": "direct", "tag": "direct" }
              ]
            }
            """;

        var links = V2pnForeignLinks.FromSingboxJson(json, out var skipped);

        await skipped.Should().BeEqualTo(0);
        await links.Count.Should().BeEqualTo(3);
        var vless = Parse(links[0]);
        await vless.StreamSecurity.Should().BeEqualTo("reality");
        await vless.Sni.Should().BeEqualTo("sni.example.com");
        await vless.Fingerprint.Should().BeEqualTo("firefox");
        await vless.PublicKey.Should().BeEqualTo("PK");
        var trojan = Parse(links[1]);
        await trojan.Network.Should().BeEqualTo("ws");
        await trojan.GetTransportExtra().Host.Should().BeEqualTo("cdn.example.com");
        var hy2 = Parse(links[2]);
        await hy2.GetAllowInsecure().Should().BeTrue();
        await hy2.GetProtocolExtra().SalamanderPass.Should().BeEqualTo("salt");
    }

    [Test]
    public async Task ClashSubscription_BecomesSeparateServers()
    {
        var config = CoreConfig.CoreConfigTestFactory.CreateConfig();
        CoreConfig.CoreConfigTestFactory.BindAppManagerConfig(config);
        SQLiteHelper.Instance.CreateTable<SubItem>();
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        var subId = Guid.NewGuid().ToString("N");

        var count = await ConfigHandler.AddBatchServers(config, ClashConfig, subId, true);

        await count.Should().BeEqualTo(9);
        var servers = await AppManager.Instance.ProfileItems(subId);
        await servers!.Any(s => s.Remarks == "NL Reality" && s.ConfigType == EConfigType.VLESS).Should().BeTrue();
        await servers!.Any(s => s.CoreType == ECoreType.mihomo).Should().BeFalse();
    }
}

/// <summary>Whatever other clients this computer has are read without throwing, and every link they give parses.</summary>
public class V2pnForeignImportTests
{
    [Test]
    public async Task Find_OnThisComputer_GivesLinksV2pnReads()
    {
        foreach (var app in V2pnForeignImport.Find())
        {
            var unreadable = app.Links.Count(l => FmtHandler.ResolveConfig(l, out _) == null);
            Console.WriteLine($"{app.Name}: subs {app.Subscriptions.Count}, links {app.Links.Count}, skipped {app.Skipped}, unreadable {unreadable}, locked {app.Locked}");
            await unreadable.Should().BeEqualTo(0);
            await app.Subscriptions.All(s => V2pnSubs.IsHttpLink(s.Url)).Should().BeTrue();
        }
    }
}
