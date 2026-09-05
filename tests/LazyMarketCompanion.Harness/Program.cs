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

ItemRule Rule(uint id, int stack, int keepB = 0, int keepR = 0, int max = 0, bool bags = true, bool ret = true, bool hq = false, int fixedPrice = 0, int itemMax = 999)
  => new(id, hq, stack, keepB, keepR, max, bags, ret, fixedPrice, itemMax);

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

// 12. Crystals: stock from the Crystals (2001) / RetainerCrystals (12001) containers flows through to the op's SourceContainer.
{
  const uint FireShard = 2;      // stack 9999
  const int Crystals = 2001, RetCrystals = 12001;
  var stock = new List<StockStack>
  {
    new(StockOrigin.Bags, Crystals, 0, FireShard, false, 2500),
    new(StockOrigin.Retainer, RetCrystals, 0, FireShard, false, 1200),
  };
  var r = AutoMarketPlanner.Plan([Rule(FireShard, 999, itemMax: 9999)], stock, EmptyMarket(), Opts(retFirst: true));
  Check("crystals: 1 retainer op + 2 bag ops of 999", r.Ops.Count == 3 && r.Ops.All(o => o.Quantity == 999), $"ops={r.Ops.Count}");
  Check("crystals: source containers are 12001 then 2001", r.Ops.Select(o => o.SourceContainer).SequenceEqual([RetCrystals, Crystals, Crystals]), string.Join(",", r.Ops.Select(o => o.SourceContainer)));
}

// 13. A rule with no stock anywhere it may sell from says so instead of silently doing nothing.
{
  var stock = new List<StockStack> { new(StockOrigin.Retainer, Ret1, 0, Dye, false, 99) };
  var r = AutoMarketPlanner.Plan([Rule(Ore, 99)], stock, EmptyMarket(), Opts());
  Check("no stock -> 0 ops + 'no stock in bags or retainer' note", r.Ops.Count == 0 && r.Notes.Count == 1 && r.Notes[0].Contains("no stock in bags or retainer"), string.Join("|", r.Notes));
  var r2 = AutoMarketPlanner.Plan([Rule(Dye, 5, bags: true, ret: false)], stock, EmptyMarket(), Opts());
  Check("stock only in a disabled origin -> 'no stock in bags' note", r2.Ops.Count == 0 && r2.Notes.Count == 1 && r2.Notes[0].Contains("no stock in bags") && !r2.Notes[0].Contains("retainer"), string.Join("|", r2.Notes));
  var r3 = AutoMarketPlanner.Plan([Rule(Dye, 5, bags: false, ret: false)], stock, EmptyMarket(), Opts());
  Check("both sources off -> 'no stock source enabled' note", r3.Ops.Count == 0 && r3.Notes.Count == 1 && r3.Notes[0].Contains("no stock source enabled"), string.Join("|", r3.Notes));
}

// 14. Less than one full listing with partials off is a note, not silence; a leftover after full listings is not.
//     (Only a crystal-stack item can have a 9999 listing since the market cap landed in 0.1.1.1.)
{
  const uint Shard = 2;
  var stock = new List<StockStack> { new(StockOrigin.Bags, Bags1, 0, Ore, false, 500), new(StockOrigin.Bags, 2001, 0, Shard, false, 500) };
  var r = AutoMarketPlanner.Plan([Rule(Shard, 9999, itemMax: 9999)], stock, EmptyMarket(), Opts());
  Check("500 of a 9999 listing, no partial -> 0 ops + note", r.Ops.Count == 0 && r.Notes.Count == 1 && r.Notes[0].Contains("less than one full listing of 9999"), string.Join("|", r.Notes));
  var r2 = AutoMarketPlanner.Plan([Rule(Ore, 99)], stock, EmptyMarket(), Opts());
  Check("500 in 99s -> 5 ops, leftover 5 is not a note", r2.Ops.Count == 5 && r2.Notes.Count == 0, $"ops={r2.Ops.Count} notes={string.Join("|", r2.Notes)}");
}

// 15. The 2026-09-05 disconnect: 297 HQ Kukuru Butter (bag stack 999) in one listing. The market takes 99 per listing;
//     the server drops the connection instead of refusing. Must never emit an op above 99 for a non-crystal.
{
  const uint Butter = 4854;
  var stock = new List<StockStack> { new(StockOrigin.Bags, Bags2, 3, Butter, true, 297) };
  var r = AutoMarketPlanner.Plan([Rule(Butter, 999, hq: true, itemMax: 999)], stock, EmptyMarket(), Opts(partial: true));
  Check("297 HQ at stack 999, partials on -> 3 ops of 99", r.Ops.Count == 3 && r.Ops.All(o => o.Quantity == 99), $"ops={string.Join(",", r.Ops.Select(o => o.Quantity))}");
  Check("no op ever exceeds 99 for a 999-stack item", r.Ops.All(o => o.Quantity <= MarketListingCap.Standard));
  Check("clamp is announced once", r.Notes.Count(n => n.Contains("clamped to the market's 99")) == 1, string.Join("|", r.Notes));
  var r2 = AutoMarketPlanner.Plan([Rule(Butter, 999, hq: true, itemMax: 999)], stock, EmptyMarket(), Opts(partial: false));
  Check("297 HQ, partials off -> 3 full 99s, leftover 0", r2.Ops.Count == 3 && r2.Ops.Sum(o => o.Quantity) == 297, $"ops={r2.Ops.Count}");
  Check("MarketListingCap.For: 999 -> 99, 99 -> 99, 1 -> 99, 9999 -> 9999", MarketListingCap.For(999) == 99 && MarketListingCap.For(99) == 99 && MarketListingCap.For(1) == 99 && MarketListingCap.For(9999) == 9999);
}

// 16. Crystals are the exception: bag stack 9999, market accepts 9999 -> Joey's x500 crystal rules go out untouched
//     (seven x500 Ice Crystal ops listed fine on 2026-09-05 15:12).
{
  const uint IceCrystal = 9;
  var stock = new List<StockStack> { new(StockOrigin.Bags, 2001, 7, IceCrystal, false, 3455) };
  var r = AutoMarketPlanner.Plan([Rule(IceCrystal, 500, itemMax: 9999)], stock, EmptyMarket(), Opts(partial: true));
  Check("crystal x500 -> 6x500 + 1x455, no clamp", r.Ops.Count == 7 && r.Ops.Take(6).All(o => o.Quantity == 500) && r.Ops[6].Quantity == 455 && r.Notes.Count == 0, $"ops={string.Join(",", r.Ops.Select(o => o.Quantity))} notes={string.Join("|", r.Notes)}");
  var r2 = AutoMarketPlanner.Plan([Rule(IceCrystal, 9999, itemMax: 9999)], stock, EmptyMarket(), Opts(partial: true));
  Check("crystal at stack 9999 -> one op of 3455, no clamp note", r2.Ops.Count == 1 && r2.Ops[0].Quantity == 3455 && r2.Notes.Count == 0, string.Join("|", r2.Notes));
}

// 17. "Stack size 0 = item max" resolves to 999 for ore at the service layer; the planner must still cap it at 99.
//     (Joey has ~40 rules at StackSize 0 on 999-stack items; 12539 x15 succeeded only because he held 15.)
{
  var stock = new List<StockStack> { new(StockOrigin.Bags, Bags1, 0, Ore, false, 999) };
  var r = AutoMarketPlanner.Plan([Rule(Ore, 999, itemMax: 999)], stock, EmptyMarket(), Opts(partial: false));
  Check("999 ore at 'max stack' -> 10 ops of 99, not one of 999", r.Ops.Count == 10 && r.Ops.All(o => o.Quantity == 99), $"ops={string.Join(",", r.Ops.Select(o => o.Quantity))}");
  Check("below-full-listing note uses the clamped size", AutoMarketPlanner.Plan([Rule(Ore, 999, itemMax: 999)], [new StockStack(StockOrigin.Bags, Bags1, 0, Ore, false, 50)], EmptyMarket(), Opts(partial: false)).Notes.Any(n => n.Contains("less than one full listing of 99")));
}

Console.WriteLine(failures == 0 ? "OK" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
