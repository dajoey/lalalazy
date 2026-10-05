using CurrencySpender.Helpers;

namespace LazyCurrencySpender.MissingLocationLogHarness;

/// <summary>
///     Offline assertions on the missing-map-location log policy. Replays the measured 2026-10-04
///     incident: seven shop NPCs with no location, logged once per item per frame at Error level.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    // ffxivdb: NpcId -> ERR lines on 2026-10-04 (plugin_log_lines, context LazyCurrencySpender).
    private static readonly (uint NpcId, int Lines)[] Incident =
    [
        (1053904, 31272), (1016305, 27929), (1010488, 26856), (1056512, 26250),
        (1049083, 23454), (1049034, 22864), (1017102, 564),
    ];

    private static int Main()
    {
        var log = new MissingLocationLog();
        var errors = 0;
        var emitted = 0;
        var calls = 0;
        foreach (var (npcId, lines) in Incident)
            for (var i = 0; i < lines; i++)
            {
                calls++;
                var level = log.Decide(npcId);
                if (level == MissingLocationLogLevel.Error) errors++;
                if (level != MissingLocationLogLevel.None) emitted++;
            }

        Check("replay covers the real incident (159,189 lines)", calls == 159_189, calls.ToString());
        Check("an expected data gap never logs at Error", errors == 0, errors.ToString());
        Check("one notice per NPC for the whole replay (7, not 159,189)", emitted == 7, emitted.ToString());

        var fresh = new MissingLocationLog();
        Check("first sighting of an NPC is reported at Debug",
            fresh.Decide(1049083) == MissingLocationLogLevel.Debug);
        Check("the same NPC again on the next frame is silent",
            fresh.Decide(1049083) == MissingLocationLogLevel.None);
        Check("a different NPC is still reported on its first sighting",
            fresh.Decide(1053904) == MissingLocationLogLevel.Debug);

        Console.WriteLine($"{_pass} pass, {_fail} fail");
        if (_fail == 0) Console.WriteLine("OK");
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  [" + detail + "]" : "")}");
    }
}
