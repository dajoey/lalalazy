using LazyFashionReport.Core;

// Offline harness: replays week 449 (verified live 2026-09-06) through the pure Core scorer.
// Fixtures from fashionreportxiv report-state + the published easy100/easy80 sets:
//   theme "Hunter from the Far East", week 449;
//   hints: body "Hingan Nights", hands "Hand in Long Glove", feet "Monster Hunt", neck "Pirates in the Sky";
//   plus2 dyes: weapon Mesa Red, head Abyssal Blue, body Metallic Silver, hands Jet Black,
//               legs Violet Purple, feet Jet Black; plus1 shades red/blue/white/black/purple/black;
//   easy100 = Weathered Kasuga Haori (body), Augmented Hailstorm Gloves of Casting (hands),
//             Augmented Rathalos Greaves (feet), Redbill Scarf (neck)  -> 100
//   easy80  = Brand-new Gloves (hands) + Abyssal Blue dye on head     -> 80
// The harness exits non-zero if ANY check fails.

var failures = new List<string>();
var passes = 0;

void Check(string name, bool ok, string detail = "")
{
    if (ok) { passes++; Console.WriteLine($"PASS {name}"); }
    else { failures.Add(name); Console.WriteLine($"FAIL {name} {detail}"); }
}

// ---- Stain fixtures (from the live Stain sheet, read via the sheet probe 2026-09-06) ----
var stainFamilies = new Dictionary<uint, string>
{
    [17] = ShadeMap.Red,     // Mesa Red
    [76] = ShadeMap.Blue,    // Abyssal Blue
    [112] = ShadeMap.White,  // Metallic Silver
    [102] = ShadeMap.Black,  // Jet Black
    [121] = ShadeMap.Purple, // Violet Purple
    [1] = ShadeMap.White,    // Snow White
    [2] = ShadeMap.Grey,     // Ash Grey
    [68] = ShadeMap.Blue,    // Ink Blue
};
var nameToStain = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
{
    ["Mesa Red"] = 17, ["Abyssal Blue"] = 76, ["Metallic Silver"] = 112,
    ["Jet Black"] = 102, ["Violet Purple"] = 121, ["Snow White"] = 1,
};

// ---- Week 449 model ----
var hints = new string?[11];
hints[(int)FashionSlot.Body] = "Hingan Nights";
hints[(int)FashionSlot.Hands] = "Hand in Long Glove";
hints[(int)FashionSlot.Feet] = "Monster Hunt";
hints[(int)FashionSlot.Neck] = "Pirates in the Sky";

var plus2 = new Dictionary<FashionSlot, string>
{
    [FashionSlot.Weapon] = "Mesa Red",
    [FashionSlot.Head] = "Abyssal Blue",
    [FashionSlot.Body] = "Metallic Silver",
    [FashionSlot.Hands] = "Jet Black",
    [FashionSlot.Legs] = "Violet Purple",
    [FashionSlot.Feet] = "Jet Black",
};
var plus1 = new Dictionary<FashionSlot, string>
{
    [FashionSlot.Weapon] = "red",
    [FashionSlot.Head] = "blue",
    [FashionSlot.Body] = "white",
    [FashionSlot.Hands] = "black",
    [FashionSlot.Legs] = "purple",
    [FashionSlot.Feet] = "black",
};

var week = new FashionWeek
{
    Week = 449,
    Theme = "Hunter from the Far East",
    Hints = hints,
    PlusTwoDyes = plus2,
    PlusOneShades = plus1,
};

// Fixture item ids (resolved from the live Item sheet via the sheet probe, 2026-09-06):
const uint KasugaHaori = 25302;        // Weathered Kasuga Haori (body gold)
const uint HailstormGloves = 14422;    // Augmented Hailstorm Gloves of Casting (hands gold)
const uint RedbillScarf = 17679;       // Redbill Scarf (neck gold)
const uint BrandNewGloves = 14036;     // Brand-new Gloves (easy80 hands)
const uint RathalosGreaves = 25303;    // Augmented Rathalos Greaves [F] (feet gold; gender-suffixed name upstream)

var crowd = new FakeCrowd(new Dictionary<FashionSlot, List<(uint id, int votes)>>
{
    [FashionSlot.Body] = new() { (KasugaHaori, 100) },
    [FashionSlot.Hands] = new() { (HailstormGloves, 90), (BrandNewGloves, 85) },
    [FashionSlot.Feet] = new() { (RathalosGreaves, 95) },
    [FashionSlot.Neck] = new() { (RedbillScarf, 88) },
}, nameToStain, plus2);

// ---- 1. Weekly base ----
Check("base-70", week.BaseScore == 70, $"got {week.BaseScore}");

var allMainHints = new string?[11];
allMainHints[(int)FashionSlot.Head] = "A";
allMainHints[(int)FashionSlot.Body] = "B";
allMainHints[(int)FashionSlot.Hands] = "C";
allMainHints[(int)FashionSlot.Legs] = "D";
var allMainWeek = new FashionWeek { Week = 448, Theme = "t", Hints = allMainHints };
Check("base-68-all-main-hints", allMainWeek.BaseScore == 68, $"got {allMainWeek.BaseScore}");

var noHintsWeek = new FashionWeek { Week = 1, Theme = "t", Hints = new string?[11] };
Check("base-100-no-hints", noHintsWeek.BaseScore == 100, $"got {noHintsWeek.BaseScore}");

// ---- 2. easy100: the four gold items, no dyes -> 100 ----
var easy100 = AllFilled();
easy100[(int)FashionSlot.Body] = new EquippedItem { Slot = FashionSlot.Body, ItemId = KasugaHaori, Name = "Weathered Kasuga Haori" };
easy100[(int)FashionSlot.Hands] = new EquippedItem { Slot = FashionSlot.Hands, ItemId = HailstormGloves, Name = "Augmented Hailstorm Gloves of Casting" };
easy100[(int)FashionSlot.Feet] = new EquippedItem { Slot = FashionSlot.Feet, ItemId = RathalosGreaves, Name = "Augmented Rathalos Greaves" };
easy100[(int)FashionSlot.Neck] = new EquippedItem { Slot = FashionSlot.Neck, ItemId = RedbillScarf, Name = "Redbill Scarf" };
var rep100 = Predictor.Build(week, easy100, stainFamilies, crowd, null);
Check("easy100-total-100", rep100.Total == 100, $"got {rep100.Total}");
Check("easy100-status", rep100.StatusLine.Contains("perfect"), rep100.StatusLine);

// ---- 3. easy80: Brand-new Gloves + Abyssal Blue on head -> 80 ----
var easy80 = AllFilled();
easy80[(int)FashionSlot.Hands] = new EquippedItem { Slot = FashionSlot.Hands, ItemId = BrandNewGloves, Name = "Brand-new Gloves" };
easy80[(int)FashionSlot.Head] = new EquippedItem { Slot = FashionSlot.Head, ItemId = 99001, Name = "Any hat", Stain0Id = 76 };
var rep80 = Predictor.Build(week, easy80, stainFamilies, crowd, null);
Check("easy80-total-80", rep80.Total == 80, $"got {rep80.Total}");
Check("easy80-status-full-mgp", rep80.StatusLine.Contains("full 50k"), rep80.StatusLine);

// ---- 4. Base-only outfit (all filled, no golds, no dyes) -> 70 ----
var plain = AllFilled();
var repPlain = Predictor.Build(week, plain, stainFamilies, crowd, null);
Check("plain-outfit-70", repPlain.Total == 70, $"got {repPlain.Total}");

// ---- 5. Dye math: Abyssal Blue exact on head = 10 + 2 = 12 ----
Check("dye-exact-2", rep80.Slots[(int)FashionSlot.Head].Score == 12, $"head score {rep80.Slots[(int)FashionSlot.Head].Score}");

// ---- 6. Same-shade: Ink Blue (68) shares Abyssal Blue's blue family -> 10 + 1 = 11 ----
var shadeTest = AllFilled();
shadeTest[(int)FashionSlot.Head] = new EquippedItem { Slot = FashionSlot.Head, ItemId = 99002, Name = "hat", Stain0Id = 68 };
var repShade = Predictor.Build(week, shadeTest, stainFamilies, crowd, null);
Check("dye-same-shade-1", repShade.Slots[(int)FashionSlot.Head].Score == 11, $"head score {repShade.Slots[(int)FashionSlot.Head].Score}");

// ---- 7. No dye, no bonus ----
Check("no-dye-0", repPlain.Slots[(int)FashionSlot.Head].Score == 10, $"head {repPlain.Slots[(int)FashionSlot.Head].Score}");

// ---- 8. Accessories: 8 unhinted; hinted gold neck = 2 + 6 = 8 ----
Check("accessory-unhinted-8", repPlain.Slots[(int)FashionSlot.Ears].Score == 8, $"ears {repPlain.Slots[(int)FashionSlot.Ears].Score}");
Check("neck-hinted-gold-8", rep100.Slots[(int)FashionSlot.Neck].Score == 8, $"neck {rep100.Slots[(int)FashionSlot.Neck].Score}");

// ---- 9. Crowd candidates: ranked by votes, owned filter works ----
var ownedOnly = new HashSet<uint> { BrandNewGloves };
var repOwned = Predictor.Build(week, easy100, stainFamilies, crowd, ownedOnly);
var handsCandidates = repOwned.Slots[(int)FashionSlot.Hands].Candidates;
Check("owned-filter", handsCandidates.Count == 1 && handsCandidates[0].ItemId == BrandNewGloves,
    $"got {handsCandidates.Count} candidates, first {handsCandidates.FirstOrDefault().ItemId}");

// ---- 10. Empty slots score 0 and AchievableIfFilled projects the fill ----
var sparse = new EquippedItem?[11];
sparse[(int)FashionSlot.Body] = new EquippedItem { Slot = FashionSlot.Body, ItemId = KasugaHaori, Name = "x" };
var repSparse = Predictor.Build(week, sparse, stainFamilies, crowd, null);
Check("empty-slot-0", repSparse.Slots[(int)FashionSlot.Head].Score == 0, "head should be 0");
// Projection: weapon 10 + head 10 + body gold 10 + hands 2 + legs 10 + feet 2 + ears 8 + neck 2 + wrist 8 + rings 8+8 = 78
Check("achievable-projection-78", repSparse.AchievableIfFilled == 78, $"got {repSparse.AchievableIfFilled}");

// ---- 11. REAL payload binding: week-449 report-state bytes through the actual parser ----
// Regression for the v0.1.0.0 field bug: the frxiv payload is camelCase, carries "week" as a
// STRING and parks a numeric "_updatedAt" inside dyeData; the old case-sensitive default
// binding turned all of that into an all-null ReportState — fetch succeeded, UI showed
// "no hint" on every slot. Fixture = the exact bytes cached on omasky 2026-09-06 21:42.
// (Hint/slot-key mapping here mirrors FashionService.ParseSlot / CrowdDataAdapter.SlotKey —
// the harness cannot reference the game-coupled plugin assembly, so the contract is doubled.)
var fixture = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "report-state-week449.json"));
var rs = RemoteDataSource.ParseReportState(fixture);
Check("rs-parses", rs is not null);
Check("rs-theme", rs?.LastOptions?.ReportTitle == "Hunter from the Far East", rs?.LastOptions?.ReportTitle ?? "<null>");
Check("rs-week-int", rs?.LastOptions?.Week == 449, $"{rs?.LastOptions?.Week}");
var rsHints = rs?.LastOptions?.Hints ?? new List<RemoteDataSource.HintEntry>();
Check("rs-four-hints", rsHints.Count == 4, $"{rsHints.Count}");
var rsDyes = rs?.DyeData ?? new Dictionary<string, RemoteDataSource.DyeEntry>();
Check("rs-six-dye-slots", rsDyes.Count == 6, $"{rsDyes.Count}");
Check("rs-updatedat-skipped", !rsDyes.ContainsKey("_updatedAt"));
Check("rs-body-plus2", rsDyes.GetValueOrDefault("body")?.Plus2 == "Metallic Silver", rsDyes.GetValueOrDefault("body")?.Plus2 ?? "<null>");
Check("rs-feet-plus1", rsDyes.GetValueOrDefault("feet")?.Plus1 == "black", rsDyes.GetValueOrDefault("feet")?.Plus1 ?? "<null>");

var week449 = new FashionWeek
{
    Week = rs?.LastOptions?.Week ?? 0,
    Theme = rs?.LastOptions?.ReportTitle ?? "",
    Hints = BuildHints(rs),
    PlusTwoDyes = BuildDyes(rs, plus2: true),
    PlusOneShades = BuildDyes(rs, plus2: false),
};
Check("rs-week-hints-landed", week449.IsHinted(FashionSlot.Body) && week449.IsHinted(FashionSlot.Hands)
    && week449.IsHinted(FashionSlot.Feet) && week449.IsHinted(FashionSlot.Neck));
Check("rs-week-base-70", week449.BaseScore == 70, $"got {week449.BaseScore}");
var week449Rep = Predictor.Build(week449, easy100, stainFamilies, crowd, null);
Check("rs-predictor-easy100-100", week449Rep.Total == 100, $"got {week449Rep.Total}");

// ---- 12. FetchPlan: the missing-pieces planner (v0.1.2.0 fetch-missing step 1) ----
// Owned = just Brand-new Gloves: for hands the crowd has Hailstorm (90) and Brand-new (85),
// so exactly Hailstorm is "missing" there; the other hinted slots keep their full lists.
var planOwned = new HashSet<uint> { BrandNewGloves };
var plan = FetchPlan.Build(week449, crowd, planOwned,
    id => id is HailstormGloves ? new RecipeOption(7777, 9, 90) : null);
Check("fp-hands-missing-hailstorm", plan.Any(p => p.Slot == FashionSlot.Hands && p.Item.ItemId == HailstormGloves),
    $"hands plan: {string.Join(",", plan.Where(p => p.Slot == FashionSlot.Hands).Select(p => p.Item.ItemId))}");
Check("fp-skips-owned-brandnew", !plan.Any(p => p.Item.ItemId == BrandNewGloves), "owned item must never be planned");
Check("fp-recipe-resolved", plan.First(p => p.Item.ItemId == HailstormGloves).Recipe?.RecipeId == 7777,
    $"recipe: {plan.First(p => p.Item.ItemId == HailstormGloves).Recipe?.RecipeId.ToString() ?? "<null>"}");
Check("fp-no-recipe-still-planned", plan.First(p => p.Slot == FashionSlot.Body).Recipe is null,
    "a non-craftable item stays in the plan with Recipe=null (UI hides its button)");
// No owned snapshot -> no plan at all (missing cannot be judged without ownership).
Check("fp-null-owned-empty", FetchPlan.Build(week449, crowd, null, _ => null).Count == 0);
// Max-per-slot cap respected (3 default; hands has 2 crowd items, 1 owned -> 1 missing).
Check("fp-cap", plan.Count(p => p.Slot == FashionSlot.Hands) == 1, $"{plan.Count(p => p.Slot == FashionSlot.Hands)}");

// ---- 13. SlotPlanner: the flat per-slot view (UI unhide, v0.2.0.0) ----
// The old UI dropped Recipe==null rows from the missing list and hid everything behind
// collapsed headers. The planner must yield a plan for EVERY hinted slot, keep every
// not-owned candidate with a source label, and be honest about unknown ownership.
var slotPlans = SlotPlanner.Compose(week449, crowd, planOwned,
    id => id is HailstormGloves ? new RecipeOption(7777, 5, 90) : null, wearFilterOwned: true);
Check("sp-four-plans", slotPlans.Count == 4, $"{slotPlans.Count} plans (body/hands/feet/neck)");
Check("sp-every-hinted-slot", slotPlans.Select(p => p.Slot).ToHashSet()
        .SetEquals(new[] { FashionSlot.Body, FashionSlot.Hands, FashionSlot.Feet, FashionSlot.Neck }),
    string.Join(",", slotPlans.Select(p => p.Slot)));
// Hands: Hailstorm (90) not owned -> fetch with recipe; Brand-new owned -> wear.
var spHands = slotPlans.First(p => p.Slot == FashionSlot.Hands);
Check("sp-hands-fetch-hailstorm-craftable",
    spHands.Fetch.Any(f => f.Item.ItemId == HailstormGloves && f.Source == PieceSource.Craftable && f.Recipe?.RecipeId == 7777),
    $"fetch: {string.Join(",", spHands.Fetch.Select(f => $"{f.Item.ItemId}:{f.Source}"))}");
Check("sp-hands-wear-brandnew",
    spHands.Wear.Any(w => w.ItemId == BrandNewGloves) && spHands.WearIsOwnedFiltered,
    $"wear: {string.Join(",", spHands.Wear.Select(w => w.ItemId))}");
// Body (Kasuga, no recipe) must STILL appear in fetch with NotCraftable — never silently hidden.
var spBody = slotPlans.First(p => p.Slot == FashionSlot.Body);
Check("sp-body-notcraftable-visible",
    spBody.Fetch.Any(f => f.Item.ItemId == KasugaHaori && f.Source == PieceSource.NotCraftable && f.Recipe is null),
    "a Recipe==null row must stay visible with NotCraftable, not vanish");
// Wear filter off: full crowd list (hands shows both candidates, owned flags cleared by filter semantics).
var spUnfiltered = SlotPlanner.Compose(week449, crowd, planOwned, _ => (RecipeOption?)null, wearFilterOwned: false);
var spHandsAll = spUnfiltered.First(p => p.Slot == FashionSlot.Hands);
Check("sp-wear-unfiltered-both", spHandsAll.Wear.Count == 2 && !spHandsAll.WearIsOwnedFiltered,
    $"{spHandsAll.Wear.Count} wear entries");
// Unknown ownership: fetch empty + flagged, wear degrades to unfiltered with the flag saying so.
var spUnknown = SlotPlanner.Compose(week449, crowd, null, _ => (RecipeOption?)null, wearFilterOwned: true);
Check("sp-unknown-ownership-honest",
    spUnknown.All(p => p.OwnershipUnknown && p.Fetch.Count == 0 && !p.WearIsOwnedFiltered),
    "ownership unknown must be flagged, fetch empty, wear NOT claimed as owned-filtered");
// Fetch cap respected (default 3).
var manyCrowd = new FakeCrowd(new Dictionary<FashionSlot, List<(uint id, int votes)>>
{
    [FashionSlot.Body] = Enumerable.Range(1, 10).Select(i => ((uint)(5000 + i), 10 - i)).ToList(),
}, nameToStain, plus2);
var spCap = SlotPlanner.Compose(week449, manyCrowd, new HashSet<uint>(), _ => (RecipeOption?)null, true);
Check("sp-fetch-cap-3", spCap.First(p => p.Slot == FashionSlot.Body).Fetch.Count == 3,
    $"{spCap.First(p => p.Slot == FashionSlot.Body).Fetch.Count}");
// Null crowd -> no plans at all (nothing to render).
Check("sp-null-crowd-empty", SlotPlanner.Compose(week449, null, planOwned, _ => (RecipeOption?)null, true).Count == 0);

// ---- 14. BuyResolver: source priority + honest fallbacks (buy leg, v0.3.0.0) ----
// Priority: craft > placed gil vendor > placed special shop > market > none.
var brCraft = BuyResolver.Resolve(HailstormGloves, _ => new RecipeOption(1, 0, 90), null, null, true);
Check("br-craft-wins", brCraft.Source == BuySource.Craft && brCraft.Recipe?.RecipeId == 1, $"{brCraft.Source}");
var brGil = BuyResolver.Resolve(KasugaHaori, null,
    _ => ((uint Price, string? Label, uint T, uint M, float X, float Y)?)(1250, "Engerrand (Limsa 8.6, 11.8)", 129u, 123u, 8.6f, 11.8f),
    null, true);
Check("br-gil-vendor", brGil.Source == BuySource.GilVendor && brGil.Label.Contains("1,250") && brGil.Label.Contains("Engerrand") && brGil.HasMapFlag, brGil.Label);
// Unplaced gil vendor: still labelled with its price, no map flag, and NOT dropped to market.
var brGilUnplaced = BuyResolver.Resolve(KasugaHaori, null,
    _ => ((uint Price, string? Label, uint T, uint M, float X, float Y)?)(300, null, 0u, 0u, 0f, 0f), null, true);
Check("br-gil-unplaced-labelled", brGilUnplaced.Source == BuySource.GilVendor && !brGilUnplaced.HasMapFlag && brGilUnplaced.Label.Contains("300"), brGilUnplaced.Label);
// Special shop offer (adapter hands a fully-resolved BuyOption).
var brShop = BuyResolver.Resolve(RedbillScarf, null, null,
    _ => new BuyOption { Source = BuySource.SpecialShop, ShopId = 1769472, Label = "Ixali vendor (North Shroud) - 7 Ixali Oaknots", TerritoryId = 152, MapId = 141, MapX = 25.1f, MapY = 19.9f, Costs = new[] { new ShopCost(21072, "Ixali Oaknot", 7, "Ixali Oaknots") } },
    true);
Check("br-special-shop", brShop.Source == BuySource.SpecialShop && brShop.Costs.Count == 1 && brShop.Costs[0].Phrase.Contains("Ixali Oaknots"), brShop.Label);
// Market fallback + none.
Check("br-market", BuyResolver.Resolve(BrandNewGloves, null, null, null, true).Source == BuySource.Market);
Check("br-none", BuyResolver.Resolve(BrandNewGloves, null, null, null, false).Source == BuySource.None);
// ShopCost plural phrase.
Check("br-cost-phrase", new ShopCost(20, "Storm Seal", 1500, "Storm Seals").Phrase == "1,500 Storm Seals",
    new ShopCost(20, "Storm Seal", 1500, "Storm Seals").Phrase);

// ---- 15. OwnedCatalog: the get-to leg (P3, v0.3.1.0) ----
// Owned != in bags: a dresser/armoire piece needs a trip; the note must say which, and a
// piece also in bags needs no note at all.
var cat = new OwnedCatalog
{
    ByItem = new Dictionary<uint, ItemStorage>
    {
        [KasugaHaori] = ItemStorage.Dresser,
        [HailstormGloves] = ItemStorage.Armoire,
        [RedbillScarf] = ItemStorage.Dresser | ItemStorage.Armoire,
        [BrandNewGloves] = ItemStorage.Bags,
        [RathalosGreaves] = ItemStorage.Bags | ItemStorage.Dresser,
        [99003] = ItemStorage.Equipped,
    },
};
Check("oc-contains", cat.Contains(KasugaHaori) && !cat.Contains(99099));
Check("oc-ids-set", cat.Ids().SetEquals(new HashSet<uint> { KasugaHaori, HailstormGloves, RedbillScarf, BrandNewGloves, RathalosGreaves, 99003 }));
Check("oc-note-dresser", cat.LocationNote(KasugaHaori) == "(in glamour dresser)", cat.LocationNote(KasugaHaori));
Check("oc-note-armoire", cat.LocationNote(HailstormGloves) == "(in armoire)", cat.LocationNote(HailstormGloves));
Check("oc-note-both-stored", cat.LocationNote(RedbillScarf) == "(in dresser or armoire)", cat.LocationNote(RedbillScarf));
Check("oc-note-bags-silent", cat.LocationNote(BrandNewGloves) == "", "in bags needs no note");
Check("oc-note-bags-beats-dresser", cat.LocationNote(RathalosGreaves) == "", "bags copy means no trip needed");
Check("oc-note-equipped-silent", cat.LocationNote(99003) == "");
Check("oc-note-unknown-silent", cat.LocationNote(12345) == "");
Check("oc-storage-flags", cat.StorageFor(RedbillScarf) == (ItemStorage.Dresser | ItemStorage.Armoire));

// ---- 16. JudgedFeedback: the P5 feedback loop (v0.3.2.0) ----
// One record per judged week (the result screen stays open across ticks), bounded history,
// within-1pt accuracy bar, and the summary line the window renders.
var jh = new List<JudgedRecord>();
var r449 = JudgedFeedback.Next(jh, 449, 85, 84, DateTime.UtcNow);
Check("jf-first-record", r449 is not null && r449.Diff == -1 && r449.WithinOne, r449?.Diff.ToString() ?? "<null>");
jh = JudgedFeedback.Append(jh, r449!).ToList();
Check("jf-dedupe-same-read", JudgedFeedback.Next(jh, 449, 85, 84, DateTime.UtcNow) is null, "re-reading the same result must not double-count");
var r450 = JudgedFeedback.Next(jh, 450, 80, 84, DateTime.UtcNow);
Check("jf-new-week-records", r450 is not null && !r450.WithinOne);
jh = JudgedFeedback.Append(jh, r450).ToList();
var sum = JudgedFeedback.SummaryLine(jh);
Check("jf-summary-format", sum == "week 450: awarded 84, predicted 80 (off by +4)", sum);
var (within, total) = JudgedFeedback.Accuracy(jh);
Check("jf-accuracy-1of2", within == 1 && total == 2, $"{within}/{total}");
// Bound: 30 weeks of history trims to 26.
var big = new List<JudgedRecord>();
for (var w = 400; w < 430; w++)
    big = JudgedFeedback.Append(big, new JudgedRecord { Week = w, Predicted = 80, Awarded = 80, JudgedAtUtc = DateTime.UtcNow }).ToList();
Check("jf-history-bounded-26", big.Count == 26 && big[0].Week == 404 && big[^1].Week == 429, $"{big.Count} first={big[0].Week}");
Check("jf-empty-summary-empty", JudgedFeedback.SummaryLine(new List<JudgedRecord>()) == "");

// ---- 17. OutfitAssembler: the P4 planner (v0.4.0.0) ----
// The best 80+ outfit from owned pieces + owned dyes, with per-slot dye instructions that
// never tell the player to apply a dye they do not own.
var plus2StainMap = new Dictionary<FashionSlot, uint>
{
    [FashionSlot.Head] = 76,   // Abyssal Blue
    [FashionSlot.Body] = 112,  // Metallic Silver
};
var ownedStainSet = new HashSet<uint> { 76, 1 };   // owns Abyssal Blue + Snow White
var stainNames = new Dictionary<uint, string> { [76] = "Abyssal Blue", [112] = "Metallic Silver", [1] = "Snow White", [68] = "Ink Blue" };
var ownCat = new OwnedCatalog
{
    ByItem = new Dictionary<uint, ItemStorage>
    {
        [KasugaHaori] = ItemStorage.Bags,
        [HailstormGloves] = ItemStorage.Bags,
        [RathalosGreaves] = ItemStorage.Dresser,
        [RedbillScarf] = ItemStorage.Bags,
        [BrandNewGloves] = ItemStorage.Bags,
    },
};
var asm = OutfitAssembler.Build(week449, crowd, ownCat, id => $"item {id}",
    plus2StainMap, ownedStainSet, stainNames, stainFamilies);
// week449 hints: body/hands/feet/neck. Owned golds for all four -> each hinted slot 2+8/6, unhinted base.
// body gold 2+8 + dye owned exact (+2) = 12; hands 2+8=10; feet 2+8=10; neck 2+6=8;
// weapon/head/legs 10 each; ears/wrist/ringL/ringR 8 each = 12+10+10+8+30+32 = 102? compute:
var asmTotal = asm.Total;
Check("oa-reaches-80", asm.Reaches80 && asmTotal == 102, $"total {asmTotal}");
var oaHead = asm.Pieces[(int)FashionSlot.Head];
Check("oa-dye-exact-owned", oaHead.DyeNote == "apply Abyssal Blue (+2)" && oaHead.Score == 12, $"{oaHead.DyeNote} / {oaHead.Score}");
var oaBody = asm.Pieces[(int)FashionSlot.Body];
// Body wants Metallic Silver (112, white family); player owns Snow White (1, white) -> +1 substitution note.
Check("oa-dye-substitution", oaBody.DyeNote == "Metallic Silver not owned - Snow White owned (+1)", oaBody.DyeNote);
var oaFeet = asm.Pieces[(int)FashionSlot.Feet];
Check("oa-location-note-carried", oaFeet.LocationNote == "(in glamour dresser)" && oaFeet.ItemId == RathalosGreaves, $"{oaFeet.ItemName} {oaFeet.LocationNote}");
// No owned candidate -> "any item" + gap line.
var ownEmpty = new OwnedCatalog { ByItem = new Dictionary<uint, ItemStorage>() };
var asm2 = OutfitAssembler.Build(week449, crowd, ownEmpty, id => $"item {id}",
    plus2StainMap, ownedStainSet, stainNames, stainFamilies);
Check("oa-gap-listed", asm2.Gaps.Count == 4, $"{asm2.Gaps.Count} gaps (one per hinted slot with nothing owned)");
Check("oa-any-item-fallback", asm2.Pieces[(int)FashionSlot.Body].ItemName == "any item");
// No dye owned at all -> honest "no matching dye owned".
var asm3 = OutfitAssembler.Build(week449, crowd, ownCat, id => $"item {id}",
    plus2StainMap, new HashSet<uint>(), stainNames, stainFamilies);
Check("oa-no-dye-honest", asm3.Pieces[(int)FashionSlot.Head].DyeNote.Contains("no matching dye owned"), asm3.Pieces[(int)FashionSlot.Head].DyeNote);

// ---- 18. ApplyPlanBuilder rails + the live-apply outcome (P4 executor, v0.5.0.0 -> v0.6.0.0) ----
// The plan rails (untouched slots never move, dyes only from located stock, missing pieces
// skip + report) were proven against the dry-run simulator in v0.5.0.0; the simulator is gone
// now that the apply is live, so the rails are asserted on the plan itself. The live result
// (status -> line wording, dye reminders, counts) and the equipped-container destination
// mapping are pinned here, pure, exactly as the player will read them in the window and log.
// Stain -> dye item fixtures: 76 (Abyssal Blue) = dye item 10001; 112 (Metallic Silver) = 10002.
var apStains = new Dictionary<FashionSlot, uint>
{
    [FashionSlot.Head] = 76,   // Abyssal Blue (head unhinted this week - rail test)
    [FashionSlot.Body] = 112,  // Metallic Silver
    [FashionSlot.Hands] = 76,  // Abyssal Blue, already on the worn gold
};
var dyeItemForStain = new Dictionary<uint, uint> { [76] = 10001, [112] = 10002 };
// Bag layout fixture: Kasuga (body gold) in bag slot (Inventory1=1, slot 3); Rathalos (feet
// gold) ONLY in the dresser; Redbill (neck gold) only in the armoire; Hailstorm (hands gold)
// already worn with Abyssal Blue on; Brand-new gloves in bags but not planned (hands is gold).
var locateMap = new Dictionary<uint, (ItemStorage, InventoryCoord?, uint)>
{
    [KasugaHaori] = (ItemStorage.Bags, new InventoryCoord { Container = 1, Slot = 3 }, 0u),
    [RathalosGreaves] = (ItemStorage.Dresser, null, 0u),
    [RedbillScarf] = (ItemStorage.Armoire, null, 0u),
    [HailstormGloves] = (ItemStorage.Equipped, null, 0u),
    [BrandNewGloves] = (ItemStorage.Bags, new InventoryCoord { Container = 1, Slot = 9 }, 0u),
};
var equippedMap = new Dictionary<FashionSlot, uint>
{
    [FashionSlot.Hands] = HailstormGloves,
    [FashionSlot.Head] = 555001,
    [FashionSlot.Body] = 555002,
};
var equippedStainMap = new Dictionary<FashionSlot, uint>
{
    [FashionSlot.Hands] = 76,   // Abyssal Blue already on the worn gold
    [FashionSlot.Head] = 0,
    [FashionSlot.Body] = 0,
};
// Two Jet-Black... no: week-449 wants Abyssal Blue on head + Metallic Silver on body.
// Stock: ONE Abyssal Blue dye item (10001) and NO Metallic Silver (10002 absent).
var dyeStock = new Dictionary<uint, IReadOnlyList<InventoryCoord>>
{
    [10001] = new[] { new InventoryCoord { Container = 1, Slot = 20 } },
};
var steps18 = ApplyPlanBuilder.Build(asm, apStains, equippedMap, equippedStainMap,
    id => locateMap.TryGetValue(id, out var v) ? v : null,
    st => dyeItemForStain.GetValueOrDefault(st),
    dyeStock,
    id => $"item {id}",
    stainNames);
// Body: Kasuga from bags (equip) + wants Metallic Silver (112) but no stock -> NO dye step.
var stBody = steps18.First(s => s.Slot == FashionSlot.Body);
Check("ap-body-equip-from-bags", stBody.Method == ObtainMethod.EquipFromBags && stBody.Source!.Container == 1 && stBody.Source.Slot == 3,
    $"{stBody.Method} {stBody.Source?.Container}:{stBody.Source?.Slot}");
Check("ap-body-no-dye-no-stock", stBody.StainId == 0, $"stain {stBody.StainId} (no Metallic Silver stock -> no dye step)");
// Feet: Rathalos only in dresser -> withdraw path.
var stFeet = steps18.First(s => s.Slot == FashionSlot.Feet);
Check("ap-feet-withdraw-dresser", stFeet.Method == ObtainMethod.WithdrawFromDresser, stFeet.Method.ToString());
// Neck: Redbill only in armoire -> withdraw path.
var stNeck = steps18.First(s => s.Slot == FashionSlot.Neck);
Check("ap-neck-withdraw-armoire", stNeck.Method == ObtainMethod.WithdrawFromArmoire, stNeck.Method.ToString());
// Hands: Hailstorm already worn WITH the exact dye already on -> AlreadyWorn + StainAlreadyOn, no consumption.
var stHands = steps18.First(s => s.Slot == FashionSlot.Hands);
Check("ap-hands-already-worn", stHands.Method == ObtainMethod.AlreadyWorn, stHands.Method.ToString());
Check("ap-hands-dye-already-on", stHands.StainId == 76 && stHands.StainAlreadyOn, $"stain {stHands.StainId} already={stHands.StainAlreadyOn}");
// Head: unhinted "any item" -> NEVER touched, even though a dye preference exists for head.
var stHead = steps18.First(s => s.Slot == FashionSlot.Head);
Check("ap-head-untouched", stHead.Method == ObtainMethod.LeaveUntouched && stHead.ItemId == 0, stHead.Method.ToString());
Check("ap-head-untouched-no-dye", stHead.StainId == 0, "an untouched slot must never consume a dye");
// Legs: unhinted -> untouched (same rail).
Check("ap-legs-untouched", steps18.First(s => s.Slot == FashionSlot.Legs).Method == ObtainMethod.LeaveUntouched);
// Weapon: unhinted -> untouched.
Check("ap-weapon-untouched", steps18.First(s => s.Slot == FashionSlot.Weapon).Method == ObtainMethod.LeaveUntouched);
// Ears/wrist/rings: unhinted accessories -> untouched.
Check("ap-ears-untouched", steps18.First(s => s.Slot == FashionSlot.Ears).Method == ObtainMethod.LeaveUntouched);
Check("ap-ringl-untouched", steps18.First(s => s.Slot == FashionSlot.RingL).Method == ObtainMethod.LeaveUntouched);

// The mover's eligibility view of the same plan: exactly 3 executable steps (body equip,
// feet withdraw, neck withdraw), 1 already-worn, the rest untouched. No simulator needed.
Check("ap-executable-count", steps18.Count(s => s.Method is ObtainMethod.EquipFromBags or ObtainMethod.WithdrawFromDresser or ObtainMethod.WithdrawFromArmoire) == 3,
    $"{steps18.Count(s => s.Method is ObtainMethod.EquipFromBags or ObtainMethod.WithdrawFromDresser or ObtainMethod.WithdrawFromArmoire)} executable");
Check("ap-alreadymethod-count", steps18.Count(s => s.Method == ObtainMethod.AlreadyWorn) == 1);

// Dye-from-stock path: put the player's head into the plan is NOT possible (unhinted), so
// prove the consumption path on the BODY slot instead: give Metallic Silver stock and the
// worn body slot a different item id (so equipping Kasuga replaces it and then dyes).
var dyeStock2 = new Dictionary<uint, IReadOnlyList<InventoryCoord>>
{
    [10001] = new[] { new InventoryCoord { Container = 1, Slot = 20 } },
    [10002] = new[] { new InventoryCoord { Container = 2, Slot = 5 }, new InventoryCoord { Container = 2, Slot = 6 } },
};
var steps18b = ApplyPlanBuilder.Build(asm, apStains, equippedMap, equippedStainMap,
    id => locateMap.TryGetValue(id, out var v) ? v : null,
    st => dyeItemForStain.GetValueOrDefault(st),
    dyeStock2,
    id => $"item {id}",
    stainNames);
var stBody2 = steps18b.First(s => s.Slot == FashionSlot.Body);
Check("ap-body-dye-with-stock", stBody2.StainId == 112 && stBody2.StainName == "Metallic Silver", $"{stBody2.StainId} {stBody2.StainName}");

// Dye already on the slot AND item not changing: AlreadyWorn + exact stain -> no consumption.
var equippedStainNoDye = new Dictionary<FashionSlot, uint>
{
    [FashionSlot.Hands] = 5,   // some other dye on
    [FashionSlot.Head] = 0,
    [FashionSlot.Body] = 0,
};
var steps18c = ApplyPlanBuilder.Build(asm, apStains, equippedMap, equippedStainNoDye,
    id => locateMap.TryGetValue(id, out var v) ? v : null,
    st => dyeItemForStain.GetValueOrDefault(st),
    dyeStock2,
    id => $"item {id}",
    stainNames);
var stHandsC = steps18c.First(s => s.Slot == FashionSlot.Hands);
Check("ap-hands-wrong-dye-gets-step", stHandsC.StainId == 76 && !stHandsC.StainAlreadyOn, $"stain {stHandsC.StainId}");

// Pre-dyed bag copy: the equip itself carries the dye - never counted as a consumption.
var locatePredyed = new Dictionary<uint, (ItemStorage, InventoryCoord?, uint)>
{
    [KasugaHaori] = (ItemStorage.Bags, new InventoryCoord { Container = 1, Slot = 3 }, 112u),
    [RathalosGreaves] = (ItemStorage.Dresser, null, 0u),
    [RedbillScarf] = (ItemStorage.Armoire, null, 0u),
    [HailstormGloves] = (ItemStorage.Equipped, null, 0u),
    [BrandNewGloves] = (ItemStorage.Bags, new InventoryCoord { Container = 1, Slot = 9 }, 0u),
};
var steps18g = ApplyPlanBuilder.Build(asm, apStains, equippedMap, equippedStainMap,
    id => locatePredyed.TryGetValue(id, out var v) ? v : null,
    st => dyeItemForStain.GetValueOrDefault(st),
    new Dictionary<uint, IReadOnlyList<InventoryCoord>>(),
    id => $"item {id}",
    stainNames);
var stBodyG = steps18g.First(s => s.Slot == FashionSlot.Body);
Check("ap-predyed-copy-carries", stBodyG.Method == ObtainMethod.EquipFromBags && stBodyG.StainId == 112 && stBodyG.StainAlreadyOn,
    $"{stBodyG.Method} stain {stBodyG.StainId} already={stBodyG.StainAlreadyOn}");
Check("ap-predyed-action-note", stBodyG.Action.Contains("already on the copy"), stBodyG.Action);

// Shared stock honesty: two slots wanting the SAME dye with only ONE item in stock ->
// exactly one gets the dye step (reserved), the other reports none.
var plus2Shared = new Dictionary<FashionSlot, uint>(apStains)
{
    [FashionSlot.Body] = 76,   // pretend body also wants Abyssal Blue this week
    [FashionSlot.Feet] = 76,   // ...and feet too: two genuine consumers, one item in stock
};
var steps18d = ApplyPlanBuilder.Build(asm, plus2Shared, equippedMap, equippedStainMap,
    id => locateMap.TryGetValue(id, out var v) ? v : null,
    st => dyeItemForStain.GetValueOrDefault(st),
    new Dictionary<uint, IReadOnlyList<InventoryCoord>>
    {
        [10001] = new[] { new InventoryCoord { Container = 1, Slot = 20 } },
    },
    id => $"item {id}",
    stainNames);
var sharedDyeSteps = steps18d.Count(s => s.StainId == 76 && !s.StainAlreadyOn);
Check("ap-shared-dye-reserved", sharedDyeSteps == 1, $"{sharedDyeSteps} consuming steps (1 item in stock -> 1 consumer; already-on stains never consume)");

var steps18d2 = ApplyPlanBuilder.Build(asm, plus2Shared, equippedMap, equippedStainMap,
    id => locateMap.TryGetValue(id, out var v) ? v : null,
    st => dyeItemForStain.GetValueOrDefault(st),
    new Dictionary<uint, IReadOnlyList<InventoryCoord>>
    {
        [10001] = new[] { new InventoryCoord { Container = 1, Slot = 20 }, new InventoryCoord { Container = 1, Slot = 21 } },
    },
    id => $"item {id}",
    stainNames);
Check("ap-shared-dye-two-stock-two-consumers", steps18d2.Count(s => s.StainId == 76 && !s.StainAlreadyOn) == 2,
    $"{steps18d2.Count(s => s.StainId == 76 && !s.StainAlreadyOn)} (2 items in stock -> 2 consumers)");

// Missing piece: planner named it, but it is nowhere now.
var locateMissing = new Dictionary<uint, (ItemStorage, InventoryCoord?, uint)>(locateMap)
{
    [KasugaHaori] = (ItemStorage.None, null, 0u),
};
var steps18e = ApplyPlanBuilder.Build(asm, apStains, equippedMap, equippedStainMap,
    id => locateMissing.TryGetValue(id, out var v) ? (v.Item1 == ItemStorage.None ? null : v) : null,
    st => dyeItemForStain.GetValueOrDefault(st),
    dyeStock2,
    id => $"item {id}",
    stainNames);
var stBodyE = steps18e.First(s => s.Slot == FashionSlot.Body);
Check("ap-missing-reports", stBodyE.Method == ObtainMethod.Missing && stBodyE.Action.Contains("not found"), $"{stBodyE.Method}: {stBodyE.Action}");

// Empty inputs degrade honestly: no equipped map, no locator, no dye stock -> all planned
// pieces Missing (except none), no dye steps, no crash.
var steps18f = ApplyPlanBuilder.Build(asm, apStains, null, null, null, null, null, id => $"item {id}", stainNames);
Check("ap-null-snapshot-missing", steps18f.Where(s => s.ItemId != 0).All(s => s.Method == ObtainMethod.Missing),
    string.Join(",", steps18f.Where(s => s.ItemId != 0).Select(s => s.Method)));
Check("ap-null-snapshot-untouched-intact", steps18f.Where(s => s.ItemId == 0).All(s => s.Method == ObtainMethod.LeaveUntouched));

// ---- 18b. Live-apply outcome: destination mapping, status -> line, dye reminders (v0.6.0.0) ----
// The FashionSlot -> EquippedItems container slot mapping, in the game's own equipped-gear
// container order (main, off, head, body, hands, [belt - retired from the game, slot stays
// empty], legs, feet, ears, neck, wrists, ring1, ring2, soul crystal). A wrong address here
// would put a ring on someone's head, so every slot is pinned.
Check("dest-weapon", ApplyDestinations.EquippedDest(FashionSlot.Weapon) == 0);
Check("dest-head", ApplyDestinations.EquippedDest(FashionSlot.Head) == 2);
Check("dest-body", ApplyDestinations.EquippedDest(FashionSlot.Body) == 3);
Check("dest-hands", ApplyDestinations.EquippedDest(FashionSlot.Hands) == 4);
Check("dest-legs", ApplyDestinations.EquippedDest(FashionSlot.Legs) == 6);
Check("dest-feet", ApplyDestinations.EquippedDest(FashionSlot.Feet) == 7);
Check("dest-ears", ApplyDestinations.EquippedDest(FashionSlot.Ears) == 8);
Check("dest-neck", ApplyDestinations.EquippedDest(FashionSlot.Neck) == 9);
Check("dest-wrist", ApplyDestinations.EquippedDest(FashionSlot.Wrist) == 10);
Check("dest-ringl", ApplyDestinations.EquippedDest(FashionSlot.RingL) == 11);
Check("dest-ringr", ApplyDestinations.EquippedDest(FashionSlot.RingR) == 12);
var destAll = Enum.GetValues<FashionSlot>().Select(ApplyDestinations.EquippedDest).ToList();
Check("dest-all-distinct", destAll.Distinct().Count() == destAll.Count, string.Join(",", destAll));
Check("dest-belt-gap-skipped", destAll.All(d => d != 5), "container slot 5 is the retired belt - nothing may map there");
Check("dest-soulcrystal-untouched", destAll.All(d => d <= 12), "container slot 13 is the soul crystal - the plugin never equips it");
Check("dest-invalid", ApplyDestinations.EquippedDest((FashionSlot)99) == -1);

// Status -> line wording, exactly as the window and the log render it.
var liveEq = ApplyLiveStep.For(stBody, ApplyStepStatus.Equipped);
Check("live-equipped-line", liveEq.Line == $"Body: equipped item {KasugaHaori}", liveEq.Line);
var liveWdDresser = ApplyLiveStep.For(stFeet, ApplyStepStatus.WithdrewThenEquipped);
Check("live-withdrew-dresser-line", liveWdDresser.Line.Contains("withdrew") && liveWdDresser.Line.Contains("glamour dresser") && liveWdDresser.Line.Contains("equipped"),
    liveWdDresser.Line);
var liveWdArmoire = ApplyLiveStep.For(stNeck, ApplyStepStatus.WithdrawPending);
Check("live-pending-armoire-line", liveWdArmoire.Line.Contains("withdrawing") && liveWdArmoire.Line.Contains("armoire"), liveWdArmoire.Line);
var liveAlready = ApplyLiveStep.For(stHands, ApplyStepStatus.AlreadyWorn);
Check("live-already-line", liveAlready.Line == $"Hands: already wearing item {HailstormGloves}", liveAlready.Line);
var liveUntouched = ApplyLiveStep.For(stHead, ApplyStepStatus.Untouched);
Check("live-untouched-line", liveUntouched.Line.Contains("leave untouched"), liveUntouched.Line);
var liveMissing = ApplyLiveStep.For(stBodyE, ApplyStepStatus.Missing);
Check("live-missing-line", liveMissing.Line.StartsWith("Body: SKIP"), liveMissing.Line);
var liveChanged = ApplyLiveStep.For(stBody, ApplyStepStatus.SkippedChanged);
Check("live-changed-line", liveChanged.Line.Contains("moved since the plan"), liveChanged.Line);
var liveFailed = ApplyLiveStep.For(stBody, ApplyStepStatus.Failed, 7);
Check("live-failed-line", liveFailed.Line.Contains("FAILED") && liveFailed.Line.Contains("code 7"), liveFailed.Line);

// Dye reminders: a planned dye that the executor cannot auto-apply (no verified dye call in
// this ClientStructs build) must surface as a manual reminder on successful steps only.
Check("live-dye-reminder-on-equipped", ApplyLiveStep.For(stBody2, ApplyStepStatus.Equipped).DyeReminder == "Body: dye manually - apply Metallic Silver",
    ApplyLiveStep.For(stBody2, ApplyStepStatus.Equipped).DyeReminder);
Check("live-dye-reminder-none-when-already-on", ApplyLiveStep.For(stHands, ApplyStepStatus.AlreadyWorn).DyeReminder == "",
    ApplyLiveStep.For(stHands, ApplyStepStatus.AlreadyWorn).DyeReminder);
Check("live-dye-reminder-none-when-untouched", ApplyLiveStep.For(stHead, ApplyStepStatus.Untouched).DyeReminder == "");
Check("live-dye-reminder-none-when-failed", ApplyLiveStep.For(stBody2, ApplyStepStatus.Failed).DyeReminder == "");

// Result aggregation: counts + predicted total carried through.
var liveResult = new ApplyLiveResult
{
    Steps = new List<ApplyLiveStep> { liveEq, liveWdDresser, liveWdArmoire, liveMissing, liveUntouched },
    PredictedTotal = asm.Total,
};
Check("live-moved-count", liveResult.Moved == 2, $"{liveResult.Moved} (equip + withdrew-then-equipped)");
Check("live-pending-count", liveResult.Pending == 1);
Check("live-failed-count", liveResult.Failed == 0);
Check("live-total-carried", liveResult.PredictedTotal == asm.Total, $"{liveResult.PredictedTotal} vs {asm.Total}");
var reminderSteps = new ApplyLiveResult
{
    Steps = new List<ApplyLiveStep> { ApplyLiveStep.For(stBody2, ApplyStepStatus.Equipped), ApplyLiveStep.For(stHands, ApplyStepStatus.AlreadyWorn) },
    PredictedTotal = asm.Total,
};
Check("live-dye-reminders-aggregated", reminderSteps.DyeReminders.Count() == 1, string.Join(" | ", reminderSteps.DyeReminders));


Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? $"OK - {passes} checks passed"
    : $"FAILED - {failures.Count}/{passes + failures.Count} checks failed: {string.Join(", ", failures)}");
return failures.Count == 0 ? 0 : 1;

static string?[] BuildHints(RemoteDataSource.ReportState? rs)
{
    var hints = new string?[11];
    if (rs?.LastOptions?.Hints is { } hs)
        foreach (var h in hs)
        {
            if (SlotKeyToSlot(h.Slot) is { } slot && !string.IsNullOrWhiteSpace(h.Hint))
                hints[(int)slot] = h.Hint;
        }
    return hints;
}

static Dictionary<FashionSlot, string> BuildDyes(RemoteDataSource.ReportState? rs, bool plus2)
{
    var d = new Dictionary<FashionSlot, string>();
    if (rs?.DyeData is { } dd)
        foreach (var (key, entry) in dd)
        {
            var v = plus2 ? entry.Plus2 : entry.Plus1;
            if (SlotKeyToSlot(key) is { } slot && !string.IsNullOrWhiteSpace(v))
                d[slot] = v;
        }
    return d;
}

static FashionSlot? SlotKeyToSlot(string? s) => s?.Trim().ToLowerInvariant() switch
{
    "weapon" => FashionSlot.Weapon,
    "head" => FashionSlot.Head,
    "body" => FashionSlot.Body,
    "hands" => FashionSlot.Hands,
    "legs" => FashionSlot.Legs,
    "feet" => FashionSlot.Feet,
    "ears" => FashionSlot.Ears,
    "neck" => FashionSlot.Neck,
    "wrist" or "wrists" => FashionSlot.Wrist,
    "ringl" or "ring left" => FashionSlot.RingL,
    "ringr" or "ring right" => FashionSlot.RingR,
    _ => null,
};

// ---------- helpers ----------

static EquippedItem?[] AllFilled()
{
    var arr = new EquippedItem?[11];
    for (var i = 0; i < 11; i++)
    {
        arr[i] = new EquippedItem
        {
            Slot = (FashionSlot)i,
            ItemId = 1000 + (uint)i,
            Name = $"generic {i}",
        };
    }
    return arr;
}

sealed class FakeCrowd : CrowdData
{
    private readonly Dictionary<FashionSlot, List<(uint id, int votes)>> _golds;
    private readonly Dictionary<string, uint> _nameToStain;
    private readonly Dictionary<FashionSlot, string> _plus2;

    public FakeCrowd(Dictionary<FashionSlot, List<(uint id, int votes)>> golds,
        Dictionary<string, uint> nameToStain, Dictionary<FashionSlot, string> plus2)
    {
        _golds = golds;
        _nameToStain = nameToStain;
        _plus2 = plus2;
    }

    public IReadOnlyList<CandidateItem> CandidatesFor(FashionWeek week, FashionSlot slot, IReadOnlySet<uint>? owned)
    {
        if (!_golds.TryGetValue(slot, out var list)) return Array.Empty<CandidateItem>();
        IEnumerable<(uint id, int votes)> seq = list.OrderByDescending(x => x.votes);
        if (owned != null) seq = seq.Where(x => owned.Contains(x.id));
        return seq.Select(x => new CandidateItem
        {
            Slot = slot,
            ItemId = x.id,
            Name = $"item {x.id}",
            Votes = x.votes,
            Owned = owned == null || owned.Contains(x.id),
        }).ToList();
    }

    public IReadOnlySet<uint> GoldIdsFor(FashionWeek week, FashionSlot slot)
    {
        if (!_golds.TryGetValue(slot, out var list)) return new HashSet<uint>();
        return list.Select(x => x.id).ToHashSet();
    }

    public uint PreferredStainFor(FashionWeek week, FashionSlot slot)
    {
        if (_plus2.TryGetValue(slot, out var name) && _nameToStain.TryGetValue(name, out var stain)) return stain;
        return 0;
    }
}
