using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class RecipeExtensions {
    extension(Recipe row) {
        public ItemHandle Handle => (ItemHandle)row.ItemResult;

        public (ItemHandle item, int amount)[] IngredientsWithAmounts
            => [.. row.Ingredient.Zip(row.AmountIngredient, (item, amount) => ((ItemHandle)item, (int)amount))];
    }

    public static unsafe void Open(this Recipe row) => AgentRecipeNote.Instance()->OpenRecipeByRecipeId(row.RowId);
}
