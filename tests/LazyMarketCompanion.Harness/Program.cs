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

Console.WriteLine(failures == 0 ? "OK" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
