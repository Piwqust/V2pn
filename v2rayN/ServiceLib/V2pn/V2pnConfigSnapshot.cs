namespace ServiceLib;

/// <summary>
/// V2pn: the settings are saved from timers and background tasks (every 20 minutes from the thread pool) while the window
/// changes them, for instance the list of recent servers. Serializing a list that changes under it fails ("collection was
/// modified"), and the save was then skipped with a line in the log. A second try a moment later almost always finds the
/// list still.
/// </summary>
public static class V2pnConfigSnapshot
{
    private const int Tries = 4;
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(40);

    /// <summary>The text <paramref name="serialize"/> gives, tried again when it gives none; empty after the last try.</summary>
    public static async Task<string> SerializeAsync(Func<string> serialize)
    {
        for (var i = 1; ; i++)
        {
            var text = serialize();
            if (text.IsNotEmpty() || i >= Tries)
            {
                return text;
            }
            await Task.Delay(Pause);
        }
    }
}
