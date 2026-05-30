using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System.Linq;
using LazyFateAutomation.Helpers.Utils;

namespace clib.Extensions;

public static class RecipeExtensions {
    public static ItemHandle Handle(this Recipe row) => (ItemHandle)row.ItemResult.RowId;

    public static (ItemHandle item, int amount)[] IngredientsWithAmounts(this Recipe row)
        => [.. row.Ingredient.Zip(row.AmountIngredient, (item, amount) => ((ItemHandle)item.RowId, (int)amount))];

    public static unsafe void Open(this Recipe row) => AgentRecipeNote.Instance()->OpenRecipeByRecipeId(row.RowId);
}
