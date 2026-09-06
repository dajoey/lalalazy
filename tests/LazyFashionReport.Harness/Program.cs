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

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? $"OK - {passes} checks passed"
    : $"FAILED - {failures.Count}/{passes + failures.Count} checks failed: {string.Join(", ", failures)}");
return failures.Count == 0 ? 0 : 1;

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
