using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>Result of <see cref="Tiering.Assess"/>: the recipe's bucket plus every leaf that fed it.</summary>
public sealed record RecipeAssessment(
    uint RecipeId,
    EffortTier Tier,
    int HowMany,
    IReadOnlyList<IngredientLeaf> Leaves)
{
    public bool CanCraft => HowMany > 0;
}

/// <summary>Result of <see cref="Tiering.AssessCart"/>: the cart's worst tier, one assessment per line, and the per-item totals.</summary>
public sealed record CartAssessment(
    EffortTier Tier,
    IReadOnlyList<RecipeAssessment> Lines,
    IReadOnlyList<IngredientLeaf> Totals)
{
    /// <summary>Only the items still short after everything on hand has been credited.</summary>
    public IEnumerable<IngredientLeaf> Missing => Totals.Where(l => l.Missing > 0);
}

/// <summary>
/// Buckets a recipe by the effort its missing materials take (Plan §Phase 1 task 4, Scope §3.2).
/// <para>
/// Per leaf: OnHand = 0; SubCraft = max(1, tiers of its own missing sub-leaves); GilVendor / RegularNode /
/// Venture = 1; TimedNode / Fish / Market / SpecialShop = 2; Drop = 3; Unknown = Blocked. A leaf with several
/// sources takes the cheapest. A recipe's tier is the max over its top-level ingredients (a sub-recipe's
/// leaves are already folded into the SubCraft tier of the ingredient they serve, and are reported for the UI).
/// </para>
/// <para>
/// Inventory is consumed as the tree is walked, so one on-hand unit is never credited to two leaves.
/// <see cref="RecipeAssessment.HowMany"/> is <see cref="RecipeGraph.HowMany(uint, IInventory)"/> unchanged.
/// </para>
/// </summary>
public sealed class Tiering
{
    private readonly RecipeGraph _graph;
    private readonly SourceClassifier _classifier;

    public Tiering(RecipeGraph graph, SourceClassifier classifier)
    {
        _graph = graph;
        _classifier = classifier;
    }

    public static EffortTier TierOf(SourceKind kind) => kind switch
    {
        SourceKind.OnHand => EffortTier.Now,
        SourceKind.SubCraft or SourceKind.GilVendor or SourceKind.RegularNode or SourceKind.Venture => EffortTier.Easy,
        SourceKind.TimedNode or SourceKind.Fish or SourceKind.Market or SourceKind.SpecialShop => EffortTier.SomeEffort,
        SourceKind.Drop => EffortTier.RealEffort,
        _ => EffortTier.Blocked,
    };

    /// <summary>Assess crafting <paramref name="crafts"/> runs of the recipe against <paramref name="inv"/>.</summary>
    public RecipeAssessment Assess(uint recipeId, IInventory inv, int crafts = 1)
    {
        var node = _graph.Expand(recipeId);
        if (node is null || crafts <= 0)
            return new RecipeAssessment(recipeId, EffortTier.Blocked, 0, Array.Empty<IngredientLeaf>());

        var leaves = new List<IngredientLeaf>();
        var consumed = new Dictionary<uint, int>();
        var tier = Walk(node, crafts, inv, consumed, leaves);
        return new RecipeAssessment(recipeId, tier, _graph.HowMany(recipeId, inv), leaves);
    }

    /// <summary>
    /// Assess several recipes as one shopping cart (Plan §Phase 4 task 4): one consumed-inventory ledger is shared
    /// across every line, so an on-hand unit is credited to at most one cart line, and the returned leaves are the
    /// per-item totals over the whole cart (need / have summed; tier worsened if a later occurrence is worse, and
    /// <see cref="IngredientLeaf.Sources"/> RE-DERIVED from the aggregate need / have). Lines with an unknown
    /// recipe or non-positive crafts are skipped.
    /// <para>
    /// The re-derivation is not cosmetic (card t_060e6992, 2026-09-05). <see cref="SourceClassifier.Classify"/>
    /// treats <see cref="SourceKind.OnHand"/> as an EXCLUSIVE sentinel: a leaf covered by stock gets
    /// <c>[OnHand]</c> and no other source is ever computed for it. The merge used to keep the FIRST occurrence's
    /// list, so an item that cart line 1 covered stayed labelled <c>[OnHand]</c> while line 2 made the total short.
    /// <c>DispatchPlan.RouteFor</c>'s <c>Route.Have</c> early-out needs <c>Missing == 0</c>, so it did not fire;
    /// <c>[OnHand]</c> then matched none of Gather / Venture / SubCraft / Vendor / Market and fell through to
    /// <c>Route.Manual</c>, which made <c>VisitIngredient</c> block every recipe above it (Iron Ore, Silver Ore,
    /// Moko Grass and Adamantite Nugget all went manual in one run; 49 deferrals against 4 for the same items in a
    /// single-line cart minutes earlier). Single-line carts never hit it: one occurrence, nothing to merge.
    /// </para>
    /// </summary>
    public CartAssessment AssessCart(IEnumerable<(uint RecipeId, int Crafts)> lines, IInventory inv)
    {
        var consumed = new Dictionary<uint, int>();
        var perLine = new List<RecipeAssessment>();
        var totals = new Dictionary<uint, IngredientLeaf>();
        var order = new List<uint>();
        var worst = EffortTier.Now;

        foreach (var (recipeId, crafts) in lines)
        {
            var node = _graph.Expand(recipeId);
            if (node is null || crafts <= 0) continue;
            var leaves = new List<IngredientLeaf>();
            var tier = Walk(node, crafts, inv, consumed, leaves);
            perLine.Add(new RecipeAssessment(recipeId, tier, _graph.HowMany(recipeId, inv), leaves));
            if (tier > worst) worst = tier;
            foreach (var l in leaves)
            {
                if (totals.TryGetValue(l.ItemId, out var t))
                {
                    totals[l.ItemId] = new IngredientLeaf(l.ItemId, checked(t.Need + l.Need), checked(t.Have + l.Have),
                        t.Sources, l.Tier > t.Tier ? l.Tier : t.Tier, Math.Min(t.Depth, l.Depth));
                }
                else
                {
                    totals[l.ItemId] = l;
                    order.Add(l.ItemId);
                }
            }
        }

        // Reclassify each per-item total against its AGGREGATE need / have instead of inheriting the first
        // occurrence's verdict (card t_060e6992). Unconditional rather than special-cased on [OnHand]: this makes a
        // merged total answer exactly the question a single-line leaf answers, and Classify already returns
        // [OnHand] when Have >= Need, so a genuinely covered total comes back unchanged.
        //
        // Tier is deliberately NOT recomputed from the new sources. Walk folds a sub-craft's whole sub-tree into
        // the leaf's tier (a SubCraft whose own materials are blocked is Blocked, not Easy), which
        // min(TierOf(sources)) cannot reproduce - recomputing would report a blocked sub-craft as Easy. The merged
        // tier is already consistent with the new sources: an occurrence is tiered Now only when it was covered, so
        // the max over occurrences is Now only when every one was, which is exactly when Have >= Need and Classify
        // returns [OnHand].
        return new CartAssessment(worst, perLine,
            order.Select(id =>
            {
                var t = totals[id];
                return t with { Sources = _classifier.Classify(t.ItemId, t.Need, t.Have) };
            }).ToArray());
    }

    /// <summary>Walks one recipe node for <paramref name="crafts"/> runs; returns the max tier over its ingredients.</summary>
    private EffortTier Walk(RecipeNode node, int crafts, IInventory inv, Dictionary<uint, int> consumed, List<IngredientLeaf> leaves, int depth = 0)
    {
        var worst = EffortTier.Now;
        foreach (var ing in node.Ingredients)
        {
            var need = checked(ing.Amount * crafts);
            var have = Take(ing.ItemId, need, inv, consumed);
            var missing = need - have;
            var sources = _classifier.Classify(ing.ItemId, need, have);

            var best = EffortTier.Blocked;
            if (missing == 0)
            {
                best = EffortTier.Now;
            }
            else
            {
                // Sub-craft first so its leaves land right after their parent in the list.
                foreach (var kind in sources)
                {
                    EffortTier t;
                    if (kind == SourceKind.SubCraft)
                    {
                        if (ing.SubRecipe is null) continue;   // cycle edge cut by Expand: not actually craftable here
                        var subCrafts = (missing + Math.Max(1, ing.SubRecipe.ResultAmount) - 1) / Math.Max(1, ing.SubRecipe.ResultAmount);
                        var subTier = Walk(ing.SubRecipe, subCrafts, inv, consumed, leaves, depth + 1);
                        t = subTier > EffortTier.Easy ? subTier : EffortTier.Easy;
                    }
                    else
                    {
                        t = TierOf(kind);
                    }
                    if (t < best) best = t;
                }
            }

            leaves.Add(new IngredientLeaf(ing.ItemId, need, have, sources, best, depth));
            if (best > worst) worst = best;
        }
        return worst;
    }

    private static int Take(uint itemId, int need, IInventory inv, Dictionary<uint, int> consumed)
    {
        consumed.TryGetValue(itemId, out var used);
        var available = Math.Max(0, inv.Count(itemId) - used);
        var take = Math.Min(available, need);
        consumed[itemId] = used + take;
        return take;
    }
}
