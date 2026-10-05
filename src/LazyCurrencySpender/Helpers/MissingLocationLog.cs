namespace CurrencySpender.Helpers;

/// <summary>What the shop-NPC "no map location" notice should be logged as.</summary>
internal enum MissingLocationLogLevel
{
    /// <summary>Already reported for this NPC; write nothing.</summary>
    None,
    /// <summary>First sighting of this NPC; expected data gap, Debug only.</summary>
    Debug,
    /// <summary>Error level.</summary>
    Error,
}

/// <summary>
///     Log policy for shop NPCs that have no entry in <c>Location.locations</c>. The check runs from the
///     draw loop (once per item per frame), so it must be quiet after the first sighting.
/// </summary>
internal sealed class MissingLocationLog
{
    private readonly HashSet<uint> reported = [];

    public MissingLocationLogLevel Decide(uint npcId) =>
        reported.Add(npcId) ? MissingLocationLogLevel.Debug : MissingLocationLogLevel.None;
}
