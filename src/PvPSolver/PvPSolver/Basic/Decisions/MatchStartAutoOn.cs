namespace RotationSolver.Decisions;

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): whether the
///     "Turn on when a match starts" setting should latch or engage the PvP state.
///     The shipped 0.1.2.26-0.1.2.32 shape engaged during the match loading screen itself, when
///     the player object does not exist and the job role is unknown, so every attempt silently
///     no-oped and the rotation loaded but never acted in a match. The core makes the two-step
///     shape explicit: latch while the loading screen is up, engage on the first cycle that has
///     a loaded player with a known job role.
/// </summary>
public static class MatchStartAutoOn
{
    /// <summary>
    ///     True during the match loading screen: the setting is on, the state is still off, the game is
    ///     between areas AND bound by duty (a match load, not walking into the PvP hub), and the
    ///     territory is PvP. The player does not exist yet, so the caller must only remember this.
    /// </summary>
    public static bool ShouldLatch(bool settingOn, bool stateOn, bool betweenAreas, bool boundByDuty, bool pvpTerritory)
        => settingOn && !stateOn && betweenAreas && boundByDuty && pvpTerritory;

    /// <summary>
    ///     True once a latched match load has landed and the player is loaded with a known job role
    ///     (engagement needs the role; without one the state command silently no-ops). The caller
    ///     engages the PvP state now and consumes the latch.
    /// </summary>
    public static bool ShouldEngage(bool settingOn, bool latched, bool stateOn, bool pvpTerritory, bool playerReady)
        => settingOn && latched && !stateOn && pvpTerritory && playerReady;

    /// <summary>True when a latched auto-on is stale (turned off, state already on, or the territory stopped being PvP) and must be dropped.</summary>
    public static bool ShouldDrop(bool settingOn, bool latched, bool stateOn, bool pvpTerritory)
        => latched && (!settingOn || stateOn || !pvpTerritory);
}
