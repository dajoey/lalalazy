namespace LazyFashionReport.Core;

/// <summary>
/// Pure predictor: turns a FashionWeek, the player's equipped items, crowd data and the
/// stain-family table into the per-slot + total scoring view the UI renders. Game-assembly-free;
/// the offline harness replays week 449 through this exact class.
/// </summary>
public static class Predictor
{
    /// <summary>
    /// Build the full outfit report. Missing optional inputs degrade, never throw:
    /// no week data -> empty outfit; no crowd data -> candidates empty, hints from the week.
    /// </summary>
    public static OutfitReport Build(
        FashionWeek week,
        IReadOnlyList<EquippedItem?> equipped,
        IReadOnlyDictionary<uint, string> stainFamilies,
        CrowdData? crowd,
        IReadOnlySet<uint>? ownedItems = null)
    {
        var slots = new List<SlotReport>(ScoreMath.TotalSlots);
        for (var i = 0; i < ScoreMath.TotalSlots; i++)
        {
            var slot = (FashionSlot)i;
            var eq = i < equipped.Count ? equipped[i] : null;
            var hinted = week.IsHinted(slot);

            string? hint = hinted ? week.Hints[i] : null;
            string? plus2 = week.PlusTwoDyes.TryGetValue(slot, out var p2) ? p2 : null;
            string? plus1 = week.PlusOneShades.TryGetValue(slot, out var p1) ? p1 : null;

            // Candidates: crowd-sourced gold items for this slot's hint, filtered to owned.
            var candidates = CrowdCandidates(week, slot, crowd, ownedItems);

            // Does the equipped item satisfy the hint? Highest-confidence first:
            // 1. the game itself already judged it (evaluation data is authoritative), 2. the
            // crowd voted it gold for this category, 3. nothing says yes.
            var satisfies = false;
            if (eq is not null && hinted)
            {
                if (crowd?.GoldIdsFor(week, slot) is { } golds)
                    satisfies = golds.Contains(eq.ItemId);
            }

            // Dye: left side only, and only when we know this week's preferred stain.
            DyeState? dye = null;
            uint? preferredStain = null;
            if (crowd?.PreferredStainFor(week, slot) is { } ps && ps != 0)
                preferredStain = ps;
            if (eq is not null && slot.IsLeftSide())
            {
                dye = ShadeMap.Evaluate(eq.Stain0Id, eq.Stain1Id, preferredStain, stainFamilies);
            }

            var score = eq is null
                ? 0
                : ScoreMath.SlotScore(slot.IsAccessory(), hinted, satisfies, dye);

            slots.Add(new SlotReport
            {
                Slot = slot,
                Hint = hint,
                PlusTwoDye = plus2,
                PlusOneShade = plus1,
                Equipped = eq,
                ItemSatisfiesHint = satisfies,
                Dye = dye,
                Score = score,
                Candidates = candidates,
            });
        }

        return new OutfitReport { Week = week, Slots = slots };
    }

    private static IReadOnlyList<CandidateItem> CrowdCandidates(
        FashionWeek week, FashionSlot slot, CrowdData? crowd, IReadOnlySet<uint>? owned)
    {
        if (crowd is null) return Array.Empty<CandidateItem>();
        var items = crowd.CandidatesFor(week, slot, owned);
        return items;
    }
}

/// <summary>
/// Crowd data view the predictor needs. Implemented by the host side from xivstats
/// (votes per item per category) + fashionreportxiv (exact dyes); faked by the harness.
/// </summary>
public interface CrowdData
{
    /// <summary>Top candidate items for a hinted slot, best-voted first, optionally owned-filtered.</summary>
    IReadOnlyList<CandidateItem> CandidatesFor(FashionWeek week, FashionSlot slot, IReadOnlySet<uint>? owned);

    /// <summary>Item ids the crowd has voted gold for this slot's hint (empty = unknown).</summary>
    IReadOnlySet<uint> GoldIdsFor(FashionWeek week, FashionSlot slot);

    /// <summary>This week's crowdsourced preferred stain id for a left-side slot (0/none = unknown).</summary>
    uint PreferredStainFor(FashionWeek week, FashionSlot slot);
}
