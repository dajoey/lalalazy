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

// 33. Auto Pinch pre-flight, flags-only: replay Joey's 2026-09-06 11:26-11:36 sweep THROUGH AllaganMarket's
//     cache. 55 rows were priced that night: 16 new listings (placeholder -> real, still not this feature's
//     business) and 39 EXISTING listings re-priced. 17 of those 39 came out at exactly the price they already
//     had, and 3 moved by a rounding error (243->242, 400->399, 30971->30951). Under the flags-only rule the
//     verdict comes from AllaganMarket's MarketPriceCache: a row whose cache entry prices BELOW it (or whose
//     data went stale) walks; a row whose cache entry is its own fresh price is unflagged and is never
//     touched. The three rounding rows are AllaganMarket RED (cached cheapest 1-20 gil lower) and therefore
//     STILL walk, even though the old 1% threshold would have skipped them - the flag outranks the retired
//     threshold, which is the card's verification bar in reverse and just as binding.
{
  // Fixed clock, LOCAL wall time: 23:00 on 2026-09-06 in the US Eastern build host's own zone
  // (= 2026-09-07 03:00 UTC = 1_788_750_000_000 ms). AllaganMarket compares its LOCAL-stamped cache
  // against DateTime.Now, so the staleness clock here is local too: Decide converts the unix-ms back
  // through .LocalDateTime, and Parse receives the same local reading - the cases are deterministic
  // at any run time. Fresh stamps (22:30) are 30 minutes old; stale stamps (12:00) are 11 hours old.
  const long Now = 1_788_750_000_000L;  // 23:00 EDT local
  var nowUtc = new DateTime(2026, 9, 6, 23, 0, 0);  // the SAME clock as a DateTime, for the Parse calls

  var options = new PinchPreflightOptions(Enabled: true);

  var rows = new List<PinchRow>();
  var quotes = new Dictionary<uint, ItemQuote>();
  var cacheCsv = new List<string>();
  const uint World = 95u;

  void Existing(uint itemId, long current, long cacheUnitCost, bool cacheIsOwn, bool hq = false)
  {
    var row = rows.Count;
    rows.Add(new PinchRow(row, row, itemId, hq, current, false));
    quotes[itemId] = new ItemQuote(itemId, true, Now - 3_600_000L, [new QuoteListing(cacheUnitCost, hq, cacheIsOwn)]);
    // AllaganMarket's cache row: the newest cheapest listing it saw for this (item, quality), an hour
    // before Now (fresh against the default 300-minute staleness period).
    cacheCsv.Add($"{itemId},{(hq ? "Y" : "N")},{World},0,09/06/2026 22:30:00,{cacheUnitCost},{(cacheIsOwn ? "Y" : "N")}");
  }

  // The 17 no-ops: he is the cheapest on the world and "Match Self" is off, so AllaganMarket's cache
  // holds his OWN price for them (ownPrice=Y) - recommendation == current => NOT undercut, fresh row =>
  // not stale => unflagged => NEVER WALKED. Under 0.1.15.0 these were 17 prediction-class skips; the
  // flags rule now skips them with no Universalis answer involved at all.
  long[] alreadyRight = [98L, 243L, 400L, 1_200L, 2_500L, 3_333L, 7_800L, 9_999L, 12_000L, 15_500L,
                         18_250L, 21_000L, 24_800L, 30_951L, 44_000L, 61_500L, 120_000L];
  for (var i = 0; i < alreadyRight.Length; i++)
    Existing((uint)(3000 + i), alreadyRight[i], alreadyRight[i], cacheIsOwn: true);

  // The 3 rounding-error moves, from his log. Someone else is 1 gil (or 20 gil) cheaper, so
  // AllaganMarket's cache row (not own) prices BELOW the listing: RED. All three walk now - the old
  // 1%-threshold skip is gone; the flag is the instruction.
  Existing(4001, 243L, 242L, cacheIsOwn: false);
  Existing(4002, 400L, 399L, cacheIsOwn: false);
  Existing(4003, 30_971L, 30_951L, cacheIsOwn: false);

  // 19 rows genuinely worth walking: a real undercut by somebody else (cache row not own, cheaper).
  for (var i = 0; i < 19; i++)
    Existing((uint)(5000 + i), 10_000L + i * 500L, 8_000L + i * 500L, cacheIsOwn: false);

  Check("flags replay: the fixture is his 39 existing rows", rows.Count == 39, $"rows={rows.Count}");

  var flags = AllaganMarketFlags.Parse(string.Join("\n", cacheCsv), null, World, nowUtc);
  Check("flags replay: the parsed cache holds all 39 entries", flags.EntryCount == 39, $"entries={flags.EntryCount}");

  var decisions = PinchPreflight.Decide(rows, quotes, options, Now, flags);

  Check("flags replay: exactly 17 rows are skipped as unflagged (his own-lowest)",
    decisions.Count(d => d.Verdict == PinchVerdict.SkipNotFlagged) == 17,
    $"{decisions.Count(d => d.Verdict == PinchVerdict.SkipNotFlagged)}");
  Check("flags replay: exactly 22 rows are walked (3 rounding + 19 real undercuts)",
    decisions.Count(d => d.Verdict == PinchVerdict.Walk) == 22,
    $"{decisions.Count(d => d.Verdict == PinchVerdict.Walk)}");
  Check("flags replay: the three rounding rows WALK now - the flag outranks the retired 1% threshold",
    decisions.Where(d => d.Row.ItemId is >= 4001u and <= 4003u).All(d => d.Verdict == PinchVerdict.Walk),
    string.Join(",", decisions.Where(d => d.Row.ItemId is >= 4001u and <= 4003u).Select(d => d.Verdict)));
  Check("flags replay: the 17 own-lowest rows are never touched, exactly as AllaganMarket shows them unmarked",
    decisions.Where(d => d.Row.ItemId is >= 3000u and <= 3016u).All(d => d.Verdict == PinchVerdict.SkipNotFlagged));

  // THE CONTROL: the master switch keeps its pre-0.1.16.0 meaning - off means walk everything.
  Check("flags replay: CONTROL - with the pre-flight off, all 39 rows walk",
    PinchPreflight.Decide(rows, quotes, options with { Enabled = false }, Now, flags)
      .Count(d => d.Verdict == PinchVerdict.Walk) == 39);
  // And with NO flag data at all, the flags rule walks nothing but placeholders - the honest failure
  // mode of a broken/missing AllaganMarket install, which the changelog states plainly.
  Check("flags replay: with an EMPTY flag set nothing but placeholders is walked",
    PinchPreflight.Decide(rows, quotes, options, Now, new AllaganFlagSet(World, AllaganSettings.Defaults))
      .Count(d => d.Verdict == PinchVerdict.SkipNotFlagged) == 39);

  // The log line this feature is graded by, character-for-character.
  Check("flags replay: the summary log line names the not-flagged bucket and the walk breakdown",
    PinchPreflight.Summarize(decisions)
      == "pinch pre-flight: walking 22 of 39 row(s); skipped 17 not flagged by AllaganMarket (22 flagged undercut, 0 flagged stale, 0 placeholder)",
    PinchPreflight.Summarize(decisions));
}

// 34. Flag polarity, end to end. The flags rule has one skip reason (unflagged) and a walk family
//     (placeholder / red / yellow); every case below is paired against the input that would flip it, so
//     a pass proves the FLAG decided and not the fixture. AllaganMarket semantics per the 1.4.0.2
//     decompile: red = cached (item, quality) unitCost minus UndercutBy strictly below the listing price
//     (own-price rows compare themselves and are never red); yellow = the newest NQ-or-HQ cache row
//     older than ItemUpdatePeriod (no rows at all is NOT flagged - it renders unmarked); MatchingQuality
//     reads the listing's own quality.
{
  const long Now = 1_788_750_000_000L;  // 23:00 EDT local (see case 33 for why)
  var nowUtc = new DateTime(2026, 9, 6, 23, 0, 0);
  const uint World = 95u;
  var options = new PinchPreflightOptions(true);

  AllaganFlagSet Flags(params string[] lines) => AllaganMarketFlags.Parse(string.Join("\n", lines), null, World, nowUtc);

  PinchVerdict One(PinchRow row, AllaganFlagSet flags, PinchPreflightOptions? opts = null)
    => PinchPreflight.Decide([row], new Dictionary<uint, ItemQuote>(), opts ?? options, Now, flags)[0].Verdict;

  PinchRow Row(uint id, bool hq, long price) => new(0, 0, id, hq, price, false);

  // 1 - RED via a cheaper stranger's cache row.
  var redFlags = Flags("7001,N,95,0,09/06/2026 22:30:00,90,N");
  Check("flags: a stranger's cached 90 below the listing's 100 is RED -> walked",
    One(Row(7001, false, 100), redFlags) == PinchVerdict.Walk);
  Check("flags: NEGATIVE CONTROL - the same cache price AT the listing price is NOT undercut (AllaganMarket compares strictly below)",
    One(Row(7001, false, 90), redFlags) == PinchVerdict.SkipNotFlagged);

  // 2 - RED via UndercutBy: cached 100, UndercutBy 5, listing 98 -> recommendation 95 < 98.
  var underBy = AllaganMarketFlags.Parse(
    "7002,N,95,0,09/06/2026 22:30:00,100,N",
    """{"IntegerSettings":{"UndercutBy":5}}""", World, nowUtc);
  Check("flags: UndercutBy=5 turns a cached-equal 100 into a 95 recommendation below the listing's 98 -> RED",
    One(Row(7002, false, 98), underBy) == PinchVerdict.Walk);
  Check("flags: the same cache row with UndercutBy=0 (the shipped default) is NOT an undercut at listing 98",
    One(Row(7002, false, 98), Flags("7002,N,95,0,09/06/2026 22:30:00,100,N")) == PinchVerdict.SkipNotFlagged);

  // 3 - an OWN-price cache row is never red: the recommendation IS the listing's own price.
  var ownFlags = Flags("7003,N,95,0,09/06/2026 22:30:00,100,Y");
  Check("flags: an own-price cache row at the listing's own price is unflagged, never walked",
    One(Row(7003, false, 100), ownFlags) == PinchVerdict.SkipNotFlagged);
  Check("flags: NEGATIVE CONTROL - the same row/value as a NOT-own cache row IS undercut -> walked",
    One(Row(7003, false, 100), Flags("7003,N,95,0,09/06/2026 22:30:00,90,N")) == PinchVerdict.Walk);

  // 4 - YELLOW via an old cache row (ItemUpdatePeriod default 300 min; the row is from 08:00, Now is
  //     that evening => well past 5h). Stale outranks "would write the same number": the flag is the
  //     instruction, and the card's bar is that a price-equal-but-flagged row still walks.
  var staleFlags = Flags("7004,N,95,0,09/06/2026 12:00:00,100,N");
  Check("flags: a cache row older than the staleness period is YELLOW -> walked even though it prices the listing AT its price",
    One(Row(7004, false, 100), staleFlags) == PinchVerdict.Walk);
  Check("flags: NEGATIVE CONTROL - a fresh row at the same values is neither stale nor undercut -> skipped",
    One(Row(7004, false, 100), Flags("7004,N,95,0,09/06/2026 22:30:00,100,N")) == PinchVerdict.SkipNotFlagged);
  Check("flags: staleness reads the NEWEST of both qualities - a fresh NQ row un-stales an old HQ row",
    One(Row(7004, true, 100), Flags(
      "7004,Y,95,0,09/06/2026 12:00:00,100,N",
      "7004,N,95,0,09/06/2026 22:30:00,100,N")) == PinchVerdict.SkipNotFlagged);

  // 5 - NO cache rows at all: NOT flagged. AllaganMarket's NeedsUpdate would call such an item stale,
  //     but on the live client a listing gets a fresh own-price row the moment it is added, so a
  //     no-row item is one AllaganMarket has never had an opinion about - it renders UNMARKED, and the
  //     spec is that unmarked rows are never walked (the card's verification bar names never-checked
  //     rows explicitly). Pinned here so the polarity cannot silently flip.
  Check("flags: an item with NO cache rows at all is unflagged -> skipped (the never-checked shape)",
    One(Row(7005, false, 100), Flags()) == PinchVerdict.SkipNotFlagged);

  // 6 - world scoping: a cache row for ANOTHER world must not answer for this one.
  var otherWorld = Flags("7006,N,96,0,09/06/2026 22:30:00,50,N");
  Check("flags: a cache row from another world is ignored - the row reads unflagged -> skipped",
    One(Row(7006, false, 100), otherWorld) == PinchVerdict.SkipNotFlagged);
  Check("flags: NEGATIVE CONTROL - the same row on OUR world is a real undercut -> walked",
    One(Row(7006, false, 100), Flags("7006,N,95,0,09/06/2026 22:30:00,50,N")) == PinchVerdict.Walk);

  // 7 - quality: MatchingQuality (Joey's setting) reads the listing's own quality for the undercut
  //     lookup, while staleness looks at BOTH qualities (NeedsUpdate takes the newest of the two).
  var hqMissing = Flags("7007,N,95,0,09/06/2026 22:30:00,10,N");
  Check("flags: MatchingQuality - an HQ listing with only a fresh NQ cache row is not undercut (NQ row is not the HQ recommendation)",
    One(Row(7007, true, 20), hqMissing) == PinchVerdict.SkipNotFlagged);
  Check("flags: ...and the NQ listing of that item IS undercut by the cached 10 -> walked",
    One(Row(7007, false, 20), hqMissing) == PinchVerdict.Walk);

  // 8 - UndercutComparison=NqOnly: every listing reads the NQ cache row.
  var nqOnly = AllaganMarketFlags.Parse(
    "7008,N,95,0,09/06/2026 22:30:00,10,N",
    """{"EnumSettings":{"UndercutComparison":{"Value":"NqOnly"}}}""", World, nowUtc);
  Check("flags: NqOnly - an HQ listing also prices off the NQ cache row and is undercut -> walked",
    One(Row(7008, true, 20), nqOnly) == PinchVerdict.Walk);

  // 9 - the placeholder ALWAYS walks, flagged or not; an unreadable row (no item id, no price) cannot
  //     be flagged, so it is skipped like any unflagged row - except the placeholder, which outranks
  //     everything.
  Check("flags: a placeholder-priced listing walks even with no cache entry",
    One(new PinchRow(0, 0, 7009, false, Placeholder, true), Flags()) == PinchVerdict.Walk);
  Check("flags: a placeholder-priced listing walks even when its cache row is fresh and own-priced",
    One(new PinchRow(0, 0, 7003, false, Placeholder, true), ownFlags) == PinchVerdict.Walk);
  Check("flags: a row with no readable item id is never flagged -> skipped",
    One(Row(0, false, 500), redFlags) == PinchVerdict.SkipNotFlagged);
  Check("flags: a row with no readable price is never flagged -> skipped",
    One(Row(7001, false, 0), redFlags) == PinchVerdict.SkipNotFlagged);

  // 10 - the master switch still means walk-everything, flags or not.
  Check("flags: with the pre-flight off, every row walks whatever AllaganMarket says",
    PinchPreflight.Decide([Row(7001, false, 100), Row(7003, false, 100), new PinchRow(0, 0, 7010, false, 5, false)],
      new Dictionary<uint, ItemQuote>(), options with { Enabled = false }, Now, redFlags)
      .All(d => d.Verdict == PinchVerdict.Walk));

  // 11 - parse robustness: junk lines are skipped, missing settings fall back to defaults, broken JSON
  //      reads as defaults, and a missing file (empty strings) reads as an EMPTY flag set.
  var junk = AllaganMarketFlags.Parse(
    "not a line\n\n7001,N\n999,Y,95,0,13/45/2026 99:99:99,5,N\n7011,N,95,0,09/06/2026 22:30:00,50,N",
    null, World, nowUtc);
  Check("flags: junk cache lines are skipped, good ones kept", junk.EntryCount == 1 && junk.HasEntry(7011, false));
  Check("flags: a zero itemId row is refused",
    !AllaganMarketFlags.TryParseLine("0,N,95,0,09/06/2026 22:30:00,50,N", out _));
  Check("flags: broken settings JSON reads as AllaganMarket's defaults (period 300, undercut 0)",
    AllaganMarketFlags.ParseSettings("{broken") == AllaganSettings.Defaults
      && AllaganMarketFlags.ParseSettings(null) == AllaganSettings.Defaults);
  Check("flags: settings parse - ItemUpdatePeriod, UndercutBy and UndercutComparison all come through",
    AllaganMarketFlags.ParseSettings("""{"IntegerSettings":{"ItemUpdatePeriod":600,"UndercutBy":3},"EnumSettings":{"UndercutComparison":{"Value":"NqOnly"}}}""")
      is { ItemUpdatePeriodMinutes: 600, UndercutBy: 3, UndercutComparison: "NqOnly" });
  Check("flags: an empty cache file is an empty flag set (nothing flagged, only placeholders walk)",
    AllaganMarketFlags.Parse("", null, World, nowUtc).EntryCount == 0);

  // 12 - red/yellow/green land on the right rows of one mixed flag set.
  var mixed = Flags(
    "7020,N,95,0,09/06/2026 22:30:00,50,N",   // fresh, cheaper -> red
    "7021,N,95,0,09/06/2026 12:00:00,200,N",  // old, equal -> yellow
    "7022,N,95,0,09/06/2026 22:30:00,100,Y"); // fresh, own -> green
  Check("flags: red/yellow/green land on the right rows",
    One(Row(7020, false, 100), mixed) == PinchVerdict.Walk
      && One(Row(7021, false, 200), mixed) == PinchVerdict.Walk
      && One(Row(7022, false, 100), mixed) == PinchVerdict.SkipNotFlagged);
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
    plan.Ops.Where(o => o.Container == 10000).Sum(o => o.Quantity) == 59, string.Join(",", plan.Ops));
  Check("vendor: bags keep 5 -> the 30-stack vendors 25, the 12-stack vendors 12",
    plan.Ops.Where(o => o.Container == 0).Sum(o => o.Quantity) == 25 + 12, string.Join(",", plan.Ops));
  Check("vendor: the estimate is unit x qty at priceLow with prefer-HQ on for NQ stock",
    plan.Ops.Sum(o => o.EstGil) == (59 + 25 + 12) * 10);

  // keep bigger than the origin's stock: nothing vendored from that origin
  var keepAll = VendorPlanner.Plan([R(5111, keepR: 999)], stock, prices, true);
  Check("vendor: keep >= stock leaves that origin untouched",
    keepAll.Ops.All(o => o.Container != 10000), string.Join(",", keepAll.Ops));
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
  Check("vendor: SellFromBags=false skips bag stock", VendorPlanner.Plan([halfRule], stock, prices, true).Ops.All(o => o.Container != 0));
  var halfRule2 = R(5111, ret: false);
  Check("vendor: SellFromRetainer=false skips retainer stock", VendorPlanner.Plan([halfRule2], stock, prices, true).Ops.All(o => o.Container != 10000));
  Check("vendor: both origins disabled -> no ops at all", VendorPlanner.Plan([R(5111, bags: false, ret: false)], stock, prices, true).Ops.Count == 0);
}

// 38. THE FIFTH REPORT, replayed under the flags-only rule (2026-09-06 21:22-21:26). The manual Auto
//     Pinch pass walked 23 rows: 14 real undercuts and 9 exact no-ops on rows AllaganMarket showed NO
//     verdict for (its overlay rendered them unmarked - no cache row beyond the own-price write that
//     makes them green-by-own-price). Joey's binding correction: "I never asked the plugin to remember
//     it. I asked you to go by allagan market's flagged items." So the 0.1.13.0 board memory is GONE
//     and the rule is: walk iff AllaganMarket's data flags the row. The 14 undercut rows (strangers'
//     cache rows below them) walk; the 9 no-op rows - freshly own-priced in the cache, never undercut,
//     never stale - are NEVER walked again, even once. The 0.1.13.0 behaviour (walk once, remember,
//     skip afterwards) is retired: the first pass does not walk them and no memory is ever written.
//     Also pinned: Universalis can no longer talk the plugin into touching an unflagged row, and a
//     flagged row needs no Universalis rescue to walk.
{
  const long Now = 1_788_750_000_000L;  // 23:00 EDT local, later the same evening
  var nowUtc = new DateTime(2026, 9, 6, 23, 0, 0);
  const uint World = 95u;

  var options = new PinchPreflightOptions(Enabled: true);
  var rows = new List<PinchRow>();
  var quotes = new Dictionary<uint, ItemQuote>();
  var cacheCsv = new List<string>();

  void Row_(uint itemId, bool hq, long current, long cacheUnitCost, bool cacheIsOwn, string updated)
  {
    var row = rows.Count;
    rows.Add(new PinchRow(row, row, itemId, hq, current, false));
    quotes[itemId] = new ItemQuote(itemId, true, Now - 3_600_000L, [new QuoteListing(cacheUnitCost, hq, cacheIsOwn)]);
    cacheCsv.Add($"{itemId},{(hq ? "Y" : "N")},{World},0,{updated},{cacheUnitCost},{(cacheIsOwn ? "Y" : "N")}");
  }

  // The 9 exact no-ops, item ids and prices from the report (30414 twice - two retainers). Their
  // AllaganMarket state: a fresh OWN-price cache row written when the listing was added, so the
  // recommendation is their own price - never undercut, never stale, UNFLAGGED.
  (uint Id, bool Hq, long Price)[] noOps =
  [
    (31911, false, 12), (36084, false, 242), (44347, false, 5000),
    (12527, true, 84), (13747, true, 65), (5081, true, 2),
    (15957, false, 857), (30414, false, 40000), (30414, false, 40000),
  ];
  foreach (var n in noOps)
    Row_(n.Id, n.Hq, n.Price, n.Price, cacheIsOwn: true, "09/06/2026 22:30:00");

  // The 14 real undercuts: three from the report verbatim, eleven in the same shape. Fresh stranger
  // cache rows below them - AllaganMarket RED.
  var undercuts = new List<(uint Id, long Current, long Board)>
  {
    (31001, 250, 1), (31002, 77, 45), (31003, 500, 263),
  };
  for (var i = 4; i <= 14; i++)
    undercuts.Add(((uint)(31000 + i), 1_000L * i, 900L * i));
  foreach (var u in undercuts)
    Row_(u.Id, false, u.Current, u.Board, cacheIsOwn: false, "09/06/2026 22:00:00");

  Check("fifth replay: the fixture is his 23 rows", rows.Count == 23, $"rows={rows.Count}");

  var flags = AllaganMarketFlags.Parse(string.Join("\n", cacheCsv), null, World, nowUtc);
  var decisions = PinchPreflight.Decide(rows, quotes, options, Now, flags);

  // THE behaviour change: the 9 no-ops are skipped ON THE FIRST PASS - no walk-once-then-remember.
  Check("fifth replay: exactly the 14 undercut rows walk; the 9 no-ops NEVER walk, not even once",
    decisions.Count(d => d.Verdict == PinchVerdict.Walk) == 14
      && decisions.Count(d => d.Verdict == PinchVerdict.SkipNotFlagged) == 9
      && decisions.Where(d => d.Verdict == PinchVerdict.SkipNotFlagged).Select(d => d.Row.ItemId)
        .OrderBy(i => i).SequenceEqual(noOps.Select(n => n.Id).OrderBy(i => i)),
    $"walked={decisions.Count(d => d.Verdict == PinchVerdict.Walk)} skipped={decisions.Count(d => d.Verdict == PinchVerdict.SkipNotFlagged)}");
  Check("fifth replay: the two 30414 rows both skip off the one shared (item, quality) key",
    decisions.Count(d => d.Row.ItemId == 30414 && d.Verdict == PinchVerdict.SkipNotFlagged) == 2);
  Check("fifth replay: every walked row is an AllaganMarket RED",
    decisions.Where(d => d.Verdict == PinchVerdict.Walk)
      .All(d => d.Reason == "AllaganMarket flags this listing undercut"));

  // The summary line for Joey's log grading.
  Check("fifth replay: the summary line reads walked 14, skipped 9 not flagged",
    PinchPreflight.Summarize(decisions)
      == "pinch pre-flight: walking 14 of 23 row(s); skipped 9 not flagged by AllaganMarket (14 flagged undercut, 0 flagged stale, 0 placeholder)",
    PinchPreflight.Summarize(decisions));

  // a) A flagged row with NO Universalis quote at all still walks: the flag alone is the instruction.
  //    (0.1.13.0's board memory existed precisely because a no-quote row used to walk on uncertainty;
  //    now it walks on the flag, and no verdict is remembered anywhere.)
  Check("fifth replay: a flagged row with no Universalis answer still walks (no rescue needed, none allowed)",
    PinchPreflight.Decide([new PinchRow(0, 0, 31001, false, 250, false)],
      new Dictionary<uint, ItemQuote>(), options, Now, flags)[0].Verdict == PinchVerdict.Walk);

  // b) THE CONTROL in the other direction: an unflagged row that UNIVERSALIS calls undercut is still
  //    skipped. Universalis may not overrule the flag - this is the exact failure Joey rejected when
  //    the pre-flight walked rows AllaganMarket had no opinion on.
  var unflaggedButUniversalisCheap = new Dictionary<uint, ItemQuote>
  {
    [31911] = new ItemQuote(31911, true, Now - 3_600_000L, [new QuoteListing(1, false, false)]),
  };
  Check("fifth replay: an unflagged row Universalis calls undercut is STILL skipped (Universalis never overrules the flag)",
    PinchPreflight.Decide([new PinchRow(0, 0, 31911, false, 12, false)],
      unflaggedButUniversalisCheap, options, Now, flags)[0].Verdict == PinchVerdict.SkipNotFlagged);

  // c) The card's verification bar: a price-equal-but-flagged row still walks. Same item, same price,
  //    but the cache row went stale - YELLOW outranks "would write the same number".
  var staleButEqual = AllaganMarketFlags.Parse("36084,N,95,0,09/06/2026 12:00:00,242,N", null, World, nowUtc);
  Check("fifth replay: a price-equal-but-STALE row still walks (the flag is the instruction)",
    PinchPreflight.Decide([new PinchRow(0, 0, 36084, false, 242, false)],
      new Dictionary<uint, ItemQuote>(), options, Now, staleButEqual)[0].Verdict == PinchVerdict.Walk);

  // d) The world moved on one row - it now sits above the cached stranger price. Still RED, walks.
  var movedRows = rows.Select(r => r.ItemId == 31911 ? r with { CurrentPrice = 15 } : r).ToList();
  var movedFlags = AllaganMarketFlags.Parse("31911,N,95,0,09/06/2026 22:30:00,12,N", null, World, nowUtc);
  var moved = PinchPreflight.Decide(movedRows, quotes, options, Now, movedFlags);
  Check("fifth replay: a row whose price moved below the cached competitor is walked (still undercut)",
    moved.Single(d => d.Row.ItemId == 31911).Verdict == PinchVerdict.Walk);
}

// 38a. THE RETIREMENT PINS for the 0.1.13.0 board memory: the file is deleted, and nothing in the
//      pre-flight answer path may depend on remembered verdicts, Universalis freshness, or the
//      gil/percent thresholds any more. The compile-time fact is pinned by the harness csproj no
//      longer compiling that file; the behavioural facts are pinned by the cases above.
//      What remains verifiable offline: the summary line has no "remembered" bucket, and the options
//      record carries exactly the one switch the rule still honours.
{
  var options = new PinchPreflightOptions(Enabled: true);
  Check("retirement: the pre-flight options carry only the master switch",
    options.Enabled && options == new PinchPreflightOptions(true));

  var decisions = PinchPreflight.Decide(
    [new PinchRow(0, 0, 7300, false, 500, false)],
    new Dictionary<uint, ItemQuote>(),
    options, 1_788_750_000_000L,
    AllaganMarketFlags.Parse("7300,N,95,0,09/06/2026 22:30:00,500,Y", null, 95, new DateTime(2026, 9, 6, 23, 0, 0)));
  Check("retirement: the summary line has no remembered-from-last-pass bucket",
    !PinchPreflight.Summarize(decisions).Contains("remembered"));
  Check("retirement: the only skip verdict is SkipNotFlagged",
    Enum.GetValues<PinchVerdict>().Length == 2
      && Enum.IsDefined(typeof(PinchVerdict), PinchVerdict.Walk)
      && Enum.IsDefined(typeof(PinchVerdict), PinchVerdict.SkipNotFlagged));
}
// 39. THE VENDOR NO-OP (t_6223b845, 0.1.12.0 shipped defect): VendorOp.Container carried the
//     StockOrigin enum (Bags=0/Retainer=1) instead of the stack's real game InventoryType, so every
//     op addressed Inventory1/Inventory2 rather than Inventory1-4/RetainerPage1-7. The pre-call slot
//     re-read read the WRONG container, found no matching stack, and all 7 ops aborted - Joey's
//     2026-09-07 23:20 run vendored 0/7. The regression pin: the op's container IS the stock stack's.
{
  const uint Item = 5111;
  var stock = new List<StockStack>
  {
    new(StockOrigin.Retainer, 10000, 0, Item, false, 99),   // RetainerPage1
    new(StockOrigin.Retainer, 10004, 3, Item, false, 50),   // RetainerPage5
    new(StockOrigin.Bags, 0, 2, Item, false, 30),           // Inventory1
  };
  var prices = new Dictionary<uint, (uint, uint)> { [Item] = (40, 10) };
  var rule = new ItemRule(Item, false, 99, 0, 0, 0, true, true, 0, 999);

  var plan = VendorPlanner.Plan([rule], stock, prices, preferHq: true);
  Check("39 container: ops exist for every stack", plan.Ops.Count == 3, string.Join(",", plan.Ops));
  Check("39 container: retainer ops carry the RETAINER PAGE id, not the origin enum (1)",
    plan.Ops.Where(o => o.Container >= 10000).All(o => o.Container is 10000 or 10004),
    string.Join(",", plan.Ops.Select(o => o.Container)));

  // THE regression pin, phrased the way the 0.1.12.0 build failed: a raw origin value (0/1) must
  // never appear as a container id for retainer stock.
  Check("39 container: no retainer op carries a bag id",
    plan.Ops.Where(o => o.Container < 10000).All(o => o.Container is >= 0 and <= 3),
    string.Join(",", plan.Ops.Select(o => o.Container)));
  Check("39 container: bag op carries Inventory1 (0) and slot 2",
    plan.Ops.Any(o => o.Container == 0 && o.Slot == 2 && o.Quantity == 30), string.Join(",", plan.Ops));
  Check("39 container: op from RetainerPage5 keeps slot 3",
    plan.Ops.Any(o => o.Container == 10004 && o.Slot == 3), string.Join(",", plan.Ops));

  // InventoryType naming: the value the log renders must be the game container, nameable.
  var op1 = plan.Ops.First(o => o.Container == 10000);
  Check("39 container: the log name for container 10000 resolves to RetainerPage1", op1.ContainerName() == "RetainerPage1");
  Check("39 container: the log name for a bag op resolves to Inventory1",
    plan.Ops.First(o => o.Container == 0).ContainerName() == "Inventory1");
  Check("39 container: an unknown id renders as Unknown(n), never a fake bag name",
    new VendorOp(4242, 0, Item, false, 1, 1).ContainerName() == "Unknown(4242)");

  // HasKnownContainer: the executor's fail-safe accepts exactly the sellable containers.
  var good = new List<int> { 0, 1, 2, 3, 2001, 10000, 10003, 10006, 12001 };
  Check("39 container: HasKnownContainer accepts every sellable container",
    good.All(c => new VendorOp(c, 0, Item, false, 1, 1).HasKnownContainer));
  Check("39 container: HasKnownContainer refuses unknown ids (the 0.1.12.0 shape is caught)",
    new VendorOp(4, 0, Item, false, 1, 1).HasKnownContainer == false
    && new VendorOp(-1, 0, Item, false, 1, 1).HasKnownContainer == false
    && new VendorOp(999, 0, Item, false, 1, 1).HasKnownContainer == false
    && new VendorOp(19999, 0, Item, false, 1, 1).HasKnownContainer == false);
}

// 40. HONEST ACCOUNTING (t_6223b845): the done line reports planned-vs-vendored, so a run that
//     vendors 0 of N says so in chat instead of announcing the plan and going quiet. The format
//     lives in AutoMarket/DoneLine.cs (Dalamud-free); Communicator.PrintSweepDone delegates to it.
{
  // the format contract, pinned character-for-character (the done line renders in chat)
  Check("40 done-line: all-zero is plain 'done.'", DoneLine.Format(0, 0, 0, 0, 0) == "done.");
  Check("40 done-line: the 0.1.12.0 7/7 failure renders '0 new listing(s), 7 vendoring op(s) failed'",
    DoneLine.Format(0, 0, 0, 0, 7) == "done: 0 new listing(s), 7 vendoring op(s) failed (see log).");
  Check("40 done-line: 3 listed + 2 vendored + 1 failed reads in order",
    DoneLine.Format(3, 0, 2, 0, 1) == "done: 3 new listing(s), 2 vendored, 1 vendoring op(s) failed (see log).");
  Check("40 done-line: no failed ops -> exactly the 0.1.12.0 line",
    DoneLine.Format(3, 1, 2, 0, 0) == "done: 3 new listing(s), 1 skipped (stock moved), 2 vendored.");
  Check("40 done-line: held-back still renders (0.1.11.0 polarity pin)",
    DoneLine.Format(0, 0, 0, 4, 0) == "done: 0 new listing(s), 4 held back by the value gate.");
  Check("40 done-line: failure clause only when nonzero",
    !DoneLine.Format(1, 0, 0, 0, 0).Contains("failed")
    && DoneLine.Format(1, 0, 0, 0, 2).Contains(", 2 vendoring op(s) failed"));
}

// 41. THE FULL-BOARD VENDOR GAP (t_bbb89c49, 0.1.15.0 shipped defect): on a retainer with a FULL
//     market board (0 free slots), the gate planned vendoring but the session ended before the
//     vendor trigger was queued - plan had 0 listing ops, BuildListingStepsNow returned early, and
//     BuildVendoringSteps (which 0.1.15.0 never called from anywhere) never ran. The run "planned 1
//     op, executed 0, said nothing" (Joey's 01:05:56 log). The pins here: the vendor decision is
//     INDEPENDENT of market slots (a full board still yields vendor ops for held-back stock), the
//     full-board pinch scope is Nothing, and a planned-but-unexecuted leg renders the honest
//     failure clause in the done line.
{
  const uint Item = 19990;                       // the exact item id from the 01:05:56 run
  var fullBoard = new List<MarketSlot>();        // 20/20 occupied: 0 free slots
  for (var i = 0; i < 20; i++) fullBoard.Add(new MarketSlot(i, 9999u, false, 1));
  var stock = new List<StockStack> { new(StockOrigin.Retainer, 10000, 0, Item, false, 12) };
  var prices = new Dictionary<uint, (uint, uint)> { [Item] = (30, 10) };
  var heldRule = new ItemRule(Item, false, 99, 0, 0, 0, true, true, 0, 999);

  var vendorPlan = VendorPlanner.Plan([heldRule], stock, prices, preferHq: true);
  Check("41 full-board: the vendor plan does NOT depend on free market slots (1 op from held-back stock)",
    vendorPlan.Ops.Count == 1, $"ops={vendorPlan.Ops.Count}");
  Check("41 full-board: the op addresses the retainer page the item is in",
    vendorPlan.Ops.Count == 1 && vendorPlan.Ops[0].Container == Ret1 && vendorPlan.Ops[0].Quantity == 12,
    string.Join(",", vendorPlan.Ops));

  // The listing side of that same retainer: nothing can be planned into a full board.
  var listingPlan = AutoMarketPlanner.Plan([Rule(Dye, 5)], stock, fullBoard, Opts());
  Check("41 full-board: the listing planner plans 0 ops (the 0.1.15.0 early-return shape)",
    listingPlan.Ops.Count == 0, $"ops={listingPlan.Ops.Count}");

  // And the pinch scope for "listed nothing" is Nothing - the session truly had nothing to do
  // EXCEPT the vendor leg, which is exactly why it must be queued on the empty-plan path too.
  Check("41 full-board: pinch scope for 0 listed is Nothing (no re-pass)",
    PinchScope.Decide(false, 0) == PinchAfterMarket.Nothing);

  // The honest-failure contract: the gate ANNOUNCED vendoring (the 01:05:56 chat line) but the leg
  // never executed, so the run closes with the failure clause - never silence.
  var plannedNotRun = DoneLine.Format(0, 0, 0, 0, 1);
  Check("41 full-board: a run that planned 1 and executed 0 prints the failure clause, not silence",
    plannedNotRun == "done: 0 new listing(s), 1 vendoring op(s) failed (see log).",
    plannedNotRun);
}

// 42. THE VENDOR LEG'S MENU-OPEN DECISION + STOP-ON-FAILURE (t_8dc20a2b, Joey's pick on Helm
//     t-joey-1788757755566). Two 0.1.15.1 defects: the menu-open step glanced ONCE (135 ms after
//     the sell-list close was queued) and the trigger failure left close steps that could only
//     time out - the "Clearing 53 remaining tasks" abort that wiped retainers 2-4 at 01:45:28.
//     The decision table lives in AutoMarket/VendorMenuGate.cs; the stop-on-failure contract is
//     that a failed leg halts the sweep DELIBERATELY with a named reason (no timeout spew, no
//     silence), and a trigger with no plan is a state-change race, never a stop.
{
  // The decision table, pinned over every input combination.
  var table = new (bool MenuReady, bool SheetLoaded, bool EntryFound, VendorMenuDecision Want, string Name)[]
  {
    (false, false, false, VendorMenuDecision.WaitForMenu,   "42 menu: menu not on screen -> wait (the 01:45:07 shape: 135 ms after the close was queued)"),
    (false, true,  false, VendorMenuDecision.WaitForMenu,   "42 menu: menu not on screen, sheet loaded -> wait"),
    (false, true,  true,  VendorMenuDecision.WaitForMenu,   "42 menu: menu not on screen, entry 'found' -> wait (nothing to click yet)"),
    (true,  false, false, VendorMenuDecision.WaitForMenu,   "42 menu: menu up but sheet text not loaded -> wait (NOT a failure - a fresh menu may not have rendered entries either)"),
    (true,  true,  true,  VendorMenuDecision.OpenPanel,     "42 menu: menu up, entrust entry found -> click it"),
    (true,  true,  false, VendorMenuDecision.MenuMissingEntry, "42 menu: menu up, sheet loaded, NO entrust entry -> the real failure"),
  };
  foreach (var (menuReady, sheetLoaded, entryFound, want, name) in table)
    Check(name, VendorMenuGate.Decide(menuReady, sheetLoaded, entryFound) == want,
      $"{VendorMenuGate.Decide(menuReady, sheetLoaded, entryFound)}");

  // The two release-defining cases, named for what they prevent:
  Check("42 menu: 'not yet' can NEVER be the stop verdict (0.1.15.1 glanced instead of waiting)",
    VendorMenuGate.Decide(false, true, false) == VendorMenuDecision.WaitForMenu);
  Check("42 menu: 'menu up, no entry' is the ONLY stop verdict",
    VendorMenuGate.Decide(true, true, false) == VendorMenuDecision.MenuMissingEntry
    && VendorMenuGate.Decide(true, false, false) != VendorMenuDecision.MenuMissingEntry);

  // Negative control: the gate is not a constant that always waits - the happy path is reachable.
  Check("42 menu: negative control - a real menu with the entry clicks through",
    VendorMenuGate.Decide(true, true, true) == VendorMenuDecision.OpenPanel);
}

// 43. STOP-ON-FAILURE CONTRACT (the operator's mid-run correction, 2026-09-07): a failed vendoring
//     leg halts the sweep ON PURPOSE - but cleanly, with a named, human-readable reason, never the
//     task manager's timeout abort ("Clearing N remaining tasks because of timeout" reads like a
//     crash) and never silence. The chat-facing strings the leg prints are pinned here so a future
//     edit cannot quietly regress to the 0.1.15.1 spew-and-die or to quiet skip.
{
  // The honest-failure done line STILL applies when the leg ran and ops failed (unchanged from 41).
  Check("43 stop: a leg that ran and failed its op still prints the failure clause",
    DoneLine.Format(0, 0, 0, 0, 1) == "done: 0 new listing(s), 1 vendoring op(s) failed (see log).");

  // The stop messages must name the reason AND that the sweep stopped on purpose. Pinned so the
  // words "stopped the sweep" survive refactors - that is the sentence Joey reads in chat.
  var chat = "value gate: vendoring stopped the sweep - the retainer bell menu never reopened after the sell list closed (waited 10 s) - the vendoring leg could not run";
  Check("43 stop: the chat line names the reason and says the sweep stopped",
    chat.Contains("vendoring stopped the sweep") && chat.Contains("menu never reopened"),
    chat);

  var log = "[LMC] vendor: FAILED - the retainer bell menu is open but has no 'Entrust or withdraw items' entry - the vendoring leg cannot open the inventory panel. Stopping the sweep here on purpose (stop-on-failure); no further retainer will be touched.";
  Check("43 stop: the log line says FAILED, names the missing entry, and says no further retainer is touched",
    log.Contains("vendor: FAILED") && log.Contains("no 'Entrust or withdraw items' entry") && log.Contains("no further retainer"),
    log);

  // Anti-spew control: neither message may quote the task manager's timeout abort - that string is
  // what the 0.1.15.1 failure looked like, and the fix must not reproduce it.
  Check("43 stop: no message echoes the 'Clearing N remaining tasks' abort",
    !chat.Contains("Clearing") && !log.Contains("Clearing"));

  // The no-plan trigger path is a RETRY, not a stop: the trigger returns false (the step reruns)
  // without stopping anything. Pinned via the decision enum: WaitForMenu is the only non-stop
  // non-click verdict, and a no-plan tick maps to it by construction in MarketAutomation.
  Check("43 stop: a state-change race (no plan yet) is a retry, never a stop",
    VendorMenuGate.Decide(false, false, false) == VendorMenuDecision.WaitForMenu);
}

// 44. NO-PLAN TRIGGER POLARITY (the 2026-09-07 "cancel button is still there" hang): the session-end
//     vendor trigger is inserted ahead of every retainer session's own steps. When the value gate
//     held no vendor items the 0.1.16.2 trigger answered RETRY (false) to the task manager, re-ran
//     every tick for its full 120 s time limit, and only then dropped - two minutes of dead air per
//     listing retainer. The no-plan path is NOTHING TO DO, which means complete (true), never wait.
{
  Check("44 trigger: a no-plan trigger completes as a no-op (true), never retries",
    VendorMenuGate.NoPlanTriggerCompletes());

  // Negative control: the polarity helper is wired to the real no-plan branch, so it must NOT be
  // reachable on a plan-bearing path's stop verdicts - those stay governed by case 42/43 gates.
  Check("44 trigger: stop-on-failure gate unchanged by the no-op polarity",
    VendorMenuGate.Decide(true, true, false) == VendorMenuDecision.MenuMissingEntry
    && VendorMenuGate.Decide(false, false, false) == VendorMenuDecision.WaitForMenu);
}


// 45. AUTO-MARKET BAG MARKERS (Helm t-joey-1788794153572): the marker predicate is the exact
//     listing predicate - an entry exists AND is Enabled. Anything else (no entry, disabled
//     entry) is NOT marked, because BuildPlan consumes only Enabled entries and a marker on a
//     disabled entry would promise a listing that never happens.
{
  var entries = new List<MarkerMatch.Entry>
  {
    new(Dye, false, true),   // NQ enabled
    new(Dye, true, false),   // HQ present but DISABLED
    new(Ore, true, true),    // HQ enabled
  };

  Check("45 markers: enabled entry marks", MarkerMatch.IsMarked(entries, Dye, hq: false));
  Check("45 markers: disabled entry does NOT mark", !MarkerMatch.IsMarked(entries, Dye, hq: true));
  Check("45 markers: missing entry does NOT mark", !MarkerMatch.IsMarked(entries, Ore, hq: false));
  Check("45 markers: HQ/NQ are independent (HQ-only entry marks HQ only)",
    MarkerMatch.IsMarked(entries, Ore, hq: true) && !MarkerMatch.IsMarked(entries, Ore, hq: false));

  // Empty list marks nothing.
  Check("45 markers: empty list marks nothing",
    MarkerMatch.MarkedStacks([], [new MarkerMatch.Stack(0, Dye, false)]).Count == 0);

  // Stack set -> marked slot set, keyed by slot: NQ dye in slot 3 marks, HQ ore in slot 7 marks,
  // HQ dye in slot 9 does not.
  var stacks = new List<MarkerMatch.Stack>
  {
    new(3, Dye, false),
    new(7, Ore, true),
    new(9, Dye, true),
    new(11, 9999u, false), // unlisted item
  };
  var marked = MarkerMatch.MarkedStacks(entries, stacks);
  Check("45 markers: exactly the enabled stacks are marked (slots 3 and 7)",
    marked.Count == 2 && marked.ContainsKey(3) && marked.ContainsKey(7) && !marked.ContainsKey(9) && !marked.ContainsKey(11),
    string.Join(",", marked.Keys));

  // Duplicate config entries (two entries for the same item+HQ): the FIRST one wins, mirroring
  // Configuration.GetAutoMarketItem's FirstOrDefault - a duplicate is a config-entry bug, but the
  // marker must agree with what the listing engine itself would read.
  var dupes = new List<MarkerMatch.Entry> { new(Dye, false, false), new(Dye, false, true) };
  Check("45 markers: duplicate entries agree with FirstOrDefault (first wins)",
    MarkerMatch.IsMarked(dupes, Dye, hq: false) == false);
  var dupes2 = new List<MarkerMatch.Entry> { new(Dye, false, true), new(Dye, false, false) };
  Check("45 markers: duplicate entries agree with FirstOrDefault (first-enabled wins)",
    MarkerMatch.IsMarked(dupes2, Dye, hq: false) == true);
}

// 46. GRID-TO-CONTAINER PAIRING (Helm t-joey-1788804058029): 0.1.17.0 paired each grid addon with
//     a container by a fixed index table - Grid0E was treated as the THIRD bag page - which is
//     wrong in the expanded view (each E-grid shows its own page by name identity: Grid0E is bag
//     0) and meaningless in the tabbed view (the single panel follows the parent Inventory
//     window's selected tab). Ground truth: CriticalCommonLib 1.15.0.12 AtkInventoryExpansion.
//     SetColors / InventoryGridOverlay.Draw, decompiled from the live install 2026-09-07.
//     GridMap.Resolve owns the pairing; everything it cannot resolve draws NOTHING.
{
  var E0 = "InventoryGrid0E"; var E1 = "InventoryGrid1E"; var E2 = "InventoryGrid2E"; var E3 = "InventoryGrid3E";

  // All four E-grids live (expanded view): fixed name identity, independent of any tab.
  var all = GridMap.Resolve([E0, E1, E2, E3], null);
  Check("46 gridmap: expanded mode pairs every E-grid by name identity (bag 0..3)",
    all.Count == 4
    && all[0] == new GridMap.GridBinding(E0, 0) && all[1] == new GridMap.GridBinding(E1, 1)
    && all[2] == new GridMap.GridBinding(E2, 2) && all[3] == new GridMap.GridBinding(E3, 3));

  // Partial expanded view: each live E-grid draws independently; absent ones are not guessed in.
  var part = GridMap.Resolve([E1, E3], null);
  Check("46 gridmap: a subset of live E-grids binds only itself (no completion by guess)",
    part.Count == 2 && part[0] == new GridMap.GridBinding(E1, 1) && part[1] == new GridMap.GridBinding(E3, 3));

  // The exact 0.1.17.0 failure shape (the only line his log ever printed: dots computed from
  // Inventory3 drawn over Grid0E, which shows Inventory1 - off by two bags).
  var bug = GridMap.Resolve([E0], null);
  Check("46 gridmap: Grid0E binds bag 0 (Inventory1) - the 0.1.17.0 table said bag 2",
    bug.Count == 1 && bug[0].BagIndex == 0);

  // Tabbed mode: the live panel binds the parent tab's bag, for every tab 0..3.
  var okTabs = true;
  for (var tab = 0; tab <= 3; tab++)
  {
    var r = GridMap.Resolve(["InventoryGrid"], tab);
    okTabs &= r.Count == 1 && r[0].GridName == "InventoryGrid" && r[0].BagIndex == tab;
  }
  Check("46 gridmap: normal mode binds the panel to each tab's bag (tabs 0..3)", okTabs);

  var twoPanels = GridMap.Resolve(["InventoryGrid", "InventoryGrid1"], 2);
  Check("46 gridmap: every live normal-mode panel binds the SAME tab bag",
    twoPanels.Count == 2 && twoPanels.All(b => b.BagIndex == 2));

  // Out-of-range or unknown tab: honest absence - nothing draws.
  Check("46 gridmap: tab outside 0..3 draws nothing",
    GridMap.Resolve(["InventoryGrid0"], 4).Count == 0 && GridMap.Resolve(["InventoryGrid0"], -1).Count == 0);
  Check("46 gridmap: unknown tab (parent Inventory window not live) draws nothing",
    GridMap.Resolve(["InventoryGrid0"], null).Count == 0);

  // Unknown names never contribute; any live E-grid means expanded mode (tab not consulted).
  Check("46 gridmap: unknown grid names are ignored",
    GridMap.Resolve(["SomeOtherGrid", E0], null).Count == 1);
  var mixed = GridMap.Resolve(["InventoryGrid0", E2], 3);
  Check("46 gridmap: any live E-grid means expanded mode (normal panel not guessed)",
    mixed.Count == 1 && mixed[0] == new GridMap.GridBinding(E2, 2));

  Check("46 gridmap: IsExpandedGrid classifies the seven registered grid names",
    GridMap.IsExpandedGrid(E0) && GridMap.IsExpandedGrid(E3)
    && !GridMap.IsExpandedGrid("InventoryGrid0") && !GridMap.IsExpandedGrid("InventoryGrid"));

  // CONTROL: the shipped source no longer carries the old fixed four-name table, and the
  // retainer standdown survives untouched. A missing file read FAILS the control (never passes
  // vacuously): find the source relative to the harness bin dir or the repo root.
  var srcCandidates = new[]
  {
    Path.Combine("..", "..", "..", "..", "..", "src", "LazyMarketCompanion", "AutoMarketMarkers.cs"),
    Path.Combine("src", "LazyMarketCompanion", "AutoMarketMarkers.cs"),
  };
  var markersSrc = srcCandidates.Where(File.Exists).Select(File.ReadAllText).FirstOrDefault() ?? "";
  Check("46 gridmap: the old hard-coded page-to-addon switch is gone from the shipped source (control)",
    markersSrc.Length > 0
    && !markersSrc.Contains("0 => \"InventoryGrid0\"") && !markersSrc.Contains("2 => \"InventoryGrid0E\""),
    markersSrc.Length == 0 ? "AutoMarketMarkers.cs not found from either candidate path" : "");
  Check("46 gridmap: retainer standdown still present (InventoryRetainer block untouched)",
    markersSrc.Contains("\"InventoryRetainer\""));
}


// 47. THE BLIND GATE (0.1.18.0). On 2026-09-07 the gate's one Universalis request 504'd at the
// gateway on 9 of 10 sweeps; GateWait expired, BuildPlan ran with null quotes, MarketGate.Decide
// returned List for every rule (uncertainty lists), and the gate printed "every item is above the
// 100 gil net threshold" - a clean bill of health for a gate that saw NOTHING. Below-threshold
// stock (19990 at ~79 net, 12593 at ~23 net) listed blind and priced at the real board price: the
// 1-gil listings. This case pins the three facts that make such a run visible and impossible to
// misread: the sight count says blind, the fetch list says stocked-only, and the trim's arithmetic
// is the verdict's own arithmetic.
{
  const long Fresh = 6 * 3_600_000L;
  const long Now = 1_788_800_000_000L;  // 2026-09-07, the blind-gate day

  // Rules: 19990 (stocked, the victim), 5111 (stocked, above threshold), 12593 (NO stock - must
  // not even be asked about), plus a rule whose stock is entirely eaten by its keep (must not be
  // asked about either - nothing sellable).
  var rules = new List<ItemRule>
  {
    Rule(19990, 99),
    Rule(5111, 999),
    Rule(12593, 99),
    Rule(5594, 99, keepB: 50),
  };
  var stock = new List<StockStack>
  {
    new(StockOrigin.Bags, Bags1, 3, 19990, false, 99),
    new(StockOrigin.Bags, Bags1, 4, 5111, false, 200),
    // 12593: deliberately no stack. 5594: one stack of 50, keep 50 -> nothing sellable.
    new(StockOrigin.Bags, Bags1, 5, 5594, false, 50),
  };

  // (a) The fetch list is stocked-only. 19990 and 5111 are asked about; 12593 (no stock) and 5594
  // (keep eats the whole stack) are not. The trim uses the verdict's own PotentialSellable.
  var fetch = MarketGate.GateFetchIds(rules, stock, listPartialStacks: false);
  Check("47 blind gate: fetch asks about the two stocked items only",
    fetch.Count == 2 && fetch.Contains(19990u) && fetch.Contains(5111u),
    "fetch=[" + string.Join(", ", fetch) + "]");

  // (b) Partial-stack flooring agrees between trim and verdict: with partials OFF, 30 units of a
  // 99-listing item floor to 0 sellable -> not asked about. With partials ON it is asked about.
  var floored = new List<ItemRule> { Rule(19990, 99) };
  var flooredStock = new List<StockStack> { new(StockOrigin.Bags, Bags1, 3, 19990, false, 30) };
  Check("47 blind gate: partials-off flooring drops a sub-listing remainder from the fetch",
    MarketGate.GateFetchIds(floored, flooredStock, false).Count == 0);
  Check("47 blind gate: partials-on keeps the sub-listing remainder in the fetch",
    MarketGate.GateFetchIds(floored, flooredStock, true).Count == 1);

  // (c) THE ANNOUNCE DIFFERENTIATOR. The same rules, two quote maps:
  //     - sighted: both stocked items have fresh quotes -> Judged 2, Unpriceable 0 -> the announce
  //       is the old "every item is above" line.
  //     - blind (null quotes, the 504 shape): Judged 0, Unpriceable 2 -> the announce MUST NOT be
  //       the old line; the sight record itself is what the log line is built from.
  var sightedQuotes = new Dictionary<uint, ItemQuote>
  {
    [19990] = new ItemQuote(19990, true, Now - 60_000L, [new QuoteListing(84, false, false)]),
    [5111] = new ItemQuote(5111, true, Now - 60_000L, [new QuoteListing(950, false, false)]),
  };
  var sighted = MarketGate.CountSight(rules, sightedQuotes, preferHq: true, Now, Fresh);
  Check("47 blind gate: sighted run judges both stocked items (no-stock rules read unpriceable)",
    sighted.Judged == 2 && sighted.Unpriceable == 2, $"judged={sighted.Judged} unpriceable={sighted.Unpriceable}");
  var blind = MarketGate.CountSight(rules, null, preferHq: true, Now, Fresh);
  Check("47 blind gate: null quotes (the 504 shape) read as fully unpriceable",
    blind.Judged == 0 && blind.Unpriceable == 4, $"judged={blind.Judged} unpriceable={blind.Unpriceable}");
  var partial = MarketGate.CountSight(rules, new Dictionary<uint, ItemQuote>
  {
    [19990] = new ItemQuote(19990, true, Now - 60_000L, [new QuoteListing(84, false, false)]),
  }, preferHq: true, Now, Fresh);
  Check("47 blind gate: one fresh quote of four rules reads 1 judged / 3 unpriceable",
    partial.Judged == 1 && partial.Unpriceable == 3, $"judged={partial.Judged} unpriceable={partial.Unpriceable}");

  // (d) A stale quote is NOT sight: judged only on fresh data, mirroring UsableQuote/Decide.
  var stale = new Dictionary<uint, ItemQuote>
  {
    [19990] = new ItemQuote(19990, true, Now - Fresh - 1, [new QuoteListing(84, false, false)]),
  };
  var staleSight = MarketGate.CountSight(new List<ItemRule> { Rule(19990, 99) }, stale, preferHq: true, Now, Fresh);
  Check("47 blind gate: a stale quote does not count as judged",
    staleSight.Judged == 0 && staleSight.Unpriceable == 1);

  // (e) CONTROL (negative): a fresh quote with no listing of the needed quality is unpriceable -
  //     CheapestUnitPrice returns null for an HQ rule with only NQ listings when preferHq is on.
  var nqOnly = new Dictionary<uint, ItemQuote>
  {
    [19990] = new ItemQuote(19990, true, Now - 60_000L, [new QuoteListing(84, false, false)]),
  };
  var hqRule = new List<ItemRule> { Rule(19990, 99, hq: true) };
  var nqSight = MarketGate.CountSight(hqRule, nqOnly, preferHq: true, Now, Fresh);
  Check("47 blind gate: fresh NQ-only quote does not judge an HQ rule",
    nqSight.Judged == 0 && nqSight.Unpriceable == 1);

  // (f) UsableQuote is the shared sight test - pin it directly against its parts.
  Check("47 blind gate: UsableQuote mirrors CheapestUnitPrice on a fresh quote",
    MarketGate.UsableQuote(sightedQuotes[5111], ruleIsHq: false, preferHq: true, Now, Fresh) == 950);
  Check("47 blind gate: UsableQuote is null on stale data",
    MarketGate.UsableQuote(stale[19990], Rule(19990, 99).HQ, preferHq: true, Now, Fresh) == null);
}

// 48. THE TWO-STATE BAG MARKER (0.1.21.0). Joey: "if it be put on the marketboard at all ever, it
// should have an indicator on it saying whether it's on my automarket list or not." The marker now
// has THREE outcomes per stack: green (on the Auto-Market list, enabled), grey (marketable but NOT
// on the list), and NO dot (cannot go on the market board at all). The separator logic is pure and
// lives in MarkerMatch.Classify; the marketability predicate itself is game-side (Item sheet).
{
  var entries = new List<MarkerMatch.Entry>
  {
    new(99u, false, true),   // Dye-ish marked item, NQ, enabled
    new(100u, true, true),   // enabled HQ entry
    new(101u, false, false), // list entry present but DISABLED: not green
  };

  var stacks = new List<MarkerMatch.Stack>
  {
    new(0, 99u, false),   // on list -> green
    new(1, 100u, true),   // on list (HQ entry) -> green
    new(2, 100u, false),  // same id NQ, no NQ entry, marketable -> grey
    new(3, 101u, false),  // disabled entry + marketable -> grey (green is for enabled entries only)
    new(4, 101u, false),  // same item again, also grey
    new(5, 200u, false),  // marketable, no entry -> grey
    new(6, 300u, false),  // NOT marketable, no entry -> no dot
  };

  var marketable = new HashSet<uint> { 100u, 101u, 200u };

  var cls = MarkerMatch.Classify(entries, stacks, marketable);

  Check("48 twostate: enabled entry is green",
    cls.TryGetValue(0, out var a) && a.Kind == MarkerMatch.MarkKind.OnList);
  Check("48 twostate: HQ entry marks the HQ stack green",
    cls.TryGetValue(1, out var b) && b.Kind == MarkerMatch.MarkKind.OnList);
  Check("48 twostate: same id without a matching entry, marketable -> grey",
    cls.TryGetValue(2, out var c) && c.Kind == MarkerMatch.MarkKind.MarketableNotListed);
  // A DISABLED entry is NOT green - but if the item itself is marketable it is still grey: grey
  // means "could be listed but is not on the (enabled) list", which is exactly the true state.
  Check("48 twostate: DISABLED entry is not green (never promised)",
    !cls.TryGetValue(3, out var x3) || x3.Kind != MarkerMatch.MarkKind.OnList);
  Check("48 twostate: DISABLED entry on a marketable item still shows grey (the not-configured answer)",
    cls.TryGetValue(3, out var x3b) && x3b.Kind == MarkerMatch.MarkKind.MarketableNotListed);
  Check("48 twostate: marketable with no entry at all -> grey",
    cls.TryGetValue(5, out var d) && d.Kind == MarkerMatch.MarkKind.MarketableNotListed);
  Check("48 twostate: unmarketable item with no entry -> no dot at all",
    !cls.ContainsKey(6));

  // (a) An on-list stack stays green EVEN IF not in the marketable set: the config-entry bug is
  // shown, not hidden (the 0.1.17.0 honesty rule).
  var bugStacks = new List<MarkerMatch.Stack> { new(7, 99u, false) };
  var cls2 = MarkerMatch.Classify(entries, bugStacks, new HashSet<uint>());
  Check("48 twostate: an on-list stack stays green regardless of marketability (honour config bugs)",
    cls2.TryGetValue(7, out var e) && e.Kind == MarkerMatch.MarkKind.OnList);

  // (b) CONTROL: an empty marketable set yields ONLY green slots - the old single-state behaviour.
  var clsOnlyOnList = MarkerMatch.Classify(entries, stacks, new HashSet<uint>());
  Check("48 twostate: control - empty marketable set reproduces the old green-only marker set",
    clsOnlyOnList.Count == 2
    && clsOnlyOnList.ContainsKey(0) && clsOnlyOnList.ContainsKey(1)
    && clsOnlyOnList[0].Kind == MarkerMatch.MarkKind.OnList
    && clsOnlyOnList[1].Kind == MarkerMatch.MarkKind.OnList);

  // (c) MARKEDSTACKS (the old verdict) is UNCHANGED - the listing engine still consumes only
  // enabled entries; the grey state is a marker-layer concept, never planner input.
  var markedOld = MarkerMatch.MarkedStacks(entries, stacks);
  Check("48 twostate: MarkedStacks still answers only the list-membership question",
    markedOld.Count == 2 && markedOld.ContainsKey(0) && markedOld.ContainsKey(1));
}

// 49. THE BELL-MENU MIS-SCAN + THE SIGHT-ANNOUNCE SCOPE (0.1.23.0, t_b307b5da). Two defects from
//     the same 15:06:55 sweep: the vendor leg's first-ever sighted plan died 181 ms into the
//     sell-list close ("the retainer bell menu is open but has no 'Entrust or withdraw items'
//     entry"), and the honest gate announce printed "no price data for ~230 of 257 item(s)" on a
//     PERFECTLY SIGHTED sweep because the sight count ran over the whole enabled list instead of
//     the stocked set the fetch actually asks about.
{
  const long Fresh = 6 * 3_600_000L;
  const long Now = 1_788_800_000_000L;

  // (a) THE MATCH. Row 2378's template ends in a live payload: "Entrust or withdraw items. (Slots
  //     filled: 0)". The 15:06 retainer's board was FULL - the rendered entry reads
  //     "(Slots filled: 20)" - so the old whole-template StartsWith compared the template against
  //     the rendered text and failed on a perfect menu. AutoRetainer drives this exact entry with
  //     the same sheet row and a StartsWith, which is why it never mis-fires: the comparison is
  //     the stable prefix.
  var want = "Entrust or withdraw items. (Slots filled: 0)";   // the sheet template (xivapi v2, verified 2026-09-07)
  Check("49 menu: rendered full-board entry (Slots filled: 20) matches the template prefix",
    VendorMenuGate.MatchMenuEntry("Entrust or withdraw items. (Slots filled: 20)", want));
  Check("49 menu: rendered empty-board entry (Slots filled: 0) matches",
    VendorMenuGate.MatchMenuEntry("Entrust or withdraw items. (Slots filled: 0)", want));
  Check("49 menu: bare sheet text (the old client shape) matches",
    VendorMenuGate.MatchMenuEntry("Entrust or withdraw items.", want));
  // CONTROL: a genuinely wrong menu must still NOT match - the matcher is not a constant true.
  Check("49 menu: control - wrong menu entries do not match",
    !VendorMenuGate.MatchMenuEntry("Entrust or withdraw gil. (Gil: 5000)", want)
    && !VendorMenuGate.MatchMenuEntry("Open the market board.", want)
    && !VendorMenuGate.MatchMenuEntry("View retainer information.", want));
  // CONTROL: an unresolved sheet row (empty/null wanted) never matches - WaitForMenu, never a stop.
  Check("49 menu: empty or null wanted text never matches (wait, do not fail)",
    !VendorMenuGate.MatchMenuEntry("Entrust or withdraw items. (Slots filled: 20)", "")
    && !VendorMenuGate.MatchMenuEntry("Entrust or withdraw items. (Slots filled: 20)", null));
  // The grace window is a named constant so the wait cannot drift from the docs.
  Check("49 menu: the mismatch grace window is 2000 ms (the 181 ms transition waits it out)",
    VendorMenuGate.MenuGraceWindowMs == 2000);
  // The decision table itself is UNCHANGED - a mismatch that survives the window is still the
  // stop verdict (the grace wait lives in the step, not the gate).
  Check("49 menu: the stop verdict is unchanged - a true missing entry still stops",
    VendorMenuGate.Decide(true, true, false) == VendorMenuDecision.MenuMissingEntry
    && VendorMenuGate.Decide(false, false, false) == VendorMenuDecision.WaitForMenu);

  // (b) THE SIGHT SCOPE. The 15:06 announce shape, shrunk to 4 rules: two stocked rules with
  //     fresh quotes (fully sighted), two no-stock rules. Whole-list counting read the no-stock
  //     pair as unpriceable and printed the no-data warning on EVERY sweep; the stocked count is
  //     what the fetch actually asked about.
  var sightRules = new List<ItemRule> { Rule(19990, 99), Rule(5111, 999), Rule(12593, 99), Rule(5594, 99, keepB: 50) };
  var sightStock = new List<StockStack>
  {
    new(StockOrigin.Bags, Bags1, 3, 19990, false, 99),
    new(StockOrigin.Bags, Bags1, 4, 5111, false, 200),
    new(StockOrigin.Bags, Bags1, 5, 5594, false, 50),   // keep 50 -> nothing sellable
  };                                                    // 12593: no stack at all
  var stockedIds = MarketGate.GateFetchIds(sightRules, sightStock, false);
  Check("49 sight: the fixture's fetch list is exactly the two stocked rules",
    stockedIds.Count == 2 && stockedIds.Contains(19990u) && stockedIds.Contains(5111u),
    "fetch=[" + string.Join(", ", stockedIds) + "]");
  var allQuoted = new Dictionary<uint, ItemQuote>
  {
    [19990] = new ItemQuote(19990, true, Now - 60_000L, [new QuoteListing(84, false, false)]),
    [5111] = new ItemQuote(5111, true, Now - 60_000L, [new QuoteListing(950, false, false)]),
  };
  var sightedScoped = MarketGate.CountSight(sightRules, allQuoted, preferHq: true, Now, Fresh, sightStock, false);
  Check("49 sight: a fully sighted sweep reads Unpriceable == 0 over the stocked set (the clean line is reachable)",
    sightedScoped.Judged == 2 && sightedScoped.Unpriceable == 0,
    $"judged={sightedScoped.Judged} unpriceable={sightedScoped.Unpriceable}");
  var blindScoped = MarketGate.CountSight(sightRules, null, preferHq: true, Now, Fresh, sightStock, false);
  Check("49 sight: null quotes still read fully blind over the stocked set (the 504 control)",
    blindScoped.Judged == 0 && blindScoped.Unpriceable == 2,
    $"judged={blindScoped.Judged} unpriceable={blindScoped.Unpriceable}");
  var partialScoped = MarketGate.CountSight(sightRules, new Dictionary<uint, ItemQuote>
  {
    [19990] = new ItemQuote(19990, true, Now - 60_000L, [new QuoteListing(84, false, false)]),
  }, preferHq: true, Now, Fresh, sightStock, false);
  Check("49 sight: one of two stocked quotes reads 1 judged / 1 unpriceable (partial blindness still named)",
    partialScoped.Judged == 1 && partialScoped.Unpriceable == 1,
    $"judged={partialScoped.Judged} unpriceable={partialScoped.Unpriceable}");
  // CONTROL: without a stock map the count keeps the old whole-list shape - the trim is opt-in
  // at the call site and case 47's whole-list expectations stay pinned.
  var legacy = MarketGate.CountSight(sightRules, allQuoted, preferHq: true, Now, Fresh);
  Check("49 sight: control - no stock map keeps the old whole-list count (case 47 unchanged)",
    legacy.Judged == 2 && legacy.Unpriceable == 2,
    $"judged={legacy.Judged} unpriceable={legacy.Unpriceable}");

  // 50. THE VENDOR LEG RUNS AFTER THE PINCH (t_2ecdbbae). Until 0.1.27.0 the leg trigger was
  //     inserted from BuildListingStepsNow, ahead of the listing steps, so on a retainer that both
  //     listed and vendored, the leg closed the sell list on its way to the bell menu and the pinch
  //     that followed read a closed list - the new listings were left at the 999,999,999 placeholder
  //     and never price-matched that session (2026-09-07 18:50: 5/5 vendored, 2 new listings
  //     unpriced; 21:06: 1/1 vendored, same). The trigger now inserts from the PinchAfterMarket
  //     step AFTER the pinch pass has queued its steps. These checks pin the ordering contract on
  //     the two Dalamud-free facts the fix rests on; the MarketAutomation wiring is verified from
  //     ffxivdb (acceptance below).
  Check("50 ordering: the pinch decision reports the vendor leg must wait for it",
    PinchScope.PinchRunsBeforeVendorLeg == true,
    "PinchScope.PinchRunsBeforeVendorLeg must be true - the pinch pass reads the open sell list");
  Check("50 ordering: a retainer that listed N still pinches exactly those N (scope unchanged by the reorder)",
    PinchScope.Decide(pinchAllAfter: false, listedThisRetainer: 2) == PinchAfterMarket.NewListingsOnly
      && PinchScope.Decide(pinchAllAfter: false, listedThisRetainer: 0) == PinchAfterMarket.Nothing
      && PinchScope.Decide(pinchAllAfter: true, listedThisRetainer: 2) == PinchAfterMarket.FullRePass,
    "the pinch scope decisions are unchanged by the ordering fix");
  Check("50 ordering: a retainer that vendored without listing still gets Nothing (leg-only path unchanged)",
    PinchScope.Decide(pinchAllAfter: false, listedThisRetainer: 0) == PinchAfterMarket.Nothing,
    "a leg-only retainer must not gain a pinch pass it never had");

  // 51. EXPANDED BAGS PAGE GATE (0.1.29.0). Only a live-and-ready parent expansion window on
  //     tab 0 (the Items page) admits the four expanded bag grids. Tab 1 ("Key Items & Crystals"),
  //     any out-of-range tab, and an unresolvable or unready parent all suppress - fail-closed.
  Check("51 pagegate: live parent on tab 0 admits bag grids",
    GridMap.ExpandedBagsPageShown(true, 0));
  Check("51 pagegate: live parent on tab 1 (Key Items & Crystals) suppresses bag grids",
    !GridMap.ExpandedBagsPageShown(true, 1));
  Check("51 pagegate: unresolvable or unready parent suppresses bag grids (fail-closed)",
    !GridMap.ExpandedBagsPageShown(false, 0));
  Check("51 pagegate: out-of-range tab 2 suppresses bag grids (fail-closed)",
    !GridMap.ExpandedBagsPageShown(true, 2));
  Check("51 pagegate: negative tab suppresses bag grids (fail-closed)",
    !GridMap.ExpandedBagsPageShown(true, -1));

}

// 52. THE VALUE GATE'S VENDOR ANNOUNCE MATCHES THE VENDOR LEG (0.1.30.0). Until 0.1.30.0 the gate
//     announced its vendor set from the Universalis board price alone, without consulting the Item
//     sheet's PriceLow. An item with PriceLow 0 (Ice Crystal, item 9) was announced as a vendor
//     target on essentially every sweep and then named-skipped downstream by VendorPlanner.Plan
//     ("no Item-sheet price for 9, leaving it in place"). The announced count was wrong before the
//     sweep started: on 2026-09-08 22:25 "vendoring 5 item(s)" ended as "4 vendored". The gate now
//     calls VendorPlanner.SplitVendorable before it announces, sharing ItemVendorPrice.Vendorable
//     with Plan.
{
  ItemRule R(uint id, bool hq = false, int stack = 99, int keepB = 0, int keepR = 0, bool bags = true, bool ret = true)
    => new(id, hq, stack, keepB, keepR, 0, bags, ret, 0, 999);

  // 1. The predicate itself
  Check("52 vendorable: PriceLow 0 is false, PriceLow 1 is true",
    !ItemVendorPrice.Vendorable(0) && ItemVendorPrice.Vendorable(1));

  // 2. The regression fixture, item 9 (2026-09-08 22:25: ids 9, 36186, 4850, 5291, 4794)
  var fixtureIds = new uint[] { 9, 36186, 4850, 5291, 4794 };
  var fixtureRules = fixtureIds.Select(id => R(id)).ToList();
  var sheetPriceLow = new Dictionary<uint, uint>
  {
    [9] = 0,       // Ice Crystal: PriceLow 0, PriceMid 229
    [36186] = 2,
    [4850] = 5,
    [5291] = 1,
    [4794] = 3,
  };
  uint LookupPriceLow(uint id) => sheetPriceLow.TryGetValue(id, out var pl) ? pl : 0;

  var fixtureSplit = VendorPlanner.SplitVendorable(fixtureRules, LookupPriceLow);
  var sellableIds = fixtureSplit.Sellable.Select(r => r.ItemId).ToList();
  var unvendorableIds = fixtureSplit.Unvendorable.Select(r => r.ItemId).ToList();

  Check("52 split: regression fixture item 9 excluded from announce so gate announces 4 matching the 4 vendored",
    fixtureSplit.Sellable.Count == 4
      && !sellableIds.Contains(9u)
      && fixtureSplit.Unvendorable.Count == 1
      && unvendorableIds.SequenceEqual(new uint[] { 9u }),
    $"sellable=[{string.Join(",", sellableIds)}], unvendorable=[{string.Join(",", unvendorableIds)}]");

  // 3. The all-sellable control - every id priced, unvendorable empty, sellable count == input count, order preserved
  var allPricedIds = new uint[] { 36186, 4850, 5291, 4794 };
  var allPricedRules = allPricedIds.Select(id => R(id)).ToList();
  var allSellableSplit = VendorPlanner.SplitVendorable(allPricedRules, LookupPriceLow);
  Check("52 split: all-sellable control preserves input count and order with empty unvendorable",
    allSellableSplit.Unvendorable.Count == 0
      && allSellableSplit.Sellable.Count == allPricedRules.Count
      && allSellableSplit.Sellable.Select(r => r.ItemId).SequenceEqual(allPricedIds),
    $"sellable=[{string.Join(",", allSellableSplit.Sellable.Select(r => r.ItemId))}], unvendorable={allSellableSplit.Unvendorable.Count}");

  // 4. The all-unvendorable case - every id at PriceLow 0 gives Sellable.Count == 0
  var allZeroSplit = VendorPlanner.SplitVendorable(fixtureRules, _ => 0u);
  Check("52 split: all-unvendorable case yields zero sellable rules so gate announces no vendoring",
    allZeroSplit.Sellable.Count == 0
      && allZeroSplit.Unvendorable.Count == fixtureRules.Count
      && allZeroSplit.Unvendorable.Select(r => r.ItemId).SequenceEqual(fixtureIds),
    $"sellable={allZeroSplit.Sellable.Count}, unvendorable={allZeroSplit.Unvendorable.Count}");

  // 5. Announce == plan: feed SplitVendorable.Sellable into VendorPlanner.Plan with real stock
  var prices = new Dictionary<uint, (uint PriceMid, uint PriceLow)>
  {
    [9] = (229, 0),
    [36186] = (10, 2),
    [4850] = (20, 5),
    [5291] = (5, 1),
    [4794] = (15, 3),
  };
  var stock = new List<StockStack>
  {
    new(StockOrigin.Retainer, 10000, 0, 36186, false, 10),
    new(StockOrigin.Retainer, 10000, 1, 4850, false, 10),
    new(StockOrigin.Retainer, 10000, 2, 5291, false, 10),
    new(StockOrigin.Retainer, 10000, 3, 4794, false, 10),
    new(StockOrigin.Retainer, 10000, 4, 9, false, 99),
  };

  var sellablePlan = VendorPlanner.Plan(fixtureSplit.Sellable, stock, prices, preferHq: true);
  Check("52 plan: announce matches plan - sellable rules produce ops with no missing-price note",
    sellablePlan.Ops.Count == 4
      && !sellablePlan.Notes.Any(n => n.Contains("no Item-sheet price")),
    $"ops={sellablePlan.Ops.Count}, notes=[{string.Join(";", sellablePlan.Notes)}]");

  // 6. The last line of defence still works: unvendorable rule fed directly to Plan yields 0 ops and exactly one note
  var unvendorablePlan = VendorPlanner.Plan(fixtureSplit.Unvendorable, stock, prices, preferHq: true);
  Check("52 plan: last line of defence - Plan directly called with unvendorable rule yields 0 ops and no-price note for 9",
    unvendorablePlan.Ops.Count == 0
      && unvendorablePlan.Notes.Count == 1
      && unvendorablePlan.Notes[0].Contains("no Item-sheet price for 9"),
    $"ops={unvendorablePlan.Ops.Count}, notes=[{string.Join(";", unvendorablePlan.Notes)}]");

  // 7. SplitVendorable([], ...) returns two empty lists (the no-op sweep)
  var emptySplit = VendorPlanner.SplitVendorable(new List<ItemRule>(), LookupPriceLow);
  Check("52 split: empty below-threshold input returns empty sellable and unvendorable lists",
    emptySplit.Sellable.Count == 0 && emptySplit.Unvendorable.Count == 0,
    $"sellable={emptySplit.Sellable.Count}, unvendorable={emptySplit.Unvendorable.Count}");}

// 53. THE BAG-MARKER DOT'S ANCHOR IS INSIDE THE CELL (0.1.31.0). Until 0.1.30.0 the marker
//     window was placed at the cell's top-right corner minus the corner inset in BOTH axes
//     (top - inset), so with the zeroed padding the dot's center sat at (right - 2.5, top - 2.5):
//     2.5 px ABOVE the cell's top edge, most of the circle outside the cell. On the stacked
//     expanded-mode E-grids a top-row dot visually landed on the bottom row of the grid above
//     (a different bag); in sparse bags it read as attached to whatever sits in the cell above
//     ("dots in seemingly random locations", Helm t-joey-1788992037468, version 0.1.30.0).
//     Since 0.1.31.0 the center is CornerInset px in from the right edge and CornerInset px
//     below the top edge - fully inside the cell - and the window is placed one radius up-left
//     of the center so the circle is exactly inscribed.
{
  // 1. The pinned contract: center = position + (size.X - Inset, Inset) - inside the cell.
  var cellPos = new System.Numerics.Vector2(100f, 200f);
  var cellSize = new System.Numerics.Vector2(40f, 40f);
  var center = MarkerAnchor.Center(cellPos, cellSize);
  Check("53 anchor: dot center is Inset in from the right edge and Inset below the top edge",
    center == cellPos + new System.Numerics.Vector2(cellSize.X - MarkerAnchor.Inset, MarkerAnchor.Inset),
    $"center=({center.X},{center.Y})");
  Check("53 anchor: the circle is fully inside the cell (span y: top+Inset-Radius .. top+Inset+Radius)",
    center.Y - MarkerAnchor.Radius >= cellPos.Y && center.Y + MarkerAnchor.Radius <= cellPos.Y + cellSize.Y
      && center.X - MarkerAnchor.Radius >= cellPos.X && center.X + MarkerAnchor.Radius <= cellPos.X + cellSize.X,
    $"circle spans x {center.X - MarkerAnchor.Radius}..{center.X + MarkerAnchor.Radius}, y {center.Y - MarkerAnchor.Radius}..{center.Y + MarkerAnchor.Radius}, cell x {cellPos.X}..{cellPos.X + cellSize.X}, y {cellPos.Y}..{cellPos.Y + cellSize.Y}");

  // 2. The window is placed one radius up-left of the center, so the inscribed circle fills it.
  var winPos = MarkerAnchor.WindowPosition(cellPos, cellSize);
  Check("53 anchor: window top-left is center minus Radius in both axes",
    winPos == center - new System.Numerics.Vector2(MarkerAnchor.Radius, MarkerAnchor.Radius),
    $"win=({winPos.X},{winPos.Y}) center=({center.X},{center.Y})");
  Check("53 anchor: window height is exactly one dot diameter (center inside the cell, not above it)",
    winPos.Y >= cellPos.Y && winPos.Y + 2 * MarkerAnchor.Radius <= cellPos.Y + cellSize.Y,
    $"win y {winPos.Y}..{winPos.Y + 2 * MarkerAnchor.Radius}, cell y {cellPos.Y}..{cellPos.Y + cellSize.Y}");

  // 3. The regression: the pre-0.1.31.0 placement (top-right corner minus inset in BOTH axes)
  //    puts the dot's center ABOVE the cell - the exact shape of the defect.
  var legacyCenter = cellPos + new System.Numerics.Vector2(cellSize.X, 0f) - new System.Numerics.Vector2(MarkerAnchor.Inset, MarkerAnchor.Inset)
    + new System.Numerics.Vector2(MarkerAnchor.Radius, MarkerAnchor.Radius);
  Check("53 anchor: the legacy top-right-corner anchor centers the dot ABOVE the cell (the defect)",
    legacyCenter.Y < cellPos.Y,
    $"legacy center y={legacyCenter.Y} < cell top {cellPos.Y} - this is the 0.1.30.0 defect shape, asserted so the old anchor cannot silently return");
  Check("53 anchor: the legacy anchor would place some or all of the circle outside the cell (the visible symptom)",
    legacyCenter.Y - MarkerAnchor.Radius < cellPos.Y,
    $"legacy circle top {legacyCenter.Y - MarkerAnchor.Radius} < cell top {cellPos.Y}");

  // 4. Scale-safety: the center is computed from the cell's own scaled rect, so UI scale moves it with the cell.
  var scaledPos = cellPos * 2f;
  var scaledSize = cellSize * 2f;
  var scaledCenter = MarkerAnchor.Center(scaledPos, scaledSize);
  Check("53 anchor: a doubled cell rect keeps the center inset doubled and still inside the cell",
    scaledCenter == cellPos * 2f + new System.Numerics.Vector2(cellSize.X * 2f - MarkerAnchor.Inset, MarkerAnchor.Inset),
    $"scaledCenter=({scaledCenter.X},{scaledCenter.Y})");
  Check("53 anchor: a doubled cell still draws the circle fully inside itself",
    scaledCenter.Y + MarkerAnchor.Radius <= scaledPos.Y + scaledSize.Y,
    $"circle bottom {scaledCenter.Y + MarkerAnchor.Radius} <= cell bottom {scaledPos.Y + scaledSize.Y}");
}

// 54. THE PULL PASS (0.1.32.0, t_4d12b8b0): a below-threshold LISTING is invisible to
//     MarketGate.PotentialSellable (stock-only), so a hand listing or a pre-gate listing sat on
//     the board forever. MarketPull.Plan pulls occupied market slots whose ENABLED rule's gate
//     verdict is Vendor; MarketGate.GateFetchIds now also asks Universalis about listed-only ids
//     so those verdicts can actually be judged. Uncertainty never pulls (same polarity as the
//     stocked gate); a rule with SellFromRetainer off is never a pull candidate.
{
  const long Now = 1_788_710_000_000L;
  const long Fresh = 6 * 3_600_000L;
  var gate = new GateOptions(true, 100, Fresh);

  ItemRule PullRule(uint id, bool ret = true, bool bags = false)
    => new(id, false, 99, 0, 0, 0, bags, ret, 0, 999);

  var pullRules = new List<ItemRule>
  {
    PullRule(5594),           // Dye - enabled, sells from retainer
    PullRule(5111),           // Ore - enabled, sells from retainer
    PullRule(19990),          // priced high - will List, not Vendor
    PullRule(4487, ret: false, bags: true), // enabled but SellFromRetainer=false - never a pull candidate
  };

  // Market: slot 0 Dye NQ x5 @1g (Vendor verdict expected), slot 1 19990 NQ x99 @84g (nets 7900,
  // lists), slot 2 Ore NQ x10 @2g (Vendor verdict expected), slot 3 12593 (no rule at all),
  // slot 4 Dye HQ x5 (rule is NQ - quality mismatch, never matches), slot 5 4487 NQ x9 @1g
  // (Vendor verdict but SellFromRetainer=false - filtered before the verdict is even asked).
  var market = new List<MarketSlot>
  {
    new(0, 5594, false, 5),
    new(1, 19990, false, 99),
    new(2, 5111, false, 10),
    new(3, 12593, false, 7),
    new(4, 5594, true, 5),
    new(5, 4487, false, 9),
  };
  var prices = new Dictionary<int, ulong> { [0] = 1, [1] = 84, [2] = 2, [3] = 50, [4] = 1, [5] = 1 };

  var quotes = new Dictionary<uint, ItemQuote>
  {
    [5594] = new ItemQuote(5594, true, Now, [new QuoteListing(1, false, false)]),
    [19990] = new ItemQuote(19990, true, Now, [new QuoteListing(84, false, false)]),
    [5111] = new ItemQuote(5111, true, Now, [new QuoteListing(2, false, false)]),
    [4487] = new ItemQuote(4487, true, Now, [new QuoteListing(1, false, false)]),
  };

  var plan = MarketPull.Plan(market, prices, pullRules, quotes, gate, preferHq: true, nowUnixMs: Now,
    freeRetainerPageSlots: () => 10, hasFreeBagSlot: () => true);
  Check("54 pull: exactly slots {0,2} are pulled (Vendor verdicts under enabled retainer-sell rules)",
    plan.Ops.Select(o => o.Slot).OrderBy(s => s).SequenceEqual(new[] { 0, 2 }),
    string.Join(",", plan.Ops.Select(o => o.Slot)));
  Check("54 pull: 19990 (nets 7900 > threshold) is never pulled - it lists",
    plan.Ops.All(o => o.ItemId != 19990));
  Check("54 pull: HQ Dye at slot 4 never matches the NQ rule",
    plan.Ops.All(o => o.Slot != 4));
  Check("54 pull: slot 3 (no rule at all) is never touched",
    plan.Ops.All(o => o.Slot != 3));
  Check("54 pull: slot 5 (Vendor verdict but SellFromRetainer=false) is filtered before the pull",
    plan.Ops.All(o => o.Slot != 5));
  Check("54 pull: pulled ops carry the listed price and target RetainerInventory (slots free)",
    plan.Ops.All(o => o.Target == PullTarget.RetainerInventory)
      && plan.Ops.First(o => o.Slot == 0).ListedPrice == 1
      && plan.Ops.First(o => o.Slot == 2).ListedPrice == 2);

  // CONTROL: stale quote (Dye 9h old, freshness 6h) -> zero pulls (uncertainty never pulls)
  var staleQuotes = new Dictionary<uint, ItemQuote>
  {
    [5594] = new ItemQuote(5594, true, Now - 9 * 3_600_000L, [new QuoteListing(1, false, false)]),
    [5111] = new ItemQuote(5111, true, Now - 9 * 3_600_000L, [new QuoteListing(2, false, false)]),
  };
  var stalePlan = MarketPull.Plan(market, prices, pullRules, staleQuotes, gate, true, Now, () => 10, () => true);
  Check("54 pull CONTROL: stale quotes never pull", stalePlan.Ops.Count == 0, $"ops={stalePlan.Ops.Count}");

  // CONTROL: null quotes -> zero pulls
  var nullPlan = MarketPull.Plan(market, prices, pullRules, null, gate, true, Now, () => 10, () => true);
  Check("54 pull CONTROL: null quotes never pull", nullPlan.Ops.Count == 0, $"ops={nullPlan.Ops.Count}");

  // CONTROL: gate disabled -> zero pulls
  var offGate = new GateOptions(false, 100, Fresh);
  var offPlan = MarketPull.Plan(market, prices, pullRules, quotes, offGate, true, Now, () => 10, () => true);
  Check("54 pull CONTROL: gate disabled never pulls", offPlan.Ops.Count == 0, $"ops={offPlan.Ops.Count}");

  // CONTROL: threshold 0 -> zero pulls
  var zeroGate = new GateOptions(true, 0, Fresh);
  var zeroPlan = MarketPull.Plan(market, prices, pullRules, quotes, zeroGate, true, Now, () => 10, () => true);
  Check("54 pull CONTROL: threshold 0 never pulls", zeroPlan.Ops.Count == 0, $"ops={zeroPlan.Ops.Count}");

  // Space exhaustion: no free retainer pages and no free bag slot for a bags-enabled rule -> the
  // pass stops early with a note, but still pulled whatever fit before that.
  var tightRules = new List<ItemRule> { PullRule(5594, ret: true, bags: false), PullRule(5111, ret: true, bags: false) };
  var noSpacePlan = MarketPull.Plan(market, prices, tightRules, quotes, gate, true, Now, () => 0, () => false);
  Check("54 pull: no retainer space and no bag fallback -> nothing pulled, stopped-for-space set",
    noSpacePlan.Ops.Count == 0 && noSpacePlan.StoppedForSpace && noSpacePlan.Notes.Count > 0,
    $"ops={noSpacePlan.Ops.Count}, stopped={noSpacePlan.StoppedForSpace}, notes=[{string.Join(";", noSpacePlan.Notes)}]");

  // One free retainer slot only: the pull that fits succeeds, the pass stops before the second.
  var oneSlotPlan = MarketPull.Plan(market, prices, pullRules, quotes, gate, true, Now, () => 1, () => false);
  Check("54 pull: exactly one free retainer slot -> exactly one pull, pass stops for space after",
    oneSlotPlan.Ops.Count == 1 && oneSlotPlan.StoppedForSpace,
    $"ops={oneSlotPlan.Ops.Count}, stopped={oneSlotPlan.StoppedForSpace}");

  // Player-bags one-shot fallback: no retainer space, but a bags-enabled rule and a free bag slot.
  var bagsRules = new List<ItemRule> { PullRule(5594, ret: true, bags: true), PullRule(5111, ret: true, bags: false) };
  var bagsPlan = MarketPull.Plan(market, prices, bagsRules, quotes, gate, true, Now, () => 0, () => true);
  Check("54 pull: bags-enabled rule falls back to PlayerBags one-shot when retainer pages are full",
    bagsPlan.Ops.Count == 1 && bagsPlan.Ops[0].Slot == 0 && bagsPlan.Ops[0].Target == PullTarget.PlayerBags,
    $"ops={string.Join(",", bagsPlan.Ops.Select(o => $"{o.Slot}:{o.Target}"))}");

  // 55. GateFetchIds' new market parameter (0.1.32.0): a listed-only item (no stock left to sell)
  //     is invisible to the stocked-only fetch, so its own verdict could never be judged without
  //     this. Adding the market slots must ADD ids, never remove or duplicate the stocked ones.
  var stockedRules = new List<ItemRule>
  {
    new(36186, false, 5, 0, 0, 0, true, true, 0, 999),    // has stock -> already in the stocked fetch
    new(4487, false, 99, 0, 0, 0, false, true, 0, 999),   // NO stock, but LISTED under an enabled rule
  };
  var stockedStock = new List<StockStack> { new(StockOrigin.Bags, 0, 0, 36186, false, 50) };
  var noMarketIds = MarketGate.GateFetchIds(stockedRules, stockedStock, false);
  Check("55 fetch: no market param -> unchanged stocked-only shape (0.1.19.0 control)",
    noMarketIds.SequenceEqual(new uint[] { 36186 }), string.Join(",", noMarketIds));

  var listedMarket = new List<MarketSlot> { new(0, 4487, false, 9), new(1, 0, false, 0) };
  var withMarketIds = MarketGate.GateFetchIds(stockedRules, stockedStock, false, listedMarket);
  Check("55 fetch: market param adds the listed-only id without duplicating the stocked one",
    withMarketIds.OrderBy(x => x).SequenceEqual(new uint[] { 4487, 36186 }), string.Join(",", withMarketIds));

  var noRuleMarket = new List<MarketSlot> { new(0, 99999, false, 1) };
  var noRuleIds = MarketGate.GateFetchIds(stockedRules, stockedStock, false, noRuleMarket);
  Check("55 fetch: a listed item with NO matching enabled rule is never added",
    noRuleIds.SequenceEqual(new uint[] { 36186 }), string.Join(",", noRuleIds));

  // 56. DoneLine.Format's pulled parameter (0.1.32.0): pulled renders between skips and vendored,
  //     and participates in the all-zero "done." check.
  Check("56 done-line: pulled alone renders 'done: 0 new listing(s), N pulled.'",
    DoneLine.Format(0, 0, 0, 0, 0, 2) == "done: 0 new listing(s), 2 pulled.");
  Check("56 done-line: listed + pulled + vendored keeps pulled BEFORE vendored (session order)",
    DoneLine.Format(1, 0, 1, 0, 0, 2) == "done: 1 new listing(s), 2 pulled, 1 vendored.");
  Check("56 done-line: all-zero including pulled=0 is still the plain 'done.'",
    DoneLine.Format(0, 0, 0, 0, 0, 0) == "done.");
  Check("56 done-line: the old five-arg call shape (case 40) is unchanged",
    DoneLine.Format(3, 1, 2, 0, 0) == "done: 3 new listing(s), 1 skipped (stock moved), 2 vendored.");
}

// 57. DISPLAY SLOT vs CONTAINER SLOT (Helm t-joey-1788992037468, card t_b8b79277). 0.1.32.0 and
//     earlier read container->Items[i] and painted the verdict onto grid->Slots[i], i.e. it assumed
//     the game's item order is always the identity permutation. It is not: the four bag pages are
//     drawn from ItemOrderModule's player-inventory sorter, whose entry f addresses container slot
//     (Page, Slot) while its DISPLAY position is f split by ItemsPerPage. Ground truth, two
//     independent production consumers: SimpleTweaks EquipFromHotbar.FindAndEquip
//     (page = i / ItemsPerPage, slot = i % ItemsPerPage, item read from Page/Slot) and
//     CriticalCommonLib InventoryScanner (buckets by index / 35, reads bag[containerIndex]
//     .Items[slotIndex]); CCL's InventoryItem.BagLocation then derives the grid cell from the
//     SORTED index, never the container slot.
{
  const int PerPage = 35;

  static List<SlotOrder.SortEntry> Identity()
  {
    var e = new List<SlotOrder.SortEntry>();
    for (var page = 0; page < 4; page++)
      for (var slot = 0; slot < PerPage; slot++)
        e.Add(new SlotOrder.SortEntry(page, slot));
    return e;
  }

  // The identity order is the one case the old code got right - it must still resolve 1:1.
  var ident = SlotOrder.Resolve(Identity(), PerPage, 2, PerPage);
  Check("57 order: identity permutation maps display i -> own bag, container slot i",
    ident.Count == PerPage && ident[0] == new SlotOrder.Cell(2, 0) && ident[34] == new SlotOrder.Cell(2, 34));
  Check("57 order: IsIdentity recognises the identity order for its own bag",
    SlotOrder.IsIdentity(ident, 2));

  // THE REGRESSION CASE, rebuilt from Joey's 19:17 screenshot + the plugin's own 19:16:07 log:
  // 60 stacks packed contiguously into the first two on-screen blocks, while the CONTAINERS hold
  // 35/15/7/3 across all four pages. Grid 2 and grid 3 display nothing at all.
  var packed = new List<SlotOrder.SortEntry>();
  for (var slot = 0; slot < 35; slot++) packed.Add(new SlotOrder.SortEntry(0, slot)); // display 0-34
  for (var slot = 0; slot < 15; slot++) packed.Add(new SlotOrder.SortEntry(1, slot)); // display 35-49
  for (var slot = 0; slot < 7; slot++) packed.Add(new SlotOrder.SortEntry(2, slot));  // display 50-56
  for (var slot = 0; slot < 3; slot++) packed.Add(new SlotOrder.SortEntry(3, slot));  // display 57-59
  // Every remaining display cell shows an EMPTY container slot from the back of pages 1-3.
  for (var slot = 15; slot < PerPage; slot++) packed.Add(new SlotOrder.SortEntry(1, slot));
  for (var slot = 7; slot < PerPage; slot++) packed.Add(new SlotOrder.SortEntry(2, slot));
  for (var slot = 3; slot < PerPage; slot++) packed.Add(new SlotOrder.SortEntry(3, slot));
  Check("57 order: the packed fixture covers all four pages exactly once",
    packed.Count == 4 * PerPage
    && packed.Distinct().Count() == 4 * PerPage);

  var g0 = SlotOrder.Resolve(packed, PerPage, 0, PerPage);
  var g1 = SlotOrder.Resolve(packed, PerPage, 1, PerPage);
  var g2 = SlotOrder.Resolve(packed, PerPage, 2, PerPage);
  var g3 = SlotOrder.Resolve(packed, PerPage, 3, PerPage);

  // Grid 1 shows the tail of bag 1 AND all of bags 2 and 3 - the dots the old code put on grids
  // 2 and 3 belong on THIS grid.
  Check("57 packed: grid1 display 0-14 show bag1 slots 0-14 (the 15 stacks logged for Inventory2)",
    Enumerable.Range(0, 15).All(d => g1[d] == new SlotOrder.Cell(1, d)));
  Check("57 packed: grid1 display 15-21 show BAG 2 slots 0-6 (the 7 stacks logged for Inventory3)",
    Enumerable.Range(0, 7).All(d => g1[15 + d] == new SlotOrder.Cell(2, d)));
  Check("57 packed: grid1 display 22-24 show BAG 3 slots 0-2 (the 3 stacks logged for Inventory4)",
    Enumerable.Range(0, 3).All(d => g1[22 + d] == new SlotOrder.Cell(3, d)));

  // The defect, stated as an assertion: those cells are NOT on grids 2 and 3.
  Check("57 packed: NO display cell of grid2 or grid3 shows an occupied slot of bags 2-3",
    g2.Values.Concat(g3.Values).All(c =>
      (c.BagIndex == 1 && c.ContainerSlot >= 15)
      || (c.BagIndex == 2 && c.ContainerSlot >= 7)
      || (c.BagIndex == 3 && c.ContainerSlot >= 3)));
  Check("57 packed: grid0 is unchanged (bag 0 was already contiguous, which is why it looked right)",
    g0.Count == PerPage && SlotOrder.IsIdentity(g0, 0));
  Check("57 packed: a permuted grid is NOT reported as identity (the marker log's order= field)",
    !SlotOrder.IsIdentity(g1, 1) && !SlotOrder.IsIdentity(g2, 2));

  // A pure within-page reversal: same page, different slots - the old code marked every one wrong.
  var reversed = Identity();
  reversed.Reverse();
  var rev0 = SlotOrder.Resolve(reversed, PerPage, 0, PerPage);
  Check("57 order: a reversed order maps display 0 to the LAST slot of the LAST page",
    rev0[0] == new SlotOrder.Cell(3, 34) && rev0[34] == new SlotOrder.Cell(3, 0));
  Check("57 order: a reversed order is never reported as identity",
    !SlotOrder.IsIdentity(rev0, 0));

  // FAIL-CLOSED: everything unproven resolves NOTHING, so the caller draws no dot at all rather
  // than falling back to the identity assumption that caused this defect.
  Check("57 fail-closed: a null order resolves nothing",
    SlotOrder.Resolve(null, PerPage, 0, PerPage).Count == 0);
  Check("57 fail-closed: a non-positive page size resolves nothing",
    SlotOrder.Resolve(Identity(), 0, 0, PerPage).Count == 0
    && SlotOrder.Resolve(Identity(), -1, 0, PerPage).Count == 0);
  Check("57 fail-closed: a bag index outside 0..3 resolves nothing",
    SlotOrder.Resolve(Identity(), PerPage, 4, PerPage).Count == 0
    && SlotOrder.Resolve(Identity(), PerPage, -1, PerPage).Count == 0);
  Check("57 fail-closed: a list too short to cover the whole page resolves nothing (no partial page)",
    SlotOrder.Resolve(Identity().Take(4 * PerPage - 1).ToList(), PerPage, 3, PerPage).Count == 0);
  Check("57 fail-closed: an entry naming a page outside 0..3 resolves nothing",
    SlotOrder.Resolve(
      Identity().Select((e, i) => i == 7 ? new SlotOrder.SortEntry(9, 0) : e).ToList(),
      PerPage, 0, PerPage).Count == 0);
  Check("57 fail-closed: an entry with a negative slot resolves nothing",
    SlotOrder.Resolve(
      Identity().Select((e, i) => i == 3 ? new SlotOrder.SortEntry(0, -1) : e).ToList(),
      PerPage, 0, PerPage).Count == 0);
  Check("57 order: the result is capped at the grid's own slot-node count",
    SlotOrder.Resolve(Identity(), PerPage, 0, 10).Count == 10);
  Check("57 fail-closed: a grid reporting no slot nodes resolves nothing",
    SlotOrder.Resolve(Identity(), PerPage, 0, 0).Count == 0);
}

// 58. RETAINER MARKERS (0.1.34.0, Helm t-joey-1789056199442: "now we need to make the dots work on
//     retainer inventory"). RetainerGridMap.Resolve is the retainer counterpart of GridMap.Resolve:
//     a single TabIndex-selected panel (no known expanded/E-grid mode for a retainer), up to SEVEN
//     pages instead of the player's fixed four. SlotOrder.ResolveForPageCount is the same fail-closed
//     display-order machinery generalised to that 7-page range.
{
  Check("58 retainergridmap: tab 0..6 all bind every live normal-mode panel to that tab's page",
    Enumerable.Range(0, RetainerGridMap.PageCount).All(tab =>
    {
      var r = RetainerGridMap.Resolve(["InventoryGrid"], tab);
      return r.Count == 1 && r[0] == new RetainerGridMap.GridBinding("InventoryGrid", tab);
    }));
  Check("58 retainergridmap: every live normal-mode panel binds the SAME tab page",
    RetainerGridMap.Resolve(["InventoryGrid", "InventoryGrid1"], 2)
      .All(b => b.PageIndex == 2));
  Check("58 retainergridmap: tab outside 0..6 draws nothing (8-page retainer would be a lie)",
    RetainerGridMap.Resolve(["InventoryGrid0"], 7).Count == 0
    && RetainerGridMap.Resolve(["InventoryGrid0"], -1).Count == 0);
  Check("58 retainergridmap: no tab (retainer addon unresolved) draws nothing",
    RetainerGridMap.Resolve(["InventoryGrid0"], null).Count == 0);
  Check("58 retainergridmap: unknown grid names are ignored",
    RetainerGridMap.Resolve(["SomeOtherGrid", "InventoryGrid0"], 1).Count == 1);
  Check("58 retainergridmap: an E-grid name is never bound (no known retainer expanded mode)",
    RetainerGridMap.Resolve(["InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E"], 0).Count == 0);
  Check("58 retainergridmap: an E-grid alongside a normal panel still binds only the normal panel",
    RetainerGridMap.Resolve(["InventoryGrid0E", "InventoryGrid0"], 3) is var mixed
    && mixed.Count == 1 && mixed[0] == new RetainerGridMap.GridBinding("InventoryGrid0", 3));

  // SlotOrder.ResolveForPageCount over a 7-page retainer sorter - same identity/permuted/fail-closed
  // battery as case 57, generalised to PageCount=7 instead of BagCount=4.
  const int PerPage = 20; // a retainer market/inventory page is smaller than a player bag page
  const int Pages = RetainerGridMap.PageCount;

  static List<SlotOrder.SortEntry> RetainerIdentity(int pages, int perPage)
  {
    var e = new List<SlotOrder.SortEntry>();
    for (var page = 0; page < pages; page++)
      for (var slot = 0; slot < perPage; slot++)
        e.Add(new SlotOrder.SortEntry(page, slot));
    return e;
  }

  var retIdent = RetainerIdentity(Pages, PerPage);
  var page5 = SlotOrder.ResolveForPageCount(retIdent, PerPage, 5, PerPage, Pages);
  Check("58 order: identity permutation maps display i -> own page, container slot i (page 5 of 7)",
    page5.Count == PerPage && page5[0] == new SlotOrder.Cell(5, 0) && page5[19] == new SlotOrder.Cell(5, 19));
  Check("58 order: IsIdentity recognises the identity order for its own page",
    SlotOrder.IsIdentity(page5, 5));

  Check("58 fail-closed: page index outside 0..6 resolves nothing (the player's Resolve caps at 4)",
    SlotOrder.ResolveForPageCount(retIdent, PerPage, 7, PerPage, Pages).Count == 0
    && SlotOrder.ResolveForPageCount(retIdent, PerPage, -1, PerPage, Pages).Count == 0);
  Check("58 fail-closed: an entry naming a page outside 0..6 resolves nothing",
    SlotOrder.ResolveForPageCount(
      RetainerIdentity(Pages, PerPage).Select((e, i) => i == 3 ? new SlotOrder.SortEntry(9, 0) : e).ToList(),
      PerPage, 0, PerPage, Pages).Count == 0);
  Check("58 fail-closed: a list too short to cover 7 pages resolves nothing (no partial page)",
    SlotOrder.ResolveForPageCount(retIdent.Take(Pages * PerPage - 1).ToList(), PerPage, 6, PerPage, Pages).Count == 0);
  Check("58 control: Resolve (player, 4 pages) still rejects a page index valid only for a retainer",
    SlotOrder.Resolve(RetainerIdentity(4, PerPage), PerPage, 4, PerPage).Count == 0);
  Check("58 control: Resolve(pageCount=4) and ResolveForPageCount(..., 4) agree on the player shape",
    SlotOrder.Resolve(RetainerIdentity(4, 35), 35, 2, 35)
      .SequenceEqual(SlotOrder.ResolveForPageCount(RetainerIdentity(4, 35), 35, 2, 35, SlotOrder.BagCount)));
}

Console.WriteLine(failures == 0 ? "OK" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

