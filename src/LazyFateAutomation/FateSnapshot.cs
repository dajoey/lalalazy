using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace LazyFateAutomation;

/// <summary>
/// Read-only loopback snapshot for the home dashboard (Helm t-joey-1788795729247):
/// GET http://127.0.0.1:10505/fates -> latest FATE/hunt snapshot as JSON.
///
/// Same pattern as LazyRetainerLive's HttpServer (proven wine-safe on omasky):
/// raw TcpListener, NOT HttpListener (http.sys does not exist under wine);
/// bind 127.0.0.1 ONLY (no auth; must never leave the host); the serve thread
/// never touches game memory - it only serializes the immutable snapshot the
/// framework tick published. The status relay on this host polls the endpoint
/// every 5 s; the plugin stays passive.
/// </summary>
internal sealed class FateSnapshotServer : IDisposable
{
    private const int Port = 10505;
    private const int RequestReadCap = 8 * 1024;
    private const int ClientTimeoutMs = 3000;
    private const int BindRetryMs = 10_000;

    private readonly Func<FateSnapshot?> _snapshot;
    private TcpListener? _listener;
    private Thread? _thread;
    private volatile bool _running;
    private long _nextBindAttempt;

    public string LastError { get; private set; } = "";
    public bool Running => _running;

    public FateSnapshotServer(Func<FateSnapshot?> snapshot) => _snapshot = snapshot;

    /// <summary>Framework-tick driver: start the listener, retry a failed bind every 10 s.</summary>
    public void EnsureStarted()
    {
        if (_running || Environment.TickCount64 < Volatile.Read(ref _nextBindAttempt))
            return;
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            _listener = listener;
            _running = true;
            _thread = new Thread(ServeLoop) { IsBackground = true, Name = "LazyFateAutomation.Http" };
            _thread.Start();
            LastError = "";
            Svc.Log.Information($"LazyFateAutomation: serving GET http://127.0.0.1:{Port}/fates (loopback only)");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Volatile.Write(ref _nextBindAttempt, Environment.TickCount64 + BindRetryMs);
            Svc.Log.Warning(ex, $"LazyFateAutomation: cannot bind 127.0.0.1:{Port} - endpoint down, retrying in 10 s");
        }
    }

    private void ServeLoop()
    {
        var listener = _listener!;
        while (_running)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                if (!_running) break;
                continue; // transient accept error; keep serving
            }
            try
            {
                HandleClient(client); // inline: single consumer, one tiny request per ~5 s
            }
            catch (Exception ex)
            {
                Svc.Log.Warning(ex, "LazyFateAutomation: snapshot request handler failed");
            }
            finally
            {
                try { client.Close(); } catch { /* ignore */ }
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        client.ReceiveTimeout = ClientTimeoutMs;
        client.SendTimeout = ClientTimeoutMs;
        using var stream = client.GetStream();

        var buf = new byte[RequestReadCap];
        var total = 0;
        while (total < buf.Length)
        {
            var n = stream.Read(buf, total, buf.Length - total);
            if (n <= 0) break;
            total += n;
            if (total >= 4 &&
                buf[total - 4] == (byte)'\r' && buf[total - 3] == (byte)'\n' &&
                buf[total - 2] == (byte)'\r' && buf[total - 1] == (byte)'\n')
                break;
        }
        var requestLine = Encoding.ASCII.GetString(buf, 0, total).Split('\n')[0].TrimEnd('\r');
        var parts = requestLine.Split(' ');
        var method = parts.Length > 0 ? parts[0] : "";
        var target = parts.Length > 1 ? parts[1] : "";
        var q = target.IndexOf('?');
        var path = q >= 0 ? target[..q] : target;

        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
            !path.Equals("/fates", StringComparison.OrdinalIgnoreCase))
        {
            WriteResponse(stream, 404, "{\"error\":\"not found\",\"hint\":\"GET /fates\"}");
            return;
        }

        var snap = _snapshot();
        if (snap == null)
        {
            // 503 = no snapshot yet this session (title screen / character not loaded).
            // The relay treats any non-200 as "no data" and keeps its last good copy.
            WriteResponse(stream, 503, "{\"error\":\"no fate snapshot\",\"hint\":\"GET /fates\"}");
            return;
        }
        WriteResponse(stream, 200, snap.ToJson());
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
        stream.Write(Encoding.ASCII.GetBytes(head), 0, head.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
        stream.Flush();
    }

    public void Dispose()
    {
        _running = false;
        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener = null;
        _thread = null;
    }
}

/// <summary>One active public event as JSON-ready data (immutable once published).</summary>
internal sealed class FateEntry
{
    public uint Id;
    public string Name = "";
    public int Progress;          // 0..100
    public int TimeRemaining;     // seconds, clamped >= 0; -1 = unknown
    public bool HasBonus;
    public bool IsCurrent;        // the FATE the player is currently in
}

/// <summary>Immutable FATE/hunt snapshot; published by the tick, read by the serve thread.</summary>
internal sealed class FateSnapshot
{
    public long Ts;               // unix seconds of the tick that built it
    public string Char = "";
    public string World = "";
    public uint ZoneId;
    public string ZoneName = "";
    public List<FateEntry> Fates = new();
    public int Cleared;           // session counter: FATEs seen reaching 100% then ending
    public string? HuntTarget;    // name of a live hunt target in-zone, or null
    public int BillKills;         // sum of current kills across unlocked hunt bills
    public int BillsUnlocked;     // how many hunt bills are unlocked

    public string ToJson()
    {
        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        var sb = new StringBuilder(512);
        sb.Append("{\"type\":\"FateSnapshot\",\"ts\":").Append(Ts);
        sb.Append(",\"char\":\"").Append(Esc(Char)).Append('"');
        sb.Append(",\"world\":\"").Append(Esc(World)).Append('"');
        sb.Append(",\"zone\":").Append(ZoneId);
        sb.Append(",\"zoneName\":\"").Append(Esc(ZoneName)).Append('"');
        sb.Append(",\"cleared\":").Append(Cleared);
        sb.Append(",\"hunt\":{\"target\":").Append(HuntTarget == null ? "null" : "\"" + Esc(HuntTarget) + "\"");
        sb.Append(",\"billKills\":").Append(BillKills);
        sb.Append(",\"bills\":").Append(BillsUnlocked).Append('}');
        sb.Append(",\"fates\":[");
        for (var i = 0; i < Fates.Count; i++)
        {
            var f = Fates[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(f.Id);
            sb.Append(",\"name\":\"").Append(Esc(f.Name)).Append('"');
            sb.Append(",\"progress\":").Append(f.Progress);
            sb.Append(",\"timeRemaining\":").Append(f.TimeRemaining);
            sb.Append(",\"bonus\":").Append(f.HasBonus ? "true" : "false");
            sb.Append(",\"current\":").Append(f.IsCurrent ? "true" : "false").Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }
}

/// <summary>
/// Framework-tick snapshot builder. Reads FATE state (PublicEvent wrapper) and
/// hunt state (ClientStructs MobHunt singleton + object table) on the framework
/// thread ONLY, then publishes an immutable FateSnapshot that the serve thread
/// serializes. Every read is guarded; a failing side never takes the other down.
/// </summary>
internal sealed class FateSnapshotService
{
    private const int MaxFates = 12;   // at-a-glance cap; the panel clips anyway
    private FateSnapshot? _current;
    private uint _prevCurrentFateId;
    private int _prevCurrentFateProgress = -1;
    private int _clearedTotal;         // session accumulator

    public FateSnapshot? Current => Volatile.Read(ref _current);

    public unsafe void Tick()
    {
        try
        {
            var player = Svc.Objects.LocalPlayer;
            if (player == null)
            {
                _prevCurrentFateId = 0;
                _prevCurrentFateProgress = -1;
                Volatile.Write(ref _current, null);   // title screen: 503, LRL semantics
                return;
            }

            var snap = new FateSnapshot
            {
                Ts = DateTimeOffset.Now.ToUnixTimeSeconds(),
                Char = player.Name.TextValue,
                ZoneId = Svc.ClientState.TerritoryType,
            };
            try { snap.World = player.CurrentWorld.Value.Name.ToString(); }
            catch { /* login edge: world name is cosmetic */ }

            // Zone name via the generated sheet (guarded; empty string on failure).
            try
            {
                var terr = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRow(snap.ZoneId);
                var place = terr != null ? terr.Value.PlaceName.Value.Name.ToString() : "";
                if (string.IsNullOrEmpty(place)) place = terr != null ? terr.Value.Name.ToString() : "";
                snap.ZoneName = string.IsNullOrEmpty(place) ? $"Zone {snap.ZoneId}" : place;
            }
            catch
            {
                snap.ZoneName = $"Zone {snap.ZoneId}";
            }

            // ---- FATEs (PublicEvent wraps FateContext / DynamicEvent / WKSMechaEvent) ----
            // Guarded as a SECTION: any transient read failure (zone change,
            // malformed row) keeps the snapshot valid with whatever it has.
            try
            {
            if (Helpers.Utils.PublicEvent.IsFateTerritory)
            {
                var currentFate = Helpers.Utils.PublicEvent.CurrentFate;
                var list = new List<FateEntry>();
                try
                {
                    foreach (var f in Helpers.Utils.PublicEvent.Fates)
                    {
                        var tr = f.TimeRemaining;
                        list.Add(new FateEntry
                        {
                            Id = f.Id,
                            Name = string.IsNullOrEmpty(f.Name) ? $"FATE {f.Id}" : f.Name,
                            Progress = f.Progress,
                            TimeRemaining = tr < 0 ? -1 : (int)Math.Min(tr, int.MaxValue),
                            HasBonus = f.HasBonus,
                            IsCurrent = currentFate != null && currentFate.Id == f.Id,
                        });
                        if (list.Count >= MaxFates) break;
                    }
                }
                catch { /* transient memory read races: keep what we got */ }

                // sort: current first, then soonest-to-expire
                list.Sort((a, b) =>
                {
                    var c = (b.IsCurrent ? 1 : 0) - (a.IsCurrent ? 1 : 0);
                    return c != 0 ? c : a.TimeRemaining.CompareTo(b.TimeRemaining);
                });
                snap.Fates = list;

                // ---- session clear counter: the FATE the player is in reached
                // 100% and then disappeared -> one clear ----
                if (currentFate != null)
                {
                    _prevCurrentFateId = currentFate.Id;
                    _prevCurrentFateProgress = currentFate.Progress;
                }
                else if (_prevCurrentFateId != 0 && _prevCurrentFateProgress >= 100)
                {
                    _clearedTotal++;
                    _prevCurrentFateId = 0;
                    _prevCurrentFateProgress = -1;
                }
                else if (_prevCurrentFateId != 0 && currentFate == null)
                {
                    _prevCurrentFateId = 0;
                    _prevCurrentFateProgress = -1;
                }
            }
            else
            {
                snap.Fates = new List<FateEntry>();
                _prevCurrentFateId = 0;
                _prevCurrentFateProgress = -1;
            }
            }
            catch { /* keep whatever the FATE section produced */ }
            snap.Cleared = _clearedTotal;

            // ---- hunts: unlocked-bill kill totals ----
            try
            {
                var mh = FFXIVClientStructs.FFXIV.Client.Game.UI.MobHunt.Instance();
                if (mh != null)
                {
                    var bills = 0;
                    var kills = 0;
                    var maxMark = FFXIVClientStructs.FFXIV.Client.Game.UI.MobHunt.MaxMarkIndex;
                    for (byte m = 0; m < maxMark; m++)
                    {
                        if (!mh->IsMarkBillUnlocked(m)) continue;
                        bills++;
                        for (byte k = 0; k < 5; k++)
                            kills += mh->GetKillCount(m, k);
                    }
                    snap.BillsUnlocked = bills;
                    snap.BillKills = kills;
                }
            }
            catch { /* hunt reads must never take the FATE side down */ }

            // ---- live hunt target in-zone (first live IsHuntTarget match) ----
            try
            {
                var mhi = FFXIVClientStructs.FFXIV.Client.Game.UI.MobHunt.Instance();
                if (mhi != null)
                {
                    foreach (var o in Svc.Objects)
                    {
                        if (o == null || o.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc) continue;
                        if (o is not Dalamud.Game.ClientState.Objects.Types.ICharacter) continue;
                        var chr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)o.Address;
                        if (chr == null || chr->Health <= 0) continue;
                        if (mhi->IsHuntTarget(chr))
                        {
                            snap.HuntTarget = o.Name.TextValue;
                            break;
                        }
                    }
                }
            }
            catch { /* same: guarded */ }

            Volatile.Write(ref _current, snap);
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"LazyFateAutomation: snapshot tick skipped ({ex.Message})");
        }
    }
}
