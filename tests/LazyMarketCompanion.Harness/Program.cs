using LazyMarketCompanion;
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

  // This row IS the row being priced (it claims slot #1 and slot #1 is what we listed into), so it is
  // still refused in 0.1.6.0 - by the scoped per-target check, which words it differently.
  Check("guard: a row naming an item the container does not have in that slot is refused",
    SellListRows.MatchBySlot(Rows((1, B, 10L), (2, A, 10L)), market, [(1, A)], out var e1) == null && e1!.Contains("shows item"), e1 ?? "");
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


// =====================================================================================
// 26-30. LMC 0.1.6.0 - the 2026-09-05 20:37:48 failure, and the price-based identification.
//
// What happened: 0.1.5.0 read the row correctly (slot #10 -> its row) and threw the pass away anyway,
// because the GLOBAL name cross-check found a disagreement on row 0 - a row nobody was pricing. Row 0 held
// "Snow Cotton Ushanka of Scouting" (41878); its clipped label resolved to "Snow Cotton" (44024), a real,
// distinct, marketable item whose name is a strict prefix of the other. Two defects in one line:
// the resolver failing OPEN, and one unrelated row vetoing the batch.
//
// Joey's answer to all of it (2026-09-05): "It should figure it out. there has to be a way to see what my
// listings are and select the one with the WILDLY INFLATED PRICE." So identification is now the market
// CONTAINER's price, and the name is a corroborator that may never veto a row it is not pricing.
//
// Every case below asserts BOTH halves - what the 0.1.5.0 logic did and what the new logic does. A
// one-sided test passes on a no-op, and this repo has been burned by that twice.
// =====================================================================================

// The 0.1.5.0 resolver, reproduced exactly (ItemNameResolver.ResolveItemId, lines 50-70 of that release):
// exact match, else the LONGEST item name contained anywhere in the text. Kept here only so the replays can
// prove the old behaviour was wrong rather than asserting it.
uint ResolveLikeV0150(string text, IEnumerable<(uint Id, string Name)> catalogue)
{
  var exact = catalogue.Where(c => c.Name.Equals(text, StringComparison.OrdinalIgnoreCase))
    .Select(c => c.Id).FirstOrDefault();
  if (exact != 0) return exact;
  return catalogue
    .Where(c => c.Name.Length > 0 && text.Contains(c.Name, StringComparison.OrdinalIgnoreCase))
    .OrderByDescending(c => c.Name.Length)
    .Select(c => c.Id)
    .FirstOrDefault();
}

// The 0.1.5.0 MatchBySlot cross-check, reproduced exactly (SellListRows.cs lines 82-92 of that release):
// EVERY row with a readable name is compared against the container, and the first disagreement returns null.
bool V0150CrossCheckVetoes(IReadOnlyList<SellListRow> rows, IReadOnlyList<MarketSlot> market, out string? why)
{
  foreach (var row in rows.Where(r => r.Slot != MarketRowMap.NoRow && r.ItemIdFromName != 0))
  {
    var inSlot = market.FirstOrDefault(m => m.Slot == row.Slot)?.ItemId ?? 0u;
    if (inSlot != 0 && inSlot != row.ItemIdFromName)
    {
      why = $"row {row.Row} says it is slot #{row.Slot} (item {inSlot}) but it is showing item {row.ItemIdFromName}";
      return true;
    }
  }
  why = null;
  return false;
}

// The three items from the incident, with their real names and ids (XIVAPI v2).
const uint IronOre = 5111;            // "Iron Ore"
const uint Ushanka = 41878;           // "Snow Cotton Ushanka of Scouting"
const uint SnowCotton = 44024;        // "Snow Cotton"
var Catalogue = new (uint Id, string Name)[]
{
  (IronOre, "Iron Ore"),
  (Ushanka, "Snow Cotton Ushanka of Scouting"),
  (SnowCotton, "Snow Cotton"),
  (13747, "Titanium Alloy Ingot"),
  (9, "Ice Crystal"),
};

// 26. The resolver must fail CLOSED. A row whose label is clipped names a shorter real item; the container
//     is the tiebreak, and where there is none the ambiguity itself is enough.
{
  // Sanity: the prefix relationship the whole defect rests on is real.
  Check("resolver: 'Snow Cotton' really is a strict prefix of the Ushanka's name",
    "Snow Cotton Ushanka of Scouting".StartsWith("Snow Cotton", StringComparison.Ordinal)
      && "Snow Cotton Ushanka of Scouting" != "Snow Cotton");

  // (a) clipped at a word boundary: the text is an EXACT match for the shorter item, so nothing about the
  //     text alone can save us - only the container can. This is the 20:37:48 shape.
  Check("resolver: the 0.1.5.0 logic reports the WRONG item for a row clipped to 'Snow Cotton'",
    ResolveLikeV0150("Snow Cotton", Catalogue) == SnowCotton);
  Check("resolver: clipped 'Snow Cotton' on a slot the container says holds 41878 -> unknown, NOT 44024",
    ItemNameMatch.Resolve("Snow Cotton", "Snow Cotton", Catalogue, expectedItemId: Ushanka) == ItemNameMatch.Unknown);

  // (b) NEGATIVE CONTROL: the same text on a row whose slot really does hold Snow Cotton must still resolve.
  //     Without this, "always return 0" would pass the case above.
  Check("resolver: NEGATIVE CONTROL - untruncated 'Snow Cotton' where the container agrees -> 44024",
    ItemNameMatch.Resolve("Snow Cotton", "Snow Cotton", Catalogue, expectedItemId: SnowCotton) == SnowCotton);
  Check("resolver: NEGATIVE CONTROL - a normal row still resolves with no container hint at all",
    ItemNameMatch.Resolve("Iron Ore", "Iron Ore", Catalogue) == IronOre
      && ItemNameMatch.Resolve("Snow Cotton", "Snow Cotton", Catalogue) == SnowCotton);

  // (c) clipped mid-word: no exact match, and the old substring fallback picks the shorter item. Here the
  //     ambiguity is visible in the text itself, so it is refused even with no container hint.
  Check("resolver: the 0.1.5.0 logic reports 44024 for a mid-word clip too",
    ResolveLikeV0150("Snow Cotton Ushank", Catalogue) == SnowCotton);
  Check("resolver: a mid-word clip is refused even with no container hint",
    ItemNameMatch.Resolve("Snow Cotton Ushank", "Snow Cotton Ushank", Catalogue) == ItemNameMatch.Unknown);
  Check("resolver: ...and refused with the container hint as well",
    ItemNameMatch.Resolve("Snow Cotton Ushank", "Snow Cotton Ushank", Catalogue, Ushanka) == ItemNameMatch.Unknown);

  // (d) the trailing-space tell from the 0.1.4.0-era log: "Titanium Alloy Ingot " on a 20-char name.
  Check("resolver: a trailing space does not stop an exact match",
    ItemNameMatch.Resolve("Titanium Alloy Ingot", "Titanium Alloy Ingot ", Catalogue) == 13747);
  Check("resolver: the truncation stem ignores trailing space and ellipsis",
    ItemNameMatch.TruncationStem("Snow Cotton ") == "Snow Cotton"
      && ItemNameMatch.TruncationStem("Snow Cotton...") == "Snow Cotton"
      && ItemNameMatch.TruncationStem("Snow Cotton\u2026") == "Snow Cotton");

  // (e) a full, unambiguous name is never refused just because a shorter name sits inside it.
  Check("resolver: the full Ushanka name resolves to the Ushanka, not to Snow Cotton",
    ItemNameMatch.Resolve("Snow Cotton Ushanka of Scouting", "Snow Cotton Ushanka of Scouting", Catalogue) == Ushanka);
  Check("resolver: ...and agrees with the container when asked",
    ItemNameMatch.Resolve("Snow Cotton Ushanka of Scouting", "Snow Cotton Ushanka of Scouting", Catalogue, Ushanka) == Ushanka);

  // (f) a genuine disagreement is still reported, not swallowed - the container check only forgives a clip.
  Check("resolver: a row naming a completely different item still reports that item",
    ItemNameMatch.Resolve("Iron Ore", "Iron Ore", Catalogue, expectedItemId: Ushanka) == IronOre);
  Check("resolver: empty / unknown text is unknown",
    ItemNameMatch.Resolve("", "", Catalogue) == ItemNameMatch.Unknown
      && ItemNameMatch.Resolve("Nonexistent Widget", "Nonexistent Widget", Catalogue) == ItemNameMatch.Unknown);
}

// 27. The 20:37:48 run, replayed end to end. One listing (Iron Ore x4) into slot #10 of a 20/20 retainer;
//     row 0 shows slot #5, which holds the Ushanka, and its clipped label reads as Snow Cotton.
{
  var filled = new List<(int, uint)>();
  for (var s = 0; s < 20; s++)
    filled.Add((s, s == 10 ? IronOre : (s == 5 ? Ushanka : 5594u)));
  var market = MarketOf(filled.ToArray());

  // The sell list, in a scrambled order: row 0 shows slot #5, row 1 shows slot #10.
  var order = new List<int> { 5, 10 };
  for (var s = 0; s < 20; s++) if (s != 5 && s != 10) order.Add(s);

  // Row 0's name is what the client rendered. Under 0.1.5.0's resolver that is 44024.
  var rowsOld = order.Select((slot, i) => new SellListRow(
    i, slot,
    slot == 5 ? ResolveLikeV0150("Snow Cotton", Catalogue) : (slot == 10 ? IronOre : 5594u),
    slot == 10 ? Placeholder : 500L)).ToList();

  Check("20:37:48 replay: row 0 resolved to 44024 under the old resolver - the log line's own numbers",
    rowsOld[0].Row == 0 && rowsOld[0].Slot == 5 && rowsOld[0].ItemIdFromName == SnowCotton);

  // HALF ONE: the 0.1.5.0 global cross-check vetoes, and reproduces the logged sentence verbatim.
  var vetoed = V0150CrossCheckVetoes(rowsOld, market, out var oldWhy);
  Check("20:37:48 replay: the 0.1.5.0 GLOBAL cross-check vetoes the batch", vetoed);
  Check("20:37:48 replay: ...with the exact sentence from Joey's log",
    oldWhy == "row 0 says it is slot #5 (item 41878) but it is showing item 44024", oldWhy ?? "(no veto)");

  // HALF TWO: with the resolver fixed, row 0 reads as unknown, and the scoped cross-check ignores it anyway.
  var rowsNew = order.Select((slot, i) => new SellListRow(
    i, slot,
    slot == 5 ? ItemNameMatch.Resolve("Snow Cotton", "Snow Cotton", Catalogue, Ushanka)
              : (slot == 10 ? IronOre : 5594u),
    slot == 10 ? Placeholder : 500L)).ToList();

  Check("20:37:48 replay: row 0 now reads as UNKNOWN instead of as a different item",
    rowsNew[0].ItemIdFromName == 0);
  Check("20:37:48 replay: the old cross-check would no longer fire on the fixed reading either",
    !V0150CrossCheckVetoes(rowsNew, market, out _), "resolver fix alone is enough for THIS row");

  var matched = SellListRows.MatchBySlot(rowsNew, market, [(10, IronOre)], out var newWhy);
  Check("20:37:48 replay: the new logic prices exactly one listing",
    matched != null && matched.Count == 1, newWhy ?? "matched");
  Check("20:37:48 replay: ...and it is slot #10 on the row the addon said, not row 0",
    matched != null && matched[0].Slot == 10 && matched[0].ItemId == IronOre && matched[0].Row == 1
      && matched[0].Source == RowMatchSource.ObservedSlot);

  // INDEPENDENCE CONTROL: the scoped cross-check must hold even if the resolver had NOT been fixed. Either
  // fix alone stops this run failing; that is deliberate, and this pins it rather than leaving it to luck.
  var stillMatched = SellListRows.MatchBySlot(rowsOld, market, [(10, IronOre)], out var why2);
  Check("20:37:48 replay: scoping ALONE fixes the run - the old wrong name on row 0 no longer vetoes",
    stillMatched != null && stillMatched.Count == 1 && stillMatched[0].Slot == 10, why2 ?? "matched");
}

// 28. The scoped cross-check: a row being PRICED must still agree, an unrelated row is ignored.
{
  const uint A = 9, B = 5111, C = 17954;
  var market = MarketOf((1, A), (2, B), (3, C));

  Check("scope: a wrong name on a row we are NOT pricing is ignored",
    SellListRows.MatchBySlot(Rows((1, A, 10L), (2, B, 10L), (3, A, 10L)), market, [(1, A)], out _)?.Single().Row == 0);
  Check("scope: a wrong name on the row we ARE pricing still refuses the batch",
    SellListRows.MatchBySlot(Rows((1, C, 10L), (2, B, 10L)), market, [(1, A)], out var e1) == null
      && e1!.Contains("not the item"), e1 ?? "");
  Check("scope: an unreadable name (0) on the row being priced is accepted on the slot reading",
    SellListRows.MatchBySlot(Rows((1, 0u, 10L), (2, B, 10L)), market, [(1, A)], out _)?.Single().Row == 0);
  // Name unreadable (0) so the name check cannot fire: this pins the CONTAINER cross-check specifically.
  Check("scope: the container disagreeing about a slot we are pricing still refuses",
    SellListRows.MatchBySlot(Rows((1, 0u, 10L)), market, [(1, C)], out var e2) == null
      && e2!.Contains("market container says"), e2 ?? "");
  Check("scope: a readable name disagreeing about that same row refuses too, just earlier",
    SellListRows.MatchBySlot(Rows((1, A, 10L)), market, [(1, C)], out var e2b) == null
      && e2b!.Contains("not the item"), e2b ?? "");
  Check("scope: duplicate-slot detection stays GLOBAL - two rows claiming one slot still refuses",
    SellListRows.MatchBySlot(Rows((3, A, 10L), (3, A, 10L), (1, A, 10L)), market, [(1, A)], out var e3) == null
      && e3!.Contains("more than one row"), e3 ?? "");
  Check("scope: ...including when the duplicate is of the slot we are pricing",
    SellListRows.MatchBySlot(Rows((1, A, 10L), (1, A, 10L)), market, [(1, A)], out _) == null);
}

// 29. ScanPlaceholders - "select the one with the WILDLY INFLATED PRICE". This is the anti-regression test
//     for the original bug and it is the important one: what may NOT be touched.
{
  const ulong PH = 999_999_999UL;
  var prices = new Dictionary<int, ulong>
  {
    [3] = PH,      // listed this run, still at the placeholder  -> target
    [7] = 250UL,   // listed this run, already priced            -> dropped
    [11] = PH,     // NOT listed this run, at the placeholder    -> NEVER touched
    [15] = 4200UL, // a stranger's listing                       -> irrelevant
  };

  var scan = SellListRows.ScanPlaceholders([(3, IronOre), (7, IronOre)], prices, PH);
  Check("scan: a listed slot still at the placeholder is a target",
    scan.Targets.Count == 1 && scan.Targets[0].Slot == 3 && scan.Targets[0].ItemId == IronOre,
    string.Join(",", scan.Targets.Select(t => t.Slot)));
  Check("scan: a listed slot that already carries a real price is DROPPED, not re-priced",
    scan.AlreadyPriced.SequenceEqual([7]) && scan.Targets.All(t => t.Slot != 7));
  Check("scan: a placeholder-priced slot this run did NOT list into is NEVER a target",
    scan.Targets.All(t => t.Slot != 11) && scan.Foreign.SequenceEqual([11]));
  Check("scan: a stranger's normally-priced listing is neither target nor foreign",
    scan.Targets.All(t => t.Slot != 15) && !scan.Foreign.Contains(15));

  // Nothing left to do is NOT a reason to re-price the retainer.
  var allPriced = SellListRows.ScanPlaceholders([(7, IronOre)], prices, PH);
  Check("scan: every listing already priced -> no targets at all",
    allPriced.Targets.Count == 0 && allPriced.AlreadyPriced.SequenceEqual([7]));

  // An unreadable slot is not the placeholder, so it is never selected on a guess.
  var missing = SellListRows.ScanPlaceholders([(19, IronOre)], prices, PH);
  Check("scan: a slot whose price could not be read is not a target",
    missing.Targets.Count == 0 && missing.AlreadyPriced.SequenceEqual([19]));

  // A non-default placeholder is honoured (the price is a setting, not a constant).
  var custom = SellListRows.ScanPlaceholders([(3, IronOre)], new Dictionary<int, ulong> { [3] = 12345UL }, 12345UL);
  Check("scan: the placeholder price is whatever the setting says, not a hardcoded 999,999,999",
    custom.Targets.Count == 1 && custom.Targets[0].Slot == 3);
  Check("scan: ...and the default no longer matches once it has been changed",
    SellListRows.ScanPlaceholders([(3, IronOre)], new Dictionary<int, ulong> { [3] = 12345UL }, PH).Targets.Count == 0);

  Check("scan: nothing listed -> nothing to do and nothing foreign-priced is dragged in",
    SellListRows.ScanPlaceholders([], prices, PH).Targets.Count == 0);
}

// 30. The two halves together on the 20:37:48 shape: the price scan picks the slot, the row reading finds
//     its row, and a foreign placeholder listing in the same retainer is left alone throughout.
{
  const ulong PH = 999_999_999UL;
  var filled = new List<(int, uint)>();
  for (var s = 0; s < 20; s++)
    filled.Add((s, s == 10 ? IronOre : (s == 5 ? Ushanka : 5594u)));
  var market = MarketOf(filled.ToArray());

  var prices = new Dictionary<int, ulong>();
  for (var s = 0; s < 20; s++) prices[s] = s == 10 ? PH : (s == 4 ? PH : 500UL);

  var scan = SellListRows.ScanPlaceholders([(10, IronOre)], prices, PH);
  Check("end-to-end: the scan picks slot #10 and only slot #10", scan.Targets.Count == 1 && scan.Targets[0].Slot == 10);
  Check("end-to-end: slot #4 is at the placeholder but was not ours, so it is reported and left",
    scan.Foreign.SequenceEqual([4]));

  var order = new List<int> { 5, 10 };
  for (var s = 0; s < 20; s++) if (s != 5 && s != 10) order.Add(s);
  var rows = order.Select((slot, i) => new SellListRow(
    i, slot,
    slot == 5 ? ItemNameMatch.Resolve("Snow Cotton", "Snow Cotton", Catalogue, Ushanka) : (slot == 10 ? IronOre : 5594u),
    prices[slot] == PH ? Placeholder : 500L)).ToList();

  var matched = SellListRows.MatchBySlot(rows, market, scan.Targets, out var why);
  Check("end-to-end: exactly one row is queued for pricing", matched?.Count == 1, why ?? "matched");
  Check("end-to-end: it is row 1 / slot #10 / Iron Ore",
    matched != null && matched[0].Row == 1 && matched[0].Slot == 10 && matched[0].ItemId == IronOre);
  Check("end-to-end: the row holding the foreign placeholder listing is never queued",
    matched != null && matched.All(m => m.Slot != 4));
  Check("end-to-end: the row count check that guards all of this still passes on a 20/20 retainer",
    MarketRowMap.RowCountAgrees(market, rows.Count));
}

// 31. How much of a retainer an Auto-Market pass may re-price. Joey, 2026-09-05 22:02: "It did the first
//     retainer correctly. none of the other retainers needed auto-market b/c they were full. and so it
//     re-pinched all of their items." A retainer this run listed NOTHING into must get nothing priced.
{
  // The condition that shipped from 0.1.0.0 (58e882000) through 0.1.6.0, reproduced verbatim so that "the old
  // code did the wrong thing" is MEASURED here rather than asserted from memory. Without this half, the case
  // below would pass just as happily on a no-op.
  static bool OldWouldRePassEverything(bool pinchAllAfter, int listedThisRetainer)
    => pinchAllAfter || listedThisRetainer == 0;

  Check("scope: OLD - a retainer nothing was listed into got its entire board re-priced (this was the bug)",
    OldWouldRePassEverything(false, 0));
  Check("scope: NEW - a retainer nothing was listed into is left completely alone",
    PinchScope.Decide(false, 0) == PinchAfterMarket.Nothing);

  Check("scope: a retainer that DID receive listings prices only those listings",
    PinchScope.Decide(false, 3) == PinchAfterMarket.NewListingsOnly);
  Check("scope: ...which is what the old condition did too, so a retainer that worked is unchanged",
    !OldWouldRePassEverything(false, 3));

  // NEGATIVE CONTROL. "Pinch everything after listing" is an explicit opt-in and must still mean exactly
  // that. Without these two, every assertion above is satisfied by a Decide() that always returns Nothing.
  Check("scope: NEGATIVE CONTROL - 'Pinch everything after listing' ON still re-prices the whole retainer",
    PinchScope.Decide(true, 0) == PinchAfterMarket.FullRePass);
  Check("scope: NEGATIVE CONTROL - ...including on a retainer that did receive new listings",
    PinchScope.Decide(true, 7) == PinchAfterMarket.FullRePass);

  Check("scope: the three outcomes are distinct and nothing falls through to a re-pass by default",
    Enum.GetValues<PinchAfterMarket>().Length == 3
      && PinchScope.Decide(false, 0) != PinchAfterMarket.FullRePass
      && PinchScope.Decide(false, 1) != PinchAfterMarket.FullRePass);

  // Joey's sweep, 22:27:29 -> 22:30:03: 3 listings, board full, 1 listing, board full.
  var sweep = new[] { 3, 0, 1, 0 };
  var decided = sweep.Select(n => PinchScope.Decide(false, n)).ToList();
  Check("sweep replay: 2 of the 4 retainers price their new listings and 2 price nothing at all",
    decided.Count(d => d == PinchAfterMarket.NewListingsOnly) == 2
      && decided.Count(d => d == PinchAfterMarket.Nothing) == 2,
    string.Join(",", decided));
  Check("sweep replay: not one retainer in that sweep triggers a full re-pass",
    decided.All(d => d != PinchAfterMarket.FullRePass));
  Check("sweep replay: the old condition full-re-passed exactly the two full retainers - the 2 he saw",
    sweep.Count(n => OldWouldRePassEverything(false, n)) == 2);
}

// 32. Empty-board fallback: median of the recent data-centre sales, with a staleness guard.
//     Joey, 2026-09-06 (Helm t-joey-1788708564633, option A "median-with-staleness-guard"):
//     "When auto-marketing something that has nothing else on the board, it should set the
//     universalis suggested price." Universalis has no such field, so this is what we build instead.
//     Every number below is a REAL measurement taken from Universalis on 2026-09-06, not a fixture.
{
  const long Now = 1788710000L;            // 2026-09-06, the day these were sampled
  const long Day = 86400L;

  static SaleHistoryEntry S(long price, long ts, bool hq = true) => new(price, ts, hq);

  // --- item 16644, empty Aether board, the last 10 HQ data-centre sales, verbatim ---
  var item16644 = new List<SaleHistoryEntry>
  {
    S(60000, 1788628902), S(200000, 1788587792), S(50000, 1788539165), S(120000, 1788461842),
    S(100000, 1788400000), S(40000, 1788300000), S(54100, 1788200000), S(50000, 1788100000),
    S(53000, 1788000000), S(49999, 1787900000),
  };

  var r16644 = SaleHistoryPricing.Evaluate(item16644, Now, 30, hqOnly: true);
  Check("history: item 16644 (traded today) gets a price", r16644.Outcome == SaleHistoryOutcome.Priced, r16644.Outcome.ToString());
  Check("history: item 16644 prices at the MEDIAN 53,550, not the 1,824,207 Universalis average",
    r16644.UnitPrice == 53550, $"got {r16644.UnitPrice}");
  Check("history: ...and the median came from all 10 sales", r16644.SampleCount == 10, $"n={r16644.SampleCount}");

  // NEGATIVE CONTROL. Without this, an Evaluate() that just returned the cheapest sale (40,000) or the
  // newest (60,000) would pass everything above. The median is a specific number and it is asserted as one.
  Check("history: NEGATIVE CONTROL - the median is not the newest sale, the cheapest, or the mean",
    r16644.UnitPrice != 60000 && r16644.UnitPrice != 40000 && r16644.UnitPrice != 77709);

  // --- item 30037, empty board, newest sale JUNE 2022 - must be refused, not priced ---
  var item30037 = new List<SaleHistoryEntry>
  {
    S(300001, 1655615224), S(999999, 1653745228), S(500001, 1651682284), S(300000, 1648061772),
  };
  var r30037 = SaleHistoryPricing.Evaluate(item30037, Now, 30, hqOnly: true);
  Check("history: item 30037 (newest sale 2022) is REFUSED, not priced off a four-year-old sale",
    r30037.Outcome == SaleHistoryOutcome.Stale && r30037.UnitPrice == 0, $"{r30037.Outcome}/{r30037.UnitPrice}");
  Check("history: the refusal still reports the newest sale it saw, so the log can say how old it is",
    r30037.NewestUnixSeconds == 1655615224);

  // --- item 5256: empty board AND no history at any scope. Distinct from stale. ---
  var rNone = SaleHistoryPricing.Evaluate(new List<SaleHistoryEntry>(), Now, 30, hqOnly: false);
  Check("history: an item with no sales at all reports NoHistory, not Stale",
    rNone.Outcome == SaleHistoryOutcome.NoHistory && rNone.UnitPrice == 0);
  Check("history: a null history is the same as an empty one and never throws",
    SaleHistoryPricing.Evaluate(null, Now, 30, false).Outcome == SaleHistoryOutcome.NoHistory);

  // --- the boundary itself ---
  var edge = new List<SaleHistoryEntry> { S(1000, Now - (30 * Day) + 60), S(4000, Now - (200 * Day)) };
  var rEdge = SaleHistoryPricing.Evaluate(edge, Now, 30, hqOnly: true);
  Check("history: a sale just inside 30 days counts", rEdge.Outcome == SaleHistoryOutcome.Priced && rEdge.SampleCount == 1, $"{rEdge.Outcome} n={rEdge.SampleCount}");
  Check("history: ...and the 200-day-old sale is excluded from the median", rEdge.UnitPrice == 1000, $"got {rEdge.UnitPrice}");
  Check("history: a sale one minute the WRONG side of 30 days is refused",
    SaleHistoryPricing.Evaluate([S(1000, Now - (30 * Day) - 60)], Now, 30, true).Outcome == SaleHistoryOutcome.Stale);

  // --- HQ/NQ must not be mixed: the listing being priced is one or the other ---
  var mixed = new List<SaleHistoryEntry> { S(900, Now - Day, hq: true), S(10, Now - Day, hq: false), S(12, Now - Day, hq: false) };
  Check("history: HQ pricing ignores NQ sales", SaleHistoryPricing.Evaluate(mixed, Now, 30, hqOnly: true).UnitPrice == 900);
  Check("history: NQ pricing (hqOnly off) uses everything Universalis returned",
    SaleHistoryPricing.Evaluate(mixed, Now, 30, hqOnly: false).SampleCount == 3);

  // --- arithmetic: even sample, and junk input ---
  Check("history: an even sample takes the whole-gil floor of the two middle sales",
    SaleHistoryPricing.Evaluate([S(101, Now - Day), S(102, Now - Day), S(200, Now - Day), S(300, Now - Day)], Now, 30, true).UnitPrice == 151);
  Check("history: a zero-price sale is not a data point",
    SaleHistoryPricing.Evaluate([S(0, Now - Day), S(500, Now - Day)], Now, 30, true).SampleCount == 1);
  Check("history: a sale timestamped in the future is discarded rather than trusted",
    SaleHistoryPricing.Evaluate([S(500, Now + (10 * Day))], Now, 30, true).Outcome == SaleHistoryOutcome.NoHistory);
  Check("history: the price is never zero or negative when a sale was priced",
    SaleHistoryPricing.Evaluate([S(1, Now - Day)], Now, 30, true).UnitPrice == 1);

  // --- the window is clamped, so no config value can switch the guard off ---
  Check("history: a window of 0 days is clamped to 1, not treated as 'no guard'",
    SaleHistoryPricing.Evaluate([S(500, Now - (5 * Day))], Now, 0, true).Outcome == SaleHistoryOutcome.Stale);
  Check("history: a 10-year window is clamped to 365 days, so 2022 sales stay refused",
    SaleHistoryPricing.Evaluate(item30037, Now, 3650, true).Outcome == SaleHistoryOutcome.Stale);
  Check("history: a legitimate wide window (365 d) does price an item that sold 100 days ago",
    SaleHistoryPricing.Evaluate([S(777, Now - (100 * Day))], Now, 365, true).UnitPrice == 777);

  // --- the shipped defaults are the ones Joey chose ---
  Check("history: the shipped freshness window is the 30 days on the decision card",
    SaleHistoryPricing.DefaultMaxAgeDays == 30);
  Check("history: the shipped sample size is 20 recent sales",
    SaleHistoryPricing.DefaultEntryCount == 20);
}

// 32. The one price formula, shared by the pricing pass and the Auto Pinch pre-flight (0.1.9.0).
//     PriceMath.Candidate was lifted verbatim out of UniversalisPriceProvider.CalculateNewPrice. If the two
//     ever disagreed, the pre-flight would skip a row the pass would in fact have re-priced - a silent wrong
//     skip, which costs a sale. So the pre-0.1.9.0 formula is reproduced HERE, inline, and the shipped one is
//     measured against it rather than trusted.
{
  static int OldCalculateNewPrice(long pricePerUnit, bool ownRetainer, UndercutMode mode, int undercutAmount, bool undercutSelf)
  {
    var price = (int)Math.Min(pricePerUnit, int.MaxValue);
    if (!undercutSelf && ownRetainer)
      return price;
    if (mode == UndercutMode.FixedAmount)
      return Math.Max(price - undercutAmount, 1);
    return (int)Math.Max((100L - undercutAmount) * price / 100L, 1);
  }

  long[] prices = [1L, 2L, 25L, 243L, 400L, 999L, 30971L, 1_500_000L, int.MaxValue, (long)int.MaxValue + 5000L];
  int[] amounts = [0, 1, 5, 99];
  var mismatches = new List<string>();
  var total = 0;
  foreach (var price in prices)
    foreach (var mode in Enum.GetValues<UndercutMode>())
      foreach (var amount in amounts)
        foreach (var own in new[] { false, true })
          foreach (var undercutSelf in new[] { false, true })
          {
            total++;
            var expected = OldCalculateNewPrice(price, own, mode, amount, undercutSelf);
            var actual = PriceMath.Candidate(price, own, mode, amount, undercutSelf);
            if (expected != actual)
              mismatches.Add($"{price}/{mode}/{amount}/own={own}/self={undercutSelf}: {expected} != {actual}");
          }

  Check($"pricemath: shared formula matches the pre-0.1.9.0 inline one over all {total} inputs",
    mismatches.Count == 0, string.Join("; ", mismatches.Take(5)));

  // NEGATIVE CONTROL: the table above only means something if these inputs actually produce different
  // answers. A Candidate() that returned a constant would pass a same-vs-same comparison too.
  Check("pricemath: the table exercises inputs that really do differ",
    PriceMath.Candidate(1000, false, UndercutMode.FixedAmount, 5, false) == 995
      && PriceMath.Candidate(1000, false, UndercutMode.Percentage, 5, false) == 950
      && PriceMath.Candidate(1000, true, UndercutMode.FixedAmount, 5, false) == 1000
      && PriceMath.Candidate(1000, true, UndercutMode.FixedAmount, 5, true) == 995);
  Check("pricemath: matching own listing never returns 0 or a negative price",
    PriceMath.Candidate(1, false, UndercutMode.FixedAmount, 99, false) == 1
      && PriceMath.Candidate(1, false, UndercutMode.Percentage, 99, false) == 1);
}

// 33. Auto Pinch pre-flight: replay Joey's 2026-09-06 11:26-11:36 sweep.
//     55 rows were priced: 16 new listings (placeholder -> real, not this feature's business) and 39 EXISTING
//     listings re-priced. 17 of those 39 came out at exactly the price they already had, and 3 moved by a
//     rounding error (243->242, 400->399, 30971->30951). Median 10.5 s per row, so ~3 min of a 9.5-min sweep
//     bought nothing.
{
  const long Now = 1_788_708_000_000L;  // fixed clock: these cases must not depend on when they run
  const long OneHour = 3_600_000L;

  var options = new PinchPreflightOptions(
    Enabled: true, FreshnessHours: 6, SkipUnderGil: 0, SkipUnderPercent: 1.0f,
    PreferHq: true, Mode: UndercutMode.FixedAmount, UndercutAmount: 0, UndercutSelf: false);

  var rows = new List<PinchRow>();
  var quotes = new Dictionary<uint, ItemQuote>();

  void Existing(uint itemId, long current, long boardLowest, bool boardIsOwn, bool hq = false, long ageMs = OneHour)
  {
    var row = rows.Count;
    rows.Add(new PinchRow(row, row, itemId, hq, current, false));
    quotes[itemId] = new ItemQuote(itemId, true, Now - ageMs, [new QuoteListing(boardLowest, hq, boardIsOwn)]);
  }

  // The 17 no-ops: he is the cheapest on the data centre and "Match Self" is off, so the matched price is
  // the price already on his listing. This is the whole reason the feature exists.
  long[] alreadyRight = [98L, 243L, 400L, 1_200L, 2_500L, 3_333L, 7_800L, 9_999L, 12_000L, 15_500L,
                         18_250L, 21_000L, 24_800L, 30_951L, 44_000L, 61_500L, 120_000L];
  for (var i = 0; i < alreadyRight.Length; i++)
    Existing((uint)(3000 + i), alreadyRight[i], alreadyRight[i], boardIsOwn: true);

  // The 3 rounding-error moves, from his log. Someone else is 1 gil (or 20 gil) cheaper.
  Existing(4001, 243L, 242L, boardIsOwn: false);
  Existing(4002, 400L, 399L, boardIsOwn: false);
  Existing(4003, 30_971L, 30_951L, boardIsOwn: false);

  // 19 rows genuinely worth walking: a real undercut by somebody else.
  for (var i = 0; i < 19; i++)
    Existing((uint)(5000 + i), 10_000L + i * 500L, 8_000L + i * 500L, boardIsOwn: false);

  Check("preflight replay: the fixture is his 39 existing rows", rows.Count == 39, $"rows={rows.Count}");

  var decisions = PinchPreflight.Decide(rows, quotes, options, Now);

  // Re-derived from the fixture rather than trusted from the card: the % move of each rounding row against
  // the 1% default. 1/243 = 0.41%, 1/400 = 0.25%, 20/30971 = 0.065% - all three under 1%.
  Check("preflight replay: all three rounding rows really are under the 1% default",
    100.0 * 1 / 243 < 1.0 && 100.0 * 1 / 400 < 1.0 && 100.0 * 20 / 30_971 < 1.0);

  Check("preflight replay: exactly 17 rows are skipped as already at the right price",
    decisions.Count(d => d.Verdict == PinchVerdict.SkipAlreadyRight) == 17,
    $"{decisions.Count(d => d.Verdict == PinchVerdict.SkipAlreadyRight)}");
  Check("preflight replay: exactly 3 rows are skipped as under the threshold",
    decisions.Count(d => d.Verdict == PinchVerdict.SkipUnderThreshold) == 3,
    $"{decisions.Count(d => d.Verdict == PinchVerdict.SkipUnderThreshold)}");
  Check("preflight replay: exactly 19 rows are still walked",
    decisions.Count(d => d.Verdict == PinchVerdict.Walk) == 19,
    $"{decisions.Count(d => d.Verdict == PinchVerdict.Walk)}");
  Check("preflight replay: the three threshold skips are his three rounding rows",
    decisions.Where(d => d.Verdict == PinchVerdict.SkipUnderThreshold).Select(d => d.Row.ItemId).OrderBy(i => i).SequenceEqual([4001u, 4002u, 4003u]));
  Check("preflight replay: every row walked is one where the price would really move",
    decisions.Where(d => d.Verdict == PinchVerdict.Walk).All(d => d.Candidate != d.Row.CurrentPrice));

  // THE CONTROL. Before 0.1.9.0 there was no pre-flight at all: the pass walked every row of the list. Without
  // this half, "17 skipped" would pass just as happily against a fixture that never had 39 rows in it.
  Check("preflight replay: CONTROL - the old pass walked all 39 of these rows",
    rows.Count == 39 && PinchPreflight.Decide(rows, quotes, options with { Enabled = false }, Now).Count(d => d.Verdict == PinchVerdict.Walk) == 39);
  Check("preflight replay: 20 of 39 rows saved, at his measured 10.5s per row",
    decisions.Count(d => d.Verdict != PinchVerdict.Walk) == 20);

  // The log line this feature is graded by, character-for-character.
  Check("preflight replay: the summary log line names what was skipped and why",
    PinchPreflight.Summarize(decisions, 6)
      == "pinch pre-flight: walking 19 of 39 row(s); skipped 17 already at the right price, 3 under the threshold (Universalis data <=6h old)",
    PinchPreflight.Summarize(decisions, 6));
  Check("preflight replay: with nothing skipped the line says so instead of listing zero reasons",
    PinchPreflight.Summarize(PinchPreflight.Decide(rows, quotes, options with { Enabled = false }, Now), 6)
      == "pinch pre-flight: walking 39 of 39 row(s); skipped nothing (Universalis data <=6h old)");
}

// 34. Every uncertainty walks the row. These are the cases where a skip would cost a sale, so each one is
//     asserted against an input whose CANDIDATE MATCHES - i.e. the only thing keeping the row alive is the
//     rule under test.
{
  const long Now = 1_788_708_000_000L;
  const long OneHour = 3_600_000L;
  // Placeholder (999_999_999) is the file-level const declared for the new-only pinch cases above.

  var options = new PinchPreflightOptions(true, 6, 0, 1.0f, true, UndercutMode.FixedAmount, 0, false);

  ItemQuote Quote(uint id, long price, bool own = true, long ageMs = OneHour, bool hq = false, bool hasData = true)
    => new(id, hasData, Now - ageMs, [new QuoteListing(price, hq, own)]);

  PinchVerdict One(PinchRow row, IReadOnlyDictionary<uint, ItemQuote> quotes, PinchPreflightOptions? opts = null)
    => PinchPreflight.Decide([row], quotes, opts ?? options, Now)[0].Verdict;

  // 1 - a placeholder-priced listing is NEVER skipped, even when its candidate equals its current price.
  var placeholderRow = new PinchRow(0, 0, 6001, false, Placeholder, true);
  Check("preflight: a new listing at the placeholder price is never skipped",
    One(placeholderRow, new Dictionary<uint, ItemQuote> { [6001] = Quote(6001, Placeholder) }) == PinchVerdict.Walk);
  Check("preflight: ...and the SAME price on a normal listing IS skipped, so the placeholder rule is what saved it",
    One(placeholderRow with { IsPlaceholder = false }, new Dictionary<uint, ItemQuote> { [6001] = Quote(6001, Placeholder) }) == PinchVerdict.SkipAlreadyRight);

  // 2 - an unreadable row.
  Check("preflight: a row with no readable item id is walked",
    One(new PinchRow(0, 0, 0, false, 500, false), new Dictionary<uint, ItemQuote> { [6002] = Quote(6002, 500) }) == PinchVerdict.Walk);
  Check("preflight: a row with no readable price is walked",
    One(new PinchRow(0, 0, 6002, false, 0, false), new Dictionary<uint, ItemQuote> { [6002] = Quote(6002, 0) }) == PinchVerdict.Walk);

  // 3 - Universalis has nothing usable.
  var row6003 = new PinchRow(0, 0, 6003, false, 500, false);
  Check("preflight: no quote for the item is walked",
    One(row6003, new Dictionary<uint, ItemQuote>()) == PinchVerdict.Walk);
  Check("preflight: hasData=false is walked",
    One(row6003, new Dictionary<uint, ItemQuote> { [6003] = Quote(6003, 500, hasData: false) }) == PinchVerdict.Walk);
  Check("preflight: a quote with no listings at all is walked",
    One(row6003, new Dictionary<uint, ItemQuote> { [6003] = new ItemQuote(6003, true, Now - OneHour, []) }) == PinchVerdict.Walk);
  Check("preflight: an HQ row with only NQ listings on the board is walked",
    One(row6003 with { HQ = true }, new Dictionary<uint, ItemQuote> { [6003] = Quote(6003, 500, hq: false) }) == PinchVerdict.Walk);
  Check("preflight: CONTROL - that same HQ row with an HQ listing at its price IS skipped",
    One(row6003 with { HQ = true }, new Dictionary<uint, ItemQuote> { [6003] = Quote(6003, 500, hq: true) }) == PinchVerdict.SkipAlreadyRight);

  // 4 - stale data. 7h old against a 6h window, with a candidate that matches.
  Check("preflight: a quote 7h old with the window at 6h is walked even though the candidate matches",
    One(row6003, new Dictionary<uint, ItemQuote> { [6003] = Quote(6003, 500, ageMs: 7 * OneHour) }) == PinchVerdict.Walk);
  Check("preflight: CONTROL - the same quote 5h old is skipped, so staleness is what walked it",
    One(row6003, new Dictionary<uint, ItemQuote> { [6003] = Quote(6003, 500, ageMs: 5 * OneHour) }) == PinchVerdict.SkipAlreadyRight);
  Check("preflight: a 7h-old quote is skipped once the window is widened to 12h",
    One(row6003, new Dictionary<uint, ItemQuote> { [6003] = Quote(6003, 500, ageMs: 7 * OneHour) }, options with { FreshnessHours = 12 }) == PinchVerdict.SkipAlreadyRight);
  Check("preflight: a quote with no lastUploadTime at all is walked",
    One(row6003, new Dictionary<uint, ItemQuote> { [6003] = new ItemQuote(6003, true, 0, [new QuoteListing(500, false, true)]) }) == PinchVerdict.Walk);

  // 5 - HQ selection: an HQ row prices off the HQ listings, not the cheaper NQ ones.
  var mixed = new Dictionary<uint, ItemQuote>
  {
    [6004] = new ItemQuote(6004, true, Now - OneHour, [new QuoteListing(100, false, false), new QuoteListing(900, true, false)]),
  };
  var hqRow = new PinchRow(0, 0, 6004, true, 900, false);
  Check("preflight: an HQ row with 'Use HQ price' on prices off the HQ listing (900), not the NQ one (100)",
    One(hqRow, mixed) == PinchVerdict.SkipAlreadyRight);
  Check("preflight: ...and with 'Use HQ price' OFF the same row prices off the cheapest listing of any quality",
    PinchPreflight.Decide([hqRow], mixed, options with { PreferHq = false }, Now)[0].Candidate == 100);
  Check("preflight: an NQ row always prices off the cheapest listing of any quality",
    PinchPreflight.Decide([hqRow with { HQ = false }], mixed, options, Now)[0].Candidate == 100);

  // 6 - own-retainer lowest with Match Self off, and the negative control with it on. Undercut amount 5 gil,
  //     because at the exact-match default (0 gil) BOTH settings return the same number and the control would
  //     prove nothing.
  var self = options with { UndercutAmount = 5 };
  var ownQuote = new Dictionary<uint, ItemQuote> { [6005] = Quote(6005, 100, own: true) };
  var ownRow = new PinchRow(0, 0, 6005, false, 100, false);
  Check("preflight: own listing lowest with Match Self OFF means the candidate is that same price - skipped",
    One(ownRow, ownQuote, self) == PinchVerdict.SkipAlreadyRight);
  Check("preflight: NEGATIVE CONTROL - the identical input with Match Self ON drops the price and is walked",
    One(ownRow, ownQuote, self with { UndercutSelf = true }) == PinchVerdict.Walk
      && PinchPreflight.Decide([ownRow], ownQuote, self with { UndercutSelf = true }, Now)[0].Candidate == 95);
  Check("preflight: a STRANGER at that same price is walked with Match Self off, not skipped",
    One(ownRow, new Dictionary<uint, ItemQuote> { [6005] = Quote(6005, 100, own: false) }, self) == PinchVerdict.Walk);

  // 7 - thresholds. Both at 0 means the feature is limited to the already-right case.
  var noThreshold = options with { SkipUnderPercent = 0f, SkipUnderGil = 0 };
  var nearRow = new PinchRow(0, 0, 6006, false, 400, false);
  var nearQuote = new Dictionary<uint, ItemQuote> { [6006] = Quote(6006, 399, own: false) };
  Check("preflight: with both thresholds at 0, a 1-gil move on 400 is walked",
    One(nearRow, nearQuote, noThreshold) == PinchVerdict.Walk);
  Check("preflight: with both thresholds at 0, no row is ever skipped for being under a threshold",
    PinchPreflight.Decide([nearRow], nearQuote, noThreshold, Now).All(d => d.Verdict != PinchVerdict.SkipUnderThreshold));
  Check("preflight: a gil threshold of 5 skips that same 1-gil move",
    One(nearRow, nearQuote, noThreshold with { SkipUnderGil = 5 }) == PinchVerdict.SkipUnderThreshold);
  Check("preflight: a gil threshold of 5 does NOT skip a 5-gil move (the boundary is exclusive)",
    One(new PinchRow(0, 0, 6006, false, 400, false), new Dictionary<uint, ItemQuote> { [6006] = Quote(6006, 395, own: false) }, noThreshold with { SkipUnderGil = 5 }) == PinchVerdict.Walk);
  Check("preflight: a 1% threshold skips 1 gil on 400 (0.25%) and walks 20 gil on 400 (5%)",
    One(nearRow, nearQuote, noThreshold with { SkipUnderPercent = 1.0f }) == PinchVerdict.SkipUnderThreshold
      && One(nearRow, new Dictionary<uint, ItemQuote> { [6006] = Quote(6006, 380, own: false) }, noThreshold with { SkipUnderPercent = 1.0f }) == PinchVerdict.Walk);
  Check("preflight: a price INCREASE is measured the same way (nobody is undercutting any more)",
    One(nearRow, new Dictionary<uint, ItemQuote> { [6006] = Quote(6006, 402, own: false) }, noThreshold with { SkipUnderPercent = 1.0f }) == PinchVerdict.SkipUnderThreshold
      && One(nearRow, new Dictionary<uint, ItemQuote> { [6006] = Quote(6006, 800, own: false) }, noThreshold with { SkipUnderPercent = 1.0f }) == PinchVerdict.Walk);

  // 8 - the master switch, and the per-item price limit applied before the comparison.
  Check("preflight: with the feature off, every row is walked whatever the board says",
    PinchPreflight.Decide([ownRow, nearRow], nearQuote, options with { Enabled = false }, Now).All(d => d.Verdict == PinchVerdict.Walk));
  Check("preflight: a per-item minimum that clamps the candidate back to the current price makes the row a skip",
    PinchPreflight.Decide([new PinchRow(0, 0, 6007, false, 500, false)],
      new Dictionary<uint, ItemQuote> { [6007] = Quote(6007, 300, own: false) }, options, Now,
      (_, price) => Math.Max(price, 500))[0].Verdict == PinchVerdict.SkipAlreadyRight);
  Check("preflight: CONTROL - without that limit the same row is walked",
    One(new PinchRow(0, 0, 6007, false, 500, false), new Dictionary<uint, ItemQuote> { [6007] = Quote(6007, 300, own: false) }) == PinchVerdict.Walk);

  // 9 - MIRROR MODE (0.1.10.0). AllaganMarket colours a row red only when the cheapest listing that is NOT
  //     one of your own retainers undercuts you. Every case below is paired with the SAME input under
  //     mirror OFF, so a pass proves the mirror flag is what changed the verdict and not the fixture.
  var mirror = options with { MirrorOverlay = true };

  // 9a - the headline case: a stranger sits below one of your own retainers. Without mirror the pre-flight
  //      predicts undercutting that stranger and walks; with mirror the row is judged against the stranger
  //      too, so it still walks. Undercut only by YOURSELF is the case that changes.
  var twoOwn = new Dictionary<uint, ItemQuote>
  {
    [6100] = new(6100, true, Now - OneHour, [new QuoteListing(50, false, true), new QuoteListing(90, false, false)]),
  };
  var undercutBySelfRow = new PinchRow(0, 0, 6100, false, 80, false);
  Check("preflight mirror: undercut only by your OWN retainer is skipped as not-undercut",
    One(undercutBySelfRow, twoOwn, mirror) == PinchVerdict.SkipNotUndercut);
  Check("preflight mirror: NEGATIVE CONTROL - the identical input with mirror OFF is walked",
    One(undercutBySelfRow, twoOwn, options) == PinchVerdict.Walk);

  // 9b - a real stranger undercut is still walked under mirror. Mirror must not suppress the case the
  //      whole feature exists to catch.
  var strangerBelow = new Dictionary<uint, ItemQuote>
  {
    [6101] = new(6101, true, Now - OneHour, [new QuoteListing(60, false, false), new QuoteListing(95, false, true)]),
  };
  Check("preflight mirror: a STRANGER below your price is still walked (this is AllaganMarket red)",
    One(new PinchRow(0, 0, 6101, false, 80, false), strangerBelow, mirror) == PinchVerdict.Walk);

  // 9c - board holds nothing but your own listings: there is nobody to undercut at all.
  var allOwn = new Dictionary<uint, ItemQuote>
  {
    [6102] = new(6102, true, Now - OneHour, [new QuoteListing(70, false, true), new QuoteListing(75, false, true)]),
  };
  Check("preflight mirror: a board holding only your own listings is skipped, not walked",
    One(new PinchRow(0, 0, 6102, false, 80, false), allOwn, mirror) == PinchVerdict.SkipNotUndercut);
  Check("preflight mirror: NEGATIVE CONTROL - the same all-yours board with mirror OFF is walked",
    One(new PinchRow(0, 0, 6102, false, 80, false), allOwn, options) == PinchVerdict.Walk);

  // 9d - a stranger AT your price is not an undercut. AllaganMarket compares strictly below.
  var strangerEqual = new Dictionary<uint, ItemQuote> { [6103] = Quote(6103, 80, own: false) };
  Check("preflight mirror: a stranger AT your exact price is not an undercut, so the row is skipped",
    One(new PinchRow(0, 0, 6103, false, 80, false), strangerEqual, mirror) == PinchVerdict.SkipNotUndercut);

  // 9e - mirror is inert when the user asked to undercut their own retainers, because then their own
  //      listings ARE competition and ignoring them would mispredict the pass - a wrong skip.
  var selfOn = mirror with { UndercutSelf = true, UndercutAmount = 5 };
  Check("preflight mirror: with Undercut-my-own-retainers ON, mirror is inert and the row is walked",
    One(undercutBySelfRow, twoOwn, selfOn) == PinchVerdict.Walk);

  // 9f - every uncertainty rule still outranks mirror. Stale data must not become a not-undercut skip.
  var staleAllOwn = new Dictionary<uint, ItemQuote>
  {
    [6104] = new(6104, true, Now - (7 * OneHour), [new QuoteListing(70, false, true)]),
  };
  Check("preflight mirror: stale data still walks the row, mirror does not outrank the freshness gate",
    One(new PinchRow(0, 0, 6104, false, 80, false), staleAllOwn, mirror) == PinchVerdict.Walk);
  Check("preflight mirror: a placeholder-priced listing is still never skipped under mirror",
    One(new PinchRow(0, 0, 6102, false, Placeholder, true), allOwn, mirror) == PinchVerdict.Walk);

  // 9g - the summary line names the new bucket so the feature is gradable from Joey's log.
  Check("preflight mirror: the summary line reports the not-undercut skips",
    PinchPreflight.Summarize(PinchPreflight.Decide([undercutBySelfRow], twoOwn, mirror, Now), 6)
      .Contains("1 not undercut by anyone else"));
}

// 35. The Universalis payload comes back in TWO shapes from the same endpoint (verified live 2026-09-06):
//     several ids give {"itemIDs":[..],"items":{"<id>":{..}}}, ONE id gives the flat single-item object with
//     no "items" key at all. Both bodies below are trimmed captures of real responses. Get this wrong and a
//     retainer with one listing left silently gets no pre-flight.
{
  const string MultiBody = """
  {"itemIDs":[5111,5594],"items":{"5111":{"itemID":5111,"lastUploadTime":1788710113343,"listings":[{"pricePerUnit":25,"quantity":99,"hq":false,"retainerID":"33777097243891520","worldName":"Cactuar"},{"pricePerUnit":30,"quantity":50,"hq":true,"retainerID":"12345678901234567","worldName":"Jenova"}],"minPrice":25,"minPriceNQ":25,"minPriceHQ":0,"hasData":true},"5594":{"itemID":5594,"lastUploadTime":1788710000000,"listings":[],"minPrice":0,"hasData":false}},"dcName":"Aether","unresolvedItems":[]}
  """;

  const string SingleBody = """
  {"itemID":5111,"lastUploadTime":1788708183917,"listings":[{"pricePerUnit":42,"quantity":10,"hq":false,"retainerID":"33777097243891520","worldName":"Cactuar"}],"minPrice":42,"minPriceNQ":42,"minPriceHQ":0,"hasData":true,"dcName":"Aether"}
  """;

  var own = new List<ulong> { 33777097243891520UL };

  var multi = UniversalisQuotes.Parse(MultiBody, own);
  Check("universalis: the multi-item shape parses both items", multi.Count == 2, $"count={multi.Count}");
  Check("universalis: multi - lastUploadTime is kept as unix MILLISECONDS, not seconds",
    multi[5111].LastUploadUnixMs == 1788710113343L);
  Check("universalis: multi - listings, quality and price come through",
    multi[5111].Listings.Count == 2 && multi[5111].Listings[0].PricePerUnit == 25 && !multi[5111].Listings[0].Hq && multi[5111].Listings[1].Hq);
  Check("universalis: multi - the user's own retainer id is recognised, a stranger's is not",
    multi[5111].Listings[0].OwnRetainer && !multi[5111].Listings[1].OwnRetainer);
  Check("universalis: multi - hasData=false survives as false", !multi[5594].HasData && multi[5594].Listings.Count == 0);

  var single = UniversalisQuotes.Parse(SingleBody, own);
  Check("universalis: THE GOTCHA - the single-item shape has no 'items' key and still parses",
    single.Count == 1 && single.ContainsKey(5111), $"count={single.Count}");
  Check("universalis: single - price, timestamp and own-retainer flag all come through",
    single[5111].HasData && single[5111].LastUploadUnixMs == 1788708183917L
      && single[5111].Listings.Count == 1 && single[5111].Listings[0].PricePerUnit == 42 && single[5111].Listings[0].OwnRetainer);
  Check("universalis: with no known retainer ids nothing is claimed as the user's own",
    UniversalisQuotes.Parse(SingleBody, null)[5111].Listings[0].OwnRetainer == false);
  Check("universalis: an empty or junk body parses to nothing rather than throwing",
    UniversalisQuotes.Parse("", own).Count == 0 && UniversalisQuotes.Parse("{}", own).Count == 0 && UniversalisQuotes.Parse("[]", own).Count == 0);
  Check("universalis: an unresolved-only multi response parses to nothing",
    UniversalisQuotes.Parse("""{"itemIDs":[1],"items":{},"unresolvedItems":[1]}""", own).Count == 0);
}
// 36. The Auto-Market value gate + listing order (0.1.11.0; vendor leg corrected in 0.1.12.0). Two
//     features, one Universalis fetch, one rule: UNCERTAINTY ALWAYS LISTS. 0.1.12.0 corrects 0.1.11.0's
//     wrong "the retainer cannot vendor" verdict - the retainer sell-items context menu DOES vendor
//     ("Have Retainer Sell Items", Addon row 5480), so a priced at-or-under-threshold item now
//     VENDORS instead of holding back (case 37 owns the vendor belt). This case keeps the LIST
//     polarity: every uncertainty still lists, never vendored, never held.
{
  const long Now = 1_788_710_000_000L;          // fixed "now" so freshness windows are exact
  const long Fresh = 6 * 3_600_000L;            // 6h in ms
  var gate = new GateOptions(true, 1_000, Fresh);
  ItemRule R(uint id, bool hq = false, int stack = 99, int keepB = 0, int keepR = 0, bool bags = true, bool ret = true)
    => new(id, hq, stack, keepB, keepR, 0, bags, ret, 0, 999);

  // --- NetRevenue: the threshold compares NET gil (5% market fee), floored ---
  Check("gate: 100 gil x 10 nets 950 after the fee", MarketGate.NetRevenue(100, 10) == 950);
  Check("gate: the fee floors to whole gil (1 x 1 -> 0)", MarketGate.NetRevenue(1, 1) == 0);
  Check("gate: zero or negative inputs net 0", MarketGate.NetRevenue(0, 10) == 0 && MarketGate.NetRevenue(10, 0) == 0);

  // --- PotentialSellable: total sellable across origins, mirroring the planner's own arithmetic ---
  var stock = new List<StockStack>
  {
    new(StockOrigin.Bags, 0, 0, 5111, false, 99),
    new(StockOrigin.Bags, 0, 1, 5111, false, 30),
    new(StockOrigin.Retainer, 10000, 2, 5111, false, 40),
  };
  // bags 99+30=129 -> floor to stack 5 -> 125; retainer 40 -> 40; total 165
  Check("gate: all sellable when nothing is kept (125 + 40 = 165)",
    MarketGate.PotentialSellable(R(5111, stack: 5), stock, false) == 165);

  // recompute by hand: keep 10 bags => bags 129-10=119 -> floor to 5 => 115; retainer 40 -> 40; total 155
  Check("gate: keep 10 bags, stack 5, partials off -> 115 + 40 = 155",
    MarketGate.PotentialSellable(R(5111, stack: 5, keepB: 10), stock, false) == 155);
  Check("gate: partials on keeps the remainder (119 + 40 = 159)",
    MarketGate.PotentialSellable(R(5111, stack: 5, keepB: 10), stock, true) == 159);
  Check("gate: a disabled origin contributes nothing (retainer off -> 115)",
    MarketGate.PotentialSellable(R(5111, stack: 5, keepB: 10, ret: false), stock, false) == 115);
  Check("gate: HQ stock is not NQ rule's sellable",
    MarketGate.PotentialSellable(R(5111), [new StockStack(StockOrigin.Bags, 0, 0, 5111, true, 99)], false) == 0);

  // --- CheapestUnitPrice: quality selection mirrors the pricing pass ---
  var mixed = new ItemQuote(5111, true, Now, new List<QuoteListing>
  {
    new(200, false, false), new(500, true, false),
  });
  Check("gate: NQ rule takes the cheapest listing regardless of quality", MarketGate.CheapestUnitPrice(mixed, false, true) == 200);
  Check("gate: HQ rule with Use-HQ-price on takes the HQ listing", MarketGate.CheapestUnitPrice(mixed, true, true) == 500);
  Check("gate: HQ rule with Use-HQ-price off prices off any quality", MarketGate.CheapestUnitPrice(mixed, true, false) == 200);
  Check("gate: no listing of the wanted quality is null, not a guess",
    MarketGate.CheapestUnitPrice(new ItemQuote(5111, true, Now, [new(200, false, false)]), true, true) == null);
  Check("gate: hasData=false is null", MarketGate.CheapestUnitPrice(new ItemQuote(5111, false, Now, []), false, true) == null);

  // --- Decide: the polarity battery. Every "cannot tell" LISTS; only fresh + priced + under-threshold holds ---
  var pricedCheap = new ItemQuote(5111, true, Now, [new(10, false, false)]);      // 99 x 10 -> 940 net
  var pricedDear = new ItemQuote(5111, true, Now, [new(900, false, false)]);      // 99 x 900 -> 84,735 net
  var stale = new ItemQuote(5111, true, Now - 7 * 3_600_000L, [new(1, false, false)]);
  var noUploadTs = new ItemQuote(5111, true, 0, [new(1, false, false)]);
  var noListing = new ItemQuote(5111, true, Now, []);

  Check("gate: above threshold lists", MarketGate.Decide(99, pricedDear, false, true, gate, Now) == GateVerdict.List);
  Check("gate: below threshold is VENDORED (0.1.12.0 - the corrected verdict)", MarketGate.Decide(99, pricedCheap, false, true, gate, Now) == GateVerdict.Vendor);
  Check("gate: zero sellable lists regardless of price",
    MarketGate.Decide(0, new ItemQuote(5111, true, Now, [new(1, false, false)]), false, true, gate, Now) == GateVerdict.List);
  // exact-threshold: unit 1000 x qty 1 -> net 950... build it precisely: want net == 1000 -> unit 1053 x 1 -> 1000 (1053*95/100 = 1000.35 -> 1000)
  var exactThousand = new ItemQuote(5111, true, Now, [new(1053, false, false)]);
  Check("gate: net exactly equal to the threshold is VENDORED (strictly more lists)",
    MarketGate.NetRevenue(1053, 1) == 1000 && MarketGate.Decide(1, exactThousand, false, true, gate, Now) == GateVerdict.Vendor);
  var justAbove = new ItemQuote(5111, true, Now, [new(1054, false, false)]);
  Check("gate: one gil above the threshold lists", MarketGate.Decide(1, justAbove, false, true, gate, Now) == GateVerdict.List);

  // THE vendor-polarity cases: uncertain data must LIST, never hold, even at price 1 with threshold 1000
  var oneGil = new ItemQuote(5111, true, Now, [new(1, false, false)]);
  var strictGate = new GateOptions(true, 1_000, Fresh);
  Check("gate: STALE data lists, never vendored for pennies, never held back",
    MarketGate.Decide(99, stale, false, true, strictGate, Now) == GateVerdict.List);
  Check("gate: missing lastUploadTime lists", MarketGate.Decide(99, noUploadTs, false, true, strictGate, Now) == GateVerdict.List);
  Check("gate: hasData=false lists", MarketGate.Decide(99, new ItemQuote(5111, false, Now, []), false, true, strictGate, Now) == GateVerdict.List);
  Check("gate: no listing of the quality lists", MarketGate.Decide(99, noListing, false, true, strictGate, Now) == GateVerdict.List);
  Check("gate: null quote lists", MarketGate.Decide(99, null, false, true, strictGate, Now) == GateVerdict.List);
  Check("gate: gate off lists even the pennies item",
    MarketGate.Decide(99, oneGil, false, true, new GateOptions(false, 1_000, Fresh), Now) == GateVerdict.List);
  Check("gate: threshold 0 is inert (lists)", MarketGate.Decide(99, oneGil, false, true, new GateOptions(true, 0, Fresh), Now) == GateVerdict.List);
  Check("gate: nothing sellable lists (nothing to judge)", MarketGate.Decide(0, oneGil, false, true, strictGate, Now) == GateVerdict.List);

  // --- RuleQuotes: velocity is per-quality, freshness-gated, and 0-velocity is a READING not unknown ---
  var velocityQuote = new ItemQuote(5594, true, Now,
    [new(100_000, false, false), new(120_000, true, false)], NqVelocityPerDay: 55.0, HqVelocityPerDay: 2.5);
  var staleQuote = new ItemQuote(7, true, Now - 7 * 3_600_000L, [new(50, false, false)], 9.9, 9.9);
  var quotes = new Dictionary<uint, ItemQuote> { [5594] = velocityQuote, [7] = staleQuote };
  var rq = MarketGate.RuleQuotes([R(5594), R(5594, hq: true), R(7)], quotes, true, Now, Fresh);
  Check("gate: an NQ rule's quote carries the NQ velocity", rq[0]!.VelocityPerDay == 55.0 && rq[0]!.UnitPrice == 100_000);
  Check("gate: an HQ rule's quote carries the HQ velocity AND the HQ listing's price, not the NQ ones", rq[1]!.VelocityPerDay == 2.5 && rq[1]!.UnitPrice == 120_000);
  Check("gate: a stale quote is null (unrankable), not zero", rq[2] == null);
  var noHqListing = new Dictionary<uint, ItemQuote> { [5594] = new(5594, true, Now, [new(100_000, false, false)], 55.0, 0) };
  Check("gate: no HQ listing on the board -> null quote for the HQ rule",
    MarketGate.RuleQuotes([R(5594, hq: true)], noHqListing, true, Now, Fresh)[0] == null);
  Check("gate: a null quote map ranks nothing (all null)",
    MarketGate.RuleQuotes([R(5111)], null, true, Now, Fresh)[0] == null);

  // --- SortRules: the fixture. A cheap+slow, B dear+fast, C unknown, D mid ---
  var rules = new List<ItemRule> { R(1001), R(1002), R(1003), R(1004) };
  var byRule = new List<RuleQuote?>
  {
    new(10, 0.5),     // A: cheapest, slowest
    new(900, 50.0),   // B: dearest, fastest
    null,             // C: no fresh data
    new(100, 5.0),    // D: mid
  };
  var fastest = MarketGate.SortRules(rules, byRule, MarketSortMode.FastestSellingFirst);
  Check("sort: fastest-first is B, D, A, C", fastest.Select(r => r.ItemId).SequenceEqual([1002u, 1004u, 1001u, 1003u]),
    string.Join(",", fastest.Select(r => r.ItemId)));
  var cheapest = MarketGate.SortRules(rules, byRule, MarketSortMode.CheapestFirst);
  Check("sort: cheapest-first is A, D, B, C", cheapest.Select(r => r.ItemId).SequenceEqual([1001u, 1004u, 1002u, 1003u]));
  var dearest = MarketGate.SortRules(rules, byRule, MarketSortMode.MostExpensiveFirst);
  Check("sort: most-expensive-first is B, D, A, C", dearest.Select(r => r.ItemId).SequenceEqual([1002u, 1004u, 1001u, 1003u]));
  Check("sort: list order returns the input untouched",
    MarketGate.SortRules(rules, byRule, MarketSortMode.ListOrder).Select(r => r.ItemId).SequenceEqual([1001u, 1002u, 1003u, 1004u]));

  // ties and unknown-relative-order are stable (keep list order)
  var tie = new List<ItemRule> { R(2001), R(2002), R(2003) };
  var tieQuotes = new List<RuleQuote?> { new(10, 5), new(10, 5), null };
  Check("sort: a velocity tie keeps list order", MarketGate.SortRules(tie, tieQuotes, MarketSortMode.FastestSellingFirst)
    .Select(r => r.ItemId).SequenceEqual([2001u, 2002u, 2003u]));
  var twoUnknown = new List<ItemRule> { R(3001), R(3002), R(3003) };
  var twoUnknownQuotes = new List<RuleQuote?> { null, new(10, 9), null };
  Check("sort: two unknowns keep their relative order at the end", MarketGate.SortRules(twoUnknown, twoUnknownQuotes, MarketSortMode.FastestSellingFirst)
    .Select(r => r.ItemId).SequenceEqual([3002u, 3001u, 3003u]));

  // THE acceptance shape: sort + scarce slots. 2 free slots, plenty of both items; the sorted order
  // decides who gets them. This is the integration the whole feature exists for.
  {
    var scarce = new List<MarketSlot>();
    for (var i = 0; i < 20; i++) scarce.Add(new MarketSlot(i, i < 18 ? 9999u : 0u, false, i < 18 ? 1 : 0));
    var plenty = new List<StockStack>
    {
      new(StockOrigin.Bags, 0, 0, 1001, false, 99),
      new(StockOrigin.Bags, 0, 1, 1001, false, 99),
      new(StockOrigin.Bags, 0, 2, 1002, false, 99),
      new(StockOrigin.Bags, 0, 3, 1002, false, 99),
    };
    var sorted = MarketGate.SortRules(rules, byRule, MarketSortMode.FastestSellingFirst);
    var plan = AutoMarketPlanner.Plan(sorted.Where(r => r.ItemId is 1001 or 1002).ToList(), plenty, scarce,
      new PlannerOptions(20, 0, true, false));
    Check("sort+slots: with 2 free slots the fastest item takes both", plan.Ops.Count == 2 && plan.Ops.All(o => o.ItemId == 1002),
      string.Join(",", plan.Ops.Select(o => o.ItemId)));
    var listOrderPlan = AutoMarketPlanner.Plan(rules.GetRange(0, 2), plenty, scarce, new PlannerOptions(20, 0, true, false));
    Check("sort+slots: CONTROL - list order gives the slots to the FIRST item instead",
      listOrderPlan.Ops.Count == 2 && listOrderPlan.Ops.All(o => o.ItemId == 1001),
      string.Join(",", listOrderPlan.Ops.Select(o => o.ItemId)));
  }

  // --- the velocity fields survive the payload parse (nqSaleVelocity / hqSaleVelocity) ---
  {
    const string Body = """
    {"itemIDs":[5594],"items":{"5594":{"itemID":5594,"lastUploadTime":1788710113343,"listings":[{"pricePerUnit":100000,"quantity":1,"hq":false}],"minPrice":100000,"nqSaleVelocity":55.5,"hqSaleVelocity":2.25,"hasData":true}},"dcName":"Aether"}
    """;
    var parsed = UniversalisQuotes.Parse(Body, null);
    Check("gate: nqSaleVelocity / hqSaleVelocity come through the parse",
      parsed[5594].NqVelocityPerDay == 55.5 && parsed[5594].HqVelocityPerDay == 2.25,
      $"{parsed[5594].NqVelocityPerDay}/{parsed[5594].HqVelocityPerDay}");
    const string NoVelocity = """
    {"itemIDs":[5594],"items":{"5594":{"itemID":5594,"lastUploadTime":1788710113343,"listings":[],"hasData":true}}}
    """;
    Check("gate: a payload with no velocity fields parses to 0, not an error",
      UniversalisQuotes.Parse(NoVelocity, null)[5594].NqVelocityPerDay == 0);
  }
}


// 37. Retainer vendoring (0.1.12.0) - the gate's vendor leg + planner, from the corrected verdict.
//     Mirrors case 36's uncertainty battery for the VENDOR decision, since vendoring is now real.
//     The wire-side uncertainty is MarketGate.DecideUncertain() which always holds; everything that
//     ACTUALLY reached the priced gate was fresh + priced, so the Vendor polarity pin is on Decide.
{
  const long Now = 1_788_710_000_000L;
  const long Fresh = 6 * 3_600_000L;
  var gate = new GateOptions(true, 1_000, Fresh);
  ItemRule R(uint id, bool hq = false, int stack = 99, int keepB = 0, int keepR = 0, bool bags = true, bool ret = true)
    => new(id, hq, stack, keepB, keepR, 0, bags, ret, 0, 999);

  // --- DecideUncertain: a request that never produced a verdict holds, deciding nothing ---
  Check("vendor: an uncertainty reached the gate without a verdict must NOT vendor",
    MarketGate.DecideUncertain() == GateVerdict.HoldBack);

  // --- the priced Decide returns Vendor, not List, at/under threshold ---
  var cheap = new ItemQuote(5111, true, Now, [new(5, false, false)]);       // 99 x 5 -> 470 net
  Check("vendor: below threshold VENDORS (not holds)", MarketGate.Decide(99, cheap, false, true, gate, Now) == GateVerdict.Vendor);
  Check("vendor: net exactly the threshold vendors", MarketGate.NetRevenue(1053, 1) == 1000
    && MarketGate.Decide(1, new ItemQuote(5111, true, Now, [new(1053, false, false)]), false, true, gate, Now) == GateVerdict.Vendor);
  Check("vendor: just above threshold lists", MarketGate.Decide(1, new ItemQuote(5111, true, Now, [new(1054, false, false)]), false, true, gate, Now) == GateVerdict.List);

  // THE vendor-uncertainty battery, mirrored from case 36: every one LISTS (never vendors)
  Check("vendor: STALE data never vendors", MarketGate.Decide(99, new ItemQuote(5111, true, Now - 7 * 3_600_000L, [new(1, false, false)]), false, true, gate, Now) == GateVerdict.List);
  Check("vendor: no lastUploadTime never vendors", MarketGate.Decide(99, new ItemQuote(5111, true, 0, [new(1, false, false)]), false, true, gate, Now) == GateVerdict.List);
  Check("vendor: hasData=false never vendors", MarketGate.Decide(99, new ItemQuote(5111, false, Now, []), false, true, gate, Now) == GateVerdict.List);
  Check("vendor: no listing of the quality never vendors", MarketGate.Decide(99, new ItemQuote(5111, true, Now, []), false, true, gate, Now) == GateVerdict.List);
  Check("vendor: null quote never vendors", MarketGate.Decide(99, null, false, true, gate, Now) == GateVerdict.List);
  Check("vendor: gate off never vendors", MarketGate.Decide(99, cheap, false, true, new GateOptions(false, 1_000, Fresh), Now) == GateVerdict.List);
  Check("vendor: threshold 0 never vendors", MarketGate.Decide(99, cheap, false, true, new GateOptions(true, 0, Fresh), Now) == GateVerdict.List);
  Check("vendor: zero sellable never vendors", MarketGate.Decide(0, cheap, false, true, gate, Now) == GateVerdict.List);

  // --- ItemVendorPrice: the estimate math ---
  Check("vendor: NQ stock prices at priceLow", ItemVendorPrice.UnitFor(false, false, 154, 1, true) == 1);
  Check("vendor: HQ stock with prefer-HQ prices at priceMid", ItemVendorPrice.UnitFor(true, true, 154, 1, true) == 154);
  Check("vendor: HQ stock with prefer-HQ off prices at priceLow", ItemVendorPrice.UnitFor(false, true, 154, 1, false) == 1);
  Check("vendor: no priceLow is 0 (unpriceable)", ItemVendorPrice.UnitFor(false, false, 154, 0, true) == 0);
  Check("vendor: priceMid=0 with prefer-HQ falls back to priceLow", ItemVendorPrice.UnitFor(false, true, 0, 1, true) == 1);
  Check("vendor: NQ stock of an HQ rule does not price (quality mismatch)", ItemVendorPrice.UnitFor(true, false, 154, 1, true) == 0);
  Check("vendor: zero quantities earn nothing", ItemVendorPrice.Total(154, 0) == 0 && ItemVendorPrice.Total(0, 99) == 0);

  // --- VendorPlanner: stacks -> ops, keeps honoured, re-read by ExecuteVendor (game side) ---
  var stock = new List<StockStack>
  {
    new(StockOrigin.Retainer, 10000, 0, 5111, false, 99),
    new(StockOrigin.Retainer, 10000, 1, 5111, false, 60),
    new(StockOrigin.Bags, 0, 0, 5111, false, 30),
    new(StockOrigin.Bags, 0, 1, 5111, false, 12),
  };
  var prices = new Dictionary<uint, (uint, uint)> { [5111] = (40, 10) };
  var rule = R(5111, stack: 99, keepB: 5, keepR: 100);

  // keep 100 retainer: 159 retainer stock - 100 kept = 59 vendored from the 60-stack; bags keep 5 -> 25 + 12
  var plan = VendorPlanner.Plan([rule], stock, prices, preferHq: true);
  Check("vendor: retainer keep 100 - 159 retainer units minus 100 kept = 59 vendored",
    plan.Ops.Where(o => o.Container == (int)StockOrigin.Retainer).Sum(o => o.Quantity) == 59, string.Join(",", plan.Ops));
  Check("vendor: bags keep 5 -> the 30-stack vendors 25, the 12-stack vendors 12",
    plan.Ops.Where(o => o.Container == (int)StockOrigin.Bags).Sum(o => o.Quantity) == 25 + 12, string.Join(",", plan.Ops));
  Check("vendor: the estimate is unit x qty at priceLow with prefer-HQ on for NQ stock",
    plan.Ops.Sum(o => o.EstGil) == (59 + 25 + 12) * 10);

  // keep bigger than the origin's stock: nothing vendored from that origin
  var keepAll = VendorPlanner.Plan([R(5111, keepR: 999)], stock, prices, true);
  Check("vendor: keep >= stock leaves that origin untouched",
    keepAll.Ops.All(o => o.Container != (int)StockOrigin.Retainer), string.Join(",", keepAll.Ops));
  Check("vendor: keep >= stock in BOTH origins -> no ops",
    VendorPlanner.Plan([R(5111, keepB: 999, keepR: 999)], stock, prices, true).Ops.Count == 0);

  // A kept remainder: the 20 units that stay are the stack's remainder - vendoring never splits a
  // stack (no listing-size floor to respect), so the op is exactly qty 20 from the same slot.
  var keepExactly = new List<StockStack> { new(StockOrigin.Bags, 0, 0, 5111, false, 30) };
  var partialPlan = VendorPlanner.Plan([R(5111, keepB: 10)], keepExactly, prices, true);
  Check("vendor: kept remainder - the 20 units the keep leaves move as ONE op",
    partialPlan.Ops.Count == 1 && partialPlan.Ops[0].Quantity == 20, string.Join(",", partialPlan.Ops));

  // unpriceable item: no sheet row -> no op, one explanatory note
  var noPrice = VendorPlanner.Plan([R(9999)], stock, new Dictionary<uint, (uint, uint)>(), true);
  Check("vendor: an item with no price stays put with a note",
    noPrice.Ops.Count == 0 && noPrice.Notes.Count == 1, string.Join(",", noPrice.Notes));

  // disabled origins contribute nothing
  var halfRule = R(5111, bags: false);
  Check("vendor: SellFromBags=false skips bag stock", VendorPlanner.Plan([halfRule], stock, prices, true).Ops.All(o => o.Container != (int)StockOrigin.Bags));
  var halfRule2 = R(5111, ret: false);
  Check("vendor: SellFromRetainer=false skips retainer stock", VendorPlanner.Plan([halfRule2], stock, prices, true).Ops.All(o => o.Container != (int)StockOrigin.Retainer));
  Check("vendor: both origins disabled -> no ops at all", VendorPlanner.Plan([R(5111, bags: false, ret: false)], stock, prices, true).Ops.Count == 0);
}

Console.WriteLine(failures == 0 ? "OK" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
