namespace ServiceLib;

/// <summary>
/// V2pn: how VPN mode on a Mac starts and stops the core with the administrator password. v2rayN wrote run_as_sudo.sh
/// and kill_as_sudo.sh into V2pn's own folder and ran them: the first got the password on its input, the second ran as
/// root. Any program of the same user could rewrite them between the write and the run and get the password or root.
/// Now nothing is written: sudo is started directly, and the stop script goes to bash as an argument from memory.
/// </summary>
public static class V2pnSudo
{
    public const string Sudo = "/usr/bin/sudo";
    public const string Bash = "/bin/bash";

    /// <summary>The arguments for <see cref="Sudo"/> that run <paramref name="program"/> with <paramref name="arguments"/> as root.</summary>
    public static string RunArguments(string program, string arguments, IEnumerable<KeyValuePair<string, string?>>? environment)
    {
        var command = $"{program.AppendQuotes()} {arguments}".TrimEnd();
        var env = (environment ?? [])
            .Where(kv => kv.Value.IsNotEmpty())
            .Select(kv => $"{kv.Key}={kv.Value.AppendQuotes()}")
            .ToList();
        // -S: the password comes on the input; --: nothing after it is read as sudo's own option.
        return env.Count > 0 ? $"-S -- env {string.Join(" ", env)} {command}" : $"-S -- {command}";
    }

    /// <summary>The arguments for <see cref="Sudo"/> that run <paramref name="script"/> as root on process <paramref name="pid"/>.</summary>
    public static List<string> KillArguments(string script, int pid) =>
        ["-S", "--", Bash, "-c", script.Replace("\r\n", "\n"), "kill_as_sudo", pid.ToString(System.Globalization.CultureInfo.InvariantCulture)];
}
