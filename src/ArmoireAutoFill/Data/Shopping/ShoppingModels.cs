namespace ArmoireAutoFill.Data.Shopping;

// Pure records for the Knightshopper shopping-list feature. No Dalamud types here —
// this file is compiled by both the plugin and the offline harness.

// One shop entry the plugin believes Knightshopper can buy: the exact
// (ItemId, VendorId, ShopId, SubCurrency) quadruple its import validation expects.
public sealed record ShopEntry(
    uint ItemId,
    uint VendorId,
    uint ShopId,
    int SubCurrency,
    byte CurrencyId,
    uint? Price,
    uint QuestRowId,
    uint AchievementRowId,
    ShopSource Source);

public enum ShopSource : byte
{
    SpecialShop = 0,
    GilShop = 1,
}

// An armoire-eligible item the character does not own and Knightshopper can buy.
public sealed record ShoppingCandidate(
    uint ItemId,
    string Name,
    byte CurrencyId,
    string CurrencyName,
    ShopEntry Entry);

public sealed record ExcludedItem(uint ItemId, string Name, string Reason);

// Per-currency result block.
public sealed record CurrencyCandidates(
    byte CurrencyId,
    string CurrencyName,
    IReadOnlyList<ShoppingCandidate> Items,
    bool Truncated,
    long TotalPrice);

public sealed record ShoppingResult(
    IReadOnlyList<CurrencyCandidates> Currencies,
    IReadOnlyList<ExcludedItem> Excluded,
    int MissingNotBuyable);
