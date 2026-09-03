using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using LazyCrafter.Core;

namespace LazyCrafter.Adapters;

/// <summary>
/// <see cref="IInventory"/> over the AllaganTools (InventoryTools) IPC (Plan §Phase 3 task 2).
/// <para>
/// One IPC call per distinct item, memoised until the next inventory event; the enabled
/// <see cref="InventorySource"/> set decides which container ids are summed and whether alt characters
/// count. <c>AllaganTools.ItemAdded</c> / <c>ItemRemoved</c> drop the memo after a 2 s debounce and
/// raise <see cref="Changed"/> so the catalog can recompute off-thread.
/// </para>
/// <para>
/// Without AllaganTools the adapter falls back to the client's <c>InventoryManager</c> (current
/// character's bags + crystals only) and <see cref="Degraded"/> is true so the UI can show a banner.
/// </para>
/// IPC shapes verified against InventoryTools <c>IPC/IPCService.cs</c> (2026-09-03):
/// <c>ItemCountOwned(uint itemId, bool currentCharacterOnly, uint[] inventoryTypes) : uint</c>,
/// <c>IsInitialized() : bool</c>, <c>GetCharactersOwnedByActive(bool includeOwner) : HashSet&lt;ulong&gt;</c>,
/// events <c>ItemAdded/ItemRemoved((uint, ItemFlags, ulong, uint))</c>, <c>Initialized(bool)</c>.
/// </summary>
public sealed class AllaganInventory : IInventory, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);

    private readonly IPluginLog _log;
    private readonly IFramework _framework;
    private readonly Func<InventorySource, bool> _isEnabled;

    private readonly ICallGateSubscriber<bool> _isInitialized;
    private readonly ICallGateSubscriber<uint, bool, uint[], uint> _itemCountOwned;
    private readonly ICallGateSubscriber<bool, HashSet<ulong>> _charactersOwnedByActive;
    private readonly ICallGateSubscriber<(uint, InventoryItem.ItemFlags, ulong, uint), bool> _itemAdded;
    private readonly ICallGateSubscriber<(uint, InventoryItem.ItemFlags, ulong, uint), bool> _itemRemoved;
    private readonly ICallGateSubscriber<bool, bool> _initialized;

    private readonly Dictionary<uint, int> _memo = new();
    private readonly object _lock = new();
    private DateTime? _invalidateAt;
    private uint[] _types = [];
    private bool _allCharacters;

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
        foreach (var s in Enum.GetValues<InventorySource>())
            if (s != InventorySource.AltCharacters && _isEnabled(s))
                types.AddRange(InventorySources.TypesFor(s));
        lock (_lock)
        {
            _types = types.ToArray();
            _allCharacters = _isEnabled(InventorySource.AltCharacters);
            _memo.Clear();
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

    /// <summary>Force a recompute (e.g. a manual refresh button).</summary>
    public void Invalidate()
    {
        lock (_lock) _memo.Clear();
        Changed?.Invoke();
    }

    private void OnItemEvent((uint ItemId, InventoryItem.ItemFlags Flags, ulong CharacterId, uint Quantity) _)
    {
        lock (_lock) _invalidateAt = DateTime.UtcNow + Debounce;
    }

    private void OnInitialized(bool ready)
    {
        Available = ready;
        lock (_lock) { _memo.Clear(); _invalidateAt = DateTime.UtcNow; }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        bool fire;
        lock (_lock)
        {
            fire = _invalidateAt is { } at && DateTime.UtcNow >= at;
            if (fire) { _invalidateAt = null; _memo.Clear(); }
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
