using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace V2pnSetup
{
    /// <summary>
    /// What the program was started for:
    ///   (nothing)            the install window
    ///   /S, /SILENT, /VERYSILENT and Inno's other switches: a silent install, passed to the engine as it is
    ///   --uninstall [--dir]  the remove window (the program copied next to V2pn as uninstall.exe always is this)
    ///   --uninstall /S       a silent removal; /REMOVEDATA=1 removes servers and settings too
    ///   --shot &lt;folder&gt;     pictures of every screen, for review, with the window off screen
    /// </summary>
    public partial class App : Application
    {
        private static Mutex _single;

        static App()
        {
            // WPF finds its XAML by the assembly's name, V2pnSetup, and loads it from a file of that name.
            // This program runs as V2pn-windows-x64-setup.exe or uninstall.exe, so it answers for itself.
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
                new System.Reflection.AssemblyName(e.Name).Name == "V2pnSetup" ? typeof(App).Assembly : null;
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var args = e.Args;
            var self = Process.GetCurrentProcess().MainModule.FileName;
            Log.Write($"Started: {self} {string.Join(" ", args)}");
            try
            {
                var code = await RunAsync(self, args);
                Shutdown(code);
            }
            catch (Exception ex)
            {
                Log.Write("Fatal: " + ex);
                MessageBox.Show(ex.Message, "V2pn", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private async Task<int> RunAsync(string self, string[] args)
        {
            var lang = Value(args, "--lang");
            if (lang != null)
            {
                Text.Ru = lang == "ru";
            }
            var shot = Value(args, "--shot");
            if (shot != null)
            {
                return await ShotsAsync(self, shot);
            }

            var silent = args.Any(a => IsSwitch(a, "/S") || IsSwitch(a, "/SILENT") || IsSwitch(a, "/VERYSILENT"));
            var uninstall = args.Contains("--uninstall") || string.Equals(Path.GetFileName(self), "uninstall.exe", StringComparison.OrdinalIgnoreCase);

            if (uninstall)
            {
                var dir = Value(args, "--dir") ?? Path.GetDirectoryName(self);
                if (silent)
                {
                    var removeData = args.Any(a => a.Equals("/REMOVEDATA=1", StringComparison.OrdinalIgnoreCase));
                    return (await Engine.UninstallAsync(dir, removeData, IntPtr.Zero)).ExitCode;
                }
                // Run from %TEMP%: a program cannot remove the folder it was started from.
                if (!args.Contains("--from-temp"))
                {
                    var copy = Path.Combine(Path.GetTempPath(), "V2pn-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                    File.Copy(self, copy, true);
                    var langArg = lang != null ? $" --lang {lang}" : string.Empty;
                    Process.Start(new ProcessStartInfo(copy, $"--uninstall --from-temp --dir \"{dir.TrimEnd('\\')}\" --parent {Process.GetCurrentProcess().Id}{langArg}") { UseShellExecute = false });
                    return 0;
                }
                WaitForParent(args);
                var window = V2pnSetup.MainWindow.ForUninstall(dir);
                var result = await ShowAsync(window);
                DeleteLater(self);
                return result;
            }

            Payload payload = null;
            string error = null;
            try
            {
                payload = Payload.Read(self);
            }
            catch (Exception ex)
            {
                Log.Write("Reading the payload: " + ex.Message);
            }
            if (payload == null)
            {
                error = Text.Broken;
            }
            else if (payload.Arch == "arm64" && !Machine.IsArm64())
            {
                error = Text.WrongArch;
            }

            if (silent)
            {
                if (payload == null || error != null)
                {
                    Log.Write(error);
                    return 1;
                }
                var passed = args.Select(a => IsSwitch(a, "/S") ? "/VERYSILENT" : a);
                return await Engine.SilentInstallAsync(payload, passed.Select(Quote));
            }

            // One install window at a time; a second start brings nothing up beside it.
            _single = new Mutex(true, "V2pnSetupWindow", out var first);
            if (!first)
            {
                return 0;
            }
            var dirArg = args.FirstOrDefault(a => a.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase))?.Substring(5).Trim('"');
            return await ShowAsync(V2pnSetup.MainWindow.ForInstall(payload, dirArg, error));
        }

        private static Task<int> ShowAsync(V2pnSetup.MainWindow window)
        {
            var done = new TaskCompletionSource<int>();
            window.Closed += (_, __) => done.TrySetResult(window.Finished ? 0 : 2);
            window.Show();
            return done.Task;
        }

        private static async Task<int> ShotsAsync(string self, string folder)
        {
            Environment.SetEnvironmentVariable("V2PN_BACKGROUND", "1");
            Directory.CreateDirectory(folder);
            var payload = new Payload();
            typeof(Payload).GetProperty(nameof(Payload.Version)).SetValue(payload, "1.3.1");
            typeof(Payload).GetProperty(nameof(Payload.EngineLength)).SetValue(payload, 1L);
            foreach (var name in new[] { "welcome", "welcome-error", "wait", "progress", "done", "error", "newer" })
            {
                var window = V2pnSetup.MainWindow.ForInstall(payload, Machine.DefaultDir, null, ignoreInstalled: true);
                window.Show();
                await window.ShotAsync(name);
                window.SaveShot(Path.Combine(folder, name + ".png"));
                window.Close();
            }
            var remove = V2pnSetup.MainWindow.ForInstall(payload, Machine.DefaultDir, null, ignoreInstalled: true);
            remove.Show();
            remove.ShotUninstall(Machine.DefaultDir);
            await Task.Delay(900);
            remove.SaveShot(Path.Combine(folder, "uninstall.png"));
            remove.Close();
            return 0;
        }

        private static void WaitForParent(string[] args)
        {
            if (int.TryParse(Value(args, "--parent"), out var pid))
            {
                try
                {
                    using (var parent = Process.GetProcessById(pid))
                    {
                        parent.WaitForExit(5000);
                    }
                }
                catch (ArgumentException)
                {
                    // Already gone.
                }
            }
        }

        /// <summary>The copy in %TEMP% removes itself once it has exited.</summary>
        private static void DeleteLater(string self)
        {
            if (!self.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c ping -n 3 127.0.0.1 >nul & del /f /q \"{self}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            catch (Exception ex)
            {
                Log.Write("Delete later: " + ex.Message);
            }
        }

        private static string Value(string[] args, string name)
        {
            var at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        private static bool IsSwitch(string arg, string name) => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);

        // Arguments come back unquoted; one with a space ("/DIR=D:\My Apps") needs its quotes again.
        private static string Quote(string arg)
        {
            if (arg.IndexOf(' ') < 0 || arg.StartsWith("\""))
            {
                return arg;
            }
            var eq = arg.IndexOf('=');
            return eq > 0 ? arg.Substring(0, eq + 1) + "\"" + arg.Substring(eq + 1) + "\"" : "\"" + arg + "\"";
        }
    }
}
