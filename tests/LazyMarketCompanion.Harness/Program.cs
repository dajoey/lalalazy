using LazyMarketCompanion.AutoMarket;

// Offline tests for the Auto-Market planner. Prints PASS/FAIL per case, exits non-zero on any FAIL.

var failures = 0;
void Check(string name, bool ok, string detail = "")
{
  Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(ok || detail.Length == 0 ? "" : " - " + detail)}");
  if (!ok) failures++;
}

const uint Dye = 5594;    // stack 99
const uint Ore = 5111;    // stack 999
const int Bags1 = 0, Bags2 = 1, Ret1 = 10000;

ItemRule Rule(uint id, int stack, int keepB = 0, int keepR = 0, int max = 0, bool bags = true, bool ret = true, bool hq = false, int fixedPrice = 0)
  => new(id, hq, stack, keepB, keepR, max, bags, ret, fixedPrice);

List<MarketSlot> EmptyMarket(int occupied = 0)
{
  var m = new List<MarketSlot>();
  for (var i = 0; i < 20; i++) m.Add(new MarketSlot(i, i < occupied ? 9999u : 0u, false, i < occupied ? 1 : 0));
  return m;
}

PlannerOptions Opts(int reserve = 0, bool retFirst = true, bool partial = false) => new(20, reserve, retFirst, partial);

// 1. Hundreds of dye in stacks of 5: fills every free slot, 5 each, from ONE bag stack.
{
  var stock = new List<StockStack> { new(StockOrigin.Bags, Bags1, 3, Dye, false, 99), new(StockOrigin.Bags, Bags1, 4, Dye, false, 99) };
  var r = AutoMarketPlanner.Plan([Rule(Dye, 5)], stock, EmptyMarket(), Opts());
  Check("dye x5 fills 20 slots", r.Ops.Count == 20, $"ops={r.Ops.Count}");
  Check("dye every op qty 5", r.Ops.All(o => o.Quantity == 5));
  Check("dye targets are 0..19 unique", r.Ops.Select(o => o.TargetSlot).Distinct().Count() == 20 && r.Ops.Max(o => o.TargetSlot) == 19);
  Check("dye ops only from the two dye stacks, 100 units total", r.Ops.All(o => o.SourceSlot is 3 or 4) && r.Ops.Sum(o => o.Quantity) == 100, string.Join(",", r.Ops.Select(o => o.SourceSlot)));
}

// 2. Reserve slots respected.
{
  var stock = new List<StockStack> { new(StockOrigin.Bags, Bags1, 0, Dye, false, 99) };
  var r = AutoMarketPlanner.Plan([Rule(Dye, 5)], stock, EmptyMarket(occupied: 15), Opts(reserve: 3));
  Check("reserve: 5 empty, 3 reserved -> 2 ops", r.Ops.Count == 2, $"ops={r.Ops.Count}");
  Check("reserve: targets are the empty slots 15,16", r.Ops.Select(o => o.TargetSlot).SequenceEqual([15, 16]));
}

// 3. KeepInBags: 12 in bags, keep 10, stack 5, no partials -> nothing (only 2 sellable).
{
  var stock = new List<StockStack> { new(StockOrigin.Bags, Bags1, 0, Dye, false, 12) };
  var r = AutoMarketPlanner.Plan([Rule(Dye, 5, keepB: 10)], stock, EmptyMarket(), Opts());
  Check("keep 10 of 12, no partial -> 0 ops", r.Ops.Count == 0, $"ops={r.Ops.Count}");
  var r2 = AutoMarketPlanner.Plan([Rule(Dye, 5, keepB: 10)], stock, EmptyMarket(), Opts(partial: true));
  Check("keep 10 of 12, partial -> 1 op of 2", r2.Ops.Count == 1 && r2.Ops[0].Quantity == 2, $"ops={r2.Ops.Count}");
}

// 4. Retainer inventory first, then bags; KeepInRetainer independent of KeepInBags.
{
  var stock = new List<StockStack>
  {
    new(StockOrigin.Bags, Bags1, 0, Ore, false, 50),
    new(StockOrigin.Retainer, Ret1, 2, Ore, false, 30),
  };
  var r = AutoMarketPlanner.Plan([Rule(Ore, 10, keepB: 45, keepR: 5)], stock, EmptyMarket(), Opts(retFirst: true));
  // retainer: 30-5 = 25 -> 2 full stacks; bags: 50-45 = 5 -> 0 full stacks
  Check("retainer first: 2 ops from retainer, 0 from bags", r.Ops.Count == 2 && r.Ops.All(o => o.Origin == StockOrigin.Retainer), $"ops={r.Ops.Count} origins={string.Join(",", r.Ops.Select(o => o.Origin))}");
}

// 5. MaxListingsPerRetainer counts existing listings of the same item.
{
  var market = EmptyMarket();
  market[0] = new MarketSlot(0, Dye, false, 5);
  market[1] = new MarketSlot(1, Dye, false, 5);
  var stock = new List<StockStack> { new(StockOrigin.Bags, Bags1, 0, Dye, false, 99) };
  var r = AutoMarketPlanner.Plan([Rule(Dye, 5, max: 3)], stock, market, Opts());
  Check("max 3 with 2 existing -> 1 op", r.Ops.Count == 1, $"ops={r.Ops.Count}");
  Check("max: HQ existing does not count against NQ", AutoMarketPlanner.Plan([Rule(Dye, 5, max: 1)], stock, [new MarketSlot(0, Dye, true, 5), .. EmptyMarket().Skip(1)], Opts()).Ops.Count == 1);
}

// 6. HQ and NQ are separate rules and separate stock.
{
  var stock = new List<StockStack>
  {
    new(StockOrigin.Bags, Bags1, 0, Ore, false, 20),
    new(StockOrigin.Bags, Bags1, 1, Ore, true, 20),
  };
  var r = AutoMarketPlanner.Plan([Rule(Ore, 10, hq: true)], stock, EmptyMarket(), Opts());
  Check("HQ rule only touches HQ stock", r.Ops.Count == 2 && r.Ops.All(o => o.HQ && o.SourceSlot == 1), $"ops={r.Ops.Count}");
}

// 7. Fragmented stock: total is enough but no single stack holds a full listing.
{
  var stock = new List<StockStack> { new(StockOrigin.Bags, Bags1, 0, Dye, false, 3), new(StockOrigin.Bags, Bags1, 1, Dye, false, 3) };
  var r = AutoMarketPlanner.Plan([Rule(Dye, 5)], stock, EmptyMarket(), Opts());
  Check("fragmented, no partial -> 0 ops + note", r.Ops.Count == 0 && r.Notes.Count == 1, $"ops={r.Ops.Count} notes={r.Notes.Count}");
  var r2 = AutoMarketPlanner.Plan([Rule(Dye, 5)], stock, EmptyMarket(), Opts(partial: true));
  Check("fragmented, partial -> 2 ops of 3", r2.Ops.Count == 2 && r2.Ops.All(o => o.Quantity == 3), $"ops={r2.Ops.Count}");
}

// 8. Items not on the list are never touched; disabled sources are honoured.
{
  var stock = new List<StockStack>
  {
    new(StockOrigin.Bags, Bags1, 0, Dye, false, 99),
    new(StockOrigin.Retainer, Ret1, 0, Dye, false, 99),
    new(StockOrigin.Bags, Bags2, 0, 12345, false, 99),
  };
  var r = AutoMarketPlanner.Plan([Rule(Dye, 99, bags: false, ret: true)], stock, EmptyMarket(), Opts());
  Check("bags disabled -> only retainer op, other item untouched", r.Ops.Count == 1 && r.Ops[0].Origin == StockOrigin.Retainer && r.Ops.All(o => o.ItemId == Dye));
}

// 9. Full market -> no ops, one note.
{
  var stock = new List<StockStack> { new(StockOrigin.Bags, Bags1, 0, Dye, false, 99) };
  var r = AutoMarketPlanner.Plan([Rule(Dye, 5)], stock, EmptyMarket(occupied: 20), Opts());
  Check("full market -> 0 ops", r.Ops.Count == 0 && r.Notes.Count == 1);
}

// 10. Two rules share the slot budget in list order.
{
  var stock = new List<StockStack> { new(StockOrigin.Bags, Bags1, 0, Dye, false, 99), new(StockOrigin.Bags, Bags1, 1, Ore, false, 999) };
  var r = AutoMarketPlanner.Plan([Rule(Dye, 5, max: 4), Rule(Ore, 99)], stock, EmptyMarket(), Opts());
  Check("dye capped at 4, ore takes the remaining 10 (999/99 = 10 full)", r.Ops.Count(o => o.ItemId == Dye) == 4 && r.Ops.Count(o => o.ItemId == Ore) == 10, $"dye={r.Ops.Count(o => o.ItemId == Dye)} ore={r.Ops.Count(o => o.ItemId == Ore)}");
}

// 11. Fixed price propagates.
{
  var stock = new List<StockStack> { new(StockOrigin.Bags, Bags1, 0, Dye, false, 5) };
  var r = AutoMarketPlanner.Plan([Rule(Dye, 5, fixedPrice: 1234)], stock, EmptyMarket(), Opts());
  Check("fixed price on op", r.Ops.Count == 1 && r.Ops[0].FixedPrice == 1234);
}

Console.WriteLine(failures == 0 ? "OK" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
