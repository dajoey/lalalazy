using System.Diagnostics;
using Dalamud.Plugin.Services;
using LazyCrafter.Adapters.Dispatch;
using LazyCrafter.Catalog;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Adapters;

/// <summary>
/// Runs a cart's dispatch plan against the live plugins (Plan A-Phase 5 task 6).
/// <para>
/// <b>0.1.4.0 (card t_efde145c, Joey's option A):</b> a dispatch is now a LOOP of waves, not one pass. A wave is
/// retrieve (one batch Artisan bell session first, then per-item) -> ventures (ARC) -> gather (GBR) -> crafts (Artisan,
/// depth-first). After every wave the cart's remaining lines are re-assessed against the LIVE bags and re-planned
/// (<see cref="DispatchLoop"/>, decision logic in Core so the harness proves it). While the fresh plan has work the
/// plugin can do on its own and the last wave moved something, the next wave runs. When nothing is runnable the run
/// ends <see cref="Phase.Blocked"/> - a terminal state distinct from Done and Failed - with ONE red block naming
/// exactly what the player has to buy / fetch (market list with est. gil, vendor map flags, manual sources) and the
/// words "then press Resume". <see cref="Resume"/> re-plans from the bags and continues the same cart. This is the
/// fix for the Alpine Chandelier run (0.1.3.1): the ore was gathered and one nugget crafted, then the run ended
/// silently with four crafts never attempted - deferrals were decided at plan-build time and never revisited.
/// </para>
/// <para>
/// Every wait is bounded: GBR stall guard (no change in its status text or the gathered items' bag counts for 10 min
/// -> stop GBR, Blocked), per-craft 10-min cap (Artisan stop request + Failed with reason), plus the existing 4/10-min
/// retainer watchdogs. While any wait is in flight a heartbeat chat line fires every 3 minutes ("still working:
/// gathering 2/5 (Titanium Ore), 7:12 elapsed") so a 16-minute gather never looks dead again.
/// </para>
/// <para>
/// Vendor / market items are printed as shopping lists with map links; the per-leaf buttons do the teleport -
/// teleporting in the middle of a cart run would fight GBR (and option A says: no teleporting or walking for him
/// unattended). Everything happens on <see cref="IFramework.Update"/> in small steps; nothing blocks, nothing runs
/// in Draw. The UI never reads game state from Draw: it reads <see cref="Snapshot"/>, an immutable
/// <see cref="RunSnapshot"/> replaced on every phase change / status update (contract v1 with card t_c360953f).
/// <c>/lcraft stop</c> aborts (retainer queue aborted, GBR off, Artisan stop request).
/// </para>
/// </summary>
public sealed class DispatchService : IDisposable
{
    public enum Phase { Idle, Retrieve, WaitRetrieve, Ventures, Gathers, WaitGather, Crafts, WaitCraftStart, WaitCraftEnd, Done, Failed, BatchRetrieve, BatchWait, Blocked }

    private readonly Plugin _plugin;
    private readonly IFramework _framework;
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;

    public ArtisanDispatch Artisan { get; }
    public GbrDispatch Gbr { get; }
    public ArcDispatch Arc { get; }
    public LifestreamDispatch Lifestream { get; }
    public PriceMatchDispatch PriceMatch { get; }
    public RetainerFetch Fetch { get; }
    public ReflectionGuard Guard { get; }

    private RecipeGraph? _graph;
    private VentureResolver? _ventures;
    private Tiering? _tiering;
    private VendorLocator? _vendors;
    private VendorContextProvider? _vendorCtx;

    private DispatchPlan.Plan? _plan;
    private Queue<DispatchPlan.Craft> _crafts = new();
    private DispatchPlan.Craft? _current;
    private readonly List<(uint ItemId, int Quantity)> _made = new();
    /// <summary>Crafts refused at execution time by the bags guard, reported in the summary alongside the plan's own deferrals.</summary>
    private readonly List<DispatchPlan.Deferral> _deferredAtRun = new();
    /// <summary>Bag count of the current craft's result immediately before <c>Artisan.CraftItem</c>, and how many units we expect it to add.</summary>
    private int _madeBefore, _expected;

    // ---- retrieval state (card t_63b845ad)
    /// <summary>The cart this run came from, so the loop can re-plan against live bags between waves (null for single-item runs).</summary>
    private CatalogSnapshot? _snap;
    private Queue<DispatchPlan.Retrieve> _retrievals = new();
    private DispatchPlan.Retrieve? _fetching;
    /// <summary>Materials we could not get into the bags, reported once at the end with the reason.</summary>
    private readonly List<(DispatchPlan.Retrieve Item, string Why)> _unfetched = new();
    /// <summary>
    /// The last run's blocked-listing answer, kept AFTER the run ends so <c>/lcraft blocked</c> can print the full
    /// detail on demand (card t_35be7be5). Survives <see cref="Finish"/> / <see cref="FinishBlocked"/> deliberately -
    /// it is cleared only by the next <see cref="StartRun"/>. Volatile: written on the framework thread, read from
    /// the command handler.
    /// </summary>
    private volatile BlockedListings.Summary _lastBlockedListings = BlockedListings.Summary.Empty;
    /// <summary>What the last run was ("cart", an item name), kept alongside <see cref="_lastBlockedListings"/> so the detail line reads the same after the run.</summary>
    private string _lastBlockedWhat = "the cart";
    private readonly Dictionary<uint, int> _fetchTries = new();
    private int _fetchBefore, _fetchedOk, _retrievalsPlanned;
    // ---- 0.1.3.0: the one batch session that runs before the per-item fallback.
    /// <summary>Recipe rows queued into the batch <c>RestockFromRetainers(NewCraftingList)</c> session.</summary>
    private IReadOnlyList<uint> _batchCrafts = Array.Empty<uint>();
    /// <summary>Bags per demanded item immediately before the batch session, so <see cref="BatchWait"/> measures the delta.</summary>
    private readonly Dictionary<uint, int> _batchBefore = new();
    /// <summary>How many materials the batch session actually delivered into the bags (measured, counted like <see cref="_fetchedOk"/>).</summary>
    private int _batchFetched;

    // ---- 0.1.4.0: the wave loop, the stall guards, the heartbeat, the snapshot.
    private DispatchLoop? _loop;
    private readonly StallGuard _gatherStall = new(TimeSpan.FromMinutes(10));
    private readonly StallGuard _craftStall = new(TimeSpan.FromMinutes(10));
    private readonly Stopwatch _runClock = new();
    private DateTime _runStartUtc;
    private DateTime? _endedUtc;
    private string _what = "";
    private readonly List<string> _cartNames = new();
    private readonly List<RunStep> _steps = new();
    private List<BlockedItem> _blockedItems = new();
    private string? _stoppedReason;
    private bool _waveProgress;
    /// <summary>This wave's gather list (ids + quantities) and the bag counts when GBR started, for the stall signal, the heartbeat and the landed count.</summary>
    private List<(uint ItemId, int Quantity)> _gatherList = new();
    private Dictionary<uint, int> _gatherBefore = new();
    private DateTime _nextHeartbeat = DateTime.MinValue;
    private string? _lastHeartbeat;
    private volatile RunSnapshot _snapshot = RunSnapshot.Empty;

    private readonly Stopwatch _phaseClock = new();
    private DateTime _nextPoll = DateTime.MinValue;
    private int _craftsDone, _craftsFailed;

    public Phase Current { get; private set; } = Phase.Idle;
    public string Status { get; private set; } = "idle";
    public bool Running => Current is not (Phase.Idle or Phase.Done or Phase.Failed or Phase.Blocked);

    /// <summary>Immutable picture of the current / last run, replaced on every phase change and status update. Safe to read from Draw.</summary>
    public RunSnapshot Snapshot => _snapshot;

    /// <summary>Blocked or Failed with the cart still held - <see cref="Resume"/> will re-plan and continue.</summary>
    public bool CanResume => !Running && _loop is not null && _snap is not null;

    public DispatchService(Plugin plugin, IFramework framework, IChatGui chat, IPluginLog log)
    {
        _plugin = plugin;
        _framework = framework;
        _chat = chat;
        _log = log;
        Guard = new ReflectionGuard(Plugin.Pi, chat, log);
        Artisan = new ArtisanDispatch(Plugin.Pi, log);
        Gbr = new GbrDispatch(Plugin.Pi, Guard, chat, log);
        Arc = new ArcDispatch(Plugin.Pi, Guard, chat, log);
        Lifestream = new LifestreamDispatch(Plugin.Pi, Plugin.GameGui, chat, log);
        PriceMatch = new PriceMatchDispatch(Plugin.Pi, chat);
        Fetch = new RetainerFetch(Guard, log);
        _framework.Update += Tick;
    }

    public void Dispose()
    {
        _framework.Update -= Tick;
    }

    private string Name(uint itemId) => _plugin.GameData?.ItemName(itemId) ?? $"#{itemId}";

    /// <summary>Own Core instances: <see cref="CatalogService"/>'s are worker-private and not thread-safe; game data is immutable once loaded.</summary>
    private bool EnsureCore()
    {
        var gd = _plugin.GameData;
        if (gd is null) { Say("game data is still loading; try again in a moment.", error: true); return false; }
        if (_graph is null)
        {
            _graph = new RecipeGraph(gd);
            _ventures = new VentureResolver(gd);
            _tiering = new Tiering(_graph, new SourceClassifier(gd, _graph, _ventures, _plugin.Player.Retainers, _plugin.Player.GatheredItems));
        }
        return true;
    }

    /// <summary>Recipe yielding <paramref name="itemId"/>, preferring <paramref name="preferJob"/> (dictionary lookup; safe from Draw).</summary>
    public RecipeRow? RecipeFor(uint itemId, uint preferJob)
    {
        var gd = _plugin.GameData;
        if (gd is null) return null;
        _graph ??= new RecipeGraph(gd);
        return _graph.RecipeFor(itemId, preferJob);
    }

    public VendorLocator Vendors => _vendors ??= new VendorLocator(Plugin.Data.GameData, line => _log.Information("{Line}", line), line => _log.Warning("{Line}", line));

    /// <summary>The locator only if it has already been created - so the Settings tab can report its data health
    /// without forcing the ~50 ms index build from the draw thread (t_1a91db8f).</summary>
    public VendorLocator? VendorsIfBuilt => _vendors;

    /// <summary>
    /// Where the player is standing and what a teleport costs, for the vendor ranking (card t_731ea0e7).
    /// EVERY vendor call site passes this, which is what stops the cart-run path and the per-item buttons
    /// answering differently for the same item; falls back to <see cref="VendorContext.Unknown"/> off-line.
    /// </summary>
    public VendorContext Here() =>
        (_vendorCtx ??= new VendorContextProvider(Plugin.ClientState, Plugin.Objects, Plugin.Data, _log)).Current();

    /// <summary>
    /// Build the plan for the cart as it is RIGHT NOW, without executing it (cart panel preview /
    /// <c>/lcraft plan</c>). Deliberately parameterless (t_9f646f4c): it used to take the window's snapshot and
    /// planned against a cart that could lag a just-made edit by a whole catalog pass.
    /// </summary>
    public DispatchPlan.Plan? PlanFor()
    {
        if (!EnsureCore()) return null;
        var (cart, totals) = _plugin.Catalog.LiveCart();
        var lines = cart.Select(l => new DispatchPlan.Line(l.Assessment, l.Crafts)).ToList();
        return DispatchPlan.Build(lines, totals.Totals, _graph!, _ventures!, _plugin.Player.Retainers, _plugin.Player.GatheredItems, _plugin.Inventory);
    }

    // ------------------------------------------------------------- entry points

    /// <summary>
    /// Dispatch the whole cart as a wave loop (card t_efde145c): run, re-plan from the live bags, repeat until
    /// nothing is runnable, then stop-and-report in <see cref="Phase.Blocked"/> for the player to buy / fetch and
    /// press Resume. Framework thread (button handler / command).
    /// </summary>
    public void DispatchCart()
    {
        if (Running) { Say($"a dispatch is already running ({Status}); /lcraft stop first.", error: true); return; }
        if (!EnsureCore()) return;
        var (cart, _) = _plugin.Catalog.LiveCart();
        if (cart.Count == 0) { Say("the cart is empty.", error: true); return; }

        var lines = cart
            .Where(l => l.Crafts > 0 && _graph!.Row(l.RecipeId) is not null)
            .Select(l => new DispatchLoop.CartLine(l.RecipeId, _graph!.Row(l.RecipeId)!.ResultItemId, l.Crafts))
            .ToList();
        if (lines.Count == 0) { Say("the cart has no craftable lines.", error: true); return; }

        StartRun("cart", cart.Where(l => l.Row is not null).Select(l => Name(l.Row!.ResultItemId)).ToList());
        _snap = _plugin.Catalog.Snapshot;   // run-liveness token only; the loop re-assesses from the live bags every wave
        _loop = new DispatchLoop(lines, Replan, FingerprintOf);
        TakeDecision(_loop.Begin());
    }

    /// <summary>
    /// Continue a Blocked (or Failed-with-cart) run: re-assess the cart's remaining lines against the live bags and
    /// carry on where the last plan left off. With nothing runnable it prints the same blocked block again - never
    /// silence. Returns false when there was nothing to resume.
    /// </summary>
    public bool Resume()
    {
        if (Running) { Say($"a dispatch is already running ({Status}); /lcraft stop first.", error: true); return false; }
        if (_loop is null || _snap is null) { Say("nothing to resume - dispatch a cart first.", error: true); return false; }
        Say("resuming - re-checking your bags.");
        _endedUtc = null;
        _stoppedReason = null;
        _unfetched.Clear();
        TakeDecision(_loop.Resume());
        return Current is Phase.Blocked || Running;
    }

    /// <summary>One sub-craft from the ingredient tree: Artisan only, single wave, no loop.</summary>
    public void CraftOne(uint recipeId, int crafts)
    {
        if (Running) { Say($"a dispatch is already running ({Status}); /lcraft stop first.", error: true); return; }
        if (!EnsureCore()) return;
        var row = _graph!.Row(recipeId);
        if (row is null) { Say($"unknown recipe {recipeId}.", error: true); return; }
        StartRun(Name(row.ResultItemId), []);
        _loop = null;
        _snap = null;
        StartWave(new DispatchPlan.Plan([], [], [new DispatchPlan.Craft(recipeId, row.ResultItemId, crafts, 0, false)], [], [], [], []));
    }

    /// <summary>One gather from the ingredient tree: GBR only, single wave, no loop.</summary>
    public void GatherOne(uint itemId, int quantity)
    {
        if (Running) { Say($"a dispatch is already running ({Status}); /lcraft stop first.", error: true); return; }
        StartRun(Name(itemId), []);
        _loop = null;
        _snap = null;
        StartWave(new DispatchPlan.Plan([], [new DispatchPlan.Gather(itemId, quantity, SourceKind.RegularNode)], [], [], [], [], []));
    }

    /// <summary>One venture from the ingredient tree: ARC only (no state machine needed - it is synchronous).</summary>
    public void VentureOne(uint itemId, int quantity)
    {
        if (!EnsureCore()) return;
        var match = _ventures!.ResolveBest(itemId, _plugin.Player.Retainers, _plugin.Player.GatheredItems);
        if (match is null) { Say($"no managed retainer qualifies for a {Name(itemId)} venture.", error: true); return; }
        Arc.Dispatch(new Dictionary<uint, int> { [itemId] = quantity }, _plugin.Player.ContentId, Name);
    }

    /// <summary>
    /// Fetch one material out of the retainers into the bags, on its own (the ingredient tree's Retrieve button).
    /// Same machinery as a cart run, just a one-item plan.
    /// </summary>
    public void RetrieveOne(uint itemId, int quantity)
    {
        if (Running) { Say($"a dispatch is already running ({Status}); /lcraft stop first.", error: true); return; }
        StartRun(Name(itemId), []);
        _loop = null;
        _snap = null;
        // PlacesFor, not the raw StoredWhere list: it puts reachable places first, so the line names the retainer
        // holding the stock rather than a bigger market-board listing of the same item (card t_05e6722b).
        var where = DispatchPlan.PlacesFor(_plugin.Inventory.StoredWhere(itemId), quantity);
        StartWave(new DispatchPlan.Plan([], [], [], [], [], [], [], [new DispatchPlan.Retrieve(itemId, quantity, where)]));
    }

    /// <summary>Fetch a whole set of materials and stop there - no ventures, no gathers, no crafts (<c>/lcraft fetch</c>).</summary>
    public void RetrieveOnly(IReadOnlyList<DispatchPlan.Retrieve> retrievals)
    {
        if (Running) { Say($"a dispatch is already running ({Status}); /lcraft stop first.", error: true); return; }
        if (retrievals.Count == 0) { Say("nothing to fetch."); return; }
        StartRun($"{retrievals.Count} material{(retrievals.Count == 1 ? "" : "s")}", []);
        _loop = null;
        _snap = null;
        StartWave(new DispatchPlan.Plan([], [], [], [], [], [], [], retrievals));
    }

    /// <summary>Teleport to the nearest vendor of the item and flag it (Lifestream).</summary>
    public void VendorOne(uint itemId, int quantity)
    {
        var where = Vendors.Find(itemId, Here());
        if (where is null) { Say($"no placed gil vendor found for {Name(itemId)} (it may be a special-currency or unplaced shop).", error: true); return; }
        Lifestream.GoToVendor(where, [(itemId, quantity)], Name);
    }

    public void MarketOne(uint itemId, int quantity) =>
        Lifestream.GoToMarket([(itemId, quantity)], Name, _plugin.Catalog.UnitCost);

    public void Stop(string why = "stopped by user")
    {
        if (!Running && _plan is null) { Say("nothing is running."); return; }
        if (Current is Phase.BatchRetrieve or Phase.BatchWait or Phase.Retrieve or Phase.WaitRetrieve) Fetch.Abort();
        if (Current is Phase.WaitGather or Phase.Gathers) Gbr.Stop();
        if (Current is Phase.WaitCraftStart or Phase.WaitCraftEnd or Phase.Crafts) Artisan.Stop();
        _loop = null;   // a manual stop is not resumable - the player chose to end it
        _snap = null;
        Finish(Phase.Failed, why);
    }

    // ------------------------------------------------------------- run / wave plumbing

    /// <summary>Cross-run reset: timers, counters, step list, snapshot. Called by every entry point.</summary>
    private void StartRun(string what, IReadOnlyList<string> cartNames)
    {
        _what = what;
        _cartNames.Clear();
        _cartNames.AddRange(cartNames);
        _made.Clear();
        _deferredAtRun.Clear();
        _unfetched.Clear();
        // The /lcraft blocked stash belongs to the LAST run and is deliberately kept past Finish/FinishBlocked;
        // a new run is the one thing that invalidates it (card t_35be7be5).
        _lastBlockedListings = BlockedListings.Summary.Empty;
        _lastBlockedWhat = string.IsNullOrEmpty(what) ? "the cart" : what;
        _fetchTries.Clear();
        _batchBefore.Clear();
        _steps.Clear();
        _blockedItems = new List<BlockedItem>();
        _stoppedReason = null;
        _craftsDone = _craftsFailed = 0;
        _madeBefore = _expected = 0;
        _fetchBefore = _fetchedOk = 0;
        _batchFetched = 0;
        _batchCrafts = Array.Empty<uint>();
        _current = null;
        _fetching = null;
        _runClock.Restart();
        _runStartUtc = DateTime.UtcNow;
        _endedUtc = null;
        _nextHeartbeat = DateTime.MinValue;
        _lastHeartbeat = null;
        _loop = null;
        _snap = null;
        Current = Phase.Idle;
        // Widen the inventory debounce for the whole run (t_410dee8a): gathering/crafting routes move inventory
        // every few seconds and at the idle 2 s window that is one catalog counts pass per node. The window
        // returns to 2 s in Finish / FinishBlocked, on every exit path (Stop routes through Finish).
        _plugin.Inventory.SetDispatchRunning(true);
    }

    /// <summary>One wave of the loop: queue the plan's work and enter the retrieve-first channel (0.1.3.0 logic, unchanged).</summary>
    private void StartWave(DispatchPlan.Plan plan)
    {
        _plan = plan;
        _crafts = new Queue<DispatchPlan.Craft>(plan.Crafts);
        _retrievals = new Queue<DispatchPlan.Retrieve>(plan.Retrievals);
        _retrievalsPlanned = plan.Retrievals.Count;
        _batchFetched = 0;
        _fetchTries.Clear();
        _waveProgress = false;
        foreach (var p in plan.Ventures) TrackStep(StepKind.Venture, p.ItemId, p.Quantity, StepState.Pending);
        foreach (var g in plan.Gathers) TrackStep(StepKind.Gather, g.ItemId, g.Quantity, StepState.Pending);
        foreach (var c in plan.Crafts) TrackStep(StepKind.Craft, c.ResultItemId, c.Crafts, StepState.Pending, recipeId: c.RecipeId);
        foreach (var v in plan.Vendor) TrackStep(StepKind.Vendor, v.ItemId, v.Quantity, StepState.Blocked, Readable(ReadableBlocked("buy", v.ItemId, v.Quantity)));
        foreach (var m in plan.Market) TrackStep(StepKind.Market, m.ItemId, m.Quantity, StepState.Blocked, Readable(ReadableBlocked("market", m.ItemId, m.Quantity)));
        foreach (var m in plan.Manual) TrackStep(StepKind.Manual, m.ItemId, m.Quantity, StepState.Blocked, Readable(ReadableBlocked("manual", m.ItemId, m.Quantity)));
        foreach (var r in plan.Retrievals) TrackStep(StepKind.Retrieve, r.ItemId, r.Quantity, StepState.Pending);

        Say($"dispatching {_what}{(_loop is { Pass: > 1 } l ? $" (pass {l.Pass})" : "")}: {plan.Ventures.Count} venture, {plan.Gathers.Count} gather, {plan.Crafts.Count} craft, {plan.Vendor.Count} vendor, {plan.Market.Count} market, {plan.Manual.Count} manual, {plan.Deferred.Count} deferred, {plan.Retrievals.Count} to retrieve.");
        _log.Information("dispatch plan for {What} pass {Pass}: ventures=[{V}] gathers=[{G}] crafts=[{C}] vendor=[{Ve}] market=[{M}] manual=[{Ma}] deferred=[{D}]", _what, _loop?.Pass ?? 1,
            string.Join(",", plan.Ventures.Select(v => $"{v.ItemId}x{v.Quantity}@{v.Match.Retainer.Name}")),
            string.Join(",", plan.Gathers.Select(g => $"{g.ItemId}x{g.Quantity}")),
            string.Join(",", plan.Crafts.Select(c => $"r{c.RecipeId}x{c.Crafts}d{c.Depth}{(c.AfterGather ? "g" : "")}")),
            string.Join(",", plan.Vendor.Select(p => $"{p.ItemId}x{p.Quantity}")),
            string.Join(",", plan.Market.Select(p => $"{p.ItemId}x{p.Quantity}")),
            string.Join(",", plan.Manual.Select(p => $"{p.ItemId}x{p.Quantity}")),
            string.Join(",", plan.Deferred.Select(d => $"r{d.RecipeId}:{d.Reason}")));
        if (plan.Retrievals.Count > 0)
            _log.Information("dispatch retrievals: [{R}]", string.Join(",", plan.Retrievals.Select(r => $"{r.ItemId}x{r.Quantity}@{r.Places}")));

        // ---- Retrieve: the step that has to happen before anything else can.
        //
        // Up to 0.1.1.0 this printed "retrieve before crafting: ..." and stopped, which meant pressing Dispatch again
        // produced the identical lecture forever (Joey, V2 run 4). Now we try to actually fetch it; only when we
        // genuinely cannot do we fall back to naming it - once, with the reason and what to do about it.
        //
        // 0.1.3.0: the fetch itself is ONE batch session, not one bell trip per material (Joey, live run: four
        // materials from one retainer became four separate ~5.5 s Artisan sessions). Artisan's batch overload takes
        // whole recipe rows and re-computes each ingredient's shortfall from the bags itself at session time, so the
        // queue is primed with the wave's recipes - the queued crafts plus deferred crafts whose blockers include a
        // retrieval (<see cref="RetainerBatch.Queue"/>). Items with no recipe row, and anything left over afterwards,
        // fall back to the per-item path.
        var fetchBlocker = plan.Retrievals.Count == 0 ? null : WhyNoFetch();
        if (plan.Retrievals.Count > 0 && fetchBlocker is not null)
        {
            foreach (var r in plan.Retrievals)
                Say($"retrieve before crafting: {Name(r.ItemId)} x{r.Quantity} from {r.Places} ({r.Detail}).");
            Say($"cannot fetch it automatically: {fetchBlocker}.", error: true);
            foreach (var r in plan.Retrievals) _unfetched.Add((r, fetchBlocker));
            _retrievals.Clear();
        }

        if (_retrievals.Count > 0)
        {
            _batchCrafts = RetainerBatch.Queue(plan.Crafts, plan.Deferred, id => _graph?.Row(id) is not null);
            _plugin.Inventory.DropMemo();
            foreach (var id in _batchCrafts)
            {
                var row = _graph!.Row(id)!;
                foreach (var (itemId, _) in row.Ingredients)
                    _batchBefore[itemId] = _plugin.Inventory.CountInBags(itemId);
            }
        }

        // Shopping lists and blockers are informational; print them up front so the player can start buying while the
        // wave runs. Deferrals caused purely by a retrieval we are about to perform are NOT printed here - the loop
        // re-plans after the wave and reports what is still stuck, so the player is not told a craft is blocked and
        // then told it ran.
        if (plan.Vendor.Count > 0)
        {
            var groups = Vendors.Plan(plan.Vendor.Select(p => (p.ItemId, p.Quantity)).ToList(), out var unlocated, Here());
            foreach (var (where, items) in groups) Lifestream.GoToVendor(where, items, Name, teleport: false);
            if (unlocated.Count > 0) Say("gil-vendor items with no placed vendor: " + string.Join(", ", unlocated.Select(u => $"{Name(u.ItemId)} x{u.Quantity}")));
        }
        if (plan.Market.Count > 0) Lifestream.GoToMarket(plan.Market.Select(p => (p.ItemId, p.Quantity)).ToList(), Name, _plugin.Catalog.UnitCost, teleport: false);
        if (plan.Manual.Count > 0) Say("needs a manual source: " + string.Join(", ", plan.Manual.Select(m => $"{Name(m.ItemId)} x{m.Quantity} ({string.Join("/", m.Sources.Where(s => s != SourceKind.OnHand).Select(s => s.ToString()))})")));
        var willRetrieve = _retrievals.Count > 0;
        if (!willRetrieve)
            foreach (var d in plan.Deferred) Say($"not crafting {Name(d.ResultItemId)} x{d.Crafts} yet - {Readable(d.Reason)}.", error: true);

        if (!plan.HasWork && !willRetrieve)
        {
            // A wave with nothing to hand off still reaches here through single-channel entry points (RetrieveOnly
            // with every fetch refused). Cart runs never do - TakeDecision screened them.
            Finish(Phase.Done, "nothing to hand off");
            return;
        }
        Enter(willRetrieve ? (_batchCrafts.Count > 0 ? Phase.BatchRetrieve : Phase.Retrieve) : Phase.Ventures);
    }

    /// <summary>
    /// Why we will not even try to fetch, or <c>null</c> when we will. One sentence the player can act on.
    /// <para>0.1.3.0 also fixes the config gate: the pre-0.1.3 nested <c>if</c> made
    /// <c>RetrieveFromRetainers</c> a no-op (switching it off changed an error message but the fetch still ran),
    /// so the toggle now actually gates every fetch path, batch and per-item alike.</para>
    /// </summary>
    private string? WhyNoFetch()
    {
        if (!_plugin.Config.RetrieveFromRetainers)
            return "retrieval from retainers is switched off in the settings - turn it on, or move the materials by hand";
        if (!Fetch.Installed)
            return "Artisan is not installed or not loaded, and LazyCrafter drives its retainer withdrawal to do the fetching";
        return Fetch.SessionPreflight();
    }

    /// <summary>Re-assess the cart's remaining lines against the LIVE bags and build the next wave's plan. Fresh leaves, fresh totals - never the snapshot's stale ones.</summary>
    private DispatchPlan.Plan? Replan(IReadOnlyList<DispatchLoop.CartLine> remaining)
    {
        if (_snap is null || !EnsureCore()) return null;
        _plugin.Inventory.DropMemo();
        var assessed = _tiering!.AssessCart(remaining.Select(l => (l.RecipeId, l.Crafts)), _plugin.Inventory);
        var lines = assessed.Lines
            .Select(a => new DispatchPlan.Line(a, remaining.First(r => r.RecipeId == a.RecipeId).Crafts))
            .ToList();
        return DispatchPlan.Build(lines, assessed.Totals, _graph!, _ventures!, _plugin.Player.Retainers, _plugin.Player.GatheredItems, _plugin.Inventory);
    }

    /// <summary>The bag counts of everything this wave could move, folded into one string - "did anything change?".</summary>
    private string FingerprintOf(IEnumerable<uint> ids)
    {
        _plugin.Inventory.DropMemo();
        return string.Join("|", ids.OrderBy(i => i).Select(i => $"{i}:{_plugin.Inventory.CountInBags(i)}"));
    }

    /// <summary>Act on a <see cref="DispatchLoop.Decision"/>: run the next wave, finish done, or stop-and-report blocked.</summary>
    private void TakeDecision(DispatchLoop.Decision dec)
    {
        switch (dec.Outcome)
        {
            case DispatchLoop.Outcome.Wave:
                StartWave(dec.Plan);
                break;

            case DispatchLoop.Outcome.Done:
                Finish(Phase.Done, null);
                break;

            case DispatchLoop.Outcome.Blocked:
                _blockedItems = BuildBlocked(dec.Plan);
                FinishBlocked(dec.Why ?? "nothing left the plugin can do on its own");
                break;
        }
    }

    /// <summary>The blocked shopping list for the snapshot (names resolved; est. gil for market items; vendor NPC + coords where placed).</summary>
    private List<BlockedItem> BuildBlocked(DispatchPlan.Plan plan)
    {
        var list = new List<BlockedItem>();
        foreach (var m in plan.Market)
            list.Add(new BlockedItem(StepKind.Market, m.ItemId, Name(m.ItemId), m.Quantity,
                _plugin.Catalog.UnitCost(m.ItemId) is { } u ? u * m.Quantity : null, null));
        // One grouping for the whole vendor list, shared with the chat block (card t_731ea0e7). Resolving each row
        // on its own would put a DIFFERENT vendor in the Run tab than the one the chat line and the map flag named,
        // because grouping trades a little distance for fewer stops - which is exactly the split this card removes.
        if (plan.Vendor.Count > 0)
        {
            var whereByItem = new Dictionary<uint, string>();
            foreach (var (loc, items) in Vendors.Plan(plan.Vendor.Select(p => (p.ItemId, p.Quantity)).ToList(), out _, Here()))
                foreach (var (itemId, _) in items)
                    whereByItem[itemId] = $"{loc.NpcName} ({loc.TerritoryName} {loc.MapCoords.X:0.0}, {loc.MapCoords.Y:0.0})";
            foreach (var v in plan.Vendor)
                list.Add(new BlockedItem(StepKind.Vendor, v.ItemId, Name(v.ItemId), v.Quantity, null,
                    whereByItem.GetValueOrDefault(v.ItemId)));
        }
        foreach (var m in plan.Manual)
            list.Add(new BlockedItem(StepKind.Manual, m.ItemId, Name(m.ItemId), m.Quantity, null,
                string.Join("/", m.Sources.Where(s => s != SourceKind.OnHand))));
        foreach (var v in plan.Ventures)
            list.Add(new BlockedItem(StepKind.Venture, v.ItemId, Name(v.ItemId), v.Quantity, null, v.Match.Retainer.Name));
        foreach (var r in plan.Retrievals)
            list.Add(new BlockedItem(StepKind.Retrieve, r.ItemId, Name(r.ItemId), r.Quantity, null, r.Places));
        return list;
    }

    private static string ReadableBlocked(string verb, uint itemId, int quantity) => $"needs {verb} #{itemId} x{quantity}";

    // ---------------------------------------------------------------- the run

    private void Enter(Phase p)
    {
        Current = p;
        _phaseClock.Restart();
        _nextPoll = DateTime.MinValue;
        if (p is Phase.WaitGather or Phase.BatchWait or Phase.WaitRetrieve or Phase.WaitCraftEnd or Phase.WaitCraftStart)
            _nextHeartbeat = DateTime.UtcNow.AddMinutes(3);
        Snap();
    }

    private void SetStatus(string status)
    {
        Status = status;
        Snap();
    }

    /// <summary>One heartbeat line per wait, at most every 3 minutes, deduped - "still working", never silence.</summary>
    private void Heartbeat(string line)
    {
        if (DateTime.UtcNow < _nextHeartbeat || _lastHeartbeat == line) return;
        _lastHeartbeat = line;
        _nextHeartbeat = DateTime.UtcNow.AddMinutes(3);
        Say($"still working: {line}, {RunSnapshot.FormatElapsed(_runClock.Elapsed)} elapsed.");
    }

    private void Tick(IFramework _)
    {
        if (_plan is null || !Running) return;
        try
        {
            switch (Current)
            {
                // ------------------------------------------------------ Retrieve: one batch pass, then per-item
                case Phase.BatchRetrieve:
                    if (!Poll(400)) break;
                    if (Fetch.Busy()) { SetStatus("waiting for Artisan's retainer queue"); break; }

                    // Queue the whole wave's demand as one session. A refusal here (unavailable overload, nothing
                    // queued) just falls through to the per-item path, which still moves what it can.
                    var batchErr = Fetch.BeginBatch(_batchCrafts);
                    if (batchErr is not null)
                    {
                        _log.Information("batch retainer fetch not queued: {Why}", batchErr);
                        Enter(Phase.Retrieve);
                        break;
                    }
                    Say("fetching the cart's materials from your retainers in one pass - stay by the bell.");
                    Enter(Phase.BatchWait);
                    break;

                case Phase.BatchWait:
                    if (!Poll(500)) break;
                    if (_phaseClock.ElapsedMilliseconds < 1500) break;
                    if (Fetch.Busy())
                    {
                        SetStatus($"retainers: batch fetch ({_phaseClock.Elapsed:m\\:ss})");
                        Heartbeat("retainer session under way - stay by the bell");
                        if (_phaseClock.ElapsedMilliseconds > 600_000)
                        {
                            Fetch.Abort();
                            var timeoutWhy = "Artisan's batch retainer session ran for 10 minutes without finishing (a dialogue may be waiting, or the bell was interrupted)";
                            Say($"gave up the batch fetch: {timeoutWhy}.", error: true);
                            foreach (var r in _retrievals) _unfetched.Add((r, timeoutWhy));
                            _retrievals.Clear();
                            Enter(Phase.Retrieve);
                        }
                        break;
                    }

                    // Artisan going idle is not proof anything moved - count the bags. Artisan withdrew
                    // "recipe demand minus what the bags held at session time"; the delta per demanded item is
                    // what actually arrived. Anything still short stays in the per-item queue (trimmed to the
                    // remainder); demand the plan had not flagged (stock moved between plan and session) is
                    // appended to it.
                    _plugin.Inventory.DropMemo();
                    var batchDemand = new Dictionary<uint, int>();
                    foreach (var id in _batchCrafts)
                    {
                        var row = _graph?.Row(id);
                        if (row is null) continue;
                        foreach (var (itemId, amount) in row.Ingredients)
                            batchDemand[itemId] = batchDemand.GetValueOrDefault(itemId) + amount;
                    }
                    foreach (var (itemId, need) in batchDemand)
                    {
                        var arrived = Math.Max(0, _plugin.Inventory.CountInBags(itemId) - _batchBefore.GetValueOrDefault(itemId));
                        if (arrived > 0) _batchFetched++;
                        var left = need - _plugin.Inventory.CountInBags(itemId);
                        if (left <= 0) { _retrievals = new Queue<DispatchPlan.Retrieve>(_retrievals.Where(r => r.ItemId != itemId)); continue; }
                        var planned = _retrievals.FirstOrDefault(r => r.ItemId == itemId);
                        if (planned is not null)
                            _retrievals = new Queue<DispatchPlan.Retrieve>(
                                _retrievals.Where(r => r.ItemId != itemId)
                                    .Prepend(planned with { Quantity = Math.Min(planned.Quantity, left) }));
                        else
                            _retrievals.Enqueue(new DispatchPlan.Retrieve(itemId, left, DispatchPlan.PlacesFor(_plugin.Inventory.StoredWhere(itemId), left)));
                    }
                    _log.Information("batch retainer pass done: {Fetched} material(s) moved, {Left} left for the per-item pass", _batchFetched, _retrievals.Count);
                    if (_batchFetched > 0) _waveProgress = true;
                    if (_retrievals.Count > 0)
                        Say($"retainer pass done - {_retrievals.Count} material{(_retrievals.Count == 1 ? "" : "s")} still short, checking the retainers again.");
                    _batchCrafts = Array.Empty<uint>();
                    Enter(Phase.Retrieve);
                    break;

                case Phase.Retrieve:
                    if (_retrievals.Count == 0) { AfterRetrieve(); break; }
                    if (!Poll(400)) break;
                    if (Fetch.Busy()) { SetStatus("waiting for Artisan's retainer queue"); break; }

                    _fetching = _retrievals.Dequeue();
                    _plugin.Inventory.DropMemo();

                    // Only the retainers are reachable this way: the saddlebag, the armoury and the glamour dresser
                    // are not summoning-bell inventories. Ask Artisan what it can actually see before promising.
                    var onRetainers = Fetch.Available(_fetching.ItemId);
                    if (onRetainers <= 0)
                    {
                        var why = $"no retainer is holding any in its bags ({_fetching.Detail}) - a summoning bell cannot reach a market-board listing, the saddlebag, the armoury chest or the glamour dresser";
                        // The twelve-line wall (card t_35be7be5): this used to print the whole multi-clause `why` at
                        // ERROR level once per short material, so a 12-material cart produced twelve near-identical
                        // red paragraphs and the actual instruction was nowhere. Now one short line at normal level -
                        // silence during a long run reads as a hang, so the per-item line stays - and the actionable
                        // "pull N off retainer X" instruction is printed ONCE at the end by ReportBlockedListings.
                        // The full `why` is still what lands in _unfetched, so /lcraft blocked and the Run tab keep it.
                        Say(BlockedListings.RefusalLine(Name(_fetching.ItemId), _fetching));
                        TrackStep(StepKind.Retrieve, _fetching.ItemId, _fetching.Quantity, StepState.Failed, why);
                        _unfetched.Add((_fetching, why));
                        _fetching = null;
                        break;
                    }

                    var want = Math.Min(_fetching.Quantity, onRetainers);
                    _fetchBefore = _plugin.Inventory.CountInBags(_fetching.ItemId);
                    var ferr = Fetch.Begin(_fetching.ItemId, want);
                    if (ferr is not null)
                    {
                        Say($"could not start the retainer fetch for {Name(_fetching.ItemId)}: {ferr}.", error: true);
                        TrackStep(StepKind.Retrieve, _fetching.ItemId, _fetching.Quantity, StepState.Failed, ferr);
                        _unfetched.Add((_fetching, ferr));
                        _fetching = null;
                        break;
                    }
                    Say($"fetching {Name(_fetching.ItemId)} x{want} from {_fetching.Places} - stay by the bell.");
                    TrackStep(StepKind.Retrieve, _fetching.ItemId, want, StepState.Running, ext: "retainer session");
                    SetStatus($"retainer: {Name(_fetching.ItemId)} x{want}");
                    Enter(Phase.WaitRetrieve);
                    break;

                case Phase.WaitRetrieve:
                    if (!Poll(500)) break;
                    if (_phaseClock.ElapsedMilliseconds < 1500) break;      // let Artisan's queue spin up before believing !IsBusy
                    if (Fetch.Busy())
                    {
                        SetStatus($"retainer: {Name(_fetching!.ItemId)} ({_phaseClock.Elapsed:m\\:ss})");
                        Heartbeat($"retainer session ({Name(_fetching.ItemId)})");
                        if (_phaseClock.ElapsedMilliseconds > 240_000)
                        {
                            Fetch.Abort();
                            var why = "Artisan's retainer session ran for 4 minutes without finishing (a dialogue may be waiting, or the bell was interrupted)";
                            Say($"gave up fetching {Name(_fetching.ItemId)}: {why}.", error: true);
                            TrackStep(StepKind.Retrieve, _fetching.ItemId, _fetching.Quantity, StepState.Failed, why);
                            _unfetched.Add((_fetching, why));
                            _fetching = null;
                            Enter(Phase.Retrieve);
                        }
                        break;
                    }

                    // Artisan going idle proves nothing (the same lesson as the craft loop): count the bags.
                    _plugin.Inventory.DropMemo();
                    var got = Math.Max(0, _plugin.Inventory.CountInBags(_fetching!.ItemId) - _fetchBefore);
                    if (got >= _fetching.Quantity)
                    {
                        _fetchedOk++;
                        _waveProgress = true;
                        Say($"retrieved {Name(_fetching.ItemId)} x{got} into your bags.");
                        TrackStep(StepKind.Retrieve, _fetching.ItemId, _fetching.Quantity, StepState.Done);
                    }
                    else
                    {
                        // RestockFromRetainers stops as soon as one retainer's pass satisfies its own bag check, so a
                        // partial pull is expected across several retainers. Go back for the remainder, bounded.
                        var tries = _fetchTries.GetValueOrDefault(_fetching.ItemId) + 1;
                        _fetchTries[_fetching.ItemId] = tries;
                        var left = _fetching.Quantity - got;
                        if (got > 0 && tries < 4)
                        {
                            _waveProgress = true;
                            Say($"retrieved {Name(_fetching.ItemId)} x{got}, {left} still to go - going back to the retainers.");
                            TrackStep(StepKind.Retrieve, _fetching.ItemId, _fetching.Quantity, StepState.Running);
                            _retrievals.Enqueue(_fetching with { Quantity = left });
                        }
                        else
                        {
                            var why = got > 0
                                ? $"only {got} of {_fetching.Quantity} came back after {tries} attempt{(tries == 1 ? "" : "s")} (bag space? the rest may be in {_fetching.Places})"
                                : $"nothing came back from the retainers (bags full, or the stock is in {_fetching.Places} rather than on a retainer)";
                            Say($"could not fully retrieve {Name(_fetching.ItemId)}: {why}.", error: true);
                            TrackStep(StepKind.Retrieve, _fetching.ItemId, _fetching.Quantity, StepState.Failed, why);
                            _unfetched.Add((_fetching with { Quantity = left }, why));
                        }
                    }
                    _fetching = null;
                    Enter(Phase.Retrieve);
                    break;

                // ------------------------------------------------------------------ the original channels
                case Phase.Ventures:
                    if (_plan.Ventures.Count > 0)
                    {
                        SetStatus("ARC ventures");
                        var n = Arc.Dispatch(_plan.VentureDictionary(), _plugin.Player.ContentId, Name);
                        if (n < 0) Say("continuing without the venture hand-off.");
                        else foreach (var v in _plan.Ventures) TrackStep(StepKind.Venture, v.ItemId, v.Quantity, StepState.Pending, "queued with ARControl (returns in hours)");
                    }
                    Enter(Phase.Gathers);
                    break;

                case Phase.Gathers:
                    if (_plan.Gathers.Count > 0)
                    {
                        SetStatus("GBR gather list");
                        var n = Gbr.Dispatch(_plan.GatherDictionary(), Name);
                        if (n > 0)
                        {
                            _plugin.Inventory.DropMemo();
                            _gatherList = _plan.Gathers.Select(g => (g.ItemId, g.Quantity)).ToList();
                            _gatherBefore = _gatherList.ToDictionary(g => g.ItemId, g => _plugin.Inventory.CountInBags(g.ItemId));
                            foreach (var g in _plan.Gathers) TrackStep(StepKind.Gather, g.ItemId, g.Quantity, StepState.Running);
                            _gatherStall.Reset();
                            Enter(Phase.WaitGather);
                            SetStatus("waiting for GBR");
                            break;
                        }
                        if (n < 0) { foreach (var g in _plan.Gathers) TrackStep(StepKind.Gather, g.ItemId, g.Quantity, StepState.Failed, "GBR hand-off refused"); Finish(Phase.Failed, "GBR hand-off refused - crafts that needed those materials would fail"); return; }
                    }
                    Enter(Phase.Crafts);
                    break;

                case Phase.WaitGather:
                    if (!Poll(1000)) break;
                    if (_phaseClock.ElapsedMilliseconds < 3000) break;      // let GBR flip Enabled on
                    if (Gbr.IsAutoGatherEnabled())
                    {
                        var s = Gbr.StatusText();
                        SetStatus("GBR: " + (string.IsNullOrEmpty(s) ? (Gbr.IsWaiting() ? "waiting for a node window" : "gathering") : s));
                        // Stall guard (card t_efde145c): GBR's own status text PLUS the gathered items' bag counts,
                        // unchanged for 10 minutes while not merely waiting for a timed node = stuck. Before this the
                        // wait looped on IsAutoGatherEnabled() forever with no timeout.
                        _plugin.Inventory.DropMemo();
                        var signal = s + "|" + string.Join(",", _gatherList.Select(g => $"{g.ItemId}:{_plugin.Inventory.CountInBags(g.ItemId)}"));
                        if (_gatherStall.Observe(signal, DateTime.UtcNow, paused: Gbr.IsWaiting()))
                        {
                            Gbr.Stop();
                            var why = $"GBR made no progress for 10 minutes ({(_gatherList.Count == 0 ? "" : "gathering " + Name(_gatherList[0].ItemId) + ", ")}{Gbr.StatusText()})".TrimEnd(' ', ',');
                            foreach (var g in _gatherList) TrackStep(StepKind.Gather, g.ItemId, g.Quantity, StepState.Failed, "GBR made no progress for 10 min");
                            FinishBlocked(why, _plan);
                            return;
                        }
                        Heartbeat(GatherHeartbeat());
                        break;
                    }

                    _plugin.Inventory.DropMemo();
                    var landed = _gatherList.Count(g => _plugin.Inventory.CountInBags(g.ItemId) > _gatherBefore.GetValueOrDefault(g.ItemId));
                    if (landed > 0) _waveProgress = true;
                    foreach (var g in _gatherList)
                        TrackStep(StepKind.Gather, g.ItemId, g.Quantity,
                            _plugin.Inventory.CountInBags(g.ItemId) > _gatherBefore.GetValueOrDefault(g.ItemId) ? StepState.Done : StepState.Failed,
                            _plugin.Inventory.CountInBags(g.ItemId) > _gatherBefore.GetValueOrDefault(g.ItemId) ? null : "GBR finished without delivering it (list skipped, node unreachable, or bags full)");
                    Say(landed == _gatherList.Count
                        ? $"GBR auto-gather finished - all {_gatherList.Count} item{(_gatherList.Count == 1 ? "" : "s")} landed in your bags."
                        : $"GBR auto-gather finished - {landed} of {_gatherList.Count} gathered item{(landed == 1 ? "" : "s")} landed in your bags.");
                    Enter(Phase.Crafts);
                    break;

                case Phase.Crafts:
                    if (_crafts.Count == 0) { WaveDone(); return; }
                    if (!Artisan.Installed) { Finish(Phase.Failed, "Artisan is not installed or not loaded"); return; }
                    if (!Poll(500)) break;
                    if (Artisan.IsBusy() == true) { SetStatus("waiting for Artisan to go idle"); if (_phaseClock.ElapsedMilliseconds > 120_000) Finish(Phase.Failed, "Artisan stayed busy for 2 minutes"); break; }
                    _current = _crafts.Dequeue();

                    // Guard: never hand Artisan a craft whose materials are not physically in the bags. The plan was
                    // built minutes ago and "owned" counts retainers / saddlebag / armoury; Artisan can only consume
                    // the bags, and it fails silently - the craft simply never starts and we would have called it done.
                    _plugin.Inventory.DropMemo();
                    var recipeRow = _graph?.Row(_current.RecipeId);
                    if (recipeRow is not null && DispatchPlan.BagsShortfall(recipeRow, _current.Crafts, _plugin.Inventory) is { Count: > 0 } shortfall)
                    {
                        _craftsFailed++;
                        var what = string.Join(", ", shortfall.Select(s => $"{Name(s.ItemId)} x{s.Quantity} is not in your bags ({s.Detail})"));
                        Say($"Artisan craft of {Name(_current.ResultItemId)} refused: {what}.", error: true);
                        Say("retrieve before crafting: " + string.Join("; ", shortfall.Select(s => $"{Name(s.ItemId)} x{s.Quantity} from {s.Places}")) + ".", error: true);
                        TrackStep(StepKind.Craft, _current.ResultItemId, _current.Crafts, StepState.Failed, Readable("needs " + string.Join(", ", shortfall.Select(s => $"retrieve #{s.ItemId} x{s.Quantity} (from {s.Places})"))), recipeId: _current.RecipeId);
                        _deferredAtRun.Add(new DispatchPlan.Deferral(_current.RecipeId, _current.ResultItemId, _current.Crafts,
                            "needs " + string.Join(", ", shortfall.Select(s => $"retrieve #{s.ItemId} x{s.Quantity} (from {s.Places})"))));
                        _current = null;
                        break;
                    }

                    // Measure, don't assume: remember what the bags hold now so WaitCraftEnd can tell whether
                    // anything was actually made.
                    _madeBefore = _plugin.Inventory.CountInBags(_current.ResultItemId);
                    _expected = _current.Crafts * Math.Max(1, recipeRow?.ResultAmount ?? 1);

                    SetStatus($"Artisan: {Name(_current.ResultItemId)} x{_current.Crafts}");
                    var err = Artisan.Craft(_current.RecipeId, _current.Crafts);
                    if (err is not null) { _craftsFailed++; Say($"Artisan refused {Name(_current.ResultItemId)}: {err}", error: true); TrackStep(StepKind.Craft, _current.ResultItemId, _current.Crafts, StepState.Failed, err, recipeId: _current.RecipeId); _current = null; break; }
                    Say($"Artisan: crafting {Name(_current.ResultItemId)} x{_current.Crafts} ({_craftsDone + 1}/{_craftsDone + _craftsFailed + _crafts.Count + 1}).");
                    TrackStep(StepKind.Craft, _current.ResultItemId, _current.Crafts, StepState.Running, recipeId: _current.RecipeId, ext: "Artisan");
                    _craftStall.Reset();
                    Enter(Phase.WaitCraftStart);
                    break;

                case Phase.WaitCraftStart:
                    if (!Poll(250)) break;
                    if (Artisan.IsBusy() == true) { Enter(Phase.WaitCraftEnd); break; }
                    if (_phaseClock.ElapsedMilliseconds > 15_000)
                    {
                        _craftsFailed++;
                        Say($"Artisan did not start {Name(_current!.ResultItemId)} within 15 s (crafting log blocked? wrong job gear set?) - skipping.", error: true);
                        TrackStep(StepKind.Craft, _current.ResultItemId, _current.Crafts, StepState.Failed, "Artisan did not start within 15 s", recipeId: _current.RecipeId);
                        _current = null;
                        Enter(Phase.Crafts);
                    }
                    break;

                case Phase.WaitCraftEnd:
                    if (!Poll(500)) break;
                    if (Artisan.StopRequested()) { Finish(Phase.Failed, "Artisan received a stop request"); return; }
                    if (Artisan.IsBusy() == true)
                    {
                        SetStatus($"Artisan: {Name(_current!.ResultItemId)} x{_current.Crafts} ({_phaseClock.Elapsed:m\\:ss})");
                        Heartbeat($"crafting {Name(_current!.ResultItemId)} x{_current.Crafts} ({_phaseClock.Elapsed:m\\:ss})");
                        // 10-minute cap per craft (card t_efde145c): before this, an Artisan that never went idle
                        // held the dispatcher forever with no timeout.
                        if (_craftStall.Observe("busy", DateTime.UtcNow))
                        {
                            Artisan.Stop();
                            var why = $"Artisan did not finish {Name(_current!.ResultItemId)} within 10 minutes";
                            Say($"{why} - sending a stop request and stopping the run.", error: true);
                            TrackStep(StepKind.Craft, _current.ResultItemId, _current.Crafts, StepState.Failed, why, recipeId: _current.RecipeId);
                            Finish(Phase.Failed, why);
                        }
                        break;
                    }

                    // Artisan going idle is not proof it made anything. Count the result in the bags and compare.
                    _plugin.Inventory.DropMemo();
                    var after = _plugin.Inventory.CountInBags(_current!.ResultItemId);
                    var made = Math.Max(0, after - _madeBefore);
                    if (made >= _expected)
                    {
                        _craftsDone++;
                        _made.Add((_current.ResultItemId, made));
                        _waveProgress = true;
                        if (_current.Depth == 0) _loop?.CraftDone(_current.RecipeId, _current.Crafts);
                        TrackStep(StepKind.Craft, _current.ResultItemId, _current.Crafts, StepState.Done, recipeId: _current.RecipeId);
                        // A first-time craft is exactly when the crafting-log flag flips (t_410dee8a): re-read
                        // THAT one flag on the framework thread and patch the cached log set in place, so the
                        // LogComplete column and the not-crafted filter update without a relog and without
                        // rescanning all 13,892 recipes.
                        _plugin.Catalog.NoteCraftCompleted(_current.RecipeId);
                    }
                    else
                    {
                        _craftsFailed++;
                        Say($"Artisan: {Name(_current.ResultItemId)} - expected {_expected}, made {made}.", error: true);
                        if (made > 0)
                        {
                            _made.Add((_current.ResultItemId, made));
                            _waveProgress = true;
                        }
                        TrackStep(StepKind.Craft, _current.ResultItemId, _current.Crafts, StepState.Failed, $"expected {_expected}, made {made}", recipeId: _current.RecipeId);
                    }
                    _current = null;
                    Enter(Phase.Crafts);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "dispatch tick failed in {Phase}", Current);
            Finish(Phase.Failed, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The gather heartbeat line: how many of the wave's items have landed, and the first one still short.</summary>
    private string GatherHeartbeat()
    {
        var done = 0;
        uint? current = null;
        foreach (var (itemId, qty) in _gatherList)
        {
            var have = _plugin.Inventory.CountInBags(itemId);
            if (have >= _gatherBefore.GetValueOrDefault(itemId) + qty) done++;
            else if (current is null) current = itemId;
        }
        return $"gathering {done}/{_gatherList.Count}{(current is { } c ? $" ({Name(c)})" : "")}";
    }

    /// <summary>
    /// The wave is finished. Single-channel runs end here as Done; cart runs hand the measured progress to the loop,
    /// which re-plans from the live bags and either runs the next wave or stops-and-reports (card t_efde145c).
    /// </summary>
    private void WaveDone()
    {
        if (_loop is null || _snap is null) { Finish(Phase.Done, null); return; }
        var dec = _loop.Next(_waveProgress);
        TakeDecision(dec);
    }

    /// <summary>The retrieval queue is empty - straight into the channels; the loop's re-plan at wave end is what picks up deferred-now-runnable crafts.</summary>
    private void AfterRetrieve() => Enter(Phase.Ventures);

    /// <summary>Blocker text with raw <c>#itemId</c> references swapped for item names.</summary>
    private string Readable(string reason) =>
        System.Text.RegularExpressions.Regex.Replace(reason, "#(\\d+)", m => Name(uint.Parse(m.Groups[1].Value)));

    private bool Poll(int everyMs)
    {
        var now = DateTime.UtcNow;
        if (now < _nextPoll) return false;
        _nextPoll = now.AddMilliseconds(everyMs);
        return true;
    }

    // ---------------------------------------------------------------- steps + snapshot

    /// <summary>Add or update one step row (keyed by kind + item + recipe); Running demotes siblings of the same kind back to Pending.</summary>
    private void TrackStep(StepKind kind, uint itemId, int quantity, StepState state, string? reason = null, uint recipeId = 0, string? ext = null)
    {
        var idx = _steps.FindIndex(s => s.Kind == kind && s.ItemId == itemId && s.RecipeId == recipeId);
        if (state == StepState.Running)
            for (var i = 0; i < _steps.Count; i++)
                if (_steps[i].Kind == kind && _steps[i].State == StepState.Running && i != idx)
                    _steps[i] = _steps[i] with { State = StepState.Pending, ExternalStatus = null };
        var step = new RunStep(kind, itemId, Name(itemId), quantity, state, reason, state == StepState.Running ? ext : null, recipeId);
        if (idx < 0) _steps.Add(step);
        else _steps[idx] = step;
    }

    private static string PhaseLabelOf(Phase p) => p switch
    {
        Phase.Idle => "Idle",
        Phase.Retrieve or Phase.WaitRetrieve or Phase.BatchRetrieve or Phase.BatchWait => "Retrieving",
        Phase.Ventures => "Ventures",
        Phase.Gathers or Phase.WaitGather => "Gathering",
        Phase.Crafts or Phase.WaitCraftStart or Phase.WaitCraftEnd => "Crafting",
        Phase.Done => "Done",
        Phase.Failed => "Failed",
        Phase.Blocked => "Blocked",
        _ => p.ToString(),
    };

    private void Snap()
    {
        var state = Current switch
        {
            Phase.Idle => RunState.Idle,
            Phase.Done => RunState.Done,
            Phase.Failed => RunState.Failed,
            Phase.Blocked => RunState.Blocked,
            _ => RunState.Running,
        };
        var elapsed = _runClock.IsRunning ? _runClock.Elapsed : (_endedUtc ?? DateTime.UtcNow) - (_runStartUtc == default ? DateTime.UtcNow : _runStartUtc);
        _snapshot = new RunSnapshot(
            state, Current.ToString(), PhaseLabelOf(Current), Status, _what, _cartNames,
            _runStartUtc, _endedUtc, elapsed, _loop?.Pass == 0 && Current != Phase.Idle ? 1 : _loop?.Pass ?? 0,
            _steps, _blockedItems, _stoppedReason, CanResume);
    }

    // ---------------------------------------------------------------- endings

    /// <summary>Stop-and-report (card t_efde145c option A): the run is Blocked, the plan is held for <see cref="Resume"/>, and the red block is printed here, once, at the END of the run.</summary>
    private void FinishBlocked(string why, DispatchPlan.Plan? blockedPlan = null)
    {
        _stoppedReason = Readable(why);
        // Defect A (card t_35be7be5): the merge lives in Core now and BOTH endings call it - this path used to
        // carry its own inline copy while Finish carried none at all.
        _blockedItems = BlockedListings.MergeIntoBlocked(_blockedItems, _unfetched, Name).ToList();
        for (var i = 0; i < _steps.Count; i++)
            if (_steps[i].State is StepState.Pending or StepState.Running)
                _steps[i] = _steps[i] with { State = StepState.Blocked, ExternalStatus = null };
        Current = Phase.Blocked;
        Status = $"blocked: {Readable(why)}";
        PrintBlockedBlock(blockedPlan ?? _plan ?? new DispatchPlan.Plan([], [], [], [], [], [], []), why);
        // Defect A (card t_35be7be5): the SAME actionable summary on both endings. This path already named the
        // retainers via _blockedItems above; the summary adds the "how many units to pull off sale, grouped by
        // retainer" instruction and the bell walk, and is the one place either path prints them.
        ReportBlockedListings();
        _plan = null;
        _current = null;
        _fetching = null;
        _crafts.Clear();
        _retrievals.Clear();
        _batchCrafts = Array.Empty<uint>();
        _gatherList.Clear();
        _runClock.Stop();
        _endedUtc = DateTime.UtcNow;
        _plugin.Inventory.DropMemo();
        // Blocked run over: restore the idle 2 s debounce (t_410dee8a), same as Finish. Blocked does NOT route
        // through Finish - the player is meant to press Resume - so this exit path restores the window itself.
        _plugin.Inventory.SetDispatchRunning(false);
        // No forced catalog recompute at run end (t_9f646f4c): the debounced AllaganTools inventory event
        // refreshes the catalog a couple of seconds later without freezing the window. Only the Degraded
        // fallback (no AllaganTools -> no event path at all) still invalidates explicitly.
        if (_plugin.Inventory.Degraded) _plugin.Catalog.Invalidate();
        Snap();
    }

    /// <summary>The one red block at the END of a blocked run (card t_efde145c option A): what to buy, where, then "press Resume". Called from <see cref="FinishBlocked"/> only - never twice.</summary>
    private void PrintBlockedBlock(DispatchPlan.Plan plan, string why)
    {
        Say($"stopped - {Readable(why)} ({Summarise()}).", error: true);
        if (plan.Market.Count > 0)
            Lifestream.GoToMarket(plan.Market.Select(p => (p.ItemId, p.Quantity)).ToList(), Name, _plugin.Catalog.UnitCost, teleport: false);
        if (plan.Vendor.Count > 0)
        {
            var groups = Vendors.Plan(plan.Vendor.Select(p => (p.ItemId, p.Quantity)).ToList(), out var unlocated, Here());
            foreach (var (where, items) in groups) Lifestream.GoToVendor(where, items, Name, teleport: false);
            if (unlocated.Count > 0) Say("gil-vendor items with no placed vendor: " + string.Join(", ", unlocated.Select(u => $"{Name(u.ItemId)} x{u.Quantity}")), error: true);
        }
        if (plan.Manual.Count > 0)
            Say("needs a manual source: " + string.Join(", ", plan.Manual.Select(m => $"{Name(m.ItemId)} x{m.Quantity} ({string.Join("/", m.Sources.Where(s => s != SourceKind.OnHand).Select(s => s.ToString()))})")), error: true);
        if (plan.Ventures.Count > 0)
            Say("still out with a retainer (ventures take hours): " + string.Join(", ", plan.Ventures.Select(v => $"{Name(v.ItemId)} x{v.Quantity} ({v.Match.Retainer.Name})")), error: true);
        if (plan.Retrievals.Count > 0)
            foreach (var r in plan.Retrievals) Say($"retrieve by hand: {Name(r.ItemId)} x{r.Quantity} from {r.Places} ({r.Detail}).", error: true);
        Say("then press Resume (or /lcraft resume) to continue the same cart.", error: true);
    }

    /// <summary>The wave loop said the cart is finished (or a single-channel run ended on its own).</summary>
    private void Finish(Phase end, string? why)
    {
        var plan = _plan;
        _stoppedReason = why;
        Current = end;
        Status = end == Phase.Done ? "done" : $"stopped: {why}";
        for (var i = 0; i < _steps.Count; i++)
            if (_steps[i].State is StepState.Pending or StepState.Running)
                _steps[i] = _steps[i] with { State = end == Phase.Done ? StepState.Done : StepState.Failed, ExternalStatus = null };
        // Defect A (card t_35be7be5): this path used to render ONLY ", N could not be retrieved" while the retainer
        // names, item ids and quantities sat in _unfetched and were discarded - Joey's 2026-09-05 22:44 run FINISHED
        // (0 manual, 20 deferred) and took exactly this branch. Same Core merge FinishBlocked calls, so the two
        // endings cannot drift apart again; /lcraft status and the Run tab now agree with the chat block. Rendered
        // under "still outstanding:" rather than "blocked - to continue:" because the run did finish (RunReport).
        _blockedItems = BlockedListings.MergeIntoBlocked(_blockedItems, _unfetched, Name).ToList();
        if (plan is not null)
        {
            foreach (var d in _deferredAtRun)
                Say($"not crafted: {Name(d.ResultItemId)} x{d.Crafts} - {Readable(d.Reason)}.", error: true);

            // "crafts finished M/N" is measured, not assumed: M counted only the crafts whose result actually
            // appeared in the bags (see WaitCraftEnd), and "retrieved M/N" only the fetches whose material actually
            // appeared in the bags (see BatchWait / WaitRetrieve). A refusal or a silent no-op lands in the failed
            // counters.
            var retrieved = _retrievalsPlanned > 0 ? $"retrieved {_fetchedOk}/{_retrievalsPlanned}, " : "";
            var stuck = _unfetched.Count > 0 ? $", {_unfetched.Count} could not be retrieved" : "";
            if (end == Phase.Done && _loop is not null)
            {
                var lines = _loop.Lines.Count;
                Say($"done - {lines} cart line{(lines == 1 ? "" : "s")} finished, {plan.Ventures.Count} venture item{(plan.Ventures.Count == 1 ? "" : "s")} to ARC, {plan.Gathers.Count} to GBR, {retrieved}{_craftsDone} craft{( _craftsDone == 1 ? "" : "s")} made{(_craftsFailed > 0 ? $", {_craftsFailed} failed" : "")}{stuck}.",
                    error: _craftsFailed > 0 || _unfetched.Count > 0);
            }
            else if (end == Phase.Done)
                Say($"done - {plan.Ventures.Count} venture item{(plan.Ventures.Count == 1 ? "" : "s")} to ARC, {plan.Gathers.Count} to GBR, {retrieved}crafts finished {_craftsDone}/{plan.Crafts.Count}{(_craftsFailed > 0 ? $", {_craftsFailed} failed" : "")}{stuck}.",
                    error: _craftsFailed > 0 || _unfetched.Count > 0);
            else
                Say($"dispatch stopped: {why} ({retrieved}crafts finished {_craftsDone}/{plan.Crafts.Count}).", error: true);
            if (_made.Count > 0 && _plugin.Config.PriceMatchAfterCraft && _plugin.GameData is { } gd)
                PriceMatch.AfterCraft(_made, Name, gd.IsMarketable);
        }
        // Defect A: the actionable summary, same call as FinishBlocked. Outside the `plan is not null` block so a
        // run that ended without a plan still reports what it could not fetch. Silent when nothing was blocked.
        ReportBlockedListings();
        _plan = null;
        _current = null;
        _fetching = null;
        if (end == Phase.Done || _loop is null) { _loop = null; _snap = null; }   // Failed keeps them for Resume
        _crafts.Clear();
        _retrievals.Clear();
        _batchCrafts = Array.Empty<uint>();
        _gatherList.Clear();
        _runClock.Stop();
        _endedUtc = DateTime.UtcNow;
        _plugin.Inventory.DropMemo();
        // Run over: restore the idle 2 s debounce (t_410dee8a). SetDispatchRunning also snaps a pending
        // deadline forward so the post-run refresh is not held an extra few seconds by the 10 s window.
        _plugin.Inventory.SetDispatchRunning(false);
        // Same as FinishBlocked: let the debounced inventory event refresh the catalog; force it only when the
        // AllaganTools event path does not exist (Degraded).
        if (_plugin.Inventory.Degraded) _plugin.Catalog.Invalidate();
        Snap();
    }

    private string Summarise() =>
        $"{_craftsDone} craft{(_craftsDone == 1 ? "" : "s")} made{(_craftsFailed > 0 ? $", {_craftsFailed} failed" : "")}{(_loop is not null ? $", pass {_loop.Pass}" : "")}";

    // ---------------------------------------------------------------- blocked listings (card t_35be7be5, Tier 1)

    /// <summary>
    /// The last run's blocked-listing summary, for <c>/lcraft blocked</c>. Kept after the run ends.
    /// </summary>
    public BlockedListings.Summary LastBlockedListings => _lastBlockedListings;

    /// <summary>What the last run was, for the <c>/lcraft blocked</c> wording.</summary>
    public string LastBlockedWhat => _lastBlockedWhat;

    /// <summary>
    /// The one actionable end-of-run block, printed identically by <see cref="Finish"/> and
    /// <see cref="FinishBlocked"/> (Defect A: the finishing path used to render only a bare count while the retainer
    /// names sat in <see cref="_unfetched"/> and were discarded). Also stashes the summary for
    /// <c>/lcraft blocked</c>, and fires the summoning-bell walk when there is actually something to unlist.
    /// <para>
    /// Called exactly once per run, on whichever path ends it. Silent on a clean run: no summary, no bell walk.
    /// </para>
    /// </summary>
    private void ReportBlockedListings()
    {
        _lastBlockedWhat = string.IsNullOrEmpty(_what) ? "the cart" : _what;
        var summary = BlockedListings.Summarise(_unfetched, Name);
        _lastBlockedListings = summary;
        if (summary.IsEmpty) return;

        // Not an error-channel line: it is the answer, not a failure, and the twelve near-identical red warnings
        // are exactly what this collapses.
        foreach (var line in BlockedListings.Lines(summary, _lastBlockedWhat)) Say(line);
        _log.Information("blocked listings: {Retainers} retainer group(s), {Units} unit(s), {Others} other blocked item(s)",
            summary.Retainers.Count, summary.TotalUnits, summary.Others.Count);

        WalkToBell(summary);
    }

    /// <summary>
    /// Send the player to the nearest summoning bell so the pull can actually happen (card t_35be7be5).
    /// <para>
    /// Gated by <see cref="Configuration.WalkToBellWhenBlocked"/> (default ON) and fired ONLY from
    /// <see cref="ReportBlockedListings"/> - i.e. only when the run has ended AND there is a non-empty
    /// listing-blocked summary. Never mid-craft, never on a clean run, never when the only trouble was a timeout.
    /// </para>
    /// <para>
    /// It reuses the EXISTING Lifestream path and adds no navigation of its own: <c>Lifestream.ExecuteCommand("mb")</c>
    /// (= <c>/li mb</c>, "go to market board" - verified in the installed Lifestream 2.5.4.16 command help). Market
    /// boards and summoning bells share the same aetheryte plaza, so that is the trip. Lifestream exposes no
    /// bell-specific IPC (checked: no "bell" literal in its assembly at all), and the card is explicit that if no
    /// suitable existing target call exists we print the destination and skip the walk rather than inventing
    /// navigation - hence no vnavmesh, and hence the graceful print-only fallback below.
    /// </para>
    /// </summary>
    private void WalkToBell(BlockedListings.Summary summary)
    {
        if (!summary.HasListings) return;
        if (!_plugin.Config.WalkToBellWhenBlocked)
        {
            Say("(walk to a summoning bell is switched off in the settings - go to one yourself.)");
            return;
        }
        if (!Lifestream.Installed)
        {
            Say("Lifestream is not installed - walk to a summoning bell yourself (they stand with the market boards at any aetheryte plaza).");
            return;
        }
        if (Lifestream.IsBusy() == true)
        {
            Say("Lifestream is busy, so no walk - head to a summoning bell yourself (they stand with the market boards).");
            return;
        }
        // Print-and-go through the existing market destination, with no shopping list - just the travel.
        var err = Lifestream.GoToMarketBoard();
        if (err is not null) { Say("could not start the walk - head to a summoning bell yourself.", error: true); return; }
        Say("heading to the nearest market board (the summoning bells stand with it) so you can pull those listings.");
    }

    private void Say(string text, bool error = false)
    {
        var line = "[LazyCrafter] " + text;
        if (error) { _log.Warning("{Line}", line); _chat.PrintError(line); }
        else { _log.Information("{Line}", line); _chat.Print(line); }
    }
}
