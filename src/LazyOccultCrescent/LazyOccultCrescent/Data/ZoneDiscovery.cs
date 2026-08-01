using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using LazyOccultCrescent.Enums;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.DalamudServices;

namespace LazyOccultCrescent.Data;

// Learns zone geometry at runtime instead of requiring a hand survey.
//
// Upstream BOCCHI hardcodes every aetheryte, shard and event position as a
// surveyed constant, which is why it only ever supported one zone: a new horn
// meant a human walking the map with a notepad. Anything that lives in the LGB
// layout rather than Excel cannot be datamined, so North Horn would have been
// blocked on that survey.
//
// Instead we read positions out of the live object table the first time the
// player gets near them and persist the result. Cost: the first lap of a fresh
// zone is partially blind. Benefit: the zone bootstraps itself, and the same
// code will handle whatever horn comes next without a code change.
public static class ZoneDiscovery
{
    private class Store
    {
        public Dictionary<string, float[]> Aethernet { get; set; } = new();

        public Dictionary<string, float[]> Events { get; set; } = new();

        public float[]? Aetheryte { get; set; }
    }

    private readonly static Dictionary<uint, Store> Stores = new();

    private static bool dirty;

    private static uint loadedTerritory;

    private static string PathFor(uint territory)
    {
        var dir = Svc.PluginInterface.GetPluginConfigDirectory();
        Directory.CreateDirectory(dir);

        var name = ZoneData.ZoneNames.TryGetValue(territory, out var n)
            ? n.Replace(" ", "")
            : territory.ToString();

        return Path.Join(dir, $"discovered_{name}.json");
    }

    private static Store StoreFor(uint territory)
    {
        if (Stores.TryGetValue(territory, out var store))
        {
            return store;
        }

        store = new Store();
        var path = PathFor(territory);

        if (File.Exists(path))
        {
            try
            {
                store = JsonSerializer.Deserialize<Store>(File.ReadAllText(path)) ?? new Store();
            }
            catch (Exception ex)
            {
                // A corrupt discovery file must never take the plugin down with it;
                // the whole point of this store is that it can be rebuilt by walking.
                Svc.Log.Warning($"[ZoneDiscovery] could not read {path}: {ex.Message}. Starting empty.");
                store = new Store();
            }
        }

        Stores[territory] = store;
        return store;
    }

    public static void Load(uint territory)
    {
        if (!ZoneData.OccultTerritories.Contains(territory))
        {
            return;
        }

        loadedTerritory = territory;
        var store = StoreFor(territory);

        foreach (var (key, xyz) in store.Aethernet)
        {
            if (uint.TryParse(key, out var baseId) && xyz.Length == 3)
            {
                AethernetData.RecordDiscoveredPosition(baseId, new Vector3(xyz[0], xyz[1], xyz[2]));
            }
        }

        Svc.Log.Debug($"[ZoneDiscovery] loaded {store.Aethernet.Count} shard(s) for territory {territory}");
    }

    public static void Save()
    {
        if (!dirty || loadedTerritory == 0)
        {
            return;
        }

        try
        {
            var path = PathFor(loadedTerritory);
            File.WriteAllText(path, JsonSerializer.Serialize(StoreFor(loadedTerritory), new JsonSerializerOptions { WriteIndented = true }));
            dirty = false;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[ZoneDiscovery] save failed: {ex.Message}");
        }
    }

    // Scan the object table for aetheryte shards we have not placed yet.
    public static void Scan()
    {
        var territory = ZoneData.CurrentTerritory;
        if (!ZoneData.OccultTerritories.Contains(territory) || Svc.Objects.LocalPlayer == null)
        {
            return;
        }

        if (loadedTerritory != territory)
        {
            Load(territory);
        }

        var store = StoreFor(territory);
        var wanted = AethernetData.AllFor(territory)
            .Where(d => !d.HasSurveyedPosition && d.Position == Vector3.Zero)
            .Select(d => d.BaseId)
            .ToHashSet();

        if (wanted.Count == 0)
        {
            return;
        }

        foreach (var obj in Svc.Objects.Where(o => o.ObjectKind == ObjectKind.EventObj))
        {
            if (!wanted.Contains(obj.BaseId))
            {
                continue;
            }

            var pos = obj.Position;
            AethernetData.RecordDiscoveredPosition(obj.BaseId, pos);
            store.Aethernet[obj.BaseId.ToString()] = [pos.X, pos.Y, pos.Z];
            dirty = true;

            // The base camp aetheryte doubles as the zone's anchor point.
            var datum = AethernetData.AllFor(territory).FirstOrDefault(d => d.BaseId == obj.BaseId);
            if (datum != null && datum.Aethernet is Aethernet.NorthHornBaseCamp or Aethernet.BaseCamp)
            {
                store.Aetheryte = [pos.X, pos.Y, pos.Z];
            }

            Svc.Log.Information($"[ZoneDiscovery] found shard {obj.BaseId} at {pos:F2}");
        }

        Save();
    }

    // Record where a dynamic event actually started, so the next lap can path to it
    // before it spawns. Events carry no position in Excel.
    public static void RecordEventPosition(uint eventId, Vector3 position)
    {
        var territory = ZoneData.CurrentTerritory;
        if (!ZoneData.OccultTerritories.Contains(territory) || position == Vector3.Zero)
        {
            return;
        }

        var store = StoreFor(territory);
        var key = eventId.ToString();

        if (store.Events.ContainsKey(key))
        {
            return;
        }

        store.Events[key] = [position.X, position.Y, position.Z];
        dirty = true;
        Save();
    }

    public static bool TryGetEventPosition(uint eventId, out Vector3 position)
    {
        position = Vector3.Zero;
        var store = StoreFor(ZoneData.CurrentTerritory);

        if (store.Events.TryGetValue(eventId.ToString(), out var xyz) && xyz.Length == 3)
        {
            position = new Vector3(xyz[0], xyz[1], xyz[2]);
            return true;
        }

        return false;
    }

    public static bool TryGetAetheryte(uint territory, out Vector3 position)
    {
        position = Vector3.Zero;
        var store = StoreFor(territory);

        if (store.Aetheryte is { Length: 3 } a)
        {
            position = new Vector3(a[0], a[1], a[2]);
            return true;
        }

        return false;
    }

    // How much of the current zone we have actually placed, for the UI to surface.
    public static (int known, int total) Coverage()
    {
        var all = AethernetData.AllFor(ZoneData.CurrentTerritory).ToList();
        return (all.Count(d => d.Position != Vector3.Zero), all.Count);
    }

    public static void Reset(uint territory)
    {
        Stores.Remove(territory);
        var path = PathFor(territory);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
