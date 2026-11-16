// Deterministic catalog, cart, run and settings state for the render scenarios. Names are in-game item, job and
// retainer-style names of realistic length; the numbers are plausible market figures, never random.
using LazyCrafter.Adapters;
using LazyCrafter.Catalog;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UiHarness;

internal static class Fixtures
{
    internal static readonly Dictionary<uint, string> JobAbbr = new()
    {
        [8] = "CRP", [9] = "BSM", [10] = "ARM", [11] = "GSM", [12] = "LTW", [13] = "WVR", [14] = "ALC", [15] = "CUL",
    };

    /// <summary>Levels of the eight crafters on the sample character; ARM and LTW sit below some recipes.</summary>
    internal static readonly Dictionary<uint, int> JobLevels = new()
    {
        [8] = 100, [9] = 100, [10] = 94, [11] = 100, [12] = 88, [13] = 100, [14] = 100, [15] = 100,
    };

    private static readonly Dictionary<string, uint> Ids = [];
    internal static readonly Dictionary<uint, string> ItemNames = [];
    private static uint _nextItem = 5000;
    private static uint _nextRecipe = 31000;

    internal static uint Item(string name)
    {
        if (Ids.TryGetValue(name, out var id))
            return id;
        id = _nextItem++;
        Ids[name] = id;
        ItemNames[id] = name;
        return id;
    }

    private static IngredientLeaf Leaf(string name, int need, int have, EffortTier tier, int depth, params SourceKind[] sources) =>
        new(Item(name), need, have, sources, tier, depth);

    private static readonly SourceKind[] Hand = [SourceKind.OnHand];

    private static ProfitEstimate? Estimate(uint recipeId, uint itemId, int units, long? revenue, long cash, long market,
        int howMany, double velocity, int listings, params string[] unpriced)
    {
        var tax = revenue is { } r ? (long)Math.Round(r * 0.05) : 0;
        long? marginCash = revenue is { } r1 ? r1 - tax - cash : null;
        long? marginMarket = revenue is { } r2 ? r2 - tax - market : null;
        var perUnit = marginCash is { } m && units > 0 ? (double)m / units : 0;
        var perDay = perUnit * Math.Min(Math.Max(howMany, units), velocity);
        var saturation = velocity > 0 ? listings / velocity : double.PositiveInfinity;
        return new ProfitEstimate(recipeId, itemId, units, false, revenue, revenue is { } hq ? hq + hq / 6 : null, cash, market, tax,
            marginCash, marginMarket, howMany, velocity, listings, perDay, saturation, unpriced.Select(Item).ToList());
    }

    private sealed record Spec(
        string Name, uint Job, int Level, EffortTier Tier, int HowMany, int Amount = 1, long? Revenue = null, long Cash = 0, long Market = 0,
        double Velocity = 0, int Listings = 0, string[]? Unpriced = null, bool Marketable = true, int Scrip = 0, double? Desynth = null,
        bool LogComplete = true, string Missing = "", IngredientLeaf[]? Leaves = null);

    private static CatalogRow Build(Spec s, int serial)
    {
        var recipe = _nextRecipe++;
        var item = Item(s.Name);
        var level = JobLevels[s.Job];
        var estimate = s.Marketable ? Estimate(recipe, item, s.Amount, s.Revenue, s.Cash, s.Market, s.HowMany, s.Velocity, s.Listings, s.Unpriced ?? []) : null;
        var exp = s.Level <= level ? Math.Max(0, 18000 - ((level - s.Level) * 260) + serial * 37) : 0;
        return new CatalogRow(recipe, item, s.Amount, s.Name, s.Job, JobAbbr[s.Job], s.Level, level, s.Tier, s.HowMany,
            s.Leaves ?? [], s.Missing, estimate, estimate, s.Marketable, true, s.Scrip, s.Desynth, s.LogComplete, exp);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Ingredient trees for the rows that get selected.

    /// <summary>Everything owned; two ingredients still sit on a retainer, so the Retrieve action shows.</summary>
    private static IngredientLeaf[] TinctureLeaves() =>
    [
        Leaf("Sagolii Sage", 6, 6, EffortTier.Now, 0, Hand),
        Leaf("Thavnairian Onion", 3, 12, EffortTier.Now, 0, Hand),
        Leaf("Lightning Cluster", 2, 38, EffortTier.Now, 0, Hand),
    ];

    /// <summary>A sub-craft chain with gathering, vendor, market, drop and unsourced leaves: every Fulfil action.</summary>
    private static IngredientLeaf[] ChandelierLeaves() =>
    [
        Leaf("Titanium Ore", 8, 0, EffortTier.Easy, 1, SourceKind.RegularNode, SourceKind.Venture),
        Leaf("Fire Cluster", 2, 0, EffortTier.Easy, 1, SourceKind.GilVendor),
        Leaf("Titanium Ingot", 4, 0, EffortTier.Easy, 0, SourceKind.SubCraft, SourceKind.Market),
        Leaf("Claro Walnut Log", 6, 0, EffortTier.SomeEffort, 1, SourceKind.TimedNode),
        Leaf("Rarefied Claro Walnut Lumber", 2, 0, EffortTier.SomeEffort, 0, SourceKind.SubCraft, SourceKind.Market),
        Leaf("Rarefied Aurum Regis Ore", 3, 1, EffortTier.RealEffort, 0, SourceKind.Drop),
        Leaf("Alpine Chandelier Frame", 1, 0, EffortTier.Blocked, 0, SourceKind.Unknown),
        Leaf("Cloud Pearl", 2, 2, EffortTier.Now, 0, Hand),
    ];

    private static IngredientLeaf[] GoldIngotLeaves() =>
    [
        Leaf("Titanium Gold Nugget", 6, 0, EffortTier.SomeEffort, 0, SourceKind.Market),
        Leaf("Rarefied Aurum Regis Ore", 2, 1, EffortTier.RealEffort, 0, SourceKind.Drop),
        Leaf("Fire Shard", 2, 40, EffortTier.Now, 0, Hand),
    ];

    private static IngredientLeaf[] NetLeaves() =>
    [
        Leaf("Hammerhead Shark Hide", 1, 0, EffortTier.Blocked, 0, SourceKind.Unknown),
        Leaf("Wind Cluster", 3, 9, EffortTier.Now, 0, Hand),
    ];

    private static IngredientLeaf[] IngotLeaves() =>
    [
        Leaf("Hardsilver Ore", 6, 6, EffortTier.Now, 1, Hand),
        Leaf("Hardsilver Nugget", 3, 2, EffortTier.Easy, 0, SourceKind.SubCraft, SourceKind.GilVendor),
        Leaf("Fire Shard", 2, 40, EffortTier.Now, 0, Hand),
    ];

    // ---------------------------------------------------------------------------------------------------------------
    // The catalog.

    internal sealed class Catalogue
    {
        public List<CatalogRow> Rows { get; } = [];
        public CatalogRow Tincture = null!;
        public CatalogRow Ingot = null!;
        public CatalogRow Chandelier = null!;
        public CatalogRow ChandelierUnpriced = null!;

        public IEnumerable<CatalogRow> Tier(EffortTier tier) => Rows.Where(r => r.Tier == tier);
    }

    internal static Catalogue BuildCatalogue()
    {
        var c = new Catalogue();
        var specs = new List<Spec>
        {
            // Now: everything owned
            new("Grade 8 Tincture of Strength", 14, 90, EffortTier.Now, 21, 3, 4200, 0, 1650, 11.4, 8, null, true, 0, 310, true, "", TinctureLeaves()),
            new("Grade 8 Tincture of Dexterity", 14, 90, EffortTier.Now, 15, 3, 3900, 0, 1710, 9.8, 12, null, true, 0, 290),
            new("Hardsilver Ingot", 9, 50, EffortTier.Now, 36, 1, 410, 0, 215, 14.2, 9, null, true, 0, 61),
            new("Titanium Ingot", 9, 56, EffortTier.Now, 24, 1, 780, 0, 342, 22.5, 14),
            new("Dwarven Mythril Ingot", 9, 60, EffortTier.Now, 10, 1, 1120, 0, 640, 6.1, 31),
            new("Rarefied Claro Walnut Lumber", 8, 90, EffortTier.Now, 8, 1, 8800, 0, 5230, 3.7, 4, null, true, 0, null, false),
            new("Maple Lumber", 8, 35, EffortTier.Now, 99, 1, 96, 0, 40, 41.0, 66),
            new("Peperoncino", 15, 51, EffortTier.Now, 12, 1, 640, 0, 210, 18.3, 22),
            new("Stuffed Peppers", 15, 52, EffortTier.Now, 12, 1, 380, 0, 118, 7.2, 41),
            new("Grade 3 Gemdraught of Vitality", 14, 80, EffortTier.Now, 6, 3, 2400, 0, 2810, 5.5, 19),
            new("Hi-Potion", 14, 55, EffortTier.Now, 30, 1, 150, 0, 144, 27.0, 55),
            new("Linen Yarn", 13, 27, EffortTier.Now, 40, 1, 22, 0, 31, 6.4, 120),
            new("Mythril Ingot", 9, 35, EffortTier.Now, 18, 1, 340, 0, 120, 0.4, 3, null, true, 0, 22),
            new("Manganese Ingot", 10, 100, EffortTier.Now, 5, 1, 12400, 0, 7020, 4.1, 6, null, true, 56, null, false),
            new("Cow Leather", 12, 90, EffortTier.Now, 7, 1, null, 0, 0, 0, 0, null, false, 0, null, true),
            // Easy
            new("Hardsilver Nugget", 9, 50, EffortTier.Easy, 0, 2, 190, 480, 600, 12.0, 17, ["Hardsilver Ore"], true, 0, null, true, "Hardsilver Ore x12", IngotLeaves()),
            new("Orange Juice", 15, 15, EffortTier.Easy, 0, 1, 45, 120, 110, 5.2, 80, null, true, 0, null, true, "Orange x4"),
            new("Cotton Cloth", 13, 30, EffortTier.Easy, 0, 1, 70, 90, 98, 9.0, 44, null, true, 0, null, true, "Cotton Boll x6"),
            new("Wool Cloth", 13, 40, EffortTier.Easy, 0, 1, 110, 260, 210, 3.0, 15, null, true, 0, null, false, "Wool Yarn x3, Linen Yarn x2"),
            new("Ash Lumber", 8, 15, EffortTier.Easy, 0, 1, 18, 40, 32, 11.0, 30, null, true, 0, null, true, "Ash Log x3"),
            new("Cobalt Ingot", 9, 25, EffortTier.Easy, 0, 1, 160, 210, 190, 4.4, 20, null, true, 0, null, true, "Cobalt Ore x4, Wind Shard x1"),
            // Some effort
            new("Rarefied Titanium Gold Ingot", 9, 90, EffortTier.SomeEffort, 0, 1, 9400, 12200, 11050, 2.2, 3, ["Titanium Gold Nugget"], true, 0, null, false, "Titanium Gold Nugget x6, Rarefied Aurum Regis Ore x2 +1", GoldIngotLeaves()),
            new("Darksteel Ingot", 9, 45, EffortTier.SomeEffort, 0, 1, 880, 610, 580, 3.4, 12, null, true, 0, null, true, "Darksteel Ore x5"),
            new("Elm Lumber", 8, 28, EffortTier.SomeEffort, 0, 1, 260, 150, 120, 2.2, 18, null, true, 0, null, true, "Elm Log x3, Wind Shard x1"),
            new("Mahogany Lumber", 8, 44, EffortTier.SomeEffort, 0, 1, 560, 340, 330, 4.0, 9, null, true, 0, null, true, "Mahogany Log x4"),
            new("Alpine Chandelier", 8, 100, EffortTier.SomeEffort, 0, 1, 180000, 126500, 131200, 0.9, 7, null, true, 0, 1800, false, "Titanium Ingot x4, Rarefied Claro Walnut Lumber x2 +2", ChandelierLeaves()),
            // Real effort / blocked
            new("Augmented Crystarium Tool Plate", 10, 100, EffortTier.RealEffort, 0, 1, 52000, 61400, 58800, 0.3, 11, ["Crystarium Plate Mold"], true, 0, null, false, "Crystarium Plate Mold x1"),
            new("Ruby Tide Fishing Net", 12, 94, EffortTier.Blocked, 0, 1, null, 0, 0, 0, 0, null, false, 0, null, false, "Hammerhead Shark Hide x1", NetLeaves()),
            new("Hammerhead Shark Leather", 12, 82, EffortTier.RealEffort, 0, 1, 3100, 4400, 4150, 1.1, 5, ["Hammerhead Shark Hide"], true, 0, null, true, "Hammerhead Shark Hide x2"),
        };
        var serial = 0;
        foreach (var spec in specs)
            c.Rows.Add(Build(spec, serial++));
        c.Tincture = c.Rows.First(r => r.Name == "Grade 8 Tincture of Strength");
        c.Ingot = c.Rows.First(r => r.Name == "Hardsilver Nugget");
        c.Chandelier = c.Rows.First(r => r.Name == "Alpine Chandelier");
        c.ChandelierUnpriced = c.Rows.First(r => r.Name == "Rarefied Titanium Gold Ingot");
        return c;
    }

    internal static IReadOnlyDictionary<uint, CatalogRow> ByRecipe(IEnumerable<CatalogRow> rows) => rows.ToDictionary(r => r.RecipeId);

    // ---------------------------------------------------------------------------------------------------------------
    // Cart.

    internal static List<CartLine> BuildCart(Catalogue c)
    {
        CartLine Line(CatalogRow row, int crafts, EffortTier tier, long? cash, bool complete)
        {
            var leaves = row.Leaves;
            var estimate = cash is { } g
                ? new ProfitEstimate(row.RecipeId, row.ResultItemId, crafts, false, row.Nq?.RevenueNq, row.Nq?.RevenueHq, g, g, 0, null, null, 0, 0, 0, 0, 0,
                    complete ? [] : [Item("Titanium Gold Nugget")])
                : null;
            return new CartLine(row.RecipeId, crafts, row, new RecipeAssessment(row.RecipeId, tier, row.HowMany, leaves), estimate);
        }
        return
        [
            Line(c.Tincture, 7, EffortTier.Now, 0, true),
            Line(c.Ingot, 12, EffortTier.Easy, 5760, true),
            Line(c.Chandelier, 1, EffortTier.SomeEffort, 126500, true),
            Line(c.ChandelierUnpriced, 2, EffortTier.SomeEffort, 24400, false),
        ];
    }

    internal static CartAssessment BuildCartTotals(Catalogue c) => new(EffortTier.RealEffort,
        [],
        [
            Leaf("Hardsilver Ore", 72, 6, EffortTier.Easy, 0, SourceKind.RegularNode, SourceKind.Venture),
            Leaf("Titanium Ore", 32, 0, EffortTier.Easy, 0, SourceKind.RegularNode),
            Leaf("Claro Walnut Log", 12, 0, EffortTier.SomeEffort, 0, SourceKind.TimedNode, SourceKind.Market),
            Leaf("Titanium Gold Nugget", 12, 3, EffortTier.SomeEffort, 0, SourceKind.Market),
            Leaf("Rarefied Aurum Regis Ore", 9, 1, EffortTier.RealEffort, 0, SourceKind.Drop),
            Leaf("Alpine Chandelier Frame", 1, 0, EffortTier.Blocked, 0, SourceKind.Unknown),
            Leaf("Fire Shard", 24, 40, EffortTier.Now, 0, Hand),
        ]);

    // ---------------------------------------------------------------------------------------------------------------
    // Runs. Local-kind times so the displayed clock text does not depend on the host time zone.

    private static readonly DateTime Start = new(2026, 11, 15, 14, 7, 31, DateTimeKind.Local);

    private static RunStep Step(StepKind kind, string name, int qty, StepState state, string? reason = null, string? external = null) =>
        new(kind, Item(name), name, qty, state, reason, external);

    internal static RunSnapshot RunRunning(DateTime startedUtc) => new(RunState.Running, "WaitCraftEnd", "Crafting", "Artisan is crafting Hardsilver Ingot x12 (3 of 12 done)",
        "cart", ["Grade 8 Tincture of Strength", "Hardsilver Ingot", "Alpine Chandelier"], startedUtc, null, TimeSpan.Zero, 1,
        [
            Step(StepKind.Retrieve, "Sagolii Sage", 4, StepState.Done),
            Step(StepKind.Venture, "Titanium Ore", 32, StepState.Done, null, "retainer Cid: 32 queued"),
            Step(StepKind.Gather, "Hardsilver Ore", 66, StepState.Done),
            Step(StepKind.Craft, "Hardsilver Nugget", 36, StepState.Done),
            Step(StepKind.Craft, "Hardsilver Ingot", 12, StepState.Running, null, "Artisan busy 1:12"),
            Step(StepKind.Craft, "Grade 8 Tincture of Strength", 7, StepState.Pending),
            Step(StepKind.Craft, "Alpine Chandelier", 1, StepState.Pending),
        ], [], null, false);

    internal static RunSnapshot RunBlocked() => new(RunState.Blocked, "Blocked", "Blocked", "Waiting for you: 3 shopping stops before the cart can continue",
        "cart", ["Alpine Chandelier", "Rarefied Titanium Gold Ingot"], Start, Start.AddMinutes(16).AddSeconds(42), TimeSpan.FromSeconds(1002), 2,
        [
            Step(StepKind.Retrieve, "Titanium Ingot", 4, StepState.Done),
            Step(StepKind.Gather, "Claro Walnut Log", 12, StepState.Done),
            Step(StepKind.Craft, "Rarefied Claro Walnut Lumber", 2, StepState.Done),
            Step(StepKind.Market, "Titanium Gold Nugget", 12, StepState.Blocked, "needs market Titanium Gold Nugget x12 (3 on retainer Cid, listed for sale)"),
            Step(StepKind.Vendor, "Cloud Pearl", 2, StepState.Blocked, "needs a gil vendor stop at Mythrite Ravine Provisioner"),
            Step(StepKind.CurrencyShop, "Rarefied Aurum Regis Ore", 3, StepState.Blocked, "7 Ixali Oaknot at Vath Stickpicker, Northern Thanalan"),
            Step(StepKind.Craft, "Alpine Chandelier", 1, StepState.Pending),
            Step(StepKind.Craft, "Rarefied Titanium Gold Ingot", 2, StepState.Pending),
        ],
        [
            new(StepKind.Market, Item("Titanium Gold Nugget"), "Titanium Gold Nugget", 12, 142800, null),
            new(StepKind.Market, Item("Claro Walnut Log"), "Claro Walnut Log", 4, null, null),
            new(StepKind.Vendor, Item("Cloud Pearl"), "Cloud Pearl", 2, null, "Mythrite Ravine Provisioner (Old Gridania 10.4, 8.2)"),
            new(StepKind.Vendor, Item("Fire Cluster"), "Fire Cluster", 6, null, "Mythrite Ravine Provisioner (Old Gridania 10.4, 8.2)"),
            new(StepKind.Vendor, Item("Wind Cluster"), "Wind Cluster", 4, null, ""),
            new(StepKind.CurrencyShop, Item("Rarefied Aurum Regis Ore"), "Rarefied Aurum Regis Ore", 3, null, "Vath Stickpicker, Northern Thanalan 14.8, 22.1"),
            new(StepKind.Manual, Item("Alpine Chandelier Frame"), "Alpine Chandelier Frame", 1, null, "unknown source"),
        ],
        "Stopped at the first shopping stop: 12 of the materials have no way to be fetched automatically.", true);

    internal static RunSnapshot RunFailed() => new(RunState.Failed, "Failed", "Failed", "GatherBuddyReborn made no progress for 10 min",
        "Hardsilver Ingot", [], Start, Start.AddMinutes(41).AddSeconds(5), TimeSpan.FromSeconds(2465), 1,
        [
            Step(StepKind.Retrieve, "Hardsilver Ore", 6, StepState.Done),
            Step(StepKind.Gather, "Hardsilver Ore", 66, StepState.Failed, "GBR made no progress for 10 min", "GBR: waiting for node"),
            Step(StepKind.Craft, "Hardsilver Nugget", 36, StepState.Pending),
            Step(StepKind.Craft, "Hardsilver Ingot", 12, StepState.Pending),
        ], [], "Stopped: the gather step did not progress. Check GatherBuddyReborn, then press Resume.", true);

    internal static RunSnapshot RunDone() => new(RunState.Done, "Done", "Done", "Finished: 7 crafts completed",
        "Grade 8 Tincture of Strength", [], Start, Start.AddMinutes(9).AddSeconds(12), TimeSpan.FromSeconds(552), 1,
        [
            Step(StepKind.Craft, "Grade 8 Tincture of Strength", 7, StepState.Done),
        ], [], null, false);
}
