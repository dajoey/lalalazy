using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Catalog;

/// <summary>One recipe as the UI sees it. Immutable; produced by <see cref="CatalogService"/> off the framework thread.</summary>
public sealed record CatalogRow(
    uint RecipeId,
    uint ResultItemId,
    int ResultAmount,
    string Name,
    uint JobId,
    string Job,
    int Level,
    int JobLevel,
    EffortTier Tier,
    int HowMany,
    IReadOnlyList<IngredientLeaf> Leaves,
    string MissingSummary,
    ProfitEstimate? Nq,
    ProfitEstimate? Hq,
    bool Marketable,
    bool CanBeHq,
    int Scrip,
    double? Desynth,
    bool LogComplete,
    int ExpPerCraft)
{
    public bool CanCraft => HowMany > 0;
    public bool AboveLevel => JobLevel > 0 && Level > JobLevel;
    public ProfitEstimate? Est(bool hq) => hq ? Hq : Nq;
    public int MissingCount => Leaves.Count(l => l.Missing > 0);
}

/// <summary>One cart line with its cost picture at the requested quantity.</summary>
public sealed record CartLine(uint RecipeId, int Crafts, CatalogRow? Row, RecipeAssessment Assessment, ProfitEstimate? Estimate);

/// <summary>Everything one compute pass produced. Swapped atomically into <see cref="CatalogService.Snapshot"/>.</summary>
public sealed record CatalogSnapshot(
    int Generation,
    IReadOnlyList<CatalogRow> Rows,
    IReadOnlyDictionary<uint, CatalogRow> ByRecipe,
    IReadOnlyDictionary<EffortTier, int> TierCounts,
    int NotYetCrafted,
    IReadOnlyDictionary<uint, int> Jobs,
    IReadOnlyList<CartLine> Cart,
    CartAssessment CartTotals,
    bool LoggedIn,
    bool InventoryDegraded,
    int RetainerCount,
    int PricedRows,
    DateTime ComputedAt,
    TimeSpan Duration)
{
    public static readonly CatalogSnapshot Empty = new(0, Array.Empty<CatalogRow>(), new Dictionary<uint, CatalogRow>(),
        new Dictionary<EffortTier, int>(), 0, new Dictionary<uint, int>(), Array.Empty<CartLine>(),
        new CartAssessment(EffortTier.Now, Array.Empty<RecipeAssessment>(), Array.Empty<IngredientLeaf>()),
        false, false, 0, 0, DateTime.MinValue, TimeSpan.Zero);

    public int Count(EffortTier tier) => TierCounts.TryGetValue(tier, out var n) ? n : 0;
    /// <summary>The "Real effort" bucket shows tier 3 and Blocked together (Scope §3.2: they don't vanish).</summary>
    public int RealEffortCount => Count(EffortTier.RealEffort) + Count(EffortTier.Blocked);
}
