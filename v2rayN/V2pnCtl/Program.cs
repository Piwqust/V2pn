using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ServiceLib;

namespace V2pnCtl;

/// <summary>
/// v2pnctl: sends one command to a running V2pn and prints the answer, or with "mcp" serves AI assistants until its
/// input ends. Everything it says comes from V2pn: it never reads V2pn's settings or database itself, which only the
/// running V2pn writes.
/// </summary>
internal static class Program
{
    private static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";

    // The words v2pnctl prints. MCP and --json stay English: assistants and scripts read them.
    private static V2pnControlText s_text;

    private static async Task<int> Main(string[] args)
    {
        s_text = SystemLanguage();
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        // A pipe gets UTF-8 (a server's name, and the Russian words, are in Cyrillic); a console keeps its own code page.
        // The error output too, so one pipe does not carry two encodings.
        TextWriter Stdout() => Console.IsOutputRedirected ? new StreamWriter(Console.OpenStandardOutput(), utf8) : Console.Out;
        if (Console.IsErrorRedirected)
        {
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        }

        var line = V2pnControlCli.Parse(args, out var error, s_text);
        if (error != null)
        {
            Console.Error.WriteLine(error);
            return V2pnControlProtocol.ExitError;
        }
        if (line.Command is V2pnControlCli.Help or V2pnControlCli.VersionCommand)
        {
            var help = Stdout();
            await help.WriteLineAsync(line.Command == V2pnControlCli.Help ? V2pnControlCli.Usage(s_text) : $"v2pnctl {Version}");
            await help.FlushAsync();
            return V2pnControlProtocol.ExitOk;
        }
        if (line.Command == V2pnControlCli.Mcp || line.Json)
        {
            s_text = V2pnControlText.English;
        }

        var folder = V2pnControlAddress.ResolveFolder(line.Instance ?? Environment.GetEnvironmentVariable("V2PN_INSTANCE"), AppContext.BaseDirectory);
        var link = new V2pnControlLink(V2pnControlEndpoint.For(folder), () => StartV2pn(folder)) { Folder = folder };

        if (line.Command == V2pnControlCli.Mcp)
        {
            // Standard output carries the protocol alone; anything else goes to standard error.
            var mcp = new V2pnControlMcp((command, arguments) => link.CallAsync(command, arguments, V2pnControlProtocol.SourceMcp), Version);
            using var input = new StreamReader(Console.OpenStandardInput(), utf8);
            await using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { NewLine = "\n" };
            await mcp.RunAsync(input, output);
            return V2pnControlProtocol.ExitOk;
        }

        var answer = await link.CallAsync(line.Command, line.Args, V2pnControlProtocol.SourceCli, s_text);
        var stdout = Stdout();
        try
        {
            if (line.Json)
            {
                await stdout.WriteLineAsync(answer.ToJson().ToJsonString(V2pnControlProtocol.Indented));
            }
            else if (answer.Ok)
            {
                var text = V2pnControlCli.Format(line.Command, answer, DateTime.UtcNow, s_text);
                if (text.Length > 0)
                {
                    await stdout.WriteLineAsync(text);
                }
            }
            else
            {
                await Console.Error.WriteLineAsync(V2pnControlCli.Format(line.Command, answer, DateTime.UtcNow, s_text));
            }
        }
        finally
        {
            await stdout.FlushAsync();
        }
        return V2pnControlProtocol.ExitCode(answer);
    }

    /// <summary>
    /// Starts the copy of V2pn in <paramref name="folder"/> in the tray: no window comes up in front of anyone. On Windows
    /// through the shell, so V2pn does not inherit this program's standard output (an MCP client's pipe).
    /// </summary>
    private static bool StartV2pn(string folder)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var exe = Path.Combine(folder, V2pnControlAddress.WindowsProgram);
                if (!File.Exists(exe))
                {
                    return false;
                }
                using var started = Process.Start(new ProcessStartInfo(exe, "--tray") { UseShellExecute = true, WorkingDirectory = folder });
                return started != null;
            }
            if (OperatingSystem.IsMacOS())
            {
                var bundle = Path.GetFullPath(Path.Combine(folder, "..", ".."));
                if (!bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(bundle))
                {
                    return false;
                }
                var open = new ProcessStartInfo("open")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var arg in new[] { "-g", "-a", bundle, "--args", "--tray" })
                {
                    open.ArgumentList.Add(arg);
                }
                using var opener = Process.Start(open);
                if (opener == null)
                {
                    return false;
                }
                _ = opener.StandardOutput.ReadToEndAsync();
                _ = opener.StandardError.ReadToEndAsync();
                return opener.WaitForExit(10_000) && opener.ExitCode == 0;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(s_text.T($"Could not start V2pn: {ex.Message}", $"Не удалось запустить V2pn: {ex.Message}"));
            return false;
        }
    }

    /// <summary>The language v2pnctl speaks here: V2PN_LANG, then the system's (<see cref="V2pnControlCli.Language"/>).</summary>
    private static V2pnControlText SystemLanguage() => V2pnControlCli.Language(
        Environment.GetEnvironmentVariable(V2pnControlCli.LanguageVariable),
        OperatingSystem.IsWindows(),
        OperatingSystem.IsWindows() ? WindowsUiLanguage() : 0,
        Environment.GetEnvironmentVariable("LC_ALL"),
        Environment.GetEnvironmentVariable("LC_MESSAGES"),
        Environment.GetEnvironmentVariable("LANG"),
        ConsoleShowsCyrillic());

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    private static int WindowsUiLanguage()
    {
        try
        {
            return GetUserDefaultUILanguage();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Whether the console can print Cyrillic. A Windows console keeps its own code page (866 on a Russian system, 437
    /// on an English one, where Russian would come out as question marks); a pipe gets UTF-8, which can.
    /// </summary>
    private static bool ConsoleShowsCyrillic()
    {
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected)
        {
            return true;
        }
        try
        {
            var encoding = Console.OutputEncoding;
            return encoding.GetString(encoding.GetBytes("Ёё")) == "Ёё";
        }
        catch (Exception)
        {
            return true;
        }
    }
}
