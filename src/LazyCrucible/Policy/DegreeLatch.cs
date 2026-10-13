using Lalalazy.Crucible;

namespace LazyCrucible.Policy;

/// <summary>
///     The Crucible board's degree as the board layout last set it. Pure (no Dalamud): <c>AgentProbe</c> feeds it every event the
///     XBMStageDetailList agent receives, and <c>Plugin</c> publishes <see cref="Value"/> to GluttonyCombo over Dalamud IPC
///     (<see cref="CrucibleDegree.IpcName"/>). See <see cref="CrucibleDegree"/> for the event and what it was measured against.
///     <para>
///         The value is the LAST degree set since the plugin loaded (or the character logged in) and stays until the next set-degree event:
///         the layout keeps its selection between runs, and since patch 7.57 the game keeps it until logout, so an AutoDuty loop (which sets
///         it once per queue) and a player's own click both land here. Before the first event the value is
///         <see cref="CrucibleDegree.Unknown"/>, and the consumer reads that conservatively: nothing assumes Standard.
///     </para>
/// </summary>
internal sealed class DegreeLatch
{
    /// <summary> 0-3 once seen, <see cref="CrucibleDegree.Unknown"/> before. </summary>
    public int Value { get; private set; } = CrucibleDegree.Unknown;

    /// <summary> Feed one event of the board-layout agent. True when it changed the latched degree. </summary>
    public bool Note(ulong eventKind, uint valueCount, int? first, int? second)
    {
        var degree = CrucibleDegree.FromStageDetailEvent(eventKind, valueCount, first, second);
        if (degree == CrucibleDegree.Unknown || degree == Value)
            return false;
        Value = degree;
        return true;
    }

    /// <summary>
    ///     The hooks come down, and the degree survives it either way. With no player at all this is a loading screen (the character is
    ///     gone for the zone change): AutoDuty sets the degree in the lobby and the queue then zones into the duty, so forgetting it here
    ///     left every fight reading unknown. With the player present it is a job change: since patch 7.57 the game itself remembers the
    ///     Crucible Mode until logout, and only a Beastmaster can open the board layout to change it, so the degree last set is still the
    ///     game's when the character is a Beastmaster again. Only <see cref="LoggedOut"/> and an unload forget it.
    /// </summary>
    public void HooksDown(bool playerAvailable)
    {
    }

    /// <summary> The character logged out: the game's Crucible Mode returns to its default, so the latch forgets. </summary>
    public void LoggedOut() => Reset();

    /// <summary> Forget the degree (logout, unload). </summary>
    public void Reset() => Value = CrucibleDegree.Unknown;
}
