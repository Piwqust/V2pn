using ServiceLib.Services.CoreConfig;
using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>Reviewed files of a partner: routing.json, transport.json, core.json, bound to the certificate by hash.</summary>
public class V2pnPartnerReviewTests
{
    internal static readonly byte[] RoutingJson = Encoding.UTF8.GetBytes("""
        {
          "name": "Kestrel: YouTube and AI",
          "rules": [
            { "domains": ["geosite:youtube", "openai.com", "full:chat.openai.com"], "ips": ["91.108.4.0/22"], "to": "proxy" },
            { "domains": ["domain:gosuslugi.ru", "keyword:sberbank"], "to": "direct" },
            { "domains": ["geosite:category-ads-all"], "to": "block" }
          ],
          "default": "direct"
        }
        """);

    internal static readonly byte[] TransportJson = Encoding.UTF8.GetBytes("""
        { "fragment": { "packets": "tlshello", "length": "100-200", "interval": "10-20" }, "noise": [ { "rand": "10-20", "delay": "10-16" } ] }
        """);

    internal static readonly byte[] CoreXrayJson = Encoding.UTF8.GetBytes("""
        {
          "core": "xray",
          "outbounds": [
            { "tag": "relay", "protocol": "vless",
              "settings": { "vnext": [ { "address": "relay.example.com", "port": 443, "users": [ { "id": "9d2d8e4e-6f2a-4f5f-9a51-0c8e5c7b1a11", "encryption": "none" } ] } ] },
              "streamSettings": { "network": "raw", "security": "tls", "tlsSettings": { "serverName": "relay.example.com" } } },
            { "tag": "us", "protocol": "trojan",
              "settings": { "servers": [ { "address": "us.example.com", "port": 443, "password": "secret" } ] },
              "streamSettings": { "network": "raw", "security": "tls", "sockopt": { "dialerProxy": "relay" } } }
          ],
          "dialer": "relay",
          "balancers": [ { "tag": "fast", "selector": ["us", "proxy"], "strategy": "leastPing" } ],
          "rules": [ { "domains": ["geosite:openai", "domain:chatgpt.com"], "to": "fast" } ]
        }
        """);

    internal static readonly byte[] CoreSingboxJson = Encoding.UTF8.GetBytes("""
        {
          "core": "sing-box",
          "outbounds": [
            { "tag": "relay", "type": "vless", "server": "relay.example.com", "server_port": 443, "uuid": "9d2d8e4e-6f2a-4f5f-9a51-0c8e5c7b1a11",
              "tls": { "enabled": true, "server_name": "relay.example.com" } },
            { "tag": "fast", "type": "urltest", "outbounds": ["relay", "proxy"], "url": "https://www.gstatic.com/generate_204", "interval": "1m" }
          ],
          "dialer": "relay",
          "rules": [ { "domains": ["domain:chatgpt.com"], "to": "fast" } ]
        }
        """);

    private static JsonObject Reviewed(params (string Name, byte[] Content)[] files)
    {
        var reviewed = new JsonObject();
        foreach (var (name, content) in files)
        {
            reviewed[name] = V2pnReviewedFiles.Hash(content);
        }
        return reviewed;
    }

    /// <summary>A partner as V2pnPartners would build it, from a signed certificate and pack holding <paramref name="files"/>.</summary>
    private static V2pnPartner Partner((string Name, byte[] Content)[] files, string[] grants, JsonObject? reviewed, string id = "kestrel")
    {
        var p = new TestPartner();
        var certJson = p.Cert(partner: id, grants: grants, reviewed: reviewed);
        var cert = p.ReadCert(certJson);
        var assets = files.Append(("logo.png", TestPartner.Logo)).ToArray();
        var packJson = TestPartner.Pack(id: id, name: "Kestrel", assets: assets);
        var pack = V2pnPartnerTrust.ReadPack(packJson, TestPartner.Sign(p.Key, packJson), cert, out var error)!;
        if (pack == null)
        {
            throw new InvalidOperationException(error);
        }
        var content = files.ToDictionary(f => f.Name, f => f.Content);
        var features = V2pnPartnerFeatures.Read(cert, pack, name => content.GetValueOrDefault(name));
        return new V2pnPartner(cert, pack, Path.GetTempPath(), "https://config.example.com/v2pn/", features);
    }

    private static readonly string[] AllGrants = ["edition", "routing", "transport", "core"];

    #region Certificate and pack

    [Test]
    public async Task Cert_ReadsReviewedHashes_AndLeavesOutWhatItDoesNotKnow()
    {
        var p = new TestPartner();
        var reviewed = new JsonObject
        {
            ["routing.json"] = new string('A', 64),
            ["transport.json"] = "not-a-hash",
            ["theme.json"] = new string('b', 64),
        };
        var cert = p.ReadCert(p.Cert(grants: ["edition", "routing", "transport", "core"], reviewed: reviewed));

        await cert.Grants.Should().BeEquivalentTo(new[] { "edition", "routing", "transport", "core" });
        await cert.ReviewedFiles.Count.Should().BeEqualTo(1);
        await cert.ReviewedFiles["routing.json"].Should().BeEqualTo(new string('a', 64));
    }

    [Test]
    public async Task Cert_WithoutReviewed_ReadsAsBefore()
    {
        var p = new TestPartner();
        var cert = p.ReadCert(p.Cert(grants: ["edition", "routing"]));

        await cert.ReviewedFiles.Count.Should().BeEqualTo(0);
        await cert.Allows(V2pnPartnerGrants.Routing).Should().BeTrue();
    }

    [Test]
    public async Task Pack_TakesTheThreeReviewedFiles_ButNoOtherJson()
    {
        var p = new TestPartner();
        var cert = p.ReadCert();
        V2pnPartnerPack? Read(params (string, byte[])[] assets)
        {
            var json = TestPartner.Pack(assets: assets.Append(("logo.png", TestPartner.Logo)).ToArray());
            return V2pnPartnerTrust.ReadPack(json, TestPartner.Sign(p.Key, json), cert, out _);
        }

        var pack = Read(("routing.json", RoutingJson), ("transport.json", TransportJson), ("core.json", CoreXrayJson));
        await (pack != null).Should().BeTrue();
        await pack!.Assets.Count.Should().BeEqualTo(4);
        await (Read(("theme.json", RoutingJson)) == null).Should().BeTrue();
        await (Read(("routing.json", new byte[V2pnReviewedFiles.MaxBytes + 1])) == null).Should().BeTrue();
    }

    [Test]
    public async Task Pack_NeverUsesAReviewedFileAsAPicture()
    {
        var p = new TestPartner();
        var cert = p.ReadCert();
        var json = TestPartner.Pack(assets: [("routing.json", RoutingJson), ("logo.png", TestPartner.Logo)],
            more: pack => pack["brand"]!["logo"] = "routing.json");

        var pack = V2pnPartnerTrust.ReadPack(json, TestPartner.Sign(p.Key, json), cert, out _)!;

        await (pack.Logo == null).Should().BeTrue();
    }

    #endregion Certificate and pack

    #region Hash binding

    [Test]
    public async Task ReviewedFile_WithTheGrantAndItsHash_IsUsed()
    {
        var partner = Partner([("routing.json", RoutingJson), ("transport.json", TransportJson), ("core.json", CoreXrayJson)], AllGrants,
            Reviewed(("routing.json", RoutingJson), ("transport.json", TransportJson), ("core.json", CoreXrayJson)));

        await partner.Features.Files.All(f => f.State == V2pnReviewState.Ready).Should().BeTrue();
        await (partner.Features.Routing != null).Should().BeTrue();
        await (partner.Features.Transport != null).Should().BeTrue();
        await (partner.Features.Core != null).Should().BeTrue();
    }

    [Test]
    public async Task ReviewedFile_ChangedAfterTheReview_IsNotUsed()
    {
        var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(RoutingJson).Replace("\"direct\" }", "\"proxy\" }"));
        var partner = Partner([("routing.json", changed)], AllGrants, Reviewed(("routing.json", RoutingJson)));

        await partner.Features.File("routing.json")!.State.Should().BeEqualTo(V2pnReviewState.Changed);
        await (partner.Features.Routing == null).Should().BeTrue();
    }

    [Test]
    public async Task ReviewedFile_WithoutTheGrant_OrWithoutItsHash_IsNotUsed()
    {
        var noGrant = Partner([("transport.json", TransportJson)], ["edition", "routing"], Reviewed(("transport.json", TransportJson)));
        var noHash = Partner([("transport.json", TransportJson)], AllGrants, Reviewed(("routing.json", RoutingJson)));

        await noGrant.Features.File("transport.json")!.State.Should().BeEqualTo(V2pnReviewState.NotReviewed);
        await noHash.Features.File("transport.json")!.State.Should().BeEqualTo(V2pnReviewState.NotReviewed);
        await (noGrant.Features.Transport == null).Should().BeTrue();
        await (noHash.Features.Transport == null).Should().BeTrue();
    }

    [Test]
    public async Task ReviewedFile_ThatFailsItsChecks_IsInvalid()
    {
        var bad = Encoding.UTF8.GetBytes("""{ "fragment": { "length": "0-99999" } }""");
        var partner = Partner([("transport.json", bad)], AllGrants, Reviewed(("transport.json", bad)));

        await partner.Features.File("transport.json")!.State.Should().BeEqualTo(V2pnReviewState.Invalid);
        await partner.Features.File("transport.json")!.Problem!.Contains("fragment.length").Should().BeTrue();
    }

    [Test]
    public async Task Partner_DownloadedWithReviewedFiles_KeepsThemOnDisk_AndChecksThemAgain()
    {
        var p = new TestPartner();
        var root = Path.Combine(Path.GetTempPath(), "v2pn-review-test-" + Guid.NewGuid().ToString("N")[..8]);
        var cert = p.Cert(grants: ["edition", "routing"], reviewed: Reviewed(("routing.json", RoutingJson)));
        var pack = TestPartner.Pack(assets: [("logo.png", TestPartner.Logo), ("routing.json", RoutingJson)]);
        var files = new Dictionary<string, byte[]>
        {
            ["cert.json"] = cert,
            ["cert.json.sig"] = Encoding.UTF8.GetBytes(TestPartner.Sign(p.Root, cert)),
            ["partner.json"] = pack,
            ["partner.json.sig"] = Encoding.UTF8.GetBytes(TestPartner.Sign(p.Key, pack)),
            ["logo.png"] = TestPartner.Logo,
            ["routing.json"] = RoutingJson,
        };
        var manager = new V2pnPartners([p.RootPublic], () => root)
        {
            Today = () => TestPartner.Today,
            Fetch = (url, _) =>
            {
                var name = Uri.UnescapeDataString(url[(url.LastIndexOf('/') + 1)..]);
                return Task.FromResult(files.TryGetValue(name, out var body) ? new V2pnHttp.Result(HttpStatusCode.OK, body, null) : new V2pnHttp.Result(HttpStatusCode.NotFound, null, "HTTP 404"));
            },
        };
        try
        {
            var partner = await manager.RefreshAsync("https://config.example.com/s/token", "https://config.example.com/v2pn/");
            await (partner?.Features.Routing != null).Should().BeTrue();
            await File.Exists(Path.Combine(root, "configra", "routing.json")).Should().BeTrue();

            var kept = manager.ReadFolder(Path.Combine(root, "configra"), out _);
            await (kept?.Features.Routing != null).Should().BeTrue();

            // A routing.json changed on disk no longer matches the signed pack: the partner is not trusted at all.
            await File.WriteAllTextAsync(Path.Combine(root, "configra", "routing.json"), "{}");
            await (manager.ReadFolder(Path.Combine(root, "configra"), out var error) == null).Should().BeTrue();
            await error.Contains("routing.json").Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    #endregion Hash binding

    #region routing.json

    [Test]
    public async Task Routing_BecomesV2pnRules_LocalNetworkFirst_DefaultLast()
    {
        var preset = V2pnRoutingPreset.Parse(RoutingJson, out var problem)!;

        await problem.Should().BeEqualTo(string.Empty);
        var rules = preset.ToRules();
        await rules[0].Ip!.Should().BeEquivalentTo(new[] { "geoip:private" });
        await rules[0].OutboundTag.Should().BeEqualTo(Global.DirectTag);
        await rules[1].Domain!.Should().BeEquivalentTo(new[] { "geosite:private" });
        await rules[2].Domain!.Should().BeEquivalentTo(new[] { "geosite:youtube", "domain:openai.com", "full:chat.openai.com" });
        await rules[2].OutboundTag.Should().BeEqualTo(Global.ProxyTag);
        await rules[3].Ip!.Should().BeEquivalentTo(new[] { "91.108.4.0/22" });
        await rules[5].OutboundTag.Should().BeEqualTo(Global.BlockTag);
        await rules[^1].Port.Should().BeEqualTo("0-65535");
        await rules[^1].OutboundTag.Should().BeEqualTo(Global.DirectTag);
        await preset.UsesGeoFiles.Should().BeTrue();
        await preset.DomainStrategy.Should().BeEqualTo(Global.IPOnDemand);
    }

    [Test]
    [Arguments("""{ "rules": [ { "ips": ["geoip:ru"], "to": "block" } ] }""", "geoip")]
    [Arguments("""{ "rules": [ { "domains": ["domain:ru"], "to": "block" } ] }""", "zone")]
    [Arguments("""{ "rules": [ { "ips": ["10.0.0.0/8"], "to": "block" } ] }""", "wider")]
    [Arguments("""{ "rules": [ { "ips": ["192.168.1.0/24"], "to": "block" } ] }""", "local network")]
    [Arguments("""{ "rules": [ { "domains": ["keyword:vk"], "to": "block" } ] }""", "keyword")]
    [Arguments("""{ "rules": [ { "domains": ["regexp:.*"], "to": "proxy" } ] }""", "regexp")]
    [Arguments("""{ "rules": [ { "domains": ["a.com"], "to": "elsewhere" } ] }""", "to")]
    [Arguments("""{ "rules": [ { "domains": ["a.com"], "to": "proxy" } ], "default": "block" }""", "default")]
    [Arguments("""{ "rules": [ { "to": "proxy" } ] }""", "no domains")]
    [Arguments("""{ "rules": [] }""", "rules")]
    [Arguments("""{ "rules": [ { "ips": ["1.2.3"], "to": "proxy" } ] }""", "not an IP")]
    [Arguments("""{ "schema": 2, "rules": [ { "domains": ["a.com"], "to": "proxy" } ] }""", "schema")]
    public async Task Routing_RefusesBroadBlocksAndBadEntries(string json, string expected)
    {
        var preset = V2pnRoutingPreset.Parse(Encoding.UTF8.GetBytes(json), out var problem);

        await (preset == null).Should().BeTrue();
        await problem.Contains(expected).Should().BeTrue();
    }

    [Test]
    public async Task Routing_KeepsItsLimits()
    {
        var rules = new JsonArray();
        for (var i = 0; i <= V2pnRoutingPreset.MaxRules; i++)
        {
            rules.Add(new JsonObject { ["domains"] = new JsonArray(JsonValue.Create($"site{i}.com")), ["to"] = "proxy" });
        }
        var tooMany = new JsonObject { ["rules"] = rules };
        var many = new JsonArray();
        for (var i = 0; i <= V2pnRoutingPreset.MaxEntriesPerRule; i++)
        {
            many.Add(JsonValue.Create($"site{i}.com"));
        }
        var tooLong = new JsonObject { ["rules"] = new JsonArray(new JsonObject { ["domains"] = many, ["to"] = "proxy" }) };
        var longName = new JsonObject { ["name"] = new string('x', 41), ["rules"] = new JsonArray(new JsonObject { ["domains"] = new JsonArray(JsonValue.Create("a.com")), ["to"] = "proxy" }) };

        await (V2pnRoutingPreset.Parse(Encoding.UTF8.GetBytes(tooMany.ToJsonString()), out _) == null).Should().BeTrue();
        await (V2pnRoutingPreset.Parse(Encoding.UTF8.GetBytes(tooLong.ToJsonString()), out _) == null).Should().BeTrue();
        await (V2pnRoutingPreset.Parse(Encoding.UTF8.GetBytes(longName.ToJsonString()), out _) == null).Should().BeTrue();
    }

    [Test]
    public async Task RouteEntries_SpotWhatTouchesDns()
    {
        await V2pnRouteEntries.AffectsDns("1.1.1.1").Should().BeTrue();
        await V2pnRouteEntries.AffectsDns("8.8.8.8/32").Should().BeTrue();
        await V2pnRouteEntries.AffectsDns("full:dns.google").Should().BeTrue();
        await V2pnRouteEntries.AffectsDns("domain:cloudflare-dns.com").Should().BeTrue();
        await V2pnRouteEntries.AffectsDns("domain:youtube.com").Should().BeFalse();
        await V2pnRouteEntries.AffectsDns("91.108.4.0/22").Should().BeFalse();
    }

    #endregion routing.json

    #region transport.json

    [Test]
    public async Task Transport_ReadsFragmentAndNoise()
    {
        var transport = V2pnTransport.Parse(TransportJson, out var problem)!;

        await problem.Should().BeEqualTo(string.Empty);
        await transport.Fragment!.Length.Should().BeEqualTo("100-200");
        await transport.Noise.Count.Should().BeEqualTo(1);
    }

    [Test]
    [Arguments("""{ "fragment": { "packets": "1-50" } }""", "packets")]
    [Arguments("""{ "fragment": { "length": "300-200" } }""", "length")]
    [Arguments("""{ "fragment": { "interval": "10-5000" } }""", "interval")]
    [Arguments("""{ "noise": [ { "rand": "0-2000" } ] }""", "noise[0]")]
    [Arguments("""{ "mux": { "enabled": true } }""", "neither")]
    public async Task Transport_RefusesValuesOutOfRange(string json, string expected)
    {
        var transport = V2pnTransport.Parse(Encoding.UTF8.GetBytes(json), out var problem);

        await (transport == null).Should().BeTrue();
        await problem.Contains(expected).Should().BeTrue();
    }

    [Test]
    public async Task Transport_OnXray_FragmentsTlsServersDialedDirectly_NoiseOnlyOnUdp()
    {
        var transport = V2pnTransport.Parse(TransportJson, out _)!;
        var reality = JsonNode.Parse("""{ "streamSettings": { "security": "reality" } }""")!.AsObject();
        var viaRelay = JsonNode.Parse("""{ "streamSettings": { "security": "tls", "sockopt": { "dialerProxy": "relay" } } }""")!.AsObject();
        var kcp = JsonNode.Parse("""{ "streamSettings": { "network": "kcp", "finalmask": { "udp": [ { "type": "mkcp-legacy" } ] } } }""")!.AsObject();
        var plain = JsonNode.Parse("""{ "streamSettings": { "network": "raw" } }""")!.AsObject();

        transport.ApplyXray(reality);
        transport.ApplyXray(viaRelay);
        transport.ApplyXray(kcp);
        transport.ApplyXray(plain);

        await reality["streamSettings"]!["finalmask"]!["tcp"]![0]!["settings"]!["packets"]!.GetValue<string>().Should().BeEqualTo("1-3");
        await (viaRelay["streamSettings"]!["finalmask"] == null).Should().BeTrue();
        await kcp["streamSettings"]!["finalmask"]!["udp"]!.AsArray().Count.Should().BeEqualTo(2);
        await kcp["streamSettings"]!["finalmask"]!["udp"]![1]!["type"]!.GetValue<string>().Should().BeEqualTo("noise");
        await (plain["streamSettings"]!["finalmask"] == null).Should().BeTrue();
    }

    #endregion transport.json

    #region core.json

    [Test]
    public async Task Core_Xray_PrefixesTagsAndRewritesReferences()
    {
        var core = V2pnCoreFragment.Parse(CoreXrayJson, out var problem)!;

        await problem.Should().BeEqualTo(string.Empty);
        var outbounds = core.OutboundsFor("kestrel", V2pnTransport.Parse(TransportJson, out _));
        await outbounds[0]["tag"]!.GetValue<string>().Should().BeEqualTo("partner-kestrel-relay");
        await outbounds[1]["streamSettings"]!["sockopt"]!["dialerProxy"]!.GetValue<string>().Should().BeEqualTo("partner-kestrel-relay");
        // The relay dials directly and speaks TLS, so it gets the partner's fragment; "us" goes through the relay.
        await outbounds[0]["streamSettings"]!["finalmask"]!["tcp"]![0]!["type"]!.GetValue<string>().Should().BeEqualTo("fragment");
        await (outbounds[1]["streamSettings"]!["finalmask"] == null).Should().BeTrue();
        await core.DialerFor("kestrel").Should().BeEqualTo("partner-kestrel-relay");
        var balancer = core.XrayBalancers("kestrel")[0];
        await balancer.tag.Should().BeEqualTo("partner-kestrel-fast-balancer");
        await balancer.selector!.Should().BeEquivalentTo(new[] { "partner-kestrel-us", "proxy" });
        var rules = core.RulesFor("kestrel");
        await rules[0].OutboundTag.Should().BeEqualTo("partner-kestrel-fast");
        await core.Owns("kestrel", "partner-kestrel-fast").Should().BeTrue();
        await core.Owns("kestrel", "partner-kestrel-other").Should().BeFalse();
        await core.Owns("other", "partner-kestrel-fast").Should().BeFalse();
    }

    [Test]
    [Arguments("""{ "core": "xray", "outbounds": [ { "tag": "out", "protocol": "freedom" } ] }""", "protocol")]
    [Arguments("""{ "core": "xray", "outbounds": [ { "tag": "out", "protocol": "vless", "streamSettings": { "tlsSettings": { "certificates": [ { "certificateFile": "/etc/x" } ] } } } ] }""", "filter")]
    [Arguments("""{ "core": "xray", "outbounds": [ { "tag": "proxy", "protocol": "vless" } ] }""", "V2pn's own")]
    [Arguments("""{ "core": "xray", "outbounds": [ { "tag": "a", "protocol": "vless", "streamSettings": { "sockopt": { "dialerProxy": "nowhere" } } } ] }""", "not an outbound")]
    [Arguments("""{ "core": "xray", "outbounds": [ { "tag": "a", "protocol": "vless" } ], "rules": [ { "domains": ["a.com"], "to": "direct" } ] }""", "to")]
    [Arguments("""{ "core": "xray", "outbounds": [ { "tag": "a", "protocol": "vless" } ], "rules": [ { "ips": ["10.1.0.0/16"], "to": "a" } ] }""", "stays on this computer")]
    [Arguments("""{ "core": "xray", "inbounds": [] }""", "not a key")]
    [Arguments("""{ "core": "sing-box", "outbounds": [ { "tag": "a", "type": "vless" } ], "balancers": [] }""", "Xray only")]
    [Arguments("""{ "core": "sing-box", "outbounds": [ { "tag": "a", "type": "tor" } ] }""", "filter")]
    [Arguments("""{ "core": "sing-box", "outbounds": [ { "tag": "a", "type": "vless" } ], "allowTop": ["experimental"] }""", "allowTop")]
    [Arguments("""{ "core": "xray", "allowTop": ["api"] }""", "allowTop")]
    [Arguments("""{ "core": "clash" }""", "core")]
    public async Task Core_RefusesWhatTheFilterOrTheReviewRulesForbid(string json, string expected)
    {
        var core = V2pnCoreFragment.Parse(Encoding.UTF8.GetBytes(json), out var problem);

        await (core == null).Should().BeTrue();
        await problem.Contains(expected).Should().BeTrue();
    }

    [Test]
    public async Task Core_Singbox_RewritesDetourAndUrltest()
    {
        var core = V2pnCoreFragment.Parse(CoreSingboxJson, out var problem)!;

        await problem.Should().BeEqualTo(string.Empty);
        var outbounds = core.OutboundsFor("kestrel", V2pnTransport.Parse(TransportJson, out _));
        await outbounds[0]["tls"]!["fragment"]!.GetValue<bool>().Should().BeTrue();
        await outbounds[1]["outbounds"]!.AsArray().Select(n => n!.GetValue<string>()).ToList().Should().BeEquivalentTo(new[] { "partner-kestrel-relay", "proxy" });
    }

    [Test]
    public async Task WholeConfig_KeepsOnlyTheTopKeysTheReviewAllows_WithoutFiles()
    {
        var config = JsonNode.Parse("""
            { "outbounds": [ { "type": "direct", "tag": "direct" } ],
              "ntp": { "enabled": true, "server": "time.apple.com" },
              "certificate": { "store": "mozilla", "certificate_path": ["C:/evil.pem"] },
              "experimental": { "clash_api": { "external_controller": "0.0.0.0:9090" } } }
            """)!.AsObject();

        var removed = V2pnCoreConfigFilter.FilterSingbox(config, ["ntp", "certificate", "experimental"]);

        await config.ContainsKey("ntp").Should().BeTrue();
        await config.ContainsKey("certificate").Should().BeTrue();
        await config["certificate"]!.AsObject().ContainsKey("certificate_path").Should().BeFalse();
        await config.ContainsKey("experimental").Should().BeFalse();
        await removed.Should().Contain("experimental");
        var plain = JsonNode.Parse("""{ "ntp": { "enabled": true } }""")!.AsObject();
        V2pnCoreConfigFilter.FilterSingbox(plain);
        await plain.ContainsKey("ntp").Should().BeFalse();
    }

    #endregion core.json

    #region Generated configs

    private static ProfileItem VlessNode(string id, string subId, ECoreType core) => new()
    {
        IndexId = id,
        ConfigType = EConfigType.VLESS,
        CoreType = core,
        Remarks = id,
        Address = id + ".example.com",
        Port = 443,
        Password = "9d2d8e4e-6f2a-4f5f-9a51-0c8e5c7b1a11",
        Network = nameof(ETransport.raw),
        StreamSecurity = Global.StreamSecurity,
        Sni = id + ".example.com",
        Subid = subId,
    };

    private static CoreConfigContext Context(ProfileItem node, ProfileItem other, ECoreType core, byte[] coreJson)
    {
        var config = CoreConfigTestFactory.CreateConfig(core);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var traffic = new V2pnPartnerTraffic("kestrel", "Kestrel", V2pnTransport.Parse(TransportJson, out _), V2pnCoreFragment.Parse(coreJson, out _));
        var context = CoreConfigTestFactory.CreateContext(config, node, core) with
        {
            V2pnPartnerSubs = new Dictionary<string, V2pnPartnerTraffic> { ["sub-kestrel"] = traffic },
        };
        context.AllProxiesMap[other.IndexId] = other;
        return context;
    }

    [Test]
    public async Task Xray_PartnerServer_GetsItsTransportDialerBalancerAndRules()
    {
        var node = VlessNode("kestrel-de", "sub-kestrel", ECoreType.Xray);
        var context = Context(node, node, ECoreType.Xray, CoreXrayJson);

        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();

        await result.Success.Should().BeTrue();
        var config = JsonNode.Parse(result.Data!.ToString()!)!.AsObject();
        var outbounds = config["outbounds"]!.AsArray().Select(o => o!.AsObject()).ToList();
        var proxy = outbounds.First(o => o["tag"]!.GetValue<string>() == Global.ProxyTag);
        await proxy["streamSettings"]!["sockopt"]!["dialerProxy"]!.GetValue<string>().Should().BeEqualTo("partner-kestrel-relay");
        var relay = outbounds.First(o => o["tag"]!.GetValue<string>() == "partner-kestrel-relay");
        await relay["streamSettings"]!["finalmask"]!["tcp"]![0]!["settings"]!["lengths"]![0]!.GetValue<string>().Should().BeEqualTo("100-200");
        await outbounds.Any(o => o["tag"]!.GetValue<string>() == "partner-kestrel-us").Should().BeTrue();
        await config["routing"]!["balancers"]!.AsArray().Any(b => b!["tag"]!.GetValue<string>() == "partner-kestrel-fast-balancer").Should().BeTrue();
        await config["observatory"]!["subjectSelector"]!.AsArray().Any(s => s!.GetValue<string>() == "partner-kestrel-us").Should().BeTrue();
        var rule = config["routing"]!["rules"]!.AsArray().First(r => r!["balancerTag"]?.GetValue<string>() == "partner-kestrel-fast-balancer")!;
        await rule["domain"]!.AsArray().Select(d => d!.GetValue<string>()).ToList().Should().Contain("domain:chatgpt.com");
    }

    [Test]
    public async Task Xray_PartnerFragment_OnlyOnThePartnersServer()
    {
        // Without core.json: the partner's own server dials directly and gets the fragment; a server of another subscription does not.
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var traffic = new V2pnPartnerTraffic("kestrel", "Kestrel", V2pnTransport.Parse(TransportJson, out _), null);
        var subs = new Dictionary<string, V2pnPartnerTraffic> { ["sub-kestrel"] = traffic };
        string? Fragment(ProfileItem node)
        {
            var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray) with { V2pnPartnerSubs = subs };
            var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();
            var proxy = JsonNode.Parse(result.Data!.ToString()!)!["outbounds"]!.AsArray().First(o => o!["tag"]!.GetValue<string>() == Global.ProxyTag)!;
            return proxy["streamSettings"]?["finalmask"]?["tcp"]?[0]?["settings"]?["lengths"]?[0]?.GetValue<string>();
        }

        await Fragment(VlessNode("kestrel-de", "sub-kestrel", ECoreType.Xray)).Should().BeEqualTo("100-200");
        await (Fragment(VlessNode("own", "sub-other", ECoreType.Xray)) == null).Should().BeTrue();
    }

    [Test]
    public async Task Singbox_PartnerServer_GetsFragmentDetourAndOutbounds_OtherServersDoNot()
    {
        var partnerNode = VlessNode("kestrel-de", "sub-kestrel", ECoreType.sing_box);
        var ownNode = VlessNode("own", "sub-other", ECoreType.sing_box);

        var partnerConfig = JsonNode.Parse(new CoreConfigSingboxService(Context(partnerNode, ownNode, ECoreType.sing_box, CoreSingboxJson))
            .GenerateClientConfigContent().Data!.ToString()!)!.AsObject();
        var ownConfig = JsonNode.Parse(new CoreConfigSingboxService(Context(ownNode, partnerNode, ECoreType.sing_box, CoreSingboxJson))
            .GenerateClientConfigContent().Data!.ToString()!)!.AsObject();

        var proxy = partnerConfig["outbounds"]!.AsArray().First(o => o!["tag"]!.GetValue<string>() == Global.ProxyTag)!;
        await proxy["tls"]!["fragment"]!.GetValue<bool>().Should().BeTrue();
        await proxy["detour"]!.GetValue<string>().Should().BeEqualTo("partner-kestrel-relay");
        await partnerConfig["outbounds"]!.AsArray().Any(o => o!["tag"]!.GetValue<string>() == "partner-kestrel-fast").Should().BeTrue();
        await partnerConfig["route"]!["rules"]!.AsArray().Any(r => r!["outbound"]?.GetValue<string>() == "partner-kestrel-fast").Should().BeTrue();

        var own = ownConfig["outbounds"]!.AsArray().First(o => o!["tag"]!.GetValue<string>() == Global.ProxyTag)!;
        await (own["tls"]!["fragment"] == null).Should().BeTrue();
        await (own["detour"] == null).Should().BeTrue();
        await ownConfig["outbounds"]!.AsArray().Any(o => o!["tag"]!.GetValue<string>().StartsWith("partner-")).Should().BeFalse();
    }

    #endregion Generated configs

    #region Routing items

    [Test]
    public async Task Routes_AreCreated_Updated_AndRemovedWithTheirPartner()
    {
        var partner = Partner([("routing.json", RoutingJson)], AllGrants, Reviewed(("routing.json", RoutingJson)));
        var all = new RoutingItem { Id = V2pnPartnerRoutes.FallbackId, Remarks = "All", Sort = 1 };

        var first = V2pnPartnerRoutes.Plan([all], [partner]);
        await first.Upserts.Count.Should().BeEqualTo(1);
        var item = first.Upserts[0];
        await item.Id.Should().BeEqualTo("v2pn-partner-kestrel");
        await (JsonUtils.Deserialize<List<RulesItem>>(item.RuleSet)!.Count == partner.Features.Routing!.ToRules().Count).Should().BeTrue();

        // Unchanged: nothing to write.
        item.IsActive = true;
        await V2pnPartnerRoutes.Plan([all, item], [partner]).Any.Should().BeFalse();

        // The partner went away (unlinked, expired, revoked: it is no longer among the partners): the active preset goes, "All" takes over.
        var gone = V2pnPartnerRoutes.Plan([all, item], []);
        await gone.Deletes.Single().Id.Should().BeEqualTo("v2pn-partner-kestrel");
        await gone.Activate!.Id.Should().BeEqualTo(V2pnPartnerRoutes.FallbackId);
        await gone.ActiveChanged.Should().BeTrue();

        // Its routing.json changed after the review: the preset goes the same way.
        var changed = Partner([("routing.json", TransportJson)], AllGrants, Reviewed(("routing.json", RoutingJson)));
        await V2pnPartnerRoutes.Plan([all, item], [changed]).Deletes.Count.Should().BeEqualTo(1);
    }

    [Test]
    public async Task Traffic_IsOffWhenThePersonTurnedItOff()
    {
        var partner = Partner([("transport.json", TransportJson)], AllGrants, Reviewed(("transport.json", TransportJson)));

        await (V2pnPartnerTraffics.For(partner, new SubInfoItem { Id = "s" })?.Transport != null).Should().BeTrue();
        await (V2pnPartnerTraffics.For(partner, new SubInfoItem { Id = "s", PartnerConnectionOff = true }) == null).Should().BeTrue();
        await (V2pnPartnerTraffics.For(null, new SubInfoItem { Id = "s" }) == null).Should().BeTrue();
    }

    #endregion Routing items
}
