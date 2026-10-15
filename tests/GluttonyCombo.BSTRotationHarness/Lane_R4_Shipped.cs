namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4, regression suite for the four shipped Crucible fixes (task tasks-20261015-crucible-r4-regression-shipped-fixes-01). Titles start with
///     "r4-shipped:" and name the fix (abaddon, dispel, dash, hourglass) or the residual row (R1, R4, R5, R7, R8, R10, R11, kd-05b, sv-03).
///     The four fix files (Lane_R4_Shipped_Abaddon / _Dispel / _Dash / _Hourglass.cs) compile and run unchanged on the build BEFORE each fix:
///     whatever a fix added to a type is reached by reflection (Lane_R4_Shipped_Support.cs), so the failing-first run fails at runtime, on the
///     assert, with no stub in the source under test. The residual file needs the extractions of this round and runs on the integration head only.
/// </summary>
internal static partial class Program
{
    private static void R4ShippedLane20261015()
    {
        R4ShippedAbaddon();
        R4ShippedDispel();
        R4ShippedDash();
        R4ShippedHourglass();
        R4ShippedResidual();
    }
}
