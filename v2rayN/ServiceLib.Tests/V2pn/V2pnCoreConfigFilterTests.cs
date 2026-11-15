using ServiceLib.Tests.CoreConfig;
using YamlDotNet.RepresentationModel;

namespace ServiceLib.Tests.V2pn;

public class V2pnCoreConfigFilterTests
{
    private static JsonObject Json(string text) => (JsonObject)JsonNode.Parse(text)!;

    // What a 3x-ui JSON subscription or a Remnawave XRAY_JSON template looks like: nothing to take out.
    private const string CleanXray = """
        {"remarks":"Германия","log":{"loglevel":"warning"},
         "inbounds":[{"tag":"socks","listen":"127.0.0.1","port":10808,"protocol":"socks","settings":{"udp":true},"sniffing":{"enabled":true,"destOverride":["http","tls"]}},
                     {"tag":"http","listen":"127.0.0.1","port":10809,"protocol":"http"}],
         "outbounds":[{"tag":"proxy","protocol":"vless","settings":{"vnext":[{"address":"de.example.com","port":443,"users":[{"id":"00000000-0000-0000-0000-000000000001","encryption":"none","flow":"xtls-rprx-vision"}]}]},
                       "streamSettings":{"network":"tcp","security":"reality","realitySettings":{"serverName":"www.example.com","publicKey":"abc","shortId":"01"}}},
                      {"tag":"direct","protocol":"freedom"},{"tag":"block","protocol":"blackhole"}],
         "dns":{"servers":["1.1.1.1"]},
         "routing":{"domainStrategy":"IPIfNonMatch","rules":[{"type":"field","domain":["geosite:category-ru"],"outboundTag":"direct"}]}}
        """;

    [Test]
    public async Task Xray_CleanConfig_IsLeftAsItIs()
    {
        var removed = new List<string>();
        var text = V2pnCoreConfigFilter.FilterWhole(ECoreType.Xray, CleanXray, removed);

        await removed.Should().BeEmpty();
        await text.Should().BeEqualTo(CleanXray);
    }

    [Test]
    public async Task Xray_ApiStatsReverseAndLogFiles_AreTakenOut()
    {
        var config = Json("""
            {"log":{"loglevel":"info","access":"C:/Users/x/AppData/Roaming/Microsoft/Windows/Start Menu/Programs/Startup/a.bat","error":"e.log"},
             "api":{"tag":"api","services":["HandlerService","StatsService"]},
             "stats":{},"metrics":{"tag":"metrics","listen":"0.0.0.0:11111"},"reverse":{"bridges":[{"tag":"bridge","domain":"r.example"}]},
             "inbounds":[{"tag":"api","listen":"0.0.0.0","port":10085,"protocol":"dokodemo-door","settings":{"address":"127.0.0.1"}},
                         {"tag":"socks","port":10808,"protocol":"socks"}],
             "outbounds":[{"tag":"proxy","protocol":"vless","settings":{"vnext":[],"reverse":{"tag":"r-in"}},
                           "streamSettings":{"security":"tls","tlsSettings":{"masterKeyLog":"C:/keys.log","certificates":[{"certificateFile":"C:/Users/x/.ssh/id","keyFile":"k.pem","usage":"verify"}]}}}],
             "routing":{"rules":[{"type":"field","inboundTag":["api"],"outboundTag":"api"},{"type":"field","domain":["x"],"outboundTag":"proxy"}]}}
            """);

        var removed = V2pnCoreConfigFilter.FilterXray(config);

        foreach (var key in new[] { "api", "stats", "metrics", "reverse" })
        {
            await config.ContainsKey(key).Should().BeFalse();
        }
        await config["log"]!.AsObject().Select(p => p.Key).ToList().Should().BeEquivalentTo(new[] { "loglevel" });
        var inbounds = config["inbounds"]!.AsArray();
        await inbounds.Count.Should().BeEqualTo(1);
        await inbounds[0]!["listen"]!.GetValue<string>().Should().BeEqualTo("127.0.0.1");
        var outbound = config["outbounds"]![0]!;
        await outbound["settings"]!.AsObject().ContainsKey("reverse").Should().BeFalse();
        var tls = outbound["streamSettings"]!["tlsSettings"]!.AsObject();
        await tls.ContainsKey("masterKeyLog").Should().BeFalse();
        await tls["certificates"]![0]!.AsObject().Select(p => p.Key).ToList().Should().BeEquivalentTo(new[] { "usage" });
        var rules = config["routing"]!["rules"]!.AsArray();
        await rules.Count.Should().BeEqualTo(1);
        await rules[0]!["outboundTag"]!.GetValue<string>().Should().BeEqualTo("proxy");
        await removed.Should().Contain("log.access");
    }

    [Test]
    public async Task Xray_Inbounds_StayOnThisComputer()
    {
        var config = Json("""
            {"inbounds":[{"tag":"lan","listen":"0.0.0.0","port":1,"protocol":"socks"},
                         {"tag":"v6","listen":"::","port":2,"protocol":"http"},
                         {"tag":"local6","listen":"::1","port":3,"protocol":"socks"},
                         {"tag":"name","listen":"localhost","port":4,"protocol":"socks"},
                         {"tag":"sock","listen":"/dev/shm/x.sock","protocol":"socks"},
                         {"tag":"server","listen":"127.0.0.1","port":5,"protocol":"vless"},
                         {"tag":"tun","protocol":"tun"},
                         {"tag":"tls","listen":"127.0.0.1","port":6,"protocol":"http","streamSettings":{"security":"tls"}}],
             "outbounds":[]}
            """);

        V2pnCoreConfigFilter.FilterXray(config);

        var listen = config["inbounds"]!.AsArray().ToDictionary(i => i!["tag"]!.GetValue<string>(), i => i!["listen"]!.GetValue<string>());
        await listen.Keys.ToList().Should().BeEquivalentTo(new[] { "lan", "v6", "local6", "name", "tls" });
        await listen["lan"].Should().BeEqualTo("127.0.0.1");
        await listen["v6"].Should().BeEqualTo("127.0.0.1");
        await listen["local6"].Should().BeEqualTo("::1");
        await listen["name"].Should().BeEqualTo("localhost");
        await config["inbounds"]!.AsArray().Last()!.AsObject().ContainsKey("streamSettings").Should().BeFalse();
    }

    [Test]
    public async Task Singbox_ListenersFilesAndProgramsAreTakenOut_ServersStay()
    {
        var config = Json("""
            {"log":{"level":"info","output":"C:/x/startup.bat"},
             "experimental":{"clash_api":{"external_controller":"0.0.0.0:9090","external_ui":"C:/Users/x/Startup","external_ui_download_url":"https://evil.example/ui.zip"},"cache_file":{"enabled":true,"path":"c.db"}},
             "ntp":{"enabled":true},"services":[{"type":"ssm-api","listen":"0.0.0.0"}],"certificate":{"store":"chrome"},
             "inbounds":[{"type":"tun","tag":"tun-in","address":["172.19.0.1/30"]},
                         {"type":"mixed","tag":"mixed-in","listen":"0.0.0.0","listen_port":2080,"set_system_proxy":true},
                         {"type":"vless","tag":"server","listen":"127.0.0.1","listen_port":443}],
             "outbounds":[{"type":"vless","tag":"proxy","server":"de.example.com","server_port":443,"tls":{"enabled":true,"certificate_path":"C:/ca.pem","ech":{"enabled":true,"config_path":"ech.pem"}},"transport":{"type":"ws","path":"/ws"}},
                          {"type":"tor","tag":"tor","executable_path":"C:/evil.exe","data_directory":"C:/d"},
                          {"type":"direct","tag":"direct"}],
             "endpoints":[{"type":"wireguard","tag":"wg","private_key":"k","peers":[]},{"type":"tailscale","tag":"ts","state_directory":"C:/ts"}],
             "dns":{"servers":[{"type":"hosts","tag":"hosts","path":["C:/secret.txt"]},{"type":"https","tag":"doh","server":"1.1.1.1","path":"/dns-query"}],
                    "rules":[{"rule_set":["local-ads"],"action":"reject"},{"domain":["a.example"],"server":"doh"}]},
             "route":{"rule_set":[{"type":"local","tag":"local-ads","path":"C:/Windows/win.ini"},{"type":"remote","tag":"geosite-ru","url":"https://example.com/ru.srs"}],
                      "rules":[{"rule_set":"local-ads","action":"reject"},
                               {"type":"logical","mode":"and","rules":[{"rule_set":["local-ads"]},{"port":443}],"outbound":"direct"},
                               {"rule_set":["geosite-ru"],"outbound":"direct"},
                               {"process_path":["C:/Program Files/app.exe"],"outbound":"direct"}]}}
            """);

        var removed = V2pnCoreConfigFilter.FilterSingbox(config);

        foreach (var key in new[] { "experimental", "ntp", "services", "certificate" })
        {
            await config.ContainsKey(key).Should().BeFalse();
        }
        await config["log"]!.AsObject().ContainsKey("output").Should().BeFalse();
        var inbounds = config["inbounds"]!.AsArray();
        await inbounds.Count.Should().BeEqualTo(1);
        await inbounds[0]!["listen"]!.GetValue<string>().Should().BeEqualTo("127.0.0.1");
        await inbounds[0]!.AsObject().ContainsKey("set_system_proxy").Should().BeFalse();

        var outbounds = config["outbounds"]!.AsArray();
        await outbounds.Select(o => o!["tag"]!.GetValue<string>()).ToList().Should().BeEquivalentTo(new[] { "proxy", "direct" });
        await outbounds[0]!["tls"]!.AsObject().ContainsKey("certificate_path").Should().BeFalse();
        await outbounds[0]!["tls"]!["ech"]!.AsObject().ContainsKey("config_path").Should().BeFalse();
        await outbounds[0]!["transport"]!["path"]!.GetValue<string>().Should().BeEqualTo("/ws");

        var endpoints = config["endpoints"]!.AsArray();
        await endpoints.Count.Should().BeEqualTo(1);
        var rules = config["route"]!["rules"]!.AsArray();
        await rules[0]!["inbound"]![0]!.GetValue<string>().Should().BeEqualTo("wg");
        await rules[0]!["action"]!.GetValue<string>().Should().BeEqualTo("reject");
        // The two rules on the dropped local rule set go; the remote one and process_path stay.
        await rules.Count.Should().BeEqualTo(3);
        await rules[2]!["process_path"]![0]!.GetValue<string>().Should().BeEqualTo("C:/Program Files/app.exe");
        await config["route"]!["rule_set"]!.AsArray().Count.Should().BeEqualTo(1);

        var servers = config["dns"]!["servers"]!.AsArray();
        await servers[0]!.AsObject().ContainsKey("path").Should().BeFalse();
        await servers[1]!["path"]!.GetValue<string>().Should().BeEqualTo("/dns-query");
        await config["dns"]!["rules"]!.AsArray().Count.Should().BeEqualTo(1);
        await removed.Should().Contain("tor tor");
    }

    [Test]
    public async Task Clash_PanelListenersAndLocalFiles_AreTakenOut()
    {
        var yaml = """
            mixed-port: 7890
            port: 7891
            allow-lan: true
            external-controller: 0.0.0.0:9090
            external-ui: C:/Users/x/AppData/Roaming/Microsoft/Windows/Start Menu/Programs/Startup
            external-ui-url: https://evil.example/ui.zip
            listeners:
              - name: open
                type: mixed
                listen: 0.0.0.0
                port: 1080
            tunnels:
              - tcp,0.0.0.0:2222,10.0.0.1:22,proxy
            ntp:
              enable: true
              write-to-system: true
            tun:
              enable: true
            dns:
              enable: true
              listen: 0.0.0.0:53
            proxies:
              - {name: de, type: vless, server: de.example.com, port: 443, uuid: 00000000-0000-0000-0000-000000000001}
            proxy-providers:
              local:
                type: file
                path: C:/secret.yaml
              remote:
                type: http
                url: https://example.com/p.yaml
                path: ../../../../Startup/p.yaml
            rule-providers:
              local-rules:
                type: file
                behavior: domain
                path: C:/Windows/win.ini
              ok:
                type: http
                behavior: domain
                url: https://example.com/r.yaml
                path: ./ruleset/ok.yaml
            proxy-groups:
              - name: auto
                type: url-test
                use: [local, remote]
                proxies: [de]
            rules:
              - RULE-SET,local-rules,REJECT
              - AND,((RULE-SET,local-rules),(NETWORK,tcp)),DIRECT
              - RULE-SET,ok,DIRECT
              - MATCH,auto
            """;
        var config = YamlUtils.FromYaml(yaml);

        var removed = V2pnCoreConfigFilter.FilterClash(config);

        foreach (var key in new[] { "port", "external-ui", "external-ui-url", "listeners", "tunnels", "ntp", "tun" })
        {
            await config.Children.ContainsKey(new YamlScalarNode(key)).Should().BeFalse();
        }
        var dns = (YamlMappingNode)config.Children[new YamlScalarNode("dns")];
        await dns.Children.ContainsKey(new YamlScalarNode("listen")).Should().BeFalse();

        var proxyProviders = (YamlMappingNode)config.Children[new YamlScalarNode("proxy-providers")];
        await proxyProviders.Children.ContainsKey(new YamlScalarNode("local")).Should().BeFalse();
        var remotePath = ((YamlScalarNode)((YamlMappingNode)proxyProviders.Children[new YamlScalarNode("remote")]).Children[new YamlScalarNode("path")]).Value!;
        await remotePath.StartsWith("./providers/v2pn-proxy-providers-").Should().BeTrue();
        await remotePath.EndsWith(".yaml").Should().BeTrue();

        var ruleProviders = (YamlMappingNode)config.Children[new YamlScalarNode("rule-providers")];
        await ruleProviders.Children.ContainsKey(new YamlScalarNode("local-rules")).Should().BeFalse();
        var okPath = ((YamlScalarNode)((YamlMappingNode)ruleProviders.Children[new YamlScalarNode("ok")]).Children[new YamlScalarNode("path")]).Value;
        await okPath.Should().BeEqualTo("./ruleset/ok.yaml");

        var rules = ((YamlSequenceNode)config.Children[new YamlScalarNode("rules")]).Children.Select(r => ((YamlScalarNode)r).Value).ToList();
        await rules.Should().BeEquivalentTo(new[] { "RULE-SET,ok,DIRECT", "MATCH,auto" });
        var group = (YamlMappingNode)((YamlSequenceNode)config.Children[new YamlScalarNode("proxy-groups")]).Children[0];
        var use = ((YamlSequenceNode)group.Children[new YamlScalarNode("use")]).Children.Select(u => ((YamlScalarNode)u).Value).ToList();
        await use.Should().BeEquivalentTo(new[] { "remote" });
        await removed.Should().Contain("dns.listen");
    }

    [Test]
    public async Task OtherCoresAndBrokenText_AreNotRun()
    {
        var removed = new List<string>();
        await V2pnCoreConfigFilter.FilterWhole(ECoreType.naiveproxy, """{"listen":"socks://0.0.0.0:1080"}""", removed).Should().BeNull();
        await V2pnCoreConfigFilter.FilterWhole(ECoreType.hysteria2, "server: x\nauth: y", removed).Should().BeNull();
        await V2pnCoreConfigFilter.FilterWhole(ECoreType.Xray, "not json", removed).Should().BeNull();
        await V2pnCoreConfigFilter.FilterWhole(null, CleanXray, removed).Should().BeNull();
    }

    [Test]
    public async Task Outbound_FromSubscription_IsFilteredOrRefused()
    {
        var removed = new List<string>();
        var xray = V2pnCoreConfigFilter.FilterOutbound(ECoreType.Xray,
            """{"tag":"{{tag}}","protocol":"vless","settings":{"address":"a.example","port":443,"id":"x","encryption":"none","reverse":{"tag":"r"}}}""", removed);
        await xray.Should().NotBeNull();
        await JsonNode.Parse(xray!)!["settings"]!.AsObject().ContainsKey("reverse").Should().BeFalse();
        await JsonNode.Parse(xray!)!["tag"]!.GetValue<string>().Should().BeEqualTo("{{tag}}");

        const string clean = """{"type":"vless","tag":"proxy","server":"a.example","server_port":443,"uuid":"x"}""";
        await V2pnCoreConfigFilter.FilterOutbound(ECoreType.sing_box, clean, []).Should().BeEqualTo(clean);
        await V2pnCoreConfigFilter.FilterOutbound(ECoreType.sing_box, """{"type":"tor","executable_path":"C:/evil.exe"}""", []).Should().BeNull();
    }

    [Test]
    public async Task FromSubscription_OnlyForSubscriptionUpdates()
    {
        await V2pnCoreConfigFilter.FromSubscription(new ProfileItem { IsSub = true, Subid = "sub" }).Should().BeTrue();
        await V2pnCoreConfigFilter.FromSubscription(new ProfileItem { IsSub = false, Subid = "sub" }).Should().BeFalse();
        await V2pnCoreConfigFilter.FromSubscription(new ProfileItem { IsSub = true, Subid = string.Empty }).Should().BeFalse();
    }

    [Test]
    public async Task GenerateClientConfig_CustomFromSubscription_RunsFilteredCopy_OwnFileAsItIs()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var dir = Path.Combine(Path.GetTempPath(), $"v2pn-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "sub.json");
            await File.WriteAllTextAsync(source, """
                {"log":{"access":"a.log"},"api":{"tag":"api"},
                 "inbounds":[{"tag":"socks","listen":"0.0.0.0","port":10808,"protocol":"socks"}],
                 "outbounds":[{"tag":"proxy","protocol":"vless","settings":{"vnext":[]},"streamSettings":{"network":"tcp"}}]}
                """);
            var output = Path.Combine(dir, "config.json");

            var fromSub = new ProfileItem { IndexId = "n1", ConfigType = EConfigType.Custom, CoreType = ECoreType.Xray, Address = source, IsSub = true, Subid = "sub" };
            var result = await CoreConfigHandler.GenerateClientConfig(new CoreConfigContext { Node = fromSub, RunCoreType = ECoreType.Xray }, output);
            await result.Success.Should().BeTrue();
            var run = Json(await File.ReadAllTextAsync(output));
            await run.ContainsKey("api").Should().BeFalse();
            await run["inbounds"]![0]!["listen"]!.GetValue<string>().Should().BeEqualTo("127.0.0.1");

            var own = new ProfileItem { IndexId = "n1", ConfigType = EConfigType.Custom, CoreType = ECoreType.Xray, Address = source, IsSub = false, Subid = "sub" };
            result = await CoreConfigHandler.GenerateClientConfig(new CoreConfigContext { Node = own, RunCoreType = ECoreType.Xray }, output);
            await result.Success.Should().BeTrue();
            await (await File.ReadAllTextAsync(output)).Should().BeEqualTo(await File.ReadAllTextAsync(source));

            var naive = new ProfileItem { IndexId = "n2", ConfigType = EConfigType.Custom, CoreType = ECoreType.naiveproxy, Address = source, IsSub = true, Subid = "sub" };
            result = await CoreConfigHandler.GenerateClientConfig(new CoreConfigContext { Node = naive, RunCoreType = ECoreType.naiveproxy }, output);
            await result.Success.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
