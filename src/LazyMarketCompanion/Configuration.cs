using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion;

// UndercutMode moved to AutoMarket/PriceMath.cs in 0.1.9.0 so the Dalamud-free price formula and the
// offline harness can see it. Same namespace, same member order, so existing configs are unaffected.

/// <summary>Where auto-market may take stock from.</summary>
public enum StockSource
{
  BagsOnly,
  RetainerOnly,
  BagsAndRetainer
}

/// <summary>
/// What Auto-Market does when it cannot positively identify the sell-list row holding a listing it just
/// created. It reads the row from the addon rather than inferring it (see AutoMarket/SellListRows.cs), so
/// this should not happen - but the whole point of 0.1.5.0 is that a fallback which quietly does the thing
/// the user asked us to stop doing is worse than no feature at all, so the fallback is a choice.
/// </summary>
public enum PinchFallbackMode
{
  /// <summary>
  /// Re-price every listing on the retainer (the 0.1.3.0/0.1.4.0 behaviour). Nothing is ever left stranded at
  /// the placeholder price, at the cost of re-pricing listings the user never asked us to touch.
  /// </summary>
  KeepFullRepass,
  /// <summary>
  /// Price nothing, and say so in chat. The new listing stays at its placeholder price (so it will not sell)
  /// until the user prices it or runs Auto Pinch, and no existing listing is touched.
  /// </summary>
  SkipAndTell,
  /// <summary>
  /// Re-price only rows holding an item that is on the user's own Auto-Market list. Cannot touch a listing of
  /// an item they never told us to sell; rows whose item cannot be identified are left alone.
  /// </summary>
  OwnItemsOnly,
}

/// <summary>How a freshly listed item gets its price.</summary>
public enum NewListingPriceMode
{
  /// <summary>List at a placeholder, then run the normal price match (Compare Prices / Universalis) on the new slot.</summary>
  PlaceholderThenMatch,
  /// <summary>Ask Universalis first and list directly at that price; fall back to placeholder-then-match when it has nothing.</summary>
  UniversalisFirst
}

[Serializable]
public sealed class ItemPriceLimit
{
  public uint ItemId { get; set; }

  public int MinPrice { get; set; } = 0;

  public int MaxPrice { get; set; } = 0;

  public int Apply(int price)
  {
    var minPrice = Math.Max(MinPrice, 0);
    var maxPrice = Math.Max(MaxPrice, 0);

    if (minPrice > 0 && price < minPrice)
      price = minPrice;

    if (maxPrice > 0)
    {
      if (minPrice > 0 && maxPrice < minPrice)
        maxPrice = minPrice;

      if (price > maxPrice)
        price = maxPrice;
    }

    return price;
  }
}

/// <summary>One entry on the Auto-Market list: an item you always sell.</summary>
[Serializable]
public sealed class AutoMarketItem
{
  public uint ItemId { get; set; }

  /// <summary>True = this entry covers the HQ variant; false = NQ. HQ and NQ are separate entries.</summary>
  public bool HQ { get; set; }

  public bool Enabled { get; set; } = true;

  /// <summary>Units per listing. 0 = the item's max stack size.</summary>
  public int StackSize { get; set; } = 0;

  /// <summary>Never sell below this many in your bags (0 = sell everything).</summary>
  public int KeepInBags { get; set; } = 0;

  /// <summary>Never sell below this many in the retainer's own inventory (0 = sell everything).</summary>
  public int KeepInRetainer { get; set; } = 0;

  /// <summary>Max listings of this item on ONE retainer at once, counting existing ones. 0 = no cap.</summary>
  public int MaxListingsPerRetainer { get; set; } = 0;

  /// <summary>Per-item stock source override. null = use the global setting.</summary>
  public StockSource? SourceOverride { get; set; }

  /// <summary>Optional per-item fixed price. 0 = use the normal match price.</summary>
  public int FixedPrice { get; set; } = 0;

  public string Key => $"{ItemId}:{(HQ ? "hq" : "nq")}";
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
  /// <summary>
  /// Config schema version. Bump this AND add a step to Plugin.MigrateIfNeeded whenever an existing
  /// config needs changing - a C# field initializer only ever reaches a FRESH config, because Newtonsoft
  /// deserializes the saved value straight over it.
  /// v1 -> v2 (0.1.3.0): AutoMarketPinchAllAfter became opt-in.
  /// </summary>
  public const int CurrentVersion = 2;

  public int Version { get; set; } = CurrentVersion;

  // ----- Price matching (inherited from Dagobert Price Matcher, field names kept for import) -----

  public bool HQ { get; set; } = true;

  public int GetMBPricesDelayMS { get; set; } = 3000;

  public int MarketBoardKeepOpenMS { get; set; } = 1000;

  public bool ShowErrorsInChat { get; set; } = true;

  public bool EnablePinchKey { get; set; } = false;

  public VirtualKey PinchKey { get; set; } = VirtualKey.Q;

  public bool EnablePostPinchkey { get; set; } = true;

  public VirtualKey PostPinchKey { get; set; } = VirtualKey.SHIFT;

  public UndercutMode UndercutMode { get; set; } = UndercutMode.FixedAmount;

  public int DefaultAmount { get; set; } = 0;

  public int UndercutAmount { get; set; } = 0;

  public float MaxUndercutPercentage { get; set; } = 100.0f;

  public bool UndercutSelf { get; set; } = false;

  public bool UseUniversalisDataCenterPrices { get; set; } = false;

  /// <summary>
  /// When a price check finds NOTHING listed on the board, fall back to the median of the recent
  /// data-centre SALES from Universalis instead of giving up. Off by default: it prices from history,
  /// not from a live competitor, so it stays opt-in. See <see cref="SaleHistoryPricing"/>.
  /// A new defaulted property needs no config Version bump - an existing save deserializes it as false.
  /// </summary>
  public bool UseUniversalisSaleHistoryFallback { get; set; } = false;

  /// <summary>
  /// Freshness guard for the above: if the newest sale is older than this many days, the listing is
  /// left at the placeholder with the usual "no board price found" message rather than priced off a
  /// stale data point. (Item 30037's newest data-centre sale is from June 2022.)
  /// </summary>
  public int SaleHistoryMaxAgeDays { get; set; } = SaleHistoryPricing.DefaultMaxAgeDays;

  /// <summary>How many recent sales to ask Universalis for when taking that median.</summary>
  public int SaleHistoryEntryCount { get; set; } = SaleHistoryPricing.DefaultEntryCount;

  public bool ShowPriceAdjustmentsMessages { get; set; } = true;

  public bool ShowRetainerNames { get; set; } = true;

  public List<ulong> SeenRetainers { get; set; } = [];

  public bool ShowInventoryContextMenuEntry { get; set; } = true;

  public List<ItemPriceLimit> ItemPriceLimits { get; set; } = [];

  /// <summary>
  /// Off-by-default price-decision tap. When on, every decision SetNewPrice makes - the writes AND
  /// the writes refused by MaxUndercutPercentage - is written to the plugin log as one
  /// <c>MT|</c> line. Diagnostic only: it changes no pricing behaviour and sends nothing anywhere.
  /// A new defaulted bool needs no config Version bump - an existing save simply deserializes it
  /// as false. See <see cref="MarketTelemetryFormat"/> for the wire format.
  /// </summary>
  public bool DecisionTelemetry { get; set; } = false;

  public const string ALL_DISABLED_SENTINEL = "__ALL_DISABLED__";

  public HashSet<string> EnabledRetainerNames { get; set; } = [];

  public List<string> LastKnownRetainerNames { get; set; } = [];

  // ----- Auto-Market -----

  public List<AutoMarketItem> AutoMarketItems { get; set; } = [];

  /// <summary>Master switch. Off = the Auto Market button and the AutoRetainer hook both do nothing.</summary>
  public bool AutoMarketEnabled { get; set; } = true;

  public StockSource AutoMarketSource { get; set; } = StockSource.BagsAndRetainer;

  public NewListingPriceMode AutoMarketPriceMode { get; set; } = NewListingPriceMode.PlaceholderThenMatch;

  /// <summary>Leave this many of the retainer's 20 market slots empty for manual use.</summary>
  public int AutoMarketReserveSlots { get; set; } = 0;

  /// <summary>Take from the retainer's own inventory before the bags (venture loot first).</summary>
  public bool AutoMarketPreferRetainerStockFirst { get; set; } = true;

  /// <summary>When stock is short of a full stack, list what's there anyway.</summary>
  public bool AutoMarketListPartialStacks { get; set; } = false;

  /// <summary>
  /// After listing, run Auto Pinch over the whole retainer (re-prices old listings too). Off (the default
  /// since 0.1.3.0) = only the slots this run just filled get priced, which is the whole point of listing
  /// and pricing in one pass. Existing configs are moved to false once by the v1 -> v2 migration.
  /// </summary>
  public bool AutoMarketPinchAllAfter { get; set; } = false;

  /// <summary>
  /// What to do when a listing this run created cannot be found on a sell-list row (see PinchFallbackMode).
  /// Ships as KeepFullRepass - the behaviour before 0.1.5.0 - so upgrading changes nothing here on its own.
  /// A later default change must go through the Version/MigrateIfNeeded ladder: a field initializer only ever
  /// reaches a FRESH config, because Newtonsoft deserializes the saved value straight over it.
  /// </summary>
  public PinchFallbackMode AutoMarketPinchFallback { get; set; } = PinchFallbackMode.KeepFullRepass;

  /// <summary>The all-retainers "Auto Pinch" sweep also auto-markets each retainer.</summary>
  public bool AutoMarketInPinchAllSweep { get; set; } = true;

  /// <summary>Run auto-market (then pinch) during AutoRetainer's venture cycle via its postprocess hook.</summary>
  public bool AutoMarketDuringAutoRetainer { get; set; } = false;

  /// <summary>Print one chat line per listing created.</summary>
  public bool ShowAutoMarketMessages { get; set; } = true;

  /// <summary>Placeholder unit price used before the match pass replaces it. Deliberately absurd so a failed match never sells cheap.</summary>
  public int AutoMarketPlaceholderPrice { get; set; } = 999_999_999;

  // ----- Auto-Market value gate + listing order (0.1.11.0) -----
  // New fields with initializers, so an existing config deserializes these defaults as-is: no Version
  // bump and no migration (the ladder is only for CHANGING a default existing installs carry). The
  // gate ships OFF with threshold 0, so updating changes nothing until it is switched on.

  /// <summary>
  /// When on, Auto-Market checks every enabled item against current Universalis prices BEFORE listing
  /// and skips the ones whose total sellable value (board price x sellable quantity, net of the 5%
  /// market fee) is at or under <see cref="AutoMarketValueGateThresholdGil"/>. A held-back item is left
  /// exactly where it is - in the bags or the retainer inventory; nothing is vendored or destroyed.
  /// Stale or missing data always lists the item: uncertainty falls on the reversible side.
  /// </summary>
  public bool AutoMarketValueGateEnabled { get; set; } = false;

  /// <summary>Minimum NET gil an item must be worth to be listed. 0 = the gate never holds anything.</summary>
  public long AutoMarketValueGateThresholdGil { get; set; } = 0;

  /// <summary>Universalis data older than this many hours never holds an item back - the item lists. Clamped 1..168.</summary>
  public int AutoMarketGateFreshnessHours { get; set; } = 6;

  /// <summary>
  /// Which items get the retainer's free market slots when there are not enough for everything.
  /// FastestSellingFirst (the default) ranks by Universalis per-item sale velocity of the rule's own
  /// quality; items with no fresh data keep their list position and sort last.
  /// </summary>
  public MarketSortMode AutoMarketSortMode { get; set; } = MarketSortMode.FastestSellingFirst;

  // ----- Auto Pinch pre-flight (0.1.9.0) -----
  // These are NEW fields with initializers, which is why there is no CurrentVersion bump: Newtonsoft only
  // overwrites a field an existing save actually contains, so a config written by 0.1.7.0 picks these
  // defaults up as-is. The migration ladder is for CHANGING an existing default, which none of these do.

  /// <summary>
  /// Before a full-row pinch pass opens a single context menu, ask Universalis for the whole retainer's
  /// items in one request and skip the rows where the pass would write back the price already on them.
  /// Uncertainty of any kind - no data, stale data, an unreadable row - walks the row as before.
  /// </summary>
  public bool AutoPinchPreflightEnabled { get; set; } = true;

  /// <summary>Universalis data older than this many hours never justifies a skip. Clamped to 1..168.</summary>
  public int AutoPinchPreflightFreshnessHours { get; set; } = 6;

  /// <summary>
  /// Mirror AllaganMarket's green/yellow/red rule in the pre-flight: ignore your OWN retainers' listings
  /// when working out the price to beat, and skip a row that nobody else is undercutting. Inert when
  /// "Undercut my own retainers" is on, because that setting means you want your own listings treated as
  /// competition. New field with an initializer, so an existing config deserializes it without a migration.
  /// </summary>
  public bool AutoPinchMirrorOverlay { get; set; } = true;

  /// <summary>Skip a row whose price would move by fewer than this many gil. 0 = off.</summary>
  public int AutoPinchSkipUnderGil { get; set; } = 0;

  /// <summary>Skip a row whose price would move by less than this percent of its current price. 0 = off.</summary>
  public float AutoPinchSkipUnderPercent { get; set; } = 1.0f;

  /// <summary>
  /// How long (in hours) a price confirmed by a previous Auto Pinch pass's compare window may justify
  /// skipping the same listing in a later pass, while the listing still carries exactly that price.
  /// This is what closes the long-tail gap: slow items nobody uploads to Universalis get walked once,
  /// their compare window confirms the price, and later passes skip them without opening the window
  /// again. 0 turns the memory off - every row is then priced exactly as before this existed.
  /// New field with an initializer, so an existing config deserializes it without a migration.
  /// </summary>
  public int AutoPinchBoardMemoryHours { get; set; } = 12;

  /// <summary>Set once the Dagobert config import has been attempted, so it never runs twice.</summary>
  public bool ImportedFromDagobert { get; set; } = false;

  /// <summary>Newest CHANGELOG version the in-game "What's new" popup has shown (shared LalaChangelog gate).</summary>
  public string? LastSeenChangelogVersion { get; set; }

  public ItemPriceLimit? GetItemPriceLimit(uint itemId)
  {
    return ItemPriceLimits.FirstOrDefault(limit => limit.ItemId == itemId);
  }

  public ItemPriceLimit GetOrAddItemPriceLimit(uint itemId)
  {
    var limit = GetItemPriceLimit(itemId);
    if (limit != null)
      return limit;

    limit = new ItemPriceLimit { ItemId = itemId };
    ItemPriceLimits.Add(limit);
    return limit;
  }

  public AutoMarketItem? GetAutoMarketItem(uint itemId, bool hq)
  {
    return AutoMarketItems.FirstOrDefault(x => x.ItemId == itemId && x.HQ == hq);
  }

  public AutoMarketItem GetOrAddAutoMarketItem(uint itemId, bool hq)
  {
    var entry = GetAutoMarketItem(itemId, hq);
    if (entry != null)
      return entry;

    entry = new AutoMarketItem { ItemId = itemId, HQ = hq };
    AutoMarketItems.Add(entry);
    return entry;
  }

  public void Save()
  {
    Plugin.PluginInterface.SavePluginConfig(this);
  }
}
