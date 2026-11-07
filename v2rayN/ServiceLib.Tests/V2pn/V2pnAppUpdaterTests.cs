using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>
/// Runs the updater against a local copy of the releases page: newest tag by redirect, the
/// package and its checksum, as GitHub serves them.
/// </summary>
[NotInParallel]
public class V2pnAppUpdaterTests
{
    private static readonly string PendingDir = Utils.GetTempPath("V2pnUpdate");

    [Before(Test)]
    public void Setup()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        config.CheckUpdateItem = new CheckUpdateItem { UpdateViaProxy = false };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        if (Directory.Exists(PendingDir))
        {
            Directory.Delete(PendingDir, true);
        }
    }

    [After(Test)]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("V2PN_RELEASES_URL", null);
        V2pnAppUpdater.Instance.State = EV2pnUpdateState.Idle;
    }

    [Test]
    public async Task NewerRelease_IsDownloadedVerifiedAndWaitsForInstall()
    {
        var package = "a V2pn package"u8.ToArray();
        await using var server = await FakeReleases.Start("v99.0.0", package, Convert.ToHexStringLower(SHA256.HashData(package)));

        await V2pnAppUpdater.Instance.CheckAsync(download: true);

        await V2pnAppUpdater.Instance.State.Should().BeEqualTo(EV2pnUpdateState.Ready);
        await V2pnAppUpdater.Instance.LatestVersion.Should().BeEqualTo("99.0.0");
        var pending = V2pnAppUpdater.Instance.FindPending();
        await pending.HasValue.Should().BeTrue();
        await pending!.Value.Version.Should().BeEqualTo("99.0.0");
        await (await File.ReadAllBytesAsync(pending.Value.File)).SequenceEqual(package).Should().BeTrue();
    }

    [Test]
    public async Task DamagedPackage_IsThrownAway()
    {
        var package = "a V2pn package"u8.ToArray();
        await using var server = await FakeReleases.Start("v99.0.0", package, new string('0', 64));

        await V2pnAppUpdater.Instance.CheckAsync(download: true);

        await V2pnAppUpdater.Instance.State.Should().BeEqualTo(EV2pnUpdateState.Failed);
        await V2pnAppUpdater.Instance.DownloadFailed.Should().BeTrue();
        await V2pnAppUpdater.Instance.FindPending().HasValue.Should().BeFalse();
        await Directory.GetFiles(PendingDir).Length.Should().BeEqualTo(0);
    }

    [Test]
    public async Task ReleaseWithoutChecksum_IsNotDownloaded()
    {
        await using var server = await FakeReleases.Start("v99.0.0", "x"u8.ToArray(), null);

        await V2pnAppUpdater.Instance.CheckAsync(download: true);

        await V2pnAppUpdater.Instance.State.Should().BeEqualTo(EV2pnUpdateState.Failed);
        await V2pnAppUpdater.Instance.FindPending().HasValue.Should().BeFalse();
    }

    [Test]
    public async Task SameOrOlderRelease_MeansUpToDate()
    {
        await using var server = await FakeReleases.Start("v" + V2pnInfo.Version, "x"u8.ToArray(), null);

        await V2pnAppUpdater.Instance.CheckAsync(download: true);

        await V2pnAppUpdater.Instance.State.Should().BeEqualTo(EV2pnUpdateState.UpToDate);
    }

    [Test]
    public async Task CheckWithoutDownload_OnlyReportsTheVersion()
    {
        var package = "a V2pn package"u8.ToArray();
        await using var server = await FakeReleases.Start("v99.0.0", package, Convert.ToHexStringLower(SHA256.HashData(package)));

        await V2pnAppUpdater.Instance.CheckAsync(download: false);

        await V2pnAppUpdater.Instance.State.Should().BeEqualTo(EV2pnUpdateState.Available);
        await server.Requests.Any(r => r.EndsWith(".zip")).Should().BeFalse();
    }

    [Test]
    public async Task UnreachableReleases_FailWithoutThrowing()
    {
        Environment.SetEnvironmentVariable("V2PN_RELEASES_URL", "http://127.0.0.1:9/releases");

        await V2pnAppUpdater.Instance.CheckAsync(download: true);

        await V2pnAppUpdater.Instance.State.Should().BeEqualTo(EV2pnUpdateState.Failed);
        await V2pnAppUpdater.Instance.DownloadFailed.Should().BeFalse();
    }

    private sealed class FakeReleases : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        public ConcurrentQueue<string> Requests { get; } = new();

        private FakeReleases(HttpListener listener) => _listener = listener;

        public static async Task<FakeReleases> Start(string tag, byte[] package, string? hash)
        {
            var port = FreePort();
            var listener = new HttpListener();
            var prefix = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(prefix);
            listener.Start();
            var server = new FakeReleases(listener);
            Environment.SetEnvironmentVariable("V2PN_RELEASES_URL", prefix + "releases");
            var name = V2pnInfo.PackageName()!;
            _ = Task.Run(async () =>
            {
                while (!server._stop.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = await listener.GetContextAsync();
                    }
                    catch
                    {
                        return;
                    }
                    var path = ctx.Request.Url!.AbsolutePath;
                    server.Requests.Enqueue(path);
                    var response = ctx.Response;
                    if (path == "/releases/latest")
                    {
                        response.StatusCode = 302;
                        response.RedirectLocation = $"{prefix}releases/tag/{tag}";
                    }
                    else if (path == $"/releases/download/{tag}/{name}")
                    {
                        response.ContentLength64 = package.Length;
                        await response.OutputStream.WriteAsync(package);
                    }
                    else if (path == $"/releases/download/{tag}/{name}.sha256" && hash != null)
                    {
                        var body = Encoding.UTF8.GetBytes($"{hash}  {name}\n");
                        await response.OutputStream.WriteAsync(body);
                    }
                    else
                    {
                        response.StatusCode = 404;
                    }
                    response.Close();
                }
            });
            await Task.CompletedTask;
            return server;
        }

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            _listener.Close();
            return ValueTask.CompletedTask;
        }
    }
}
