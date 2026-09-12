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
