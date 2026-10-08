using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 6, SCH-CHAIN (SCH-1). T2: Chain Stratagem's Guard test must read the enemy the action itself
///     selected (<c>ChainStratagemPvP.Target.Target</c>), read after <c>CanUse</c> has run, not the hard
///     target (<c>Target</c>, which is the player when nothing is targeted).
/// </summary>
internal static class SchChain
{
    // A bare `Target.HasStatus(false, StatusID.Guard)`: `Target` not reached through another member.
    private static readonly Regex BareTargetGuard = new(@"(?<![\w.])Target\s*\.\s*HasStatus\s*\(\s*false\s*,\s*StatusID\.Guard\b");

    public static void Run()
    {
        Console.WriteLine("-- change 6 SCH-CHAIN (T2) --");

        var m = SourceFiles.OverrideMethod("SCH_Default.PVP.cs", "EmergencyAbility");
        var conds = CsSource.IfConditions(m.Body).Where(c => c.Contains("ChainStratagemPvP.CanUse(")).ToList();
        Harness.Case("SCH EmergencyAbility has exactly one Chain Stratagem branch", conds.Count == 1, $"{conds.Count} found");
        if (conds.Count != 1) return;

        var cond = conds[0];
        Harness.Case("Chain Stratagem branch has no bare Target.HasStatus(false, StatusID.Guard)", !BareTargetGuard.IsMatch(cond), cond);

        var canUse = cond.IndexOf("ChainStratagemPvP.CanUse(", StringComparison.Ordinal);
        var read = Regex.Match(cond, @"ChainStratagemPvP\.Target\.Target\.HasStatus\(\s*false\s*,\s*StatusID\.Guard\s*\)");
        Harness.Case("Chain Stratagem branch reads ChainStratagemPvP.Target.Target.HasStatus(false, Guard) after CanUse",
            read.Success && read.Index > canUse, cond);

        // Canary: the old text is recognised as the bare form, so this assertion (old text is clean) is false.
        const string old = "ChainStratagemPvP.CanUse(out action) && Target.HasStatus(false, StatusID.Guard)";
        Harness.Canary("old SCH condition is accepted as free of a bare Target.HasStatus Guard test", !BareTargetGuard.IsMatch(old));
    }
}
