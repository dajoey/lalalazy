using System.Text.RegularExpressions;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 3, DEFENSE-STALL (TANK-S1). T1: <see cref="DefenseSingleGate.EndsStage"/> over all 32 input
///     combinations. Outside PvP it equals the OLD expression (PvE behaviour unchanged); in PvP it equals
///     <c>abilityFound</c>, so the stage no longer returns true with no action and the later Attack and General
///     abilities still run. The call site in CustomRotation_Ability.cs is only shape-checked (not claimed as T1).
/// </summary>
internal static class DefenseStall
{
    // The expression as it stood in Ability(): found || (!castingToTank && !Vengeance && !Damnation).
    private static bool OldExpression(bool found, bool hostileCastingToTank, bool hasVengeance, bool hasDamnation) =>
        found || (!hostileCastingToTank && !hasVengeance && !hasDamnation);

    private static IEnumerable<(bool Found, bool Pvp, bool Cast, bool Veng, bool Damn)> AllRows()
    {
        bool[] b = [false, true];
        foreach (var found in b) foreach (var pvp in b) foreach (var cast in b) foreach (var veng in b) foreach (var damn in b)
            yield return (found, pvp, cast, veng, damn);
    }

    private static bool Expected(bool found, bool pvp, bool cast, bool veng, bool damn) =>
        pvp ? found : OldExpression(found, cast, veng, damn);

    private static string[] Mismatches(Func<bool, bool, bool, bool, bool, bool> impl) =>
        AllRows().Where(r => impl(r.Found, r.Pvp, r.Cast, r.Veng, r.Damn) != Expected(r.Found, r.Pvp, r.Cast, r.Veng, r.Damn))
            .Select(r => $"found={r.Found},pvp={r.Pvp},cast={r.Cast},veng={r.Veng},damn={r.Damn}").ToArray();

    public static void Run()
    {
        Console.WriteLine("-- change 3 DEFENSE-STALL (T1) --");

        var rows = AllRows().ToArray();
        Harness.Case("truth table covers all 32 combinations", rows.Length == 32 && rows.Distinct().Count() == 32);

        var pve = rows.Where(r => !r.Pvp).ToArray();
        var pveBad = pve.Where(r => DefenseSingleGate.EndsStage(r.Found, false, r.Cast, r.Veng, r.Damn) != OldExpression(r.Found, r.Cast, r.Veng, r.Damn)).ToArray();
        Harness.Case("PvE (isPvP false): equals the old expression for all 16 combinations", pve.Length == 16 && pveBad.Length == 0,
            $"{pve.Length} rows, {pveBad.Length} differ");

        var pvp = rows.Where(r => r.Pvp).ToArray();
        var pvpBad = pvp.Where(r => DefenseSingleGate.EndsStage(r.Found, true, r.Cast, r.Veng, r.Damn) != r.Found).ToArray();
        Harness.Case("PvP (isPvP true): equals abilityFound for all 16 combinations", pvp.Length == 16 && pvpBad.Length == 0,
            $"{pvp.Length} rows, {pvpBad.Length} differ");

        Harness.Case("PvP with nothing found never ends the stage (the stall row)",
            !DefenseSingleGate.EndsStage(false, true, false, false, false), "found=False,pvp=True,cast=False,veng=False,damn=False -> False");

        // Red against the old behaviour: in PvP the old expression ends the stage with no action in exactly one row.
        var oldBad = Mismatches((f, p, c, v, d) => OldExpression(f, c, v, d));
        Harness.Case("RED on old behaviour: the old expression is wrong in exactly the PvP stall row",
            oldBad.Length == 1 && oldBad[0] == "found=False,pvp=True,cast=False,veng=False,damn=False", string.Join(" | ", oldBad));

        // Call site: Ability() routes the whole condition through the gate, with DataCenter.IsPvP.
        var path = Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "Rotations", "CustomRotation_Ability.cs");
        var san = CsSource.Sanitize(File.ReadAllText(path));
        Harness.Case("Ability() calls DefenseSingleGate.EndsStage(defenseSingleFound, DataCenter.IsPvP, ...)",
            Regex.IsMatch(san, @"DefenseSingleGate\.EndsStage\(\s*defenseSingleFound\s*,\s*DataCenter\.IsPvP\s*,"));
        Harness.Case("Ability() no longer carries the raw PvE tail (!IsHostileCastingToTank && !Vengeance && !Damnation)",
            !Regex.IsMatch(san, @"!\s*DataCenter\.IsHostileCastingToTank\s*&&"));

        // Canary: the old expression ignores isPvP, so it must not satisfy the table.
        Harness.Canary("old expression (ignores isPvP) satisfies the truth table", oldBad.Length == 0);
    }
}
