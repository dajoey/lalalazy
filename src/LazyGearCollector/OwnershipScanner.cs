using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyGearCollector;

/// <summary>
/// Answers "do I have this item, and where". Bags, the armoury chest and equipped gear are read
/// live every time. Saddlebag and retainer containers can only be read while the game has them
/// loaded, so those are snapshotted opportunistically and replayed from config with a timestamp -
/// the UI always says which numbers are live and which are remembered.
/// </summary>
public sealed class OwnershipScanner
{
    private static readonly InventoryType[] OpportunisticContainers =
    [
        InventoryType.SaddleBag1, InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2,
        InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3,
        InventoryType.RetainerPage4, InventoryType.RetainerPage5, InventoryType.RetainerPage6,
        InventoryType.RetainerPage7, InventoryType.RetainerEquippedItems,
    ];

    private readonly Configuration _config;
    private Dictionary<uint, int>? _liveCache;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);

    public OwnershipScanner(Configuration config) => _config = config;

    public void Invalidate() => _liveCache = null;

    /// <summary>
    /// Live count from bags + armoury + equipped. Currency-category items also check the currency
    /// container. Results are cached briefly: the UI asks about ~140 items several times per frame.
    /// </summary>
    public int LiveCount(uint itemId)
    {
        if (_liveCache == null || DateTime.UtcNow >= _cacheExpiry)
        {
            _liveCache = new Dictionary<uint, int>();
            _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
        }

        if (_liveCache.TryGetValue(itemId, out var cached)) return cached;

        var value = ReadLiveCount(itemId);
        _liveCache[itemId] = value;
        return value;
    }

    private static unsafe int ReadLiveCount(uint itemId)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return 0;

        // Obols, fixatives and the like live in the hidden currency container rather than a bag.
        var currency = mgr->GetItemCountInContainer(itemId, InventoryType.Currency, false, 0);
        if (currency > 0) return currency;

        return mgr->GetInventoryItemCount(itemId, false, true, true, 0);
    }

    /// <summary>Live count plus any remembered saddlebag/retainer copies, when that option is on.</summary>
    public int TotalCount(uint itemId)
    {
        var total = LiveCount(itemId);
        if (_config.IncludeCachedContainers)
            total += CachedCount(itemId);
        return total;
    }

    public int CachedCount(uint itemId) =>
        _config.Snapshots.Values
            .Where(s => s.Counts.ContainsKey(itemId))
            .Sum(s => s.Counts[itemId]);

    /// <summary>Labels of remembered containers that hold this item, for tooltips.</summary>
    public IEnumerable<(string Label, int Count, DateTime SeenUtc)> CachedSources(uint itemId) =>
        _config.Snapshots.Values
            .Where(s => s.Counts.TryGetValue(itemId, out var n) && n > 0)
            .Select(s => (s.Label, s.Counts[itemId], s.SeenUtc));

    /// <summary>
    /// Walks the containers we can only see sometimes and remembers anything from the tracked
    /// collections. Cheap and side-effect free; safe to call on a timer.
    /// </summary>
    public unsafe void RefreshOpportunisticSnapshots(IEnumerable<uint> trackedItemIds)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return;

        var tracked = trackedItemIds as IReadOnlyCollection<uint> ?? trackedItemIds.ToList();
        if (tracked.Count == 0) return;

        var dirty = false;

        foreach (var type in OpportunisticContainers)
        {
            var container = mgr->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded || container->Size == 0) continue;

            var key = ContainerKey(type);
            var label = ContainerLabel(type);
            var counts = new Dictionary<uint, int>();

            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0) continue;

                var id = slot->ItemId;
                if (id >= 1000000) id -= 1000000; // strip HQ marker
                if (!tracked.Contains(id)) continue;

                counts.TryGetValue(id, out var existing);
                counts[id] = existing + Math.Max(1, (int)slot->Quantity);
            }

            // Record even an empty read: it is meaningful that the container was seen and held nothing.
            if (_config.Snapshots.TryGetValue(key, out var snap))
            {
                if (SameCounts(snap.Counts, counts) && snap.Label == label) continue;
                snap.Counts = counts;
                snap.Label = label;
                snap.SeenUtc = DateTime.UtcNow;
            }
            else
            {
                _config.Snapshots[key] = new ContainerSnapshot
                {
                    Label = label, Counts = counts, SeenUtc = DateTime.UtcNow,
                };
            }
            dirty = true;
        }

        if (dirty) _config.Save();
    }

    private static bool SameCounts(Dictionary<uint, int> a, Dictionary<uint, int> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
            if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
        return true;
    }

    private static string ContainerKey(InventoryType type) => type switch
    {
        InventoryType.SaddleBag1 or InventoryType.SaddleBag2 => "saddlebag",
        InventoryType.PremiumSaddleBag1 or InventoryType.PremiumSaddleBag2 => "saddlebag-premium",
        _ => $"retainer:{CurrentRetainerId()}:{(int)type}",
    };

    private static string ContainerLabel(InventoryType type) => type switch
    {
        InventoryType.SaddleBag1 or InventoryType.SaddleBag2 => "Saddlebag",
        InventoryType.PremiumSaddleBag1 or InventoryType.PremiumSaddleBag2 => "Premium saddlebag",
        _ => CurrentRetainerName(),
    };

    private static unsafe ulong CurrentRetainerId()
    {
        try
        {
            var rm = RetainerManager.Instance();
            if (rm == null) return 0;
            var active = rm->GetActiveRetainer();
            return active == null ? 0 : active->RetainerId;
        }
        catch { return 0; }
    }

    private static unsafe string CurrentRetainerName()
    {
        try
        {
            var rm = RetainerManager.Instance();
            if (rm == null) return "Retainer";
            var active = rm->GetActiveRetainer();
            if (active == null) return "Retainer";
            var name = active->NameString;
            return string.IsNullOrWhiteSpace(name) ? "Retainer" : name;
        }
        catch { return "Retainer"; }
    }
}
