using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

/// <summary>
/// Game-side half of auto-market: snapshots bags + crystals / retainer pages + retainer crystals / the market container,
/// asks the planner what to list, and issues the InventoryManager calls. Must run on the
/// framework thread inside an open retainer session (RetainerSellList or the retainer menu).
/// </summary>
internal static unsafe class AutoMarketService
{
  public const int MarketSlotCount = 20;

  // Shards / crystals / clusters do not live in Inventory1-4: the player's are in Crystals (2001) and the
  // retainer's in RetainerCrystals (12001). Both are valid MoveToRetainerMarket sources (the vanilla Sell UI's
  // crystals tab moves from the same containers), so they are part of the stock snapshot.
  private static readonly InventoryType[] BagTypes =
  [
    InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
    InventoryType.Crystals,
  ];

  private static readonly InventoryType[] RetainerTypes =
  [
    InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4,
    InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7,
    InventoryType.RetainerCrystals,
  ];

  public static bool IsMarketContainerLoaded()
  {
    var manager = InventoryManager.Instance();
    if (manager == null) return false;
    var container = manager->GetInventoryContainer(InventoryType.RetainerMarket);
    return container != null && container->IsLoaded;
  }

  /// <summary>
  /// Vendored-item count for this run (0.1.12.0: the vendor leg increments it; the done line reports it). Game-side
  /// only; the decision itself lives in <see cref="MarketGate"/> and is harness-covered there.
  /// </summary>
  internal static int GateHeldThisRun { get; private set; }

  /// <summary>
  /// Rules the gate sent to the retainer-vendor leg this run (0.1.12.0). Used by
  /// <see cref="MarketAutomation.BuildVendoringSteps"/> to plan the vendor ops and by the end-of-run
  /// chat line to say how many were vendored vs listed.
  /// </summary>
  internal static List<ItemRule> HeldBackRules { get; } = new();

  internal static void ResetGateHeld()
  {
    GateHeldThisRun = 0;
    HeldBackRules.Clear();
  }

  /// <summary>
  /// Every enabled Auto-Market item id - what the gate's one Universalis request asks about. Cheaper
  /// than building the full rule list (no Item-sheet lookups) and enough for the fetch.
  /// </summary>
  public static List<uint> GateItemIds()
  {
    var config = Plugin.Configuration;
    var ids = new List<uint>();
    foreach (var entry in config.AutoMarketItems)
    {
      if (entry.Enabled && !ids.Contains(entry.ItemId))
        ids.Add(entry.ItemId);
    }
    return ids;
  }

  public static PlanResult BuildPlan(Dictionary<uint, ItemQuote>? gateQuotes = null)
  {
    var config = Plugin.Configuration;
    var items = Svc.Data.GetExcelSheet<Item>();

    var rules = new List<ItemRule>();
    foreach (var entry in config.AutoMarketItems.Where(x => x.Enabled))
    {
      if (!items.TryGetRow(entry.ItemId, out var row))
        continue;

      var maxStack = (int)Math.Max(row.StackSize, 1u);
      var stackSize = entry.StackSize > 0 ? Math.Min(entry.StackSize, maxStack) : maxStack;
      var source = entry.SourceOverride ?? config.AutoMarketSource;

      rules.Add(new ItemRule(
        entry.ItemId,
        entry.HQ,
        stackSize,
        entry.KeepInBags,
        entry.KeepInRetainer,
        entry.MaxListingsPerRetainer,
        source is StockSource.BagsOnly or StockSource.BagsAndRetainer,
        source is StockSource.RetainerOnly or StockSource.BagsAndRetainer,
        entry.FixedPrice,
        maxStack));
    }

    var stock = SnapshotStock();
    var market = SnapshotMarket();

    // 0.1.11.0 value gate + listing order. Both run BEFORE the planner hands out the retainer's free
    // market slots, so an item the gate holds back cannot consume a slot another item could have used,
    // and the sort decides which items get the slots when there are not enough for everything. A null
    // quote map (request failed or neither feature is on) leaves both alone: every item lists, in list
    // order, exactly as before 0.1.11.0.
    var gated = ApplyValueGate(rules, stock, gateQuotes);
    rules = gated;

    var options = new PlannerOptions(MarketSlotCount, config.AutoMarketReserveSlots, config.AutoMarketPreferRetainerStockFirst, config.AutoMarketListPartialStacks);
    return AutoMarketPlanner.Plan(rules, stock, market, options);
  }

  /// <summary>
  /// The gate + sort half of 0.1.11.0 (the rules themselves live in <see cref="MarketGate"/>, where the
  /// harness covers them). Gate first - a held item is out entirely - then the sort reorders the
  /// survivors so the scarce free slots go to the items that deserve them. The quote map is fetched by
  /// the caller's task chain (MarketAutomation.StartGateLookup) so nothing blocks the framework thread.
  /// </summary>
  private static List<ItemRule> ApplyValueGate(List<ItemRule> rules, List<StockStack> stock, Dictionary<uint, ItemQuote>? quotes)
  {
    var config = Plugin.Configuration;
    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var freshnessMs = (long)Math.Clamp(config.AutoMarketGateFreshnessHours, 1, 168) * 3_600_000L;

    List<ItemRule> kept = rules;
    var vendored = new List<ItemRule>();
    if (config.AutoMarketValueGateEnabled)
    {
      var gateOptions = new GateOptions(true, Math.Max(config.AutoMarketValueGateThresholdGil, 0), freshnessMs);
      kept = new List<ItemRule>(rules.Count);
      var held = new List<string>();

      foreach (var rule in rules)
      {
        ItemQuote? quote = null;
        quotes?.TryGetValue(rule.ItemId, out quote);
        var sellable = MarketGate.PotentialSellable(rule, stock, config.AutoMarketListPartialStacks);
        var verdict = MarketGate.Decide(sellable, quote, rule.HQ, config.HQ, gateOptions, now);
        if (verdict == GateVerdict.List)
        {
          kept.Add(rule);
          continue;
        }

        // 0.1.12.0: the request SUCCEEDED and priced this item at or under the threshold, so the
        // below-threshold vendor leg takes it - "Have Retainer Sell Items", no market slot, no fee.
        // The old 0.1.11.0 "hold it" is gone; only an uncertainty (request failed/null below) still holds.
        if (verdict == GateVerdict.Vendor)
        {
          vendored.Add(rule);
          continue;
        }

        // Impossible with the current Decide (uncertainty returns List), kept as the polarity pin:
        // a HoldBack verdict that reaches the priced gate is a data bug, not a sell decision.
        kept.Add(rule);
      }

      if (vendored.Count > 0)
      {
        GateHeldThisRun += vendored.Count;
        HeldBackRules.AddRange(vendored);
        var names = string.Join(", ", vendored.Select(r => r.ItemId.ToString() + (r.HQ ? " HQ" : "")));
        Svc.Log.Information($"[LMC] gate: vendoring {vendored.Count} item(s) at the retainer (at or under {gateOptions.ThresholdGil:N0} gil net): {names}");
        if (Plugin.Configuration.ShowAutoMarketMessages)
          Communicator.PrintInfo($"value gate: vendoring {vendored.Count} item(s) at the retainer (at or under {gateOptions.ThresholdGil:N0} gil net): {names}");
      }
      else if (kept.Count > 0)
      {
        Svc.Log.Information($"[LMC] gate: every item is above the {gateOptions.ThresholdGil:N0} gil net threshold");
      }
    }

    // The sort rides on the same fetch whenever a data-backed mode is selected, gate or no gate.
    var ordered = MarketGate.SortRules(kept, MarketGate.RuleQuotes(kept, quotes, config.HQ, now, freshnessMs), config.AutoMarketSortMode);
    if (config.AutoMarketSortMode != MarketSortMode.ListOrder && ordered.Count > 1)
      Svc.Log.Information($"[LMC] gate: listing order ({DescribeSort(config.AutoMarketSortMode)}): {string.Join(", ", ordered.Select(r => $"{r.ItemId}{(r.HQ ? " HQ" : "")}"))}");

    return ordered;
  }

  private static string DescribeSort(MarketSortMode mode) => mode switch
  {
    MarketSortMode.CheapestFirst => "cheapest first",
    MarketSortMode.MostExpensiveFirst => "most expensive first",
    MarketSortMode.FastestSellingFirst => "fastest selling first",
    _ => "list order",
  };

  public static List<StockStack> SnapshotStock()
  {
    var result = new List<StockStack>();
    var manager = InventoryManager.Instance();
    if (manager == null) return result;

    foreach (var type in BagTypes)
      Snapshot(manager, type, StockOrigin.Bags, result);
    foreach (var type in RetainerTypes)
      Snapshot(manager, type, StockOrigin.Retainer, result);

    return result;
  }

  private static void Snapshot(InventoryManager* manager, InventoryType type, StockOrigin origin, List<StockStack> into)
  {
    var container = manager->GetInventoryContainer(type);
    if (container == null || !container->IsLoaded) return;

    for (var i = 0; i < container->Size; i++)
    {
      var item = container->GetInventorySlot(i);
      if (item == null || item->ItemId == 0 || item->Quantity <= 0) continue;
      if (item->Flags.HasFlag(InventoryItem.ItemFlags.Collectable)) continue;

      into.Add(new StockStack(origin, (int)type, i, item->ItemId, item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality), item->Quantity));
    }
  }

  public static List<MarketSlot> SnapshotMarket()
  {
    var result = new List<MarketSlot>();
    var manager = InventoryManager.Instance();
    if (manager == null) return result;

    var container = manager->GetInventoryContainer(InventoryType.RetainerMarket);
    if (container == null || !container->IsLoaded) return result;

    for (var i = 0; i < Math.Min(container->Size, MarketSlotCount); i++)
    {
      var item = container->GetInventorySlot(i);
      if (item == null)
      {
        result.Add(new MarketSlot(i, 0, false, 0));
        continue;
      }
      result.Add(new MarketSlot(i, item->ItemId, item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality), item->Quantity));
    }

    return result;
  }

  /// <summary>Issue the listing call. Returns false if the source stack no longer matches the plan (stock moved).</summary>
  public static bool Execute(ListingOp op, uint unitPrice)
  {
    var manager = InventoryManager.Instance();
    if (manager == null) return false;

    var source = manager->GetInventorySlot((InventoryType)op.SourceContainer, op.SourceSlot);
    if (source == null || source->ItemId != op.ItemId || source->Quantity < op.Quantity)
    {
      Svc.Log.Warning($"[LMC] source {(InventoryType)op.SourceContainer}#{op.SourceSlot} changed since planning; skipping {op.ItemId} x{op.Quantity}");
      return false;
    }

    var market = manager->GetInventoryContainer(InventoryType.RetainerMarket);
    if (market == null || !market->IsLoaded) return false;
    var target = market->GetInventorySlot(op.TargetSlot);
    if (target != null && target->ItemId != 0)
    {
      Svc.Log.Warning($"[LMC] market slot {op.TargetSlot} is no longer empty; skipping {op.ItemId} x{op.Quantity}");
      return false;
    }

    // Last line of defence: the server answers an oversize listing by dropping the connection, not with an error
    // (4854 HQ x297 on 2026-09-05). The planner already clamps; this catches any op that reaches here another way.
    var cap = MarketListingCap.For(ItemMaxStack(op.ItemId));
    if (op.Quantity > cap)
    {
      Svc.Log.Error($"[LMC] refusing to list {op.ItemId}{(op.HQ ? " HQ" : "")} x{op.Quantity}: the market accepts at most {cap} per listing (would disconnect)");
      return false;
    }

    Svc.Log.Information($"[LMC] MoveToRetainerMarket {(InventoryType)op.SourceContainer}#{op.SourceSlot} -> market#{op.TargetSlot} item={op.ItemId}{(op.HQ ? " HQ" : "")} qty={op.Quantity} price={unitPrice}");
    manager->MoveToRetainerMarket((InventoryType)op.SourceContainer, (ushort)op.SourceSlot, InventoryType.RetainerMarket, (ushort)op.TargetSlot, (uint)op.Quantity, unitPrice);
    return true;
  }

  private static int ItemMaxStack(uint itemId)
  {
    var items = Svc.Data.GetExcelSheet<Item>();
    return items.TryGetRow(itemId, out var row) ? (int)Math.Max(row.StackSize, 1u) : 999;
  }

  /// <summary>True once the server has populated the target slot with the expected item.</summary>
  public static bool IsListed(ListingOp op)
  {
    var manager = InventoryManager.Instance();
    if (manager == null) return false;
    var market = manager->GetInventoryContainer(InventoryType.RetainerMarket);
    if (market == null || !market->IsLoaded) return false;
    var slot = market->GetInventorySlot(op.TargetSlot);
    return slot != null && slot->ItemId == op.ItemId;
  }

  /// <summary>
  /// Unit price the client currently holds for one of the retainer's 20 market slots, or 0 when it cannot
  /// be read. Used to prove a freshly listed slot did not stay at the Auto-Market placeholder price.
  /// </summary>
  public static ulong MarketPrice(int slot)
  {
    if (slot is < 0 or >= MarketSlotCount) return 0;
    var manager = InventoryManager.Instance();
    if (manager == null) return 0;
    var container = manager->GetInventoryContainer(InventoryType.RetainerMarket);
    if (container == null || !container->IsLoaded) return 0;
    return manager->GetRetainerMarketPrice((short)slot);
  }

  /// <summary>
  /// Unit price of every occupied market slot, keyed by slot. Empty when the container is not loaded, which
  /// callers must treat as "cannot tell" rather than as "nothing is at the placeholder price".
  ///
  /// This is the primary way Auto-Market recognises its own new listings: they are born at
  /// <c>AutoMarketPlaceholderPrice</c>, and no listing the user made by hand ever carries that price. It
  /// reads the container, so it has none of the failure modes of reading the sell list's text - not
  /// virtualisation, not a clipped label, not the list's sort order.
  /// </summary>
  public static Dictionary<int, ulong> MarketPricesBySlot()
  {
    var result = new Dictionary<int, ulong>();
    var manager = InventoryManager.Instance();
    if (manager == null) return result;

    var container = manager->GetInventoryContainer(InventoryType.RetainerMarket);
    if (container == null || !container->IsLoaded) return result;

    for (var i = 0; i < Math.Min(container->Size, MarketSlotCount); i++)
    {
      var item = container->GetInventorySlot(i);
      if (item == null || item->ItemId == 0) continue;
      result[i] = manager->GetRetainerMarketPrice((short)i);
    }

    return result;
  }

  public static int OccupiedSlotCount() => SnapshotMarket().Count(m => m.ItemId != 0);

  public static int RetainerMarketItemCount()
  {
    var rm = RetainerManager.Instance();
    if (rm == null) return -1;
    var active = rm->GetActiveRetainer();
    return active == null ? -1 : active->MarketItemCount;
  }

  /// <summary>
  /// The client's vendor prices for one item, off the Item sheet. These are the numbers the sell UI
  /// autofills as a new listing's default price, so the vendoring estimate is the client's own
  /// arithmetic. priceLow is the honest per-unit vendor price; priceMid exists for the estimate line
  /// on HQ stock with "Use HQ prices" on. Zero when the row is missing (the gate treats that as
  /// unpriceable and the item stays where it is).
  /// </summary>
  public static (uint PriceMid, uint PriceLow) VendorPrices(uint itemId)
  {
    var items = Svc.Data.GetExcelSheet<Item>();
    if (items == null || !items.TryGetRow(itemId, out var row))
      return (0, 0);
    return (row.PriceMid, row.PriceLow);
  }

  /// <summary>
  /// Vendoring one stack through the retainer (0.1.12.0). The call is FFXIVClientStructs
  /// AgentRetainerItemCommandModule + RetainerItemCommand.HaveRetainerSellItem, exactly how
  /// AutoRetainer's InventorySpaceManager.SafeSellSlot drives the retainer's item menu headlessly
  /// (its own enum member carries the same value, 5 = "Have Retainer Sell Items"); the session
  /// precondition is the same one Auto-Market already satisfies - AgentRetainer active with the
  /// retainer inventory loaded. The slot is RE-READ right before the call and every mismatch aborts
  /// it, so a plan built on a stale snapshot can never vendor the wrong thing.
  /// </summary>
  public unsafe static bool ExecuteVendor(VendorOp op)
  {
    var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer);
    if (agent == null || !agent->IsAgentActive())
    {
      Svc.Log.Warning($"[LMC] vendor: agent retainer is not active, skipping item {op.ItemId} slot {op.Container}:{op.Slot}");
      return false;
    }

    var loaded = GenericHelpers.TryGetAddonByName<AtkUnitBase>("InventoryRetainer", out _) ||
                 GenericHelpers.TryGetAddonByName<AtkUnitBase>("InventoryRetainerLarge", out _);
    if (!loaded)
    {
      Svc.Log.Warning($"[LMC] vendor: retainer inventory panel not open, skipping item {op.ItemId} slot {op.Container}:{op.Slot}");
      return false;
    }

    // RE-READ the slot immediately before firing. The plan was built from a snapshot; if anything
    // about this slot moved since, abort rather than sell something we did not judge.
    var manager = InventoryManager.Instance();
    var container = manager == null ? null : manager->GetInventoryContainer((InventoryType)op.Container);
    var slot = container == null ? null : container->GetInventorySlot(op.Slot);
    if (slot == null || slot->ItemId != op.ItemId || slot->Quantity < op.Quantity)
    {
      var had = slot == null ? 0u : slot->ItemId;
      var hadQty = slot == null ? 0 : (int)slot->Quantity;
      Svc.Log.Warning($"[LMC] vendor: slot {op.Container}:{op.Slot} changed since planning (had {had} x{hadQty}, planned {op.ItemId} x{op.Quantity}); skipping");
      return false;
    }

    var module = (nint)AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer) + 40;
    Plugin.RetainerItemCommand(module, (uint)op.Slot, (InventoryType)op.Container, 0, RetainerItemCommand.HaveRetainerSellItem);
    Svc.Log.Information($"[LMC] vendored {(InventoryType)op.Container}:{op.Slot} item {op.ItemId}{(op.HQ ? " HQ" : "")} x{op.Quantity} (est {op.EstGil:N0} gil)");
    return true;
  }
}
