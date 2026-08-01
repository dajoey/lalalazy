using System;
using System.Collections.Generic;
using System.Linq;
using LazyOccultCrescent.Enums;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.Data;

// Learns which North Horn event yields which Phantom Dispeller.
//
// This mapping is NOT in the game's data files. A scan of all 7,912 Excel sheets
// on 2026-08-01 found exactly nine references to the dispeller and demiatma item
// ids across the entire client, and every one of them was in the Quest sheet -
// the relic quests that CONSUME them (70855 South Horn, 71039 North Horn). No
// loot table, no MKD sheet, nothing on Fate or DynamicEvent. The drop is decided
// server-side, which is why upstream's South Horn table is hand-observed.
//
// So rather than ask anyone to write it down, watch the inventory: when a
// dispeller count goes up and exactly one event is active, attribute it. The
// "exactly one" guard matters - crediting the wrong event would send the
// Automator across the map for a drop that is not there.
public static class DispellerObserver
{
    private readonly static PhantomDispeller[] All =
        (PhantomDispeller[])Enum.GetValues(typeof(PhantomDispeller));

    private static Dictionary<PhantomDispeller, int> counts = new();

    private static bool primed;

    public static unsafe int CountOf(PhantomDispeller dispeller)
    {
        var manager = InventoryManager.Instance();
        return manager == null ? 0 : manager->GetInventoryItemCount((uint)dispeller);
    }

    public static void Reset()
    {
        counts = new Dictionary<PhantomDispeller, int>();
        primed = false;
    }

    // activeEventIds: every FATE / critical encounter currently running.
    public static void Tick(IReadOnlyCollection<uint> activeEventIds)
    {
        if (!ZoneData.IsInNorthHorn())
        {
            return;
        }

        var current = All.ToDictionary(d => d, CountOf);

        // First tick after entering the zone establishes the baseline. Without
        // this, everything already in the bag reads as a fresh drop.
        if (!primed)
        {
            counts = current;
            primed = true;
            return;
        }

        foreach (var dispeller in All)
        {
            var before = counts.GetValueOrDefault(dispeller);
            var after = current[dispeller];
            if (after <= before)
            {
                continue;
            }

            if (activeEventIds.Count == 1)
            {
                var eventId = activeEventIds.First();
                if (ZoneDiscovery.RecordEventDispeller(eventId, dispeller))
                {
                    Svc.Log.Information(
                        $"[DispellerObserver] learned: event {eventId} yields {dispeller.ToFriendlyString()}");
                }
            }
            else
            {
                Svc.Log.Debug(
                    $"[DispellerObserver] +{after - before} {dispeller} but {activeEventIds.Count} events active - not attributing");
            }
        }

        counts = current;
    }
}
