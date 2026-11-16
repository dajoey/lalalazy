using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace LazyRetainerLive.WireProbe;

// Dalamud-free harness: proves the exact wire bytes RetainerWire emits.
// Exit 0 = all assertions hold; any mismatch prints and exits 1.
internal static class Program
{
    private static int Main()
    {
        var failures = new List<string>();

        var c = new CharInfo
        {
            Char = "TestCharacter",
            World = "Hyperion",
            Gil = 29279279,
            Ventures = 5065,
            Seals = 12345,
            Inventory = 42,
            Retainers =
            [
                new RetainerInfo { Name = "RetainerC", Job = 25, Level = 100, HasVenture = true, EndsAt = 1788647220, Gil = 0, VentureId = 939, Mb = 20 },
                new RetainerInfo { Name = "RetainerD", Job = 17, Level = 100, HasVenture = true, EndsAt = 1788648041, Gil = 123, VentureId = 395, Mb = 3 },
                new RetainerInfo { Name = "RetainerA", Job = 16, Level = 100, HasVenture = false, EndsAt = 0, Gil = 0, VentureId = 0, Mb = 0 },
                new RetainerInfo { Name = "RetainerB", Job = 18, Level = 100, HasVenture = true, EndsAt = 1788648278, Gil = 999, VentureId = 395, Mb = 20 },
            ],
        };

        var json = System.Text.Encoding.UTF8.GetString(RetainerWire.Write(c, 1788640000));
        Console.WriteLine(json);

        // 1. Round-trips as JSON.
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (Exception ex) { Console.WriteLine($"FAIL: not valid JSON: {ex.Message}"); return 1; }

        var root = doc.RootElement;
        Check(failures, root.TryGetProperty("type", out var t) && t.GetString() == "Retainers", "type==Retainers");
        Check(failures, root.GetProperty("source").GetString() == "plugin", "source==plugin");
        Check(failures, root.GetProperty("readAt").GetInt64() == 1788640000, "readAt value");
        Check(failures, !root.TryGetProperty("mtime", out _), "no mtime key");

        // 2. Key order of the top-level frame: type, chars, readAt, source.
        var topKeys = root.EnumerateObject().Select(p => p.Name).ToArray();
        Check(failures, topKeys.SequenceEqual(["type", "chars", "readAt", "source"]),
            $"top-level key order = [{string.Join(",", topKeys)}]");

        // 3. chars[0] key order: char, world, gil, ventures, seals, inventory, retainers.
        var ch = root.GetProperty("chars")[0];
        var charKeys = ch.EnumerateObject().Select(p => p.Name).ToArray();
        Check(failures, charKeys.SequenceEqual(["char", "world", "gil", "ventures", "seals", "inventory", "retainers"]),
            $"char key order = [{string.Join(",", charKeys)}]");

        Check(failures, ch.GetProperty("char").GetString() == "TestCharacter", "char value");
        Check(failures, ch.GetProperty("world").GetString() == "Hyperion", "world value");
        Check(failures, ch.GetProperty("gil").GetInt64() == 29279279, "gil value");
        Check(failures, ch.GetProperty("ventures").GetInt64() == 5065, "ventures value");
        Check(failures, ch.GetProperty("seals").GetInt64() == 12345, "seals value");
        Check(failures, ch.GetProperty("inventory").GetInt64() == 42, "inventory value");

        // 4. Retainer key order: name, job, level, hasVenture, endsAt, gil, ventureId, mb.
        var r0 = ch.GetProperty("retainers")[0];
        var retKeys = r0.EnumerateObject().Select(p => p.Name).ToArray();
        Check(failures, retKeys.SequenceEqual(["name", "job", "level", "hasVenture", "endsAt", "gil", "ventureId", "mb"]),
            $"retainer key order = [{string.Join(",", retKeys)}]");

        Check(failures, r0.GetProperty("name").GetString() == "RetainerC", "retainer name");
        Check(failures, r0.GetProperty("job").GetInt32() == 25, "job value");
        Check(failures, r0.GetProperty("level").GetInt32() == 100, "level value");
        Check(failures, r0.GetProperty("hasVenture").GetBoolean() == true, "hasVenture value");
        Check(failures, r0.GetProperty("endsAt").GetInt64() == 1788647220, "endsAt value");
        Check(failures, r0.GetProperty("ventureId").GetInt32() == 939, "ventureId value");
        Check(failures, r0.GetProperty("mb").GetInt32() == 20, "mb value");

        Check(failures, ch.GetProperty("retainers").GetArrayLength() == 4, "four retainers");
        var r2 = ch.GetProperty("retainers")[2];
        Check(failures, r2.GetProperty("hasVenture").GetBoolean() == false && r2.GetProperty("endsAt").GetInt64() == 0,
            "no-venture retainer: hasVenture=false endsAt=0");

        // 5. 503 body: exactly {"type":"Retainers","chars":[],"source":"plugin","readAt":N}.
        var un = System.Text.Encoding.UTF8.GetString(RetainerWire.WriteUnavailable(1788649999));
        Console.WriteLine(un);
        Check(failures, un == "{\"type\":\"Retainers\",\"chars\":[],\"source\":\"plugin\",\"readAt\":1788649999}",
            "503 body exact bytes");

        // 6. Window presentation derives only from real listener and snapshot state.
        var disabled = RetainerLivePresentation.Status(false, "old bind error", c, true);
        Check(failures, disabled.State == RetainerLiveState.Idle, "disabled listener is Idle");
        Check(failures, disabled.Action == "Listener stopped", "disabled listener action");
        Check(failures, RetainerLivePresentation.LiveSubtext(false, 10504) == "Listener off · snapshots still update",
            "disabled listener subtext");

        var failed = RetainerLivePresentation.Status(true, "bind failed", c, true);
        Check(failures, failed.State == RetainerLiveState.Failed && failed.Detail == "bind failed",
            "bind failure is surfaced");

        var missing = RetainerLivePresentation.Status(true, "", null, false);
        Check(failures, missing.State == RetainerLiveState.NeedsSetup, "missing first snapshot needs setup");
        Check(failures, missing.Detail.Contains("503", StringComparison.Ordinal), "missing snapshot explains 503");
        Check(failures, RetainerLivePresentation.LiveCount(null) == "No snapshot", "empty live count");

        var frozen = RetainerLivePresentation.Status(true, "", c, false);
        Check(failures, frozen.State == RetainerLiveState.Blocked, "last-known snapshot is Blocked");
        Check(failures, frozen.Action == "Serving the last-known snapshot", "last-known action");

        var live = RetainerLivePresentation.Status(true, "", c, true);
        Check(failures, live.State == RetainerLiveState.Ready, "fresh snapshot is Ready");
        Check(failures, RetainerLivePresentation.ActiveVentures(c) == 3, "three active ventures");
        Check(failures, RetainerLivePresentation.LiveCount(c) == "4 retainers · 3 active ventures",
            "live retainer and venture count");
        Check(failures, RetainerLivePresentation.LiveSubtext(true, 10504) == "127.0.0.1:10504 · GET /retainers",
            "live endpoint subtext");

        ListenerRebindCase(failures);

        if (failures.Count > 0)
        {
            Console.WriteLine($"FAIL ({failures.Count}):");
            foreach (var f in failures) Console.WriteLine("  - " + f);
            return 1;
        }
        Console.WriteLine("ALL WIRE CHECKS PASS");
        Console.WriteLine("ALL PRESENTATION CHECKS PASS");
        Console.WriteLine("ALL LISTENER REBIND CHECKS PASS");
        return 0;
    }

    // A port change stops the listener and binds a new one. A stale serve loop woken
    // after the rebind used to spin on the dead listener; twenty rebinds must leave
    // the process idle and the last listener serving.
    private static void ListenerRebindCase(List<string> failures)
    {
        var ports = new[] { FreePort(), FreePort() };
        var current = ports[0];
        var server = new HttpServer(new RetainerLiveService(), () => current, () => true);
        try
        {
            // Keep the first serve loop busy inside a request, so it is still running
            // when the rebinds finish and only notices the stop afterwards.
            server.EnsureStarted();
            using var holder = new TcpClient();
            holder.Connect(IPAddress.Loopback, current);
            Thread.Sleep(300);

            for (var i = 1; i <= 20; i++)
            {
                current = ports[i % 2];
                server.Stop();
                server.EnsureStarted();
            }

            holder.Close();

            Check(failures, server.LastError.Length == 0, "rebind: last bind succeeded (" + server.LastError + ")");

            var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            Thread.Sleep(1500);
            var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds;
            Console.WriteLine($"rebind: idle CPU over 1.5 s = {cpu:F3} s");
            Check(failures, cpu < 0.3, $"rebind: idle CPU over 1.5 s was {cpu:F3} s (limit 0.3 s)");

            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, current);
            var stream = client.GetStream();
            var req = System.Text.Encoding.ASCII.GetBytes("GET /retainers HTTP/1.1\r\nHost: x\r\n\r\n");
            stream.Write(req, 0, req.Length);
            var buf = new byte[256];
            var n = stream.Read(buf, 0, buf.Length);
            var head = System.Text.Encoding.ASCII.GetString(buf, 0, n);
            Check(failures, head.StartsWith("HTTP/1.1 503"), "rebind: the last listener still answers (" + head.Split('\n')[0].Trim() + ")");
        }
        finally
        {
            server.Dispose();
        }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static void Check(List<string> failures, bool cond, string what)
    {
        if (!cond) failures.Add(what);
    }
}
