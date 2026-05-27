namespace LazyFATEAutomator;

/// <summary>
/// Sort criteria for prioritising FATEs. Apply in order — the first criterion is the primary
/// sort key, subsequent criteria are tiebreakers.
/// </summary>
public enum FateSortCriteria
{
    /// <summary>Bonus FATEs ranked higher when player has the Twist of Fate buff (so you keep stacking it).</summary>
    HasBonusWithTwist = 0,
    /// <summary>FATEs further along get priority — clear them out fast.</summary>
    Progress = 1,
    /// <summary>FATEs with the inherent gold-frame bonus icon.</summary>
    HasBonus = 2,
    /// <summary>FATEs about to expire (within MinTimeToPrioritise) rank higher.</summary>
    TimeRemainingUrgent = 3,
    /// <summary>Closer to the player (3D distance).</summary>
    Distance = 4,
    /// <summary>Lower FATE level first.</summary>
    Level = 5,
    /// <summary>Alphabetical.</summary>
    Name = 6,
    /// <summary>Raw remaining time (smaller first — useful as a tiebreaker after TimeRemainingUrgent).</summary>
    TimeRemaining = 7,
}

/// <summary>One row in the configured sort order. Ascending vs Descending.</summary>
[System.Serializable]
public sealed class FateSortRule
{
    public FateSortCriteria Criteria { get; set; }
    public bool Descending { get; set; }
}
