using ServiceLib.Handler;

namespace ServiceLib.Tests.V2pn;

public class V2pnSubResultTests
{
    [Test]
    public async Task ImportedIsOk()
    {
        var (result, detail) = SubscriptionHandler.Classify(true, "vless://…", true, 200, null);
        await result.Should().BeEqualTo(EV2pnSubUpdateResult.Ok);
        await detail.Should().BeNull();
    }

    [Test]
    public async Task AnAnswerThatIsNotServersIsInvalid()
    {
        var (result, _) = SubscriptionHandler.Classify(false, "<html>Sign in</html>", true, 200, null);
        await result.Should().BeEqualTo(EV2pnSubUpdateResult.Invalid);
    }

    [Test]
    public async Task AnEmptyAnswerIsEmpty()
    {
        var (result, _) = SubscriptionHandler.Classify(false, string.Empty, true, 200, null);
        await result.Should().BeEqualTo(EV2pnSubUpdateResult.Empty);
    }

    [Test]
    public async Task AnHttpErrorSaysItsCode()
    {
        var (result, detail) = SubscriptionHandler.Classify(false, string.Empty, false, 500, "Response status code does not indicate success: 500");
        await result.Should().BeEqualTo(EV2pnSubUpdateResult.Unreachable);
        await detail.Should().BeEqualTo("HTTP 500");
    }

    [Test]
    public async Task NoAnswerKeepsTheNetworkError()
    {
        var (result, detail) = SubscriptionHandler.Classify(false, null, false, null, "No such host is known.");
        await result.Should().BeEqualTo(EV2pnSubUpdateResult.Unreachable);
        await detail.Should().BeEqualTo("No such host is known.");
    }
}
