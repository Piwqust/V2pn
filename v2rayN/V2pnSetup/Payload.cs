using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace V2pnSetup
{
    /// <summary>
    /// What tools/release.ps1 appends to this program: the Inno Setup engine, then a few "key=value" lines about it,
    /// then a trailer. Layout, from the end of the file:
    ///   [this program][engine][header][engine length: 8 bytes][header length: 8 bytes]["V2PNSTP1"]
    /// The header holds version=, arch= and sha256= (of the engine). A file without the trailer is the bare
    /// program, as it is copied next to V2pn for removal.
    /// </summary>
    internal sealed class Payload
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("V2PNSTP1");
        private const int TrailerSize = 24;

        public string Path { get; private set; }
        public long EngineOffset { get; private set; }
        public long EngineLength { get; private set; }
        public string Version { get; private set; }
        public string Arch { get; private set; }
        public string Sha256 { get; private set; }

        public static Payload Read(string exe)
        {
            using (var file = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length < TrailerSize)
                {
                    return null;
                }
                var trailer = new byte[TrailerSize];
                file.Seek(-TrailerSize, SeekOrigin.End);
                ReadExactly(file, trailer);
                for (var i = 0; i < Magic.Length; i++)
                {
                    if (trailer[16 + i] != Magic[i])
                    {
                        return null;
                    }
                }
                var engineLength = BitConverter.ToInt64(trailer, 0);
                var headerLength = BitConverter.ToInt64(trailer, 8);
                var headerOffset = file.Length - TrailerSize - headerLength;
                var engineOffset = headerOffset - engineLength;
                if (headerLength <= 0 || headerLength > 64 * 1024 || engineLength <= 0 || engineOffset <= 0)
                {
                    return null;
                }
                var header = new byte[headerLength];
                file.Seek(headerOffset, SeekOrigin.Begin);
                ReadExactly(file, header);
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in Encoding.UTF8.GetString(header).Split('\n'))
                {
                    var at = line.IndexOf('=');
                    if (at > 0)
                    {
                        fields[line.Substring(0, at).Trim()] = line.Substring(at + 1).Trim();
                    }
                }
                fields.TryGetValue("version", out var version);
                fields.TryGetValue("arch", out var arch);
                fields.TryGetValue("sha256", out var sha);
                return new Payload
                {
                    Path = exe,
                    EngineOffset = engineOffset,
                    EngineLength = engineLength,
                    Version = version ?? "0.0.0",
                    Arch = arch ?? "x64",
                    Sha256 = (sha ?? string.Empty).ToLowerInvariant(),
                };
            }
        }

        /// <summary>Writes the engine to <paramref name="target"/>, checking it against the header's SHA-256 on the way.</summary>
        public void ExtractEngine(string target)
        {
            using (var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sha = SHA256.Create())
            {
                file.Seek(EngineOffset, SeekOrigin.Begin);
                var buffer = new byte[1 << 20];
                var left = EngineLength;
                while (left > 0)
                {
                    var n = file.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (n <= 0)
                    {
                        throw new InvalidDataException(Text.Broken);
                    }
                    output.Write(buffer, 0, n);
                    sha.TransformBlock(buffer, 0, n, null, 0);
                    left -= n;
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                var hash = BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
                if (Sha256.Length > 0 && hash != Sha256)
                {
                    throw new InvalidDataException(Text.Broken);
                }
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer)
        {
            var done = 0;
            while (done < buffer.Length)
            {
                var n = stream.Read(buffer, done, buffer.Length - done);
                if (n <= 0)
                {
                    throw new EndOfStreamException();
                }
                done += n;
            }
        }
    }
}
