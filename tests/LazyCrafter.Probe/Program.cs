using LazyCrafter.Adapters;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;
using Lumina;

// Usage: LazyCrafter.Probe <path-to-game/sqpack> [itemId...]
var sqpack = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack";
var data = new GameData(sqpack, new LuminaOptions { PanicOnSheetChecksumMismatch = false, LoadMultithreaded = true });
var gd = LuminaGameData.Load(data, Console.WriteLine);

// Wire the Core against the real sheets and run a few sanity checks.
var graph = new RecipeGraph(gd);
var ventures = new VentureResolver(gd);
var retainers = new[] { new RetainerStats("probe-min", 100, 16, 0, 2000, 2000), new RetainerStats("probe-dow", 100, 19, 700, 0, 0) };
var classifier = new SourceClassifier(gd, graph, ventures, retainers);
var tiering = new Tiering(graph, classifier);
var inv = new EmptyInventory();

var byTier = new Dictionary<EffortTier, int>();
var sample = 0;
foreach (var id in graph.RecipeIds)
{
    var a = tiering.Assess(id, inv);
    byTier[a.Tier] = byTier.GetValueOrDefault(a.Tier) + 1;
    sample++;
}
var unknown = new Dictionary<uint, int>();
foreach (var id in graph.RecipeIds)
{
    var a = tiering.Assess(id, inv);
    if (a.Tier != EffortTier.Blocked) continue;
    foreach (var l in a.Leaves.Where(l => l.Sources.Contains(SourceKind.Unknown))) unknown[l.ItemId] = unknown.GetValueOrDefault(l.ItemId) + 1;
}
Console.WriteLine($"unknown-source leaves: {unknown.Count} distinct; top: " + string.Join(", ", unknown.OrderByDescending(kv => kv.Value).Take(25).Select(kv => $"{kv.Key}:{gd.ItemName(kv.Key)}x{kv.Value}")));
Console.WriteLine($"assessed {sample} recipes with an empty inventory: " + string.Join(", ", byTier.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));

void Show(uint itemId)
{
    var name = gd.ItemName(itemId);
    var kinds = classifier.Classify(itemId, 1, 0);
    var g = gd.Gather(itemId);
    Console.WriteLine($"item {itemId} {name}: sources=[{string.Join(",", kinds)}] gilVendor={(gd.IsGilVendor(itemId, out var gil) ? gil : 0)} gather={(g is null ? "-" : $"{g.JobId}/L{g.Level}/{g.NodeType}/timed={g.Timed}")} fish={gd.IsFish(itemId)} market={gd.IsMarketable(itemId)} drop={gd.IsDrop(itemId)} coll={(gd.Collectable(itemId) is { } c ? $"cur{c.Currency} {string.Join("/", c.Reward)}" : "-")} desynth={gd.Desynth(itemId).Count} ventures={ventures.VenturesFor(itemId).Count}");
}

// Iron Ore, Maple Log, Black Alumen, Fire Crystal, Hempen Yarn, Potion, Iron Chocobotail Saw (desynth), Lemonette (unspoiled), Berkanan Sap (venture/drop), Rarefied Cedar Longbow (collectable).
var ids = args.Length > 1 ? args.Skip(1).Select(uint.Parse) : new uint[] { 5111, 5380, 5525, 8, 5333, 4551, 2320, 27835, 36261, 30970 };
foreach (var id in ids) Show(id);
Console.WriteLine($"craftable collectables by scrip currency: {string.Join(", ", graph.RecipeIds.Select(graph.Row).Select(x => gd.Collectable(x!.ResultItemId)).Where(c => c is not null).GroupBy(c => c!.Currency).Select(g => $"cur{g.Key}={g.Count()}"))}");

// A recipe walk: first recipe whose result is marketable.
var r = graph.RecipeIds.Select(graph.Row).First(x => x is not null && gd.IsMarketable(x.ResultItemId))!;
var node = graph.Expand(r.RecipeId)!;
Console.WriteLine($"recipe {r.RecipeId} -> {gd.ItemName(r.ResultItemId)} x{r.ResultAmount} job={r.JobId} lvl={r.Level} ingredients={string.Join(", ", node.Ingredients.Select(i => $"{gd.ItemName(i.ItemId)}x{i.Amount}{(i.SubRecipe is null ? "" : "(sub)")}"))}");
var assess = tiering.Assess(r.RecipeId, inv);
Console.WriteLine($"  tier={assess.Tier} howMany={assess.HowMany} leaves={string.Join(", ", assess.Leaves.Select(l => $"{gd.ItemName(l.ItemId)}[{string.Join("|", l.Sources)}]"))}");

// Universalis smoke test against the live API (Aether), incl. disk-cache round trip.
var cacheDir = Path.Combine(Path.GetTempPath(), "lazycrafter-probe");
if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
Directory.CreateDirectory(cacheDir);
var probeIds = new uint[] { 5111, 5333, 2320, 30970, 999999 };
using (var uni = new UniversalisClient(cacheDir, "probe", Console.WriteLine) { Scope = "Aether" })
{
    var marketable = await uni.MarketableAsync();
    var tax = await uni.TaxRatesAsync("Zalera");
    var primed = await uni.PrimeAsync(probeIds);
    var extra = await uni.PrimeAsync(gd.Recipes().Select(r => r.ResultItemId).Where(gd.IsMarketable).Distinct().Take(150));
    Console.WriteLine($"universalis: marketable={marketable.Count} tax={string.Join(",", tax.Select(kv => $"{kv.Key}={kv.Value}"))} bestTax={uni.BestTaxPct} primed={primed}+{extra} cache={uni.CacheSize} requests={uni.RequestsMade} failures={uni.Failures}");
    foreach (var id in probeIds)
    {
        var q = uni.Get(id);
        Console.WriteLine($"  quote {id} {gd.ItemName(id)}: " + (q is null ? "none" : $"minNQ={q.MinListingNq} minHQ={q.MinListingHq} medNQ={q.MedianNq} avgNQ={q.AvgSaleNq} velNQ={q.VelocityNq:F2} velHQ={q.VelocityHq:F2} listings={q.ListingsCount} upload={q.LastUpload:u}"));
    }
    var again = await uni.PrimeAsync(probeIds);
    Console.WriteLine($"  re-prime within TTL fetched {again} (expect 0)");
}
using (var uni2 = new UniversalisClient(cacheDir, "probe", Console.WriteLine))
{
    Console.WriteLine($"  disk cache reload: scope={uni2.Scope} size={uni2.CacheSize} 5111={(uni2.Get(5111)?.MinListingNq)}");
}
Console.WriteLine("OK");
return 0;

sealed class EmptyInventory : IInventory { public int Count(uint itemId) => 0; }
