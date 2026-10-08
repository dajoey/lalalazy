namespace RotationSolver.Decisions;

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): which PvP actions may be used
///     when no hostile is within 40 yalms. Called from <c>BaseAction.CanUse</c>.
///     The ids are the PvP actions' own ids; the harness asserts each one equals the real
///     <c>(uint)ActionID.&lt;Name&gt;</c>.
/// </summary>
public static class PvpUtilityActionIds
{
    /// <summary>Guard (PvP).</summary>
    public const uint GuardPvP = 29054;

    /// <summary>Standard-issue Elixir (PvP).</summary>
    public const uint StandardissueElixirPvP = 29055;

    /// <summary>Purify (PvP).</summary>
    public const uint PurifyPvP = 29056;

    /// <summary>Sprint (PvP).</summary>
    public const uint SprintPvP = 29057;

    /// <summary>Recuperate (PvP).</summary>
    public const uint RecuperatePvP = 29711;

    /// <summary>True for exactly the five utility actions that stay usable with no enemy within 40 yalms.</summary>
    public static bool IsAllowedWithoutHostiles(uint id) =>
        id == GuardPvP
        || id == StandardissueElixirPvP
        || id == PurifyPvP
        || id == SprintPvP
        || id == RecuperatePvP;
}
