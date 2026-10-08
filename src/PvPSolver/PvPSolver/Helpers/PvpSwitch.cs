using ECommons.GameHelpers;
using RotationSolver.Commands;
using RotationSolver.Decisions;

namespace RotationSolver.Helpers;

/// <summary>
///     The one zone-dependent control of the PvP settings window: turning PvP Solver on. Kept out of the window's own
///     files on purpose: those files must not read the zone or the loaded rotation, so that every setting stays editable
///     everywhere, and this class is the single place that does. It follows the Lazy Hub master toggle: on is refused
///     outside a PvP zone, off is allowed anywhere, and a state that did not change means nothing happened.
/// </summary>
internal static class PvpSwitch
{
    /// <summary>What the top strip of the window shows and allows right now.</summary>
    internal static SwitchView View => SwitchStatus.Describe(DataCenter.State, DataCenter.IsPvP, Player.Available);

    /// <summary>Turns PvP Solver on or off. Returns a plain sentence when it did not happen, otherwise null.</summary>
    internal static string? Set(bool on)
    {
        if (on == DataCenter.State)
        {
            return null;
        }

        if (on && !DataCenter.IsPvP)
        {
            return "PvP Solver only turns on in a PvP zone.";
        }

        RSCommands.DoStateCommandType(on ? StateCommandType.PvP : StateCommandType.Off);

        // DoStateCommandType returns silently when there is no player object (a zone change), and the state is set
        // before it returns, so a state that did not move means nothing happened.
        return DataCenter.State == on ? null : "Could not change PvP Solver right now.";
    }
}
