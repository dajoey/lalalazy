using LazyCrafter.Adapters;
using LazyCrafter.Catalog;
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

// ---- Phase 4: the catalog pass the CatalogService worker runs, end to end, without the client ----
// Real sheets, live Universalis for the top of the "Now"-ish window, a fake character (every crafter at 100,
// nothing in the crafting log) and a fake inventory seeded from a handful of common materials so the Now/Easy
// buckets are populated the way a real bag would populate them.
{
    var swAll = System.Diagnostics.Stopwatch.StartNew();
    var builder = new CatalogBuilder(gd, graph, retainers, null, RevenueBasis.MinListing);
    var allItems = builder.AllItemIds();
    var fakeCounts = new Dictionary<uint, int>();
    foreach (var id in new uint[] { 5111, 5380, 5525, 8, 2, 3, 4, 5, 6, 7, 5333, 5106, 5107, 5432, 5364, 5362, 5322, 5326 }) fakeCounts[id] = 99;   // ores/logs/crystals/shards/yarn
    var inv2 = new CatalogBuilder.DictInventory(fakeCounts);
    var jobs = new Dictionary<uint, int>();
    foreach (var j in new uint[] { 8, 9, 10, 11, 12, 13, 14, 15 }) jobs[j] = 100;
    var logDone = new HashSet<uint>();
    Console.WriteLine($"catalog: {allItems.Count} distinct item ids touched by all recipes");

    using var uni = new UniversalisClient(cacheDir, "probe", Console.WriteLine) { Scope = "Aether" };
    await uni.MarketableAsync();
    await uni.TaxRatesAsync("Zalera");
    gd.UseMarketableOverride(uni.IsMarketable);
    var tax = uni.BestTaxPct;

    var rows = new Dictionary<uint, CatalogRow>();
    var assessAll = new Dictionary<uint, RecipeAssessment>();
    var tierCounts = new Dictionary<EffortTier, int>();
    var swPass = System.Diagnostics.Stopwatch.StartNew();
    foreach (var id in graph.RecipeIds)
    {
        var def = graph.Row(id)!;
        var a = builder.Tiering.Assess(id, inv2);
        assessAll[id] = a;
        rows[id] = builder.BuildRow(def, a, inv2, uni, tax, jobs, logDone);
        tierCounts[a.Tier] = tierCounts.GetValueOrDefault(a.Tier) + 1;
    }
    swPass.Stop();
    var snap = new CatalogSnapshot(1, rows.Values.ToArray(), rows, tierCounts, rows.Values.Count(r => !r.LogComplete), jobs,
        Array.Empty<CartLine>(), builder.Tiering.AssessCart([], inv2), true, false, retainers.Length, 0, DateTime.Now, swPass.Elapsed);
    Console.WriteLine($"catalog pass: {snap.Rows.Count} rows in {swPass.ElapsedMilliseconds} ms; tiers " + string.Join(", ", tierCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")) + $"; realEffortBucket={snap.RealEffortCount}");
    if (snap.Rows.Count != gd.RecipeCount) { Console.WriteLine($"FAIL: rows {snap.Rows.Count} != recipes {gd.RecipeCount}"); return 1; }

    // Every tab, default sort, then price the top window of the Now tab and re-evaluate like PrimeAndRefine does.
    ViewRequest Req(CatalogTab tab, bool hq = false) => new(tab, 0, hq, 0, false, "", ViewRequest.DefaultSort(tab), ViewRequest.DefaultDescending(tab), 10, 3, 2, false);
    foreach (var tab in Enum.GetValues<CatalogTab>())
    {
        var swV = System.Diagnostics.Stopwatch.StartNew();
        var v = ViewBuilder.Build(snap, Req(tab), gd, graph, uni);
        Console.WriteLine($"  view {tab,-14} {v.Rows.Count,6} rows in {swV.ElapsedMilliseconds,4} ms; top: " + string.Join(" | ", v.Rows.Take(3).Select(r => $"{r.Name} [{r.Job}{r.Level} {r.Tier} can={r.HowMany}]")));
    }

    var now = ViewBuilder.Build(snap, Req(CatalogTab.Now), gd, graph, uni);
    var wanted = new HashSet<uint>();
    foreach (var cr in now.Rows.Take(200)) { wanted.Add(cr.ResultItemId); foreach (var l in cr.Leaves) wanted.Add(l.ItemId); }   // 200 = CatalogService.PriceWindow
    wanted.RemoveWhere(id => !gd.IsMarketable(id));
    var swP = System.Diagnostics.Stopwatch.StartNew();
    var primedNow = await uni.PrimeAsync(wanted);
    Console.WriteLine($"  priced {primedNow}/{wanted.Count} items for the Now window in {swP.ElapsedMilliseconds} ms ({uni.RequestsMade} requests total, {uni.Failures} failures)");
    var touched = new HashSet<uint>(wanted);
    var re = 0;
    foreach (var cr in snap.Rows)
    {
        if (!touched.Contains(cr.ResultItemId) && !cr.Leaves.Any(l => touched.Contains(l.ItemId))) continue;
        rows[cr.RecipeId] = builder.BuildRow(graph.Row(cr.RecipeId)!, assessAll[cr.RecipeId], inv2, uni, tax, jobs, logDone);
        re++;
    }
    var snap2 = snap with { Generation = 2, Rows = rows.Values.ToArray(), ByRecipe = rows, PricedRows = rows.Values.Count(r => r.Nq is { RevenueKnown: true }) };
    var now2 = ViewBuilder.Build(snap2, Req(CatalogTab.Now), gd, graph, uni);
    Console.WriteLine($"  re-evaluated {re} rows; {snap2.PricedRows} rows now have revenue; Now by /day: " + string.Join(" | ", now2.Rows.Take(5).Select(r => $"{r.Name} margin={r.Nq?.MarginCash} /day={r.Nq?.PerDay:F0} vel={r.Nq?.Velocity:F1} sat={r.Nq?.SaturationDays:F1}")));
    var hqView = ViewBuilder.Build(snap2, Req(CatalogTab.Now, hq: true), gd, graph, uni);
    Console.WriteLine($"  HQ-only Now: {hqView.Rows.Count} rows (all CanBeHq={hqView.Rows.All(r => r.CanBeHq)}); top: " + string.Join(" | ", hqView.Rows.Take(3).Select(r => $"{r.Name} /day={r.Hq?.PerDay:F0}")));

    // Every sort key on the Now tab must run and keep the row count.
    foreach (var key in Enum.GetValues<SortKey>())
    {
        var v = ViewBuilder.Build(snap2, Req(CatalogTab.Now) with { Sort = key, Descending = true }, gd, graph, uni);
        if (v.Rows.Count != now2.Rows.Count) { Console.WriteLine($"FAIL: sort {key} changed the row count {v.Rows.Count} != {now2.Rows.Count}"); return 1; }
    }
    var search = ViewBuilder.Build(snap2, Req(CatalogTab.Now) with { Search = "ingot" }, gd, graph, uni);
    Console.WriteLine($"  search 'ingot' on Now: {search.Rows.Count} rows; all match={search.Rows.All(r => r.Name.Contains("ingot", StringComparison.OrdinalIgnoreCase))}");
    var byJob = ViewBuilder.Build(snap2, Req(CatalogTab.Easy) with { JobFilter = 10 }, gd, graph, uni);
    Console.WriteLine($"  Easy filtered to BSM: {byJob.Rows.Count} rows; all BSM={byJob.Rows.All(r => r.JobId == 10)}");

    // Cart: two lines sharing materials, TeamCraft link, ingredient tree of the first Easy row.
    var first = now2.Rows.First();
    var second = now2.Rows.Skip(1).First();
    var cart = builder.Tiering.AssessCart([(first.RecipeId, 2), (second.RecipeId, 1)], inv2);
    var link = TeamcraftExport.Link([new TeamcraftExport.Line(first.ResultItemId, first.RecipeId, 2 * first.ResultAmount), new TeamcraftExport.Line(second.ResultItemId, second.RecipeId, second.ResultAmount)]);
    Console.WriteLine($"  cart [{first.Name} x2, {second.Name} x1]: tier={cart.Tier} totals={cart.Totals.Count} missing={cart.Missing.Count()} link={link}");
    var easyRow = ViewBuilder.Build(snap2, Req(CatalogTab.Easy), gd, graph, uni).Rows.First();
    var tree = LazyCrafter.Core.IngredientTree.Build(easyRow.Leaves);
    Console.WriteLine($"  tree for {easyRow.Name}: " + string.Join("; ", LazyCrafter.Core.IngredientTree.Flatten(tree).Select(x => new string(' ', x.Depth * 2) + $"{gd.ItemName(x.Node.Leaf.ItemId)} {x.Node.Leaf.Have}/{x.Node.Leaf.Need} [{string.Join(",", x.Node.Leaf.Sources)}]")));
    Console.WriteLine($"catalog probe done in {swAll.ElapsedMilliseconds} ms");
}
// ---- Phase 5: VendorLocator (gil-vendor -> nearest aetheryte) and DispatchPlan over a real cart ----
{
    var vl = new VendorLocator(data, Console.WriteLine);
    // Coal-ish staples every crafter buys: Iron Ore (5111) is gathered, but Alumen (5524), Bomb Ash (5530), Black Alumen (5525), Growth Formula Alpha (5352)? -> use known vendor goods:
    // 5384 Maple Sap? no. Use: 5525 Black Alumen (vendor), 5530? Query a handful and print whatever resolves.
    var vendorIds = new uint[] { 5525, 5524, 5527, 5528, 5530, 4551, 5333, 5061, 5333, 8 };
    var located = 0;
    foreach (var id in vendorIds.Distinct())
    {
        var loc = vl.Find(id);
        if (loc is null) { Console.WriteLine($"  vendor {id} {gd.ItemName(id)}: none (gilVendor={gd.IsGilVendor(id, out _)})"); continue; }
        located++;
        Console.WriteLine($"  vendor {id} {gd.ItemName(id)}: {loc.NpcName} @ {loc.TerritoryName} ({loc.MapCoords.X:0.0}, {loc.MapCoords.Y:0.0}) map {loc.MapId}; aetheryte {loc.AetheryteId} {loc.AetheryteName} ({loc.AetheryteMapCoords.X:0.0}, {loc.AetheryteMapCoords.Y:0.0}) dist {loc.MapDistance:0.0}");
    }
    var groups = vl.Plan([(5525u, 3), (5524u, 2), (5527u, 1), (4551u, 5)], out var unlocated);
    Console.WriteLine($"  vendor plan: {groups.Count} stop(s) - " + string.Join(" | ", groups.Select(g => $"{g.Where.NpcName} @ {g.Where.TerritoryName}: {string.Join(", ", g.Items.Select(i => $"{gd.ItemName(i.ItemId)} x{i.Quantity}"))}")) + $"; unlocated {unlocated.Count}");
    Console.WriteLine($"  VendorLocator: {vl.ShopItemCount} gil-shop items, {vl.PlacedNpcCount} placed shop NPCs, located {located}/{vendorIds.Distinct().Count()} probes");

    // DispatchPlan over the same two-line cart the P4 probe built (empty-ish inventory): every channel should be exercised.
    var vres = new VentureResolver(gd);
    var tier = new Tiering(graph, new SourceClassifier(gd, graph, vres, retainers));
    var cartIds = graph.RecipeIds.Select(graph.Row).Where(r => r is not null && gd.IsMarketable(r.ResultItemId) && r.Level is >= 20 and <= 40).Take(2).Select(r => r!.RecipeId).ToList();
    var cartA = tier.AssessCart(cartIds.Select(id => (id, 1)).ToList(), inv);
    var plan = DispatchPlan.Build(cartA.Lines.Select(a => new DispatchPlan.Line(a, 1)).ToList(), cartA.Totals, graph, vres, retainers);
    Console.WriteLine($"  dispatch plan for [{string.Join(", ", cartIds.Select(id => gd.ItemName(graph.Row(id)!.ResultItemId)))}]: " +
        $"ARC {plan.Ventures.Count} [{string.Join(", ", plan.Ventures.Select(v => $"{gd.ItemName(v.ItemId)} x{v.Quantity} via {v.Match.Retainer.Name}"))}] " +
        $"GBR {plan.Gathers.Count} [{string.Join(", ", plan.Gathers.Select(g => $"{gd.ItemName(g.ItemId)} x{g.Quantity}"))}] " +
        $"Artisan {plan.Crafts.Count} [{string.Join(", ", plan.Crafts.Select(c => $"{gd.ItemName(c.ResultItemId)} x{c.Crafts} d{c.Depth}{(c.AfterGather ? "*" : "")}"))}] " +
        $"vendor {plan.Vendor.Count} market {plan.Market.Count} manual {plan.Manual.Count} deferred {plan.Deferred.Count} [{string.Join("; ", plan.Deferred.Select(d => $"{gd.ItemName(d.ResultItemId)}: {d.Reason}"))}]");
}
// ---- Run report (t_c360953f): render a synthetic Blocked run offline and prove every blocked item,
// every blocker reason, and every blocked channel line makes it into the report the Run tab copies and
// /lcraft status prints. This is the acceptance probe for the snapshot->report path (no Dalamud, no client).
{
    var started = DateTime.UtcNow.AddMinutes(-16).AddSeconds(-42);
    var blockedRun = new RunSnapshot(
        RunState.Blocked, "Blocked", "Blocked", "stopped: 4 crafts still blocked", "cart",
        new[] { "Alpine Chandelier" }, started, DateTime.UtcNow.AddSeconds(-10), TimeSpan.FromMinutes(16.7), 2,
        new RunStep[]
        {
            new(StepKind.Gather, 38957, "Titanium Ore", 15, StepState.Done, null, null),
            new(StepKind.Craft, 38962, "Hardsilver Nugget", 1, StepState.Done, null, "Artisan busy 0:11", 3762),
            new(StepKind.Craft, 38966, "Titanium Ingot", 3, StepState.Blocked, "needs market Titanium Ore x15", null, 3788),
            new(StepKind.Craft, 38963, "Titanium Nugget", 3, StepState.Blocked, "needs market Titanium Ore x15", null, 3787),
            new(StepKind.Craft, 38965, "Hardsilver Ingot", 1, StepState.Blocked, "needs vendor Tallow Candle x7", null, 3789),
            new(StepKind.Craft, 12566, "Alpine Chandelier", 1, StepState.Blocked, "needs craft Hardsilver Ingot x1", null, 12566),
        },
        new BlockedItem[]
        {
            new(StepKind.Market, 38957, "Titanium Ore", 15, 15 * 210, null),
            new(StepKind.Vendor, 4934, "Tallow Candle", 7, null, "Syrnphe (Old Gridania 8.0, 11.0)"),
            new(StepKind.Manual, 5352, "Growth Formula Alpha", 2, null, "special shop"),
        },
        "4 crafts still blocked - buy the market/vendor items, then resume", true);
    var report = RunReport.Render(blockedRun);
    Console.WriteLine("--- run report (Blocked) ---");
    Console.WriteLine(report);
    Console.WriteLine("--- end run report ---");
    var reportFails = new List<string>();
    foreach (var expect in new[] { "Titanium Ore x15 (~", "Tallow Candle x7", "Growth Formula Alpha x2", "Syrnphe", "buy on the market board", "buy from vendor", "needs a manual source", "est. 3,150 gil", "Press Resume" })
        if (!report.Contains(expect)) reportFails.Add($"report missing: {expect}");
    foreach (var st in blockedRun.Steps.Where(st => st.State == StepState.Blocked))
        if (!report.Contains(st.Name)) reportFails.Add($"blocked step missing from report: {st.Name}");
    foreach (var b in blockedRun.Blocked)
        if (!report.Contains($"{b.Name} x{b.Quantity}")) reportFails.Add($"blocked item missing from report: {b.Name}");
    if (!string.Join("\n", RunReport.ChatLines(blockedRun)).Contains("Titanium Ore x15 (~")) reportFails.Add("chat lines missing the market line");
    if (RunReport.Render(RunSnapshot.Empty).Contains("x15")) reportFails.Add("Empty run rendered a step list");
    Console.WriteLine(reportFails.Count == 0 ? "run-report probe: OK" : "run-report probe: FAIL " + string.Join("; ", reportFails));
    if (reportFails.Count > 0) return 1;
}
Console.WriteLine("OK");
return 0;

sealed class EmptyInventory : IInventory { public int Count(uint itemId) => 0; }
