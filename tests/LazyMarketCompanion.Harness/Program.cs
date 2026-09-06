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

// =====================================================================================
// MarketRowMap - the row <-> slot bridge used by "pinch only what I just listed".
// The pinch chain clicks a RetainerSellList ROW; auto-market knows its listings by market SLOT.
// A wrong mapping is silent and expensive: the new listing keeps its 999,999,999 placeholder
// (never sells, no error) while an unrelated listing is re-priced.
// =====================================================================================

// Occupied slots in container order, exactly the layout the mapping assumes.
List<MarketSlot> SlotOrdered(params (int Slot, uint ItemId)[] filled)
{
  var m = new List<MarketSlot>();
  for (var i = 0; i < 20; i++)
  {
    var hit = filled.FirstOrDefault(f => f.Slot == i);
    m.Add(new MarketSlot(i, hit.ItemId, false, hit.ItemId == 0 ? 0 : 1));
  }
  return m;
}

// 18. Slot-ordered list: the mapping is correct, and it is Joey's real 2026-09-05 15:12 shape.
{
  // 13 existing listings then 7 new Ice Crystal stacks into slots 3,7,9,10,11,12,15 -> 20/20.
  const uint IceCrystal = 9, Other = 5111;
  var filled = new List<(int, uint)>();
  var newSlots = new[] { 3, 7, 9, 10, 11, 12, 15 };
  for (var i = 0; i < 20; i++) filled.Add((i, newSlots.Contains(i) ? IceCrystal : Other));
  var market = SlotOrdered(filled.ToArray());

  Check("rowmap: 20 occupied slots -> rows 0..19 in slot order", Enumerable.Range(0, 20).All(s => MarketRowMap.RowOfSlot(market, s) == s));
  Check("rowmap: row count agrees with occupied count", MarketRowMap.RowCountAgrees(market, 20) && !MarketRowMap.RowCountAgrees(market, 19));
  var rows = MarketRowMap.RowsForSlots(market, newSlots.Select(s => (s, IceCrystal)));
  Check("rowmap: the 7 new crystal slots map to rows 3,7,9,10,11,12,15", rows != null && rows.Select(r => r.Row).SequenceEqual(newSlots), rows == null ? "null" : string.Join(",", rows.Select(r => r.Row)));
  Check("rowmap: every mapped row is predicted to hold the crystal", rows != null && rows.All(r => MarketRowMap.RowHoldsItem(market, r.Row, IceCrystal)));

  // A gap means later slots sit on EARLIER rows - the case a naive "row == slot" would get wrong.
  var gapped = SlotOrdered((0, Other), (5, IceCrystal), (9, IceCrystal));
  Check("rowmap: with gaps, slots 0,5,9 -> rows 0,1,2", MarketRowMap.RowOfSlot(gapped, 0) == 0 && MarketRowMap.RowOfSlot(gapped, 5) == 1 && MarketRowMap.RowOfSlot(gapped, 9) == 2);
  Check("rowmap: an empty slot has no row", MarketRowMap.RowOfSlot(gapped, 3) == MarketRowMap.NoRow && MarketRowMap.RowOfSlot(gapped, 19) == MarketRowMap.NoRow);
  Check("rowmap: out-of-range row resolves to nothing", MarketRowMap.ItemIdAtRow(gapped, 3) == 0 && MarketRowMap.SlotAtRow(gapped, 3) == MarketRowMap.NoRow && MarketRowMap.ItemIdAtRow(gapped, -1) == 0);
}

// 19. A list that is NOT in slot order: the guard must DETECT it, not price the wrong row.
//     RetainerSellList is the game's list and nothing guarantees container order (DailyRoutines'
//     equivalent worker carries an explicit sort-order concept for the same list).
{
  const uint New = 9, Existing = 5111;
  // Market: slot 2 holds an existing listing, slot 4 is the one we just listed.
  var market = SlotOrdered((2, Existing), (4, New));

  // Under the assumption, slot 4 is row 1. If the UI is actually sorted by name/price/whatever, row 1
  // can be the OTHER listing - which is what the runtime check compares against.
  Check("rowmap: assumption puts the new slot 4 on row 1", MarketRowMap.RowOfSlot(market, 4) == 1);
  Check("rowmap: row 1 predicted to hold the new item, not the existing one", MarketRowMap.RowHoldsItem(market, 1, New) && !MarketRowMap.RowHoldsItem(market, 1, Existing));
  Check("rowmap: a re-sorted list showing the OTHER item at row 1 is refused", !MarketRowMap.RowHoldsItem(market, 1, Existing), "the runtime guard compares the open item against this prediction");
  Check("rowmap: an unidentifiable item (id 0) never satisfies the guard", !MarketRowMap.RowHoldsItem(market, 1, 0));

  // A row count that disagrees with the occupied count means rows cannot be trusted at all.
  Check("rowmap: 2 occupied but 5 rows shown -> refuse to map", !MarketRowMap.RowCountAgrees(market, 5));
  Check("rowmap: 2 occupied but 0 rows shown -> refuse to map", !MarketRowMap.RowCountAgrees(market, 0));

  // If the item we listed is not where we think it is, the whole batch is refused rather than
  // half-priced: one bad slot means the ordering assumption itself is suspect.
  Check("rowmap: a slot holding a DIFFERENT item than we listed -> whole batch refused", MarketRowMap.RowsForSlots(market, [(4, Existing)]) == null);
  Check("rowmap: an EMPTY slot we thought we listed into -> whole batch refused", MarketRowMap.RowsForSlots(market, [(7, New)]) == null);
  Check("rowmap: one good + one bad slot -> whole batch refused, not partially applied", MarketRowMap.RowsForSlots(market, [(4, New), (7, New)]) == null);
  Check("rowmap: all-good batch maps", MarketRowMap.RowsForSlots(market, [(4, New)])?.Single().Row == 1);
  Check("rowmap: empty batch maps to nothing", MarketRowMap.RowsForSlots(market, []) == null);
}

// 20. An empty market cannot produce a row for anything, and a full one maps every slot.
{
  const uint Item = 9;
  Check("rowmap: empty market -> no rows, no occupied", MarketRowMap.OccupiedCount(EmptyMarket()) == 0 && MarketRowMap.RowOfSlot(EmptyMarket(), 0) == MarketRowMap.NoRow && !MarketRowMap.RowCountAgrees(EmptyMarket(), 0));
  var full = SlotOrdered(Enumerable.Range(0, 20).Select(i => (i, Item)).ToArray());
  Check("rowmap: full market -> 20 occupied, row == slot throughout", MarketRowMap.OccupiedCount(full) == 20 && Enumerable.Range(0, 20).All(i => MarketRowMap.SlotAtRow(full, i) == i));
}

// =====================================================================================
// SellListRows - the 0.1.5.0 replacement for the row/slot GUESS above.
// The old mapping assumed "the sell list shows occupied slots in ascending container order". That was
// measured WRONG on 4 of 4 Auto-Market runs on Joey's client on 2026-09-05, and its safe fallback was
// "re-price the whole retainer" - i.e. the very behaviour the feature existed to remove. These cases
// replay those four runs and pin that reading the rows resolves what guessing them could not.
// =====================================================================================

const long Placeholder = 999_999_999L;

// Build a sell list in an arbitrary (non-container) order. Every row reports the slot it shows, which is
// what the addon actually gives us (AtkValues[15 + 13n].Int).
List<SellListRow> Rows(params (int Slot, uint ItemId, long Price)[] inOrder)
  => inOrder.Select((r, i) => new SellListRow(i, r.Slot, r.ItemId, r.Price)).ToList();

// The matching market container for such a list.
List<MarketSlot> MarketOf(params (int Slot, uint ItemId)[] filled)
{
  var m = new List<MarketSlot>();
  for (var i = 0; i < 20; i++)
  {
    var hit = filled.FirstOrDefault(f => f.Slot == i);
    m.Add(new MarketSlot(i, hit.ItemId, false, hit.ItemId == 0 ? 0 : 1));
  }
  return m;
}

// 21. The four real 2026-09-05 failures. Each is a 20/20 retainer whose sell list is NOT in slot order -
//     the run's own log line tells us exactly which item the client had on the row the old code picked, so
//     each case is built to put that item there. The old mapping must fail and the new one must succeed.
{
  var runs = new (string Name, int NewSlot, uint NewItem, uint ItemOnGuessedRow)[]
  {
    ("17:44 row 17 held Ice Crystal",              17, 41083u, 9u),
    ("18:38 row 3 held Heavens' Eye Materia VII",   3, 41768u, 25187u),
    ("18:38 row 12 held Zormor Stone Lantern",     12, 25198u, 44933u),
    ("18:40 row 19 held Table Orchestrion",        19,  7008u, 17954u),
    ("19:30 row 10 held Liquid Glass",             10, 52255u, 39711u),
  };

  foreach (var (name, newSlot, newItem, decoyItem) in runs)
  {
    // 20 occupied slots; the new listing is in newSlot, the decoy somewhere else.
    var decoySlot = newSlot == 0 ? 1 : 0;
    var filled = new List<(int, uint)>();
    for (var s = 0; s < 20; s++)
      filled.Add((s, s == newSlot ? newItem : (s == decoySlot ? decoyItem : 5111u)));
    var market = MarketOf(filled.ToArray());

    // The sell list is in SOME other order: the row the old code would have picked (row == newSlot, since
    // all 20 slots are occupied) is showing the decoy, exactly as the client reported.
    var order = Enumerable.Range(0, 20).ToList();
    order[newSlot] = decoySlot;
    order[decoySlot] = newSlot;
    var rows = Rows(order.Select(s => (s, s == newSlot ? newItem : (s == decoySlot ? decoyItem : 5111u), 100L)).ToArray());

    // The old guess: row == slot, and the row holds the wrong item. This is what fired 4/4 in production.
    Check($"replay {name}: the OLD container-order guess picks a row holding the wrong item",
      MarketRowMap.RowOfSlot(market, newSlot) == newSlot && MarketRowMap.ItemIdAtRow(market, newSlot) == newItem
        && rows[newSlot].ItemIdFromName == decoyItem);
    Check($"replay {name}: the old row-count check still PASSES, so no count check could ever catch it",
      MarketRowMap.RowCountAgrees(market, rows.Count));

    // The new reading: find the row that says it is showing that slot.
    var matched = SellListRows.MatchBySlot(rows, market, [(newSlot, newItem)], out var why);
    Check($"replay {name}: reading the rows finds the right one",
      matched != null && matched.Count == 1 && matched[0].Slot == newSlot && matched[0].ItemId == newItem
        && matched[0].Source == RowMatchSource.ObservedSlot, why ?? "matched");
    Check($"replay {name}: and it is NOT the row the old code would have clicked",
      matched != null && matched[0].Row == decoySlot, matched == null ? "null" : matched[0].Row.ToString());
  }
}

// 22. Two listings of the same item: only the placeholder-priced one is new. Slot reading handles it
//     without needing the price at all; the name fallback needs the price to tell them apart.
{
  const uint Same = 5111;
  var market = MarketOf((4, Same), (9, Same));
  // List shows slot 9 first, then slot 4 - the new one (slot 4) is still at the placeholder.
  var rows = Rows((9, Same, 250L), (4, Same, Placeholder));

  var bySlot = SellListRows.MatchBySlot(rows, market, [(4, Same)], out var e1);
  Check("dupes: slot reading picks the right row of two identical items", bySlot?.Single().Row == 1, e1 ?? "matched");

  var noSlots = rows.Select(r => r with { Slot = MarketRowMap.NoRow }).ToList();
  var byName = SellListRows.MatchByName(noSlots, [(4, Same)], Placeholder, out var e2);
  Check("dupes: name fallback uses the placeholder price to pick the NEW one", byName?.Single().Row == 1, e2 ?? "matched");
  Check("dupes: name fallback reports it matched by name", byName?.Single().Source == RowMatchSource.ObservedName);

  // UniversalisFirst mode: the new listing is born at a real price, so nothing separates the two rows.
  var bothReal = noSlots.Select(r => r with { AskingPrice = 250L }).ToList();
  Check("dupes: two identical rows with no placeholder are REFUSED, not guessed",
    SellListRows.MatchByName(bothReal, [(4, Same)], Placeholder, out var e3) == null && e3!.Contains("cannot be told apart"), e3 ?? "");
  // ...and two placeholders are equally ambiguous.
  var bothPlaceholder = noSlots.Select(r => r with { AskingPrice = Placeholder }).ToList();
  Check("dupes: two placeholder rows of one item are also refused",
    SellListRows.MatchByName(bothPlaceholder, [(4, Same)], Placeholder, out _) == null);
}

// 23. A name that does not resolve. The row still carries its slot, so slot matching is unaffected; the
//     name fallback cannot see it at all and must refuse rather than pick a neighbour.
{
  const uint New = 9, Other = 5111;
  var market = MarketOf((2, Other), (4, New));
  var rows = Rows((4, 0u, Placeholder), (2, Other, 300L)); // row 0 shows slot 4 but its name did not resolve

  var bySlot = SellListRows.MatchBySlot(rows, market, [(4, New)], out var e1);
  Check("unresolved name: slot reading still identifies the row", bySlot?.Single().Row == 0, e1 ?? "matched");

  var noSlots = rows.Select(r => r with { Slot = MarketRowMap.NoRow }).ToList();
  Check("unresolved name: the name fallback refuses instead of picking a neighbour",
    SellListRows.MatchByName(noSlots, [(4, New)], Placeholder, out var e2) == null && e2!.Contains("no visible sell-list row"), e2 ?? "");
}

// 24. The reading itself is checked, and one bad row refuses the whole batch.
{
  const uint A = 9, B = 5111;
  var market = MarketOf((1, A), (2, B));

  Check("guard: a row naming an item the container does not have in that slot is refused",
    SellListRows.MatchBySlot(Rows((1, B, 10L), (2, A, 10L)), market, [(1, A)], out var e1) == null && e1!.Contains("showing item"), e1 ?? "");
  Check("guard: two rows claiming the same slot are refused",
    SellListRows.MatchBySlot(Rows((1, A, 10L), (1, A, 10L)), market, [(1, A)], out var e2) == null && e2!.Contains("more than one row"), e2 ?? "");
  Check("guard: a listed slot no row is showing is refused",
    SellListRows.MatchBySlot(Rows((1, A, 10L), (2, B, 10L)), market, [(7, A)], out var e3) == null && e3!.Contains("no sell-list row"), e3 ?? "");
  Check("guard: one good + one bad slot refuses the WHOLE batch, never half-applies",
    SellListRows.MatchBySlot(Rows((1, A, 10L), (2, B, 10L)), market, [(1, A), (7, A)], out _) == null);
  Check("guard: an empty batch matches nothing",
    SellListRows.MatchBySlot(Rows((1, A, 10L)), market, [], out _) == null);
  Check("guard: a fully-good batch matches",
    SellListRows.MatchBySlot(Rows((2, B, 10L), (1, A, 10L)), market, [(1, A), (2, B)], out _)?.Count == 2);

  // Rows with no name at all (scrolled out of view - the list virtualises) do not block slot matching.
  Check("guard: unrendered rows (no name) still match by slot",
    SellListRows.MatchBySlot(Rows((1, 0u, 0L), (2, 0u, 0L)), market, [(1, A)], out _)?.Single().Row == 0);
  Check("HasSlotReadings: true when any row reports a slot, false when none do",
    SellListRows.HasSlotReadings(Rows((1, A, 10L))) &&
    !SellListRows.HasSlotReadings(Rows((1, A, 10L)).Select(r => r with { Slot = MarketRowMap.NoRow }).ToList()));
}

// 25. The own-items-only fallback can only ever touch items the user put on their Auto-Market list.
{
  const uint Mine = 9, Theirs = 17954;
  var market = MarketOf((0, Mine), (1, Theirs), (2, Mine));
  var own = new HashSet<uint> { Mine };

  var rows = Rows((1, Theirs, 500L), (0, Mine, 100L), (2, Mine, Placeholder));
  var pick = SellListRows.RowsHoldingOwnItems(rows, market, own);
  Check("own-items: only rows holding a listed item are chosen", pick.SequenceEqual([1, 2]), string.Join(",", pick));

  // A row whose name did not resolve is still identifiable through its slot.
  var unnamed = Rows((1, Theirs, 500L), (0, 0u, 100L), (2, 0u, 0L));
  Check("own-items: an unnamed row is resolved via the slot it reports",
    SellListRows.RowsHoldingOwnItems(unnamed, market, own).SequenceEqual([1, 2]));

  // A row with neither a name nor a slot is left alone - never re-priced on a guess.
  var blind = Rows((1, Theirs, 500L)).Concat([new SellListRow(1, MarketRowMap.NoRow, 0u, 0L)]).ToList();
  Check("own-items: a row with no name AND no slot is never touched",
    SellListRows.RowsHoldingOwnItems(blind, market, own).Count == 0);
  Check("own-items: nothing on the list -> nothing to re-price",
    SellListRows.RowsHoldingOwnItems(rows, market, []).Count == 0);
}

Console.WriteLine(failures == 0 ? "OK" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
