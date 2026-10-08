using System.Numerics;
using System.Text.RegularExpressions;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 11, PLD-RANGE (PLD-3). T2 plus T1 for the extracted predicate: Guardian can only be executed on a
///     member closer than 10 yalms, centre to centre, while the action range is 20. The T1 part is the pure
///     <see cref="GuardianReach.IsWithinReach"/> over a distance table; the T2 part reads PaladinRotation.cs and
///     requires <c>ModifyGuardianPvP</c> to set a <c>CanTarget</c> that uses it with both centres.
/// </summary>
internal static class GuardianRange
{
    private static readonly (Vector3 Player, Vector3 Target, bool Expected, string Why)[] Table =
    [
        (new(0, 0, 0), new(0, 0, 0), true, "same spot"),
        (new(0, 0, 0), new(9.99f, 0, 0), true, "9.99 on x"),
        (new(0, 0, 0), new(10f, 0, 0), false, "exactly 10 is not closer than 10"),
        (new(0, 0, 0), new(10.01f, 0, 0), false, "10.01"),
        (new(0, 0, 0), new(6f, 0, 8f), false, "3-4-5 triangle, exactly 10"),
        (new(0, 0, 0), new(5.9f, 0, 7.9f), true, "9.86 diagonal"),
        (new(0, 0, 0), new(0, 9.9f, 0), true, "9.9 vertical"),
        (new(0, 0, 0), new(0, 10.5f, 0), false, "10.5 vertical"),
        (new(-3, 1, -4), new(-3, 1, 5.9f), true, "negative coordinates, 9.9"),
        (new(-3, 1, -4), new(-3, 1, 6.1f), false, "negative coordinates, 10.1"),
        (new(0, 0, 0), new(20f, 0, 0), false, "20 (the action range)"),
        (new(100, 0, 100), new(100, 0, 109.5f), true, "far from the origin, 9.5"),
    ];

    private static string[] Mismatches(Func<Vector3, Vector3, bool> impl) =>
        Table.Where(r => impl(r.Player, r.Target) != r.Expected || impl(r.Target, r.Player) != r.Expected).Select(r => r.Why).ToArray();

    public static void Run()
    {
        Console.WriteLine("-- change 11 PLD-RANGE (T2 + T1 for the predicate) --");

        var bad = Mismatches(GuardianReach.IsWithinReach);
        Harness.Case("GuardianReach.IsWithinReach: strictly closer than 10, centre to centre, symmetric, over the whole table",
            bad.Length == 0, bad.Length == 0 ? $"{Table.Length} rows" : "differs at: " + string.Join(", ", bad));

        // Red against the old behaviour: with no CanTarget filter every ally in the action's 20-yalm range was offered.
        var oldBad = Mismatches((p, t) => true);
        Harness.Case("RED on old behaviour: no filter offers every ally the table says is out of reach",
            oldBad.Length == Table.Count(r => !r.Expected), $"{oldBad.Length} out-of-reach rows offered");

        // T2: the base action setting carries the filter.
        var path = Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "Rotations", "Basic", "PaladinRotation.cs");
        var san = CsSource.Sanitize(File.ReadAllText(path));
        var head = new Regex(@"\bstatic\s+partial\s+void\s+(?<name>ModifyGuardianPvP)\s*\(");
        var m = CsSource.Methods(san, "PaladinRotation.cs", head).SingleOrDefault();
        Harness.Case("PaladinRotation has ModifyGuardianPvP", m != null);
        if (m == null) return;

        var body = CsSource.Squash(m.Body);
        Harness.Case("ModifyGuardianPvP sets CanTarget from GuardianReach.IsWithinReach(player centre, target centre)",
            Regex.IsMatch(body, @"setting\.CanTarget\s*=\s*t\s*=>\s*Player is \{ \} me && RotationSolver\.Decisions\.GuardianReach\.IsWithinReach\(me\.Position, t\.Position\);"), body);
        Harness.Case("ModifyGuardianPvP keeps Hallowed Ground need, LowHP target type and friendly flag",
            body.Contains("setting.StatusNeed = [StatusID.HallowedGround_1302];")
            && body.Contains("setting.TargetType = TargetType.LowHP;")
            && body.Contains("setting.IsFriendly = true;"), body);

        // Canary: an inclusive (<= 10) mutant must not satisfy the table.
        Harness.Canary("mutant predicate using <= 10 satisfies the table",
            Mismatches((p, t) => Vector3.Distance(p, t) <= 10f).Length == 0);
    }
}
