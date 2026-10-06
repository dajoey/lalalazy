using Lalalazy.Crucible;

namespace LazyCrucible.Policy;

/// <summary>
///     The Crucible board's degree as the board layout last set it. Pure (no Dalamud): <c>AgentProbe</c> feeds it every event the
///     XBMStageDetailList agent receives, and <c>Plugin</c> publishes <see cref="Value"/> to GluttonyCombo over Dalamud IPC
///     (<see cref="CrucibleDegree.IpcName"/>). See <see cref="CrucibleDegree"/> for the event and what it was measured against.
///     <para>
///         The value is the LAST degree set since the plugin loaded and stays until the next set-degree event: the layout keeps its selection
///         between runs, so an AutoDuty loop (which sets it once per queue) and a player's own click both land here. Before the first event the
///         value is <see cref="CrucibleDegree.Unknown"/>, and the consumer reads that conservatively.
///     </para>
/// </summary>
internal sealed class DegreeLatch
{
    /// <summary> 0-3 once seen, <see cref="CrucibleDegree.Unknown"/> before. </summary>
    public int Value { get; private set; } = CrucibleDegree.Unknown;

    /// <summary> Feed one event of the board-layout agent. True when it changed the latched degree. </summary>
    public bool Note(ulong eventKind, uint valueCount, int? first, int? second) => false;

    /// <summary> Forget the degree (plugin job change, unload). </summary>
    public void Reset() => Value = CrucibleDegree.Unknown;
}
