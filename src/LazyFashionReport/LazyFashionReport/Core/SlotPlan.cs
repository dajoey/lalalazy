namespace LazyFashionReport.Core;

/// <summary>Where a missing piece can come from. Craftable = a Recipe-sheet entry exists;
/// NotCraftable = no known recipe yet (the shop/market leg lands in a later update, so the
/// honest label is "not craftable", never silence).</summary>
public enum PieceSource
{
    Craftable,
    NotCraftable,
}

/// <summary>One not-owned crowd candidate with its resolved source.</summary>
public sealed record FetchPiece
{
    public required CandidateItem Item { get; init; }
    public RecipeOption? Recipe { get; init; }
    public PieceSource Source => Recipe is null ? PieceSource.NotCraftable : PieceSource.Craftable;
    /// <summary>Resolved buy source (v0.3.0.0 buy leg); null until the service resolves it.</summary>
    public BuyOption? Buy { get; set; }
}

/// <summary>
/// The flat per-slot view the report window renders: what to WEAR (owned candidates, best
/// first) and what to FETCH (not-owned candidates, each with a source). Built for every
/// hinted slot, even when a list is empty — an empty list is information, not a reason to
/// hide the slot.
/// </summary>
public sealed record SlotPlan
{
    public required FashionSlot Slot { get; init; }
    public required string Hint { get; init; }
    /// <summary>Owned (or all, when the owned filter is off) candidates for this slot, best-voted first.</summary>
    public IReadOnlyList<CandidateItem> Wear { get; init; } = Array.Empty<CandidateItem>();
    /// <summary>True when Wear was filtered to owned items; false = the full crowd list
    /// (either the filter is off, or ownership could not be read — the UI must label it).</summary>
    public bool WearIsOwnedFiltered { get; init; }
    /// <summary>Not-owned candidates with source resolution. Empty when ownership is unknown.</summary>
    public IReadOnlyList<FetchPiece> Fetch { get; init; } = Array.Empty<FetchPiece>();
    /// <summary>True when the owned snapshot was unavailable, so "missing" could not be judged.</summary>
    public bool OwnershipUnknown { get; init; }
}

/// <summary>
/// Pure composer of the per-slot plan view (UI unhide, v0.2.0.0). Replaces the old collapsed
/// candidate headers + the craft-only missing list that silently dropped Recipe==null rows:
/// every hinted slot yields a SlotPlan, every missing candidate yields a FetchPiece with a
/// Source, and nothing is filtered out for being uncraftable. Offline-harness-tested.
/// </summary>
public static class SlotPlanner
{
    /// <param name="week">Current week (hints).</param>
    /// <param name="crowd">Crowd data; null yields empty plans for the hinted slots.</param>
    /// <param name="owned">Owned snapshot; null (unknown) yields empty Fetch — "missing"
    /// cannot be judged without ownership, but Wear degrades to the unfiltered list.</param>
    /// <param name="recipeFor">Item id to recipe resolver; null result = not craftable.</param>
    /// <param name="wearFilterOwned">True to keep only owned items in Wear (the config filter).</param>
    /// <param name="maxWear">Cap on Wear entries.</param>
    /// <param name="maxFetch">Cap on Fetch entries.</param>
    public static IReadOnlyList<SlotPlan> Compose(
        FashionWeek week,
        CrowdData? crowd,
        IReadOnlySet<uint>? owned,
        Func<uint, RecipeOption?>? recipeFor,
        bool wearFilterOwned,
        int maxWear = 8,
        int maxFetch = 3)
    {
        var plans = new List<SlotPlan>();
        if (crowd is null) return plans;

        foreach (var slot in Enum.GetValues<FashionSlot>())
        {
            if (!week.IsHinted(slot)) continue;
            var hint = week.Hints[(int)slot] ?? "";

            var wear = crowd.CandidatesFor(week, slot, wearFilterOwned ? owned : null)
                .Take(maxWear)
                .ToList();

            var fetch = new List<FetchPiece>();
            if (owned is not null)
            {
                foreach (var c in crowd.CandidatesFor(week, slot, null))
                {
                    if (owned.Contains(c.ItemId)) continue;
                    fetch.Add(new FetchPiece
                    {
                        Item = c,
                        Recipe = recipeFor?.Invoke(c.ItemId),
                    });
                    if (fetch.Count >= maxFetch) break;
                }
            }

            plans.Add(new SlotPlan
            {
                Slot = slot,
                Hint = hint,
                Wear = wear,
                WearIsOwnedFiltered = wearFilterOwned && owned is not null,
                Fetch = fetch,
                OwnershipUnknown = owned is null,
            });
        }
        return plans;
    }
}
