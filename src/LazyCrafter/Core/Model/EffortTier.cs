namespace LazyCrafter.Core.Model;

/// <summary>
/// Effort buckets from Scope §3.2. A recipe's tier is the max over its missing leaves.
/// </summary>
public enum EffortTier
{
    /// <summary>Everything on hand.</summary>
    Now = 0,
    /// <summary>Sub-craft from on-hand, gil vendor, regular node, or retainer venture.</summary>
    Easy = 1,
    /// <summary>Timed/unspoiled node, fishing, market board, special shop.</summary>
    SomeEffort = 2,
    /// <summary>Monster drop, voyage, dungeon, other.</summary>
    RealEffort = 3,
    /// <summary>Untradeable and unsourced - a blocker.</summary>
    Blocked = int.MaxValue,
}
