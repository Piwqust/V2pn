using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;

namespace ServiceLib;

// V2pn: compiled into ServiceLib and into v2pnctl, like V2pnControlProtocol.cs.

/// <summary>Where one copy of V2pn listens: a named pipe on Windows, a Unix socket elsewhere.</summary>
public sealed record V2pnControlEndpoint(string? PipeName, string? SocketPath)
{
    public static V2pnControlEndpoint For(string programFolder) => OperatingSystem.IsWindows()
        ? new V2pnControlEndpoint(V2pnControlAddress.PipeName(programFolder), null)
        : new V2pnControlEndpoint(null, V2pnControlAddress.SocketPath(programFolder));

    public override string ToString() => PipeName != null ? $@"\\.\pipe\{PipeName}" : SocketPath ?? "(nowhere)";
}

/// <summary>
/// V2pn: the listening end of the control channel. No TCP port: a port on the loopback is open to every program and,
/// through a browser, to every web page (DNS rebinding), while a pipe or a socket is a file the system guards. The
/// pipe's ACL lets in only the user V2pn runs as; the socket sits in that user's own temporary folder and is made
/// readable by them alone. Each line read is handed to <c>handle</c>, whose answer goes back as one line.
/// </summary>
public sealed class V2pnControlServer : IDisposable
{
    // A few at once is plenty: v2pnctl opens one per command and closes it.
    private const int MaxPipes = 8;

    // A client that connects and says nothing is dropped after this.
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(2);

    private readonly V2pnControlEndpoint _endpoint;
    private readonly Func<string, Task<string>> _handle;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _stop = new();
    private Socket? _socket;

    private V2pnControlServer(V2pnControlEndpoint endpoint, Func<string, Task<string>> handle, Action<string> log)
    {
        _endpoint = endpoint;
        _handle = handle;
        _log = log;
    }

    public V2pnControlEndpoint Endpoint => _endpoint;

    /// <summary>Starts listening. Null when another program already holds the name, or the system refused it.</summary>
    public static V2pnControlServer? Start(V2pnControlEndpoint endpoint, Func<string, Task<string>> handle, Action<string> log)
    {
        var server = new V2pnControlServer(endpoint, handle, log);
        try
        {
            if (endpoint.PipeName != null && OperatingSystem.IsWindows())
            {
                // The first instance is made here, so a name someone else holds fails now, not in the loop.
                var first = server.CreatePipe(first: true);
                _ = Task.Run(() => OperatingSystem.IsWindows() ? server.AcceptPipesAsync(first) : Task.CompletedTask);
            }
            else if (endpoint.SocketPath != null)
            {
                server._socket = server.Bind(endpoint.SocketPath);
                _ = Task.Run(server.AcceptSocketsAsync);
            }
            else
            {
                return null;
            }
            return server;
        }
        catch (Exception ex)
        {
            log($"V2pn control: cannot listen on {endpoint}: {ex.Message}");
            server.Dispose();
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    internal static PipeSecurity Security()
    {
        using var me = WindowsIdentity.GetCurrent();
        var user = me.User!;
        var security = new PipeSecurity();
        // The owner is the user, not the Administrators group an elevated V2pn would otherwise give the pipe: v2pnctl
        // checks that the pipe's owner is its own user (PipeOptions.CurrentUserOnly), so no other user's program can
        // pretend to be V2pn.
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(user,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return security;
    }

    [SupportedOSPlatform("windows")]
    private NamedPipeServerStream CreatePipe(bool first) =>
        NamedPipeServerStreamAcl.Create(_endpoint.PipeName!, PipeDirection.InOut, MaxPipes, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None), 0, 0, Security());

    [SupportedOSPlatform("windows")]
    private async Task AcceptPipesAsync(NamedPipeServerStream first)
    {
        var next = first;
        while (!_stop.IsCancellationRequested)
        {
            var pipe = next;
            next = null;
            try
            {
                pipe ??= CreatePipe(first: false);
                await pipe.WaitForConnectionAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                _log($"V2pn control: the pipe failed: {ex.Message}");
                // All instances busy, or a passing fault: try again shortly rather than spin.
                await Delay(TimeSpan.FromSeconds(1));
                continue;
            }
            _ = Task.Run(() => ServeAsync(pipe));
        }
    }

    private Socket Bind(string path)
    {
        if (File.Exists(path))
        {
            // A socket file left by a V2pn that did not close: nobody answers on it. One that answers belongs to a
            // running copy, which the lock on the copy's folder would not allow.
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                probe.Connect(new UnixDomainSocketEndPoint(path));
                throw new IOException("another program answers on it");
            }
            catch (SocketException)
            {
                File.Delete(path);
            }
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(path));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            socket.Listen(MaxPipes);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task AcceptSocketsAsync()
    {
        while (!_stop.IsCancellationRequested && _socket is { } listener)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log($"V2pn control: the socket failed: {ex.Message}");
                await Delay(TimeSpan.FromSeconds(1));
                continue;
            }
            _ = Task.Run(() => ServeAsync(new NetworkStream(client, ownsSocket: true)));
        }
    }

    private async Task ServeAsync(Stream stream)
    {
        try
        {
            await using (stream)
            {
                var reader = new V2pnControlLineReader(stream, V2pnControlProtocol.MaxLineBytes);
                while (!_stop.IsCancellationRequested)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    idle.CancelAfter(Idle);
                    var line = await reader.ReadLineAsync(idle.Token);
                    if (line == null)
                    {
                        return;
                    }
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    var answer = await _handle(line);
                    await V2pnControlLineReader.WriteLineAsync(stream, answer, _stop.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped, or the client said nothing for too long.
        }
        catch (IOException)
        {
            // The client went away.
        }
        catch (Exception ex)
        {
            _log($"V2pn control: a connection failed: {ex.Message}");
        }
    }

    private async Task Delay(TimeSpan time)
    {
        try
        {
            await Task.Delay(time, _stop.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }
        _stop.Cancel();
        _socket?.Dispose();
        if (_endpoint.SocketPath is { } path && _socket != null)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // Removed at the next start instead.
            }
        }
    }
}

/// <summary>The asking end: one request, one answer.</summary>
public static class V2pnControlClient
{
    /// <summary>
    /// Sends <paramref name="line"/> and returns the answer line; null when nobody listens there (V2pn is not running,
    /// or is older than the channel). Throws when V2pn was reached but the exchange failed.
    /// </summary>
    public static async Task<string?> ExchangeAsync(V2pnControlEndpoint endpoint, string line, TimeSpan connectTimeout, TimeSpan answerTimeout)
    {
        var stream = await ConnectAsync(endpoint, connectTimeout);
        if (stream == null)
        {
            return null;
        }
        await using (stream)
        {
            using var timeout = new CancellationTokenSource(answerTimeout);
            await V2pnControlLineReader.WriteLineAsync(stream, line, timeout.Token);
            var reader = new V2pnControlLineReader(stream, V2pnControlProtocol.MaxLineBytes);
            return await reader.ReadLineAsync(timeout.Token)
                   ?? throw new IOException("V2pn closed the channel without an answer.");
        }
    }

    private static async Task<Stream?> ConnectAsync(V2pnControlEndpoint endpoint, TimeSpan timeout)
    {
        if (endpoint.PipeName != null && OperatingSystem.IsWindows())
        {
            // CurrentUserOnly: the pipe must be owned by this user, so another user's program cannot pose as V2pn.
            var pipe = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync((int)Math.Max(1, timeout.TotalMilliseconds));
                return pipe;
            }
            catch (TimeoutException)
            {
                await pipe.DisposeAsync();
                return null;
            }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }
        }
        if (endpoint.SocketPath is not { } path || !File.Exists(path))
        {
            return null;
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            using var cancel = new CancellationTokenSource(timeout);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancel.Token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            socket.Dispose();
            return null;
        }
    }
}

/// <summary>Lines of UTF-8 text over a stream, each at most so long.</summary>
public sealed class V2pnControlLineReader(Stream stream, int maxBytes)
{
    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    /// <summary>The next line without its end; null at the end of the stream.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        using var line = new MemoryStream();
        while (true)
        {
            var newline = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
            if (newline >= 0)
            {
                line.Write(_buffer, _start, newline - _start);
                _start = newline + 1;
                return Decode(line);
            }
            line.Write(_buffer, _start, _end - _start);
            _start = _end = 0;
            if (line.Length > maxBytes)
            {
                throw new InvalidDataException("The line is too long.");
            }
            var read = await stream.ReadAsync(_buffer, cancellationToken);
            if (read == 0)
            {
                return line.Length > 0 ? Decode(line) : null;
            }
            _end = read;
        }
    }

    private static string Decode(MemoryStream line) => Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');

    public static async Task WriteLineAsync(Stream stream, string line, CancellationToken cancellationToken)
    {
        // One line: a newline inside would be read as two requests.
        var bytes = Encoding.UTF8.GetBytes(line.Replace("\r", string.Empty).Replace("\n", " ") + "\n");
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}

/// <summary>
/// v2pnctl's way to V2pn: sends a command, and when no V2pn listens, starts the copy in the tray and waits for it.
/// </summary>
public sealed class V2pnControlLink(V2pnControlEndpoint endpoint, Func<bool> startV2pn)
{
    private long _nextId;

    /// <summary>How long a started V2pn has to open its channel.</summary>
    public TimeSpan StartWait { get; init; } = TimeSpan.FromSeconds(15);

    public string Folder { get; init; } = string.Empty;

    /// <summary>
    /// Sends one command. <paramref name="t"/> is the language of every word in the answer: V2pn's (the request asks
    /// for it) and this end's own. MCP leaves it English.
    /// </summary>
    public async Task<V2pnControlAnswer> CallAsync(string command, JsonObject args, string source, V2pnControlText t = default)
    {
        var request = new V2pnControlRequest(Interlocked.Increment(ref _nextId), command, args, source, t.Code);
        // A connect waits for the check, and the fastest server is found by testing them all.
        var answerTimeout = command == V2pnControlProtocol.Use ? TimeSpan.FromMinutes(6) : TimeSpan.FromMinutes(2);
        try
        {
            var line = await V2pnControlClient.ExchangeAsync(endpoint, request.ToLine(), TimeSpan.FromSeconds(1.5), answerTimeout);
            if (line == null)
            {
                if (!startV2pn())
                {
                    return V2pnControlAnswer.Failure(request.Id, V2pnControlProtocol.ErrNotRunning, t.T(
                        $"V2pn is not running, and there is no V2pn to start in {Folder}.",
                        $"V2pn не запущен, и в папке {Folder} нет V2pn, чтобы его запустить."));
                }
                var waited = System.Diagnostics.Stopwatch.StartNew();
                while (line == null && waited.Elapsed < StartWait)
                {
                    await Task.Delay(400);
                    // Nothing was sent while nobody listened, so sending now runs the command once.
                    line = await V2pnControlClient.ExchangeAsync(endpoint, request.ToLine(), TimeSpan.FromSeconds(1), answerTimeout);
                }
                if (line == null)
                {
                    var seconds = (int)StartWait.TotalSeconds;
                    return V2pnControlAnswer.Failure(request.Id, V2pnControlProtocol.ErrNotRunning, t.T(
                        $"V2pn was started but did not answer within {seconds} seconds. If it is running, it may be a version without the command line.",
                        $"V2pn запущен, но не ответил за {seconds} {V2pnControlText.Plural(seconds, "секунду", "секунды", "секунд")}. Если он работает, возможно, это версия без управления из командной строки."));
                }
            }
            return V2pnControlAnswer.Parse(line, request.Id, t);
        }
        catch (UnauthorizedAccessException)
        {
            return V2pnControlAnswer.Failure(request.Id, V2pnControlProtocol.ErrFailed, t.T(
                "The channel is not owned by this user, so v2pnctl did not use it.",
                "Канал принадлежит другому пользователю, поэтому v2pnctl не стал через него работать."));
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidDataException)
        {
            return V2pnControlAnswer.Failure(request.Id, V2pnControlProtocol.ErrFailed,
                t.T($"The exchange with V2pn failed: {ex.Message}", $"Связь с V2pn прервалась: {ex.Message}"));
        }
    }
}
