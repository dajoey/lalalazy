using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>One stack of an item sitting somewhere we may sell from.</summary>
public sealed record StockStack(StockOrigin Origin, int Container, int Slot, uint ItemId, bool HQ, int Quantity);

public enum StockOrigin { Bags, Retainer }

/// <summary>One of the retainer's 20 market slots as seen at planning time.</summary>
public sealed record MarketSlot(int Slot, uint ItemId, bool HQ, int Quantity);

/// <summary>Per-item rules, already resolved against the global config (no nulls left).</summary>
public sealed record ItemRule(
  uint ItemId,
  bool HQ,
  int StackSize,           // > 0, already clamped to the item's max stack
  int KeepInBags,
  int KeepInRetainer,
  int MaxListingsPerRetainer, // 0 = no cap
  bool SellFromBags,
  bool SellFromRetainer,
  int FixedPrice);         // 0 = match

public sealed record PlannerOptions(
  int MarketSlotCount,      // 20
  int ReserveSlots,
  bool PreferRetainerStockFirst,
  bool ListPartialStacks);

/// <summary>One MoveToRetainerMarket call to make, in order.</summary>
public sealed record ListingOp(StockOrigin Origin, int SourceContainer, int SourceSlot, int TargetSlot, uint ItemId, bool HQ, int Quantity, int FixedPrice);

public sealed record PlanResult(IReadOnlyList<ListingOp> Ops, IReadOnlyList<string> Notes);

public static class AutoMarketPlanner
{
  public static PlanResult Plan(IEnumerable<ItemRule> rules, IReadOnlyList<StockStack> stock, IReadOnlyList<MarketSlot> market, PlannerOptions options)
  {
    var ops = new List<ListingOp>();
    var notes = new List<string>();

    var occupied = market.Where(m => m.ItemId != 0).ToList();
    var emptySlots = new Queue<int>(Enumerable.Range(0, options.MarketSlotCount)
      .Where(i => market.All(m => m.Slot != i || m.ItemId == 0))
      .OrderBy(i => i));

    var freeBudget = emptySlots.Count - Math.Max(options.ReserveSlots, 0);
    if (freeBudget <= 0)
    {
      notes.Add($"no free market slots ({emptySlots.Count} empty, {options.ReserveSlots} reserved)");
      return new PlanResult(ops, notes);
    }

    // Mutable local copy of stock so successive ops see decremented quantities.
    var pool = stock.Select(s => new MutableStack(s)).ToList();

    foreach (var rule in rules)
    {
      if (freeBudget <= 0)
        break;

      if (rule.StackSize <= 0)
      {
        notes.Add($"{rule.ItemId}{(rule.HQ ? " HQ" : "")}: stack size 0, skipped");
        continue;
      }

      var existing = occupied.Count(m => m.ItemId == rule.ItemId && m.HQ == rule.HQ) + ops.Count(o => o.ItemId == rule.ItemId && o.HQ == rule.HQ);
      var capLeft = rule.MaxListingsPerRetainer > 0 ? rule.MaxListingsPerRetainer - existing : int.MaxValue;
      if (capLeft <= 0)
      {
        notes.Add($"{rule.ItemId}{(rule.HQ ? " HQ" : "")}: already at {rule.MaxListingsPerRetainer} listings on this retainer");
        continue;
      }

      var origins = new List<StockOrigin>();
      if (options.PreferRetainerStockFirst)
      {
        if (rule.SellFromRetainer) origins.Add(StockOrigin.Retainer);
        if (rule.SellFromBags) origins.Add(StockOrigin.Bags);
      }
      else
      {
        if (rule.SellFromBags) origins.Add(StockOrigin.Bags);
        if (rule.SellFromRetainer) origins.Add(StockOrigin.Retainer);
      }

      if (origins.Count == 0)
      {
        notes.Add($"{rule.ItemId}{(rule.HQ ? " HQ" : "")}: no stock source enabled");
        continue;
      }

      // A rule whose item is nowhere we may sell from used to fall through silently (sellable <= 0, loop never
      // entered). Say so, so a container the snapshot does not scan shows up as a note instead of nothing.
      var available = pool.Where(s => s.ItemId == rule.ItemId && s.HQ == rule.HQ && s.Quantity > 0 && origins.Contains(s.Origin)).Sum(s => s.Quantity);
      if (available <= 0)
      {
        notes.Add($"{rule.ItemId}{(rule.HQ ? " HQ" : "")}: no stock in {DescribeOrigins(origins)}");
        continue;
      }

      foreach (var origin in origins)
      {
        if (freeBudget <= 0 || capLeft <= 0)
          break;

        var keep = origin == StockOrigin.Bags ? rule.KeepInBags : rule.KeepInRetainer;
        var stacks = pool.Where(s => s.Origin == origin && s.ItemId == rule.ItemId && s.HQ == rule.HQ && s.Quantity > 0).ToList();
        var sellable = stacks.Sum(s => s.Quantity) - Math.Max(keep, 0);
        var listedFromOrigin = 0;

        while (sellable > 0 && freeBudget > 0 && capLeft > 0)
        {
          var want = Math.Min(rule.StackSize, sellable);
          if (want < rule.StackSize && !options.ListPartialStacks)
          {
            // Only worth a note when nothing at all went out from this origin (a leftover after full listings is normal).
            if (listedFromOrigin == 0)
              notes.Add($"{rule.ItemId}{(rule.HQ ? " HQ" : "")}: {sellable} sellable in {origin} is less than one full listing of {rule.StackSize} (partial stacks are off)");
            break;
          }

          // Largest stack first: a single op moves from ONE source slot, so we need one stack that holds the whole listing.
          var source = stacks.Where(s => s.Quantity >= want).OrderByDescending(s => s.Quantity).FirstOrDefault();
          if (source == null)
          {
            // Fragmented: total is enough but no single stack is. Take what the biggest stack has if partials are allowed.
            var biggest = stacks.Where(s => s.Quantity > 0).OrderByDescending(s => s.Quantity).FirstOrDefault();
            if (biggest == null || !options.ListPartialStacks)
            {
              notes.Add($"{rule.ItemId}{(rule.HQ ? " HQ" : "")}: {sellable} sellable in {origin} but no single stack holds {want}");
              break;
            }
            source = biggest;
            want = Math.Min(biggest.Quantity, want);
          }

          var target = emptySlots.Dequeue();
          ops.Add(new ListingOp(origin, source.Container, source.Slot, target, rule.ItemId, rule.HQ, want, rule.FixedPrice));
          source.Quantity -= want;
          sellable -= want;
          freeBudget--;
          capLeft--;
          listedFromOrigin++;
        }
      }
    }

    return new PlanResult(ops, notes);
  }

  private static string DescribeOrigins(List<StockOrigin> origins)
  {
    var bags = origins.Contains(StockOrigin.Bags);
    var ret = origins.Contains(StockOrigin.Retainer);
    if (bags && ret) return "bags or retainer";
    return bags ? "bags" : "retainer";
  }

  private sealed class MutableStack(StockStack s)
  {
    public StockOrigin Origin { get; } = s.Origin;
    public int Container { get; } = s.Container;
    public int Slot { get; } = s.Slot;
    public uint ItemId { get; } = s.ItemId;
    public bool HQ { get; } = s.HQ;
    public int Quantity { get; set; } = s.Quantity;
  }
}
