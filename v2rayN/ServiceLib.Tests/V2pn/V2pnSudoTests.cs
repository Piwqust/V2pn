namespace ServiceLib.Tests.V2pn;

/// <summary>VPN mode on a Mac runs sudo directly; nothing that gets the password or runs as root is written to a file.</summary>
public class V2pnSudoTests
{
    [Test]
    public async Task Run_IsTheCommandTheScriptUsedToHold()
    {
        var args = V2pnSudo.RunArguments("/Users/a/Library/Application Support/V2pn/bin/sing_box/sing-box", "run -c \"config.json\" --disable-color",
            new Dictionary<string, string?> { ["ENABLE_DEPRECATED"] = "true", ["EMPTY"] = "" });

        await args.Should().BeEqualTo("-S -- env ENABLE_DEPRECATED=\"true\" \"/Users/a/Library/Application Support/V2pn/bin/sing_box/sing-box\" run -c \"config.json\" --disable-color");
    }

    [Test]
    public async Task Run_WithoutEnvironment_GoesStraightToTheCore()
    {
        await V2pnSudo.RunArguments("/opt/xray", "run", null).Should().BeEqualTo("-S -- \"/opt/xray\" run");
    }

    [Test]
    public async Task Kill_PassesTheScriptFromMemory_AndThePidAsItsArgument()
    {
        var args = V2pnSudo.KillArguments("#!/bin/bash\r\necho $1\r\n", 4242);

        await string.Join("|", args).Should().BeEqualTo("-S|--|/bin/bash|-c|#!/bin/bash\necho $1\n|kill_as_sudo|4242");
    }
}
