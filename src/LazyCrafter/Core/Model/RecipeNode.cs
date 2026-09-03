namespace LazyCrafter.Core.Model;

/// <summary>A recipe expanded into its ingredient tree (Plan §Phase 1 RecipeGraph.Expand).</summary>
public sealed record RecipeNode(
    uint RecipeId,
    uint ResultItemId,
    int ResultAmount,
    uint JobId,
    int Level,
    IReadOnlyList<RecipeNode.Ingredient> Ingredients)
{
    /// <summary>An ingredient slot; <see cref="SubRecipe"/> is set when it is itself craftable.</summary>
    public sealed record Ingredient(uint ItemId, int Amount, RecipeNode? SubRecipe);
}
