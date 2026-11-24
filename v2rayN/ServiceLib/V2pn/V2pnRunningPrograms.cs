using System.Xml.Linq;

namespace ServiceLib;

/// <summary>
/// One running program a person may send directly: the name its process has (what a rule matches), the name to show,
/// where it runs from and whether it has a window.
/// </summary>
public sealed record V2pnRunningProgram(string Process, string Title, string? Path, bool HasWindow);

/// <summary>
/// V2pn: the programs that run now, for "Programs that always go directly" (V2pnPresetRules). System processes and
/// V2pn's own are left out, so the list holds what a person recognizes: programs with a window or their own file.
/// </summary>
public static class V2pnRunningPrograms
{
    /// <summary>The programs running now, programs with a window first. Never throws.</summary>
    public static List<V2pnRunningProgram> Snapshot()
    {
        var found = new List<V2pnRunningProgram>();
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn: running programs", ex);
            return [];
        }
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var path = V2pnImport.ImagePath(process);
                    var window = OperatingSystem.IsWindows() && process.MainWindowHandle != IntPtr.Zero;
                    var name = path != null ? System.IO.Path.GetFileName(path)
                        : OperatingSystem.IsWindows() ? process.ProcessName + ".exe" : process.ProcessName;
                    found.Add(new V2pnRunningProgram(name, name, path, window));
                }
                catch
                {
                    // The process ended or refuses to be described: not one to list.
                }
            }
        }
        var system = OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.Windows) : null;
        return Pick(found, system, AppContext.BaseDirectory, OperatingSystem.IsMacOS()).Select(Titled).ToList();
    }

    /// <summary>
    /// The programs a person would pick from <paramref name="all"/>: not the system's (under
    /// <paramref name="systemDir"/>, or on a Mac anything outside an app bundle in /Applications-like places), not
    /// V2pn's own (under <paramref name="ownDir"/>, its cores included), not a nameless process without a window.
    /// One entry per program; with a window first, then by name.
    /// </summary>
    public static List<V2pnRunningProgram> Pick(IEnumerable<V2pnRunningProgram> all, string? systemDir, string? ownDir, bool mac)
    {
        var picked = new List<V2pnRunningProgram>();
        foreach (var program in all)
        {
            var path = program.Path;
            if (path == null ? !program.HasWindow : Under(path, systemDir) || Under(path, ownDir))
            {
                continue;
            }
            var entry = program;
            if (mac)
            {
                var bundle = path == null ? null : MacBundleOf(path);
                if (bundle == null || IsMacSystemPath(bundle))
                {
                    continue;
                }
                entry = program with { Title = System.IO.Path.GetFileNameWithoutExtension(bundle) };
            }
            if (V2pnPresetRules.NormalizeProgram(entry.Process) is not { } name)
            {
                continue;
            }
            entry = entry with { Process = name };
            var same = picked.FindIndex(p => V2pnPresetRules.ProgramKey(p.Process) == V2pnPresetRules.ProgramKey(name));
            if (same < 0)
            {
                picked.Add(entry);
            }
            else if (entry.HasWindow && !picked[same].HasWindow)
            {
                picked[same] = entry;
            }
        }
        return picked
            .OrderByDescending(p => p.HasWindow)
            .ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool Under(string path, string? dir)
    {
        if (dir.IsNullOrEmpty())
        {
            return false;
        }
        var folder = dir.TrimEnd('/', '\\');
        return path.Length > folder.Length
               && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
               && path[folder.Length] is '/' or '\\';
    }

    // On Windows the file's description ("Telegram Desktop") reads better than "Telegram.exe".
    private static V2pnRunningProgram Titled(V2pnRunningProgram program)
    {
        if (!OperatingSystem.IsWindows() || program.Path == null)
        {
            return program;
        }
        try
        {
            var description = FileVersionInfo.GetVersionInfo(program.Path).FileDescription?.Trim();
            return description.IsNullOrEmpty() ? program : program with { Title = description! };
        }
        catch
        {
            return program;
        }
    }

    /// <summary>
    /// The app bundle a Mac program runs from: "/Applications/Telegram.app" for
    /// "/Applications/Telegram.app/Contents/MacOS/Telegram". Null for a program outside a bundle and for a helper
    /// inside another app's bundle (Chrome's renderers): the person picks the app, not its parts.
    /// </summary>
    public static string? MacBundleOf(string path)
    {
        const string marker = ".app/Contents/MacOS/";
        var at = path.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0 || path.IndexOf(".app/", StringComparison.Ordinal) != at)
        {
            return null;
        }
        return path[..(at + 4)];
    }

    private static bool IsMacSystemPath(string bundle) =>
        bundle.StartsWith("/System/", StringComparison.Ordinal)
        || bundle.StartsWith("/Library/Apple/", StringComparison.Ordinal)
        || bundle.StartsWith("/usr/", StringComparison.Ordinal);

    /// <summary>
    /// The name of a Mac app's program, which is what its process is called: CFBundleExecutable of the bundle's
    /// Info.plist, read as XML, or with plutil when the file is binary; otherwise the only file in Contents/MacOS, or
    /// the bundle's own name. Null when <paramref name="bundle"/> is not an app.
    /// </summary>
    public static string? MacExecutable(string bundle)
    {
        bundle = bundle.TrimEnd('/');
        var contents = System.IO.Path.Combine(bundle, "Contents");
        if (!Directory.Exists(contents))
        {
            return null;
        }
        var plist = System.IO.Path.Combine(contents, "Info.plist");
        var name = ReadPlistString(plist, "CFBundleExecutable") ?? PlutilString(plist, "CFBundleExecutable");
        if (name.IsNullOrEmpty())
        {
            var programs = Directory.Exists(System.IO.Path.Combine(contents, "MacOS"))
                ? Directory.GetFiles(System.IO.Path.Combine(contents, "MacOS"))
                : [];
            name = programs.Length == 1
                ? System.IO.Path.GetFileName(programs[0])
                : System.IO.Path.GetFileNameWithoutExtension(bundle);
        }
        return V2pnPresetRules.NormalizeProgram(name);
    }

    /// <summary>A string value of an XML property list; null for a binary one or a missing key.</summary>
    public static string? ReadPlistString(string file, string key)
    {
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }
            using var stream = File.OpenRead(file);
            var head = new byte[8];
            if (stream.Read(head, 0, head.Length) < 6 || Encoding.ASCII.GetString(head).StartsWith("bplist", StringComparison.Ordinal))
            {
                return null;
            }
            stream.Position = 0;
            // Info.plist files carry a DOCTYPE; it is skipped, never fetched.
            using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Ignore,
                XmlResolver = null,
            });
            var dict = XDocument.Load(reader, LoadOptions.None).Root?.Element("dict");
            var keyElement = dict?.Elements("key").FirstOrDefault(k => k.Value == key);
            return keyElement?.ElementsAfterSelf().FirstOrDefault() is { Name.LocalName: "string" } value ? value.Value.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? PlutilString(string file, string key)
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists(file))
        {
            return null;
        }
        try
        {
            using var process = System.Diagnostics.Process.Start(new ProcessStartInfo("/usr/bin/plutil")
            {
                ArgumentList = { "-extract", key, "raw", "-o", "-", file },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null)
            {
                return null;
            }
            var output = process.StandardOutput.ReadToEnd().Trim();
            return process.WaitForExit(3000) && process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
