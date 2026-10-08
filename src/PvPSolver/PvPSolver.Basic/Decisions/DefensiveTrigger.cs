namespace RotationSolver.Decisions;

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): the HP trigger of the generic
///     PvP defensives (the per-job "use the defensive when own HP is below X%" table).
/// </summary>
public static class DefensiveTrigger
{
    /// <summary>
    ///     False whenever the setting is off, whatever the HP. When on, true only while the health ratio is
    ///     strictly below the threshold: a ratio equal to the threshold, a NaN ratio and a NaN threshold never
    ///     trigger it.
    /// </summary>
    public static bool ShouldUse(bool enabled, float healthRatio, float threshold) =>
        enabled && healthRatio < threshold;
}
