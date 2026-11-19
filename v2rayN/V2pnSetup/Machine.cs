using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace V2pnSetup
{
    /// <summary>What the installer needs to know about this computer before it starts the engine.</summary>
    internal static class Machine
    {
        /// <summary>The entry V2pn.iss leaves in "Installed apps" (AppId=V2pn); V2pnUninstallEntry.cs keeps its version current.</summary>
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\V2pn_is1";

        /// <summary>Tests: V2PN_SETUP_DIR makes the window look at that folder only, never at the V2pn in use.</summary>
        private static readonly string TestDir = Environment.GetEnvironmentVariable("V2PN_SETUP_DIR");

        public static string DefaultDir => !string.IsNullOrEmpty(TestDir) ? TestDir :
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "V2pn");

        public sealed class Installed
        {
            public string Dir;
            public string Version;
            /// <summary>An uninstall entry exists, so Inno's uninstaller is there; a copy made by install.ps1 has none.</summary>
            public bool HasUninstaller;
        }

        /// <summary>V2pn installed for this user: by this installer (the uninstall entry) or by install.ps1 (the default folder).</summary>
        public static Installed FindInstalled()
        {
            if (!string.IsNullOrEmpty(TestDir))
            {
                return File.Exists(System.IO.Path.Combine(TestDir, "V2pn.exe"))
                    ? new Installed { Dir = TestDir, Version = ExeVersion(TestDir), HasUninstaller = File.Exists(System.IO.Path.Combine(TestDir, "unins000.exe")) }
                    : null;
            }
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    if (key?.GetValue("InstallLocation") is string location && File.Exists(System.IO.Path.Combine(location, "V2pn.exe")))
                    {
                        var dir = location.TrimEnd('\\');
                        return new Installed
                        {
                            Dir = dir,
                            Version = ExeVersion(dir) ?? key.GetValue("DisplayVersion") as string,
                            HasUninstaller = File.Exists(System.IO.Path.Combine(dir, "unins000.exe")),
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Reading the uninstall entry: " + ex.Message);
            }
            if (File.Exists(System.IO.Path.Combine(DefaultDir, "V2pn.exe")))
            {
                return new Installed { Dir = DefaultDir, Version = ExeVersion(DefaultDir) };
            }
            return null;
        }

        /// <summary>The release number of V2pn.exe in a folder ("1.3.0"), from its file version.</summary>
        public static string ExeVersion(string dir)
        {
            try
            {
                var exe = System.IO.Path.Combine(dir, "V2pn.exe");
                if (!File.Exists(exe))
                {
                    return null;
                }
                var info = FileVersionInfo.GetVersionInfo(exe);
                return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Compares "1.3.0" and "1.3.1-local.5" by their numbers only.</summary>
        public static int CompareVersions(string a, string b)
        {
            Version Parse(string s)
            {
                s = (s ?? "0.0.0").Split('-', '+')[0];
                return System.Version.TryParse(s, out var v) ? v : new Version(0, 0, 0);
            }
            return Parse(a).CompareTo(Parse(b));
        }

        /// <summary>
        /// A process started from the install folder: V2pn, its updater or a core, which hold files the engine
        /// replaces. An elevated V2pn does not show its path to a normal process, so a nameless V2pn.exe counts too.
        /// The uninstallers are left out: they wait in the folder while their copy in %TEMP% works.
        /// </summary>
        public static string RunningFrom(string dir)
        {
            var prefix = System.IO.Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var path = ProcessPath(process.Id);
                    if (path == null)
                    {
                        if (string.Equals(process.ProcessName, "V2pn", StringComparison.OrdinalIgnoreCase))
                        {
                            return "V2pn.exe";
                        }
                        continue;
                    }
                    if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var name = System.IO.Path.GetFileName(path);
                    if (name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) || process.Id == Process.GetCurrentProcess().Id)
                    {
                        continue;
                    }
                    return name;
                }
                catch
                {
                    // The process ended while it was looked at.
                }
                finally
                {
                    process.Dispose();
                }
            }
            return null;
        }

        private static string ProcessPath(int pid)
        {
            var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)pid);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                var buffer = new StringBuilder(1024);
                var size = (uint)buffer.Capacity;
                return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString() : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>Null when the folder can be written to by this user; else what to tell them.</summary>
        public static string CheckFolder(string dir, long needBytes)
        {
            try
            {
                var full = System.IO.Path.GetFullPath(dir);
                var existed = Directory.Exists(full);
                Directory.CreateDirectory(full);
                var probe = System.IO.Path.Combine(full, ".v2pn-setup-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
                if (!existed)
                {
                    Directory.Delete(full);
                }
                var root = System.IO.Path.GetPathRoot(full);
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    if (drive.IsReady && drive.AvailableFreeSpace < needBytes)
                    {
                        return Text.NoSpace(Megabytes(needBytes));
                    }
                }
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return Text.NoAccess;
            }
            catch (IOException ex)
            {
                return ex.Message;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
            {
                return ex.Message;
            }
        }

        public static string Megabytes(long bytes) => $"{Math.Ceiling(bytes / 1048576.0)} MB";

        public static bool IsArm64()
        {
            try
            {
                if (IsWow64Process2(Process.GetCurrentProcess().Handle, out _, out var native))
                {
                    return native == 0xAA64;
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Windows 10 before 1709 has no IsWow64Process2 and no ARM64 to speak of.
            }
            return false;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
    }
}
