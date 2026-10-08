using System.Numerics;

namespace RotationSolver.Decisions;

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): Paladin Guardian can only be
///     executed on a member closer than 10 yalms (centre to centre).
/// </summary>
public static class GuardianReach
{
    /// <summary>Guardian's stated reach, in yalms.</summary>
    public const float Yalms = 10f;

    /// <summary>True when the two centres are strictly closer than <see cref="Yalms"/>.</summary>
    public static bool IsWithinReach(Vector3 playerCentre, Vector3 targetCentre) =>
        Vector3.Distance(playerCentre, targetCentre) < Yalms;
}
