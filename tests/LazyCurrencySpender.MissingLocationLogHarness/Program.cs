using System.Text.RegularExpressions;
using CurrencySpender.Helpers;

namespace LazyCurrencySpender.MissingLocationLogHarness;

/// <summary>
///     Offline assertions on the missing-map-location log policy. Replays the measured 2026-10-04
///     incident: seven shop NPCs with no location, logged once per item per frame at Error level.
///     Plus the 2026-10-07 follow-up measured by the same query: the Faux Commander (1033921, the
///     Faux Hollows vendor mapped from SpecialShop 1770282), which the 1.3.1.1 upstream sync added
///     to the shop generation without a table entry - one Error per session init.
///     Also asserts the map-location table itself (the shipped data, parsed from
///     src/LazyCurrencySpender/Classes/Location.cs, which is not Dalamud-free and so cannot be
///     compiled here): every incident NPC is either placed with a non-zero position or recorded as
///     a deliberate gap, so the data gap stays explicit instead of silent.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    // ffxivdb: NpcId -> ERR lines (plugin_log_lines, context LazyCurrencySpender). The first seven
    // on 2026-10-04 (per frame), the Faux Commander on 2026-10-07 (once per init, 1.3.1.1).
    private static readonly (uint NpcId, int Lines)[] Incident =
    [
        (1053904, 31272), (1016305, 27929), (1010488, 26856), (1056512, 26250),
        (1049083, 23454), (1049034, 22864), (1017102, 564), (1033921, 3),
    ];

    // Incident shop NPCs that stay OUT of the map-location table on purpose: the game data places
    // them nowhere (no Level-sheet row of Type 8, no ENpcPlace placement - campaign/seasonal NPCs).
    // An incident NPC must be either in the table or here; adding a real placement for one of these
    // means moving it out of this list, never leaving both stale.
    private static readonly uint[] DeliberatelyUnplaced =
    [
        1053904, 1016305, 1056512, 1049083, 1049034,
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

        Check("replay covers the real incident (159,192 lines)", calls == 159_192, calls.ToString());
        Check("an expected data gap never logs at Error", errors == 0, errors.ToString());
        Check("one notice per NPC for the whole replay (8, not 159,192)", emitted == 8, emitted.ToString());

        var fresh = new MissingLocationLog();
        Check("first sighting of an NPC is reported at Debug",
            fresh.Decide(1049083) == MissingLocationLogLevel.Debug);
        Check("the same NPC again on the next frame is silent",
            fresh.Decide(1049083) == MissingLocationLogLevel.None);
        Check("a different NPC is still reported on its first sighting",
            fresh.Decide(1053904) == MissingLocationLogLevel.Debug);

        CheckLocationTable();

        Console.WriteLine($"{_pass} pass, {_fail} fail");
        if (_fail == 0) Console.WriteLine("OK");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     Asserts the shipped map-location data (the <c>locations</c> list in
    ///     src/LazyCurrencySpender/Classes/Location.cs) against the incident: every incident shop
    ///     NPC has an entry with a non-zero position, or is recorded in <see cref="DeliberatelyUnplaced"/>.
    ///     The table source is parsed as text because Location.cs is not Dalamud-free.
    /// </summary>
    private static void CheckLocationTable()
    {
        // bin/Release/net10.0 -> up five levels -> the repo root.
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = Path.Combine(root, "src", "LazyCurrencySpender", "Classes", "Location.cs");
        var text = File.ReadAllText(source);

        var entries = Regex.Matches(text, @"new Location \{([^{}]*)\}")
            .Select(m => m.Groups[1].Value)
            .Select(ParseEntry)
            .ToList();

        Check("the map-location table parses (at least 60 entries)", entries.Count >= 60, $"{entries.Count} entries");
        Check("no table entry ships a zero position",
            entries.All(e => e.X != 0f && e.Y != 0f),
            string.Join(",", entries.Where(e => e.X == 0f && e.Y == 0f).Select(e => e.NpcId)));
        Check("the deliberately-unplaced list names only incident NPCs",
            DeliberatelyUnplaced.All(id => Incident.Any(i => i.NpcId == id)));

        foreach (var (npcId, _) in Incident)
        {
            var entry = entries.FirstOrDefault(e => e.NpcId == npcId);
            var found = entry.NpcId == npcId;   // the default tuple has NpcId 0; real entries never do
            if (DeliberatelyUnplaced.Contains(npcId))
                Check($"incident NPC {npcId} stays out of the table only by record", !found,
                    found ? $"has an entry at {entry.MapId}/{entry.TerritoryId}" : "");
            else
                Check($"incident NPC {npcId} has a table entry with a non-zero position",
                    found && entry.X != 0f && entry.Y != 0f,
                    found ? $"at {entry.MapId}/{entry.TerritoryId} ({entry.X},{entry.Y})" : "no entry");
        }
    }

    private static (uint NpcId, uint MapId, uint TerritoryId, float X, float Y) ParseEntry(string body)
    {
        return (
            Field(body, "NpcId"),
            Field(body, "MapId"),
            Field(body, "TerritoryId"),
            Pos(body, 1),
            Pos(body, 2));
    }

    private static uint Field(string body, string name) =>
        uint.TryParse(Regex.Match(body, $@"\b{name} = (\d+)").Groups[1].Value, out var v) ? v : 0;

    private static float Pos(string body, int group) =>
        float.TryParse(Regex.Match(body, @"Position = new Pos\(\s*([0-9.]+)f\s*,\s*([0-9.]+)f\s*\)").Groups[group].Value,
            out var v) ? v : 0f;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  [" + detail + "]" : "")}");
    }
}
