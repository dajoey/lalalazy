namespace LazyCrafter.Core.Model;

/// <summary>A resolved leaf of a recipe tree: what we need, what we have, where it can come from.</summary>
public sealed record IngredientLeaf(
    uint ItemId,
    int Need,
    int Have,
    IReadOnlyList<SourceKind> Sources,
    EffortTier Tier)
{
    public int Missing => Math.Max(0, Need - Have);
}
