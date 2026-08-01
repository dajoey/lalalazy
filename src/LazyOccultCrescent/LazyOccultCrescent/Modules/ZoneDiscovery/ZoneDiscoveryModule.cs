using System;
using System.Collections.Generic;
using System.Linq;
using LazyOccultCrescent.Modules.CriticalEncounters;
using LazyOccultCrescent.Modules.Fates;
using Ocelot.Modules;
using Ocelot.Windows;
using Discovery = LazyOccultCrescent.Data.ZoneDiscovery;
using DispellerObserver = LazyOccultCrescent.Data.DispellerObserver;

namespace LazyOccultCrescent.Modules.ZoneDiscovery;

// Runs early (low order) so that by the time the pathfinding and teleport modules
// tick, any shard the player can see has already been placed this frame.
[OcelotModule(2000, 0)]
public class ZoneDiscoveryModule(Plugin plugin, Config config) : Module(plugin, config)
{
    public override ZoneDiscoveryConfig Config
    {
        get => PluginConfig.ZoneDiscoveryConfig;
    }

    public override bool IsEnabled
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    public override bool ShouldUpdate
    {
        get => true;
    }

    // Scanning the object table every frame is wasted work: shards do not move,
    // and the only thing that changes is which ones are in render range.
    private readonly static TimeSpan ScanInterval = TimeSpan.FromSeconds(2);

    private DateTime nextScan = DateTime.MinValue;

    public override void Update(UpdateContext context)
    {
        if (DateTime.UtcNow < nextScan)
        {
            return;
        }

        nextScan = DateTime.UtcNow + ScanInterval;
        Discovery.Scan();
        DispellerObserver.Tick(ActiveEventIds());
    }

    // Every FATE and critical encounter currently running. The observer needs
    // this to know whether a drop can be attributed unambiguously.
    private List<uint> ActiveEventIds()
    {
        var ids = new List<uint>();

        if (Modules.TryGetModule<FatesModule>(out var fates) && fates != null)
        {
            ids.AddRange(fates.fates.Values.Select(f => f.Id));
        }

        if (Modules.TryGetModule<CriticalEncountersModule>(out var ce) && ce != null)
        {
            ids.AddRange(ce.CriticalEncounters.Values
                .Where(e => e.State != FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEventState.Inactive)
                .Select(e => (uint)e.DynamicEventId));
        }

        return ids;
    }

    public override void OnTerritoryChanged(uint id)
    {
        nextScan = DateTime.MinValue;
        Discovery.Load(id);
        DispellerObserver.Reset();
    }

    public override void Dispose()
    {
        Discovery.Save();
        base.Dispose();
    }
}
