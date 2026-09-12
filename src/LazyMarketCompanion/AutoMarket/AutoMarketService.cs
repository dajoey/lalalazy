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
    // 0.1.19.0: ask only about items with stock Auto-Market could actually sell
    // (MarketGate.GateFetchIds, judged with the same PotentialSellable arithmetic as the verdict
    // itself). The old body returned every enabled id - 243 on the live install - and one
    // 243-id request is exactly the shape Universalis 504s under load (measured 2026-09-07: the
    // giant request died at the gateway on 9 of 10 sweeps while a 30-id request answered in
    // 5.8 s). An id with no sellable stock can never be judged below-threshold anyway, so the
    // trim loses nothing and keeps the request in the size class the API answers reliably.
    var rules = BuildEnabledRules();
    // 0.1.32.0: also ask about items currently LISTED under an enabled rule (the pull pass) - see
    // MarketGate.GateFetchIds' own remarks for why a listed-only item would otherwise never get
    // a quote at all.
    return MarketGate.GateFetchIds(rules, SnapshotStock(), Plugin.Configuration.AutoMarketListPartialStacks, SnapshotMarket());
  }

  /// <summary>
  /// Builds the resolved ItemRule list from the enabled config entries (Item-sheet stack-size
  /// lookup, source resolution). Extracted 0.1.32.0 - GateItemIds and BuildPlan each had their own
  /// copy of this loop; a change to one and not the other was one edit away from happening.
  /// </summary>
  internal static List<ItemRule> BuildEnabledRules()
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
    return rules;
  }

  public static PlanResult BuildPlan(Dictionary<uint, ItemQuote>? gateQuotes = null)
  {
    var config = Plugin.Configuration;
    var rules = BuildEnabledRules();

    // Category routing (0.1.37.0): filter to what THIS retainer is eligible for BEFORE the value gate,
    // so a routed-elsewhere item is never listed here and never vendored here either - it is left
    // exactly where it is, same as an item with no stock in an enabled source. Runs before the value
    // gate deliberately: both listing and vendoring stem from the same filtered rule list. An unreadable
    // retainer name (not inside a retainer session) makes every mapped rule's RetainerName comparison
    // false, which routes every category-mapped item away from an unknown retainer - the same fail
    // direction as routing to the wrong retainer, never a crash.
    if (config.CategoryRetainerRules.Count > 0)
      rules = ApplyCategoryRouting(rules, config.CategoryRetainerRules);

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
  /// Name of the retainer whose sell list is currently open (RetainerManager.GetActiveRetainer, the same
  /// call RetainerMarketItemCount already uses), or "" when no retainer is active. Read fresh at the
  /// point of use rather than plumbed through as a parameter - every Auto-Market entry point (the sweep,
  /// the current-retainer button, and the AutoRetainer postprocess hook) already has an open retainer
  /// session by the time BuildPlan runs, so the game itself is the one source of truth for "which
  /// retainer is this".
  /// </summary>
  public static string CurrentRetainerName()
  {
    var rm = RetainerManager.Instance();
    if (rm == null) return string.Empty;
    var active = rm->GetActiveRetainer();
    return active == null ? string.Empty : active->NameString;
  }

  /// <summary>
  /// Filters an enabled-rule list down to what the CURRENTLY OPEN retainer is eligible for, per
  /// CategoryRouter.FilterForRetainer. Builds the per-item category/marketable/exclude lookups the
  /// Dalamud-free filter needs from the Item sheet and the config, once per call.
  /// </summary>
  private static List<ItemRule> ApplyCategoryRouting(List<ItemRule> rules, List<CategoryRetainerRule> categoryRules)
  {
    var retainerName = CurrentRetainerName();
    var items = Svc.Data.GetExcelSheet<Item>();
    var categoryByKey = new Dictionary<string, ItemCategoryInfo>(rules.Count);
    var excludeByKey = new Dictionary<string, bool>(rules.Count);

    foreach (var rule in rules)
    {
      var key = $"{rule.ItemId}:{(rule.HQ ? "hq" : "nq")}";
      var entry = Plugin.Configuration.GetAutoMarketItem(rule.ItemId, rule.HQ);
      excludeByKey[key] = entry?.ExcludeFromCategoryRouting ?? false;

      if (items.TryGetRow(rule.ItemId, out var row))
        categoryByKey[key] = new ItemCategoryInfo(rule.ItemId, rule.HQ, row.ItemSearchCategory.RowId, row.ItemSearchCategory.RowId != 0 && !row.IsUntradable);
      // else: no entry, CategoryRouter.FilterForRetainer fails open on the missing key.
    }

    return CategoryRouter.FilterForRetainer(rules, categoryByKey, excludeByKey, categoryRules, retainerName);
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
    var unvendorable = new List<ItemRule>();
    if (config.AutoMarketValueGateEnabled)
    {
      var gateOptions = new GateOptions(true, Math.Max(config.AutoMarketValueGateThresholdGil, 0), freshnessMs);
      kept = new List<ItemRule>(rules.Count);
      var belowThreshold = new List<ItemRule>();

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
          belowThreshold.Add(rule);
          continue;
        }

        // Impossible with the current Decide (uncertainty returns List), kept as the polarity pin:
        // a HoldBack verdict that reaches the priced gate is a data bug, not a sell decision.
        kept.Add(rule);
      }

      // 0.1.30.0: the Item-sheet lookup runs HERE, before the announce. A below-threshold item the
      // sheet prices at 0 can never be vendored, so announcing it as a vendor target was wrong
      // before the sweep even started: Ice Crystal (item 9, PriceLow 0) was named by
      // "gate: vendoring N item(s)" on essentially every sweep and then named-skipped downstream by
      // VendorPlanner's own guard, and on 2026-09-08 22:25 that is exactly why "vendoring 5" ended
      // as "4 vendored". The split uses the planner's own predicate (ItemVendorPrice.Vendorable),
      // so the announced set and the set the leg attempts are the same set by construction. The
      // unvendorable rules keep the outcome they already had - left in place, never listed (they
      // are below the threshold) and never vendored - they are only named honestly now, and they
      // are deliberately NOT added to HeldBackRules, which is the vendor leg's input.
      var split = VendorPlanner.SplitVendorable(belowThreshold, id => VendorPrices(id).PriceLow);
      vendored = split.Sellable.ToList();
      unvendorable = split.Unvendorable.ToList();

      if (vendored.Count > 0)
      {
        GateHeldThisRun += vendored.Count;
        HeldBackRules.AddRange(vendored);
        var names = string.Join(", ", vendored.Select(r => r.ItemId.ToString() + (r.HQ ? " HQ" : "")));
        Svc.Log.Information($"[LMC] gate: vendoring {vendored.Count} item(s) at the retainer (at or under {gateOptions.ThresholdGil:N0} gil net): {names}");
        if (Plugin.Configuration.ShowAutoMarketMessages)
          Communicator.PrintInfo($"value gate: vendoring {vendored.Count} item(s) at the retainer (at or under {gateOptions.ThresholdGil:N0} gil net): {names}");
      }
      if (unvendorable.Count > 0)
      {
        var names = string.Join(", ", unvendorable.Select(r => r.ItemId.ToString() + (r.HQ ? " HQ" : "")));
        Svc.Log.Information($"[LMC] gate: {unvendorable.Count} item(s) below the {gateOptions.ThresholdGil:N0} gil net threshold have no Item-sheet vendor price, so they are not vendor candidates; left in place, not listed: {names}");
        if (Plugin.Configuration.ShowAutoMarketMessages)
          Communicator.PrintInfo($"value gate: {unvendorable.Count} item(s) below the threshold cannot be vendored (no vendor price); left in place, not listed: {names}");
      }

      // The clean / no-data sight announce describes a sweep the gate held NOTHING back on, so it
      // must stay out of the way of BOTH held sets. Before 0.1.30.0 only the vendored set gated it;
      // once item 9 moved to the unvendorable set, an unchanged "else" here would have printed
      // "every item is above the threshold" on a sweep that had just held one back - trading one
      // honesty wart for another.
      if (vendored.Count == 0 && unvendorable.Count == 0 && kept.Count > 0)
      {
        // 0.1.19.0: the old line printed identically whether every item was JUDGED above the
        // threshold or NOTHING was judged at all (failed request -> null quotes -> every rule
        // unpriceable -> every rule lists). On the 2026-09-07 runs the fetch 504'd and this same
        // line announced a check that never happened while below-threshold stock listed blind.
        // Fully-judged keeps the old wording; anything less says how many items the threshold
        // was NOT checked for.
        // 0.1.23.0: the sight count and both announce branches are judged over the STOCKED set -
        // the same rules GateFetchIds asked Universalis about - with the fully-stocked count in
        // the log line so a degenerate "every list entry has nothing to sell" sweep is still
        // visible and auditable. The 0.1.19.0 whole-list count made every no-stock rule read
        // unpriceable (no quote is ever fetched for it), so on the live config (~257 entries,
        // ~25 stocked) the no-data warning printed on EVERY sweep and the clean
        // "every item is above the threshold" line was unreachable.
        var stockedRules = rules.Where(r => MarketGate.PotentialSellable(r, stock, config.AutoMarketListPartialStacks) > 0).ToList();
        var sight = MarketGate.CountSight(rules, quotes, config.HQ, now, freshnessMs, stock, config.AutoMarketListPartialStacks);
        if (sight.Unpriceable == 0)
          Svc.Log.Information($"[LMC] gate: every item is above the {gateOptions.ThresholdGil:N0} gil net threshold (checked {sight.Judged} of {rules.Count} enabled item(s), {stockedRules.Count} with stock)");
        else
        {
          Svc.Log.Warning($"[LMC] gate: no price data for {sight.Unpriceable} of {sight.Judged + sight.Unpriceable} item(s) with stock to sell ({stockedRules.Count} of {rules.Count} enabled item(s) have stock) - the {gateOptions.ThresholdGil:N0} gil net threshold was NOT checked for those; they list (uncertainty lists, never vendors)");
          if (Plugin.Configuration.ShowAutoMarketMessages)
            Communicator.PrintInfo($"value gate: no price data for {sight.Unpriceable} of {sight.Judged + sight.Unpriceable} item(s) with stock; they will list unchecked - vendoring still only fires on a confirmed price");
        }
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
  /// True when the retainer inventory panel (InventoryRetainer or the large variant) is open and ready.
  /// This panel is a SEPARATE UI from the sell list (RetainerSellList): the Auto-Market flow only opens
  /// the latter, which is why the 0.1.12.0 vendor leg - gated on this panel - skipped every op.
  /// </summary>
  public static bool IsRetainerInventoryOpen()
  {
    return (GenericHelpers.TryGetAddonByName<AtkUnitBase>("InventoryRetainer", out var a) && GenericHelpers.IsAddonReady(a))
        || (GenericHelpers.TryGetAddonByName<AtkUnitBase>("InventoryRetainerLarge", out var b) && GenericHelpers.IsAddonReady(b));
  }

  /// <summary>
  /// Vendoring one stack through the retainer (0.1.12.0, reworked 0.1.15.0). The call is FFXIVClientStructs
  /// AgentRetainerItemCommandModule + RetainerItemCommand.HaveRetainerSellItem, exactly how
  /// AutoRetainer's InventorySpaceManager.SafeSellSlot drives the retainer's item menu headlessly
  /// (its own enum member carries the same value, 5 = "Have Retainer Sell Items").
  ///
  /// Preconditions, in AutoRetainer's own order (TaskVendorItems.Enqueue -> WaitUntilInventoryLoaded ->
  /// SafeSellSlot): AgentRetainer active, the retainer inventory PANEL open (InventoryRetainer /
  /// InventoryRetainerLarge - a different UI from the sell list; the 0.1.12.0 build wrongly assumed the
  /// sell-list session satisfied this and skipped every op), and the slot re-read. op.Container must be
  /// a real game InventoryType (0.1.12.0 planned from the StockOrigin enum instead, which addressed the
  /// wrong containers); the raw value is logged by NAME so this class of defect can never hide behind
  /// a numeric log line again.
  /// </summary>
  public unsafe static bool ExecuteVendor(VendorOp op)
  {
    if (!op.HasKnownContainer)
    {
      Svc.Log.Warning($"[LMC] vendor: op carries unknown container id {op.Container}; refusing (planner bug, item {op.ItemId} slot {op.Slot} untouched)");
      return false;
    }

    var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer);
    if (agent == null || !agent->IsAgentActive())
    {
      Svc.Log.Warning($"[LMC] vendor: agent retainer is not active, skipping item {op.ItemId} slot {op.ContainerName()}:{op.Slot}");
      return false;
    }

    // The panel precondition, now ENFORCED upstream as queued steps (OpenRetainerInventory /
    // WaitRetainerInventory) - this is the last-line check, not the only line.
    if (!IsRetainerInventoryOpen())
    {
      Svc.Log.Warning($"[LMC] vendor: retainer inventory panel not open, skipping item {op.ItemId} slot {op.ContainerName()}:{op.Slot}");
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
      Svc.Log.Warning($"[LMC] vendor: slot {op.ContainerName()}:{op.Slot} changed since planning (had {had} x{hadQty}, planned {op.ItemId} x{op.Quantity}); skipping");
      return false;
    }

    var module = (nint)AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer) + 40;
    Plugin.RetainerItemCommand(module, (uint)op.Slot, (InventoryType)op.Container, 0, RetainerItemCommand.HaveRetainerSellItem);
    Svc.Log.Information($"[LMC] vendored {(InventoryType)op.Container}:{op.Slot} item {op.ItemId}{(op.HQ ? " HQ" : "")} x{op.Quantity} (est {op.EstGil:N0} gil)");
    return true;
  }

  // =====================================================================================
  // Pull pass (0.1.32.0, t_4d12b8b0): pulling a below-threshold LISTING back into inventory so the
  // unchanged vendor leg below can sell it. See AutoMarket/MarketPull.cs for the decision table.
  // =====================================================================================

  /// <summary>
  /// Builds this retainer's pull plan. Occupied market slots under an enabled rule whose gate
  /// verdict, judged fresh right now with the SAME MarketGate.Decide the stocked path uses, is
  /// Vendor. Called from MarketAutomation.BuildListingStepsNow BEFORE the stock snapshot/plan/
  /// vendor leg, using the gate's own quotes - the fetch already asked about listed items (see
  /// MarketGate.GateFetchIds). Value-gate-off configs get an empty plan: the pull pass exists only
  /// to feed the gate, and a config with the gate off has no threshold to judge a listing against.
  /// </summary>
  public static PullPlan PlanPulls(Dictionary<uint, ItemQuote>? gateQuotes)
  {
    var config = Plugin.Configuration;
    if (!config.AutoMarketValueGateEnabled)
      return new PullPlan(new List<PullOp>(), new List<string>(), false);

    var rules = BuildEnabledRules();
    var market = SnapshotMarket();
    var prices = MarketPricesBySlot();
    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var freshnessMs = (long)Math.Clamp(config.AutoMarketGateFreshnessHours, 1, 168) * 3_600_000L;
    var gateOptions = new GateOptions(true, Math.Max(config.AutoMarketValueGateThresholdGil, 0), freshnessMs);

    return MarketPull.Plan(market, prices, rules, gateQuotes, gateOptions, config.HQ, now,
      RetainerPageFreeSlot, HasFreeBagSlot);
  }

  /// <summary>How many empty slots exist across the retainer's 7 inventory pages right now.</summary>
  public static int RetainerPageFreeSlot()
  {
    var manager = InventoryManager.Instance();
    if (manager == null) return 0;
    var free = 0;
    foreach (var type in RetainerPageTypes)
    {
      var container = manager->GetInventoryContainer(type);
      if (container == null || !container->IsLoaded) continue;
      for (var i = 0; i < container->Size; i++)
      {
        var item = container->GetInventorySlot(i);
        if (item == null || item->ItemId == 0) free++;
      }
    }
    return free;
  }

  /// <summary>True when the player's own bags (Inventory1-4) have at least one empty slot - the
  /// one-shot fallback destination for a pull when every retainer page is full.</summary>
  public static bool HasFreeBagSlot()
  {
    var manager = InventoryManager.Instance();
    if (manager == null) return false;
    foreach (var type in PlayerBagTypes)
    {
      var container = manager->GetInventoryContainer(type);
      if (container == null || !container->IsLoaded) continue;
      for (var i = 0; i < container->Size; i++)
      {
        var item = container->GetInventorySlot(i);
        if (item == null || item->ItemId == 0) return true;
      }
    }
    return false;
  }

  private static readonly InventoryType[] RetainerPageTypes =
  [
    InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4,
    InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7,
  ];

  private static readonly InventoryType[] PlayerBagTypes =
  [
    InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
  ];

  /// <summary>Human-readable name for a container type, for the pull log lines.</summary>
  public static string NameOfContainer(InventoryType type) => type switch
  {
    InventoryType.RetainerPage1 => "RetainerPage1",
    InventoryType.RetainerPage2 => "RetainerPage2",
    InventoryType.RetainerPage3 => "RetainerPage3",
    InventoryType.RetainerPage4 => "RetainerPage4",
    InventoryType.RetainerPage5 => "RetainerPage5",
    InventoryType.RetainerPage6 => "RetainerPage6",
    InventoryType.RetainerPage7 => "RetainerPage7",
    InventoryType.Inventory1 => "Inventory1",
    InventoryType.Inventory2 => "Inventory2",
    InventoryType.Inventory3 => "Inventory3",
    InventoryType.Inventory4 => "Inventory4",
    _ => type.ToString(),
  };

  /// <summary>
  /// Finds the slot a stack of (itemId, hq, quantity) landed on inside the given container types.
  /// Cosmetic only - used for the proof log line, never for anything that decides behaviour, since
  /// ExecutePull's own before/after re-read is what actually gates success.
  /// </summary>
  private static (InventoryType Container, int Slot)? FindStack(InventoryType[] types, uint itemId, bool hq, int quantity)
  {
    var manager = InventoryManager.Instance();
    if (manager == null) return null;
    foreach (var type in types)
    {
      var container = manager->GetInventoryContainer(type);
      if (container == null || !container->IsLoaded) continue;
      for (var i = 0; i < container->Size; i++)
      {
        var item = container->GetInventorySlot(i);
        if (item == null || item->ItemId != itemId) continue;
        if (item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) != hq) continue;
        if (item->Quantity == quantity)
          return (type, i);
      }
    }
    return null;
  }

  /// <summary>
  /// Executes one pull: withdraws a below-threshold listing back into the retainer's own inventory
  /// (or, one-shot, the player's bags) via the client's own withdrawal call - verified offline
  /// against FFXIVClientStructs (RetainerInventory: E8 ?? ?? ?? ?? 45 85 F6 75 22; PlayerBags:
  /// E8 ?? ?? ?? ?? EB 49 84 C0). RE-READS the market slot immediately before firing (the plan was
  /// built from a snapshot; the caller's own PlanPulls judged this from that same snapshot). The
  /// call's int return code is the primary proof of success; the market slot is re-read again
  /// AFTER the call to confirm it actually emptied - a zero rc with the item still sitting there
  /// would be a worse silent failure than an honest one. FindStack's result only decides which
  /// name goes in the log line.
  /// </summary>
  public static bool ExecutePull(PullOp op, out int rc, out string destination)
  {
    rc = -1;
    destination = op.Target == PullTarget.PlayerBags ? "player inventory" : "retainer inventory";
    var manager = InventoryManager.Instance();
    if (manager == null) return false;

    var market = manager->GetInventoryContainer(InventoryType.RetainerMarket);
    if (market == null || !market->IsLoaded) return false;
    var before = market->GetInventorySlot(op.Slot);
    if (before == null || before->ItemId != op.ItemId || before->Quantity != op.Quantity
        || before->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) != op.HQ)
      return false;

    rc = op.Target == PullTarget.PlayerBags
      ? manager->MoveFromRetainerMarketToPlayerInventory(InventoryType.RetainerMarket, (ushort)op.Slot, (uint)op.Quantity)
      : manager->MoveFromRetainerMarketToRetainerInventory(InventoryType.RetainerMarket, (ushort)op.Slot, (uint)op.Quantity);
    if (rc != 0)
      return false;

    var after = market->GetInventorySlot(op.Slot);
    if (after != null && after->ItemId == op.ItemId)
      return false;

    var landedTypes = op.Target == PullTarget.PlayerBags ? PlayerBagTypes : RetainerPageTypes;
    var found = FindStack(landedTypes, op.ItemId, op.HQ, op.Quantity);
    if (found != null)
      destination = $"{NameOfContainer(found.Value.Container)}:{found.Value.Slot}";

    return true;
  }

  // =====================================================================================
  // Routing mover (0.1.40.0, Helm t-joey-1789190796770 "build-mover"): the movement half of
  // category routing. See AutoMarket/RoutingMove.cs for the decision table.
  // =====================================================================================

  /// <summary>
  /// Builds this retainer's routing-move plan from a FRESH stock snapshot. Builds the category
  /// lookups exactly as ApplyCategoryRouting does (same Item-sheet read, same exclude map), so the
  /// mover and the gate can never disagree about which retainer a category is assigned to. Returns
  /// an empty plan when no routing rules exist.
  /// </summary>
  public static RoutingMovePlan PlanRoutingMoves()
  {
    var config = Plugin.Configuration;
    if (config.CategoryRetainerRules.Count == 0)
      return new RoutingMovePlan(new List<RoutingMoveOp>(), new List<string>(), false, false);

    var rules = BuildEnabledRules();
    var stock = SnapshotStock();
    var items = Svc.Data.GetExcelSheet<Item>();
    var categoryByKey = new Dictionary<string, ItemCategoryInfo>(rules.Count);
    var excludeByKey = new Dictionary<string, bool>(rules.Count);

    foreach (var rule in rules)
    {
      var key = $"{rule.ItemId}:{(rule.HQ ? "hq" : "nq")}";
      var entry = config.GetAutoMarketItem(rule.ItemId, rule.HQ);
      excludeByKey[key] = entry?.ExcludeFromCategoryRouting ?? false;

      if (items.TryGetRow(rule.ItemId, out var row))
        categoryByKey[key] = new ItemCategoryInfo(rule.ItemId, rule.HQ, row.ItemSearchCategory.RowId, row.ItemSearchCategory.RowId != 0 && !row.IsUntradable);
    }

    return RoutingMove.Plan(stock, rules, categoryByKey, excludeByKey, config.CategoryRetainerRules,
      CurrentRetainerName(), CountFreeBagSlots, RetainerPageFreeSlot);
  }

  /// <summary>Empty slots across Inventory1-4 (NOT crystals - the mover never targets that container).</summary>
  private static int CountFreeBagSlots()
  {
    var manager = InventoryManager.Instance();
    if (manager == null) return 0;
    var free = 0;
    foreach (var type in PlayerBagTypes)
    {
      var container = manager->GetInventoryContainer(type);
      if (container == null || !container->IsLoaded) continue;
      for (var i = 0; i < container->Size; i++)
      {
        var item = container->GetInventorySlot(i);
        if (item == null || item->ItemId == 0) free++;
      }
    }
    return free;
  }

  /// <summary>
  /// Executes one routing move: a whole-stack container-to-container InventoryManager.MoveItemSlot
  /// call (signature verified against FFXIVClientStructs 15.0.3.1: MoveItemSlot(InventoryType,
  /// ushort, InventoryType, ushort, bool) -> int, the vanilla swap/merge move the game itself uses
  /// for bag <-> retainer entrust). The destination slot is LITERAL, not a hint: pointing it at an
  /// occupied slot swaps the two stacks instead of moving (0.1.40.0 fired every move at slot 0 and
  /// chained swaps that validated as successes - see the 0.1.41.0 changelog), so the executor now
  /// resolves a concrete EMPTY destination slot itself, at execution time, fresh for every move:
  /// an existing same-item stack that can absorb the whole move (merge), else the first empty slot.
  /// RE-READS the source slot immediately before firing - the plan was built from a snapshot - and
  /// re-reads it AFTER the call; success requires the source slot to be EMPTY. rc==0 with anything
  /// still sitting in the source (a swap leaves the displaced stack there) is a failure
  /// (ExecutePull precedent: an honest failure beats a silent no-op). No empty destination slot and
  /// no mergeable stack -> the move fails with rc=-2 and the stack stays where it is; the planner's
  /// free-slot accounting is advisory, and a swap-shaped hole never opens.
  /// </summary>
  public static bool ExecuteRoutingMove(RoutingMoveOp op, out int rc)
  {
    rc = -1;
    var manager = InventoryManager.Instance();
    if (manager == null) return false;

    var src = manager->GetInventoryContainer((InventoryType)op.SrcContainer);
    if (src == null || !src->IsLoaded) return false;
    var before = src->GetInventorySlot(op.SrcSlot);
    if (before == null || before->ItemId != op.ItemId
        || before->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) != op.HQ)
      return false;

    var dstTypes = op.Leg == MoveLeg.RetainerToBags ? PlayerBagTypes : RetainerPageTypes;
    var merged = false;
    var moved = false;
    InventoryType? dstType = null;
    var dstSlot = -1;
    foreach (var type in dstTypes)
    {
      var cont = manager->GetInventoryContainer(type);
      if (cont == null || !cont->IsLoaded) continue;
      for (var i = 0; i < cont->Size; i++)
      {
        var it = cont->GetInventorySlot(i);
        if (it == null) continue;
        var hq = it->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality);
        if (it->ItemId == op.ItemId && hq == op.HQ && it->Quantity + before->Quantity <= 999)
        {
          dstType = type; dstSlot = i; merged = true; moved = true; break;
        }
      }
      if (moved) break;
    }
    if (!moved)
    {
      foreach (var type in dstTypes)
      {
        var cont = manager->GetInventoryContainer(type);
        if (cont == null || !cont->IsLoaded) continue;
        for (var i = 0; i < cont->Size; i++)
        {
          var it = cont->GetInventorySlot(i);
          if (it == null || it->ItemId == 0)
          {
            dstType = type; dstSlot = i; moved = true; break;
          }
        }
        if (moved) break;
      }
    }
    if (!moved)
    {
      rc = -2;
      Svc.Log.Warning($"[LMC] routing move: no empty slot and no mergeable stack for item {op.ItemId}{(op.HQ ? " HQ" : "")} in {(op.Leg == MoveLeg.RetainerToBags ? "the player's bags" : "this retainer's pages")}; leaving the stack in {NameOfContainer((InventoryType)op.SrcContainer)}#{op.SrcSlot}");
      return false;
    }

    rc = manager->MoveItemSlot((InventoryType)op.SrcContainer, (ushort)op.SrcSlot, dstType!.Value, (ushort)dstSlot, false);
    Svc.Log.Information($"[LMC] routing move: MoveItemSlot {NameOfContainer((InventoryType)op.SrcContainer)}#{op.SrcSlot} item {op.ItemId}{(op.HQ ? " HQ" : "")} -> {NameOfContainer(dstType!.Value)}#{dstSlot}{(merged ? " (merged)" : "")} rc={rc}");
    if (rc != 0)
      return false;

    // The container-level view lags the move by a frame or two; success requires the source slot
    // to be EMPTY. 0.1.40.0 accepted "source no longer holds this item" - which a swap satisfies
    // (the displaced stack sits there instead), so a chain of swaps graded itself as successes.
    var after = src->GetInventorySlot(op.SrcSlot);
    if (after != null && after->ItemId != 0)
      return false;

    return true;
  }
}

