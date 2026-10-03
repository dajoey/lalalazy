using Lalalazy.Crucible;

namespace LazyCrucible.Policy;

/// <summary> One need-coverage correction: the rows after the fix, the delta ("<c>+11-49</c>") and why anything stayed uncovered. </summary>
public readonly record struct AutoDutyFix(List<int> Rows, string Delta, List<string> Miss);

/// <summary>
///     STUB (failing-first for the 2026-10-03 fix round). The real planner minimally corrects another driver's
///     (AutoDuty's) familiar picks so the fight's ability needs stay covered; this stub changes nothing, which is
///     exactly the live behaviour being fixed.
/// </summary>
internal static class AutoDutyNeedFix
{
    public static AutoDutyFix Horn(int board, int battle, IReadOnlyList<int> driverRows, IReadOnlyList<int> rosterRows, IReadOnlyDictionary<int, int> hpByRow) =>
        new([.. driverRows], "", []);

    public static AutoDutyFix Roster(int board, IReadOnlyList<int> driverRows, IReadOnlyList<int> capturedRows, IReadOnlyDictionary<int, int> hpByRow, int cap) =>
        new([.. driverRows], "", []);
}
