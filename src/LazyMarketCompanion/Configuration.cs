using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion;

public enum UndercutMode
{
  FixedAmount,
  Percentage
}

/// <summary>Where auto-market may take stock from.</summary>
public enum StockSource
{
  BagsOnly,
  RetainerOnly,
  BagsAndRetainer
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
  public int Version { get; set; } = 1;

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

  public bool ShowPriceAdjustmentsMessages { get; set; } = true;

  public bool ShowRetainerNames { get; set; } = true;

  public List<ulong> SeenRetainers { get; set; } = [];

  public bool ShowInventoryContextMenuEntry { get; set; } = true;

  public List<ItemPriceLimit> ItemPriceLimits { get; set; } = [];

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

  /// <summary>After listing, run Auto Pinch over the whole retainer (re-prices old listings too). Off = only the new slots get priced.</summary>
  public bool AutoMarketPinchAllAfter { get; set; } = true;

  /// <summary>The all-retainers "Auto Pinch" sweep also auto-markets each retainer.</summary>
  public bool AutoMarketInPinchAllSweep { get; set; } = true;

  /// <summary>Run auto-market (then pinch) during AutoRetainer's venture cycle via its postprocess hook.</summary>
  public bool AutoMarketDuringAutoRetainer { get; set; } = false;

  /// <summary>Print one chat line per listing created.</summary>
  public bool ShowAutoMarketMessages { get; set; } = true;

  /// <summary>Placeholder unit price used before the match pass replaces it. Deliberately absurd so a failed match never sells cheap.</summary>
  public int AutoMarketPlaceholderPrice { get; set; } = 999_999_999;

  /// <summary>Set once the Dagobert config import has been attempted, so it never runs twice.</summary>
  public bool ImportedFromDagobert { get; set; } = false;

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
