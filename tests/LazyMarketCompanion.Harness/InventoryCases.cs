using LazyMarketCompanion;
using LazyMarketCompanion.AutoMarket;
using LazyMarketCompanion.Inventory;

// Offline cases for the Inventory tab's Dalamud-free core (src/LazyMarketCompanion/Inventory/Core).
// Adversarial cases are marked RAIL: they pin the things that must never happen to an item.
internal static class InventoryCases
{
  // Generic fixture ids. Retainer names are placeholders, never real ones.
  private const ulong Cid = 0x00400000AABBCCDDUL;
  private const ulong OtherCid = 0x0040000011223344UL;
  private const string RetA = "RetainerA";
  private const string RetB = "RetainerB";

  private const uint Mat = 5000;        // marketable material, not gear, not desynth
  private const uint Mat2 = 5001;
  private const uint GreenGear = 6000;  // green equippable gear, marketable, desynthable
  private const uint WhiteGear = 6001;  // white equippable gear, marketable, desynthable
  private const uint Unmarket = 7000;   // tradable, no search category, desynthable
  private const uint UnmarketNoDesynth = 7001;
  private const uint UniqueItem = 8000; // unique, marketable
  private const uint Untradable = 8001;
  private const uint BlueItem = 8002;   // rare
  private const uint NoPrice = 8003;    // marketable, PriceLow 0, not desynth
  private const uint NoPriceDesynth = 8004;
  private const uint Missing = 9999;    // sheet miss
  private const uint UiCatMetal = 48;
  private const uint UiCatGear = 34;

  private const long Now = 1_790_000_000_000L; // unix ms
  private const long NowSec = Now / 1000;

  private static ItemFacts? Facts(uint id) => id switch
  {
    Mat => new ItemFacts(Mat, UiCatMetal, 47, false, false, 1, ArmourySlot.None, 0, 3, 4),
    Mat2 => new ItemFacts(Mat2, UiCatMetal, 47, false, false, 1, ArmourySlot.None, 0, 2, 3),
    GreenGear => new ItemFacts(GreenGear, UiCatGear, 31, false, false, 2, ArmourySlot.Body, 50, 300, 400),
    WhiteGear => new ItemFacts(WhiteGear, UiCatGear, 31, false, false, 1, ArmourySlot.Head, 40, 200, 250),
    Unmarket => new ItemFacts(Unmarket, 60, 0, false, false, 1, ArmourySlot.None, 30, 10, 12),
    UnmarketNoDesynth => new ItemFacts(UnmarketNoDesynth, 60, 0, false, false, 1, ArmourySlot.None, 0, 10, 12),
    UniqueItem => new ItemFacts(UniqueItem, 60, 50, false, true, 1, ArmourySlot.None, 0, 10, 12),
    Untradable => new ItemFacts(Untradable, 60, 0, true, false, 1, ArmourySlot.None, 0, 10, 12),
    BlueItem => new ItemFacts(BlueItem, 60, 50, false, false, 3, ArmourySlot.None, 0, 10, 12),
    NoPrice => new ItemFacts(NoPrice, 60, 50, false, false, 1, ArmourySlot.None, 0, 0, 0),
    NoPriceDesynth => new ItemFacts(NoPriceDesynth, 60, 50, false, false, 1, ArmourySlot.None, 20, 0, 0),
    _ => null,
  };

  private static ItemQuote Quote(uint id, long unit, bool hq = false, long ageMs = 60_000)
    => new(id, true, Now - ageMs, [new QuoteListing(unit, hq, false)]);

  private static GateOptions Gate(bool on = true, long threshold = 1000) => new(on, threshold, 6 * 3_600_000L);

  private static LootOptions Opts(bool allowHq = false, IEnumerable<uint>? optIns = null, GateOptions? gate = null, long fetchedAgoMs = 60_000, int lookback = 30)
    => new(lookback, allowHq, new HashSet<uint>(optIns ?? []), gate ?? Gate(), false, Now, fetchedAgoMs < 0 ? 0 : Now - fetchedAgoMs, 30 * 60_000L);

  private static VentureRecord Qe(uint id, bool hq = false, uint amount = 1, long agoSec = 3600, uint venture = 395)
    => new(id, hq, NowSec - agoSec, amount, venture);

  private static RetainerStack Page(int slot, uint id, int qty = 1, bool hq = false, int page = 0, bool collectable = false)
    => new(10000 + page, slot, id, hq, qty, collectable);

  private static ArSnapshot EmptyAr() => new() { Available = true, RecordStats = true };

  private static LootInput Input(
    IEnumerable<RetainerStack> stacks,
    IEnumerable<VentureRecord> records,
    Dictionary<uint, ItemQuote>? quotes,
    ArSnapshot? ar = null,
    IEnumerable<AmEntry>? am = null,
    IEnumerable<uint>? gearset = null,
    IEnumerable<uint>? bags = null,
    string retainer = RetA)
    => new(retainer, stacks.ToList(), records.ToList(), Facts, (am ?? []).ToList(), ar ?? EmptyAr(),
      new HashSet<uint>(gearset ?? []), new HashSet<uint>(bags ?? []), quotes);

  private static Dictionary<uint, ItemQuote> CheapQuotes()
    => new()
    {
      [Mat] = Quote(Mat, 5), [Mat2] = Quote(Mat2, 5), [GreenGear] = Quote(GreenGear, 50), [WhiteGear] = Quote(WhiteGear, 50),
      [UniqueItem] = Quote(UniqueItem, 5), [BlueItem] = Quote(BlueItem, 5), [NoPrice] = Quote(NoPrice, 5),
      [NoPriceDesynth] = Quote(NoPriceDesynth, 5),
    };

  private static LootRow One(LootPlan p) => p.Rows.Single();

  private static ArSnapshot ArWith(
    IEnumerable<ArEntrustPlan>? plans = null,
    Dictionary<string, string>? byRetainer = null,
    ArImLists? im = null,
    bool available = true)
    => new()
    {
      Available = available,
      Status = available ? "ok" : "AutoRetainer config not found",
      Plans = (plans ?? []).ToList(),
      PlanByRetainer = byRetainer ?? new Dictionary<string, string>(),
      Im = im ?? ArImLists.Empty,
      RecordStats = true,
    };

  private static ArEntrustPlan Plan(string guid, IEnumerable<uint>? items = null, IEnumerable<uint>? cats = null, bool dup = false, bool multi = false, bool manual = false, bool exclProt = false, string name = "Plan")
    => new(guid, name, dup, multi, new HashSet<uint>(items ?? []), new HashSet<uint>(cats ?? []), manual, exclProt);

  private static ArImLists Im(IEnumerable<uint>? hard = null, IEnumerable<uint>? soft = null, IEnumerable<uint>? discard = null, IEnumerable<uint>? desynth = null, IEnumerable<uint>? protect = null, bool autoVendor = true, bool desynthOn = true)
    => new(new HashSet<uint>(hard ?? []), new HashSet<uint>(), new HashSet<uint>(soft ?? []), new HashSet<uint>(discard ?? []), new HashSet<uint>(),
      new HashSet<uint>(desynth ?? []), new HashSet<uint>(protect ?? []), 9999, 9, autoVendor, desynthOn);

  public static void Run(Action<string, bool, string> check)
  {
    void Check(string name, bool ok, string detail = "") => check(name, ok, detail);

    // =====================================================================================
    // I1. AutoRetainer DefaultConfig.json parsing (shape pinned against AutoRetainer 4.6.1.34)
    // =====================================================================================
    var cidHex = Cid.ToString("X16");
    var otherHex = OtherCid.ToString("X16");
    var arJson = $$"""
    {
      "RecordStats": true,
      "OfflineData": [
        { "CID": {{Cid}}, "InventoryCleanupPlan": "00000000-0000-0000-0000-000000000000", "RetainerData": [ { "Name": "{{RetA}}" } ] },
        { "CID": {{OtherCid}}, "InventoryCleanupPlan": "aaaaaaaa-0000-0000-0000-000000000001" }
      ],
      "AdditionalData": {
        "#{{cidHex}} {{RetA}}": { "EntrustPlan": "11111111-0000-0000-0000-000000000001", "EntrustDuplicates": false },
        "#{{cidHex}} {{RetB}}": { "EntrustPlan": "00000000-0000-0000-0000-000000000000" },
        "#{{otherHex}} OtherRetainer": { "EntrustPlan": "11111111-0000-0000-0000-000000000002" }
      },
      "EntrustPlans": [
        { "Guid": "11111111-0000-0000-0000-000000000001", "Name": "Mats", "Duplicates": true, "DuplicatesMultiStack": false,
          "EntrustCategories": [ { "ID": 48, "AmountToKeep": 0 }, { "ID": 0, "AmountToKeep": 0 } ], "EntrustItems": [ 5001, 0 ],
          "EntrustItemsAmountToKeep": {}, "AllowEntrustFromArmory": false, "ManualPlan": true, "ExcludeProtected": false },
        { "Guid": "11111111-0000-0000-0000-000000000002", "Name": "Other", "Duplicates": false, "EntrustCategories": [], "EntrustItems": [ 1234 ] }
      ],
      "DefaultIMSettings": { "GUID": "d", "IMEnableAutoVendor": true, "IMAutoVendorHard": [ 11, 12 ], "IMAutoVendorSoft": [ 13 ],
        "IMDiscardList": [ 14 ], "IMDesynth": [ 15 ], "IMProtectList": [ 16 ], "IMAutoVendorHardStackLimit": 9999, "IMDiscardStackLimit": 9,
        "IMEnableItemDesynthesis": false },
      "AdditionalIMSettings": [
        { "GUID": "aaaaaaaa-0000-0000-0000-000000000001", "IMAutoVendorHard": [ 21 ], "IMProtectList": [ 26 ], "IMEnableAutoVendor": false,
          "AdditionModeProtectList": true, "AdditionModeHardSellList": false }
      ]
    }
    """;
    {
      var ar = ArConfigParser.Parse(arJson, Cid);
      Check("I1 ar: parses, available, RecordStats on", ar.Available && ar.RecordStats && ar.Status == "ok", ar.Status);
      Check("I1 ar: two plans, ids/categories read, zero ids dropped",
        ar.Plans.Count == 2 && ar.Plans[0].Items.SetEquals([5001u]) && ar.Plans[0].UiCategories.SetEquals([48u]) && ar.Plans[0].Duplicates && ar.Plans[0].ManualPlan);
      Check("I1 ar: plan assignment only for THIS character, empty guid ignored",
        ar.PlanByRetainer.Count == 1 && ar.PlanByRetainer.ContainsKey(RetA), string.Join(",", ar.PlanByRetainer.Keys));
      Check("I1 ar: PlanFor resolves the retainer's plan (name trimmed)", ar.PlanFor(" " + RetA + " ")?.Name == "Mats");
      Check("I1 ar: PlanFor unknown retainer is null", ar.PlanFor(RetB) == null);
      Check("I1 ar: default IM lists used when the character names no cleanup plan",
        ar.Im.VendorHard.SetEquals([11u, 12u]) && ar.Im.VendorSoft.SetEquals([13u]) && ar.Im.Discard.SetEquals([14u])
        && ar.Im.Desynth.SetEquals([15u]) && ar.Im.Protect.SetEquals([16u]) && ar.Im.AutoVendorEnabled && !ar.Im.DesynthEnabled
        && ar.Im.VendorHardStackLimit == 9999 && ar.Im.DiscardStackLimit == 9);
      Check("I1 ar: AnyPlanNames by item id and by ItemUICategory", ar.AnyPlanNames(5001, 0) && ar.AnyPlanNames(4242, 48) && ar.AnyPlanNames(1234, 0));
      Check("I1 ar: AnyPlanNames never matches category 0", !ar.AnyPlanNames(4242, 0));

      var other = ArConfigParser.Parse(arJson, OtherCid);
      Check("I1 ar: additional cleanup plan selected by the character's InventoryCleanupPlan",
        other.Im.VendorHard.SetEquals([21u]) && !other.Im.AutoVendorEnabled, string.Join(",", other.Im.VendorHard));
      Check("I1 ar: AdditionMode merges the default list only where its flag is set (protect yes, hard-sell no)",
        other.Im.Protect.SetEquals([26u, 16u]) && !other.Im.VendorHard.Contains(11));
      Check("I1 ar: other character's plan assignment", other.PlanByRetainer.ContainsKey("OtherRetainer") && !other.PlanByRetainer.ContainsKey(RetA));

      var noCid = ArConfigParser.Parse(arJson, 0);
      Check("I1 ar: unknown character -> plans parse, no assignments, default lists", noCid.Available && noCid.Plans.Count == 2 && noCid.PlanByRetainer.Count == 0 && noCid.Im.VendorHard.Contains(11));

      Check("I1 ar: missing file -> Unavailable, never throws", !ArConfigParser.Parse(null, Cid).Available && !ArConfigParser.Parse("  ", Cid).Available);
      var broken = ArConfigParser.Parse("{ \"EntrustPlans\": [ { \"Guid\": ", Cid);
      Check("I1 ar: truncated file (AutoRetainer mid-write) -> Unavailable with a reason", !broken.Available && broken.Status.Contains("could not be parsed"), broken.Status);
      Check("I1 ar: non-object JSON -> Unavailable", !ArConfigParser.Parse("[1,2]", Cid).Available);
      var bom = ArConfigParser.Parse("﻿{\"RecordStats\":true}", Cid);
      Check("I1 ar: leading BOM tolerated", bom.Available && bom.RecordStats, bom.Status);
      var odd = ArConfigParser.Parse("{\"EntrustPlans\":[{\"Guid\":\"g\",\"EntrustItems\":\"notalist\",\"EntrustCategories\":[{\"ID\":\"x\"}]}],\"DefaultIMSettings\":[]}", Cid);
      Check("I1 ar: wrong-typed keys read as empty, not a failure", odd.Available && odd.Plans.Count == 1 && odd.Plans[0].Items.Count == 0 && odd.Plans[0].UiCategories.Count == 0 && odd.Im.VendorHard.Count == 0);
    }

    // =====================================================================================
    // I2. AutoRetainer venture statistics
    // =====================================================================================
    {
      var json = "{\"Records\":[{\"I\":5000,\"T\":1790000000,\"V\":395},{\"I\":6000,\"H\":1,\"T\":1789990000,\"A\":2,\"V\":395},{\"I\":0,\"T\":5},{\"T\":7},{\"I\":7000,\"T\":1789000000}],\"PlayerName\":\"x\",\"RetainerName\":\"y\"}";
      var recs = ArVentureStats.Parse(json);
      Check("I2 stats: 3 valid records, malformed skipped", recs.Count == 3, recs.Count.ToString());
      Check("I2 stats: defaults H=NQ A=1; explicit H/A read", !recs[0].Hq && recs[0].Amount == 1 && recs[0].VentureId == 395 && recs[1].Hq && recs[1].Amount == 2);
      Check("I2 stats: absent V is venture 0", recs[2].VentureId == 0);
      Check("I2 stats: malformed file -> empty", ArVentureStats.Parse("{\"Records\":[{\"I\":1,").Count == 0 && ArVentureStats.Parse(null).Count == 0);
      Check("I2 stats: BOM tolerated", ArVentureStats.Parse("﻿" + json).Count == 3);
      Check("I2 stats: file name is <CID:X16>_<retainer>.statistic.json", ArVentureStats.FileName(Cid, RetA) == $"{cidHex}_{RetA}.statistic.json");
      var intake = ArVentureStats.Summarize([Qe(Mat, agoSec: 100), Qe(Mat, agoSec: 90_000), Qe(Mat, agoSec: 90_000, venture: 10), Qe(Mat, agoSec: 8 * 86_400)], NowSec);
      Check("I2 stats: intake last 24h / 7-day average / QE count", intake.Last24h == 1 && Math.Abs(intake.PerDay7d - 3 / 7.0) < 1e-9 && intake.QuickExploration7d == 2,
        $"{intake.Last24h} {intake.PerDay7d} {intake.QuickExploration7d}");
    }

    // =====================================================================================
    // I3. Who handles this stack
    // =====================================================================================
    {
      OwnershipInput In(uint id, bool hq = false, IEnumerable<AmEntry>? am = null, IEnumerable<CategoryRetainerRule>? routes = null,
        ArSnapshot? ar = null, Dictionary<string, IReadOnlySet<uint>>? holdings = null, IEnumerable<int>? gearsets = null)
        => new(id, hq, Facts(id), (am ?? []).ToList(), (routes ?? []).ToList(), ar ?? EmptyAr(), holdings, (gearsets ?? []).ToList());

      var none = StackOwnership.Resolve(In(Mat));
      Check("I3 owners: nothing -> 'Nothing handles this stack.'", none.Count == 0 && StackOwnership.Lines(none).SequenceEqual([StackOwnership.NothingText]));

      var am = StackOwnership.Resolve(In(Mat, am: [new AmEntry(Mat, false, true, false)]));
      Check("I3 owners: enabled Auto-Market entry acts", am.Count == 1 && am[0].Kind == OwnerKind.LmcAutoMarket && am[0].Acts && !StackOwnership.Lines(am).Contains(StackOwnership.NothingText));
      var amHq = StackOwnership.Resolve(In(Mat, hq: true, am: [new AmEntry(Mat, false, true, false)]));
      Check("I3 owners: NQ entry does not claim an HQ stack (quality is part of the identity)", amHq.Count == 0);
      var unticked = StackOwnership.Resolve(In(Mat, am: [new AmEntry(Mat, false, false, false)]));
      Check("I3 owners: unticked entry is shown but does not act", unticked.Count == 1 && !unticked[0].Acts && StackOwnership.Lines(unticked).Contains(StackOwnership.NothingText));

      var routed = StackOwnership.Resolve(In(Mat, am: [new AmEntry(Mat, false, true, false)], routes: [new CategoryRetainerRule { CategoryId = 47, RetainerName = RetB }]));
      Check("I3 owners: category route named for an enabled marketable entry", routed.Any(o => o.Kind == OwnerKind.LmcCategoryRoute && o.Text.Contains(RetB)));
      var routedExcl = StackOwnership.Resolve(In(Mat, am: [new AmEntry(Mat, false, true, true)], routes: [new CategoryRetainerRule { CategoryId = 47, RetainerName = RetB }]));
      Check("I3 owners: routing-excluded entry is not routed", !routedExcl.Any(o => o.Kind == OwnerKind.LmcCategoryRoute));
      var routedUnmarket = StackOwnership.Resolve(In(Unmarket, am: [new AmEntry(Unmarket, false, true, false)], routes: [new CategoryRetainerRule { CategoryId = 0, RetainerName = RetB }]));
      Check("I3 owners: unmarketable item is never routed", !routedUnmarket.Any(o => o.Kind == OwnerKind.LmcCategoryRoute));

      var plans = ArWith([Plan("p1", items: [Mat], manual: true), Plan("p2", cats: [UiCatGear]), Plan("p3", dup: true), Plan("p4", items: [Mat2], name: "Loose")],
        new Dictionary<string, string> { [RetA] = "p1", [RetB] = "p2", ["RetainerC"] = "p3" });
      var ent = StackOwnership.Resolve(In(Mat, ar: plans));
      Check("I3 owners: entrust item list names the retainer and the manual plan", ent.Count == 1 && ent[0].Kind == OwnerKind.ArEntrustItem && ent[0].Acts && ent[0].Text.Contains(RetA) && ent[0].Text.Contains("manual plan"));
      var cat = StackOwnership.Resolve(In(WhiteGear, ar: plans));
      Check("I3 owners: entrust by ItemUICategory", cat.Count == 1 && cat[0].Kind == OwnerKind.ArEntrustCategory && cat[0].Text.Contains(RetB));
      var dup = StackOwnership.Resolve(In(Unmarket, ar: plans, holdings: new() { ["RetainerC"] = new HashSet<uint> { Unmarket } }));
      Check("I3 owners: duplicates plan claims an item its retainer held when last seen", dup.Count == 1 && dup[0].Kind == OwnerKind.ArEntrustDuplicates && dup[0].Text.Contains("RetainerC"));
      var dupUnknown = StackOwnership.Resolve(In(Unmarket, ar: plans));
      Check("I3 owners: duplicates plan with no last-seen holdings claims nothing", dupUnknown.Count == 0);
      var dupUnique = StackOwnership.Resolve(In(UniqueItem, ar: plans, holdings: new() { ["RetainerC"] = new HashSet<uint> { UniqueItem } }));
      Check("I3 owners: single-stack duplicates skip unique items (AutoRetainer's own rule)", dupUnique.Count == 0);
      var loose = StackOwnership.Resolve(In(Mat2, ar: plans));
      Check("I3 owners: a plan assigned to no retainer is informational only", loose.Count == 1 && loose[0].Kind == OwnerKind.ArEntrustUnassignedPlan && !loose[0].Acts && loose[0].Text.Contains("Loose"));

      var protPlans = ArWith([Plan("p1", items: [Mat], exclProt: true)], new Dictionary<string, string> { [RetA] = "p1" }, Im(protect: [Mat]));
      var prot = StackOwnership.Resolve(In(Mat, ar: protPlans));
      Check("I3 owners: ExcludeProtected plan does not claim a protected item; protect line shown, not acting",
        prot.Count == 1 && prot[0].Kind == OwnerKind.ArProtect && !prot[0].Acts && StackOwnership.Lines(prot).Contains(StackOwnership.NothingText));

      var imAr = ArWith(im: Im(hard: [Mat], soft: [Mat2], discard: [Unmarket], desynth: [WhiteGear], autoVendor: false, desynthOn: false));
      var hard = StackOwnership.Resolve(In(Mat, ar: imAr));
      Check("I3 owners: vendor list with auto-vendor OFF says so and does not act", hard.Count == 1 && hard[0].Kind == OwnerKind.ArVendorHard && !hard[0].Acts && hard[0].Text.Contains("OFF"));
      var disc = StackOwnership.Resolve(In(Unmarket, ar: imAr));
      Check("I3 owners: discard list acts and is loud", disc.Count == 1 && disc[0].Kind == OwnerKind.ArDiscard && disc[0].Acts && disc[0].Text.Contains("DISCARDS") && disc[0].Text.Contains("under 9"));
      var des = StackOwnership.Resolve(In(WhiteGear, ar: imAr));
      Check("I3 owners: desynth list with desynthesis OFF does not act", des.Count == 1 && !des[0].Acts && des[0].Text.Contains("OFF"));
      var imOn = ArWith(im: Im(hard: [Mat], soft: [Mat2]));
      Check("I3 owners: vendor lists act when auto-vendor is on", StackOwnership.Resolve(In(Mat, ar: imOn))[0].Acts && StackOwnership.Resolve(In(Mat2, ar: imOn))[0].Kind == OwnerKind.ArVendorSoft);

      var gs = StackOwnership.Resolve(In(WhiteGear, gearsets: [7, 2]));
      Check("I3 owners: gearset line is informational, numbers sorted", gs.Count == 1 && gs[0].Kind == OwnerKind.Gearset && !gs[0].Acts && gs[0].Text == "In gearset #2, #7");

      var noAr = StackOwnership.Resolve(In(Mat, ar: ArWith([Plan("p1", items: [Mat])], new Dictionary<string, string> { [RetA] = "p1" }, Im(hard: [Mat]), available: false)));
      Check("I3 owners: unreadable AutoRetainer config contributes nothing", noAr.Count == 0);
    }

    // =====================================================================================
    // I4. Venture loot: the definition
    // =====================================================================================
    {
      var p = VentureLoot.Classify(Input([Page(0, Mat, 3)], [Qe(Mat, amount: 3)], CheapQuotes()), Opts());
      Check("I4 loot: QE-delivered cheap material is venture loot -> VENDOR", p.Rows.Count == 1 && One(p).IsLoot && One(p).Bucket == LootBucket.Vendor && One(p).Actionable, One(p).Reason);
      Check("I4 loot: vendor estimate = PriceLow x qty", One(p).EstGil == 9, One(p).EstGil.ToString());

      Check("I4 loot: another venture's reward is not venture loot (not shown)",
        VentureLoot.Classify(Input([Page(0, Mat)], [Qe(Mat, venture: 10)], CheapQuotes()), Opts()).Rows.Count == 0);
      Check("I4 loot: a record older than the look-back window does not count",
        VentureLoot.Classify(Input([Page(0, Mat)], [Qe(Mat, agoSec: 31 * 86_400L)], CheapQuotes()), Opts()).Rows.Count == 0);
      Check("I4 loot: look-back window is a setting",
        VentureLoot.Classify(Input([Page(0, Mat)], [Qe(Mat, agoSec: 31 * 86_400L)], CheapQuotes()), Opts(lookback: 60)).Rows.Count == 1);
      Check("I4 loot: HQ record does not make an NQ stack loot",
        VentureLoot.Classify(Input([Page(0, Mat)], [Qe(Mat, hq: true)], CheapQuotes()), Opts()).Rows.Count == 0);
      var mixed = VentureLoot.Classify(Input([Page(0, Mat, 50)], [Qe(Mat, amount: 2)], CheapQuotes()), Opts());
      Check("RAIL I4 loot: retainer holds MORE than ventures delivered -> not loot, KEEP, never vendored",
        mixed.Rows.Count == 1 && !One(mixed).IsLoot && One(mixed).Bucket == LootBucket.Keep && VentureLoot.VendorOps(mixed).Count == 0, One(mixed).Reason);
      var split = VentureLoot.Classify(Input([Page(0, Mat, 2), Page(1, Mat, 2)], [Qe(Mat, amount: 3)], CheapQuotes()), Opts());
      Check("RAIL I4 loot: quantity cap sums every stack of the item in the retainer", split.Rows.All(r => !r.IsLoot) && VentureLoot.VendorOps(split).Count == 0);
      Check("I4 loot: crystals page and market are never looked at",
        VentureLoot.Classify(Input([new RetainerStack(12001, 0, Mat, false, 1, false), new RetainerStack(12002, 0, Mat, false, 1, false)], [Qe(Mat)], CheapQuotes()), Opts()).Rows.Count == 0);
      var noLog = VentureLoot.Classify(Input([Page(0, Mat)], [], CheapQuotes()), Opts());
      Check("I4 loot: no venture log -> nothing is loot, and a note says why", noLog.Rows.Count == 0 && noLog.Notes.Any(n => n.Contains("venture log")));
      var statsOff = VentureLoot.Classify(Input([Page(0, Mat)], [Qe(Mat)], CheapQuotes(), ar: new ArSnapshot { Available = true, RecordStats = false }), Opts());
      Check("I4 loot: AutoRetainer statistics off -> note", statsOff.Notes.Any(n => n.Contains("Record Venture Statistics")));
    }

    // =====================================================================================
    // I5. Venture loot: the rails (adversarial)
    // =====================================================================================
    {
      LootRow Row(uint id, bool hq = false, IEnumerable<AmEntry>? am = null, ArSnapshot? ar = null, IEnumerable<uint>? gearset = null,
        IEnumerable<uint>? bags = null, LootOptions? opt = null, Dictionary<uint, ItemQuote>? quotes = null, bool collectable = false, int qty = 1)
        => One(VentureLoot.Classify(Input([Page(0, id, qty, hq, collectable: collectable)], [Qe(id, hq, (uint)qty)], quotes ?? CheapQuotes(), ar, am, gearset, bags), opt ?? Opts()));

      bool NeverVendor(LootRow r) => r.Bucket != LootBucket.Vendor && !r.Actionable;

      var amRow = Row(Mat, am: [new AmEntry(Mat, false, true, false)]);
      Check("RAIL I5: market stock (Auto-Market entry) in a retainer is NEVER bucketed for vendor", NeverVendor(amRow) && amRow.Bucket == LootBucket.Keep && amRow.Reason.Contains("Auto-Market"), amRow.Reason);
      Check("RAIL I5: an Auto-Market entry of the OTHER quality still keeps it", NeverVendor(Row(Mat, am: [new AmEntry(Mat, true, true, false)])));
      Check("RAIL I5: an unticked Auto-Market entry still keeps it", NeverVendor(Row(Mat, am: [new AmEntry(Mat, false, false, false)])));

      var entrustItem = ArWith([Plan("p1", items: [Mat])], new Dictionary<string, string> { [RetB] = "p1" });
      Check("RAIL I5: item on an AutoRetainer entrust plan is never moved or sold", NeverVendor(Row(Mat, ar: entrustItem)) && Row(Mat, ar: entrustItem).Reason.Contains("entrust"));
      var entrustCat = ArWith([Plan("p1", cats: [UiCatMetal])], new Dictionary<string, string> { [RetA] = "p1" });
      Check("RAIL I5: item whose ItemUICategory is on an entrust plan is kept", NeverVendor(Row(Mat, ar: entrustCat)));
      var unassigned = ArWith([Plan("p1", items: [Mat])]);
      Check("RAIL I5: an UNASSIGNED plan naming the item still keeps it", NeverVendor(Row(Mat, ar: unassigned)));
      var dupPlan = ArWith([Plan("p1", dup: true)], new Dictionary<string, string> { [RetA] = "p1" });
      Check("RAIL I5: duplicates plan on this retainer + a copy in the bags -> kept", NeverVendor(Row(Mat, ar: dupPlan, bags: [Mat])));
      Check("I5: duplicates plan but no copy in the bags -> not railed", Row(Mat, ar: dupPlan).Bucket == LootBucket.Vendor);
      Check("RAIL I5: AutoRetainer unreadable -> nothing is sorted", NeverVendor(Row(Mat, ar: ArWith(available: false))));
      Check("RAIL I5: AutoRetainer protect list -> kept", NeverVendor(Row(Mat, ar: ArWith(im: Im(protect: [Mat])))));

      var gsRow = Row(WhiteGear, gearset: [WhiteGear]);
      Check("RAIL I5: gearset item is never sorted", gsRow.Bucket == LootBucket.Keep && gsRow.Reason.Contains("gearset"));

      var uniq = Row(UniqueItem);
      Check("RAIL I5: unique item kept without opt-in, rail named", uniq.Bucket == LootBucket.Keep && uniq.OptInRail == "unique");
      Check("RAIL I5: untradable item kept without opt-in", Row(Untradable).OptInRail == "untradable");
      Check("RAIL I5: rare (blue) item kept without opt-in", Row(BlueItem).OptInRail == "rare");
      Check("I5: opt-in lifts ONLY that rail (unique marketable below gate -> vendor)", Row(UniqueItem, opt: Opts(optIns: [UniqueItem])).Bucket == LootBucket.Vendor);
      Check("RAIL I5: opt-in never lifts the Auto-Market rail",
        NeverVendor(Row(UniqueItem, am: [new AmEntry(UniqueItem, false, true, false)], opt: Opts(optIns: [UniqueItem]))));
      Check("RAIL I5: opt-in never lifts the entrust rail", NeverVendor(Row(UniqueItem, ar: ArWith([Plan("p", items: [UniqueItem])]), opt: Opts(optIns: [UniqueItem]))));
      Check("I5: opted-in untradable is not vendored (cannot be priced)", Row(Untradable, opt: Opts(optIns: [Untradable])).Bucket == LootBucket.Keep);

      var hq = Row(Mat, hq: true, quotes: new() { [Mat] = Quote(Mat, 5, hq: true) });
      Check("RAIL I5: HQ kept unless the HQ setting is on", hq.Bucket == LootBucket.Keep && hq.Reason.Contains("HQ"));
      Check("I5: HQ with the setting on -> vendor", Row(Mat, hq: true, quotes: new() { [Mat] = Quote(Mat, 5, hq: true) }, opt: Opts(allowHq: true)).Bucket == LootBucket.Vendor);
      Check("RAIL I5: collectable kept", NeverVendor(Row(Mat, collectable: true)));
      Check("RAIL I5: sheet miss kept", NeverVendor(Row(Missing)));

      // Uncertainty never vendors.
      Check("RAIL I5: prices never checked -> kept", NeverVendor(One(VentureLoot.Classify(Input([Page(0, Mat)], [Qe(Mat)], null), Opts()))));
      Check("RAIL I5: prices checked too long ago -> kept", NeverVendor(Row(Mat, opt: Opts(fetchedAgoMs: 31 * 60_000L))));
      Check("RAIL I5: no fetch timestamp -> kept", NeverVendor(Row(Mat, opt: Opts(fetchedAgoMs: -1))));
      Check("RAIL I5: no quote for the item -> kept", NeverVendor(Row(Mat, quotes: new() { [Mat2] = Quote(Mat2, 5) })));
      Check("RAIL I5: stale board data (older than the gate's freshness) -> kept", NeverVendor(Row(Mat, quotes: new() { [Mat] = Quote(Mat, 5, ageMs: 7 * 3_600_000L) })));
      Check("RAIL I5: no-data quote -> kept", NeverVendor(Row(Mat, quotes: new() { [Mat] = new ItemQuote(Mat, false, Now, []) })));
      Check("RAIL I5: value gate disabled -> kept", NeverVendor(Row(Mat, opt: Opts(gate: Gate(on: false)))));
      Check("RAIL I5: value gate threshold 0 -> kept", NeverVendor(Row(Mat, opt: Opts(gate: Gate(threshold: 0)))));
      var worth = Row(Mat, quotes: new() { [Mat] = Quote(Mat, 5000) });
      Check("RAIL I5: worth listing (over the threshold) -> kept, reason says so", worth.Bucket == LootBucket.Keep && worth.Reason.Contains("worth listing"), worth.Reason);
      Check("I5: the gate judges the retainer's whole holding (3 x 400 = 1140 net > 1000 -> keep)",
        Row(Mat, qty: 3, quotes: new() { [Mat] = Quote(Mat, 400) }).Bucket == LootBucket.Keep);
      Check("I5: at exactly the threshold it is below the gate (MarketGate's own polarity)",
        Row(Mat, qty: 1, quotes: new() { [Mat] = Quote(Mat, 1054) }).Bucket == LootBucket.Keep     // 1054 x 0.95 = 1001 net
        && Row(Mat, qty: 1, quotes: new() { [Mat] = Quote(Mat, 1053) }).Bucket == LootBucket.Vendor); // 1053 x 0.95 = 1000 net
    }

    // =====================================================================================
    // I6. Venture loot: buckets
    // =====================================================================================
    {
      LootRow Row(uint id, Dictionary<uint, ItemQuote>? quotes = null)
        => One(VentureLoot.Classify(Input([Page(0, id)], [Qe(id)], quotes ?? CheapQuotes()), Opts()));

      var green = Row(GreenGear);
      Check("I6 buckets: green gear below the gate -> GC DELIVERY (never vendored), preview only", green.Bucket == LootBucket.GcDelivery && !green.Actionable);
      Check("I6 buckets: green gear worth listing -> KEEP", Row(GreenGear, new() { [GreenGear] = Quote(GreenGear, 100_000) }).Bucket == LootBucket.Keep);
      var white = Row(WhiteGear);
      Check("I6 buckets: white gear below the gate with a vendor price -> VENDOR", white.Bucket == LootBucket.Vendor && white.Actionable);
      var des = Row(Unmarket);
      Check("I6 buckets: unmarketable desynthesizable -> DESYNTH, preview only", des.Bucket == LootBucket.Desynth && !des.Actionable);
      var keep = Row(UnmarketNoDesynth);
      Check("I6 buckets: unmarketable, not desynthesizable -> KEEP (never vendored unpriced)", keep.Bucket == LootBucket.Keep && keep.Reason.Contains("cannot be listed"));
      Check("I6 buckets: below the gate, no vendor price, desynthesizable -> DESYNTH", Row(NoPriceDesynth).Bucket == LootBucket.Desynth);
      var noPrice = Row(NoPrice);
      Check("I6 buckets: below the gate, no vendor price -> KEEP", noPrice.Bucket == LootBucket.Keep && noPrice.Reason.Contains("no vendor price"));

      var plan = VentureLoot.Classify(Input(
        [Page(0, Mat), Page(1, GreenGear), Page(2, Unmarket), Page(3, UnmarketNoDesynth, page: 6), Page(4, WhiteGear, page: 2)],
        [Qe(Mat), Qe(GreenGear), Qe(Unmarket), Qe(UnmarketNoDesynth), Qe(WhiteGear)], CheapQuotes()), Opts());
      Check("I6 buckets: counts per bucket", plan.Count(LootBucket.Vendor) == 2 && plan.Count(LootBucket.GcDelivery) == 1 && plan.Count(LootBucket.Desynth) == 1 && plan.Count(LootBucket.Keep) == 1,
        $"v={plan.Count(LootBucket.Vendor)} gc={plan.Count(LootBucket.GcDelivery)} d={plan.Count(LootBucket.Desynth)} k={plan.Count(LootBucket.Keep)}");
      var ops = VentureLoot.VendorOps(plan);
      Check("I6 vendor ops: only the actionable VENDOR rows", ops.Count == 2 && ops.Select(o => o.ItemId).OrderBy(x => x).SequenceEqual([Mat, WhiteGear]));
      Check("I6 vendor ops: real retainer-page containers only (RetainerPage1 / RetainerPage3)",
        ops.All(o => o.HasKnownContainer && o.Container is >= 10000 and <= 10006) && ops.Any(o => o.ContainerName() == "RetainerPage3"));
      var forged = new LootPlan([new LootRow(new RetainerStack(0, 1, Mat, false, 1, false), LootBucket.Vendor, "x", true, 1, null, 1, true)], []);
      Check("RAIL I6 vendor ops: a bag-container row can never become a vendor op", VentureLoot.VendorOps(forged).Count == 0);
      var notLoot = new LootPlan([new LootRow(Page(0, Mat), LootBucket.Vendor, "x", true, 1, null, 1, false)], []);
      Check("RAIL I6 vendor ops: a not-loot row can never become a vendor op", VentureLoot.VendorOps(notLoot).Count == 0);
      var stale = VentureLoot.Classify(Input([Page(0, Mat)], [Qe(Mat)], CheapQuotes()), Opts(fetchedAgoMs: 31 * 60_000L));
      Check("RAIL I6 vendor ops: stale price check -> zero ops", VentureLoot.VendorOps(stale).Count == 0);
    }

    // =====================================================================================
    // I7. Gear mover
    // =====================================================================================
    {
      sbyte[] Cols(params (int Index, sbyte Value)[] set)
      {
        var c = new sbyte[14];
        foreach (var (i, v) in set) c[i] = v;
        return c;
      }
      Check("I7 slot: two-handed weapon (main 1, off -1) -> MainHand", ArmourySlots.FromEquipSlotCategory(Cols((0, 1), (1, -1))) == ArmourySlot.MainHand);
      Check("I7 slot: shield -> OffHand", ArmourySlots.FromEquipSlotCategory(Cols((1, 1))) == ArmourySlot.OffHand);
      Check("I7 slot: robe (body 1, legs -1) -> Body", ArmourySlots.FromEquipSlotCategory(Cols((3, 1), (6, -1))) == ArmourySlot.Body);
      Check("I7 slot: gloves -> Hands, feet -> Feet", ArmourySlots.FromEquipSlotCategory(Cols((4, 1))) == ArmourySlot.Hands && ArmourySlots.FromEquipSlotCategory(Cols((7, 1))) == ArmourySlot.Feet);
      Check("I7 slot: ring (both fingers) -> Rings", ArmourySlots.FromEquipSlotCategory(Cols((11, 1), (12, 1))) == ArmourySlot.Rings);
      Check("I7 slot: soul crystal -> SoulCrystal", ArmourySlots.FromEquipSlotCategory(Cols((13, 1))) == ArmourySlot.SoulCrystal);
      Check("I7 slot: waist-only / nothing / short row -> None",
        ArmourySlots.FromEquipSlotCategory(Cols((5, 1))) == ArmourySlot.None && ArmourySlots.FromEquipSlotCategory(Cols()) == ArmourySlot.None
        && ArmourySlots.FromEquipSlotCategory(new sbyte[3]) == ArmourySlot.None);

      var noOwners = new Func<uint, bool, IReadOnlyList<Owner>>((_, _) => []);
      var free = new Dictionary<ArmourySlot, int> { [ArmourySlot.Head] = 2, [ArmourySlot.Body] = 5 };
      BagStack B(int c, int s, uint id, bool hq = false) => new(c, s, id, hq, 1);

      var p = GearMover.Plan([B(0, 0, WhiteGear), B(0, 1, Mat), B(1, 0, GreenGear)], Facts, free, noOwners, new HashSet<uint>(), 0);
      Check("I7 plan: gear moves to its page, non-gear is not listed at all", p.Ops.Count == 2 && p.Skipped.Count == 0
        && p.Ops.Any(o => o.ItemId == WhiteGear && o.Target == ArmourySlot.Head) && p.Ops.Any(o => o.ItemId == GreenGear && o.Target == ArmourySlot.Body));

      var gs = GearMover.Plan([B(0, 0, WhiteGear)], Facts, free, noOwners, new HashSet<uint> { WhiteGear }, 0);
      Check("RAIL I7: gearset item is never moved", gs.Ops.Count == 0 && gs.Skipped.Single().Reason.Contains("gearset"));

      IReadOnlyList<Owner> ActsOn(uint id, bool hq) => id == WhiteGear ? [new Owner(OwnerKind.ArVendorHard, "AutoRetainer sells it", true)] : [];
      var owned = GearMover.Plan([B(0, 0, WhiteGear), B(0, 1, GreenGear)], Facts, free, ActsOn, new HashSet<uint>(), 0);
      Check("RAIL I7: a stack another mover/seller acts on is never moved (reason names it)", owned.Ops.Count == 1 && owned.Ops[0].ItemId == GreenGear
        && owned.Skipped.Single().Reason.Contains("AutoRetainer sells it"));
      IReadOnlyList<Owner> Info(uint id, bool hq) => [new Owner(OwnerKind.ArProtect, "protect", false)];
      Check("I7: an informational owner (protect list) does not block", GearMover.Plan([B(0, 0, WhiteGear)], Facts, free, Info, new HashSet<uint>(), 0).Ops.Count == 1);

      var full = GearMover.Plan([B(0, 0, WhiteGear), B(0, 1, WhiteGear), B(0, 2, WhiteGear)], Facts, free, noOwners, new HashSet<uint>(), 0);
      Check("I7 plan: armoury budget counts planned moves (2 free, 3 heads -> 2 moves, 1 'full')", full.Ops.Count == 2 && full.Skipped.Single().Reason.Contains("full"));
      Check("I7 plan: a page with no free slot moves nothing", GearMover.Plan([B(0, 0, WhiteGear)], Facts, new Dictionary<ArmourySlot, int>(), noOwners, new HashSet<uint>(), 0).Ops.Count == 0);
      var cap = GearMover.Plan([B(0, 0, GreenGear), B(0, 1, GreenGear), B(0, 2, GreenGear)], Facts, free, noOwners, new HashSet<uint>(), 2);
      Check("I7 plan: per-pass cap", cap.Ops.Count == 2 && cap.Skipped.Single().Reason.Contains("cap"));
      Check("RAIL I7: only Inventory1-4 are sources (crystals / armoury / retainer never)",
        GearMover.Plan([B(2001, 0, WhiteGear), B(3201, 0, WhiteGear), B(10000, 0, WhiteGear)], Facts, free, noOwners, new HashSet<uint>(), 0).Ops.Count == 0);
      Check("I7 plan: sheet miss is never moved", GearMover.Plan([B(0, 0, Missing)], Facts, free, noOwners, new HashSet<uint>(), 0).Ops.Count == 0);

      // Undo
      var rec1 = new GearMoveRecord(WhiteGear, false, 0, 5, ArmourySlot.Head, 3201, 3);
      var rec2 = new GearMoveRecord(GreenGear, true, 1, 7, ArmourySlot.Body, 3202, 0);
      (uint, bool)? Armoury(int c, int s) => (c, s) switch { (3201, 3) => (WhiteGear, false), (3202, 0) => (GreenGear, true), _ => null };
      var u = GearMover.PlanUndo([rec1, rec2], Armoury, [(0, 9), (0, 5), (2, 0)]);
      Check("I7 undo: back to the original bag slot when it is still empty", u.Ops.Count == 2 && u.Ops[0].DstContainer == 0 && u.Ops[0].DstSlot == 5);
      Check("I7 undo: otherwise the first empty bag slot not already claimed", u.Ops[1].DstContainer == 0 && u.Ops[1].DstSlot == 9);
      (uint, bool)? Changed(int c, int s) => (c, s) == (3201, 3) ? (Mat, false) : null;
      var u2 = GearMover.PlanUndo([rec1, rec2], Changed, [(0, 5)]);
      Check("RAIL I7 undo: an armoury slot that no longer holds the item is left alone", u2.Ops.Count == 0 && u2.Notes.Count == 2);
      var u3 = GearMover.PlanUndo([rec1, rec2], Armoury, [(3, 0)]);
      Check("I7 undo: bags fill up -> the rest stays in the armoury, with a note", u3.Ops.Count == 1 && u3.Notes.Single().Contains("no empty bag slot"));
      Check("I7 undo: HQ flag must match", GearMover.PlanUndo([rec2 with { Hq = false }], Armoury, [(0, 0)]).Ops.Count == 0);
    }

    // =====================================================================================
    // I8. Idle gate + debounce
    // =====================================================================================
    {
      var clear = new IdleFacts(true, false, false, false, false, false, false, false, PluginBusy.Idle, PluginBusy.NotInstalled, PluginBusy.Idle, PluginBusy.NotInstalled);
      Check("I8 idle: all clear -> idle", IdleGate.Decide(clear).Idle && IdleGate.Decide(clear).Why.Count == 0);
      var cases = new (string Name, IdleFacts F)[]
      {
        ("not logged in", clear with { LoggedIn = false }),
        ("combat", clear with { InCombat = true }),
        ("duty", clear with { InDuty = true }),
        ("crafting", clear with { Crafting = true }),
        ("gathering", clear with { Gathering = true }),
        ("cutscene", clear with { Cutscene = true }),
        ("occupied", clear with { Occupied = true }),
        ("LMC busy", clear with { LmcBusy = true }),
        ("AutoRetainer busy", clear with { AutoRetainer = PluginBusy.Busy }),
        ("AutoDuty busy", clear with { AutoDuty = PluginBusy.Busy }),
        ("Artisan busy", clear with { Artisan = PluginBusy.Busy }),
        ("GatherBuddy busy", clear with { GatherBuddy = PluginBusy.Busy }),
        ("AutoRetainer unreadable", clear with { AutoRetainer = PluginBusy.Unknown }),
        ("AutoDuty unreadable", clear with { AutoDuty = PluginBusy.Unknown }),
      };
      foreach (var (name, f) in cases)
      {
        var v = IdleGate.Decide(f);
        Check($"RAIL I8 idle: {name} blocks, with a reason", !v.Idle && v.Why.Count == 1, string.Join("; ", v.Why));
      }
      Check("I8 idle: not-installed plugins never block", IdleGate.Decide(clear with { AutoRetainer = PluginBusy.NotInstalled, Artisan = PluginBusy.NotInstalled }).Idle);

      var d = new IdleDebounce(1000, 5000);
      var seq = new[] { d.Update(true, 0), d.Update(true, 500), d.Update(true, 1000), d.Update(true, 1500) };
      Check("I8 debounce: acts only after the hold, then cools down", !seq[0] && !seq[1] && seq[2] && !seq[3]);
      var reset = d.Update(false, 2000) || d.Update(true, 6100) || d.Update(true, 6500);
      Check("I8 debounce: a busy reading restarts the hold", !reset && d.Update(true, 7100));
      Check("I8 debounce: cooldown holds even after a long idle", !new Func<bool>(() => { var x = new IdleDebounce(0, 5000); x.Update(true, 0); return x.Update(true, 4999); })());
    }

    // =====================================================================================
    // I9. Space math
    // =====================================================================================
    {
      Check("I9 space: free = size - used, floored at 0", SpaceMath.Free(35, 30) == 5 && SpaceMath.Free(35, 40) == 0 && SpaceMath.Free(35, -2) == 35);
      Check("I9 space: low at or under the threshold; 0 = only when full; negative never", SpaceMath.Low(10, 10) && !SpaceMath.Low(11, 10) && SpaceMath.Low(0, 0) && !SpaceMath.Low(1, 0) && !SpaceMath.Low(0, -1));
      Check("I9 space: days until full", SpaceMath.DaysUntilFull(28, 28) == 1.0 && SpaceMath.DaysUntilFull(10, 0) == null && SpaceMath.DaysUntilFull(-3, 2) == 0);
      Check("I9 space: age text", SpaceMath.Age(0, Now) == "never" && SpaceMath.Age(Now - 30_000, Now) == "just now" && SpaceMath.Age(Now - 600_000, Now) == "10 min ago"
        && SpaceMath.Age(Now - 3 * 3_600_000L, Now) == "3 h ago" && SpaceMath.Age(Now - 3 * 86_400_000L, Now) == "3 d ago");
      Check("I9 space: retainer capacity is 7 pages x 25", SpaceMath.RetainerCapacity == 175);
    }

    // =====================================================================================
    // I10. IV| action lines (the audit trail's grammar)
    // =====================================================================================
    {
      var v = InventoryActionLine.Vendor(1_790_000_000_123, "0.1.65.0", RetA, "RetainerPage2:14", Mat, false, 3, 9, true, "below gate");
      Check("I10 line: vendor line, fixed key order", v == $"IV|1790000000123|vendor|v=0.1.65.0|r={RetA}|src=RetainerPage2:14|i=5000|hq=0|q=3|est=9|ok=1|why=below gate", v);
      var esc = InventoryActionLine.Vendor(1, "v", "a|b", "s", 1, true, 1, 0, false, "x\ny");
      Check("I10 line: values are escaped (pipe, newline) so the line stays parseable", esc.Contains("r=a\\pb") && esc.Contains("why=x\\ny") && !esc.Contains('\n') && esc.Split('|').Length == 12, esc);
      var m = InventoryActionLine.Move(5, "move", "v", "Inventory1:3", "ArmoryHead:0", WhiteGear, true, true, 0, "idle");
      Check("I10 line: move line", m == "IV|5|move|v=v|src=Inventory1:3|dst=ArmoryHead:0|i=6001|hq=1|ok=1|rc=0|why=idle", m);
      Check("I10 line: undo kind", InventoryActionLine.Move(5, "undo", "v", "a", "b", 1, false, false, -2, "").StartsWith("IV|5|undo|"));
      Check("I10 line: anything but 'undo' is a move", InventoryActionLine.Move(5, "weird", "v", "a", "b", 1, false, false, -2, "").StartsWith("IV|5|move|"));
      var b = InventoryActionLine.Batch(7, "v", "vendor", "end", 4, 3, 1, "slot changed");
      Check("I10 line: batch line", b == "IV|7|batch|v=v|what=vendor|ev=end|n=4|okn=3|fail=1|why=slot changed", b);
      Check("I10 line: opt-in line", InventoryActionLine.OptIn(9, "v", UniqueItem, true, "unique") == "IV|9|optin|v=v|i=8000|on=1|rail=unique");
      Check("I10 line: container names",
        InventoryActionLine.Slot(10001, 14) == "RetainerPage2:14" && InventoryActionLine.ContainerName(0) == "Inventory1" && InventoryActionLine.ContainerName(3202) == "ArmoryBody"
        && InventoryActionLine.ContainerName(3500) == "ArmoryMainHand" && InventoryActionLine.ContainerName(3300) == "ArmoryRings" && InventoryActionLine.ContainerName(4100) == "PremiumSaddleBag1"
        && InventoryActionLine.ContainerName(77) == "Unknown(77)");
      var parsed = Lalalazy.Telemetry.ParsedTelemetryLine.TryParse(v, out var pl);
      Check("I10 line: parses with the shared telemetry parser", parsed && pl.Kind == "vendor" && pl.Get("src") == "RetainerPage2:14" && pl.Get("ok") == "1");
    }

    // =====================================================================================
    // I11. Sidecar state
    // =====================================================================================
    {
      var s = new InventoryState();
      var c = s.For(Cid);
      c.Retainers[RetA] = new RetainerSeen { Name = RetA, PagesSeenUnixMs = Now, Used = 40, Capacity = 175, Stacks = [new SeenStack { C = 10000, S = 1, I = Mat, Q = 3 }] };
      c.Retainers[RetB] = new RetainerSeen { Name = RetB, BellSeenUnixMs = Now, BellItemCount = 170 };
      c.LastGearBatch = [new GearMoveRecord(WhiteGear, false, 0, 5, ArmourySlot.Head, 3201, 3)];
      var round = InventoryState.Load(s.Save());
      var rc = round.For(Cid);
      Check("I11 state: round trip keeps retainers, stacks and the gear batch",
        rc.Retainers.Count == 2 && rc.Retainers[RetA].Stacks.Single().I == Mat && rc.LastGearBatch.Single().Target == ArmourySlot.Head && rc.LastGearBatch.Single().ArmouryIndex == 3);
      Check("I11 state: corrupt / empty file -> empty state, never a throw", InventoryState.Load("{\"Characters\":").Characters.Count == 0 && InventoryState.Load(null).Characters.Count == 0);
      Check("I11 state: characters are separate", round.For(OtherCid).Retainers.Count == 0);
      var bestA = InventoryState.BestSpace(rc.Retainers[RetA]);
      var bestB = InventoryState.BestSpace(rc.Retainers[RetB]);
      Check("I11 state: best space - page snapshot when newest, bell count otherwise",
        bestA is { Used: 40, Source: "pages" } && bestB is { Used: 170, Capacity: 175, Source: "bell" });
      var both = new RetainerSeen { PagesSeenUnixMs = Now - 1000, Used = 10, BellSeenUnixMs = Now, BellItemCount = 12 };
      Check("I11 state: a newer bell count beats an older page snapshot", InventoryState.BestSpace(both) is { Used: 12, Source: "bell" });
      Check("I11 state: never seen -> null", InventoryState.BestSpace(new RetainerSeen()) == null);
      var holdings = InventoryState.Holdings(rc);
      Check("I11 state: holdings only from page snapshots", holdings.Count == 1 && holdings[RetA].Contains(Mat));
      Check("I11 state: stacks convert back to RetainerStack", InventoryState.StacksOf(rc.Retainers[RetA]).Single() == new RetainerStack(10000, 1, Mat, false, 3, false));
    }
  }
}
