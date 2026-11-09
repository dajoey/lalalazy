namespace RotationSolver.Decisions;

/// <summary>
/// Real-HP read for the PvP defensive rules: the current HP out of the maximum, 0 when the
/// maximum is 0 and clamped to at most 1, so a wrong sheet value can never read as invulnerable.
/// Deliberately not PvP Solver's predicted HP, which counts heals still in flight.
/// </summary>
public static class RealHp
{
    /// <summary>Real HP as a ratio: current over max, 0 when max is 0. Never a predicted value with pending heals added.</summary>
    public static float HpRatio(uint now, uint max) => max == 0 ? 0f : Math.Clamp((float)now / max, 0f, 1f);
}
