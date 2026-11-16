using ServiceLib.Services.CoreConfig;
using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>Reviewed files of a partner: routing.json, transport.json, core.json, bound to the signed index by hash.</summary>
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

    /// <summary>A partner as V2pnPartners would build it, from a listing and a pack holding <paramref name="files"/>.</summary>
    private static V2pnPartner Partner((string Name, byte[] Content)[] files, string[] grants, JsonObject? reviewed, string id = "kestrel")
    {
        var cert = TestPartner.Listing(partner: id, grants: grants, reviewed: reviewed);
        var assets = files.Append(("logo.png", TestPartner.Logo)).ToArray();
        var packJson = TestPartner.Pack(id: id, name: "Kestrel", assets: assets);
        var pack = V2pnPartnerTrust.ReadPack(packJson, cert, out var error)!;
        if (pack == null)
        {
            throw new InvalidOperationException(error);
        }
        var content = files.ToDictionary(f => f.Name, f => f.Content);
        var features = V2pnPartnerFeatures.Read(cert, pack, name => content.GetValueOrDefault(name));
        return new V2pnPartner(cert, pack, Path.GetTempPath(), features);
    }

    private static readonly string[] AllGrants = ["edition", "routing", "transport", "core"];

    #region Listing and pack

    /// <summary>The listing as V2pn reads it from a signed index whose entry has <paramref name="reviewed"/> as written.</summary>
    private static V2pnPartnerListing FromIndex(string[] grants, JsonObject? reviewed)
    {
        var p = new TestPartner();
        var root = (JsonObject)JsonNode.Parse(V2pnPartnerPublisher.IndexBytes([TestPartner.Listing(grants: grants)], DateTimeOffset.UnixEpoch))!;
        root["partners"]![0]!["reviewed"] = reviewed;
        var json = Encoding.UTF8.GetBytes(root.ToJsonString());
        return V2pnPartnerTrust.ReadIndex(json, TestPartner.Sign(p.Root, json), [p.RootPublic], out _)!.Partners[0];
    }

    [Test]
    public async Task Listing_ReadsReviewedHashes_AndLeavesOutWhatItDoesNotKnow()
    {
        var reviewed = new JsonObject
        {
            ["routing.json"] = new string('A', 64),
            ["transport.json"] = "not-a-hash",
            ["theme.json"] = new string('b', 64),
        };
        var cert = FromIndex(["edition", "routing", "transport", "core"], reviewed);

        await cert.Grants.Should().BeEquivalentTo(new[] { "edition", "routing", "transport", "core" });
        await cert.ReviewedFiles.Count.Should().BeEqualTo(1);
        await cert.ReviewedFiles["routing.json"].Should().BeEqualTo(new string('a', 64));
    }

    [Test]
    public async Task Listing_WithoutReviewed_ReadsAsBefore()
    {
        var cert = FromIndex(["edition", "routing"], null);

        await cert.ReviewedFiles.Count.Should().BeEqualTo(0);
        await cert.Allows(V2pnPartnerGrants.Routing).Should().BeTrue();
    }

    [Test]
    public async Task Pack_TakesTheThreeReviewedFiles_ButNoOtherJson()
    {
        var cert = TestPartner.Listing();
        V2pnPartnerPack? Read(params (string, byte[])[] assets)
        {
            var json = TestPartner.Pack(assets: assets.Append(("logo.png", TestPartner.Logo)).ToArray());
            return V2pnPartnerTrust.ReadPack(json, cert, out _);
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
        var cert = TestPartner.Listing();
        var json = TestPartner.Pack(assets: [("routing.json", RoutingJson), ("logo.png", TestPartner.Logo)],
            more: pack => pack["brand"]!["logo"] = "routing.json");

        var pack = V2pnPartnerTrust.ReadPack(json, cert, out _)!;

        await (pack.Logo == null).Should().BeTrue();
    }

    #endregion Listing and pack

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
        var site = new TestSite();
        var root = Path.Combine(Path.GetTempPath(), "v2pn-review-test-" + Guid.NewGuid().ToString("N")[..8]);
        var pack = TestPartner.Pack(assets: [("logo.png", TestPartner.Logo), ("routing.json", RoutingJson)]);
        var zip = TestPartner.Zip(pack, ("logo.png", TestPartner.Logo), ("routing.json", RoutingJson));
        // The index names the reviewed hash; the pack's own copy of the file must have it.
        p.Publish(site, DateTimeOffset.UnixEpoch, (TestPartner.Listing(grants: ["edition", "routing"], reviewed: Reviewed(("routing.json", RoutingJson)), zip: zip), zip));
        var manager = p.Manager(site, root, ["https://config.example.com/s/token"]);
        try
        {
            await manager.RefreshAsync();
            await (manager.Get("configra")?.Features.Routing != null).Should().BeTrue();
            await File.Exists(Path.Combine(root, "configra", "routing.json")).Should().BeTrue();

            var listing = manager.Index!.Get("configra")!;
            var kept = manager.ReadFolder(Path.Combine(root, "configra"), listing, out _);
            await (kept?.Features.Routing != null).Should().BeTrue();

            // A routing.json changed on disk no longer matches the kept pack: the partner is not trusted at all.
            await File.WriteAllTextAsync(Path.Combine(root, "configra", "routing.json"), "{}");
            await (manager.ReadFolder(Path.Combine(root, "configra"), listing, out var error) == null).Should().BeTrue();
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

    [Test]
    public async Task ReviewedFile_IsUsedOnlyOnceTheIndexNamesItsHash()
    {
        var p = new TestPartner();
        var site = new TestSite();
        var root = Path.Combine(Path.GetTempPath(), "v2pn-review-test-" + Guid.NewGuid().ToString("N")[..8]);
        var pack = TestPartner.Pack(assets: [("logo.png", TestPartner.Logo), ("routing.json", RoutingJson)]);
        var zip = TestPartner.Zip(pack, ("logo.png", TestPartner.Logo), ("routing.json", RoutingJson));
        p.Publish(site, DateTimeOffset.UnixEpoch, (TestPartner.Listing(grants: ["edition", "routing"], zip: zip), zip));
        var manager = p.Manager(site, root, ["https://config.example.com/s/token"]);
        try
        {
            await manager.RefreshAsync();
            await manager.Get("configra")!.Features.File("routing.json")!.State.Should().BeEqualTo(V2pnReviewState.NotReviewed);

            // The owner read it and published its hash: the same zip, a new listing.
            p.Publish(site, DateTimeOffset.UnixEpoch.AddDays(1),
                (TestPartner.Listing(grants: ["edition", "routing"], reviewed: Reviewed(("routing.json", RoutingJson)), zip: zip), zip));
            await manager.RefreshAsync(forceIndex: true);
            await manager.Get("configra")!.Features.File("routing.json")!.State.Should().BeEqualTo(V2pnReviewState.Ready);
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

    private static ProfileItem VlessNode(string id, string subId, ECoreType core)
    {
        var node = new ProfileItem
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
        node.SetProtocolExtra(node.GetProtocolExtra() with { VlessEncryption = Global.None });
        return node;
    }

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

        // Dialed through the relay: the relay carries the fragment (its own TLS), the server inside it does not.
        var proxy = partnerConfig["outbounds"]!.AsArray().First(o => o!["tag"]!.GetValue<string>() == Global.ProxyTag)!;
        await proxy["detour"]!.GetValue<string>().Should().BeEqualTo("partner-kestrel-relay");
        await (proxy["tls"]!["fragment"] == null).Should().BeTrue();
        var relay = partnerConfig["outbounds"]!.AsArray().First(o => o!["tag"]!.GetValue<string>() == "partner-kestrel-relay")!;
        await relay["tls"]!["fragment"]!.GetValue<bool>().Should().BeTrue();
        // Without core.json the partner's own server dials directly and gets the fragment.
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        var transportOnly = CoreConfigTestFactory.CreateContext(config, partnerNode, ECoreType.sing_box) with
        {
            V2pnPartnerSubs = new Dictionary<string, V2pnPartnerTraffic> { ["sub-kestrel"] = new("kestrel", "Kestrel", V2pnTransport.Parse(TransportJson, out _), null) },
        };
        var direct = JsonNode.Parse(new CoreConfigSingboxService(transportOnly).GenerateClientConfigContent().Data!.ToString()!)!["outbounds"]!.AsArray()
            .First(o => o!["tag"]!.GetValue<string>() == Global.ProxyTag)!;
        await direct["tls"]!["fragment"]!.GetValue<bool>().Should().BeTrue();
        await partnerConfig["outbounds"]!.AsArray().Any(o => o!["tag"]!.GetValue<string>() == "partner-kestrel-fast").Should().BeTrue();
        await partnerConfig["route"]!["rules"]!.AsArray().Any(r => r!["outbound"]?.GetValue<string>() == "partner-kestrel-fast").Should().BeTrue();

        var own = ownConfig["outbounds"]!.AsArray().First(o => o!["tag"]!.GetValue<string>() == Global.ProxyTag)!;
        await (own["tls"]!["fragment"] == null).Should().BeTrue();
        await (own["detour"] == null).Should().BeTrue();
        await ownConfig["outbounds"]!.AsArray().Any(o => o!["tag"]!.GetValue<string>().StartsWith("partner-")).Should().BeFalse();
    }

    /// <summary>
    /// With V2PN_CORES set to a folder like dist/V2pn-win-x64/bin (xray/, sing_box/, geo files), the configs generated
    /// for a partner's server with its transport and core.json are checked by the bundled cores themselves; they are
    /// kept in V2PN_CORES_OUT (or a temp folder) to read. Without it the test does nothing.
    /// </summary>
    [Test]
    public async Task GeneratedConfigs_PassTheBundledCores()
    {
        var cores = Environment.GetEnvironmentVariable("V2PN_CORES");
        if (cores.IsNullOrEmpty() || !Directory.Exists(cores))
        {
            return;
        }
        var scratch = Environment.GetEnvironmentVariable("V2PN_CORES_OUT") is { Length: > 0 } outDir
            ? outDir
            : Path.Combine(Path.GetTempPath(), "v2pn-partner-cores-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(scratch);

        var xrayNode = VlessNode("kestrel-de", "sub-kestrel", ECoreType.Xray);
        var xray = new CoreConfigV2rayService(Context(xrayNode, xrayNode, ECoreType.Xray, CoreXrayJson)).GenerateClientConfigContent().Data!.ToString()!;
        var singboxNode = VlessNode("kestrel-de", "sub-kestrel", ECoreType.sing_box);
        var singbox = new CoreConfigSingboxService(Context(singboxNode, singboxNode, ECoreType.sing_box, CoreSingboxJson)).GenerateClientConfigContent().Data!.ToString()!;
        await File.WriteAllTextAsync(Path.Combine(scratch, "xray-partner.json"), xray);
        await File.WriteAllTextAsync(Path.Combine(scratch, "sing-box-partner.json"), singbox);

        async Task<(int Code, string Output)> Run(string exe, params string[] arguments)
        {
            var start = new ProcessStartInfo(exe)
            {
                WorkingDirectory = scratch,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            start.Environment["XRAY_LOCATION_ASSET"] = cores;
            using var process = Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, output);
        }

        var exe = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var xrayResult = await Run(Path.Combine(cores, "xray", "xray" + exe), "run", "-test", "-c", "xray-partner.json");
        await File.WriteAllTextAsync(Path.Combine(scratch, "xray-test.txt"), xrayResult.Output);
        await xrayResult.Output.Contains("Configuration OK").Should().BeTrue();
        var singboxResult = await Run(Path.Combine(cores, "sing_box", "sing-box" + exe), "check", "-c", "sing-box-partner.json");
        await File.WriteAllTextAsync(Path.Combine(scratch, "sing-box-check.txt"), singboxResult.Output);
        await singboxResult.Code.Should().BeEqualTo(0);
    }

    /// <summary>The person's mode: chatgpt.com direct, everything else through the VPN (a catch-all at the end).</summary>
    private static RoutingItem PersonRouting() => new()
    {
        Id = "person",
        Remarks = "mine",
        DomainStrategy = Global.AsIs,
        DomainStrategy4Singbox = string.Empty,
        RuleSet = JsonUtils.Serialize(new List<RulesItem>
        {
            new() { Id = "r1", Enabled = true, OutboundTag = Global.DirectTag, Domain = ["domain:chatgpt.com"] },
            new() { Id = "r2", Enabled = true, OutboundTag = Global.ProxyTag, Port = "0-65535" },
        }, false),
    };

    [Test]
    public async Task Xray_PersonsOwnRulesComeBeforeThePartnersCoreRules_TheCatchAllAfterThem()
    {
        var node = VlessNode("kestrel-de", "sub-kestrel", ECoreType.Xray);
        var context = Context(node, node, ECoreType.Xray, CoreXrayJson) with { RoutingItem = PersonRouting() };

        var config = JsonNode.Parse(new CoreConfigV2rayService(context).GenerateClientConfigContent().Data!.ToString()!)!;
        var rules = config["routing"]!["rules"]!.AsArray().Select(r => r!.AsObject()).ToList();

        var person = rules.FindIndex(r => r["outboundTag"]?.GetValue<string>() == Global.DirectTag
                                          && r["domain"]?.AsArray().Any(d => d!.GetValue<string>() == "domain:chatgpt.com") == true);
        var partner = rules.FindIndex(r => r["balancerTag"]?.GetValue<string>() == "partner-kestrel-fast-balancer");
        var catchAll = rules.FindIndex(r => r["port"]?.GetValue<string>() == "0-65535");
        await (person >= 0 && partner > person).Should().BeTrue();
        await (catchAll > partner).Should().BeTrue();
    }

    [Test]
    public async Task Singbox_PersonsOwnRulesComeBeforeThePartnersCoreRules_TheCatchAllAfterThem()
    {
        var node = VlessNode("kestrel-de", "sub-kestrel", ECoreType.sing_box);
        var context = Context(node, node, ECoreType.sing_box, CoreSingboxJson) with { RoutingItem = PersonRouting() };

        var config = JsonNode.Parse(new CoreConfigSingboxService(context).GenerateClientConfigContent().Data!.ToString()!)!;
        var rules = config["route"]!["rules"]!.AsArray().Select(r => r!.AsObject()).ToList();

        var person = rules.FindIndex(r => r["outbound"]?.GetValue<string>() == Global.DirectTag
                                          && r["domain_suffix"]?.AsArray().Any(d => d!.GetValue<string>() == "chatgpt.com") == true);
        var partner = rules.FindIndex(r => r["outbound"]?.GetValue<string>() == "partner-kestrel-fast");
        var catchAll = rules.FindIndex(r => r["outbound"]?.GetValue<string>() == Global.ProxyTag && r["port_range"] != null);
        await (person >= 0 && partner > person).Should().BeTrue();
        await (catchAll > partner).Should().BeTrue();
    }

    [Test]
    public async Task RouteCatchAll_IsOnlyTheRuleThatMatchesEverything()
    {
        await V2pnPartnerRoutes.IsCatchAll(new RulesItem { Enabled = true, Port = "0-65535", OutboundTag = "proxy" }).Should().BeTrue();
        await V2pnPartnerRoutes.IsCatchAll(new RulesItem { Enabled = true, Network = "tcp,udp", OutboundTag = "proxy" }).Should().BeTrue();
        await V2pnPartnerRoutes.IsCatchAll(new RulesItem { Enabled = true, Port = "50000-65535", Network = "udp" }).Should().BeFalse();
        await V2pnPartnerRoutes.IsCatchAll(new RulesItem { Enabled = true, Port = "0-65535", Domain = ["a.com"] }).Should().BeFalse();
        await V2pnPartnerRoutes.IsCatchAll(new RulesItem { Enabled = true, Protocol = ["bittorrent"] }).Should().BeFalse();
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

    [Test]
    public async Task PartnerMode_KeepsThePersonsOwnRulesFirst()
    {
        var partner = Partner([("routing.json", RoutingJson)], AllGrants, Reviewed(("routing.json", RoutingJson)));
        var created = V2pnPartnerRoutes.Plan([], [partner]).Upserts.Single();
        var presetRules = JsonUtils.Deserialize<List<RulesItem>>(created.RuleSet)!;
        // The person adds a rule to the preset in the routing editor (it lands at the end, with a generated id).
        presetRules.Add(new RulesItem { Id = "my-own", Enabled = true, OutboundTag = Global.DirectTag, Domain = ["domain:chatgpt.com"] });
        created.RuleSet = JsonUtils.Serialize(presetRules, false);

        var again = V2pnPartnerRoutes.Plan([created], [partner]).Upserts.Single();
        var rules = JsonUtils.Deserialize<List<RulesItem>>(again.RuleSet)!;

        await rules[0].Id.Should().BeEqualTo("my-own");
        await rules.Count.Should().BeEqualTo(partner.Features.Routing!.ToRules().Count + 1);
        // Once ordered, nothing more to write.
        await V2pnPartnerRoutes.Plan([again], [partner]).Any.Should().BeFalse();
    }

    #endregion Routing items
}
