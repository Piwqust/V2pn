namespace ServiceLib.Tests.V2pn;

/// <summary>What a subscription may weigh, and what of it reaches the log.</summary>
public class V2pnLimitsTests
{
    [Test]
    public async Task Url_KeepsTheHost_HidesThePathQueryAndUser()
    {
        await V2pnLimits.Url("https://user:pass@panel.example.com:8443/sub/3f9a1c?token=abc").Should().BeEqualTo("https://panel.example.com:8443/…");
        await V2pnLimits.Url("https://panel.example.com/").Should().BeEqualTo("https://panel.example.com");
        await V2pnLimits.Url("not a url").Should().BeEqualTo("(not a URL)");
    }

    [Test]
    public async Task Body_TellsTheLengthAndHash_NotTheLinks()
    {
        var body = "vless://3f9a1c2e-0000-4000-8000-000000000001@203.0.113.7:443?security=reality#Germany";

        var logged = V2pnLimits.Body(body);

        await logged.Contains("3f9a1c2e", StringComparison.Ordinal).Should().BeFalse();
        await logged.Contains("203.0.113.7", StringComparison.Ordinal).Should().BeFalse();
        await logged.StartsWith($"{body.Length} characters", StringComparison.Ordinal).Should().BeTrue();
        await V2pnLimits.Body(null).Should().BeEqualTo("empty");
    }

    [Test]
    public async Task ReadString_StopsPastTheLimit()
    {
        using var small = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"));
        using var large = new StreamContent(new MemoryStream(new byte[2048]));

        await (await V2pnLimits.ReadStringAsync(small, 1024, default)).Should().BeEqualTo("ok");
        await (await V2pnLimits.ReadStringAsync(large, 1024, default) == null).Should().BeTrue();
    }

    [Test]
    public async Task ReadString_FollowsTheCharset()
    {
        using var content = new ByteArrayContent(Encoding.GetEncoding("utf-16").GetBytes("Привет"));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "utf-16" };

        await (await V2pnLimits.ReadStringAsync(content, 1024, default)).Should().BeEqualTo("Привет");
    }

    [Test]
    public async Task Yaml_ABillionLaughs_IsStopped()
    {
        var yaml = new StringBuilder("a: &a [\"lol\",\"lol\",\"lol\",\"lol\",\"lol\",\"lol\",\"lol\",\"lol\",\"lol\"]\n");
        for (var i = 0; i < 9; i++)
        {
            var prev = (char)('a' + i);
            var next = (char)('b' + i);
            yaml.Append($"{next}: &{next} [*{prev},*{prev},*{prev},*{prev},*{prev},*{prev},*{prev},*{prev},*{prev}]\n");
        }

        var node = YamlUtils.FromYaml(yaml.ToString());
        var written = YamlUtils.ToYaml(node);

        // Written out whole it would be 9^10 strings; the writer stops long before and the save gives up.
        await written.Length.Should().BeLessThanOrEqualTo(V2pnYamlLimits.MaxChars);
    }

    [Test]
    public async Task Yaml_AnOrdinaryConfig_StillRoundTrips()
    {
        var node = YamlUtils.FromYaml("mixed-port: 7890\nproxies:\n  - name: a\n    type: ss\n");

        await YamlUtils.ToYaml(node).Contains("mixed-port: 7890", StringComparison.Ordinal).Should().BeTrue();
    }
}
