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

/// <summary>
/// One retainer venture, i.e. one <c>RetainerTask</c> row joined to its <c>RetainerTaskNormal</c>
/// and <c>RetainerTaskParameter</c> rows. Field semantics follow the public EXD schema:
/// <list type="bullet">
/// <item><see cref="Level"/> = <c>RetainerTask.RetainerLevel</c>.</item>
/// <item><see cref="JobCategory"/> = <c>RetainerTask.ClassJobCategory</c> row id
///   (17 = MIN, 18 = BTN, 19 = FSH, anything else = combat / DoW-DoM).</item>
/// <item><see cref="RequiredGathering"/> / <see cref="RequiredItemLevel"/> = the same-named
///   <c>RetainerTask</c> columns.</item>
/// <item><see cref="QuantityTiers"/> = <c>RetainerTaskNormal.Quantity[0..4]</c>, ascending reward tiers.</item>
/// <item><see cref="RewardThresholds"/> = the four <c>RetainerTaskParameter</c> stat breakpoints that
///   gate tiers 1..4 (<c>ItemLevelDoW</c> for combat, <c>PerceptionDoL</c> for MIN/BTN,
///   <c>PerceptionFSH</c> for FSH). Tier 0 has no threshold.</item>
/// </list>
/// </summary>
public sealed record VentureRow(
    uint TaskId,
    uint ItemId,
    int Level,
    uint JobCategory,
    int RequiredGathering,
    int RequiredItemLevel,
    IReadOnlyList<int> QuantityTiers,
    IReadOnlyList<int> RewardThresholds);

public interface IGameData
{
    IEnumerable<RecipeRow> Recipes();
    bool IsGilVendor(uint itemId, out uint gil);
    bool IsSpecialShop(uint itemId);
    GatherInfo? Gather(uint itemId);
    bool IsFish(uint itemId);
    IEnumerable<VentureRow> Ventures();
    bool IsMarketable(uint itemId);
    /// <summary>Monster drop / voyage / dungeon source known (TeamCraft drop-sources, voyage-sources).</summary>
    bool IsDrop(uint itemId);
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
