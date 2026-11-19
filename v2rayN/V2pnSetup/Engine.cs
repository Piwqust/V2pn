using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace V2pnSetup
{
    /// <summary>
    /// Runs Inno Setup (tools/installer/V2pn.iss) with no window of its own. It posts its progress to the installer's
    /// window: <see cref="ProgressMessage"/> with per mille done in wParam, <see cref="StepMessage"/> with a step.
    /// </summary>
    internal static class Engine
    {
        public const int ProgressMessage = 0x8001; // WM_APP + 1
        public const int StepMessage = 0x8002;     // WM_APP + 2

        // The steps V2pn.iss posts.
        public const int StepFiles = 1;
        public const int StepFinish = 2;
        public const int StepUninstall = 11;
        public const int StepUninstallFinish = 12;

        public sealed class Result
        {
            public int ExitCode;
            public string Error;
            public bool Ok => ExitCode == 0;
        }

        public static async Task<Result> InstallAsync(Payload payload, string dir, bool desktop, bool import, IntPtr window, IEnumerable<string> extra = null)
        {
            var work = Path.Combine(Path.GetTempPath(), "v2pn-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var engine = Path.Combine(work, "V2pn-engine.exe");
            try
            {
                await Task.Run(() => payload.ExtractEngine(engine));
                var tasks = new List<string>();
                if (desktop)
                {
                    tasks.Add("desktopicon");
                }
                if (import)
                {
                    tasks.Add("importv2rayn");
                }
                var arguments = new List<string>
                {
                    "/SP-", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCANCEL",
                    $"/DIR={Quote(dir)}",
                    $"/TASKS={Quote(string.Join(",", tasks))}",
                    $"/LOG={Quote(Log.EnginePath)}",
                    $"/LANG={(Text.Ru ? "ru" : "en")}",
                };
                if (window != IntPtr.Zero)
                {
                    arguments.Add($"/V2PNHWND={window.ToInt64()}");
                }
                if (extra != null)
                {
                    arguments.AddRange(extra);
                }
                return await RunAsync(engine, arguments, Log.EnginePath);
            }
            catch (InvalidDataException ex)
            {
                return new Result { ExitCode = -1, Error = ex.Message };
            }
            finally
            {
                Cleanup(work);
            }
        }

        public static Task<Result> UninstallAsync(string dir, bool removeData, IntPtr window, bool silentRun = false)
        {
            var arguments = new List<string>
            {
                "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
                $"/LOG={Quote(Log.EnginePath)}",
                $"/REMOVEDATA={(removeData ? 1 : 0)}",
            };
            if (window != IntPtr.Zero)
            {
                arguments.Add($"/V2PNHWND={window.ToInt64()}");
            }
            return RunAsync(Path.Combine(dir, "unins000.exe"), arguments, Log.EnginePath);
        }

        /// <summary>A silent run passed straight to the engine (winget, scripts): the window's own command line goes with it.</summary>
        public static async Task<int> SilentInstallAsync(Payload payload, IEnumerable<string> arguments)
        {
            var work = Path.Combine(Path.GetTempPath(), "v2pn-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var engine = Path.Combine(work, "V2pn-engine.exe");
            try
            {
                payload.ExtractEngine(engine);
                var all = new List<string> { "/SP-", "/SUPPRESSMSGBOXES", "/NORESTART" };
                all.AddRange(arguments);
                return (await RunAsync(engine, all, null)).ExitCode;
            }
            finally
            {
                Cleanup(work);
            }
        }

        private static async Task<Result> RunAsync(string exe, List<string> arguments, string logPath)
        {
            Log.Write($"Running {exe} {string.Join(" ", arguments)}");
            if (logPath != null)
            {
                TryDelete(logPath);
            }
            var start = new ProcessStartInfo(exe, string.Join(" ", arguments))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath(),
            };
            int code;
            using (var process = Process.Start(start))
            {
                await Task.Run(() => process.WaitForExit());
                code = process.ExitCode;
            }
            Log.Write($"Exit code {code}");
            return new Result { ExitCode = code, Error = code == 0 ? null : Explain(code, logPath) };
        }

        /// <summary>The engine's own words for what stopped it, from the end of its log.</summary>
        private static string Explain(int code, string logPath)
        {
            try
            {
                if (logPath != null && File.Exists(logPath))
                {
                    var lines = File.ReadAllLines(logPath);
                    foreach (var marker in new[] { "PrepareToInstall failed:", "Fatal exception", "Exception message:", "Error" })
                    {
                        var index = Array.FindLastIndex(lines, l => l.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (index < 0)
                        {
                            continue;
                        }
                        // Log lines start with a time stamp; a message may go on over the next, indented lines.
                        var text = Strip(lines[index]);
                        var at = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                        text = text.Substring(at + marker.Length).Trim(' ', ':');
                        for (var i = index + 1; i < lines.Length && lines[i].StartsWith(" "); i++)
                        {
                            text += " " + lines[i].Trim();
                        }
                        if (text.Length > 0)
                        {
                            return text;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Reading the engine log: " + ex.Message);
            }
            return Text.EngineFailed(code);
        }

        private static string Strip(string line) => line.Length > 26 && char.IsDigit(line[0]) ? line.Substring(26) : line;

        private static string Quote(string value) => "\"" + value + "\"";

        private static void Cleanup(string work)
        {
            // The engine may hold its file for a moment after it exits.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (Directory.Exists(work))
                    {
                        Directory.Delete(work, true);
                    }
                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(200);
                }
                catch (UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(200);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }

        public static IEnumerable<string> Except(this IEnumerable<string> args, params string[] prefixes) =>
            args.Where(a => !prefixes.Any(p => a.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
    }
}
