using System.Diagnostics;
using System.Reflection;
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

    private static async Task<int> Main(string[] args)
    {
        var line = V2pnControlCli.Parse(args, out var error);
        if (error != null)
        {
            Console.Error.WriteLine(error);
            return V2pnControlProtocol.ExitError;
        }
        if (line.Command == V2pnControlCli.Help)
        {
            Console.Out.WriteLine(V2pnControlCli.Usage);
            return V2pnControlProtocol.ExitOk;
        }
        if (line.Command == V2pnControlCli.VersionCommand)
        {
            Console.Out.WriteLine($"v2pnctl {Version}");
            return V2pnControlProtocol.ExitOk;
        }

        var folder = V2pnControlAddress.ResolveFolder(line.Instance ?? Environment.GetEnvironmentVariable("V2PN_INSTANCE"), AppContext.BaseDirectory);
        var link = new V2pnControlLink(V2pnControlEndpoint.For(folder), () => StartV2pn(folder)) { Folder = folder };
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        if (line.Command == V2pnControlCli.Mcp)
        {
            // Standard output carries the protocol alone; anything else goes to standard error.
            var mcp = new V2pnControlMcp((command, arguments) => link.CallAsync(command, arguments, V2pnControlProtocol.SourceMcp), Version);
            using var input = new StreamReader(Console.OpenStandardInput(), utf8);
            await using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { NewLine = "\n" };
            await mcp.RunAsync(input, output);
            return V2pnControlProtocol.ExitOk;
        }

        var answer = await link.CallAsync(line.Command, line.Args, V2pnControlProtocol.SourceCli);
        // A pipe gets UTF-8 (a server's name may be in Cyrillic); a console keeps its own code page.
        var stdout = Console.IsOutputRedirected ? new StreamWriter(Console.OpenStandardOutput(), utf8) : Console.Out;
        try
        {
            if (line.Json)
            {
                await stdout.WriteLineAsync(answer.ToJson().ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            else if (answer.Ok)
            {
                var text = V2pnControlCli.Format(line.Command, answer, DateTime.UtcNow);
                if (text.Length > 0)
                {
                    await stdout.WriteLineAsync(text);
                }
            }
            else
            {
                await Console.Error.WriteLineAsync(V2pnControlCli.Format(line.Command, answer, DateTime.UtcNow));
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
            Console.Error.WriteLine($"Could not start V2pn: {ex.Message}");
            return false;
        }
    }
}
