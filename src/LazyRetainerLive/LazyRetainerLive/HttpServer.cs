using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace LazyRetainerLive;

/// <summary>
/// Loopback-only HTTP surface: GET /retainers -> the latest snapshot as JSON.
///
/// Deliberately a raw TcpListener, NOT System.Net.HttpListener: HttpListener is
/// http.sys-based and the game host (omasky) runs the client under wine, which
/// does not implement http.sys. Plain sockets are what the proven precedent
/// (IINACT on 127.0.0.1:10501) uses on this same host.
///
/// Card hard rules honoured here:
///   - bind 127.0.0.1 ONLY (no auth on the endpoint; it must never leave the host)
///   - never read game memory on this thread — Serve() only touches the last
///     completed snapshot object the framework tick published.
/// </summary>
internal sealed class HttpServer : IDisposable
{
    private const int RequestReadCap = 8 * 1024;   // enough for any sane GET
    private const int ClientTimeoutMs = 3000;

    private readonly RetainerLiveService _service;
    private readonly Func<int> _port;
    private readonly Func<bool> _enabled;
    private TcpListener? _listener;
    private Thread? _thread;
    private bool _running; // all reads/writes go through Volatile.* (CS0420-clean)
    private int _boundPort;
    private long _nextBindAttemptTicks;

    public HttpServer(RetainerLiveService service, Func<int> port, Func<bool> enabled)
    {
        _service = service;
        _port = port;
        _enabled = enabled;
    }

    public string LastError { get; private set; } = "";

    /// <summary>
    /// Framework-tick driver. Starts the listener when enabled, restarts it if
    /// the configured port changed, retries a failed bind every ~10 s, and
    /// stops it when disabled. Cheap no-op in the steady state.
    /// </summary>
    public void EnsureStarted()
    {
        var want = _enabled();
        if (!want)
        {
            if (_running) Stop();
            return;
        }

        var port = _port();
        if (_running && _boundPort == port)
            return;

        if (!Volatile.Read(ref _running) && Environment.TickCount64 < Volatile.Read(ref _nextBindAttemptTicks))
            return; // failed bind recently — wait out the 10 s backoff

        Stop();
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            _listener = listener;
            _boundPort = port;
            _running = true;
            _thread = new Thread(ServeLoop) { IsBackground = true, Name = "LazyRetainerLive.Http" };
            _thread.Start();
            LastError = "";
            Plugin.Log.Information($"LazyRetainerLive: serving GET http://127.0.0.1:{port}/retainers (loopback only)");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Volatile.Write(ref _nextBindAttemptTicks, Environment.TickCount64 + 10_000);
            Plugin.Log.Warning(ex, $"LazyRetainerLive: cannot bind 127.0.0.1:{port} — endpoint down, retrying in 10 s");
        }
    }

    private void ServeLoop()
    {
        var listener = _listener!;
        while (Volatile.Read(ref _running))
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                if (!Volatile.Read(ref _running)) break;
                continue; // transient accept error; keep serving
            }

            try
            {
                HandleClient(client); // inline: single consumer, one tiny request per ~5 s
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "LazyRetainerLive: request handler failed");
            }
            finally
            {
                try { client.Close(); } catch { }
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        client.ReceiveTimeout = ClientTimeoutMs;
        client.SendTimeout = ClientTimeoutMs;
        using var stream = client.GetStream();

        // Read the request head (headers included, bodies never expected).
        var buf = new byte[RequestReadCap];
        var total = 0;
        while (total < buf.Length)
        {
            var n = stream.Read(buf, total, buf.Length - total);
            if (n <= 0) break;
            total += n;
            // End of request head?
            if (total >= 4 &&
                buf[total - 4] == (byte)'\r' && buf[total - 3] == (byte)'\n' &&
                buf[total - 2] == (byte)'\r' && buf[total - 1] == (byte)'\n')
                break;
            var head = Encoding.ASCII.GetString(buf, 0, Math.Min(total, 256));
            if (head.Contains("\n\n")) break; // tolerate bare-LF clients
        }

        var requestLine = Encoding.ASCII.GetString(buf, 0, total).Split('\n')[0].TrimEnd('\r');
        // "GET /retainers HTTP/1.1"
        var parts = requestLine.Split(' ');
        var method = parts.Length > 0 ? parts[0] : "";
        var target = parts.Length > 1 ? parts[1] : "";
        var path = target;
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];

        // This is the SERVE time, and it is what ships as "readAt". It is NOT
        // the snapshot's age: a frozen last-known snapshot still reports a fresh
        // readAt on every request (measured 2026-09-05 - identical chars, readAt
        // advancing with the clock). A consumer that needs to age out stale data
        // needs a snapshot-time field; readAt cannot do that job.
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
            !path.Equals("/retainers", StringComparison.OrdinalIgnoreCase))
        {
            WriteResponse(stream, 404, "{\"error\":\"not found\",\"hint\":\"GET /retainers\"}");
            return;
        }

        var snap = _service.Current;
        if (snap == null)
        {
            // 503 = "this plugin has NEVER built a snapshot this session", NOT
            // "logged out". The service deliberately keeps the last good snapshot
            // published across login screens and zone changes (see the anti-flap
            // comment in RetainerLiveService.Tick), so once any character has
            // logged in, this branch is unreachable until the plugin reloads.
            // In practice that means: reached at the title screen before the
            // first login, or if the retainer table never became readable.
            // The relay treats any non-200 as "fall back to file". Never 200
            // with empty chars — that would read as "zero retainers".
            WriteResponse(stream, 503, Encoding.UTF8.GetString(RetainerWire.WriteUnavailable(now)));
            return;
        }

        WriteResponse(stream, 200, Encoding.UTF8.GetString(RetainerWire.Write(snap, now)));
    }

    private static void WriteResponse(NetworkStream stream, int status, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var statusText = status switch
        {
            200 => "OK",
            404 => "Not Found",
            503 => "Service Unavailable",
            _ => "Error",
        };
        var head =
            $"HTTP/1.1 {status} {statusText}\r\n" +
            "Content-Type: application/json\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n";
        var headBytes = Encoding.ASCII.GetBytes(head);
        stream.Write(headBytes, 0, headBytes.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
        stream.Flush();
    }

    public void Stop()
    {
        Volatile.Write(ref _running, false);
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _thread = null;
    }

    public void Dispose() => Stop();
}
