using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

// Plain-data contracts the adapters implement (Plan §Phase 1). Nothing in this
// namespace may reference game-client or sheet-reader assemblies - the harness compiles Core alone.

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

    // ---- Phase 2 additions ----

    /// <summary>
    /// Collectable turn-in data for an item, or <c>null</c> when it is not a collectable:
    /// <c>CollectablesShopItem</c> joined to <c>CollectablesShopRefine</c> (the three collectability
    /// breakpoints) and <c>CollectablesShopRewardScrip</c> (currency + reward per tier + XP ratio per tier).
    /// </summary>
    CollectableInfo? Collectable(uint itemId);

    /// <summary>
    /// Desynthesis outcomes for an item (<c>Item.Desynth</c> flag + the supplemental desynth-results table),
    /// or an empty list when the item cannot be desynthesized / nothing is known.
    /// </summary>
    IReadOnlyList<DesynthResult> Desynth(uint itemId);
}

/// <summary>
/// One collectable's turn-in table. <see cref="Collectability"/> are the low/mid/high breakpoints
/// (<c>CollectablesShopRefine</c>); <see cref="Reward"/> the scrip paid at each
/// (<c>CollectablesShopRewardScrip.Low/Mid/HighReward</c>); <see cref="ExpRatio"/> the matching
/// <c>ExpRatioLow/Mid/High</c>. <see cref="Currency"/> is the scrip item/currency id of the shop.
/// <see cref="LevelMin"/>/<see cref="LevelMax"/> is the job-level band the shop accepts it in.
/// </summary>
public sealed record CollectableInfo(
    uint ItemId,
    uint Currency,
    int LevelMin,
    int LevelMax,
    IReadOnlyList<int> Collectability,
    IReadOnlyList<int> Reward,
    IReadOnlyList<int> ExpRatio);

/// <summary>One possible desynth output: the item, its drop chance (0..1) and the mean quantity when it drops.</summary>
public sealed record DesynthResult(uint ItemId, double Chance, double Quantity = 1);

/// <summary>
/// Counts already filtered by the enabled inventory sources.
/// <para>
/// <see cref="Count"/> is what the character <b>owns</b> across every enabled source (Scope §0 "Inventory scope =
/// everything AllaganTools can see") and drives tiering, HowMany and profit. <see cref="CountInBags"/> is the far
/// narrower question a craft actually asks - <b>is it in the bags right now</b> - because a synthesis can only
/// consume the four bags plus the crystal pouch. The two differ whenever stock sits on a retainer, in the
/// saddlebag, the armoury chest, the glamour dresser or on another character; that difference is a Retrieve step,
/// not free stock, and <see cref="StoredWhere"/> names the places so the plan can print where to fetch it from.
/// </para>
/// <para>
/// A minimal implementation may return <see cref="Count"/> from <see cref="CountInBags"/> and an empty list from
/// <see cref="StoredWhere"/>; the plan then behaves exactly as it did before this interface grew, i.e. it assumes
/// everything owned is in the bags. Adapters that can tell the difference should.
/// </para>
/// </summary>
public interface IInventory
{
    /// <summary>Units owned across every enabled inventory source.</summary>
    int Count(uint itemId);

    /// <summary>
    /// Units physically in the character's bags (and crystal pouch) - what a craft can consume without fetching
    /// anything. Never greater than <see cref="Count"/>.
    /// </summary>
    int CountInBags(uint itemId) => Count(itemId);

    /// <summary>
    /// Where the units that are <b>not</b> in the bags are sitting: reachable places first, most-stocked first
    /// within each group. Only places holding at least one unit appear. Empty when everything owned is already in
    /// the bags (or when the adapter cannot tell).
    /// <para>
    /// The list may include places you cannot fetch from - a market-board listing is reported so the player is
    /// told where the stock went, with <see cref="StoredElsewhere.Fetchable"/> false. A consumer choosing where to
    /// send the player must respect that flag rather than the quantity (card t_05e6722b);
    /// <c>DispatchPlan.PlacesFor</c> is the shared implementation.
    /// </para>
    /// </summary>
    IReadOnlyList<StoredElsewhere> StoredWhere(uint itemId) => Array.Empty<StoredElsewhere>();

    /// <summary>
    /// Units of a currency item the player holds - Grand Company seals, beast-tribe tokens, tomestones, Fluorite
    /// Lenses. <c>null</c> when it cannot be read at all (no client, no inventory bridge); 0 means "read it, they
    /// have none". The currency-shop routing treats both the same way - it refuses - so a partial or failed read
    /// can only ever move an item BACK to the market board, never spend anything (card t_b431de3a, decision D2).
    /// <para>
    /// Defaults to <c>null</c> so an implementation that cannot answer simply does not, and the affordability gate
    /// closes on its own.
    /// </para>
    /// </summary>
    int? CurrencyBalance(uint currencyItemId) => null;
}

public interface IPriceSource
{
    PriceQuote? Get(uint itemId);
}
