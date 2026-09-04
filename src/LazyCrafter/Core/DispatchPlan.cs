using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>
/// Turns an assessed cart into work for the four hand-off channels (Plan §Phase 5 task 6, Scope §3.4).
/// Pure: assessments + recipe graph + retainer stats in, a routed plan out. The adapters only execute it.
/// <para>
/// Routing per missing item, first match wins:
/// <list type="number">
/// <item><see cref="SourceKind.RegularNode"/> / <see cref="SourceKind.TimedNode"/> / <see cref="SourceKind.Fish"/> → <see cref="Gathers"/> (GBR, now).</item>
/// <item><see cref="SourceKind.Venture"/> (a managed retainer qualifies) → <see cref="Ventures"/> (ARC, asynchronous - hours to days,
/// so it only wins when nothing can fetch the item this session; the per-leaf buttons still offer it explicitly).</item>
/// <item><see cref="SourceKind.SubCraft"/> whose sub-tree is itself routable → <see cref="Crafts"/> (Artisan), depth-first so
/// intermediates are made before the recipe that consumes them.</item>
/// <item><see cref="SourceKind.GilVendor"/> → <see cref="Vendor"/>; <see cref="SourceKind.Market"/> → <see cref="Market"/> (Lifestream + shopping list).</item>
/// <item>anything else → <see cref="Manual"/>.</item>
/// </list>
/// A craft is only queued when every material below it is <b>in the bags</b> or comes from a gather; a craft that needs a
/// venture result, a purchase, a manual item, or stock that is sitting somewhere other than the bags is
/// <see cref="Deferred"/> with the reason, because Artisan would just fail on it.
/// Crafts whose materials come from a gather carry <see cref="Craft.AfterGather"/> so the executor holds them until GBR is idle
/// (GBR and Artisan both drive the character; they cannot run at once).
/// </para>
/// <para>
/// <b>Owned is not in-bags.</b> The catalog counts everything AllaganTools can see (Scope §0) - retainers, the
/// saddlebag, the armoury chest, the glamour dresser, alt characters - so a recipe can read as fully stocked while a
/// synthesis would fail on the spot, because a craft consumes the four bags plus the crystal pouch and nothing else.
/// Every unit the plan intends to consume is therefore checked against <see cref="IInventory.CountInBags"/>; the
/// shortfall becomes a <see cref="Retrieve"/> item naming where to fetch it from, and any craft that would consume
/// it is deferred rather than handed to Artisan. Passing no inventory keeps the old behaviour (everything owned is
/// assumed to be in the bags).
/// </para>
/// </summary>
public static class DispatchPlan
{
    public sealed record Line(RecipeAssessment Assessment, int Crafts);
    public sealed record Venture(uint ItemId, int Quantity, VentureMatch Match);
    public sealed record Gather(uint ItemId, int Quantity, SourceKind Kind);
    public sealed record Craft(uint RecipeId, uint ResultItemId, int Crafts, int Depth, bool AfterGather);
    public sealed record Purchase(uint ItemId, int Quantity);
    public sealed record Deferral(uint RecipeId, uint ResultItemId, int Crafts, string Reason);
    public sealed record ManualItem(uint ItemId, int Quantity, IReadOnlyList<SourceKind> Sources);

    /// <summary>
    /// Stock the plan wants to consume that is <b>not in the bags</b>: fetch <see cref="Quantity"/> of
    /// <see cref="ItemId"/> from <see cref="Where"/> (most-stocked place first) before any craft that needs it can run.
    /// This is a manual step - LazyCrafter never opens a retainer for you.
    /// </summary>
    public sealed record Retrieve(uint ItemId, int Quantity, IReadOnlyList<StoredElsewhere> Where)
    {
        /// <summary>"retainer Cid, the saddlebag" - the places, most-stocked first, for one chat line.</summary>
        public string Places => Where.Count == 0 ? "elsewhere" : string.Join(", ", Where.Select(w => w.Where));

        /// <summary>"107 on retainer Cid, 3 in the saddlebag" - the same places with their counts.</summary>
        public string Detail => Where.Count == 0 ? $"{Quantity} not in your bags" : string.Join(", ", Where.Select(w => w.Phrase));
    }

    public sealed record Plan(
        IReadOnlyList<Venture> Ventures,
        IReadOnlyList<Gather> Gathers,
        IReadOnlyList<Craft> Crafts,
        IReadOnlyList<Purchase> Vendor,
        IReadOnlyList<Purchase> Market,
        IReadOnlyList<ManualItem> Manual,
        IReadOnlyList<Deferral> Deferred,
        IReadOnlyList<Retrieve> Retrievals)
    {
        /// <summary>Back-compat ctor for callers that build a single-channel plan (per-leaf fulfil buttons).</summary>
        public Plan(
            IReadOnlyList<Venture> ventures,
            IReadOnlyList<Gather> gathers,
            IReadOnlyList<Craft> crafts,
            IReadOnlyList<Purchase> vendor,
            IReadOnlyList<Purchase> market,
            IReadOnlyList<ManualItem> manual,
            IReadOnlyList<Deferral> deferred)
            : this(ventures, gathers, crafts, vendor, market, manual, deferred, Array.Empty<Retrieve>()) { }

        public bool IsEmpty => Ventures.Count == 0 && Gathers.Count == 0 && Crafts.Count == 0 && Vendor.Count == 0 && Market.Count == 0 && Manual.Count == 0 && Deferred.Count == 0 && Retrievals.Count == 0;

        /// <summary>Work we can hand off. A retrieval is <b>not</b> work - only the player can do it.</summary>
        public bool HasWork => Ventures.Count + Gathers.Count + Crafts.Count > 0;
        public Dictionary<uint, int> GatherDictionary() => Gathers.GroupBy(g => g.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        public Dictionary<uint, int> VentureDictionary() => Ventures.GroupBy(v => v.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
    }

    private enum Route { Have, Venture, Gather, Craft, Vendor, Market, Manual }

    /// <summary>
    /// Build the plan. <paramref name="lines"/> are the cart's per-line assessments (from <see cref="Tiering.AssessCart"/>,
    /// which shares one inventory ledger so a unit is never credited twice); <paramref name="totals"/> is that cart's
    /// per-item total list. <paramref name="retainers"/> / <paramref name="gatheredItems"/> feed the venture resolver.
    /// <para>
    /// <paramref name="inv"/> is the same inventory the assessment was made against and is used for one extra question
    /// the assessment does not ask: of the units we are about to consume from stock, how many are physically in the
    /// bags? The rest become <see cref="Plan.Retrievals"/> and block every craft that would consume them. Pass
    /// <c>null</c> to keep the pre-fix behaviour (owned == in bags).
    /// </para>
    /// </summary>
    public static Plan Build(
        IReadOnlyList<Line> lines,
        IReadOnlyList<IngredientLeaf> totals,
        RecipeGraph graph,
        VentureResolver ventures,
        IReadOnlyList<RetainerStats> retainers,
        IReadOnlySet<uint>? gatheredItems = null,
        IInventory? inv = null)
    {
        var ventureList = new List<Venture>();
        var gatherList = new List<Gather>();
        var craftList = new List<Craft>();
        var vendorList = new List<Purchase>();
        var marketList = new List<Purchase>();
        var manualList = new List<ManualItem>();
        var deferred = new List<Deferral>();
        var retrieveList = new List<Retrieve>();

        // Route every item once, from the cart totals, so the ARC/GBR dictionaries carry the whole cart's quantity.
        // In the same pass, check the stock we plan to consume against the BAGS: `Have` came from every enabled
        // inventory source, and a synthesis can only reach the four bags + crystals.
        var routeOf = new Dictionary<uint, Route>();
        var retrieveOf = new Dictionary<uint, Retrieve>();
        foreach (var leaf in totals)
        {
            var (route, match) = RouteFor(leaf, ventures, retainers, gatheredItems);
            routeOf[leaf.ItemId] = route;

            if (inv is not null && leaf.Have > 0)
            {
                var inBags = Math.Max(0, Math.Min(leaf.Have, inv.CountInBags(leaf.ItemId)));
                var shortfall = leaf.Have - inBags;
                if (shortfall > 0)
                {
                    var r = new Retrieve(leaf.ItemId, shortfall, PlacesFor(inv.StoredWhere(leaf.ItemId), shortfall));
                    retrieveOf[leaf.ItemId] = r;
                    retrieveList.Add(r);
                }
            }

            if (leaf.Missing <= 0) continue;
            switch (route)
            {
                case Route.Venture: ventureList.Add(new Venture(leaf.ItemId, leaf.Missing, match!)); break;
                case Route.Gather: gatherList.Add(new Gather(leaf.ItemId, leaf.Missing, GatherKind(leaf.Sources))); break;
                case Route.Vendor: vendorList.Add(new Purchase(leaf.ItemId, leaf.Missing)); break;
                case Route.Market: marketList.Add(new Purchase(leaf.ItemId, leaf.Missing)); break;
                case Route.Manual: manualList.Add(new ManualItem(leaf.ItemId, leaf.Missing, leaf.Sources)); break;
            }
        }

        // Crafts: walk each line's tree depth-first; sub-crafts before the recipe that consumes them.
        foreach (var line in lines)
        {
            if (line.Crafts <= 0) continue;
            var row = graph.Row(line.Assessment.RecipeId);
            if (row is null) continue;
            var roots = IngredientTree.Build(line.Assessment.Leaves);
            var blockers = new List<string>();
            var afterGather = false;
            foreach (var root in roots)
                VisitIngredient(root, row.JobId, 0, graph, routeOf, retrieveOf, craftList, deferred, blockers, ref afterGather);
            if (blockers.Count > 0)
                deferred.Add(new Deferral(row.RecipeId, row.ResultItemId, line.Crafts, "needs " + string.Join(", ", blockers.Distinct())));
            else
                craftList.Add(new Craft(row.RecipeId, row.ResultItemId, line.Crafts, 0, afterGather));
        }

        return new Plan(ventureList, gatherList, craftList, vendorList, marketList, manualList, deferred, retrieveList);
    }

    /// <summary>The places, most-stocked first, that together hold <paramref name="quantity"/> units; the last is clipped.</summary>
    private static IReadOnlyList<StoredElsewhere> PlacesFor(IReadOnlyList<StoredElsewhere> where, int quantity)
    {
        if (where.Count == 0) return Array.Empty<StoredElsewhere>();
        var taken = new List<StoredElsewhere>();
        var left = quantity;
        foreach (var w in where.OrderByDescending(w => w.Quantity))
        {
            if (left <= 0) break;
            var take = Math.Min(left, w.Quantity);
            if (take <= 0) continue;
            taken.Add(take == w.Quantity ? w : w with { Quantity = take });
            left -= take;
        }
        return taken.Count > 0 ? taken : where;
    }

    /// <summary>
    /// The guard the executor runs <b>immediately before</b> handing one recipe to Artisan (Plan §Phase 5 defect fix, task 3).
    /// <para>
    /// A plan is built once and then executed over minutes: gathers land, sub-crafts consume, the player moves stock
    /// around. This asks the only question that matters at that instant - can the bags pay for
    /// <paramref name="crafts"/> runs of <paramref name="recipe"/> right now - and returns one
    /// <see cref="Retrieve"/> per ingredient that is short, naming where the missing units are sitting. An empty
    /// list means go. Direct ingredients only: sub-crafts are separate queue entries that have already run by then.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Retrieve> BagsShortfall(RecipeRow recipe, int crafts, IInventory inv)
    {
        if (crafts <= 0) return Array.Empty<Retrieve>();
        var short_ = new List<Retrieve>();
        foreach (var (itemId, amount) in recipe.Ingredients)
        {
            var need = checked(amount * crafts);
            if (need <= 0) continue;
            var inBags = Math.Max(0, inv.CountInBags(itemId));
            if (inBags >= need) continue;
            var missing = need - inBags;
            short_.Add(new Retrieve(itemId, missing, PlacesFor(inv.StoredWhere(itemId), missing)));
        }
        return short_;
    }

    /// <summary>
    /// Route a single ingredient the way <see cref="Build"/> would, for the per-leaf fulfil buttons.
    /// Returns the channel name the UI should offer first and, for a sub-craft, the recipe to hand Artisan.
    /// </summary>
    public static (SourceKind Channel, RecipeRow? SubRecipe) RouteLeaf(IngredientLeaf leaf, uint parentJob, RecipeGraph graph, VentureResolver ventures, IReadOnlyList<RetainerStats> retainers, IReadOnlySet<uint>? gatheredItems = null)
    {
        var (route, _) = RouteFor(leaf, ventures, retainers, gatheredItems);
        return route switch
        {
            Route.Venture => (SourceKind.Venture, null),
            Route.Gather => (GatherKind(leaf.Sources), null),
            Route.Craft => (SourceKind.SubCraft, graph.RecipeFor(leaf.ItemId, parentJob)),
            Route.Vendor => (SourceKind.GilVendor, null),
            Route.Market => (SourceKind.Market, null),
            Route.Have => (SourceKind.OnHand, null),
            _ => (leaf.Sources.Count > 0 ? leaf.Sources[0] : SourceKind.Unknown, null),
        };
    }

    private static (Route, VentureMatch?) RouteFor(IngredientLeaf leaf, VentureResolver ventures, IReadOnlyList<RetainerStats> retainers, IReadOnlySet<uint>? gatheredItems)
    {
        if (leaf.Missing <= 0) return (Route.Have, null);
        if (leaf.Sources.Any(s => s is SourceKind.RegularNode or SourceKind.TimedNode or SourceKind.Fish)) return (Route.Gather, null);
        if (leaf.Sources.Contains(SourceKind.Venture) && ventures.ResolveBest(leaf.ItemId, retainers, gatheredItems) is { } m) return (Route.Venture, m);
        if (leaf.Sources.Contains(SourceKind.SubCraft)) return (Route.Craft, null);
        if (leaf.Sources.Contains(SourceKind.GilVendor)) return (Route.Vendor, null);
        if (leaf.Sources.Contains(SourceKind.Market)) return (Route.Market, null);
        if (leaf.Sources.Contains(SourceKind.SpecialShop)) return (Route.Manual, null);
        return (Route.Manual, null);
    }

    private static SourceKind GatherKind(IReadOnlyList<SourceKind> sources) =>
        sources.Contains(SourceKind.RegularNode) ? SourceKind.RegularNode
        : sources.Contains(SourceKind.TimedNode) ? SourceKind.TimedNode
        : SourceKind.Fish;

    /// <summary>
    /// Visit one ingredient node. Emits sub-crafts (children first) into <paramref name="crafts"/>; appends to
    /// <paramref name="blockers"/> when something below cannot be made now; sets <paramref name="afterGather"/> when a gather
    /// feeds this branch. A node whose on-hand stock is sitting outside the bags blocks too, whatever its route -
    /// Artisan cannot consume a retainer's stack.
    /// </summary>
    private static void VisitIngredient(IngredientTree.Node node, uint parentJob, int depth, RecipeGraph graph,
        Dictionary<uint, Route> routeOf, Dictionary<uint, Retrieve> retrieveOf, List<Craft> crafts, List<Deferral> deferred, List<string> blockers, ref bool afterGather)
    {
        var leaf = node.Leaf;
        // Checked BEFORE the on-hand early-out: a leaf can be fully "have" and still be unreachable.
        if (retrieveOf.TryGetValue(leaf.ItemId, out var fetch))
            blockers.Add($"retrieve #{leaf.ItemId} x{fetch.Quantity} (from {fetch.Places})");
        if (leaf.Missing <= 0) return;
        var route = routeOf.TryGetValue(leaf.ItemId, out var r) ? r : Route.Manual;
        switch (route)
        {
            case Route.Have:
                return;
            case Route.Venture:
                blockers.Add($"venture #{leaf.ItemId}");
                return;
            case Route.Gather:
                afterGather = true;
                return;
            case Route.Craft:
            {
                var sub = graph.RecipeFor(leaf.ItemId, parentJob);
                if (sub is null || node.Children.Count == 0) { blockers.Add($"craft #{leaf.ItemId}"); return; }
                var subBlockers = new List<string>();
                var subAfterGather = false;
                foreach (var c in node.Children)
                    VisitIngredient(c, sub.JobId, depth + 1, graph, routeOf, retrieveOf, crafts, deferred, subBlockers, ref subAfterGather);
                var subCrafts = (leaf.Missing + Math.Max(1, sub.ResultAmount) - 1) / Math.Max(1, sub.ResultAmount);
                if (subBlockers.Count > 0)
                {
                    deferred.Add(new Deferral(sub.RecipeId, sub.ResultItemId, subCrafts, "needs " + string.Join(", ", subBlockers.Distinct())));
                    blockers.Add($"craft #{leaf.ItemId}");
                    return;
                }
                crafts.Add(new Craft(sub.RecipeId, sub.ResultItemId, subCrafts, depth + 1, subAfterGather));
                if (subAfterGather) afterGather = true;
                return;
            }
            case Route.Vendor: blockers.Add($"buy #{leaf.ItemId}"); return;
            case Route.Market: blockers.Add($"market #{leaf.ItemId}"); return;
            default: blockers.Add($"manual #{leaf.ItemId}"); return;
        }
    }
}
