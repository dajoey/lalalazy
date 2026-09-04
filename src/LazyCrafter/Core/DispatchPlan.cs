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
/// A craft is only queued when every material below it is on hand or comes from a gather; a craft that needs a venture
/// result, a purchase, or a manual item is <see cref="Deferred"/> with the reason, because Artisan would just fail on it.
/// Crafts whose materials come from a gather carry <see cref="Craft.AfterGather"/> so the executor holds them until GBR is idle
/// (GBR and Artisan both drive the character; they cannot run at once).
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

    public sealed record Plan(
        IReadOnlyList<Venture> Ventures,
        IReadOnlyList<Gather> Gathers,
        IReadOnlyList<Craft> Crafts,
        IReadOnlyList<Purchase> Vendor,
        IReadOnlyList<Purchase> Market,
        IReadOnlyList<ManualItem> Manual,
        IReadOnlyList<Deferral> Deferred)
    {
        public bool IsEmpty => Ventures.Count == 0 && Gathers.Count == 0 && Crafts.Count == 0 && Vendor.Count == 0 && Market.Count == 0 && Manual.Count == 0 && Deferred.Count == 0;
        public bool HasWork => Ventures.Count + Gathers.Count + Crafts.Count > 0;
        public Dictionary<uint, int> GatherDictionary() => Gathers.GroupBy(g => g.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        public Dictionary<uint, int> VentureDictionary() => Ventures.GroupBy(v => v.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
    }

    private enum Route { Have, Venture, Gather, Craft, Vendor, Market, Manual }

    /// <summary>
    /// Build the plan. <paramref name="lines"/> are the cart's per-line assessments (from <see cref="Tiering.AssessCart"/>,
    /// which shares one inventory ledger so a unit is never credited twice); <paramref name="totals"/> is that cart's
    /// per-item total list. <paramref name="retainers"/> / <paramref name="gatheredItems"/> feed the venture resolver.
    /// </summary>
    public static Plan Build(
        IReadOnlyList<Line> lines,
        IReadOnlyList<IngredientLeaf> totals,
        RecipeGraph graph,
        VentureResolver ventures,
        IReadOnlyList<RetainerStats> retainers,
        IReadOnlySet<uint>? gatheredItems = null)
    {
        var ventureList = new List<Venture>();
        var gatherList = new List<Gather>();
        var craftList = new List<Craft>();
        var vendorList = new List<Purchase>();
        var marketList = new List<Purchase>();
        var manualList = new List<ManualItem>();
        var deferred = new List<Deferral>();

        // Route every item once, from the cart totals, so the ARC/GBR dictionaries carry the whole cart's quantity.
        var routeOf = new Dictionary<uint, Route>();
        foreach (var leaf in totals)
        {
            var (route, match) = RouteFor(leaf, ventures, retainers, gatheredItems);
            routeOf[leaf.ItemId] = route;
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
                VisitIngredient(root, row.JobId, 0, graph, routeOf, craftList, deferred, blockers, ref afterGather);
            if (blockers.Count > 0)
                deferred.Add(new Deferral(row.RecipeId, row.ResultItemId, line.Crafts, "needs " + string.Join(", ", blockers.Distinct())));
            else
                craftList.Add(new Craft(row.RecipeId, row.ResultItemId, line.Crafts, 0, afterGather));
        }

        return new Plan(ventureList, gatherList, craftList, vendorList, marketList, manualList, deferred);
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
    /// feeds this branch.
    /// </summary>
    private static void VisitIngredient(IngredientTree.Node node, uint parentJob, int depth, RecipeGraph graph,
        Dictionary<uint, Route> routeOf, List<Craft> crafts, List<Deferral> deferred, List<string> blockers, ref bool afterGather)
    {
        var leaf = node.Leaf;
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
                    VisitIngredient(c, sub.JobId, depth + 1, graph, routeOf, crafts, deferred, subBlockers, ref subAfterGather);
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
