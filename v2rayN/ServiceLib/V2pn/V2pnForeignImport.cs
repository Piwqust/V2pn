namespace ServiceLib;

/// <summary>A subscription found in another client: its name there and its link.</summary>
public sealed record ForeignSub(string Name, string Url);

/// <summary>Another client found on this computer, with what V2pn can take from it.</summary>
public sealed class ForeignApp
{
    public required string Name { get; init; }
    public required string Folder { get; init; }
    public List<ForeignSub> Subscriptions { get; } = [];

    /// <summary>Servers added by hand in that client, as share links.</summary>
    public List<string> Links { get; } = [];

    /// <summary>Servers of kinds V2pn cannot hold (h2, shadow-tls, plain HTTP proxies…).</summary>
    public int Skipped { get; set; }

    /// <summary>An app whose data cannot be read (Happ encrypts it): offered only as advice to move by hand.</summary>
    public bool Locked { get; init; }

    public bool HasAny => Subscriptions.Count > 0 || Links.Count > 0;
}

/// <summary>What a move brought: new subscriptions (their ids, to download them) and servers added.</summary>
public sealed record ForeignImportResult(List<string> NewSubIds, int ExistingSubs, int ServersAdded, int Skipped);

/// <summary>
/// V2pn: moving to V2pn from clients other than v2rayN. Their data has nothing in common with
/// v2rayN's database, so instead of the whole-folder copy (<see cref="V2pnImport"/>) V2pn reads what
/// matters from their files: subscription links, which then download in V2pn as usual, and servers
/// added by hand, turned into share links (<see cref="V2pnForeignLinks"/>). Their files are only read;
/// the other client keeps working as it was. Routing rules of other clients do not come over: their
/// formats differ, and V2pn's own presets cover what people use them for.
/// </summary>
public static partial class V2pnForeignImport
{
    private const string Tag = "V2pn foreign import";

    /// <summary>Every client with something to move, in the order of <see cref="Readers"/>.</summary>
    public static List<ForeignApp> Find()
    {
        var found = new List<ForeignApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, folders, read) in Readers())
        {
            foreach (var folder in folders().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(folder) || !seen.Add($"{name}|{Normalize(folder)}"))
                    {
                        continue;
                    }
                    if (read(folder) is { } app && (app.HasAny || app.Locked)
                        && !found.Any(f => f.Name == app.Name && (f.Locked || Normalize(f.Folder).Equals(Normalize(app.Folder), StringComparison.OrdinalIgnoreCase))))
                    {
                        found.Add(app);
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog($"{Tag}: {name} at {folder}: {ex.Message}");
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Whatever any reader finds in a folder the person pointed at (a portable NekoRay, Throne or
    /// Clash Verge, say), also one or two levels down; a v2rayN folder is left to <see cref="V2pnImport"/>.
    /// </summary>
    public static List<ForeignApp> FindIn(string folder)
    {
        var found = new List<ForeignApp>();
        var candidates = new List<string> { folder };
        try
        {
            candidates.AddRange(Directory.EnumerateDirectories(folder));
            candidates.Add(Path.Combine(folder, ".config"));
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{Tag}: {folder}: {ex.Message}");
        }
        foreach (var (name, _, read) in Readers())
        {
            foreach (var candidate in candidates.Where(Directory.Exists))
            {
                try
                {
                    if (read(candidate) is { HasAny: true } app)
                    {
                        found.Add(app);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog($"{Tag}: {name} at {candidate}: {ex.Message}");
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Adds the app's subscriptions (kept when V2pn already has the link) and its servers that V2pn
    /// does not have yet. The new subscriptions are only saved; the caller downloads them.
    /// </summary>
    public static async Task<ForeignImportResult> ImportAsync(Config config, ForeignApp app)
    {
        var newSubs = new List<string>();
        var existing = 0;
        foreach (var sub in app.Subscriptions.DistinctBy(s => s.Url.Trim()))
        {
            if (await V2pnSubs.AddSubscriptionAsync(config, sub.Url) is not { } result)
            {
                continue;
            }
            if (result.Existed)
            {
                existing++;
                continue;
            }
            // The person's own name for it in the other app, unless that app made one up.
            if (IsOwnName(sub.Name, sub.Url))
            {
                result.Item.Remarks = sub.Name.Trim();
                await ConfigHandler.AddSubItem(config, result.Item);
            }
            newSubs.Add(result.Item.Id);
        }

        var added = 0;
        var links = await NewLinksAsync(app.Links);
        if (links.Count > 0)
        {
            added = Math.Max(0, await ConfigHandler.AddBatchServers(config, string.Join(Environment.NewLine, links), string.Empty, false));
        }
        Logging.SaveLog($"V2pn: moved from {app.Name} at {app.Folder}: {newSubs.Count} new subscriptions, {existing} already there, {added} servers, {app.Skipped} skipped");
        return new ForeignImportResult(newSubs, existing, added, app.Skipped);
    }

    /// <summary>
    /// A v2rayN read like any other client, for a move from several apps at once: the whole-folder copy
    /// (<see cref="V2pnImport"/>) would replace what the others bring. Its subscriptions, and the servers
    /// that are not from one of them, as links.
    /// </summary>
    public static ForeignApp ReadV2rayN(string dataDir)
    {
        var app = new ForeignApp { Name = "v2rayN", Folder = dataDir };
        var copy = CopyDatabase(Path.Combine(dataDir, V2pnImport.ConfigFolder, "guiNDB.db"));
        if (copy == null)
        {
            return app;
        }
        try
        {
            using var db = new SQLiteConnection(copy, false);
            var subs = db.Table<SubItem>().ToList().Where(s => V2pnSubs.IsHttpLink(s.Url)).ToList();
            app.Subscriptions.AddRange(subs.Select(s => new ForeignSub(s.Remarks ?? string.Empty, s.Url.Trim())));
            var subIds = subs.Select(s => s.Id).ToHashSet();
            foreach (var item in db.Table<ProfileItem>().ToList().Where(p => p.Subid.IsNullOrEmpty() || !subIds.Contains(p.Subid)))
            {
                var link = FmtHandler.GetShareUri(item);
                if (link.IsNullOrEmpty())
                {
                    app.Skipped++;
                }
                else
                {
                    app.Links.Add(link);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{Tag}: v2rayN at {dataDir}: {ex.Message}");
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(copy)!, true);
            }
            catch
            {
                // A leftover in guiTemps is harmless.
            }
        }
        return app;
    }

    /// <summary>The links whose server V2pn does not hold yet (same kind, address, port and secret).</summary>
    private static async Task<List<string>> NewLinksAsync(IEnumerable<string> links)
    {
        var have = (await AppManager.Instance.ProfileItems(string.Empty) ?? [])
            .Select(Identity)
            .ToHashSet(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var link in links.Distinct(StringComparer.Ordinal))
        {
            var item = FmtHandler.ResolveConfig(link, out _);
            if (item != null && have.Add(Identity(item)))
            {
                result.Add(link);
            }
        }
        return result;
    }

    private static string Identity(ProfileItem item) =>
        $"{item.ConfigType}|{item.Address?.ToLowerInvariant()}|{item.Port}|{item.Password}|{item.Username}";

    /// <summary>Names clients give on their own: "Remote File", "Subscription 1", the link's host…</summary>
    private static bool IsOwnName(string? name, string url)
    {
        var trimmed = name?.Trim();
        if (trimmed.IsNullOrEmpty() || trimmed == SubInfoManager.FallbackName(url) || trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return !GeneratedName().IsMatch(trimmed);
    }

    [GeneratedRegex(@"^(remote( file)?|local( file)?|new profile|profile|subscription|subscribe|config|default|clash|sub)\s*\d*$", RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedName();

    #region Reading files

    /// <summary>A file's text even while its app has it open; null when it is missing or unreadable.</summary>
    internal static string? ReadText(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{Tag}: {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// A copy of an SQLite database (with its WAL) in V2pn's temp folder: the running app may hold the
    /// file, and reading a copy never touches theirs. The caller deletes the folder.
    /// </summary>
    internal static string? CopyDatabase(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        var folder = Utils.GetTempPath($"V2pnForeign-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            if (File.Exists(path + suffix))
            {
                using var from = new FileStream(path + suffix, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var to = File.Create(Path.Combine(folder, Path.GetFileName(path) + suffix));
                from.CopyTo(to);
            }
        }
        return Path.Combine(folder, Path.GetFileName(path));
    }

    /// <summary>Links of every kind a file of servers can hold: Clash YAML, sing-box JSON, or links one per line.</summary>
    internal static List<string> LinksFromContent(string? content, out int skipped)
    {
        skipped = 0;
        if (content.IsNullOrEmpty())
        {
            return [];
        }
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return V2pnForeignLinks.FromSingboxJson(content, out skipped);
        }
        var clash = V2pnForeignLinks.FromClashYaml(content, out skipped);
        if (clash.Count > 0 || skipped > 0)
        {
            return clash;
        }
        var text = Utils.IsBase64String(trimmed.Trim()) ? Utils.Base64Decode(trimmed.Trim()) : content;
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => Global.ProtocolShares.Values.Any(p => line.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string MacSupport => Path.Combine(Home, "Library", "Application Support");

    /// <summary>Per-user app data folders named <paramref name="names"/>: Roaming and Local on Windows, Application Support on a Mac, ~/.config and ~/.local/share on Linux.</summary>
    private static IEnumerable<string> AppData(params string[] names)
    {
        foreach (var name in names)
        {
            if (OperatingSystem.IsWindows())
            {
                yield return Path.Combine(Roaming, name);
                yield return Path.Combine(Local, name);
            }
            else if (OperatingSystem.IsMacOS())
            {
                yield return Path.Combine(MacSupport, name);
            }
            else
            {
                yield return Path.Combine(Home, ".config", name);
                yield return Path.Combine(Home, ".local", "share", name);
            }
        }
    }

    private static IEnumerable<string> SearchRoots() =>
    [
        Path.Combine(Home, "Downloads"),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Path.Combine(Local, "Programs"),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Path.Combine(Home, "scoop", "apps"),
        Home,
        Path.GetPathRoot(Home) ?? "C:\\",
    ];

    /// <summary>Each of <paramref name="exeNames"/> within <paramref name="depth"/> levels of <paramref name="root"/>, with its folder.</summary>
    private static List<(string Exe, string Folder)> SearchExe(string root, string[] exeNames, int depth)
    {
        var result = new List<(string, string)>();
        void Walk(string dir, int level)
        {
            try
            {
                foreach (var exe in exeNames)
                {
                    if (File.Exists(Path.Combine(dir, exe)))
                    {
                        result.Add((exe, dir));
                    }
                }
                if (level >= depth)
                {
                    return;
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    // Large trees that never hold a portable app.
                    if (name.StartsWith('.') || name.StartsWith('$') || name is "node_modules" or "AppData" or "Windows" or "ProgramData" or "Program Files" or "Program Files (x86)" or "Users")
                    {
                        continue;
                    }
                    Walk(sub, level + 1);
                }
            }
            catch
            {
                // Folders without access are skipped.
            }
        }
        if (Directory.Exists(root))
        {
            Walk(root, 0);
        }
        return result;
    }

    #endregion Reading files
}
