using System.Diagnostics;
using Dalamud.Plugin.Services;
using LazyCrafter.Adapters.Dispatch;
using LazyCrafter.Catalog;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Adapters;

/// <summary>
/// Runs a <see cref="DispatchPlan.Plan"/> against the live plugins (Plan A—Phase 5 task 6):
/// <b>0.1.3.0:</b> when the plan needs stock fetched out of the retainers, ONE batch Artisan session first -
/// bell once, every queued recipe's missing materials withdrawn (see <see cref="RetainerFetch.BeginBatch"/>) -
/// measured against the bags, then the plan re-built, and only then any remainder falls back to the 0.1.2.0
/// per-item sessions.
/// Ventures start first (card t_63b845ad), then retainers are asynchronous so ventures start next, gathering drives
/// the character so crafting waits for it, then each craft is handed to Artisan in depth-first order, polling
/// <c>Artisan.IsBusy</c> between recipes. Vendor / market items are printed as shopping lists with map links (the
/// per-leaf buttons do the teleport; teleporting in the middle of a cart run would fight GBR). Everything happens on
/// <see cref="IFramework.Update"/> in small steps; nothing blocks, nothing runs in Draw. <c>/lcraft stop</c> or the
/// Stop button aborts (retainer queue aborted, GBR off, Artisan stop request).
/// </summary>
public sealed class DispatchService : IDisposable
{
    public enum Phase { Idle, Retrieve, WaitRetrieve, Ventures, Gathers, WaitGather, Crafts, WaitCraftStart, WaitCraftEnd, Done, Failed, BatchRetrieve, BatchWait }

    private readonly Plugin _plugin;
    private readonly IFramework _framework;
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;

    public ArtisanDispatch Artisan { get; }
    public GbrDispatch Gbr { get; }
    public ArcDispatch Arc { get; }
    public LifestreamDispatch Lifestream { get; }
    public DagobertDispatch Dagobert { get; }
    public RetainerFetch Fetch { get; }
    public ReflectionGuard Guard { get; }

    private RecipeGraph? _graph;
    private VentureResolver? _ventures;
    private VendorLocator? _vendors;

    private DispatchPlan.Plan? _plan;
    private Queue<DispatchPlan.Craft> _crafts = new();
    private DispatchPlan.Craft? _current;
    private readonly List<(uint ItemId, int Quantity)> _made = new();
    /// <summary>Crafts refused at execution time by the bags guard, reported in the summary alongside the plan's own deferrals.</summary>
    private readonly List<DispatchPlan.Deferral> _deferredAtRun = new();
    /// <summary>Bag count of the current craft's result immediately before <c>Artisan.CraftItem</c>, and how many units we expect it to add.</summary>
    private int _madeBefore, _expected;

    // ---- retrieval state (card t_63b845ad)
    /// <summary>The cart this run came from, so the plan can be rebuilt once the fetched stock is in the bags.</summary>
    private CatalogSnapshot? _snap;
    private Queue<DispatchPlan.Retrieve> _retrievals = new();
    private DispatchPlan.Retrieve? _fetching;
    /// <summary>Materials we could not get into the bags, reported once at the end with the reason.</summary>
    private readonly List<(DispatchPlan.Retrieve Item, string Why)> _unfetched = new();
    private readonly Dictionary<uint, int> _fetchTries = new();
    private int _fetchBefore, _fetchedOk, _retrievalsPlanned;
    // ---- 0.1.3.0: the one batch session that runs before the per-item fallback.
    /// <summary>Recipe rows queued into the batch <c>RestockFromRetainers(NewCraftingList)</c> session.</summary>
    private IReadOnlyList<uint> _batchCrafts = Array.Empty<uint>();
    /// <summary>Bags per demanded item immediately before the batch session, so <see cref="BatchWait"/> measures the delta.</summary>
    private readonly Dictionary<uint, int> _batchBefore = new();
    /// <summary>How many materials the batch session actually delivered into the bags (measured, counted like <see cref="_fetchedOk"/>).</summary>
    private int _batchFetched;

    private readonly Stopwatch _phaseClock = new();
    private DateTime _nextPoll = DateTime.MinValue;
    private int _craftsDone, _craftsFailed;

    public Phase Current { get; private set; } = Phase.Idle;
    public string Status { get; private set; } = "idle";
    public bool Running => Current is not (Phase.Idle or Phase.Done or Phase.Failed);

    public DispatchService(Plugin plugin, IFramework framework, IChatGui chat, IPluginLog log)
    {
        _plugin = plugin;
        _framework = framework;
        _chat = chat;
        _log = log;
        Guard = new ReflectionGuard(Plugin.Pi, chat, log);
        Artisan = new ArtisanDispatch(Plugin.Pi, log);
        Gbr = new GbrDispatch(Plugin.Pi, Guard, chat, log);
        Arc = new ArcDispatch(Guard, chat, log);
        Lifestream = new LifestreamDispatch(Plugin.Pi, Plugin.GameGui, chat, log);
        Dagobert = new DagobertDispatch(Plugin.Pi, chat);
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
        _graph ??= new RecipeGraph(gd);
        _ventures ??= new VentureResolver(gd);
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

    /// <summary>Build the plan for the current cart without executing it (for the cart panel preview / <c>/lcraft plan</c>).</summary>
    public DispatchPlan.Plan? PlanFor(CatalogSnapshot snap)
    {
        if (!EnsureCore()) return null;
        var lines = snap.Cart.Select(l => new DispatchPlan.Line(l.Assessment, l.Crafts)).ToList();
        return DispatchPlan.Build(lines, snap.CartTotals.Totals, _graph!, _ventures!, _plugin.Player.Retainers, _plugin.Player.GatheredItems, _plugin.Inventory);
    }

    /// <summary>Dispatch the whole cart. Framework thread (button handler / command).</summary>
    public void DispatchCart(CatalogSnapshot snap)
    {
        if (Running) { Say($"a dispatch is already running ({Status}); /lcraft stop first.", error: true); return; }
        if (snap.Cart.Count == 0) { Say("the cart is empty.", error: true); return; }
        var plan = PlanFor(snap);
        if (plan is null) return;
        _snap = snap;
        Start(plan, "cart");
    }

    /// <summary>One sub-craft from the ingredient tree: Artisan only.</summary>
    public void CraftOne(uint recipeId, int crafts)
    {
        if (Running) { Say($"a dispatch is already running ({Status}); /lcraft stop first.", error: true); return; }
        if (!EnsureCore()) return;
        var row = _graph!.Row(recipeId);
        if (row is null) { Say($"unknown recipe {recipeId}.", error: true); return; }
        _snap = null;
        Start(new DispatchPlan.Plan([], [], [new DispatchPlan.Craft(recipeId, row.ResultItemId, crafts, 0, false)], [], [], [], []), Name(row.ResultItemId));
    }

    /// <summary>One gather from the ingredient tree: GBR only.</summary>
    public void GatherOne(uint itemId, int quantity)
    {
        if (Running) { Say($"a dispatch is already running ({Status}); /lcraft stop first.", error: true); return; }
        _snap = null;
        Start(new DispatchPlan.Plan([], [new DispatchPlan.Gather(itemId, quantity, SourceKind.RegularNode)], [], [], [], [], []), Name(itemId));
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
        var where = _plugin.Inventory.StoredWhere(itemId);
        _snap = null;
        Start(new DispatchPlan.Plan([], [], [], [], [], [], [], [new DispatchPlan.Retrieve(itemId, quantity, where)]), Name(itemId));
    }

    /// <summary>Fetch a whole set of materials and stop there - no ventures, no gathers, no crafts (<c>/lcraft fetch</c>).</summary>
    public void RetrieveOnly(IReadOnlyList<DispatchPlan.Retrieve> retrievals)
    {
        if (Running) { Say($"a dispatch is already running ({Status}); /lcraft stop first.", error: true); return; }
        if (retrievals.Count == 0) { Say("nothing to fetch."); return; }
        _snap = null;
        Start(new DispatchPlan.Plan([], [], [], [], [], [], [], retrievals), $"{retrievals.Count} material{(retrievals.Count == 1 ? "" : "s")}");
    }

    /// <summary>Teleport to the nearest vendor of the item and flag it (Lifestream).</summary>
    public void VendorOne(uint itemId, int quantity)
    {
        var where = Vendors.Find(itemId);
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
        Finish(Phase.Failed, why);
    }

    // ---------------------------------------------------------------- the run

    private void Start(DispatchPlan.Plan plan, string what)
    {
        _plan = plan;
        _crafts = new Queue<DispatchPlan.Craft>(plan.Crafts);
        _made.Clear();
        _deferredAtRun.Clear();
        _unfetched.Clear();
        _fetchTries.Clear();
        _batchBefore.Clear();
        _craftsDone = _craftsFailed = 0;
        _madeBefore = _expected = 0;
        _fetchBefore = _fetchedOk = 0;
        _batchFetched = 0;
        _batchCrafts = Array.Empty<uint>();
        _current = null;
        _fetching = null;
        _retrievals = new Queue<DispatchPlan.Retrieve>(plan.Retrievals);
        _retrievalsPlanned = plan.Retrievals.Count;
        Say($"dispatching {what}: {plan.Ventures.Count} venture, {plan.Gathers.Count} gather, {plan.Crafts.Count} craft, {plan.Vendor.Count} vendor, {plan.Market.Count} market, {plan.Manual.Count} manual, {plan.Deferred.Count} deferred, {plan.Retrievals.Count} to retrieve.");
        _log.Information("dispatch plan for {What}: ventures=[{V}] gathers=[{G}] crafts=[{C}] vendor=[{Ve}] market=[{M}] manual=[{Ma}] deferred=[{D}]", what,
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
        // queue is primed with the cart's recipes - the queued crafts plus deferred crafts whose blockers include a
        // retrieval (<see cref="RetainerBatch.Queue"/>; that is the deferred-craft shape whose stock sat on a
        // retainer). Items with no recipe row, and anything left over afterwards, fall back to the per-item path.
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
            _plugin.Inventory.Invalidate();
            foreach (var id in _batchCrafts)
            {
                var row = _graph!.Row(id)!;
                foreach (var (itemId, _) in row.Ingredients)
                    _batchBefore[itemId] = _plugin.Inventory.CountInBags(itemId);
            }
        }

        // Shopping lists and blockers are informational; print them up front. Deferrals caused purely by a retrieval
        // we are about to perform are NOT printed here - Retrieve re-plans afterwards and reports what is still stuck,
        // so the player is not told a craft is blocked and then told it ran.
        if (plan.Vendor.Count > 0)
        {
            var groups = Vendors.Plan(plan.Vendor.Select(p => (p.ItemId, p.Quantity)).ToList(), out var unlocated);
            foreach (var (where, items) in groups) Lifestream.GoToVendor(where, items, Name, teleport: false);
            if (unlocated.Count > 0) Say("gil-vendor items with no placed vendor: " + string.Join(", ", unlocated.Select(u => $"{Name(u.ItemId)} x{u.Quantity}")));
        }
        if (plan.Market.Count > 0) Lifestream.GoToMarket(plan.Market.Select(p => (p.ItemId, p.Quantity)).ToList(), Name, _plugin.Catalog.UnitCost, teleport: false);
        if (plan.Manual.Count > 0) Say("needs a manual source: " + string.Join(", ", plan.Manual.Select(m => $"{Name(m.ItemId)} x{m.Quantity} ({string.Join("/", m.Sources.Where(s => s != SourceKind.OnHand).Select(s => s.ToString()))})")));
        var willRetrieve = _retrievals.Count > 0;
        if (!willRetrieve)
            foreach (var d in plan.Deferred) Say($"not crafting {Name(d.ResultItemId)} x{d.Crafts} yet - {Readable(d.Reason)}.", error: true);

        if (!plan.HasWork && !willRetrieve) { Finish(Phase.Done, "nothing to hand off"); return; }
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

    private void Enter(Phase p)
    {
        Current = p;
        _phaseClock.Restart();
        _nextPoll = DateTime.MinValue;
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
                    if (Fetch.Busy()) { Status = "waiting for Artisan's retainer queue"; break; }

                    // Queue the whole cart's demand as one session. A refusal here (unavailable overload, nothing
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
                        Status = $"retainers: batch fetch ({_phaseClock.Elapsed:m\\:ss})";
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
                    _plugin.Inventory.Invalidate();
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
                            _retrievals.Enqueue(new DispatchPlan.Retrieve(itemId, left, _plugin.Inventory.StoredWhere(itemId)));
                    }
                    _log.Information("batch retainer pass done: {Fetched} material(s) moved, {Left} left for the per-item pass", _batchFetched, _retrievals.Count);
                    if (_retrievals.Count > 0)
                        Say($"retainer pass done - {_retrievals.Count} material{(_retrievals.Count == 1 ? "" : "s")} still short, checking the retainers again.");
                    _batchCrafts = Array.Empty<uint>();
                    Enter(Phase.Retrieve);
                    break;

                case Phase.Retrieve:
                    if (_retrievals.Count == 0) { AfterRetrieve(); break; }
                    if (!Poll(400)) break;
                    if (Fetch.Busy()) { Status = "waiting for Artisan's retainer queue"; break; }

                    _fetching = _retrievals.Dequeue();
                    _plugin.Inventory.Invalidate();

                    // Only the retainers are reachable this way: the saddlebag, the armoury and the glamour dresser
                    // are not summoning-bell inventories. Ask Artisan what it can actually see before promising.
                    var onRetainers = Fetch.Available(_fetching.ItemId);
                    if (onRetainers <= 0)
                    {
                        var why = $"no retainer is holding any ({_fetching.Detail}) - a summoning bell cannot reach the saddlebag, the armoury chest or the glamour dresser";
                        Say($"cannot fetch {Name(_fetching.ItemId)} x{_fetching.Quantity}: {why}.", error: true);
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
                        _unfetched.Add((_fetching, ferr));
                        _fetching = null;
                        break;
                    }
                    Say($"fetching {Name(_fetching.ItemId)} x{want} from {_fetching.Places} - stay by the bell.");
                    Status = $"retainer: {Name(_fetching.ItemId)} x{want}";
                    Enter(Phase.WaitRetrieve);
                    break;

                case Phase.WaitRetrieve:
                    if (!Poll(500)) break;
                    if (_phaseClock.ElapsedMilliseconds < 1500) break;      // let Artisan's queue spin up before believing !IsBusy
                    if (Fetch.Busy())
                    {
                        Status = $"retainer: {Name(_fetching!.ItemId)} ({_phaseClock.Elapsed:m\\:ss})";
                        if (_phaseClock.ElapsedMilliseconds > 240_000)
                        {
                            Fetch.Abort();
                            var why = "Artisan's retainer session ran for 4 minutes without finishing (a dialogue may be waiting, or the bell was interrupted)";
                            Say($"gave up fetching {Name(_fetching.ItemId)}: {why}.", error: true);
                            _unfetched.Add((_fetching, why));
                            _fetching = null;
                            Enter(Phase.Retrieve);
                        }
                        break;
                    }

                    // Artisan going idle proves nothing (the same lesson as the craft loop): count the bags.
                    _plugin.Inventory.Invalidate();
                    var got = Math.Max(0, _plugin.Inventory.CountInBags(_fetching!.ItemId) - _fetchBefore);
                    if (got >= _fetching.Quantity)
                    {
                        _fetchedOk++;
                        Say($"retrieved {Name(_fetching.ItemId)} x{got} into your bags.");
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
                            Say($"retrieved {Name(_fetching.ItemId)} x{got}, {left} still to go - going back to the retainers.");
                            _retrievals.Enqueue(_fetching with { Quantity = left });
                        }
                        else
                        {
                            var why = got > 0
                                ? $"only {got} of {_fetching.Quantity} came back after {tries} attempt{(tries == 1 ? "" : "s")} (bag space? the rest may be in {_fetching.Places})"
                                : $"nothing came back from the retainers (bags full, or the stock is in {_fetching.Places} rather than on a retainer)";
                            Say($"could not fully retrieve {Name(_fetching.ItemId)}: {why}.", error: true);
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
                        Status = "ARC ventures";
                        var n = Arc.Dispatch(_plan.VentureDictionary(), _plugin.Player.ContentId, Name);
                        if (n < 0) Say("continuing without the venture hand-off.");
                    }
                    Enter(Phase.Gathers);
                    break;

                case Phase.Gathers:
                    if (_plan.Gathers.Count > 0)
                    {
                        Status = "GBR gather list";
                        var n = Gbr.Dispatch(_plan.GatherDictionary(), Name);
                        if (n > 0) { Enter(Phase.WaitGather); Status = "waiting for GBR"; break; }
                        if (n < 0) { Finish(Phase.Failed, "GBR hand-off refused - crafts that needed those materials would fail"); return; }
                    }
                    Enter(Phase.Crafts);
                    break;

                case Phase.WaitGather:
                    if (!Poll(1000)) break;
                    if (_phaseClock.ElapsedMilliseconds < 3000) break;      // let GBR flip Enabled on
                    if (Gbr.IsAutoGatherEnabled())
                    {
                        var s = Gbr.StatusText();
                        Status = "GBR: " + (string.IsNullOrEmpty(s) ? (Gbr.IsWaiting() ? "waiting for a node window" : "gathering") : s);
                        break;
                    }
                    Say("GBR auto-gather finished.");
                    _plugin.Inventory.Invalidate();
                    Enter(Phase.Crafts);
                    break;

                case Phase.Crafts:
                    if (_crafts.Count == 0) { Finish(Phase.Done, null); return; }
                    if (!Artisan.Installed) { Finish(Phase.Failed, "Artisan is not installed or not loaded"); return; }
                    if (!Poll(500)) break;
                    if (Artisan.IsBusy() == true) { Status = "waiting for Artisan to go idle"; if (_phaseClock.ElapsedMilliseconds > 120_000) Finish(Phase.Failed, "Artisan stayed busy for 2 minutes"); break; }
                    _current = _crafts.Dequeue();

                    // Guard: never hand Artisan a craft whose materials are not physically in the bags. The plan was
                    // built minutes ago and "owned" counts retainers / saddlebag / armoury; Artisan can only consume
                    // the bags, and it fails silently - the craft simply never starts and we would have called it done.
                    _plugin.Inventory.Invalidate();
                    var recipeRow = _graph?.Row(_current.RecipeId);
                    if (recipeRow is not null && DispatchPlan.BagsShortfall(recipeRow, _current.Crafts, _plugin.Inventory) is { Count: > 0 } shortfall)
                    {
                        _craftsFailed++;
                        var what = string.Join(", ", shortfall.Select(s => $"{Name(s.ItemId)} x{s.Quantity} is not in your bags ({s.Detail})"));
                        Say($"Artisan craft of {Name(_current.ResultItemId)} refused: {what}.", error: true);
                        Say("retrieve before crafting: " + string.Join("; ", shortfall.Select(s => $"{Name(s.ItemId)} x{s.Quantity} from {s.Places}")) + ".", error: true);
                        _deferredAtRun.Add(new DispatchPlan.Deferral(_current.RecipeId, _current.ResultItemId, _current.Crafts,
                            "needs " + string.Join(", ", shortfall.Select(s => $"retrieve #{s.ItemId} x{s.Quantity} (from {s.Places})"))));
                        _current = null;
                        break;
                    }

                    // Measure, don't assume: remember what the bags hold now so WaitCraftEnd can tell whether
                    // anything was actually made.
                    _madeBefore = _plugin.Inventory.CountInBags(_current.ResultItemId);
                    _expected = _current.Crafts * Math.Max(1, recipeRow?.ResultAmount ?? 1);

                    Status = $"Artisan: {Name(_current.ResultItemId)} x{_current.Crafts}";
                    var err = Artisan.Craft(_current.RecipeId, _current.Crafts);
                    if (err is not null) { _craftsFailed++; Say($"Artisan refused {Name(_current.ResultItemId)}: {err}", error: true); _current = null; break; }
                    Say($"Artisan: crafting {Name(_current.ResultItemId)} x{_current.Crafts} ({_craftsDone + 1}/{_plan.Crafts.Count}).");
                    Enter(Phase.WaitCraftStart);
                    break;

                case Phase.WaitCraftStart:
                    if (!Poll(250)) break;
                    if (Artisan.IsBusy() == true) { Enter(Phase.WaitCraftEnd); break; }
                    if (_phaseClock.ElapsedMilliseconds > 15_000)
                    {
                        _craftsFailed++;
                        Say($"Artisan did not start {Name(_current!.ResultItemId)} within 15 s (crafting log blocked? wrong job gear set?) - skipping.", error: true);
                        _current = null;
                        Enter(Phase.Crafts);
                    }
                    break;

                case Phase.WaitCraftEnd:
                    if (!Poll(500)) break;
                    if (Artisan.StopRequested()) { Finish(Phase.Failed, "Artisan received a stop request"); return; }
                    if (Artisan.IsBusy() == true) { Status = $"Artisan: {Name(_current!.ResultItemId)} x{_current.Crafts} ({_phaseClock.Elapsed:m\\:ss})"; break; }

                    // Artisan going idle is not proof it made anything. Count the result in the bags and compare.
                    _plugin.Inventory.Invalidate();
                    var after = _plugin.Inventory.CountInBags(_current!.ResultItemId);
                    var made = Math.Max(0, after - _madeBefore);
                    if (made >= _expected)
                    {
                        _craftsDone++;
                        _made.Add((_current.ResultItemId, made));
                    }
                    else
                    {
                        _craftsFailed++;
                        Say($"Artisan: {Name(_current.ResultItemId)} - expected {_expected}, made {made}.", error: true);
                        if (made > 0) _made.Add((_current.ResultItemId, made));
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

    /// <summary>
    /// The retrieval queue is empty. If anything actually landed in the bags, rebuild the plan against the new
    /// inventory: crafts that were deferred purely because their materials sat on a retainer become real crafts,
    /// which is the whole point of the exercise. Then carry on into the normal channels.
    /// </summary>
    private void AfterRetrieve()
    {
        if (_fetchedOk + _batchFetched > 0 && _snap is not null)
        {
            _plugin.Inventory.Invalidate();
            var fresh = PlanFor(_snap);
            if (fresh is not null)
            {
                var was = _plan!.Crafts.Count;
                _plan = fresh;
                _crafts = new Queue<DispatchPlan.Craft>(fresh.Crafts);
                Say(fresh.Crafts.Count == was
                    ? $"materials retrieved; {fresh.Crafts.Count} craft{(fresh.Crafts.Count == 1 ? "" : "s")} to run."
                    : $"materials retrieved; {fresh.Crafts.Count} craft{(fresh.Crafts.Count == 1 ? "" : "s")} now ready (was {was}).");
                foreach (var d in fresh.Deferred) Say($"still not crafting {Name(d.ResultItemId)} x{d.Crafts} - {Readable(d.Reason)}.", error: true);
            }
        }
        else if (_plan!.Deferred.Count > 0)
        {
            // Nothing was fetched, so the deferrals we withheld in Start still stand - report them now, once.
            foreach (var d in _plan.Deferred) Say($"not crafting {Name(d.ResultItemId)} x{d.Crafts} yet - {Readable(d.Reason)}.", error: true);
        }
        Enter(Phase.Ventures);
    }

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

    private void Finish(Phase end, string? why)
    {
        var plan = _plan;
        Current = end;
        Status = end == Phase.Done ? "done" : $"stopped: {why}";
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
            if (end == Phase.Done)
                Say($"done - {plan.Ventures.Count} venture item{(plan.Ventures.Count == 1 ? "" : "s")} to ARC, {plan.Gathers.Count} to GBR, {retrieved}crafts finished {_craftsDone}/{plan.Crafts.Count}{(_craftsFailed > 0 ? $", {_craftsFailed} failed" : "")}{stuck}.",
                    error: _craftsFailed > 0 || _unfetched.Count > 0);
            else
                Say($"dispatch stopped: {why} ({retrieved}crafts finished {_craftsDone}/{plan.Crafts.Count}).", error: true);
            if (_made.Count > 0 && _plugin.Config.DagobertAfterCraft && _plugin.GameData is { } gd)
                Dagobert.AfterCraft(_made, Name, gd.IsMarketable);
        }
        _plan = null;
        _current = null;
        _fetching = null;
        _snap = null;
        _crafts.Clear();
        _retrievals.Clear();
        _batchCrafts = Array.Empty<uint>();
        _plugin.Inventory.Invalidate();
        _plugin.Catalog.Invalidate();
    }

    private void Say(string text, bool error = false)
    {
        var line = "[LazyCrafter] " + text;
        if (error) { _log.Warning("{Line}", line); _chat.PrintError(line); }
        else { _log.Information("{Line}", line); _chat.Print(line); }
    }
}
