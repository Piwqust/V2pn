using System.IO.Compression;

namespace AmazTool;

/// <summary>
/// V2pn: installs an update package so that a failure never leaves half of one version and half of
/// another. The package is unpacked next to the app first and checked; only then are files moved
/// into place, each old file going to a backup folder. If a move fails, or the new version does
/// not stay up, everything moved is put back.
///
/// Two layouts:
///  - a folder (Windows portable): files are replaced one by one; cores in bin/ that already exist
///    are left alone, because the app updates them itself and settings live beside them;
///  - a macOS bundle (V2pn.app): the whole bundle is swapped, so its code signature stays whole.
/// </summary>
internal sealed class Installer
{
    private const string WorkFolder = ".v2pn-update";

    // What gives a copy a partner's tab (ServiceLib's V2pnEdition): only a partner's package has it, never an update.
    private const string PartnerMarker = "v2pn-partner.txt";

    private readonly string _root;
    private readonly bool _bundle;
    private readonly string _stage;
    private readonly string _backup;
    private readonly Action<string> _log;
    private readonly List<(string Relative, bool HadOld)> _journal = [];
    private string? _stagedRoot;
    private bool _bundleSwapped;

    /// <param name="installRoot">The app folder, or the .app bundle on macOS.</param>
    public Installer(string installRoot, Action<string> log)
    {
        _root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _bundle = IsBundle(_root);
        // Same volume as the app, so every move is a rename: instant, and all or nothing per file.
        var work = Path.Combine(_bundle ? Path.GetDirectoryName(_root)! : _root, WorkFolder);
        _stage = Path.Combine(work, "stage");
        _backup = Path.Combine(work, "backup");
        _log = log;
    }

    public static bool IsBundle(string path) => path.EndsWith(".app", StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder an update replaces: the bundle when the app runs from X.app/Contents/MacOS.</summary>
    public static string InstallRoot(string appDir)
    {
        var dir = Path.GetFullPath(appDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var contents = Path.GetDirectoryName(dir);
        var bundle = contents == null ? null : Path.GetDirectoryName(contents);
        return Path.GetFileName(dir) == "MacOS" && Path.GetFileName(contents) == "Contents" && bundle != null && IsBundle(bundle)
            ? bundle
            : dir;
    }

    /// <summary>The program an update must contain and that is started afterwards.</summary>
    public string AppExecutable(string? root = null) => _bundle
        ? Path.Combine(root ?? _root, "Contents", "MacOS", Utils.V2rayN)
        : Path.Combine(root ?? _root, OperatingSystem.IsWindows() ? Utils.V2rayN + ".exe" : Utils.V2rayN);

    /// <summary>
    /// Unpacks the package beside the app and checks it. Nothing of the installed app is touched;
    /// on any problem this throws and the app is as it was.
    /// </summary>
    public void Stage(string zip)
    {
        DeleteWork();
        Directory.CreateDirectory(_stage);
        var stageFull = _stage + Path.DirectorySeparatorChar;

        using (var archive = ZipFile.OpenRead(zip))
        {
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                var target = Path.GetFullPath(Path.Combine(_stage, name));
                if (!target.StartsWith(stageFull, Utils.GetPathComparison()))
                {
                    throw new InvalidDataException($"The package has an entry outside its folder: {entry.FullName}");
                }
                if (name.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                // Unix mode from the zip (written by ditto/zip on macOS): type in the top bits, then rwx.
                var mode = (entry.ExternalAttributes >> 16) & 0xFFFF;
                if (!OperatingSystem.IsWindows() && (mode & 0xF000) == 0xA000)
                {
                    using var reader = new StreamReader(entry.Open());
                    File.CreateSymbolicLink(target, reader.ReadToEnd());
                    continue;
                }
                entry.ExtractToFile(target, true);
                if (new FileInfo(target).Length != entry.Length)
                {
                    throw new IOException($"{entry.FullName} was not written completely.");
                }
                if (!OperatingSystem.IsWindows() && (mode & 0x1FF) != 0)
                {
                    File.SetUnixFileMode(target, (UnixFileMode)(mode & 0x1FF));
                }
            }
        }

        // One top folder holds the app ("V2pn/" or "V2pn.app/"); anything else is not a V2pn package.
        var top = Directory.GetDirectories(_stage);
        if (top.Length != 1 || Directory.GetFiles(_stage).Length != 0)
        {
            throw new InvalidDataException("The package must hold a single top folder.");
        }
        if (_bundle != IsBundle(top[0]))
        {
            throw new InvalidDataException("The package is for another system.");
        }
        if (!File.Exists(AppExecutable(top[0])))
        {
            throw new InvalidDataException($"The package has no {Utils.V2rayN} program.");
        }
        _stagedRoot = top[0];
    }

    /// <summary>Moves the staged version into place. On failure everything is put back and this throws.</summary>
    public void Swap()
    {
        if (_stagedRoot == null)
        {
            throw new InvalidOperationException("Nothing is staged.");
        }
        try
        {
            if (_bundle)
            {
                KeepPartnerMarker();
                Directory.CreateDirectory(_backup);
                Directory.Move(_root, Path.Combine(_backup, Path.GetFileName(_root)));
                _bundleSwapped = true;
                Directory.Move(_stagedRoot, _root);
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_stagedRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(_stagedRoot, file);
                var target = Path.Combine(_root, relative);
                var exists = File.Exists(target);
                if (exists && IsCore(relative))
                {
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (exists)
                {
                    var saved = Path.Combine(_backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    Retry(() => File.Move(target, saved, true));
                }
                _journal.Add((relative, exists));
                Retry(() => File.Move(file, target));
            }
        }
        catch (Exception ex)
        {
            _log($"Replacing files failed: {ex.Message}");
            Rollback();
            throw;
        }
    }

    /// <summary>
    /// A partner build of V2pn 1.2 keeps its tab through the update to the version that moves its marker into the
    /// settings (V2pnEdition): the bundle is swapped whole, so the marker is copied into the staged bundle first. A folder
    /// install needs nothing, as files the package does not carry are left where they are. Nothing writes new markers.
    /// </summary>
    private void KeepPartnerMarker()
    {
        var kept = Path.Combine(_root, "Contents", "MacOS", PartnerMarker);
        var staged = Path.Combine(_stagedRoot!, "Contents", "MacOS", PartnerMarker);
        if (File.Exists(kept) && !File.Exists(staged))
        {
            File.Copy(kept, staged);
            _log($"Kept {PartnerMarker}");
        }
    }

    /// <summary>Puts back everything <see cref="Swap"/> moved.</summary>
    public void Rollback()
    {
        if (_bundle)
        {
            if (!_bundleSwapped)
            {
                return;
            }
            var saved = Path.Combine(_backup, Path.GetFileName(_root));
            if (Directory.Exists(_root))
            {
                Directory.Move(_root, Path.Combine(_stage, "rolled-back"));
            }
            Directory.Move(saved, _root);
            _bundleSwapped = false;
            return;
        }

        for (var i = _journal.Count - 1; i >= 0; i--)
        {
            var (relative, hadOld) = _journal[i];
            var target = Path.Combine(_root, relative);
            try
            {
                var saved = Path.Combine(_backup, relative);
                if (hadOld && File.Exists(saved))
                {
                    Retry(() => File.Move(saved, target, true));
                }
                else if (!hadOld)
                {
                    File.Delete(target);
                }
            }
            catch (Exception ex)
            {
                _log($"Could not restore {relative}: {ex.Message}");
            }
        }
        _journal.Clear();
    }

    /// <summary>The new version is in and runs: drop the backup and the staging folder.</summary>
    public void Commit()
    {
        _journal.Clear();
        _bundleSwapped = false;
        DeleteWork();
    }

    public void DeleteWork()
    {
        var work = Path.GetDirectoryName(_stage)!;
        try
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
        }
        catch (Exception ex)
        {
            // Typically this program's own previous file, still mapped while it runs; the next
            // update clears it.
            _log($"Left {work} behind: {ex.Message}");
        }
    }

    private static bool IsCore(string relative)
    {
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first.Equals("bin", Utils.GetPathComparison());
    }

    /// <summary>A file can stay locked for a moment after its process ends (antivirus, the indexer).</summary>
    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(400 * attempt);
            }
        }
    }
}
