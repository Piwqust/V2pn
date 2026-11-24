using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>The opt-in note about a failed update says only versions, system, step and a short code.</summary>
public class V2pnUpdateReportTests
{
    [Test]
    public async Task Body_HasOnlyWhatTheServerTakes()
    {
        var json = JsonNode.Parse(V2pnUpdateReport.Body("1.4.2", "1.4.3", "stage", "FileNotFoundException")!)!.AsObject();

        await string.Join(",", json.Select(p => p.Key)).Should().BeEqualTo("from,to,os,arch,stage,code");
        await json["code"]!.GetValue<string>().Should().BeEqualTo("filenotfoundexception");
        await json["stage"]!.GetValue<string>().Should().BeEqualTo("stage");
    }

    [Test]
    public async Task Body_RefusesWhatIsNotAVersionOrAStep()
    {
        await (V2pnUpdateReport.Body("1.4.2", "1.4.3", "elsewhere", "x") == null).Should().BeTrue();
        await (V2pnUpdateReport.Body("C:\\Users\\Ivan", "1.4.3", "stage", "x") == null).Should().BeTrue();
    }

    [Test]
    public async Task Code_IsShortAndPlain()
    {
        await V2pnUpdateReport.Code("The SSL connection could not be established, see inner exception.").Should()
            .BeEqualTo("the_ssl_connection_could_not_be_establis");
        await V2pnUpdateReport.Code("   ").Should().BeEqualTo("unknown");
        await V2pnUpdateReport.Code("exit_-1").Should().BeEqualTo("exit_-1");
    }

    [Test]
    public async Task Allowed_OnlyWhenTurnedOn_AndOncePerVersionAndStep()
    {
        var config = CoreConfigTestFactory.CreateConfig();

        await V2pnUpdateReport.Allowed(config, "1.4.3", "stage").Should().BeFalse();
        config.UiItem.V2pnReportUpdateFailures = true;
        await V2pnUpdateReport.Allowed(config, "1.4.3", "stage").Should().BeTrue();
        config.UiItem.V2pnReportedFailures = ["1.4.3|stage"];
        await V2pnUpdateReport.Allowed(config, "1.4.3", "stage").Should().BeFalse();
        await V2pnUpdateReport.Allowed(config, "1.4.3", "start").Should().BeTrue();
    }
}
