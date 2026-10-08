using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 10, PLD-THRESHOLD (PLD-1). T2: AttackAbility's Hallowed Ground Guardian branch must apply the
///     <c>HallowedGuardianThreshold</c> HP test to the ally Guardian chose (<c>GuardianPvP.Target.Target</c>,
///     read after <c>CanUse</c>), as the GeneralAbility branch already does. Without it Guardian fires at any HP
///     whenever an enemy is in range.
/// </summary>
internal static class PldThreshold
{
    public static void Run()
    {
        Console.WriteLine("-- change 10 PLD-THRESHOLD (T2) --");

        var m = SourceFiles.OverrideMethod("PLD_Default.PVP.cs", "AttackAbility");
        var at = Regex.Match(m.Body, @"if\s*\(\s*StatusHelper\.PlayerHasStatus\(\s*true\s*,\s*StatusID\.HallowedGround_1302\s*\)\s*\)\s*\{");
        Harness.Case("PLD AttackAbility has a Hallowed Ground branch", at.Success);
        if (!at.Success) return;

        var open = at.Index + at.Length - 1;
        var inner = m.Body.Substring(open + 1, CsSource.Match(m.Body, open) - open - 1);
        var conds = CsSource.IfConditions(inner).Where(c => c.Contains("GuardianPvP.CanUse(")).ToList();
        Harness.Case("Hallowed Ground branch has exactly one Guardian use", conds.Count == 1, $"{conds.Count} found");
        if (conds.Count != 1) return;

        var cond = conds[0];
        var canUse = cond.IndexOf("GuardianPvP.CanUse(", StringComparison.Ordinal);
        var test = Regex.Match(cond, @"GuardianPvP\.Target\.Target\.GetHealthRatio\(\)\s*<=\s*HallowedGuardianThreshold\b");
        Harness.Case("Hallowed Ground Guardian is held until the chosen ally is <= HallowedGuardianThreshold",
            test.Success && test.Index > canUse, cond);

        // The existing GeneralAbility branch is the model and must still carry the same test.
        var general = SourceFiles.OverrideMethod("PLD_Default.PVP.cs", "GeneralAbility");
        Harness.Case("GeneralAbility Hallowed Guardian branch still carries the threshold",
            CsSource.IfConditions(general.Body).Any(c => c.Contains("GuardianPvP.CanUse(") && c.Contains("<= HallowedGuardianThreshold")));

        // Canary: the old AttackAbility condition (CanUse only) is not a threshold-gated one.
        const string old = "GuardianPvP.CanUse(out action, targetOverride: TargetType.LowHP)";
        Harness.Canary("old Hallowed Ground Guardian condition carries the HP threshold", old.Contains("HallowedGuardianThreshold"));
    }
}
