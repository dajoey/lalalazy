using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>
/// Turns the dispatcher's raw "could not get this into the bags" list into the one actionable instruction Joey
/// asked for: <b>which retainer, which item, how many units to pull off sale</b> (card t_35be7be5, Tier 1).
/// <para>
/// Pure Core so the offline harness can assert on the RENDERED text, which is the whole point - the detail already
/// existed in memory before this class and was thrown away by the renderer, so a test that asserts on an internal
/// value would not have caught the defect it is here to prevent.
/// </para>
///
/// <para><b>The three defects this fixes, from the 2026-09-05 22:44 run (t_e31ccdfb):</b></para>
/// <list type="bullet">
/// <item>
/// <b>A - the finishing path threw the detail away.</b> <c>DispatchService._unfetched</c> is written at six sites
/// and was read at two that disagreed: <c>FinishBlocked</c> rendered retainer names, <c>Finish</c> rendered only
/// <c>", N could not be retrieved"</c>. Joey's run FINISHED, so he got a bare count while the retainer names, item
/// ids and quantities sat in memory. Both paths now call <see cref="Summarise"/> and print <see cref="Lines"/>.
/// </item>
/// <item>
/// <b>B - six causes were mixed, only one means "go unlist something".</b> The discriminator is the DATA, not the
/// call site: an entry is listing-blocked only when every place holding the stock is
/// <see cref="StoredElsewhere.Fetchable"/> <c>== false</c>, which <c>AllaganInventory.SplitListings</c> is the
/// only producer of. An item that was merely slow (a session timeout, a Begin error) still has a reachable place,
/// so it can never appear in the "pull these off sale" instruction - it is reported separately by
/// <see cref="Summary.Others"/> with its own reason.
/// </item>
/// <item>
/// <b>C - the list was not de-duplicated.</b> <c>WaitRetrieve</c> re-adds an item with the remaining quantity after
/// a partial pull, so one item can appear twice. Entries are folded per item.
/// <para>
/// <b>The folded figure is the MAXIMUM recorded need, not the sum.</b> The card asked for "the combined figure";
/// summing is the wrong arithmetic for this data and would overstate it. A second entry for the same item is a
/// <i>remainder of</i> the first (WaitRetrieve records <c>Quantity = left</c>, a subset of the entry it replaces),
/// or the same unchanged need re-recorded on a later wave (<c>_unfetched</c> is not cleared between waves). In both
/// cases the outstanding need is the larger of the two, never their total. The per-ITEM need is then spread across
/// the retainers actually holding listings, so the sum across a retainer grouping is still the item's real total -
/// which is the "combined" figure the card wanted, arrived at without double-counting.
/// </para>
/// </item>
/// </list>
///
/// <para>
/// <b>Accepted limitation (decided before the build, do not "fix" it here):</b> the AllaganTools bridge
/// (<c>ItemCount</c> / <c>ItemCountOwned</c>) has no HQ dimension, so the advice reads "pull 7 Silver Ore from
/// Hussypants" and cannot say "pull the HQ ones".
/// </para>
/// </summary>
public static class BlockedListings
{
    /// <summary>One item to pull off sale, and how many units of it.</summary>
    public sealed record Pull(uint ItemId, string Name, int Units)
    {
        public override string ToString() => $"{Name} x{Units}";
    }

    /// <summary>Everything to pull off one retainer, so a single summoning-bell visit clears the whole group.</summary>
    public sealed record RetainerPull(string Retainer, IReadOnlyList<Pull> Items)
    {
        public int TotalUnits => Items.Sum(i => i.Units);
        public string ItemList => string.Join(", ", Items.Select(i => i.ToString()));
    }

    /// <summary>A material that could not be fetched for a reason that is NOT a market listing - reported apart, never as "unlist this".</summary>
    public sealed record OtherBlocked(uint ItemId, string Name, int Units, string Why, string Places);

    /// <summary>The end-of-run answer, ready to render on either ending path.</summary>
    /// <param name="Retainers">Listing-blocked materials, grouped by the retainer whose listing is holding them.</param>
    /// <param name="Others">Everything else that could not be retrieved, with its own reason.</param>
    public sealed record Summary(IReadOnlyList<RetainerPull> Retainers, IReadOnlyList<OtherBlocked> Others)
    {
        public static Summary Empty { get; } = new(Array.Empty<RetainerPull>(), Array.Empty<OtherBlocked>());

        /// <summary>The only condition that may trigger the bell walk: there is something to go and unlist.</summary>
        public bool HasListings => Retainers.Count > 0;

        public bool IsEmpty => Retainers.Count == 0 && Others.Count == 0;

        /// <summary>Distinct listing-blocked materials (an item split across two retainers counts once).</summary>
        public int ItemCount => Retainers.SelectMany(r => r.Items).Select(i => i.ItemId).Distinct().Count();

        public int TotalUnits => Retainers.Sum(r => r.TotalUnits);
    }

    /// <summary>
    /// Fold the dispatcher's <c>_unfetched</c> list into the summary. <paramref name="name"/> resolves item ids
    /// (the caller's <c>Name</c>, so an unknown id still renders as <c>#id</c> rather than throwing).
    /// </summary>
    public static Summary Summarise(
        IEnumerable<(DispatchPlan.Retrieve Item, string Why)> unfetched,
        Func<uint, string> name)
    {
        // ---- Defect C: fold per item. Keep the entry carrying the LARGEST outstanding need (see the class
        // remarks for why max, not sum) and use that entry's places and reason.
        var folded = new Dictionary<uint, (DispatchPlan.Retrieve Item, string Why)>();
        var order = new List<uint>();
        foreach (var (item, why) in unfetched)
        {
            if (item is null) continue;
            if (!folded.TryGetValue(item.ItemId, out var kept))
            {
                folded[item.ItemId] = (item, why);
                order.Add(item.ItemId);
                continue;
            }
            if (item.Quantity > kept.Item.Quantity) folded[item.ItemId] = (item, why);
        }

        // ---- Defect B: split on the DATA. Listing-blocked = every place holding it is unreachable.
        var pullsByRetainer = new Dictionary<string, List<Pull>>(StringComparer.Ordinal);
        var retainerOrder = new List<string>();
        var others = new List<OtherBlocked>();

        foreach (var id in order)
        {
            var (item, why) = folded[id];
            if (!IsListingBlocked(item))
            {
                others.Add(new OtherBlocked(id, name(id), item.Quantity, why, item.Places));
                continue;
            }

            // Spread this item's need over the retainers actually holding listings, largest stack first, so one
            // bell visit covers one retainer and the quantities still add up to the item's real total.
            var left = Math.Max(0, item.Quantity);
            foreach (var place in item.Where.Where(w => !w.Fetchable && w.Quantity > 0).OrderByDescending(w => w.Quantity))
            {
                if (left <= 0) break;
                var take = Math.Min(left, place.Quantity);
                left -= take;
                var owner = place.Owner;
                if (!pullsByRetainer.TryGetValue(owner, out var list))
                {
                    pullsByRetainer[owner] = list = new List<Pull>();
                    retainerOrder.Add(owner);
                }
                var existing = list.FindIndex(p => p.ItemId == id);
                if (existing >= 0) list[existing] = list[existing] with { Units = list[existing].Units + take };
                else list.Add(new Pull(id, name(id), take));
            }
            // The listings are clipped to the shortfall by DispatchPlan.PlacesFor, so `left` is normally 0. If the
            // places somehow hold less than the need, the remainder is still the player's problem - say so rather
            // than silently dropping units.
            if (left > 0)
                others.Add(new OtherBlocked(id, name(id), left, "more is needed than is listed for sale - the rest is not anywhere the plugin can see", item.Places));
        }

        var retainers = retainerOrder
            .Select(r => new RetainerPull(r, pullsByRetainer[r]))
            .Where(r => r.Items.Count > 0)
            .ToList();

        return retainers.Count == 0 && others.Count == 0
            ? Summary.Empty
            : new Summary(retainers, others);
    }

    /// <summary>
    /// The Defect B discriminator, on its own so a test can pin it: the board listing is the ONLY place this item
    /// exists. A retrieval with no places at all is NOT listing-blocked (nothing is known about where it went).
    /// </summary>
    public static bool IsListingBlocked(DispatchPlan.Retrieve item) =>
        item.Where.Count > 0 && item.Where.All(w => !w.Fetchable);

    /// <summary>
    /// The end-of-run chat block, identical on the finishing path and the stopping path (Defect A).
    /// Empty list when there is nothing to report, so a clean run stays silent.
    /// </summary>
    /// <param name="what">What the run was ("cart", an item name) - so the line says what the pull unblocks.</param>
    public static IReadOnlyList<string> Lines(Summary s, string what = "the cart")
    {
        var lines = new List<string>();
        if (s.IsEmpty) return lines;

        if (s.Retainers.Count > 0)
        {
            var items = s.ItemCount;
            var bells = s.Retainers.Count;
            lines.Add($"to unblock {what} you have to pull {items} material{(items == 1 ? "" : "s")} off sale " +
                      $"({s.TotalUnits} unit{(s.TotalUnits == 1 ? "" : "s")} across {bells} retainer{(bells == 1 ? "" : "s")}):");
            foreach (var r in s.Retainers)
                lines.Add($"  {r.Retainer}: {r.ItemList}");
            lines.Add("  (open a summoning bell, take those units off the market board, then press Resume or /lcraft resume.)");
        }

        if (s.Others.Count > 0)
        {
            lines.Add($"also could not be retrieved, for other reasons ({s.Others.Count}) - these are NOT listed for sale:");
            foreach (var o in s.Others)
                lines.Add($"  {o.Name} x{o.Units} - {o.Why}");
        }

        if (s.Retainers.Count > 0) lines.Add("full detail: /lcraft blocked");
        return lines;
    }

    /// <summary>
    /// The <c>/lcraft blocked</c> body: every material, its units, the retainer, and the verbatim reason - the
    /// detail the short end-of-run block deliberately leaves out. Works after the run has ended.
    /// </summary>
    public static IReadOnlyList<string> Detail(Summary s, string what = "the cart")
    {
        if (s.IsEmpty) return new[] { "the last run had nothing it could not retrieve - nothing to pull off sale." };

        var lines = new List<string>();
        if (s.Retainers.Count > 0)
        {
            lines.Add($"listed for sale, so {what} is blocked on them - pull these off the market board:");
            foreach (var r in s.Retainers)
            {
                lines.Add($"  retainer {r.Retainer} ({r.TotalUnits} unit{(r.TotalUnits == 1 ? "" : "s")}):");
                foreach (var i in r.Items) lines.Add($"    {i.Name} x{i.Units}");
            }
        }
        if (s.Others.Count > 0)
        {
            lines.Add("could not be retrieved for other reasons (do NOT unlist anything for these):");
            foreach (var o in s.Others)
            {
                lines.Add($"  {o.Name} x{o.Units} - stock is at: {o.Places}");
                lines.Add($"    reason: {o.Why}");
            }
        }
        return lines;
    }

    /// <summary>The one short per-item line printed as each material is refused, replacing the multi-clause wall.</summary>
    public static string RefusalLine(string itemName, DispatchPlan.Retrieve item)
    {
        if (!IsListingBlocked(item))
            return $"{itemName} x{item.Quantity}: not in your bags and not on a retainer ({item.Detail}).";
        var who = item.Where.Where(w => !w.Fetchable).Select(w => w.Owner).Distinct().ToList();
        var by = who.Count == 0 ? "" : $" ({string.Join(", ", who)})";
        return $"{itemName} x{item.Quantity}: listed for sale on the market board{by} - pull it off sale to use it.";
    }

    /// <summary>
    /// Fold the unfetched list into a run's <see cref="BlockedItem"/> list, so the SNAPSHOT (the Run tab,
    /// <c>/lcraft status</c>, the Copy report button) names the retainer and the quantity - <b>on both ending
    /// paths</b> (Defect A, card t_35be7be5).
    /// <para>
    /// This is the seam the defect lived in. <c>FinishBlocked</c> had this loop inline; <c>Finish</c> - the DONE
    /// path Joey's 2026-09-05 22:44 run actually took - had nothing, and rendered only a bare count. Both endings
    /// now call this one function, so there is exactly one implementation to keep correct and a harness check can
    /// pin it without loading any Dalamud type.
    /// </para>
    /// <para>
    /// Existing entries win: a retrieval already named by the plan's own blocked list is not duplicated. Quantities
    /// are the folded per-item figures (Defect C), not the raw duplicates.
    /// </para>
    /// </summary>
    /// <param name="existing">The blocked list built from the plan; appended to in place order.</param>
    public static IReadOnlyList<BlockedItem> MergeIntoBlocked(
        IReadOnlyList<BlockedItem> existing,
        IEnumerable<(DispatchPlan.Retrieve Item, string Why)> unfetched,
        Func<uint, string> name)
    {
        var merged = new List<BlockedItem>(existing);
        var folded = new Dictionary<uint, DispatchPlan.Retrieve>();
        var order = new List<uint>();
        foreach (var (item, _) in unfetched)
        {
            if (item is null) continue;
            if (!folded.TryGetValue(item.ItemId, out var kept)) { folded[item.ItemId] = item; order.Add(item.ItemId); }
            else if (item.Quantity > kept.Quantity) folded[item.ItemId] = item;
        }
        foreach (var id in order)
        {
            if (merged.Any(b => b.Kind == StepKind.Retrieve && b.ItemId == id)) continue;
            var item = folded[id];
            merged.Add(new BlockedItem(StepKind.Retrieve, id, name(id), item.Quantity, null, item.Places));
        }
        return merged;
    }
}
