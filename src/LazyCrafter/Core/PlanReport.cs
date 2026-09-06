using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>
/// The <b>rendered</b> shopping lines of a <see cref="DispatchPlan.Plan"/> - market, currency shop, manual - and
/// the plan's contribution to a run's <see cref="BlockedItem"/> list.
/// <para>
/// <b>Why this class exists (card t_b431de3a).</b> These lines used to be built inline in
/// <c>DispatchService</c> and <c>LifestreamDispatch</c>, i.e. behind Dalamud, where the offline harness could not
/// reach them. That is exactly the shape of the last two defects on this thread: both were RENDERER bugs that an
/// internal-value test stayed green through, because the detail existed in memory and was dropped on the way to
/// the player (<see cref="BlockedListings"/> documents the same lesson for the retainer summary). Putting the
/// text here means a check can assert the sentence Joey actually reads, and there is exactly one implementation
/// of it rather than one per ending path.
/// </para>
/// <para>
/// Pure Core: no Dalamud, no Lumina, no game state. The adapters pass in their <c>Name</c> / <c>UnitCost</c>
/// lookups and print what comes back.
/// </para>
/// </summary>
public static class PlanReport
{
    /// <summary>"Titanium Ore x15 (~187,500)" plus, when known, " - or Ixali vendor (North Shroud) for 7 Ixali Oaknot".</summary>
    public static string MarketItem(DispatchPlan.Purchase p, Func<uint, string> name, Func<uint, long?> unitCost)
    {
        var also = string.IsNullOrEmpty(p.Where) ? "" : $" - {p.Where}";
        return unitCost(p.ItemId) is { } u
            ? $"{name(p.ItemId)} x{p.Quantity} (~{u * p.Quantity:N0}){also}"
            : $"{name(p.ItemId)} x{p.Quantity}{also}";
    }

    /// <summary>
    /// The whole market shopping list as one chat line, or <c>null</c> when there is nothing to buy.
    /// <c>&gt;</c> before the estimate means at least one item had no price, so the total is a lower bound.
    /// </summary>
    public static string? MarketLine(DispatchPlan.Plan plan, Func<uint, string> name, Func<uint, long?> unitCost)
    {
        if (plan.Market.Count == 0) return null;
        long total = 0;
        var complete = true;
        var parts = new List<string>(plan.Market.Count);
        foreach (var p in plan.Market)
        {
            if (unitCost(p.ItemId) is { } u) total += u * p.Quantity;
            else complete = false;
            parts.Add(MarketItem(p, name, unitCost));
        }
        return $"Market board list ({plan.Market.Count} item{(plan.Market.Count == 1 ? "" : "s")}, est. {(complete ? "" : ">")}{total:N0} gil): {string.Join(", ", parts)}";
    }

    /// <summary>
    /// One line per currency-shop stop: what to trade for, from whom, where, and at what price.
    /// Empty when the plan has no currency-shop items - a plan built without a
    /// <see cref="SpecialShopContext"/> always does, which is what keeps older behaviour untouched.
    /// </summary>
    public static IReadOnlyList<string> CurrencyLines(DispatchPlan.Plan plan, Func<uint, string> name) =>
        plan.CurrencyShop
            .Select(c => $"trade for {name(c.ItemId)} x{c.Quantity} at {c.Where}")
            .ToList();

    /// <summary>
    /// The manual line. Keeps the historical parenthesised <see cref="SourceKind"/> list (that is what the UI has
    /// always shown) and appends the real vendor names when a currency shop was located for the item.
    /// </summary>
    public static string? ManualLine(DispatchPlan.Plan plan, Func<uint, string> name)
    {
        if (plan.Manual.Count == 0) return null;
        var parts = plan.Manual.Select(m =>
        {
            var kinds = string.Join("/", m.Sources.Where(s => s != SourceKind.OnHand).Select(s => s.ToString()));
            var also = string.IsNullOrEmpty(m.Where) ? "" : $" - {m.Where}";
            return $"{name(m.ItemId)} x{m.Quantity} ({kinds}){also}";
        });
        return "needs a manual source: " + string.Join(", ", parts);
    }

    /// <summary>
    /// The plan's own blocked items for the run snapshot: market (with the gil estimate and any named currency
    /// vendor), currency shop, manual, ventures and retrievals.
    /// <para>
    /// Gil vendors are deliberately NOT here: they need the adapter's placement grouping, and resolving them a
    /// second time is precisely the drift card t_731ea0e7 removed. The caller appends those itself.
    /// </para>
    /// </summary>
    public static IReadOnlyList<BlockedItem> BlockedFrom(
        DispatchPlan.Plan plan, Func<uint, string> name, Func<uint, long?> unitCost)
    {
        var list = new List<BlockedItem>();
        foreach (var m in plan.Market)
            list.Add(new BlockedItem(StepKind.Market, m.ItemId, name(m.ItemId), m.Quantity,
                unitCost(m.ItemId) is { } u ? u * m.Quantity : null,
                string.IsNullOrEmpty(m.Where) ? null : m.Where));
        foreach (var c in plan.CurrencyShop)
            list.Add(new BlockedItem(StepKind.CurrencyShop, c.ItemId, name(c.ItemId), c.Quantity, null, c.Where));
        foreach (var m in plan.Manual)
            list.Add(new BlockedItem(StepKind.Manual, m.ItemId, name(m.ItemId), m.Quantity, null,
                string.IsNullOrEmpty(m.Where) ? string.Join("/", m.Sources.Where(s => s != SourceKind.OnHand)) : m.Where));
        return list;
    }
}
