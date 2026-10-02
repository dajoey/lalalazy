using Lalalazy.Crucible;

namespace LazyCrucible.Harness;

/// <summary>
///     The fight guide checked against the recorded runs (task tasks-20261002-crucible-horn-picks-by-needed-abilities-01,
///     amendment C, 2026-10-02). The evidence comes from Joey's Dalamud log (CR| telemetry, the game's own "cleansable debuff on the
///     character" flag C) joined with ffxivdb status events on the character, 2026-09-24 .. 10-01: a status counts as cleansable only
///     when C was set while it was on the character. Statuses the logs show were NEVER cleansable are not guide counters.
/// </summary>
internal static class ResearchLogCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    public static void Run()
    {
        Console.WriteLine("-- research checked against the logs --");
        var guide = CrucibleGuide.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CrucibleGuide.json")));

        // Controls: the log agrees with the guide, so the counter stays.
        Check("control, Banemite poison (1.4, status 5140): cleansable 5 of 5 applications, the cleanse counter stays",
            guide.Fight(1, 4)!.Counters.Any(c => c.AsNeed == CrucibleNeeds.Cleanse));
        Check("control, Flauros paralysis (5.1, status 5382): cleansable 4 of 4 applications, the cleanse counter stays",
            guide.Fight(5, 1)!.Counters.Any(c => c.AsNeed == CrucibleNeeds.Cleanse));

        // Disproved: the game never flagged these cleansable (applications / windows with the C flag).
        Check("Lakhamu + Golem (3.5): Sand Tempest's Blind (5389) was on the character 30 times and never cleansable, so it is not a cleanse counter",
            !guide.Fight(3, 5)!.Counters.Any(c => c.AsNeed == CrucibleNeeds.Cleanse), string.Join(" | ", guide.Fight(3, 5)!.Counters.Select(c => c.What)));
        Check("Lakhamu + Golem (3.5): the unresolved list no longer asks whether Sand Tempest's blind can be cleansed",
            !guide.Fight(3, 5)!.Unknown.Any(u => u.Contains("Sand Tempest", StringComparison.Ordinal)));
        Check("Borgny (4.0): Toxicosis (5183) was on the character 33 times (339 s) and never cleansable, so it is not a cleanse counter",
            !guide.Fight(4, 0)!.Counters.Any(c => c.AsNeed == CrucibleNeeds.Cleanse), string.Join(" | ", guide.Fight(4, 0)!.Counters.Select(c => c.What)));
        Check("Borgny (4.0): the unresolved list no longer asks whether the toxin can be cleansed",
            !guide.Fight(4, 0)!.Unknown.Any(u => u.Contains("Toxin", StringComparison.Ordinal)));
        Check("Catoblepas (3.4): Petrification (4891) was on the character 4 times (35 s) and never cleansable, so Scouring Ash is not listed as removing it",
            !guide.Fight(3, 4)!.Bring.Any(b => b.Text.Contains("Scouring Ash", StringComparison.Ordinal)), string.Join(" | ", guide.Fight(3, 4)!.Bring.Select(b => b.Text)));

        // Cast times the guide gave that a recorded run contradicts: the castbar remaining read on the target at its first sample
        // (the plugin's bar runs 0.7 s over the cast time) can never exceed the cast, so a guide cast shorter than the observed
        // remaining is wrong. Largest observed remaining minus 0.7 s, samples in brackets.
        (int Board, int Battle, string Name, double Cast, int Samples)[] measured =
        [
            (2, 5, "1000-tonze Swipe", 9.0, 15), (2, 5, "1111-tonze Swing", 13.0, 7), (2, 5, "10-tonze Stomp", 9.5, 15),
            (3, 3, "Fore Carve / Rear Carve", 5.0, 2), (3, 3, "Crossbreeze", 9.0, 8), (3, 3, "Airy Pursuit", 7.0, 3),
            (4, 7, "Stoneshower", 2.0, 6),
        ];
        var wrong = new List<string>();
        foreach (var (board, battle, name, cast, _) in measured)
        {
            var m = guide.Fight(board, battle)!.Mechanics.FirstOrDefault(x => x.Name == name);
            if (m is null || m.Cast is not { } c || Math.Abs(c - cast) > 0.05)
                wrong.Add($"{board}:{battle} {name} guide {m?.Cast} vs logged {cast}");
        }
        Check("guide cast times follow the recorded castbars where the guide was off by a second or more (7 casts: Elder / Younger Tablitaur swings, the zu pack's carves, crossbreeze and pursuit, Golem's Stoneshower)",
            wrong.Count == 0, string.Join(" | ", wrong));

        // Still open, and said so: Might was never dispelled in a recorded run.
        Check("Lakhamu + Golem (3.5): the Might dispel stays a disputed (Useful) counter, and the unresolved list says no run has dispelled it",
            guide.Fight(3, 5)!.Counters.Any(c => c.AsNeed == CrucibleNeeds.Dispel && c.Disputed)
            && guide.Fight(3, 5)!.Unknown.Any(u => u.Contains("Might", StringComparison.Ordinal) && u.Contains("no run", StringComparison.OrdinalIgnoreCase)),
            string.Join(" | ", guide.Fight(3, 5)!.Unknown));
    }
}
