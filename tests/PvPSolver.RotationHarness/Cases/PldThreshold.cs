namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 10, PLD-THRESHOLD (PLD-1), updated for the 7.57 upstream sync. T2: Guardian is
///     deduplicated into GeneralAbility, where both the free and Hallowed Ground paths apply
///     their configured HP thresholds to the chosen ally. AttackAbility must not retain the
///     older duplicate calls, one of which bypassed GuardianThreshold entirely.
/// </summary>
internal static class PldThreshold
{
    public static void Run()
    {
        Console.WriteLine("-- change 10 PLD-THRESHOLD (T2) --");

        var attack = SourceFiles.OverrideMethod("PLD_Default.PVP.cs", "AttackAbility");
        Harness.Case("PLD AttackAbility has no duplicate Guardian calls",
            !attack.Body.Contains("GuardianPvP.CanUse("));
        Harness.Case("PLD AttackAbility has no duplicate Hallowed Ground branch",
            !attack.Body.Contains("StatusID.HallowedGround_1302"));

        var general = SourceFiles.OverrideMethod("PLD_Default.PVP.cs", "GeneralAbility");
        var conds = CsSource.IfConditions(general.Body).Where(c => c.Contains("GuardianPvP.CanUse(")).ToList();
        Harness.Case("GeneralAbility has exactly the free and Hallowed Guardian uses", conds.Count == 2, $"{conds.Count} found");
        Harness.Case("GeneralAbility free Guardian carries GuardianThreshold",
            conds.Any(c => c.Contains("<= GuardianThreshold")));
        Harness.Case("GeneralAbility Hallowed Guardian carries HallowedGuardianThreshold",
            conds.Any(c => c.Contains("<= HallowedGuardianThreshold")));
        Harness.Case("GeneralAbility Hallowed Guardian respects HallowedGuardianFree",
            general.Body.Contains("if (HallowedGuardianFree)") && general.Body.Contains("StatusID.HallowedGround_1302"));
        Harness.Case("GeneralAbility thresholds read the ally Guardian chose after CanUse",
            conds.Count == 2 && conds.All(c =>
            {
                var canUse = c.IndexOf("GuardianPvP.CanUse(", StringComparison.Ordinal);
                var target = c.IndexOf("GuardianPvP.Target.Target.GetHealthRatio()", StringComparison.Ordinal);
                return canUse >= 0 && target > canUse;
            }));

        // Canary: the old duplicate AttackAbility call must fail the new no-Guardian assertion.
        const string oldAttack = "GuardianPvP.CanUse(out action, targetOverride: TargetType.LowHP)";
        Harness.Canary("old duplicate AttackAbility call passes the no-Guardian check",
            !oldAttack.Contains("GuardianPvP.CanUse("));
    }
}
