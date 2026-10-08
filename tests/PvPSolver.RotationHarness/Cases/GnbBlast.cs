using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 8, GNB-BLAST (GNB-2). T2: Blasting Zone's 50 percent test must read the enemy the action selected
///     (<c>BlastingZonePvP.Target.Target</c>, read after <c>CanUse</c> ran), not <c>Target</c> (the hard target,
///     or the player's own HP when nothing is targeted).
/// </summary>
internal static class GnbBlast
{
    private static readonly Regex BareTargetHp = new(@"(?<![\w.])Target\s*\.\s*GetHealthRatio\s*\(");

    public static void Run()
    {
        Console.WriteLine("-- change 8 GNB-BLAST (T2) --");

        var m = SourceFiles.OverrideMethod("GNB_Default.PVP.cs", "AttackAbility");
        var conds = CsSource.IfConditions(m.Body).Where(c => c.Contains("BlastingZonePvP.CanUse(")).ToList();
        Harness.Case("GNB AttackAbility has exactly one Blasting Zone branch", conds.Count == 1, $"{conds.Count} found");
        if (conds.Count != 1) return;

        var cond = conds[0];
        Harness.Case("Blasting Zone branch has no bare Target.GetHealthRatio()", !BareTargetHp.IsMatch(cond), cond);

        var canUse = cond.IndexOf("BlastingZonePvP.CanUse(", StringComparison.Ordinal);
        var read = Regex.Match(cond, @"BlastingZonePvP\.Target\.Target\.GetHealthRatio\(\)\s*\*\s*100\s*<=\s*50\b");
        Harness.Case("Blasting Zone branch tests BlastingZonePvP.Target.Target.GetHealthRatio() * 100 <= 50 after CanUse",
            read.Success && read.Index > canUse, cond);

        // Canary: the old condition text reads the hard target, so "old is clean" is false.
        const string old = "Target.GetHealthRatio() * 100 <= 50 && BlastingZonePvP.CanUse(out action)";
        Harness.Canary("old Blasting Zone condition is accepted as free of a bare Target.GetHealthRatio()", !BareTargetHp.IsMatch(old));
    }
}
