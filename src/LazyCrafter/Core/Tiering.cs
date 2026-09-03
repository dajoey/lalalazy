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
    /// per-item totals over the whole cart (need / have summed, sources and tier from the first occurrence, tier
    /// worsened if a later occurrence is worse). Lines with an unknown recipe or non-positive crafts are skipped.
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

        return new CartAssessment(worst, perLine, order.Select(id => totals[id]).ToArray());
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
