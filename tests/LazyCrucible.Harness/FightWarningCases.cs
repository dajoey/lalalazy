using Lalalazy.Crucible;
using LazyCrucible;

namespace LazyCrucible.Harness;

/// <summary>
///     What the fight guide says from the measured Master's Board data (recorded runs 2026-09-26 .. 09-30): the incoming-cast
///     warning for Atomic Ray, the low-HP entry advisories, and the guide text corrected from the measurements.
/// </summary>
internal static class FightWarningCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    public static void Run()
    {
        Console.WriteLine("-- fight warnings (measured) --");

        // Durga Piece, 2026-09-29 21:27:03 and 21:30:33: Atomic Ray, 12.7 s, hit the character for 3,998 and 4,782 (max HP 4,060)
        // while the familiar held enmity (CR|...|c=49272:12.6|...|f=yc, then f=pc for the rest of the cast).
        var ray = FightWarnings.ForCast(49272, 9.42f);
        Check("Atomic Ray 49272 with 9.4 s left: a warning naming the cast, the seconds and the 4,782 damage",
            ray is { Name: "Atomic Ray", MaxOnCharacter: 4782 } && ray.Text.Contains("Atomic Ray") && ray.Text.Contains("9.4 s") && ray.Text.Contains("4,782"), ray?.Text);
        Check("Atomic Ray warning says a familiar holding enmity does not stop it (0 of 2 casts followed the holder)",
            ray is not null && ray.Text.Contains("familiar holding enmity does not stop it"));
        Check("Atomic Ray with the castbar over: clamps to 0.0 s, never negative", FightWarnings.ForCast(49272, -0.3f) is { RemainingSeconds: 0f });
        Check("casts with no warning row give no warning: Sea of Pitch 48729, Sweeping Evisceration 48717, Grounding Jolt 49266, an unknown id, 0",
            new uint[] { 48729, 48717, 49266, 99999, 0 }.All(id => FightWarnings.ForCast(id, 5f) is null));

        // Entry HP: First Master's battle 3 was entered at 1%, 20%, 38% and 42% (2026-09-26 18:36, 09-29 18:05, 09-29 21:36, 09-30 14:35)
        // and the character was knocked out in every visit; Durga (Second Master's battle 6) was entered at 23%.
        Check("battle 3 at 1%, 20%, 38%, 42% HP: advisory for each recorded entry",
            new[] { 1, 20, 38, 42 }.All(hp => FightWarnings.EntryAdvice(4, 3, hp) is { } t && t.StartsWith($"Character at {hp}%") && t.Contains("random space")),
            FightWarnings.EntryAdvice(4, 3, 38));
        Check("battle 3 at 100% (a campsite came first): no advisory", FightWarnings.EntryAdvice(4, 3, 100) is null);
        Check("battle 3 at 70% (the line): no advisory; 69%: advisory", FightWarnings.EntryAdvice(4, 3, 70) is null && FightWarnings.EntryAdvice(4, 3, 69) is not null);
        Check("Durga at 23%: advisory names Atomic Ray; at 95%: none", FightWarnings.EntryAdvice(5, 6, 23) is { } d && d.Contains("Atomic Ray") && FightWarnings.EntryAdvice(5, 6, 95) is null);
        Check("a fight with no recorded entry problem never gets an advisory (Gargoyle, Strix)", FightWarnings.EntryAdvice(4, 5, 10) is null && FightWarnings.EntryAdvice(4, 1, 10) is null);

        // The guide text agrees with the measurements.
        var guide = CrucibleGuide.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CrucibleGuide.json")));
        var problems = guide.Validate();
        Check("Guide still validates after the measured corrections", problems.Count == 0, string.Join(" | ", problems.Take(5)));
        var strix = guide.Fight(4, 1)!;
        Check("Strix: On the Properties of Darkness is a dangerous single hit on the enmity holder, not 'room-wide, can't be avoided'",
            strix.Hits.Any(h => h.Ids.Contains(48669) && h.Tell.Contains("enmity holder") && h.Src.Contains("LOG"))
            && !strix.Mechanics.Any(m => m.Ids.Contains(48669)) && !System.Text.Json.JsonSerializer.Serialize(strix).Contains("room-wide hit", StringComparison.OrdinalIgnoreCase));
        var garg = guide.Fight(4, 5)!;
        Check("Gargoyle: Grim Fate is listed as a five-hit cast on the holder (measured), Sweeping Evisceration as a hit on whoever holds enmity, nothing 'appears only in BMR'",
            garg.Hits.Any(h => h.Ids.Contains(48730) && h.Tell.Contains("five hits") && h.Src.Contains("LOG"))
            && garg.Hits.Any(h => h.Ids.Contains(48717) && h.Tell.Contains("holds enmity"))
            && garg.Unknown.Count == 0);
        Check("Golem: Obliterate is a listed hit", guide.Fight(4, 7)!.Hits.Any(h => h.Ids.Contains(50649)));
        Check("Corpse Flower: Rotten Stench hit character and familiar together (no 'Snarl keeps it off the character')",
            guide.Fight(4, 3)!.Mechanics.Any(m => m.Ids.Contains(48690) && m.Tell.Contains("character and familiar") && !m.Do.Contains("Keep Snarl")));
        Check("Corpse Flower: the guide tells the player there is no heal before this fight", guide.Fight(4, 3)!.Bring.Any(b => b.Text.Contains("Full HP before the random space")));
        var durga = guide.Fight(5, 6)!;
        Check("Durga: Atomic Ray 12.7 s, measured 3,998 / 4,782 on the character while the familiar held enmity",
            durga.Hits.Any(h => h.Ids.Contains(49272) && h.Cast == 12.7 && h.Tell.Contains("4,782") && h.Src.Contains("LOG")));
        Check("Borgny: Salivous Snap is a listed hit", guide.Fight(4, 0)!.Hits.Any(h => h.Ids.Contains(48822)));
        Check("Every Tankbuster measured on a Master's Board is a listed hit or mechanic of its own fight in the guide",
            BST_CrucibleData.HeavyCasts.Where(h => h.Kind == CrucibleHitKind.Tankbuster).All(h =>
                guide.Fight(h.Board, h.Battle) is { } f && f.Hits.Concat(f.Mechanics).Any(m => m.Ids.Contains(h.CastId) || (h.CastId == 49188 && m.Ids.Contains(49188)))),
            string.Join(",", BST_CrucibleData.HeavyCasts.Where(h => h.Kind == CrucibleHitKind.Tankbuster && !(guide.Fight(h.Board, h.Battle) is { } f && f.Hits.Concat(f.Mechanics).Any(m => m.Ids.Contains(h.CastId)))).Select(h => h.Name)));
        Check("The LOG source is declared", guide.File.Sources.ContainsKey("LOG"));
    }
}
