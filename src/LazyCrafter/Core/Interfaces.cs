using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

// Plain-data contracts the adapters implement (Plan §Phase 1). Nothing in this
// namespace may reference Dalamud or Lumina - the harness compiles Core alone.

public sealed record RecipeRow(
    uint RecipeId,
    uint ResultItemId,
    int ResultAmount,
    uint JobId,
    int Level,
    IReadOnlyList<(uint ItemId, int Amount)> Ingredients);

public enum NodeType { Regular, Unspoiled, Ephemeral, Legendary, Clouded }

public sealed record GatherInfo(uint JobId, int Level, NodeType NodeType, bool Timed, bool Collectable);

public sealed record VentureRow(
    uint TaskId,
    uint ItemId,
    int Level,
    uint JobCategory,
    int RequiredGathering,
    int RequiredItemLevel,
    IReadOnlyList<int> QuantityTiers);

public interface IGameData
{
    IEnumerable<RecipeRow> Recipes();
    bool IsGilVendor(uint itemId, out uint gil);
    bool IsSpecialShop(uint itemId);
    GatherInfo? Gather(uint itemId);
    bool IsFish(uint itemId);
    IEnumerable<VentureRow> Ventures();
    bool IsMarketable(uint itemId);
}

/// <summary>Counts already filtered by the enabled inventory sources.</summary>
public interface IInventory
{
    int Count(uint itemId);
}

public interface IPriceSource
{
    PriceQuote? Get(uint itemId);
}
