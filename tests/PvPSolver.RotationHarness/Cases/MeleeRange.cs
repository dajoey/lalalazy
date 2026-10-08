using System.Text.RegularExpressions;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 4, MELEE-RANGE (RANGE-1). T1: <see cref="AttackRangeGate.Radius"/> returns 5 only for a melee job
///     in PvP and otherwise passes <c>jobRange</c> through unchanged (tanks, ranged jobs and PvE keep theirs).
///     <c>DataCenter.JobRange</c> is not changed. The call sites and the "same distance as
///     NumberOfHostilesInRange" claim are only shape-checked (not claimed as T1).
/// </summary>
internal static class MeleeRange
{
    private static readonly float[] JobRanges = [0f, 3f, 4.99f, 5f, 5.01f, 10f, 25f];

    private static IEnumerable<(bool Pvp, bool Melee, float Range)> AllRows()
    {
        foreach (var pvp in new[] { false, true })
            foreach (var melee in new[] { false, true })
                foreach (var r in JobRanges)
                    yield return (pvp, melee, r);
    }

    private static float Expected(bool pvp, bool melee, float range) => pvp && melee ? 5f : range;

    private static string[] Mismatches(Func<bool, bool, float, float> impl) =>
        AllRows().Where(r => impl(r.Pvp, r.Melee, r.Range) != Expected(r.Pvp, r.Melee, r.Range))
            .Select(r => $"pvp={r.Pvp},melee={r.Melee},range={r.Range}").ToArray();

    private static string Between(string s, string from, string to)
    {
        var a = s.IndexOf(from, StringComparison.Ordinal);
        var b = a < 0 ? -1 : s.IndexOf(to, a + from.Length, StringComparison.Ordinal);
        return a < 0 || b < 0 ? "" : s.Substring(a, b - a);
    }

    public static void Run()
    {
        Console.WriteLine("-- change 4 MELEE-RANGE (T1) --");

        var rows = AllRows().ToArray();
        Harness.Case("domain covers pvp x melee x 7 job ranges (28 rows)", rows.Length == 28);

        var bad = Mismatches(AttackRangeGate.Radius);
        Harness.Case("Radius: 5 only for PvP melee, otherwise jobRange unchanged, over the whole domain", bad.Length == 0,
            bad.Length == 0 ? "28 rows" : string.Join(" | ", bad));
        Harness.Case("tank in PvP (melee=false) keeps its job range 3", AttackRangeGate.Radius(true, false, 3f) == 3f);
        Harness.Case("melee in PvE keeps its job range 3", AttackRangeGate.Radius(false, true, 3f) == 3f);
        Harness.Case("melee in PvP uses 5", AttackRangeGate.Radius(true, true, 3f) == 5f);

        // Red against the old behaviour: the old gate always used jobRange, so it is wrong exactly where PvP melee has jobRange != 5.
        var oldBad = Mismatches((pvp, melee, range) => range);
        Harness.Case("RED on old behaviour: the old gate (always jobRange) is wrong for PvP melee at every range except 5",
            oldBad.Length == JobRanges.Count(r => r != 5f) && oldBad.All(x => x.StartsWith("pvp=True,melee=True")), $"{oldBad.Length} rows");

        // Call sites in Ability(): both AttackAbility gates use the new radius; neither still uses HasHostilesInRange.
        var abilityPath = Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "Rotations", "CustomRotation_Ability.cs");
        var ability = CsSource.Sanitize(File.ReadAllText(abilityPath));
        Harness.Case("Ability() counts hostiles with NumberOfHostilesInRangeOf(AttackRangeGate.Radius(IsPvP, Role == Melee, JobRange))",
            Regex.IsMatch(ability, @"DataCenter\.NumberOfHostilesInRangeOf\(\s*RotationSolver\.Decisions\.AttackRangeGate\.Radius\(\s*DataCenter\.IsPvP\s*,\s*DataCenter\.Role\s*==\s*JobRole\.Melee\s*,\s*DataCenter\.JobRange\s*\)\s*\)\s*>\s*0"));
        var stillOld = Regex.Matches(ability, @"\bHasHostilesInRange\s*&&\s*(DataCenter\.CurrentDutyRotation\?\.)?AttackAbility\(").Count;
        Harness.Case("neither AttackAbility gate (duty rotation, job) is still gated by HasHostilesInRange", stillOld == 0, $"{stillOld} old gates");
        var gated = Regex.Matches(ability, @"hasHostilesForAttack\s*&&\s*(DataCenter\.CurrentDutyRotation\?\.)?AttackAbility\(").Count;
        Harness.Case("both AttackAbility gates (duty rotation, job) use the new radius count", gated == 2, $"{gated} gates");

        // DataCenter: JobRange is untouched, and NumberOfHostilesInRangeOf measures exactly what NumberOfHostilesInRange does.
        var dc = CsSource.Sanitize(File.ReadAllText(Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "DataCenter.cs")));
        var jobRange = Between(dc, "public static float JobRange", "SylphManagementFinished"); // comments are blanked by Sanitize, so cut at the next member
        Harness.Case("DataCenter.JobRange still returns 3 for Tank and Melee and 25 otherwise",
            Regex.IsMatch(jobRange, @"float radius = 25;") && Regex.IsMatch(jobRange, @"case JobRole\.Tank:\s*case JobRole\.Melee:\s*radius = 3;"), "JobRange body unchanged");
        var inRange = Between(dc, "public static int NumberOfHostilesInRange\n", "public static int NumberOfHostilesInMaxRange");
        if (inRange == "") inRange = Between(dc, "public static int NumberOfHostilesInRange\r\n", "public static int NumberOfHostilesInMaxRange");
        var inRangeOf = Between(dc, "public static int NumberOfHostilesInRangeOf(", "public static int NumberOfPartyMembersInRangeOf(");
        var measureA = Regex.Match(inRange, @"AllHostileTargets[\s\S]*?targets\[i\]\.DistanceToPlayer\(\)\s*<\s*jobRange");
        var measureB = Regex.Match(inRangeOf, @"AllHostileTargets[\s\S]*?targets\[i\]\.DistanceToPlayer\(\)\s*<\s*range");
        Harness.Case("NumberOfHostilesInRange and NumberOfHostilesInRangeOf both count AllHostileTargets with DistanceToPlayer() < radius",
            measureA.Success && measureB.Success && Regex.IsMatch(inRange, @"float jobRange = JobRange;"), $"{inRange.Length}/{inRangeOf.Length} chars read");

        // Canary: a gate that applies 5 to every PvP job (tanks too) must not satisfy the table.
        Harness.Canary("mutant gate that gives every PvP job 5 satisfies the table",
            Mismatches((pvp, melee, range) => pvp ? 5f : range).Length == 0);
    }
}
