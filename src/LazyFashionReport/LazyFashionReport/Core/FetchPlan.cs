namespace LazyFashionReport.Core;

/// <summary>One recipe that produces an item, resolved from the game's Recipe sheet.</summary>
/// <param name="RecipeId">Recipe sheet RowId - the value Artisan.CraftItem takes.</param>
/// <param name="CraftTypeId">CraftType row (0..7 = CRP..CUL); display only.</param>
/// <param name="Level">RecipeLevelTable ClassJobLevel of the recipe.</param>
public sealed record RecipeOption(uint RecipeId, int CraftTypeId, short Level);

/// <summary>A crowd-suggested piece for a hinted slot that the player does not own, plus its
/// recipe when one exists. The UI turns this into a "craft via Artisan" action.</summary>
public sealed record MissingPiece
{
    public required FashionSlot Slot { get; init; }
    public required string Hint { get; init; }
    public required CandidateItem Item { get; init; }
    public RecipeOption? Recipe { get; init; }
}

/// <summary>
/// Pure fetch-missing planner (auto-dress spec v1 step 4, kept out of the Dalamud surface so the
/// offline harness can replay it): per hinted slot, the best-voted crowd candidate the player
/// does NOT own, with its recipe when one exists. Never throws; a missing crowd dataset or an
/// empty owned snapshot yields an empty plan.
/// </summary>
public static class FetchPlan
{
    /// <param name="week">The current week (hints).</param>
    /// <param name="crowd">Crowd candidates; asked WITHOUT the owned filter on purpose - the plan
    /// is exactly the part of that list the player lacks.</param>
    /// <param name="owned">The owned-items snapshot; null disables the plan entirely, because
    /// "missing" cannot be judged without knowing what is owned.</param>
    /// <param name="recipeFor">Item id to recipe resolver (game-side); null result = not craftable.</param>
    /// <param name="maxPerSlot">How many missing candidates to keep per slot.</param>
    public static IReadOnlyList<MissingPiece> Build(
        FashionWeek week,
        CrowdData? crowd,
        IReadOnlySet<uint>? owned,
        Func<uint, RecipeOption?> recipeFor,
        int maxPerSlot = 3)
    {
        if (crowd is null || owned is null) return Array.Empty<MissingPiece>();

        var plan = new List<MissingPiece>();
        foreach (var slot in Enum.GetValues<FashionSlot>())
        {
            if (!week.IsHinted(slot)) continue;
            var hint = week.Hints[(int)slot] ?? "";
            var missing = 0;
            foreach (var c in crowd.CandidatesFor(week, slot, null))
            {
                if (owned.Contains(c.ItemId)) continue;
                plan.Add(new MissingPiece
                {
                    Slot = slot,
                    Hint = hint,
                    Item = c,
                    Recipe = recipeFor?.Invoke(c.ItemId),
                });
                if (++missing >= maxPerSlot) break;
            }
        }
        return plan;
    }
}
