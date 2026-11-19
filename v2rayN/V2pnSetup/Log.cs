using System;
using System.IO;

namespace V2pnSetup
{
    /// <summary>%TEMP%\V2pn-setup.log: what the window did, next to the engine's own log (V2pn-setup-engine.log).</summary>
    internal static class Log
    {
        public static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "V2pn-setup.log");
        public static readonly string EnginePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "V2pn-setup-engine.log");
        private static readonly object Gate = new object();

        public static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}{Environment.NewLine}");
                }
            }
            catch
            {
                // A log that cannot be written must not stop the install.
            }
        }
    }
}
