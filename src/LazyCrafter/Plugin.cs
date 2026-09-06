using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using Lalalazy.Changelog;
using LazyCrafter.Adapters;
using LazyCrafter.Catalog;
using LazyCrafter.Spike;
using LazyCrafter.UI;

namespace LazyCrafter;

/// <summary>
/// Entry point. Wiring only: service injection, window system, command handler.
/// All logic lives in <c>Core/</c> (pure) and <c>Adapters/</c> (Dalamud-facing).
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "LazyCrafter";

    [PluginService] internal static IDalamudPluginInterface Pi { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerStateSvc { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;

    private const string CommandName = "/lcraft";

    public static string Version => typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public Configuration Config { get; }

    // Adapters (Phase 3). GameData is loaded off-thread; null until then.
    public GbrData Gbr { get; }
    public AllaganInventory Inventory { get; }
    public UniversalisClient Prices { get; }
    public PlayerState Player { get; }
    public LuminaGameData? GameData { get; private set; }
    public Task GameDataLoad { get; }

    // Phase 4: every expensive computation behind the window lives here, on its own worker.
    public CatalogService Catalog { get; }

    // Phase 5: the hand-offs (ARC / GBR / Artisan / Lifestream / price match) and the ReflectionGuard behind two of them.
    public DispatchService Dispatch { get; }

    private readonly WindowSystem _windows = new("LazyCrafter");
    private readonly MainWindow _mainWindow;
    private readonly ChangelogGate _changelog;
    // Phase 6 spike runner (t_933683a5): '/lcraft spike' and nothing else. INERT - no dispatch, cart, Run tab
    // or vendor hand-off path calls into it, so a normal run behaves identically with and without it.
    private readonly VendorSpike _spike;
    private readonly CancellationTokenSource _cts = new();

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);
        // DalamudReflector is needed for the GBR game-data read (Phase 3) and the GBR/ARC dispatch (Phase 5).
        ECommonsMain.Init(pi, this, Module.DalamudReflector);

        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = pi.ConfigFile.Exists;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Config.MigrateIfNeeded();

        Gbr = new GbrData(Log);
        Player = new PlayerState(pi, ClientState, PlayerStateSvc, Data, Log);
        Inventory = new AllaganInventory(pi, Framework, Log, Config.IsSourceEnabled);
        Prices = new UniversalisClient(pi.ConfigDirectory.FullName, Version, line => Log.Warning("{Line}", line))
        {
            Ttl = TimeSpan.FromMinutes(Math.Max(1, Config.PriceCacheMinutes)),
        };

        // The catalog worker waits on GameDataLoad, so it can be created before the sheets are indexed.

        Pi.UiBuilder.Draw += _windows.Draw;
        Pi.UiBuilder.OpenConfigUi += OpenMain;
        Pi.UiBuilder.OpenMainUi += OpenMain;
        ClientState.Login += OnLogin;
        Inventory.Changed += OnInventoryChanged;

        Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the LazyCrafter window. debug | prices | plan | dispatch | status | stop | resume | changelog | spike <1-5|all|stop|results> | guard <plugin> <minVersion> | guard reset",
        });

        // Sheet indexing takes a few hundred ms - never on the framework thread.
        GameDataLoad = Task.Run(() =>
        {
            try
            {
                Gbr.Refresh();
                var gd = LuminaGameData.Load(Data.GameData, line => Log.Information("{Line}", line), Gbr.Available ? Gbr.Get : null,
                    line => Log.Warning("{Line}", line));
                gd.UseMarketableOverride(Prices.IsMarketable);
                GameData = gd;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LuminaGameData load failed");
            }
        }, _cts.Token);

        Catalog = new CatalogService(this, Framework, Log);
        Dispatch = new DispatchService(this, Framework, ChatGui, Log);
        _spike = new VendorSpike(Pi, Framework, ClientState, Condition, Objects, Targets, GameGui, ChatGui, Log, () => Version);
        _mainWindow = new MainWindow(this);
        _windows.AddWindow(_mainWindow);

        // Shared "What's new" popup (repo standing rule): shows this plugin's CHANGELOG once after an update.
        _changelog = new ChangelogGate(new ChangelogGate.Options
        {
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "LazyCrafter",
            ChangelogPath = "src/LazyCrafter/CHANGELOG.md",
            Framework = Framework,
            ClientState = ClientState,
            Condition = Condition,
            Log = Log,
            Windows = _windows,
            ExistingInstall = existingInstall,
            SeenStore = new DelegateSeenStore(
                () => Config.LastSeenChangelogVersion,
                v => { Config.LastSeenChangelogVersion = v; Pi.SavePluginConfig(Config); }),
        });

        if (ClientState.IsLoggedIn) OnLogin();

        Log.Information("LazyCrafter {Version} loaded (core {Core})", Version, Core.CoreInfo.Version);
    }

    public void SaveConfig()
    {
        Pi.SavePluginConfig(Config);
        Inventory.RefreshSources();
        Prices.Ttl = TimeSpan.FromMinutes(Math.Max(1, Config.PriceCacheMinutes));
        Catalog?.Invalidate();
    }

    private void OpenMain() => _mainWindow.IsOpen = true;

    private void OnLogin()
    {
        // Scope prices to the home DC (or world) and warm the session caches. Cheap; two requests.
        var dc = Player.DataCenterName;
        var world = Player.HomeWorldName;
        if (string.IsNullOrEmpty(dc)) return;
        Prices.Scope = Config.PriceByWorld ? world : dc;
        Prices.ScopeIsWorld = Config.PriceByWorld;
        _ = Task.Run(async () =>
        {
            try
            {
                await Prices.MarketableAsync(_cts.Token).ConfigureAwait(false);
                await Prices.TaxRatesAsync(world, _cts.Token).ConfigureAwait(false);
                Log.Debug("Universalis session caches ready: {Marketable} marketable ids, tax {Tax}", Prices.MarketableCount, Prices.TaxRates is null ? "n/a" : string.Join(",", Prices.TaxRates.Select(kv => $"{kv.Key}={kv.Value}")));
                // The marketable set and tax rate feed the tiering (Market source) and the profit model: recompute.
                Catalog?.Invalidate();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warning(ex, "Universalis session warm-up failed"); }
        }, _cts.Token);
        Inventory.Probe();
        Catalog?.Invalidate();
    }

    private void OnInventoryChanged()
    {
        Log.Verbose("Inventory changed (AllaganTools event, debounced) - refreshing counts against the cached crafting log");
        // Counts pass, not a full pass (t_410dee8a): picking up an ore cannot change the crafting log, so the
        // 13,892-flag sweep (the measured ~145 ms framework hitch) must never run for an inventory event. The
        // full pass stays on Invalidate(): login, settings changes, and the Refresh button only.
        Catalog?.InvalidateCounts();
    }

    private void OnCommand(string command, string args)
    {
        var a = args.Trim();
        if (a.Equals("debug", StringComparison.OrdinalIgnoreCase)) { LogDebugState(); return; }
        if (a.Equals("prices", StringComparison.OrdinalIgnoreCase)) { PrimeSamplePrices(); return; }
        if (a.Equals("stop", StringComparison.OrdinalIgnoreCase)) { Dispatch.Stop(); return; }
        if (a.Equals("resume", StringComparison.OrdinalIgnoreCase)) { if (!Dispatch.Resume()) ChatGui.PrintError("[LazyCrafter] nothing to resume."); return; }
        if (a.Equals("status", StringComparison.OrdinalIgnoreCase)) { PrintRunStatus(); return; }
        if (a.Equals("dispatch", StringComparison.OrdinalIgnoreCase)) { Dispatch.DispatchCart(); return; }
        if (a.Equals("plan", StringComparison.OrdinalIgnoreCase)) { PrintPlan(); return; }
        if (a.Equals("fetch", StringComparison.OrdinalIgnoreCase)) { FetchCommand(); return; }
        if (a.Equals("changelog", StringComparison.OrdinalIgnoreCase) || a.Equals("whatsnew", StringComparison.OrdinalIgnoreCase)) { _changelog.ShowNow(); return; }
        if (a.StartsWith("spike", StringComparison.OrdinalIgnoreCase)) { _spike.Command(a.Length > 5 ? a[5..] : ""); return; }
        if (a.StartsWith("guard", StringComparison.OrdinalIgnoreCase)) { GuardCommand(a[5..].Trim()); return; }
        _mainWindow.Toggle();
    }

    /// <summary>
    /// <c>/lcraft guard &lt;InternalName&gt; &lt;minVersion&gt;</c> raises a hand-off's minimum version for this session (Plan §Phase 5
    /// acceptance: "reflection guard demonstrably refuses when version mismatch is simulated"); <c>guard reset</c> clears;
    /// <c>guard</c> alone shows the pins against what is installed.
    /// </summary>
    private void GuardCommand(string args)
    {
        var g = Dispatch.Guard;
        if (args.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var k in g.Overrides.Keys.ToList()) g.OverrideMinVersion(k, null);
            ChatGui.Print("[LazyCrafter] guard overrides cleared.");
            return;
        }
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && System.Version.TryParse(parts[1], out var v))
        {
            g.OverrideMinVersion(parts[0], v);
            ChatGui.Print($"[LazyCrafter] guard: {parts[0]} now requires >= {v} for this session (installed {g.InstalledVersion(parts[0], out _)?.ToString() ?? "none"}). Dispatch to see the refusal; '/lcraft guard reset' to undo.");
            return;
        }
        foreach (var pin in new[] { Adapters.Dispatch.GbrDispatch.Pin, Adapters.Dispatch.ArcDispatch.Pin, Adapters.Dispatch.RetainerFetch.Pin })
        {
            var installed = g.InstalledVersion(pin.InternalName, out var loaded);
            var min = g.Overrides.TryGetValue(pin.InternalName, out var o) ? o : pin.MinVersion;
            ChatGui.Print($"[LazyCrafter] guard {pin.InternalName}: installed {installed?.ToString() ?? "none"}{(installed is not null && !loaded ? " (not loaded)" : "")}, pinned [{min}, {pin.MaxVerified}) - {pin.Members.Count} members verified against {pin.VerifiedAgainst}{(o is not null ? " [OVERRIDE]" : "")}.");
            var r = g.Require(pin, pin.InternalName + " check");
            if (r is not null) ChatGui.Print($"[LazyCrafter] guard {pin.InternalName}: OK - all {r.Members.Count} members resolved on {r.Version}.");
        }
        ChatGui.Print($"[LazyCrafter] Artisan {(Dispatch.Artisan.Installed ? "loaded" : "missing")}, Lifestream {(Dispatch.Lifestream.Installed ? "loaded" : "missing")}, price match (Lazy Market Companion) {(Dispatch.PriceMatch.Installed ? "loaded" : "missing")} (IPC, no reflection).");
    }

    /// <summary>
    /// <c>/lcraft fetch</c>: retrieve the current cart's out-of-bags materials from the retainers and stop there
    /// (card t_63b845ad) - the same machinery Dispatch runs first, without the crafting.
    /// </summary>
    private void FetchCommand()
    {
        var plan = Dispatch.PlanFor();
        if (plan is null) return;
        if (plan.Retrievals.Count == 0) { ChatGui.Print("[LazyCrafter] nothing to fetch - every material the cart needs is already in your bags."); return; }
        Dispatch.RetrieveOnly(plan.Retrievals);
    }

    /// <summary>
    /// <c>/lcraft status</c> (card t_c360953f): the current / last run as chat lines - headline, status, the steps
    /// that need attention (running / blocked / failed) plus a done/pending count, the blocked shopping lists, the
    /// stop reason and the resume hint. Same <see cref="Core.RunReport"/> renderer as the Run tab's Copy report,
    /// so the Run tab can stay closed.
    /// </summary>
    private void PrintRunStatus()
    {
        var s = Dispatch.Snapshot;
        var elapsed = s.State == Core.RunState.Running && s.StartedAt != DateTime.MinValue ? DateTime.UtcNow - s.StartedAt : s.Elapsed;
        foreach (var line in Core.RunReport.ChatLines(s, elapsed)) ChatGui.Print("[LazyCrafter] " + line);
    }

    /// <summary><c>/lcraft plan</c>: what Dispatch would do with the cart, without doing it.</summary>
    private void PrintPlan()
    {
        var (cartLines, _) = Catalog.LiveCart();
        var plan = Dispatch.PlanFor();
        if (plan is null) return;
        string N(uint id) => GameData?.ItemName(id) ?? $"#{id}";
        ChatGui.Print($"[LazyCrafter] plan for {cartLines.Count} cart line(s): " +
            $"retrieve [{string.Join(", ", plan.Retrievals.Select(r => $"{N(r.ItemId)} x{r.Quantity} from {r.Places}"))}] " +
            $"ARC [{string.Join(", ", plan.Ventures.Select(v => $"{N(v.ItemId)} x{v.Quantity} ({v.Match.Retainer.Name})"))}] " +
            $"GBR [{string.Join(", ", plan.Gathers.Select(x => $"{N(x.ItemId)} x{x.Quantity}"))}] " +
            $"Artisan [{string.Join(", ", plan.Crafts.Select(c => $"{N(c.ResultItemId)} x{c.Crafts}{(c.AfterGather ? "*" : "")}"))}] " +
            $"vendor [{string.Join(", ", plan.Vendor.Select(x => $"{N(x.ItemId)} x{x.Quantity}"))}] " +
            $"market [{string.Join(", ", plan.Market.Select(x => $"{N(x.ItemId)} x{x.Quantity}"))}] " +
            $"manual [{string.Join(", ", plan.Manual.Select(x => $"{N(x.ItemId)} x{x.Quantity}"))}] " +
            $"deferred [{string.Join(", ", plan.Deferred.Select(d => $"{N(d.ResultItemId)} x{d.Crafts}"))}]" +
            (plan.Crafts.Any(c => c.AfterGather) ? " (* = after GBR finishes)" : ""));
    }

    /// <summary>Phase 3 acceptance: recipe count, inventory source states, price cache size, DC, retainer count.</summary>
    private void LogDebugState()
    {
        var gd = GameData;
        Log.Information("[LazyCrafter debug] version={Version} core={Core} configVersion={Cfg} loggedIn={In}",
            Version, Core.CoreInfo.Version, Config.Version, ClientState.IsLoggedIn);
        Log.Information("[LazyCrafter debug] game data: {State}; recipes={Recipes} gilVendor={Gil} specialShop={Special} gatherable={Gather} (gbr={Gbr}) fish={Fish} ventures={Ventures} marketable={Market} drops={Drops} collectables={Coll} desynthSources={Desynth} loadMs={Ms}",
            gd is null ? (GameDataLoad.IsCompleted ? "FAILED" : "loading") : "ready",
            gd?.RecipeCount ?? 0, gd?.GilVendorCount ?? 0, gd?.SpecialShopCount ?? 0, gd?.GatherableCount ?? 0, gd?.GbrUsed ?? false,
            gd?.FishCount ?? 0, gd?.VentureCount ?? 0, gd?.MarketableCount ?? 0, gd?.DropCount ?? 0, gd?.CollectableCount ?? 0,
            gd?.DesynthSourceCount ?? 0, (int)(gd?.LoadTime.TotalMilliseconds ?? 0));
        Log.Information("[LazyCrafter debug] inventory: allaganTools={Avail} degraded={Degraded} sources: {Sources}",
            Inventory.Probe(), Inventory.Degraded, Inventory.DescribeSources());
        Log.Information("[LazyCrafter debug] prices: scope={Scope} ({Kind}) cacheSize={Cache} marketable={Marketable} ttlMin={Ttl} requests={Req} failures={Fail} lastFetch={Last} tax={Tax}",
            Prices.Scope, Prices.ScopeIsWorld ? "world" : "DC", Prices.CacheSize, Prices.MarketableCount, Prices.Ttl.TotalMinutes,
            Prices.RequestsMade, Prices.Failures, Prices.LastFetch?.ToString("HH:mm:ss") ?? "never",
            Prices.TaxRates is null ? "n/a" : string.Join(",", Prices.TaxRates.Select(kv => $"{kv.Key}={kv.Value}")));
        Log.Information("[LazyCrafter debug] player: world={World} DC={DC} jobs={Jobs} retainers={Retainers}{Hint}",
            Player.HomeWorldName, Player.DataCenterName,
            string.Join(",", Player.UnlockedJobs().Select(kv => $"{kv.Key}:{kv.Value}")),
            Player.Retainers.Count,
            Player.RetainerHint is { } h ? $" ({h})" : "");
        var snap = Catalog.Snapshot;
        Log.Information("[LazyCrafter debug] catalog: gen={Gen} status={Status} rows={Rows} tiers={Tiers} notCrafted={NotCrafted} priced={Priced} cart={Cart} view={View}/{ViewRows} computedAt={At} in {Ms} ms",
            snap.Generation, Catalog.Status, snap.Rows.Count,
            string.Join(",", snap.TierCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")),
            snap.NotYetCrafted, snap.PricedRows, snap.Cart.Count, Catalog.View.Request.Tab, Catalog.View.Rows.Count,
            snap.ComputedAt == DateTime.MinValue ? "never" : snap.ComputedAt.ToString("HH:mm:ss"), (int)snap.Duration.TotalMilliseconds);
        Log.Information("[LazyCrafter debug] dispatch: phase={Phase} status={Status} artisan={Artisan} gbr={Gbr} arc={Arc} lifestream={Ls} pricematch={Pm} guardOverrides={Ov}",
            Dispatch.Current, Dispatch.Status, Dispatch.Artisan.Installed, Dispatch.Gbr.Installed, Dispatch.Arc.Installed, Dispatch.Lifestream.Installed, Dispatch.PriceMatch.Installed,
            string.Join(",", Dispatch.Guard.Overrides.Select(kv => $"{kv.Key}>={kv.Value}")));
        foreach (var r in Player.Retainers)
            Log.Information("[LazyCrafter debug]   retainer {Name} lvl={Level} job={Job} ilvl={Ilvl} gathering={Gathering} perception={Perception}",
                r.Name, r.Level, r.JobId, r.ItemLevel, r.Gathering, r.Perception);
        ChatGui.Print("[LazyCrafter] debug state written to /xllog.");
    }

    /// <summary>Manual price smoke test: prime the first 100 marketable recipe results. Real callers pass the visible set (Phase 4).</summary>
    private void PrimeSamplePrices()
    {
        var gd = GameData;
        if (gd is null) { ChatGui.Print("[LazyCrafter] game data not loaded yet."); return; }
        if (string.IsNullOrEmpty(Prices.Scope)) { ChatGui.Print("[LazyCrafter] no price scope (not logged in?)."); return; }
        var sample = gd.Recipes().Select(r => r.ResultItemId).Where(gd.IsMarketable).Distinct().Take(UniversalisClient.BatchSize).ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                var n = await Prices.PrimeAsync(sample, _cts.Token).ConfigureAwait(false);
                Log.Information("[LazyCrafter] primed {N} of {Sample} sample prices; cache now {Cache}", n, sample.Count, Prices.CacheSize);
                ChatGui.Print($"[LazyCrafter] primed {n} prices ({Prices.Scope}); cache {Prices.CacheSize}.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Error(ex, "price prime failed"); }
        }, _cts.Token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        Commands.RemoveHandler(CommandName);
        ClientState.Login -= OnLogin;
        Inventory.Changed -= OnInventoryChanged;
        Pi.UiBuilder.Draw -= _windows.Draw;
        Pi.UiBuilder.OpenConfigUi -= OpenMain;
        Pi.UiBuilder.OpenMainUi -= OpenMain;
        _changelog.Dispose();
        _spike.Dispose();
        _windows.RemoveAllWindows();
        Dispatch.Dispose();
        Catalog.Dispose();
        Inventory.Dispose();
        Prices.Dispose();
        ECommonsMain.Dispose();
        _cts.Dispose();
    }
}
