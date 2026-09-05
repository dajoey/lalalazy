using System;
using System.Collections.Generic;
using System.Threading;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace LazyRetainerLive;

/// <summary>
/// Framework-ticked snapshot builder. Reads the LIVE retainer table
/// (RetainerManager) plus the live wallet items and writes a cached CharInfo
/// the HTTP handler serves. Never touch game memory from the HTTP thread —
/// the handler only ever reads the last completed snapshot object reference.
/// </summary>
internal sealed unsafe class RetainerLiveService
{
    /// <summary>Item 21072 = venture coin (Timeworn Vernines); AutoRetainer's own
    /// GetVenturesAmount() reads GetInventoryItemCount(21072).</summary>
    private const uint VentureCoinItemId = 21072;

    // Item 1 = gil (currency), same read AutoRetainer's WriteOfflineData uses.
    private const uint GilItemId = 1;

    private readonly Plugin _plugin;
    private long _nextRebuildTicks;
    private string _lastReason = "(no tick yet)";

    /// <summary>Lock-free published snapshot: a completed, immutable object.
    /// The HTTP thread swaps in via Interlocked and reads without locking.</summary>
    private CharInfo? _published;

    public RetainerLiveService(Plugin plugin)
    {
        _plugin = plugin;
    }

    public void Tick()
    {
        var now = Environment.TickCount64;
        if (now < Volatile.Read(ref _nextRebuildTicks))
            return;

        // Once per second, per the card. Throttle BEFORE doing any game-memory
        // work; on failure we still wait the full second (fail-slow, no spin).
        Volatile.Write(ref _nextRebuildTicks, now + 1000);

        try
        {
            var snap = BuildSnapshot();
            if (snap != null)
            {
                Interlocked.Exchange(ref _published, snap);
                _lastReason = "ok";
            }
            else
            {
                // Keep the LAST GOOD snapshot published while the game is in a
                // transition (login screen, zone change) — the relay overlays
                // whatever we serve, so serving the last-known live data beats
                // flapping to 503 and back. 503 only happens when we never had
                // data this session (see HttpServer.Serve).
                _lastReason = "no snapshot this tick (not logged in / not ready)";
            }
        }
        catch (Exception ex)
        {
            _lastReason = "rebuild failed: " + ex.Message;
            Plugin.Log.Warning(ex, "LazyRetainerLive snapshot rebuild failed");
        }
    }

    /// <summary>Latest completed snapshot, or null if none was ever built.</summary>
    public CharInfo? Current => Interlocked.CompareExchange(ref _published, null, null);

    public string LastReason => _lastReason;

    /// <summary>
    /// Builds one CharInfo from live game memory, or null when the live table
    /// cannot answer right now (no player, manager not ready, no usable rows).
    /// </summary>
    internal CharInfo? BuildSnapshot()
    {
        // API 15: the local player lives on IObjectTable (IClientState.LocalPlayer is gone).
        var localPlayer = Plugin.Objects.LocalPlayer;
        if (localPlayer == null)
            return null;

        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
            return null;

        // RowRef<World> is a struct here (API 15 Lumina bindings): .Value derefs
        // the Excel row; at any point LocalPlayer is non-null this is valid.
        var world = localPlayer.CurrentWorld.Value.Name.ToString();

        var chars = new CharInfo
        {
            Char = localPlayer.Name.TextValue,
            World = world,
        };

        // Character-level wallet reads — the same reads AutoRetainer's
        // WriteOfflineData performs before it saves DefaultConfig.json:
        //   Gil       = InventoryManager->GetInventoryItemCount(1)
        //   Ventures  = GetInventoryItemCount(21072)   (GetVenturesAmount)
        //   GCSeals   = GetCompanySeals(GrandCompany)  (AutoGCHandin.GetSeals)
        var im = InventoryManager.Instance();
        if (im != null)
        {
            chars.Gil = im->GetInventoryItemCount(GilItemId);
            chars.Ventures = im->GetInventoryItemCount(VentureCoinItemId);

            var gc = PlayerState.Instance()->GrandCompany;
            chars.Seals = gc == 0 ? 0 : im->GetCompanySeals(gc);
        }

        // Free inventory slots across the four main bags — the same loop shape
        // AutoRetainer's GetInventoryFreeSlotCount uses (ItemId == 0 counts).
        chars.Inventory = CountFreeSlots(im);

        // The live retainer table. Valid rows carry a nonzero RetainerId and a
        // name; AR's GameRetainerManager.Retainers additionally skips rows that
        // are not Available — mirror that so a freshly-quitting retainer is not
        // served as data. Order = table order (DisplayOrder is UI-only).
        var list = new List<RetainerInfo>(10);
        var ready = false;
        for (var i = 0; i < manager->Retainers.Length; i++)
        {
            var r = manager->Retainers[i];
            if (r.RetainerId == 0 || r.NameString.Length == 0)
                continue;

            var hasVenture = r.VentureId != 0;
            // AR: HasVenture = ret.VentureID != 0, and the file's VentureEndsAt
            // is ret.VentureCompleteTimeStamp = the same uint we read here.
            // endsAt is meaningful when a venture is out; emit 0 otherwise.
            list.Add(new RetainerInfo
            {
                Name = r.NameString,
                Job = r.ClassJob,
                Level = r.Level,
                HasVenture = hasVenture,
                EndsAt = hasVenture ? r.VentureComplete : 0,
                Gil = r.Gil,
                VentureId = r.VentureId,
                Mb = r.MarketItemCount,
            });
            ready = true;
        }
        chars.Retainers = list;

        // "Not logged in / no retainer data" -> null -> the HTTP layer answers
        // 503 (the card's fallback contract). A logged-in character with zero
        // valid rows only ever happens while the table is mid-reload, so treat
        // it the same as not-ready rather than publishing an empty list.
        if (!ready)
            return null;

        return chars;
    }

    private static long CountFreeSlots(InventoryManager* im)
    {
        if (im == null)
            return 0;
        long free = 0;
        foreach (var type in new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 })
        {
            var inv = im->GetInventoryContainer(type);
            if (inv == null)
                continue;
            var size = inv->Size;
            var items = inv->Items;
            for (var i = 0; i < size; i++)
            {
                if (items[i].ItemId == 0)
                    free++;
            }
        }
        return free;
    }
}
