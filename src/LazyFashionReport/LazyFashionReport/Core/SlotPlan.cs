namespace LazyFashionReport.Core;

/// <summary>Where an owned item physically sits (P3 get-to leg). "Owned" is not "in bags":
/// a piece in the glamour dresser or the armoire is owned and glamour-usable, but the player
/// has to go to a dresser to apply it - the UI must say which.</summary>
[Flags]
public enum ItemStorage
{
    None = 0,
    Bags = 1,       // bags or armoury chest (equippable directly)
    Dresser = 2,    // glamour dresser (apply at any dresser/prism)
    Armoire = 4,    // armoire (apply at a dresser)
    Equipped = 8,   // currently worn
}

/// <summary>Pure owned-items catalog: item id -> where it was found (P3). Built by the client
/// reader on the framework thread; consumed by the planner on any thread. An item found in
/// more than one place carries all the flags (a dresser copy of a bag item, say).</summary>
public sealed record OwnedCatalog
{
    private static readonly OwnedCatalog EmptyInstance = new() { ByItem = new Dictionary<uint, ItemStorage>() };
    /// <summary>Semantic empty (no reads done); not the same as "read found nothing".</summary>
    public static OwnedCatalog Empty => EmptyInstance;

    public required IReadOnlyDictionary<uint, ItemStorage> ByItem { get; init; }

    public bool Contains(uint itemId) => ByItem.ContainsKey(itemId);
    public ItemStorage StorageFor(uint itemId) => ByItem.GetValueOrDefault(itemId, ItemStorage.None);

    /// <summary>The id set the owned-filter and the fetch plan consume (every location counts).</summary>
    public HashSet<uint> Ids() => new(ByItem.Keys);

    /// <summary>Human suffix for the Wear list: "" when in bags (nothing to say), otherwise
    /// where the piece is. Bags beats dresser when both - the nearest action wins.</summary>
    public string LocationNote(uint itemId)
    {
        var s = StorageFor(itemId);
        if (s == ItemStorage.None) return "";
        if (s.HasFlag(ItemStorage.Bags) || s.HasFlag(ItemStorage.Equipped)) return "";
        if (s.HasFlag(ItemStorage.Dresser) && s.HasFlag(ItemStorage.Armoire)) return "(in dresser or armoire)";
        if (s.HasFlag(ItemStorage.Dresser)) return "(in glamour dresser)";
        if (s.HasFlag(ItemStorage.Armoire)) return "(in armoire)";
        return "";
    }
}


/// <summary>Where a missing piece can come from. Craftable = a Recipe-sheet entry exists;
/// NotCraftable = no known recipe (buy leg resolves those in v0.3.0.0+).</summary>
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
