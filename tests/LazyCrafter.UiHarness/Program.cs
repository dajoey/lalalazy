using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Lala.UiRender;
using LazyCrafter;
using LazyCrafter.Adapters;
using LazyCrafter.Catalog;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;
using LazyCrafter.UI;

namespace LazyCrafter.UiHarness;

/// <summary>
/// Renders the real LazyCrafter window (shell, top bar, catalog, cart, run and settings pages) to PNG without the
/// game, a Dalamud instance or a GPU. Only the services the plugin entry point wires to the client are stubbed
/// (see Stubs.cs); every pixel of layout comes from the real sources.
/// </summary>
internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _lookReported;
    private static bool _missingGlyphs;
    private static bool _strictGlyphs;

    // The window opens at 1180x760 on first use; 760x560 is its enforced minimum size.
    private const int DefaultWidth = 1180, DefaultHeight = 760, MinWidth = 760, MinHeight = 560;

    private sealed record Scenario(string Name, int Width, int Height, Action<Ctx> Setup)
    {
        /// <summary>Game mode only: the built-in set keeps one image per state at the default size.</summary>
        public bool GameOnly { get; init; }
        public float? Scale { get; init; }
    }

    /// <summary>Everything a scenario can reach: the stub plugin, the real window (by reflection for its private view state) and the fixtures.</summary>
    private sealed class Ctx
    {
        public required Plugin P { get; init; }
        public required MainWindow W { get; init; }
        public required Fixtures.Catalogue C { get; init; }

        public void Field(string name, object? value)
        {
            var field = typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException($"MainWindow has no field {name}.");
            if (value is string text && field.FieldType.IsEnum)
                value = Enum.Parse(field.FieldType, text);
            field.SetValue(W, value);
        }

        public void SelectTab(CatalogTab tab)
        {
            Field("_tab", tab);
            typeof(MainWindow).GetMethod("OnTabChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(W, [tab]);
        }

        public void Section(string section) => Field("_section", section);
        public void Select(CatalogRow row) => Field("_selectedRecipe", row.RecipeId);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Scenarios.

    private static IEnumerable<Scenario> BuildScenarios()
    {
        // First run: game data still indexing, no character, nothing computed yet.
        yield return new("catalog-first-run-loading", DefaultWidth, DefaultHeight, x =>
        {
            x.P.GameData = null;
            x.P.GameDataLoad = new TaskCompletionSource().Task;
            x.P.Catalog.Status = "waiting for game data";
            x.P.Catalog.Busy = true;
        });

        // Typical use: Craft now bucket, a recipe selected, a cart in progress, owned materials partly on a retainer.
        yield return new("catalog-now-typical", DefaultWidth, DefaultHeight, x => CatalogNow(x));
        yield return new("catalog-now-typical-760x560", MinWidth, MinHeight, x => CatalogNow(x)) { GameOnly = true };
        yield return new("scale150-catalog-now-typical", DefaultWidth, DefaultHeight, x => CatalogNow(x)) { GameOnly = true, Scale = 1.5f };

        // Easy bucket: sub-craft ingredient tree. The single-column layout at the minimum width is taller than the
        // minimum window, so it is shown in full (width at the minimum, height extended).
        yield return new("catalog-easy-760x1200", MinWidth, 1200, x =>
        {
            Logged(x);
            x.SelectTab(CatalogTab.Easy);
            Publish(x, CatalogTab.Easy, x.C.Tier(EffortTier.Easy), null);
            x.Select(x.C.Ingot);
        }) { GameOnly = true };

        // Some effort: the ingredient tree with every Fulfil action (craft, gather, venture, vendor, buy).
        yield return new("catalog-some-effort-tree", DefaultWidth, DefaultHeight, x =>
        {
            Logged(x);
            x.SelectTab(CatalogTab.SomeEffort);
            Publish(x, CatalogTab.SomeEffort, x.C.Tier(EffortTier.SomeEffort), null);
            x.Select(x.C.Chandelier);
        });

        // Real effort bucket with blocked recipes, AllaganTools missing, an untradeable recipe selected.
        yield return new("catalog-real-effort-degraded", DefaultWidth, DefaultHeight, x =>
        {
            Logged(x);
            x.P.Inventory.Degraded = true;
            x.SelectTab(CatalogTab.RealEffort);
            Publish(x, CatalogTab.RealEffort, x.C.Rows.Where(r => r.Tier >= EffortTier.RealEffort), null, degraded: true);
            x.Select(x.C.Rows.First(r => r.Name == "Ruby Tide Fishing Net"));
        });

        // Leveling: extra EXP column and the job picker; nothing selected (empty detail card).
        yield return new("catalog-leveling", DefaultWidth, DefaultHeight, x =>
        {
            Logged(x);
            x.SelectTab(CatalogTab.Leveling);
            x.Field("_levelingJob", 9u);
            Publish(x, CatalogTab.Leveling, x.C.Rows.Where(r => r.Tier <= EffortTier.Easy && r.JobId == 9 && r.ExpPerCraft > 0), null);
        });

        // Log completion: extra Cost column, recipe not yet crafted.
        yield return new("catalog-log-completion", DefaultWidth, DefaultHeight, x =>
        {
            Logged(x);
            x.SelectTab(CatalogTab.LogCompletion);
            Publish(x, CatalogTab.LogCompletion, x.C.Rows.Where(r => !r.LogComplete), null);
            x.Select(x.C.ChandelierUnpriced);
        });

        // Undersupplied: extra Listings column fed by the finder result.
        yield return new("catalog-undersupplied", DefaultWidth, DefaultHeight, x =>
        {
            Logged(x);
            x.SelectTab(CatalogTab.Undersupplied);
            var picks = new (string Name, double Velocity, int Listings)[]
            {
                ("Grade 8 Tincture of Strength", 14.0, 1), ("Hi-Potion", 31.0, 2), ("Peperoncino", 12.1, 1), ("Hardsilver Ingot", 9.6, 2),
                ("Titanium Ingot", 8.4, 1), ("Grade 8 Tincture of Dexterity", 5.2, 1), ("Cotton Cloth", 7.5, 2),
            };
            var rows = picks.Select(p => x.C.Rows.First(r => r.Name == p.Name)).ToList();
            var under = picks.ToDictionary(p => x.C.Rows.First(r => r.Name == p.Name).ResultItemId,
                p => new UndersuppliedItem(x.C.Rows.First(r => r.Name == p.Name).ResultItemId, p.Velocity, p.Listings,
                    x.C.Rows.First(r => r.Name == p.Name).RecipeId, p.Velocity > 0 ? p.Listings / p.Velocity : double.PositiveInfinity));
            Publish(x, CatalogTab.Undersupplied, rows, under);
            x.Select(x.C.Tincture);
        });

        // Empty filter result with the page search open on the top bar.
        yield return new("catalog-no-match-page-search", DefaultWidth, DefaultHeight, x =>
        {
            Logged(x);
            x.SelectTab(CatalogTab.Now);
            x.Field("_search", "Rarefied Mythril Cleaver");
            x.Field("_pageSearch", "dispatch");
            Publish(x, CatalogTab.Now, [], null);
        });

        // Cart.
        yield return new("cart-empty-game-data-failed", DefaultWidth, DefaultHeight, x =>
        {
            Logged(x);
            x.P.GameData = null;
            x.P.GameDataLoad = Task.CompletedTask;
            x.Section("Cart");
            Publish(x, CatalogTab.Now, [], null, withCart: false);
        });
        yield return new("cart-populated", DefaultWidth, DefaultHeight, x => CartPopulated(x));
        yield return new("cart-populated-760x560", MinWidth, MinHeight, x => CartPopulated(x)) { GameOnly = true };

        // Run page.
        yield return new("run-idle", DefaultWidth, DefaultHeight, x =>
        {
            Logged(x);
            x.Section("Run");
            Publish(x, CatalogTab.Now, x.C.Tier(EffortTier.Now), null);
        });
        yield return new("run-running", DefaultWidth, DefaultHeight, x =>
        {
            Logged(x);
            Publish(x, CatalogTab.Now, x.C.Tier(EffortTier.Now), null);
            x.P.Dispatch.Current = DispatchService.Phase.WaitCraftEnd;
            x.P.Dispatch.Status = "Artisan is crafting Hardsilver Ingot x12 (3 of 12 done)";
            // The only clock-dependent image: elapsed time and the start time are live while a run is going.
            x.P.Dispatch.Snapshot = Fixtures.RunRunning(DateTime.UtcNow.AddSeconds(-754));
        });
        yield return new("run-blocked", DefaultWidth, DefaultHeight, x => RunBlocked(x));
        // Tall: the rail's "Required before Resume" card (market, vendor, currency and manual lists) is below the fold at the default size.
        yield return new("run-blocked-1180x1300", DefaultWidth, 1300, x => RunBlocked(x)) { GameOnly = true };
        yield return new("run-failed", DefaultWidth, DefaultHeight, x =>
        {
            Logged(x);
            Publish(x, CatalogTab.Now, x.C.Tier(EffortTier.Now), null);
            x.P.Dispatch.Current = DispatchService.Phase.Failed;
            x.P.Dispatch.Status = "GatherBuddyReborn made no progress for 10 min";
            x.P.Dispatch.Snapshot = Fixtures.RunFailed();
            x.Section("Run");
        });

        // Settings.
        yield return new("settings-typical", DefaultWidth, DefaultHeight, x =>
        {
            Logged(x);
            x.Section("Settings");
            Publish(x, CatalogTab.Now, x.C.Tier(EffortTier.Now), null);
        });
        // Everything wrong at once, single-column layout at the minimum width (height extended to show every card).
        yield return new("settings-degraded-760x2600", MinWidth, 2600, x => SettingsDegraded(x)) { GameOnly = true };
    }

    private static void CatalogNow(Ctx x)
    {
        Logged(x);
        x.P.Config.ShowAboveLevel = true;
        x.SelectTab(CatalogTab.Now);
        Publish(x, CatalogTab.Now, x.C.Tier(EffortTier.Now), null);
        x.Select(x.C.Tincture);
        // Two of the selected recipe's ingredients are owned but sit on a retainer: the Retrieve action shows.
        var sage = Fixtures.Item("Sagolii Sage");
        x.P.Inventory.InBags[sage] = 2;
        x.P.Inventory.Stored[sage] = [new StoredElsewhere("retainer Wulgaru", 20, true, "Wulgaru"), new StoredElsewhere("the saddlebag", 4)];
        var onion = Fixtures.Item("Thavnairian Onion");
        x.P.Inventory.InBags[onion] = 12;
    }

    private static void CartPopulated(Ctx x)
    {
        Logged(x);
        x.Section("Cart");
        Publish(x, CatalogTab.Now, x.C.Tier(EffortTier.Now), null, withCart: true);
    }

    private static void RunBlocked(Ctx x)
    {
        Logged(x);
        Publish(x, CatalogTab.Now, x.C.Tier(EffortTier.Now), null, withCart: true);
        x.P.Dispatch.Current = DispatchService.Phase.Blocked;
        x.P.Dispatch.Status = "Waiting for you: 3 shopping stops before the cart can continue";
        x.P.Dispatch.Snapshot = Fixtures.RunBlocked();
        x.P.Dispatch.VendorsIfBuilt = new VendorLocator();
        x.Section("Run");
    }

    private static void SettingsDegraded(Ctx x)
    {
        Logged(x);
        x.Section("Settings");
        x.P.Inventory.Degraded = true;
        x.P.Player.Retainers = [];
        x.P.Player.RetainerHint = "ARControl.json not found - install/configure ARC (AutoRetainer Control) so LazyCrafter can see retainer stats; ventures are disabled until then";
        x.P.Prices.Scope = "";
        x.P.Prices.CacheSize = 0;
        x.P.Config.SetSourceEnabled(InventorySource.Saddlebag, false);
        x.P.Config.SetSourceEnabled(InventorySource.FCChest, true);
        x.P.Config.WalkToBellWhenBlocked = false;
        x.P.Config.BuyVendorItemsWithKnightshopper = false;
        x.P.GameData!.Failures.AddRange(
        [
            "LuminaSupplemental.Excel: drop sources table (FileNotFoundException: Sylvan.Data.Csv)",
            "LuminaSupplemental.Excel: desynth results table (FileNotFoundException: Sylvan.Data.Csv)",
            "LuminaSupplemental.Excel: ENpcPlace vendor locations (FileNotFoundException: Sylvan.Data.Csv)",
        ]);
        x.P.Dispatch.Artisan.Installed = false;
        x.P.Dispatch.Lifestream.Installed = false;
        x.P.Dispatch.PriceMatch.Installed = false;
        var guard = x.P.Dispatch.Guard;
        guard.Installed.Clear();
        guard.Installed["GatherBuddyReborn"] = new Version(7, 6, 2);
        guard.Installed["ARControl"] = new Version(8, 4, 1);
        guard.NotLoaded.Add("ARControl");
        guard.OverridesMap["GatherBuddyReborn"] = new Version(99, 0);
        Publish(x, CatalogTab.Now, x.C.Tier(EffortTier.Now), null, degraded: true);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Shared state setup.

    /// <summary>The baseline: a logged-in crafter with game data loaded, AllaganTools present, an idle dispatcher.</summary>
    private static void Logged(Ctx x)
    {
        var p = x.P;
        p.GameData!.Names.Clear();
        foreach (var (id, name) in Fixtures.ItemNames)
            p.GameData.Names[id] = name;
        foreach (var (id, abbr) in Fixtures.JobAbbr)
            p.GameData.Jobs[id] = abbr;
        p.GameData.DropCount = 11842;
        p.GameData.DesynthSourceCount = 6305;
        p.GameDataLoad = Task.CompletedTask;
        p.Player.HomeWorldName = "Balmung";
        p.Player.DataCenterName = "Crystal";
        p.Player.Retainers =
        [
            new RetainerStats("Wulgaru", 100, 17, 790, 5400, 4300),
            new RetainerStats("Hildibrand", 100, 16, 790, 5400, 4300),
            new RetainerStats("Marguerite", 98, 18, 760, 5100, 4100),
        ];
        p.Prices.Scope = "Crystal";
        p.Prices.CacheSize = 482;
        p.Prices.BestTaxPct = 5;
        p.Prices.RequestsMade = 37;
        p.Dispatch.Artisan.Installed = true;
        p.Dispatch.Lifestream.Installed = true;
        p.Dispatch.PriceMatch.Installed = true;
        var guard = p.Dispatch.Guard;
        guard.Installed["GatherBuddyReborn"] = new Version(7, 5, 5);
        guard.Installed["ARControl"] = new Version(8, 7, 0);
        guard.Installed["Artisan"] = new Version(4, 0, 5, 19);
        var costs = p.Catalog.UnitCosts;
        foreach (var (name, gil) in new (string, long)[]
        {
            ("Sagolii Sage", 38), ("Thavnairian Onion", 11), ("Lightning Cluster", 7), ("Titanium Ore", 54), ("Fire Cluster", 9),
            ("Titanium Ingot", 780), ("Claro Walnut Log", 145), ("Rarefied Claro Walnut Lumber", 5230), ("Rarefied Aurum Regis Ore", 11800),
            ("Cloud Pearl", 480), ("Hardsilver Ore", 31), ("Hardsilver Nugget", 96), ("Fire Shard", 3), ("Titanium Gold Nugget", 11900),
            ("Wind Cluster", 6),
        })
            costs[Fixtures.Item(name)] = gil;
    }

    private static readonly Dictionary<EffortTier, int> TierCounts = new()
    {
        [EffortTier.Now] = 38, [EffortTier.Easy] = 212, [EffortTier.SomeEffort] = 96, [EffortTier.RealEffort] = 33, [EffortTier.Blocked] = 8,
    };

    /// <summary>Publishes a snapshot and the view of <paramref name="rows"/> the worker would have computed for the tab.</summary>
    private static void Publish(Ctx x, CatalogTab tab, IEnumerable<CatalogRow> rows, IReadOnlyDictionary<uint, UndersuppliedItem>? under,
        bool withCart = true, bool degraded = false)
    {
        var all = x.C.Rows;
        var ordered = SortFor(tab, rows, under).ToList();
        var cart = withCart ? Fixtures.BuildCart(x.C) : [];
        var totals = withCart
            ? Fixtures.BuildCartTotals(x.C)
            : new CartAssessment(EffortTier.Now, [], []);
        var snapshot = new CatalogSnapshot(7, all, Fixtures.ByRecipe(all), TierCounts, 187, Fixtures.JobLevels, cart, totals,
            true, degraded, degraded ? 0 : 3, all.Count(r => r.Nq is not null), new DateTime(2026, 11, 15, 14, 2, 9, DateTimeKind.Local), TimeSpan.FromMilliseconds(412));
        var request = new ViewRequest(tab, 0, false, 0, false, "", ViewRequest.DefaultSort(tab), ViewRequest.DefaultDescending(tab), 0, 3, 2, true);
        x.P.Catalog.Snapshot = snapshot;
        x.P.Catalog.View = new CatalogView(request, snapshot.Generation, ordered, under);
        x.P.Catalog.Status = $"{ordered.Count} recipes · priced {snapshot.PricedRows} of {all.Count} · computed in 412 ms";
        x.P.Catalog.Busy = false;
    }

    private static IEnumerable<CatalogRow> SortFor(CatalogTab tab, IEnumerable<CatalogRow> rows, IReadOnlyDictionary<uint, UndersuppliedItem>? under) => tab switch
    {
        CatalogTab.Leveling => rows.OrderByDescending(r => r.ExpPerCraft).ThenBy(r => r.Name),
        CatalogTab.LogCompletion => rows.OrderBy(r => r.Est(false) is { CostComplete: true } e ? e.CashCost : long.MaxValue).ThenBy(r => r.Name),
        CatalogTab.Undersupplied => rows.OrderByDescending(r => under!.TryGetValue(r.ResultItemId, out var u) ? u.Velocity : 0).ThenBy(r => r.Name),
        _ => rows.OrderByDescending(r => r.Est(false) is { RevenueKnown: true } e ? e.PerDay : double.MinValue).ThenBy(r => r.Name),
    };

    // -----------------------------------------------------------------------------------------------------------------
    // Rendering.

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            Options = RenderOptions.Resolve(args);
            _strictGlyphs = args.Contains("--strict-glyphs");
            if (Options.OutputSubdirectory.Length > 0)
                OutputDirectory = Path.Combine(OutputDirectory, Options.OutputSubdirectory);
            Directory.CreateDirectory(OutputDirectory);

            var scenarios = BuildScenarios().Where(s => Options.IsGame || !s.GameOnly).ToList();
            var names = scenarios.Select(s => s.Name).ToList();
            if (names.Distinct().Count() != names.Count)
                throw new InvalidOperationException("Scenario names must be unique.");

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in scenarios)
            {
                var result = RenderScenario(scenario);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, scenario.Name))
                {
                    if (!Options.IsGame)
                        throw new InvalidOperationException($"{scenario.Name} is byte-identical to {hashes[hash]}.");
                    Console.WriteLine($"WARNING: {scenario.Name} is byte-identical to {hashes[hash]}; the state it sets up is not visible at this size.");
                }
                Console.WriteLine($"{scenario.Name}: {Path.GetRelativePath(ProjectRoot, result.Path).Replace('\\', '/')} | {result.Triangles} triangles | {result.Bytes:N0} bytes | sha256 {hash[..16]}…");
            }
            Console.WriteLine($"Rendered {scenarios.Count} real-source UI states ({hashes.Count} distinct). No window or game process was created.");
            if (_missingGlyphs && _strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the windows draw contains characters the game font lacks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    private static RenderResult RenderScenario(Scenario scenario)
    {
        var options = scenario.Scale is { } scale && Options.IsGame ? Options with { Scale = scale } : Options;
        var plugin = new Plugin();
        var window = new MainWindow(plugin);
        var ctx = new Ctx { P = plugin, W = window, C = Fixtures.BuildCatalogue() };
        scenario.Setup(ctx);

        var size = new Vector2(scenario.Width, scenario.Height);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = new Vector2(-10000, -10000);
            var look = UiLook.Apply(options);
            if (!_lookReported)
            {
                _lookReported = true;
                Console.WriteLine($"Look: {look.Description}");
                if (Options.IsGame)
                    CheckGlyphs(look);
            }

            // Three frames: window storage, table column widths and the child sizes settle before the one that is kept.
            for (var frame = 0; frame < 3; frame++)
            {
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(size);
                var open = true;
                ImGui.Begin(window.WindowName, ref open, window.Flags |
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
                window.Draw();
                ImGui.End();
                ImGui.Render();
            }

            var pixels = new byte[scenario.Width * scenario.Height * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 20;
                pixels[i + 1] = 20;
                pixels[i + 2] = 24;
                pixels[i + 3] = 255;
            }
            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{scenario.Name}: the real Draw method produced no draw data.");
            var stats = CpuRasterizer.Render(drawData, pixels, scenario.Width, scenario.Height, look.Textures);
            var path = Path.Combine(OutputDirectory, scenario.Name + ".png");
            PngWriter.Write(path, pixels, scenario.Width, scenario.Height);
            return new RenderResult(path, new FileInfo(path).Length, stats.Triangles);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    /// <summary>
    /// Heuristic glyph audit: every non-ASCII character in the string literals of the UI, the shared kit, the core and
    /// the rest of the plugin source (status lines reach the window from the adapters) must exist in the game font,
    /// otherwise ImGui draws the fallback mark. <c>--strict-glyphs</c> turns a miss into a failure.
    /// </summary>
    private static void CheckGlyphs(RenderLook look)
    {
        var found = new SortedDictionary<string, SortedSet<string>>();
        var root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value;
        var literal = new Regex("@?\"(?:[^\"\\\r\n]|\\.)*\"");
        var scanned = 0;
        foreach (var folder in new[] { Path.Combine("src", "LazyCrafter"), Path.Combine("src", "Shared", "LalaUiKit") })
        {
            var directory = root is null ? null : Path.Combine(root, folder);
            if (directory is null || !Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;
                scanned++;
                var text = new StringBuilder();
                foreach (var line in File.ReadLines(file))
                {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                        continue;
                    foreach (Match match in literal.Matches(line))
                    {
                        var value = match.Value;
                        try { value = Regex.Unescape(value); } catch (ArgumentException) { }
                        text.Append(value).Append('\n');
                    }
                }
                var relative = Path.GetRelativePath(root!, file).Replace('\\', '/');
                foreach (var entry in look.MissingGlyphs(text.ToString()))
                {
                    if (!found.TryGetValue(entry, out var sources))
                        found[entry] = sources = [];
                    sources.Add(relative);
                }
            }
        }

        _missingGlyphs = found.Count > 0;
        if (found.Count == 0)
        {
            Console.WriteLine($"Glyph check: every drawn non-ASCII character exists in the game font ({scanned} source files scanned).");
            return;
        }
        Console.WriteLine($"Glyph check: {found.Count} character(s) in drawn text have no glyph in the game font ({scanned} source files scanned):");
        foreach (var (entry, sources) in found)
            Console.WriteLine($"  {entry} in {string.Join(", ", sources)}");
    }

    private sealed record RenderResult(string Path, long Bytes, int Triangles);
}
