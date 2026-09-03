using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using LazyCrafter.Adapters;
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
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

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

    private readonly WindowSystem _windows = new("LazyCrafter");
    private readonly MainWindow _mainWindow;
    private readonly CancellationTokenSource _cts = new();
    // P6 spike (t_977b94b4) — throwaway; lives on branch spike/p6-vnav-vendor only.
    private readonly VendorSpike _spike;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);
        // DalamudReflector is needed for the GBR game-data read (Phase 3) and the GBR/ARC dispatch (Phase 5).
        ECommonsMain.Init(pi, this, Module.DalamudReflector);

        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Config.MigrateIfNeeded();

        Gbr = new GbrData(Log);
        Player = new PlayerState(pi, ClientState, PlayerStateSvc, Data, Log);
        Inventory = new AllaganInventory(pi, Framework, Log, Config.IsSourceEnabled);
        Prices = new UniversalisClient(pi.ConfigDirectory.FullName, Version, line => Log.Warning("{Line}", line))
        {
            Ttl = TimeSpan.FromMinutes(Math.Max(1, Config.PriceCacheMinutes)),
        };

        _spike = new VendorSpike(pi, Framework, ClientState, Condition, Objects, Targets, GameGui, ChatGui, Log);
        _mainWindow = new MainWindow(this);
        _windows.AddWindow(_mainWindow);

        Pi.UiBuilder.Draw += _windows.Draw;
        Pi.UiBuilder.OpenConfigUi += OpenMain;
        Pi.UiBuilder.OpenMainUi += OpenMain;
        ClientState.Login += OnLogin;
        Inventory.Changed += OnInventoryChanged;

        Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the LazyCrafter window. '/lcraft debug' dumps state to the log; '/lcraft prices' refreshes Universalis; '/lcraft spike <1-5|all|stop|results>' runs the P6 walk-to-vendor spike.",
        });

        // Sheet indexing takes a few hundred ms - never on the framework thread.
        GameDataLoad = Task.Run(() =>
        {
            try
            {
                Gbr.Refresh();
                var gd = LuminaGameData.Load(Data.GameData, line => Log.Information("{Line}", line), Gbr.Available ? Gbr.Get : null);
                gd.UseMarketableOverride(Prices.IsMarketable);
                GameData = gd;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LuminaGameData load failed");
            }
        }, _cts.Token);

        if (ClientState.IsLoggedIn) OnLogin();

        Log.Information("LazyCrafter {Version} loaded (core {Core})", Version, Core.CoreInfo.Version);
    }

    public void SaveConfig()
    {
        Pi.SavePluginConfig(Config);
        Inventory.RefreshSources();
        Prices.Ttl = TimeSpan.FromMinutes(Math.Max(1, Config.PriceCacheMinutes));
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
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warning(ex, "Universalis session warm-up failed"); }
        }, _cts.Token);
        Inventory.Probe();
    }

    private void OnInventoryChanged()
    {
        // Phase 4 hooks the catalog recompute here. For now just note it.
        Log.Verbose("Inventory changed (AllaganTools event, debounced)");
    }

    private void OnCommand(string command, string args)
    {
        var a = args.Trim();
        if (a.Equals("debug", StringComparison.OrdinalIgnoreCase)) { LogDebugState(); return; }
        if (a.Equals("prices", StringComparison.OrdinalIgnoreCase)) { PrimeSamplePrices(); return; }
        if (a.StartsWith("spike", StringComparison.OrdinalIgnoreCase)) { _spike.Command(a.Length > 5 ? a[5..] : ""); return; }
        _mainWindow.Toggle();
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
        _windows.RemoveAllWindows();
        _spike.Dispose();
        Inventory.Dispose();
        Prices.Dispose();
        ECommonsMain.Dispose();
        _cts.Dispose();
    }
}

