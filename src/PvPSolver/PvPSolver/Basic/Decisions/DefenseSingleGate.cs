namespace RotationSolver.Decisions;

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): whether the DefenseSingle stage
///     of <c>CustomRotation.Ability</c> ends the ability pass.
/// </summary>
public static class DefenseSingleGate
{
    /// <summary>
    ///     The stage ends when a defensive ability was found. Outside PvP it also ends, with no action, while no
    ///     hostile is casting at the tank and the tank has neither Vengeance nor Damnation (the PvE behaviour,
    ///     unchanged). In PvP that tail never applies: ending the stage with no action would stop every later
    ///     ability (Attack, General) for as long as the stage is merged.
    /// </summary>
    public static bool EndsStage(bool abilityFound, bool isPvP, bool hostileCastingToTank, bool hasVengeance, bool hasDamnation) =>
        abilityFound || (!isPvP && !hostileCastingToTank && !hasVengeance && !hasDamnation);
}
