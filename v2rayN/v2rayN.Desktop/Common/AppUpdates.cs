namespace v2rayN.Desktop.Common;

/// <summary>
/// When V2pn looks for a new version of itself: shortly after start, then every few hours. With
/// automatic updates on, a found version downloads in the background and installs at the next
/// start, so an open connection is never cut by an update the person did not ask for.
/// </summary>
internal static class AppUpdates
{
    // Long enough for the window, the core and the connection to settle first.
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    private static DispatcherTimer? _timer;

    public static bool Auto(Config config) => config.UiItem.V2pnAutoUpdate ?? true;

    /// <summary>
    /// Installs a package that finished downloading in an earlier session. Returns true when V2pn
    /// is closing for it.
    /// </summary>
    public static async Task<bool> InstallPendingAtStartAsync(Config config, Action<string> announce)
    {
        var updater = V2pnAppUpdater.Instance;
        if (!Auto(config) || !updater.Supported || updater.FindPending() is not { } pending)
        {
            return false;
        }
        announce(string.Format(L.UpdInstalling, pending.Version));
        // Lets the message paint before AmazTool closes the window.
        await Task.Delay(1200);
        return await updater.InstallAsync();
    }

    public static void Start(Config config)
    {
        if (_timer != null || !V2pnAppUpdater.Instance.Supported)
        {
            return;
        }
        _timer = new DispatcherTimer(FirstCheck, DispatcherPriority.Background, async (_, _) =>
        {
            _timer!.Interval = Every;
            await CheckAsync(config);
        });
        _timer.Start();
    }

    public static Task CheckAsync(Config config) => V2pnAppUpdater.Instance.CheckAsync(download: Auto(config));
}
