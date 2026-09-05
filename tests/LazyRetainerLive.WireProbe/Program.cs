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
            Char = "Grandpa Joe",
            World = "Hyperion",
            Gil = 29279279,
            Ventures = 5065,
            Seals = 12345,
            Inventory = 42,
            Retainers =
            [
                new RetainerInfo { Name = "Hussypants", Job = 25, Level = 100, HasVenture = true, EndsAt = 1788647220, Gil = 0, VentureId = 939, Mb = 20 },
                new RetainerInfo { Name = "Sofondapeters", Job = 17, Level = 100, HasVenture = true, EndsAt = 1788648041, Gil = 123, VentureId = 395, Mb = 3 },
                new RetainerInfo { Name = "Bussyqueen", Job = 16, Level = 100, HasVenture = false, EndsAt = 0, Gil = 0, VentureId = 0, Mb = 0 },
                new RetainerInfo { Name = "Dojarat", Job = 18, Level = 100, HasVenture = true, EndsAt = 1788648278, Gil = 999, VentureId = 395, Mb = 20 },
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

        Check(failures, ch.GetProperty("char").GetString() == "Grandpa Joe", "char value");
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

        Check(failures, r0.GetProperty("name").GetString() == "Hussypants", "retainer name");
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

        if (failures.Count > 0)
        {
            Console.WriteLine($"FAIL ({failures.Count}):");
            foreach (var f in failures) Console.WriteLine("  - " + f);
            return 1;
        }
        Console.WriteLine("ALL WIRE CHECKS PASS");
        return 0;
    }

    private static void Check(List<string> failures, bool cond, string what)
    {
        if (!cond) failures.Add(what);
    }
}
