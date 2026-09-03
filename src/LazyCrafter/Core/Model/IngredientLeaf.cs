namespace LazyCrafter.Core.Model;

/// <summary>
/// A resolved leaf of a recipe tree: what we need, what we have, where it can come from.
/// <see cref="Depth"/> is 0 for a top-level ingredient, 1 for an ingredient of a sub-craft the walk chose, and so
/// on; in <see cref="LazyCrafter.Core.RecipeAssessment.Leaves"/> a sub-craft's leaves directly <b>precede</b> the
/// ingredient they serve (see <see cref="LazyCrafter.Core.IngredientTree"/> to rebuild the tree).
/// </summary>
public sealed record IngredientLeaf(
    uint ItemId,
    int Need,
    int Have,
    IReadOnlyList<SourceKind> Sources,
    EffortTier Tier,
    int Depth = 0)
{
    public int Missing => Math.Max(0, Need - Have);
}
