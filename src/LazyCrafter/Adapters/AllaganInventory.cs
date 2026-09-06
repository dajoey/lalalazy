using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Adapters;

/// <summary>
/// <see cref="IInventory"/> over the AllaganTools (InventoryTools) IPC (Plan §Phase 3 task 2).
/// <para>
/// One IPC call per distinct item, memoised until the next inventory event; the enabled
/// <see cref="InventorySource"/> set decides which container ids are summed and whether alt characters
/// count. <c>AllaganTools.ItemAdded</c> / <c>ItemRemoved</c> drop the memo after a trailing debounce and
/// raise <see cref="Changed"/> so the catalog can recompute off-thread.
/// </para>
/// <para>
/// The debounce window widens while a LazyCrafter dispatch is running (t_410dee8a): gathering and crafting
/// move inventory constantly, and at the idle window (2 s) a busy route still queues a catalog counts pass
/// every few nodes. During a run the trailing deadline slides at <see cref="DispatchDebounce"/> (10 s)
/// instead - one pass after the route settles, not one per node - and returns to
/// <see cref="IdleDebounce"/> when the run ends.
/// </para>
/// <para>
/// Without AllaganTools the adapter falls back to the client's <c>InventoryManager</c> (current
/// character's bags + crystals only) and <see cref="Degraded"/> is true so the UI can show a banner.
/// </para>
/// <para>
/// <b>Owned is not in-bags.</b> <see cref="Count"/> answers the catalog's question (everything the enabled sources
/// can see, Scope §0); <see cref="CountInBags"/> and <see cref="StoredWhere"/> answer the dispatcher's - what a
/// synthesis can actually consume, and where the rest is sitting. The split is a second
/// <c>ItemCountOwned</c> over <see cref="InventorySources.BagTypes"/> only, plus one per enabled non-bag source,
/// memoised alongside the owned count and cleared by the same inventory events. Retainers are broken out by name
/// via <c>RetainerManager</c> when the counts allow it; otherwise they read as "your retainers".
/// </para>
/// IPC shapes verified against InventoryTools <c>IPC/IPCService.cs</c> (2026-09-03):
/// <c>ItemCountOwned(uint itemId, bool currentCharacterOnly, uint[] inventoryTypes) : uint</c>,
/// <c>IsInitialized() : bool</c>, <c>GetCharactersOwnedByActive(bool includeOwner) : HashSet&lt;ulong&gt;</c>,
/// events <c>ItemAdded/ItemRemoved((uint, ItemFlags, ulong, uint))</c>, <c>Initialized(bool)</c>.
/// </summary>
public sealed class AllaganInventory : IInventory, IDisposable
{
    /// <summary>Trailing debounce while idle: the AllaganTools event fires this long after the last item change.</summary>
    public static readonly TimeSpan IdleDebounce = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Trailing debounce while a LazyCrafter dispatch is running (t_410dee8a): gathering / crafting routes move
    /// inventory every few seconds, and at the idle window that is one catalog counts pass per node. 10 s of
    /// quiet (or the run ending) is what actually clears the memo and raises <see cref="Changed"/>.
    /// </summary>
    public static readonly TimeSpan DispatchDebounce = TimeSpan.FromSeconds(10);

    private readonly IPluginLog _log;
    private readonly IFramework _framework;
    private readonly Func<InventorySource, bool> _isEnabled;

    private readonly ICallGateSubscriber<bool> _isInitialized;
    private readonly ICallGateSubscriber<uint, bool, uint[], uint> _itemCountOwned;
    private readonly ICallGateSubscriber<uint, ulong, int, uint> _itemCount;
    private readonly ICallGateSubscriber<bool, HashSet<ulong>> _charactersOwnedByActive;
    private readonly ICallGateSubscriber<(uint, InventoryItem.ItemFlags, ulong, uint), bool> _itemAdded;
    private readonly ICallGateSubscriber<(uint, InventoryItem.ItemFlags, ulong, uint), bool> _itemRemoved;
    private readonly ICallGateSubscriber<bool, bool> _initialized;

    private readonly Dictionary<uint, int> _memo = new();
    private readonly Dictionary<uint, int> _bagMemo = new();
    private readonly Dictionary<uint, IReadOnlyList<StoredElsewhere>> _whereMemo = new();
    /// <summary>Currency balances (card t_b431de3a), memoised alongside the others and cleared by the same events.</summary>
    private readonly Dictionary<uint, int?> _currencyMemo = new();
    private readonly object _lock = new();
    private DateTime? _invalidateAt;
    private bool _dispatchRunning;
    private uint[] _types = [];
    private InventorySource[] _sources = [];
    private bool _allCharacters;

    /// <summary>Retainer content id → name, refreshed on the framework thread so <see cref="StoredWhere"/> can name places off it.</summary>
    private IReadOnlyList<(ulong Id, string Name)> _retainerNames = [];
    private DateTime _retainersReadAt = DateTime.MinValue;

    /// <summary>Raised on the framework thread after the debounce window closes; the memo is already cleared.</summary>
    public event Action? Changed;

    /// <summary>True when AllaganTools answered <c>IsInitialized</c> the last time we asked.</summary>
    public bool Available { get; private set; }

    /// <summary>True when counts come from the client's own bags because AllaganTools is missing.</summary>
    public bool Degraded => !Available;

    public AllaganInventory(IDalamudPluginInterface pi, IFramework framework, IPluginLog log, Func<InventorySource, bool> isEnabled)
    {
        _log = log;
        _framework = framework;
        _isEnabled = isEnabled;

        _isInitialized = pi.GetIpcSubscriber<bool>("AllaganTools.IsInitialized");
        _itemCountOwned = pi.GetIpcSubscriber<uint, bool, uint[], uint>("AllaganTools.ItemCountOwned");
        _itemCount = pi.GetIpcSubscriber<uint, ulong, int, uint>("AllaganTools.ItemCount");
        _charactersOwnedByActive = pi.GetIpcSubscriber<bool, HashSet<ulong>>("AllaganTools.GetCharactersOwnedByActive");
        _itemAdded = pi.GetIpcSubscriber<(uint, InventoryItem.ItemFlags, ulong, uint), bool>("AllaganTools.ItemAdded");
        _itemRemoved = pi.GetIpcSubscriber<(uint, InventoryItem.ItemFlags, ulong, uint), bool>("AllaganTools.ItemRemoved");
        _initialized = pi.GetIpcSubscriber<bool, bool>("AllaganTools.Initialized");

        _itemAdded.Subscribe(OnItemEvent);
        _itemRemoved.Subscribe(OnItemEvent);
        _initialized.Subscribe(OnInitialized);
        _framework.Update += OnFrameworkUpdate;

        RefreshSources();
        Probe();
    }

    /// <summary>Re-read the enabled-source toggles; call after the settings tab changes one.</summary>
    public void RefreshSources()
    {
        var types = new List<uint>();
        var sources = new List<InventorySource>();
        foreach (var s in Enum.GetValues<InventorySource>())
            if (s != InventorySource.AltCharacters && _isEnabled(s))
            {
                types.AddRange(InventorySources.TypesFor(s));
                sources.Add(s);
            }
        lock (_lock)
        {
            _types = types.ToArray();
            _sources = sources.ToArray();
            _allCharacters = _isEnabled(InventorySource.AltCharacters);
            _memo.Clear();
            _bagMemo.Clear();
            _whereMemo.Clear(); _currencyMemo.Clear();
        }
    }

    /// <summary>Ask AllaganTools whether it is up. Cheap; safe to call every few seconds.</summary>
    public bool Probe()
    {
        try
        {
            Available = _isInitialized.InvokeFunc();
        }
        catch (IpcNotReadyError)
        {
            Available = false;
        }
        catch (Exception ex)
        {
            Available = false;
            _log.Debug(ex, "AllaganTools.IsInitialized threw");
        }
        return Available;
    }

    /// <summary>
    /// Widen (or restore) the trailing debounce window for a running dispatch (t_410dee8a). Called by
    /// <c>DispatchService</c> when a run starts and when it ends. When a run ends and the idle window is now
    /// SHORTER than the pending deadline, the deadline snaps forward so the post-run refresh is not held an
    /// extra few seconds by a window that was set while gathering.
    /// </summary>
    public void SetDispatchRunning(bool running)
    {
        lock (_lock)
        {
            if (_dispatchRunning == running) return;
            _dispatchRunning = running;
            if (!running && _invalidateAt is { } at && at > DateTime.UtcNow + IdleDebounce)
                _invalidateAt = DateTime.UtcNow + IdleDebounce;
        }
    }

    public int Count(uint itemId)
    {
        lock (_lock)
        {
            if (_memo.TryGetValue(itemId, out var cached)) return cached;
        }

        var count = Available ? CountViaAllagan(itemId) : CountViaClient(itemId);

        lock (_lock) _memo[itemId] = count;
        return count;
    }

    private int CountViaAllagan(uint itemId)
    {
        uint[] types;
        bool all;
        lock (_lock) { types = _types; all = _allCharacters; }
        if (types.Length == 0) return 0;
        try
        {
            return (int)Math.Min(int.MaxValue, _itemCountOwned.InvokeFunc(itemId, !all, types));
        }
        catch (IpcNotReadyError)
        {
            Available = false;
            return CountViaClient(itemId);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "AllaganTools.ItemCountOwned({Item}) failed; falling back to client bags", itemId);
            return CountViaClient(itemId);
        }
    }

    /// <summary>Current character's bags + crystals via the client. NQ + HQ.</summary>
    private static unsafe int CountViaClient(uint itemId)
    {
        var im = InventoryManager.Instance();
        if (im == null) return 0;
        return im->GetInventoryItemCount(itemId, isHq: false) + im->GetInventoryItemCount(itemId, isHq: true);
    }

    /// <summary>
    /// Units physically in the four bags + the crystal pouch - what a synthesis can consume without fetching
    /// anything. Always the client's own containers, so it is right even when AllaganTools is stale or missing.
    /// (<c>ItemCountOwned</c> over <see cref="InventorySources.BagTypes"/> would give the same answer through
    /// AllaganTools' cache; the client is authoritative and cheaper.)
    /// </summary>
    public int CountInBags(uint itemId)
    {
        lock (_lock)
        {
            if (_bagMemo.TryGetValue(itemId, out var cached)) return cached;
        }
        var count = CountViaClient(itemId);
        lock (_lock) _bagMemo[itemId] = count;
        return count;
    }

    /// <summary>
    /// Where the units that are not in the bags are sitting, most-stocked first. One <c>ItemCountOwned</c> per
    /// enabled non-bag source; the retainer total is then split per retainer with <c>ItemCount(item, id, -1)</c>
    /// and named from <see cref="_retainerNames"/>. Only called for items a plan actually wants to consume, so the
    /// handful of extra IPC calls is bounded by the cart, not the catalog. Empty without AllaganTools.
    /// </summary>
    public IReadOnlyList<StoredElsewhere> StoredWhere(uint itemId)
    {
        lock (_lock)
        {
            if (_whereMemo.TryGetValue(itemId, out var cached)) return cached;
        }

        var places = new List<StoredElsewhere>();
        if (Available)
        {
            InventorySource[] sources;
            bool all;
            lock (_lock) { sources = _sources; all = _allCharacters; }
            foreach (var source in sources)
            {
                if (source == InventorySource.Bags) continue;
                var types = InventorySources.TypesFor(source);
                if (types.Length == 0) continue;
                var n = OwnedIn(itemId, types, all);
                if (n <= 0) continue;
                // `n` for Retainers is bags + crystals only: 12002 left RetainerTypes when a listing stopped counting
                // as stock you have (2026-09-05), so nothing here has to be netted off any more.
                if (source == InventorySource.Retainers) places.AddRange(SplitRetainers(itemId, n));
                else places.Add(new StoredElsewhere(PlaceName(source), n));
            }

            // Listings are no longer inside ANY enabled source's container set, because they are not owned stock -
            // but the player still wants to be told "it is sitting on the market board" rather than watch the plan
            // quietly buy one back. Reported as its own place, never fetchable, never part of Have.
            var listedOnBoard = OwnedIn(itemId, [InventorySources.RetainerMarket], all);
            if (listedOnBoard > 0) places.AddRange(SplitListings(itemId, listedOnBoard));
        }

        IReadOnlyList<StoredElsewhere> result = places.Count == 0
            ? Array.Empty<StoredElsewhere>()
            // Fetchable places first (card t_05e6722b): a listing is FYI, not a destination, so it must never
            // outrank a retainer stack just by being bigger. Within each group, most-stocked first as before.
            : places.OrderByDescending(p => p.Fetchable).ThenByDescending(p => p.Quantity).ToArray();
        lock (_lock) _whereMemo[itemId] = result;
        return result;
    }

    /// <summary>
    /// Units of a currency item the player holds (card t_b431de3a, decision D2) - Grand Company seals, beast-tribe
    /// tokens, tomestones, Fluorite Lenses. <c>null</c> when it cannot be read at all.
    /// <para>
    /// Read from the client's own <c>InventoryManager</c>, not through AllaganTools, for two reasons. First, these
    /// live in containers (<c>Currency</c> 2000, <c>KeyItem</c> 2004) that are in no <see cref="InventorySource"/>
    /// set, so <c>ItemCountOwned</c> over the enabled types would answer 0 for every one of them. Second,
    /// <c>GetInventoryItemCount</c> already searches the currency and crystal containers for exactly this kind of
    /// item, and it is the same call <see cref="CountInBags"/> trusts.
    /// </para>
    /// <para>
    /// A read that fails, or a client that is not logged in, returns <c>null</c>, and the affordability gate then
    /// refuses - which leaves the item on the market board. <b>There is no failure mode here that spends the
    /// player's currency</b>: the only way to be wrong is to under-report, and under-reporting is the safe
    /// direction by construction.
    /// </para>
    /// </summary>
    public int? CurrencyBalance(uint currencyItemId)
    {
        if (currencyItemId == 0) return null;
        lock (_lock)
        {
            if (_currencyMemo.TryGetValue(currencyItemId, out var cached)) return cached;
        }
        int? balance;
        try { balance = CurrencyViaClient(currencyItemId); }
        catch (Exception ex)
        {
            _log.Debug("currency balance read for {Item} failed: {Msg}", currencyItemId, ex.Message);
            balance = null;
        }
        lock (_lock) _currencyMemo[currencyItemId] = balance;
        return balance;
    }

    /// <summary>The client's own count for a currency item, or <c>null</c> when the inventory is not up.</summary>
    private static unsafe int? CurrencyViaClient(uint currencyItemId)
    {
        var im = InventoryManager.Instance();
        if (im == null) return null;
        return im->GetInventoryItemCount(currencyItemId, isHq: false);
    }

    /// <summary>Human place name for the refusal / retrieve lines: reads after "from".</summary>
    private static string PlaceName(InventorySource source) => source switch
    {
        InventorySource.Bags => "your bags",
        InventorySource.ArmouryChest => "the armoury chest",
        InventorySource.Saddlebag => "the chocobo saddlebag",
        InventorySource.Retainers => "your retainers",
        InventorySource.FCChest => "the FC chest",
        InventorySource.GlamourDresser => "the glamour dresser",
        _ => source.ToString(),
    };

    private int OwnedIn(uint itemId, uint[] types, bool allCharacters)
    {
        try { return (int)Math.Min(int.MaxValue, _itemCountOwned.InvokeFunc(itemId, !allCharacters, types)); }
        catch (Exception ex) { _log.Debug("AllaganTools.ItemCountOwned({Item}, scoped) failed: {Msg}", itemId, ex.Message); return 0; }
    }

    /// <summary>
    /// Break a retainer total into named retainers. Falls back to one unnamed "your retainers" entry when the
    /// names are unknown or the per-retainer counts do not add up (a retainer AllaganTools knows but
    /// <c>RetainerManager</c> has not listed yet, e.g. before the retainer bell has been opened this session).
    /// </summary>
    private IEnumerable<StoredElsewhere> SplitRetainers(uint itemId, int total)
    {
        var names = _retainerNames;
        if (names.Count == 0) return [new StoredElsewhere(PlaceName(InventorySource.Retainers), total)];

        var split = new List<StoredElsewhere>();
        var sum = 0;
        foreach (var (id, name) in names)
        {
            int n;
            // ItemCount(item, retainer, -1) is every container on that retainer, listings included; the total this
            // was handed excludes listings, so subtract them or the sums never reconcile and the split is discarded.
            try { n = (int)Math.Min(int.MaxValue, _itemCount.InvokeFunc(itemId, id, -1)) - (int)Math.Min(int.MaxValue, _itemCount.InvokeFunc(itemId, id, (int)InventorySources.RetainerMarket)); }
            catch (Exception ex) { _log.Debug("AllaganTools.ItemCount({Item}, {Id}) failed: {Msg}", itemId, id, ex.Message); return [new StoredElsewhere(PlaceName(InventorySource.Retainers), total)]; }
            if (n <= 0) continue;
            var who = string.IsNullOrWhiteSpace(name) ? id.ToString("X") : name;
            split.Add(new StoredElsewhere($"retainer {who}", n, Retainer: who));
            sum += n;
        }
        if (split.Count == 0 || sum != total) return [new StoredElsewhere(PlaceName(InventorySource.Retainers), total)];
        return split;
    }

    /// <summary>
    /// Market-board listings per retainer: "the market board (listed by retainer Cid)". Reads after "on". One unnamed
    /// entry when the names are unknown or the per-retainer counts do not reconcile.
    /// <para>
    /// Every entry here is built with <c>Fetchable: false</c> - a summoning bell cannot hand a listing over, so
    /// these places are named for information only and must never be chosen as the destination of a retrieval
    /// (card t_05e6722b). This is the ONLY producer of unfetchable places.
    /// </para>
    /// </summary>
    private IEnumerable<StoredElsewhere> SplitListings(uint itemId, int total)
    {
        const string unnamed = "the market board (your retainers' listings)";
        var names = _retainerNames;
        if (names.Count == 0) return [new StoredElsewhere(unnamed, total, Fetchable: false)];

        var split = new List<StoredElsewhere>();
        var sum = 0;
        foreach (var (id, name) in names)
        {
            int n;
            try { n = (int)Math.Min(int.MaxValue, _itemCount.InvokeFunc(itemId, id, (int)InventorySources.RetainerMarket)); }
            catch (Exception ex) { _log.Debug("AllaganTools.ItemCount({Item}, {Id}, market) failed: {Msg}", itemId, id, ex.Message); return [new StoredElsewhere(unnamed, total, Fetchable: false)]; }
            if (n <= 0) continue;
            var who = string.IsNullOrWhiteSpace(name) ? id.ToString("X") : name;
            split.Add(new StoredElsewhere($"the market board (listed by retainer {who})", n, Fetchable: false, Retainer: who));
            sum += n;
        }
        if (split.Count == 0 || sum != total) return [new StoredElsewhere(unnamed, total, Fetchable: false)];
        return split;
    }

    /// <summary>Framework thread: retainer content ids and names, for <see cref="StoredWhere"/>. Cheap; re-read every 30 s.</summary>
    private unsafe void RefreshRetainerNames()
    {
        if (DateTime.UtcNow - _retainersReadAt < TimeSpan.FromSeconds(30)) return;
        _retainersReadAt = DateTime.UtcNow;
        try
        {
            var mgr = RetainerManager.Instance();
            if (mgr == null) return;
            var list = new List<(ulong, string)>();
            for (uint i = 0; i < mgr->GetRetainerCount(); i++)
            {
                var r = mgr->GetRetainerBySortedIndex(i);
                if (r == null || r->RetainerId == 0) continue;
                list.Add((r->RetainerId, r->NameString));
            }
            if (list.Count > 0) _retainerNames = list;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "RetainerManager read failed; retainer stock will be reported unnamed");
        }
    }

    /// <summary>Number of retainers AllaganTools knows for the active character (0 when unavailable).</summary>
    public int OwnedRetainerCount()
    {
        if (!Available) return 0;
        try
        {
            return _charactersOwnedByActive.InvokeFunc(false).Count(id => id != 0);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "AllaganTools.GetCharactersOwnedByActive failed");
            return 0;
        }
    }

    /// <summary>
    /// Counts for a set of items as a plain dictionary (Phase 4 catalog input). With AllaganTools this is safe to
    /// call off the framework thread - the IPC is a managed LINQ over its item list - and answers come from the
    /// memo after the first pass; without it (<see cref="Degraded"/>) the client's <c>InventoryManager</c> is read,
    /// so call it on the framework thread.
    /// </summary>
    public Dictionary<uint, int> Snapshot(IEnumerable<uint> itemIds)
    {
        var d = new Dictionary<uint, int>();
        foreach (var id in itemIds) d[id] = Count(id);
        return d;
    }

    /// <summary>
    /// Drop the count memos WITHOUT raising <see cref="Changed"/> - the dispatcher's per-guard refresh. Dispatch
    /// wants fresh bag counts for its own checks, not a catalog recompute: raising here made every dispatch
    /// phase queue a full 13,892-recipe catalog pass (the post-run freeze, t_9f646f4c).
    /// </summary>
    public void DropMemo()
    {
        lock (_lock) { _memo.Clear(); _bagMemo.Clear(); _whereMemo.Clear(); _currencyMemo.Clear(); }
    }

    /// <summary>Force a recompute and notify listeners (a manual refresh button).</summary>
    public void Invalidate()
    {
        DropMemo();
        Changed?.Invoke();
    }

    private void OnItemEvent((uint ItemId, InventoryItem.ItemFlags Flags, ulong CharacterId, uint Quantity) _)
    {
        lock (_lock) _invalidateAt = DateTime.UtcNow + (_dispatchRunning ? DispatchDebounce : IdleDebounce);
    }

    private void OnInitialized(bool ready)
    {
        Available = ready;
        lock (_lock) { _memo.Clear(); _bagMemo.Clear(); _whereMemo.Clear(); _currencyMemo.Clear(); _invalidateAt = DateTime.UtcNow; }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        RefreshRetainerNames();
        bool fire;
        lock (_lock)
        {
            fire = _invalidateAt is { } at && DateTime.UtcNow >= at;
            if (fire) { _invalidateAt = null; _memo.Clear(); _bagMemo.Clear(); _whereMemo.Clear(); _currencyMemo.Clear(); }
        }
        if (fire) Changed?.Invoke();
    }

    /// <summary>For <c>/lcraft debug</c>: one line per source with its on/off state.</summary>
    public string DescribeSources()
    {
        var parts = Enum.GetValues<InventorySource>().Select(s => $"{s}={(_isEnabled(s) ? "on" : "off")}");
        return string.Join(" ", parts);
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _itemAdded.Unsubscribe(OnItemEvent);
        _itemRemoved.Unsubscribe(OnItemEvent);
        _initialized.Unsubscribe(OnInitialized);
    }
}
