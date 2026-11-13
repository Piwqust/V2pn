using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>
/// Runs the updater against local copies of a releases page: the signed manifest of the newest
/// release and its packages, laid out as GitHub (and a mirror) serve them.
/// </summary>
[NotInParallel]
public class V2pnAppUpdaterTests
{
    private static readonly string PendingDir = Utils.GetTempPath("V2pnUpdate");
    private static readonly byte[] Package = "a V2pn package"u8.ToArray();
    private static readonly string Name = V2pnInfo.PackageName()!;

    private readonly TestRelease _release = new();
    private V2pnAppUpdater Updater => V2pnAppUpdater.Instance;

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
        Updater.Settings = V2pnUpdateSettings.FromBuild();
        Updater.State = EV2pnUpdateState.Idle;
        if (Directory.Exists(PendingDir))
        {
            Directory.Delete(PendingDir, true);
        }
    }

    private void Trust(params string[] sources) => Updater.Settings = new V2pnUpdateSettings(sources, [_release.PublicKey], true, "1.0.0");

    private FakeReleases Serve(string version, byte[]? served = null, TestRelease? signedBy = null)
    {
        var manifest = TestRelease.Manifest(version, (Name, Package));
        return FakeReleases.Start(version, manifest, (signedBy ?? _release).Sign(manifest), served ?? Package);
    }

    [Test]
    public async Task NewerRelease_IsDownloadedVerifiedAndWaitsForInstall()
    {
        await using var server = Serve("99.0.0");
        Trust(server.Source);

        await Updater.CheckAsync(download: true);

        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.Ready);
        await Updater.LatestVersion.Should().BeEqualTo("99.0.0");
        var pending = Updater.FindPending();
        await pending.HasValue.Should().BeTrue();
        await pending!.Value.Version.Should().BeEqualTo("99.0.0");
        await (await File.ReadAllBytesAsync(pending.Value.File)).SequenceEqual(Package).Should().BeTrue();
    }

    [Test]
    public async Task PackageThatDoesNotMatchTheManifest_IsThrownAway()
    {
        await using var server = Serve("99.0.0", served: "something else"u8.ToArray());
        Trust(server.Source);

        await Updater.CheckAsync(download: true);

        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.Failed);
        await Updater.DownloadFailed.Should().BeTrue();
        await Updater.FindPending().HasValue.Should().BeFalse();
        await Directory.GetFiles(PendingDir).Length.Should().BeEqualTo(0);
    }

    [Test]
    public async Task ReleaseSignedWithAnotherKey_IsNotEvenDownloaded()
    {
        await using var server = Serve("99.0.0", signedBy: new TestRelease());
        Trust(server.Source);

        await Updater.CheckAsync(download: true);

        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.Failed);
        await Updater.DownloadFailed.Should().BeFalse();
        await server.Requests.Any(r => r.EndsWith(".zip")).Should().BeFalse();
        await Updater.FindPending().HasValue.Should().BeFalse();
    }

    [Test]
    public async Task SameOrOlderRelease_MeansUpToDate()
    {
        await using var server = Serve("1.0.0");
        Trust(server.Source);

        await Updater.CheckAsync(download: true);

        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.UpToDate);
        await server.Requests.Any(r => r.EndsWith(".zip")).Should().BeFalse();
    }

    [Test]
    public async Task NothingPublishedYet_IsNotAnError()
    {
        await using var server = FakeReleases.StartEmpty();
        Trust(server.Source);

        await Updater.CheckAsync(download: true);

        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.UpToDate);
        await (Updater.LatestVersion == null).Should().BeTrue();
        await (Updater.Error == null).Should().BeTrue();
    }

    [Test]
    public async Task CheckWithoutDownload_OnlyReportsTheVersion()
    {
        await using var server = Serve("99.0.0");
        Trust(server.Source);

        await Updater.CheckAsync(download: false);

        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.Available);
        await server.Requests.Any(r => r.EndsWith(".zip")).Should().BeFalse();
    }

    [Test]
    public async Task UnreachableReleases_FailWithoutThrowing()
    {
        Trust("http://127.0.0.1:9/releases");

        await Updater.CheckAsync(download: true);

        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.Failed);
        await Updater.DownloadFailed.Should().BeFalse();
    }

    [Test]
    public async Task WhenTheFirstSourceIsDown_TheMirrorServesTheUpdate()
    {
        await using var mirror = Serve("99.0.0");
        Trust("http://127.0.0.1:9/releases", mirror.Source);

        await Updater.CheckAsync(download: true);

        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.Ready);
        await Updater.FindPending()!.Value.Version.Should().BeEqualTo("99.0.0");
    }

    [Test]
    public async Task MirrorWithATamperedPackage_IsSkippedForTheNextSource()
    {
        await using var bad = Serve("99.0.0", served: "something else"u8.ToArray());
        await using var good = Serve("99.0.0");
        Trust(bad.Source, good.Source);

        await Updater.CheckAsync(download: true);

        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.Ready);
        await (await File.ReadAllBytesAsync(Updater.FindPending()!.Value.File)).SequenceEqual(Package).Should().BeTrue();
    }

    [Test]
    public async Task PackageDroppedIntoTheFolderWithoutASignedManifest_IsNotAnUpdate()
    {
        Trust("http://127.0.0.1:9/releases");
        Directory.CreateDirectory(PendingDir);
        var zip = Path.Combine(PendingDir, "V2pn-99.0.0.zip");
        await File.WriteAllBytesAsync(zip, Package);
        await Updater.FindPending().HasValue.Should().BeFalse();

        // A manifest that names the package but is signed by someone else changes nothing.
        var manifest = TestRelease.Manifest("99.0.0", (Name, Package));
        await File.WriteAllBytesAsync(Path.ChangeExtension(zip, ".json"), manifest);
        await File.WriteAllTextAsync(Path.ChangeExtension(zip, ".json") + ".sig", new TestRelease().Sign(manifest));
        await Updater.FindPending().HasValue.Should().BeFalse();
        await (await Updater.InstallAsync()).Should().BeFalse();

        // The release key's signature does.
        await File.WriteAllTextAsync(Path.ChangeExtension(zip, ".json") + ".sig", _release.Sign(manifest));
        await Updater.FindPending().HasValue.Should().BeTrue();
    }

    [Test]
    public async Task VersionWhoseInstallWasRolledBack_IsNotTriedAgainUntilAsked()
    {
        await using var server = Serve("99.0.0");
        Trust(server.Source);
        await Updater.CheckAsync(download: true);
        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.Ready);

        // AmazTool leaves this after putting the previous version back.
        await File.WriteAllTextAsync(Path.Combine(PendingDir, V2pnAppUpdater.FailedMarker), "99.0.0\n");

        await Updater.FindPending().HasValue.Should().BeFalse();
        await Updater.CheckAsync(download: true);
        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.Failed);
        await Updater.InstallFailed.Should().BeTrue();

        await Updater.CheckAsync(download: true, retryFailed: true);
        await Updater.State.Should().BeEqualTo(EV2pnUpdateState.Ready);
        await Updater.InstallFailed.Should().BeFalse();
    }

    private sealed class FakeReleases : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        public ConcurrentQueue<string> Requests { get; } = new();
        public string Source { get; }

        private FakeReleases(HttpListener listener, string source)
        {
            _listener = listener;
            Source = source;
        }

        public static FakeReleases StartEmpty() => Start(null, [], string.Empty, []);

        public static FakeReleases Start(string? version, byte[] manifest, string signature, byte[] package)
        {
            var port = FreePort();
            var listener = new HttpListener();
            var prefix = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(prefix);
            listener.Start();
            var server = new FakeReleases(listener, prefix + "releases");
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
                    byte[]? body = null;
                    if (version != null)
                    {
                        body = path switch
                        {
                            "/releases/latest/download/release.json" => manifest,
                            "/releases/latest/download/release.json.sig" => Encoding.ASCII.GetBytes(signature),
                            _ when path == $"/releases/download/v{version}/{Name}" => package,
                            _ => null,
                        };
                    }
                    try
                    {
                        if (body == null)
                        {
                            response.StatusCode = 404;
                        }
                        else
                        {
                            response.ContentLength64 = body.Length;
                            await response.OutputStream.WriteAsync(body);
                        }
                        response.Close();
                    }
                    catch
                    {
                        // The client gave up on this answer.
                    }
                }
            });
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
