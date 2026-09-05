using System.Diagnostics;
using Dalamud.Plugin.Services;
using LazyCrafter.Adapters;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Catalog;

/// <summary>
/// Owns every expensive computation behind the window (Plan §Phase 4 task 6): recipe tiering over the whole
/// catalog, profit / scrip / desynth / leveling per recipe, the cart, the filtered+sorted view, and Universalis
/// priming for what is on screen. All of it runs on <b>one</b> background worker; the UI only ever reads the
/// immutable <see cref="Snapshot"/> / <see cref="View"/> references (swapped atomically) and pokes the worker
/// through <see cref="Invalidate"/>, <see cref="InvalidateCounts"/>, <see cref="Request"/> and the cart methods.
/// Nothing here is called from <c>Draw</c> except those pokes, and nothing on the worker touches ImGui.
/// <para>
/// The Core objects (<see cref="RecipeGraph"/> memoises, <see cref="Tiering"/> walks) are private to the worker -
/// they are not thread-safe and must not be shared with the draw thread. Game reads that must happen on the
/// framework thread (job levels, crafting-log flags, the no-AllaganTools inventory fallback) are gathered in one
/// <see cref="IFramework.RunOnFrameworkThread(Func{Task})"/> prologue per pass.
/// </para>
/// <para>
/// <b>Pass levels (t_410dee8a).</b> The full pass re-reads everything, including the 13,892-recipe crafting-log
/// sweep - the measured ~145 ms framework hitch behind the gather stutter - so it now runs only when something
/// that is NOT inventory asked for it: login, a settings change, or the Refresh button. An inventory change takes
/// the <see cref="InvalidateCounts"/> path instead: fresh item counts and rows against the CACHED log set, with
/// the crafting-log sweep and the cache itself skipped entirely. The log cache lives for the whole login and is
/// patched in place (one flag per craft) by <see cref="NoteCraftCompleted"/>.
/// </para>
/// </summary>
public sealed class CatalogService : IDisposable
{
    /// <summary>How many rows from the top of the current view get priced per priming round.</summary>
    public const int PriceWindow = 200;
    /// <summary>Priming rounds per view change before we stop chasing the sort (Scope §6: price the visible set, not everything).</summary>
    public const int MaxPrimeRounds = 3;

    private readonly Plugin _plugin;
    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly object _lock = new();
    private readonly Task _worker;
    private readonly Timer _priceTimer;
    private readonly object _publishLock = new();

    // Worker-private Core (built once game data is ready; rebuilt when the retainer set / price basis changes).
    private RecipeGraph? _graph;
    private CatalogBuilder? _builder;
    private string _coreKey = "";
    private Dictionary<uint, RecipeAssessment> _assess = new();
    private Dictionary<uint, CatalogRow> _rows = new();
    private CatalogBuilder.DictInventory _inv = new(new Dictionary<uint, int>());
    private int _generation;

    // Crafting-log cache (t_410dee8a): the completed-recipe set for this login. null = not read yet; the next
    // full pass populates it. An inventory event must never clear it - picking up an ore cannot change the
    // crafting log, and re-reading all 13,892 flags on the framework thread is exactly the hitch we removed.
    // Guarded by _logLock: written by the worker prologue and patched in place by NoteCraftCompleted (framework
    // thread), read by the counts prologue (worker) - plain reads of the reference are safe everywhere.
    private readonly object _logLock = new();
    private HashSet<uint>? _logComplete;

    // Requests from the UI thread.
    private bool _fullDirty = true;
    private bool _countsDirty;
    private bool _viewDirty = true;
    private bool _priceDirty;
    private ViewRequest _request = CatalogView.Empty.Request;
    private List<(uint RecipeId, int Crafts)> _cart;
    private HashSet<uint> _pinned = new();

    private volatile CatalogSnapshot _snapshot = CatalogSnapshot.Empty;
    private volatile CatalogView _view = CatalogView.Empty;
    private volatile string _status = "starting";
    private volatile string? _lastError;

    public CatalogService(Plugin plugin, IFramework framework, IPluginLog log)
    {
        _plugin = plugin;
        _framework = framework;
        _log = log;
        _cart = plugin.Config.Cart.Select(c => (c.RecipeId, c.Crafts)).Where(c => c.Crafts > 0).ToList();
        _worker = Task.Run(RunAsync, _cts.Token);
        _priceTimer = new Timer(_ => { lock (_lock) _priceDirty = true; Poke(); }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    // ---------------------------------------------------------------- what the UI reads

    public CatalogSnapshot Snapshot => _snapshot;
    public CatalogView View => _view;
    /// <summary>One line for the status bar: "computing...", "ready (n rows)", or the last failure.</summary>
    public string Status => _status;
    public string? LastError => _lastError;
    public bool Busy { get; private set; }
    public int PriceRequests { get; private set; }

    // ---------------------------------------------------------------- pokes from the UI thread

    /// <summary>
    /// Full recompute on the next worker pass: settings changed, login, or the Refresh button. This is the only
    /// path (besides the first pass) that re-reads the whole crafting log - a deliberate ~145 ms cost the player
    /// just asked for, never one an idle background pass may pay (t_410dee8a).
    /// </summary>
    public void Invalidate()
    {
        lock (_lock) { _fullDirty = true; _countsDirty = true; _viewDirty = true; }
        lock (_logLock) _logComplete = null;   // force the 13,892-flag sweep in the next prologue
        Poke();
    }

    /// <summary>
    /// Inventory changed (the debounced AllaganTools event): re-snapshot item counts and rebuild the rows against
    /// the CACHED jobs / crafting-log / retainer data on the next worker pass. No crafting-log sweep, no full
    /// pass - this is the path that used to hitch gathering every ~30 s (t_410dee8a).
    /// </summary>
    public void InvalidateCounts()
    {
        lock (_lock) { _countsDirty = true; _viewDirty = true; }
        Poke();
    }

    /// <summary>
    /// A dispatch run just finished crafting <paramref name="recipeId"/>: its crafting-log flag may have flipped
    /// to complete (a first-time craft is exactly when that happens). Re-read THAT ONE flag on the framework
    /// thread and patch the cached log set in place, then poke a counts pass so the row (and the not-yet-crafted
    /// counter) update without a relog and without rescanning the log.
    /// </summary>
    public void NoteCraftCompleted(uint recipeId)
    {
        // Framework hop: QuestManager.IsRecipeComplete is a game read. Fire-and-forget - never blocks the caller.
        _ = Task.Run(async () =>
        {
            try
            {
                var done = await _framework.RunOnFrameworkThread(() => _plugin.Player.IsRecipeComplete(recipeId)).ConfigureAwait(false);
                lock (_logLock)
                {
                    var set = _logComplete;
                    if (set is not null)
                    {
                        if (done) set.Add(recipeId);
                        else set.Remove(recipeId);
                    }
                }
                InvalidateCounts();
            }
            catch (ObjectDisposedException) { /* shutting down */ }
            catch (Exception ex) { _log.Warning(ex, "LazyCrafter post-craft log refresh failed for {RecipeId}", recipeId); }
        });
    }

    /// <summary>Re-prime prices for the visible set (manual refresh button). Stale quotes are re-fetched, fresh ones are not.</summary>
    public void RefreshPrices()
    {
        lock (_lock) _priceDirty = true;
        Poke();
    }

    /// <summary>The window's current filters/sort; cheap to call every frame - only a change wakes the worker.</summary>
    public void Request(ViewRequest req)
    {
        bool changed;
        lock (_lock)
        {
            changed = !req.Equals(_request);
            if (changed) { _request = req; _viewDirty = true; _priceDirty = true; }
        }
        if (changed) Poke();
    }

    /// <summary>Make sure this recipe (its result and materials) is included in the next price round - the selected row. Replaces the previous pin.</summary>
    public void Pin(uint recipeId)
    {
        bool changed;
        lock (_lock)
        {
            changed = !(_pinned.Count == 1 && _pinned.Contains(recipeId));
            if (changed) { _pinned.Clear(); _pinned.Add(recipeId); _priceDirty = true; }
        }
        if (changed) Poke();
    }

    public IReadOnlyList<(uint RecipeId, int Crafts)> CartLines { get { lock (_lock) return _cart.ToArray(); } }

    public void AddToCart(uint recipeId, int crafts)
    {
        if (crafts <= 0) return;
        lock (_lock)
        {
            var i = _cart.FindIndex(c => c.RecipeId == recipeId);
            if (i >= 0) _cart[i] = (recipeId, checked(_cart[i].Crafts + crafts));
            else _cart.Add((recipeId, crafts));
        }
        PersistCart();
        RepublishCart();
        QueueCartPriceRound();
    }

    public void SetCartQuantity(uint recipeId, int crafts)
    {
        lock (_lock)
        {
            var i = _cart.FindIndex(c => c.RecipeId == recipeId);
            if (i < 0) return;
            if (crafts <= 0) _cart.RemoveAt(i); else _cart[i] = (recipeId, crafts);
        }
        PersistCart();
        RepublishCart();
        QueueCartPriceRound();
    }

    public void RemoveFromCart(uint recipeId) => SetCartQuantity(recipeId, 0);

    public void ClearCart()
    {
        lock (_lock) _cart.Clear();
        PersistCart();
        RepublishCart();
    }

    /// <summary>
    /// Re-publish the cart immediately on the calling thread (the UI thread) - NOT on the coalescing worker,
    /// whose turn can be held by a full pass for minutes (t_9f646f4c: the post-run "stuck" cart). A cart edit
    /// becomes visible in the same frame it is made, even while <see cref="ComputeAllAsync"/> /
    /// <see cref="PrimeAndRefineAsync"/> is mid-flight. Reads only published-immutable state (the snapshot's
    /// row copy, the frozen per-pass inventory) plus Core objects that are safe off the worker (the
    /// <see cref="RecipeGraph"/> expand memo is concurrent; game data and the price cache are read-only /
    /// locked). Never touches the worker-private <c>_rows</c> / <c>_assess</c>.
    /// </summary>
    private void RepublishCart()
    {
        try
        {
            if (_plugin.GameData is null || _builder is null || _graph is null) return;   // nothing published yet - the first full pass brings the cart
            var snap = _snapshot;
            var prices = _plugin.Prices;
            var cartSnap = BuildCart(_plugin.GameData, _inv, prices, prices.BestTaxPct, snap.ByRecipe);
            lock (_publishLock)
            {
                // Base the swap on whatever is newest right now so a worker publish that landed while we were
                // building is never reverted; only the cart (and the generation) changes.
                var cur = _snapshot;
                _snapshot = cur with { Generation = Interlocked.Increment(ref _generation), Cart = cartSnap.Lines, CartTotals = cartSnap.Totals };
            }
        }
        catch (Exception ex)
        {
            // A cart edit must never be lost to a republish failure; the worker reads the live cart at the end
            // of its pass and the price round queued by the mutators guarantees one more publish.
            _log.Warning(ex, "LazyCrafter cart republish failed");
        }
    }

    /// <summary>
    /// A cart edit no longer needs the worker for visibility (see <see cref="RepublishCart"/>), but the new
    /// lines' materials may be unpriced: ask for one cheap price round. Never a full pass. This is also the
    /// eventual-consistency net: the worker's own publish reads the live cart under <c>_lock</c>, so a round
    /// that starts after an edit re-publishes it even if a full publish raced the synchronous one.
    /// </summary>
    private void QueueCartPriceRound()
    {
        lock (_lock) _priceDirty = true;
        Poke();
    }

    /// <summary>
    /// The cart as it is RIGHT NOW, re-assessed on the calling thread. Dispatch (and the plan preview) must
    /// plan against this, never against a snapshot's <c>Cart</c> - that can lag a cart edit by a whole catalog
    /// pass, and Dispatch would act on what Joey typed seconds ago instead of what he just typed.
    /// </summary>
    public (IReadOnlyList<CartLine> Lines, CartAssessment Totals) LiveCart()
    {
        var snap = _snapshot;
        if (_plugin.GameData is null || _builder is null || _graph is null || snap.Generation == 0) return (snap.Cart, snap.CartTotals);
        var prices = _plugin.Prices;
        return BuildCart(_plugin.GameData, _inv, prices, prices.BestTaxPct, snap.ByRecipe);
    }

    private void PersistCart()
    {
        List<(uint, int)> copy;
        lock (_lock) copy = _cart.ToList();
        _plugin.Config.Cart = copy.Select(c => new Configuration.CartEntry { RecipeId = c.Item1, Crafts = c.Item2 }).ToList();
        Plugin.Pi.SavePluginConfig(_plugin.Config);
    }

    private void Poke()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* already signalled */ }
    }

    // ---------------------------------------------------------------- worker

    private async Task RunAsync()
    {
        var ct = _cts.Token;
        try
        {
            _status = "loading game data";
            await _plugin.GameDataLoad.ConfigureAwait(false);
            if (_plugin.GameData is null) { _status = "game data failed to load - see /xllog"; return; }

            while (!ct.IsCancellationRequested)
            {
                await _signal.WaitAsync(ct).ConfigureAwait(false);
                bool full, counts, view, price;
                lock (_lock)
                {
                    full = _fullDirty; counts = _countsDirty; view = _viewDirty; price = _priceDirty;
                    _fullDirty = _countsDirty = _viewDirty = _priceDirty = false;
                }
                Busy = true;
                try
                {
                    if (full) await ComputeAllAsync(ct).ConfigureAwait(false);
                    else if (counts) await RefreshCountsAsync(ct).ConfigureAwait(false);
                    if (full || counts || view) BuildView();
                    if (full || counts || view || price) await PrimeAndRefineAsync(ct).ConfigureAwait(false);
                    _status = $"ready - {_snapshot.Rows.Count} recipes, {_snapshot.PricedRows} priced, computed in {(int)_snapshot.Duration.TotalMilliseconds} ms";
                    _lastError = null;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    _status = "error: " + ex.Message;
                    _log.Error(ex, "LazyCrafter catalog pass failed");
                }
                finally { Busy = false; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.Error(ex, "LazyCrafter catalog worker died"); _status = "worker died: " + ex.Message; }
    }

    private sealed record Prologue(bool LoggedIn, IReadOnlyDictionary<uint, int> Jobs, HashSet<uint> Complete, Dictionary<uint, int>? DegradedCounts,
        IReadOnlyList<RetainerStats> Retainers, IReadOnlySet<uint>? GatheredItems);

    /// <summary>
    /// Everything a FULL pass must read on the framework thread, in one hop. When the crafting-log cache is
    /// valid the 13,892-flag sweep is skipped and the cached set is returned instead (t_410dee8a); the sweep
    /// runs only on the first pass after login, a settings change, or the Refresh button.
    /// </summary>
    private Task<Prologue> ReadPrologueAsync(IEnumerable<uint> recipeIds, IEnumerable<uint> itemIds)
    {
        var ids = recipeIds.ToArray();
        var items = itemIds.ToArray();
        return _framework.RunOnFrameworkThread(() =>
        {
            var player = _plugin.Player;
            var loggedIn = player.IsLoggedIn;
            var jobs = loggedIn ? player.UnlockedJobs() : new Dictionary<uint, int>();
            HashSet<uint> complete;
            lock (_logLock)
            {
                var cached = _logComplete;
                if (cached is not null && loggedIn)
                {
                    complete = cached;   // login-scoped cache: no sweep, no copy - the worker never mutates it
                }
                else
                {
                    // THE expensive read (13,892 x QuestManager.IsRecipeComplete, ~145 ms with the renderer
                    // waiting) - only on an invalidated cache, i.e. once per login or on an explicit refresh.
                    complete = new HashSet<uint>();
                    if (loggedIn) foreach (var id in ids) if (player.IsRecipeComplete(id)) complete.Add(id);
                    if (loggedIn) _logComplete = complete;
                }
            }
            Dictionary<uint, int>? degraded = null;
            if (_plugin.Inventory.Degraded) degraded = _plugin.Inventory.Snapshot(items);
            // Retainer stats (ARControl.json, keyed by the player's content id) are read here too so the worker never touches IPlayerState.
            return new Prologue(loggedIn, jobs, complete, degraded, player.Retainers, player.GatheredItems);
        });
    }

    /// <summary>
    /// The counts-pass prologue: everything an INVENTORY-driven pass still needs from the framework thread -
    /// jobs, retainers, gathered items, and the degraded bag-count fallback. Never the crafting-log sweep: the
    /// cached set is copied under <see cref="_logLock"/> and handed to the row rebuild (t_410dee8a).
    /// </summary>
    private Task<Prologue> ReadCountsPrologueAsync(IEnumerable<uint> itemIds)
    {
        var items = itemIds.ToArray();
        return _framework.RunOnFrameworkThread(() =>
        {
            var player = _plugin.Player;
            var loggedIn = player.IsLoggedIn;
            var jobs = loggedIn ? player.UnlockedJobs() : new Dictionary<uint, int>();
            HashSet<uint> complete;
            lock (_logLock) complete = _logComplete is { } cached && loggedIn ? new HashSet<uint>(cached) : new HashSet<uint>();
            Dictionary<uint, int>? degraded = null;
            if (_plugin.Inventory.Degraded) degraded = _plugin.Inventory.Snapshot(items);
            return new Prologue(loggedIn, jobs, complete, degraded, player.Retainers, player.GatheredItems);
        });
    }

    private void EnsureCore(LuminaGameData gd, IReadOnlyList<RetainerStats> retainers, IReadOnlySet<uint>? gathered)
    {
        // Retainer stats gate the Venture source and the basis changes the profit model; rebuild the builder when
        // either changes (cheap - the RecipeGraph and its Expand memo are reused).
        var basis = _plugin.Config.RevenueBasis;
        var key = string.Join("|", retainers.Select(r => $"{r.Name}:{r.Level}:{r.JobId}:{r.ItemLevel}:{r.Gathering}:{r.Perception}")) + "#" + (gathered?.Count ?? -1) + "#" + basis;
        if (_builder is not null && key == _coreKey) return;
        _graph ??= new RecipeGraph(gd);
        _builder = new CatalogBuilder(gd, _graph, retainers, gathered, basis);
        _coreKey = key;
    }

    private async Task ComputeAllAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var gd = _plugin.GameData!;
        _status = "computing catalog";
        _graph ??= new RecipeGraph(gd);
        var graph = _graph;
        var recipeIds = graph.RecipeIds.ToArray();
        var itemIds = new HashSet<uint>();
        foreach (var id in recipeIds) CatalogBuilder.CollectIngredients(graph.Expand(id), itemIds);

        var pro = await ReadPrologueAsync(recipeIds, itemIds).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        EnsureCore(gd, pro.Retainers, pro.GatheredItems);
        var builder = _builder!;

        // Inventory snapshot: one consistent set of counts for the whole pass (AllaganTools off-thread is fine; the
        // client fallback was read in the prologue).
        var counts = pro.DegradedCounts ?? _plugin.Inventory.Snapshot(itemIds);
        _inv = new CatalogBuilder.DictInventory(counts);
        var inv = _inv;
        var prices = _plugin.Prices;
        var tax = prices.BestTaxPct;

        var assess = new Dictionary<uint, RecipeAssessment>(recipeIds.Length);
        var rows = new Dictionary<uint, CatalogRow>(recipeIds.Length);
        var tierCounts = new Dictionary<EffortTier, int>();
        var notCrafted = 0;
        var priced = 0;
        var i = 0;
        foreach (var id in recipeIds)
        {
            if ((++i & 1023) == 0) { ct.ThrowIfCancellationRequested(); _status = $"computing catalog {i}/{recipeIds.Length}"; }
            var rowDef = graph.Row(id)!;
            var a = builder.Tiering.Assess(id, inv);
            assess[id] = a;
            var row = builder.BuildRow(rowDef, a, inv, prices, tax, pro.Jobs, pro.Complete);
            rows[id] = row;
            tierCounts[a.Tier] = tierCounts.GetValueOrDefault(a.Tier) + 1;
            if (!row.LogComplete) notCrafted++;
            if (row.Nq is { RevenueKnown: true }) priced++;
        }
        _assess = assess;
        _rows = rows;

        var cartSnap = BuildCart(gd, inv, prices, tax, rows);
        Publish(pro.LoggedIn, pro.Jobs, pro.Retainers.Count, tierCounts, notCrafted, priced, cartSnap.Lines, cartSnap.Totals, sw.Elapsed);
    }

    /// <summary>
    /// The inventory-driven pass (t_410dee8a): fresh item counts and a full row / cart rebuild against the
    /// CACHED jobs, crafting-log set, retainers and gathered items. The one framework hop reads none of the
    /// 13,892 crafting-log flags - the whole point - and the pass is pure recompute plus publish.
    /// </summary>
    private async Task RefreshCountsAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var gd = _plugin.GameData!;
        // A counts pass rebuilds rows against cached data; without a published snapshot there is nothing to
        // rebuild against (e.g. an inventory event racing the very first pass). Fall back to the full pass.
        if (_builder is null || _graph is null || _snapshot.Generation == 0)
        {
            await ComputeAllAsync(ct).ConfigureAwait(false);
            return;
        }
        _status = "refreshing counts";
        var graph = _graph!;
        var recipeIds = graph.RecipeIds.ToArray();
        var itemIds = new HashSet<uint>();
        foreach (var id in recipeIds) CatalogBuilder.CollectIngredients(graph.Expand(id), itemIds);

        var pro = await ReadCountsPrologueAsync(itemIds).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        EnsureCore(gd, pro.Retainers, pro.GatheredItems);
        var builder = _builder!;

        var counts = pro.DegradedCounts ?? _plugin.Inventory.Snapshot(itemIds);
        _inv = new CatalogBuilder.DictInventory(counts);
        var inv = _inv;
        var prices = _plugin.Prices;
        var tax = prices.BestTaxPct;

        var assess = new Dictionary<uint, RecipeAssessment>(recipeIds.Length);
        var rows = new Dictionary<uint, CatalogRow>(recipeIds.Length);
        var tierCounts = new Dictionary<EffortTier, int>();
        var notCrafted = 0;
        var priced = 0;
        var i = 0;
        foreach (var id in recipeIds)
        {
            if ((++i & 1023) == 0) { ct.ThrowIfCancellationRequested(); _status = $"refreshing counts {i}/{recipeIds.Length}"; }
            var rowDef = graph.Row(id)!;
            var a = builder.Tiering.Assess(id, inv);
            assess[id] = a;
            var row = builder.BuildRow(rowDef, a, inv, prices, tax, pro.Jobs, pro.Complete);
            rows[id] = row;
            tierCounts[a.Tier] = tierCounts.GetValueOrDefault(a.Tier) + 1;
            if (!row.LogComplete) notCrafted++;
            if (row.Nq is { RevenueKnown: true }) priced++;
        }
        _assess = assess;
        _rows = rows;

        var cartSnap = BuildCart(gd, inv, prices, tax, rows);
        Publish(pro.LoggedIn, pro.Jobs, pro.Retainers.Count, tierCounts, notCrafted, priced, cartSnap.Lines, cartSnap.Totals, sw.Elapsed);
    }

    /// <param name="rowSource">
    /// Where cart-line rows are read from. The worker passes its own live dictionary (it is the only mutator);
    /// <see cref="RepublishCart"/> / <see cref="LiveCart"/> on the UI thread MUST pass the published snapshot's
    /// immutable copy instead - the worker refines <c>_rows</c> in place (<c>_rows[id] = ...</c>) and a plain
    /// <see cref="Dictionary{TKey,TValue}"/> cannot be read while that happens.
    /// </param>
    private (IReadOnlyList<CartLine> Lines, CartAssessment Totals) BuildCart(LuminaGameData gd, IInventory inv, IPriceSource prices, double tax, IReadOnlyDictionary<uint, CatalogRow> rowSource)
    {
        List<(uint RecipeId, int Crafts)> cart;
        lock (_lock) cart = _cart.ToList();
        var builder = _builder;
        var graph = _graph;
        if (builder is null || graph is null) return (Array.Empty<CartLine>(), new CartAssessment(EffortTier.Blocked, Array.Empty<RecipeAssessment>(), Array.Empty<IngredientLeaf>()));
        var totals = builder.Tiering.AssessCart(cart, inv);
        var lines = new List<CartLine>(cart.Count);
        var li = 0;
        foreach (var (recipeId, crafts) in cart)
        {
            if (graph.Row(recipeId) is null) continue;
            var a = totals.Lines[li++];
            rowSource.TryGetValue(recipeId, out var row);
            var est = row is { Marketable: true } ? builder.Profit.Evaluate(recipeId, inv, prices, tax, hq: false, crafts: crafts) : null;
            lines.Add(new CartLine(recipeId, crafts, row, a, est));
        }
        return (lines, totals);
    }

    private void Publish(bool loggedIn, IReadOnlyDictionary<uint, int> jobs, int retainers, Dictionary<EffortTier, int> tierCounts, int notCrafted, int priced, IReadOnlyList<CartLine> cart, CartAssessment totals, TimeSpan duration)
    {
        var list = _rows.Values.ToArray();
        // Under _publishLock with Interlocked: the UI thread republishes the cart concurrently (RepublishCart),
        // and two unguarded publishers would tear the generation counter.
        lock (_publishLock)
        {
            _snapshot = new CatalogSnapshot(Interlocked.Increment(ref _generation), list, new Dictionary<uint, CatalogRow>(_rows), tierCounts, notCrafted,
                jobs, cart, totals, loggedIn, _plugin.Inventory.Degraded, retainers, priced, DateTime.Now, duration);
        }
    }

    private void BuildView()
    {
        ViewRequest req;
        lock (_lock) req = _request;
        var snap = _snapshot;
        if (_graph is null || snap.Generation == 0) return;
        _view = ViewBuilder.Build(snap, req, _plugin.GameData!, _graph, _plugin.Prices);
    }

    /// <summary>
    /// Price what is on screen (top of the view + its materials + the cart + pinned rows), re-evaluate the rows those
    /// quotes touch, republish and rebuild the view. Repeats while the top of the view keeps changing, at most
    /// <see cref="MaxPrimeRounds"/> times, so a sort by /day converges without crawling the whole market.
    /// </summary>
    private async Task PrimeAndRefineAsync(CancellationToken ct)
    {
        var gd = _plugin.GameData!;
        var prices = _plugin.Prices;
        if (string.IsNullOrEmpty(prices.Scope) || _graph is null || _snapshot.Generation == 0) return;

        var seen = new HashSet<uint>();
        for (var round = 0; round < MaxPrimeRounds; round++)
        {
            var wanted = new HashSet<uint>();
            var view = _view;
            var snap = _snapshot;
            HashSet<uint> pinned;
            List<(uint RecipeId, int Crafts)> cart;
            lock (_lock) { pinned = new HashSet<uint>(_pinned); cart = _cart.ToList(); }
            foreach (var row in view.Rows.Take(PriceWindow)) AddRowItems(row, wanted);
            foreach (var id in pinned) if (snap.ByRecipe.TryGetValue(id, out var r)) AddRowItems(r, wanted);
            foreach (var (id, _) in cart) if (snap.ByRecipe.TryGetValue(id, out var r)) AddRowItems(r, wanted);
            if (view.Request.Tab == CatalogTab.Undersupplied)
                foreach (var row in snap.Rows) if (row.Marketable) wanted.Add(row.ResultItemId);   // the finder needs the whole craftable set

            wanted.RemoveWhere(id => !gd.IsMarketable(id));
            var fresh = wanted.Where(id => prices.IsStale(id) && seen.Add(id)).ToList();
            if (fresh.Count == 0) return;

            _status = $"pricing {fresh.Count} items";
            var primed = await prices.PrimeAsync(fresh, ct).ConfigureAwait(false);
            PriceRequests = prices.RequestsMade;
            if (primed == 0) return;

            // Re-evaluate only the rows whose result or materials were just quoted.
            var touched = new HashSet<uint>(fresh);
            var tax = prices.BestTaxPct;
            var complete = new HashSet<uint>(snap.Rows.Where(r => r.LogComplete).Select(r => r.RecipeId));
            var changed = 0;
            foreach (var row in snap.Rows)
            {
                if (!touched.Contains(row.ResultItemId) && !row.Leaves.Any(l => touched.Contains(l.ItemId))) continue;
                var def = _graph.Row(row.RecipeId)!;
                _rows[row.RecipeId] = _builder!.BuildRow(def, _assess[row.RecipeId], _inv, prices, tax, snap.Jobs, complete);
                changed++;
            }
            ct.ThrowIfCancellationRequested();
            var priced = _rows.Values.Count(r => r.Nq is { RevenueKnown: true });
            var cartSnap = BuildCart(gd, _inv, prices, tax, _rows);
            var tierCounts = new Dictionary<EffortTier, int>(snap.TierCounts);
            Publish(snap.LoggedIn, snap.Jobs, snap.RetainerCount, tierCounts, snap.NotYetCrafted, priced, cartSnap.Lines, cartSnap.Totals, snap.Duration);
            var before = _view.Rows.Take(PriceWindow).Select(r => r.RecipeId).ToArray();
            BuildView();
            var after = _view.Rows.Take(PriceWindow).Select(r => r.RecipeId).ToArray();
            _log.Debug("LazyCrafter price round {Round}: {Fresh} quoted, {Changed} rows re-evaluated", round, primed, changed);
            if (before.SequenceEqual(after)) return;
        }
    }

    private static void AddRowItems(CatalogRow row, HashSet<uint> into)
    {
        into.Add(row.ResultItemId);
        foreach (var l in row.Leaves) into.Add(l.ItemId);
    }

    // ---------------------------------------------------------------- helpers for the UI thread (read-only, cheap)

    /// <summary>Cash unit cost of a material as the profit model sees it (cheapest of market / gil vendor); null when unpriced.</summary>
    public long? UnitCost(uint itemId)
    {
        var gd = _plugin.GameData;
        if (gd is null) return null;
        long? best = null;
        var q = _plugin.Prices.Get(itemId);
        if (q is not null)
        {
            var p = q.MinListingNq ?? q.AvgSaleNq ?? q.MedianNq ?? q.MinListingHq ?? q.AvgSaleHq ?? q.MedianHq;
            if (p is { } mp && mp > 0) best = mp;
        }
        if (gd.IsGilVendor(itemId, out var gil) && gil > 0 && (best is null || gil < best)) best = gil;
        return best;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _priceTimer.Dispose();
        try { _signal.Release(); } catch (SemaphoreFullException) { }
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { /* cancelled */ }
        _cts.Dispose();
        _signal.Dispose();
    }
}
