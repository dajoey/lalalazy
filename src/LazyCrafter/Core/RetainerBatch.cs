namespace LazyCrafter.Core;

/// <summary>
/// The cart-side half of the batch retainer fetch (0.1.3.0). Artisan's list-shaped
/// <c>RestockFromRetainers(NewCraftingList)</c> withdraws a whole list of recipes' materials in ONE bell session
/// (bell once, then per retainer x per required item, quantities recomputed as list demand minus what the bags
/// hold at session time). LazyCrafter feeds it recipe rows - which rows is a pure decision, so the Harness runs
/// it against the fake world and the adapter only executes the result.
/// </summary>
public static class RetainerBatch
{
    /// <summary>
    /// Recipe rows for the batch session: every queued craft's row, plus deferred crafts whose blocker list
    /// includes a retrieval - those exist exactly because their stock sat on a retainer, so fetching the stock
    /// un-defers them on the post-fetch re-plan. A deferral blocked by something else too (a venture, a gather, a
    /// purchase) still goes in: the batch withdraws whatever retainer stock it can, and the re-plan keeps the craft
    /// deferred for the remaining reason. Rows are distinct, craft order first. <paramref name="rowExists"/> is the
    /// recipe-graph lookup (Artisan throws on an unknown row id, so unknown rows are filtered out); pass null to
    /// skip the filter.
    /// </summary>
    public static IReadOnlyList<uint> Queue(IReadOnlyList<DispatchPlan.Craft> crafts, IReadOnlyList<DispatchPlan.Deferral> deferred, Func<uint, bool>? rowExists = null)
    {
        var rows = new List<uint>();
        rows.AddRange(crafts.Select(c => c.RecipeId));
        rows.AddRange(deferred
            .Where(d => d.Reason.Contains("retrieve #"))
            .Select(d => d.RecipeId));
        if (rowExists is not null) rows.RemoveAll(r => !rowExists(r));
        return rows.Distinct().ToList();
    }
}
