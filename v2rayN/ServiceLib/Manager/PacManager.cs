namespace ServiceLib.Manager;

public class PacManager
{
    private const string Tag = "PacManager";
    private CancellationTokenSource? _cts;
    private int _pacPort;
    private TcpListener? _tcpListener;
    private byte[] _writeContent = [];

    public async Task StartAsync(int httpPort, int pacPort)
    {
        var content = await InitText(httpPort);
        _writeContent = content;

        if (_tcpListener is not null && _pacPort == pacPort)
        {
            return;
        }

        Stop();
        var cts = new CancellationTokenSource();
        // V2pn: the system proxy asks http://127.0.0.1:<port>/pac, and the script names 127.0.0.1 itself, so the
        // server listens on loopback only; TcpListener.Create took every interface and told the network the local port.
        var listener = new TcpListener(IPAddress.Loopback, pacPort);
        listener.Start();

        _cts = cts;
        _pacPort = pacPort;
        _tcpListener = listener;
        _ = ListenLoopAsync(listener, cts.Token);
    }

    private async Task<byte[]> InitText(int httpPort)
    {
        var customSystemProxyPacPath = ConfigTextHandler.Config.SystemProxyItem.CustomSystemProxyPacPath;
        var fileName = customSystemProxyPacPath.IsNotEmpty() && File.Exists(customSystemProxyPacPath)
            ? customSystemProxyPacPath
            : Path.Combine(Utils.GetConfigPath(), "pac.txt");

        // TODO: temporarily notify which script is being used
        NoticeManager.Instance.SendMessage(fileName);

        if (!File.Exists(fileName))
        {
            var pac = EmbedUtils.GetEmbedText(Global.PacFileName);
            await File.AppendAllTextAsync(fileName, pac);
        }

        var pacText = await File.ReadAllTextAsync(fileName);
        pacText = pacText.Replace("__PROXY__", $"PROXY 127.0.0.1:{httpPort};DIRECT;");

        var sb = new StringBuilder();
        sb.AppendLine("HTTP/1.0 200 OK");
        sb.AppendLine("Content-type:application/x-ns-proxy-autoconfig");
        sb.AppendLine("Connection:close");
        sb.AppendLine("Content-Length:" + Encoding.UTF8.GetByteCount(pacText));
        sb.AppendLine();
        sb.Append(pacText);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private async Task ListenLoopAsync(TcpListener listener, CancellationToken token)
    {
        var buffer = new byte[1024];
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    await using var stream = client.GetStream();
                    _ = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                    await stream.WriteAsync(_writeContent, token).ConfigureAwait(false);
                    await stream.FlushAsync(token).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
        }
        finally
        {
            listener.Stop();
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _tcpListener?.Stop();
        _cts?.Dispose();
        _cts = null;
        _pacPort = 0;
        _tcpListener = null;
    }
}
