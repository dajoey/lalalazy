using System.Linq;
using LazyOccultCrescent.Chains;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using LazyOccultCrescent.Modules.CriticalEncounters;
using LazyOccultCrescent.Modules.Fates;
using LazyOccultCrescent.Modules.StateManager;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Chain;
using Ocelot.IPC;

namespace LazyOccultCrescent.Modules.Automator;

public class Automator
{
    private static bool IsChainActive
    {
        get => ChainManager.Queues.Count > 0;
    }

    public Activity? Activity { get; private set; } = null;

    private int idleTime = 0;

    public void PostUpdate(AutomatorModule module, IFramework framework)
    {
        var vnav = module.GetIPCSubscriber<VNavmesh>();
        var lifestream = module.GetIPCSubscriber<Lifestream>();
        if (!vnav.IsReady() || !lifestream.IsReady())
        {
            return;
        }

        var states = module.GetModule<StateManagerModule>();
        if (Activity == null)
        {
            if (states.GetState() == State.InCombat)
            {
                return;
            }

            if (states.GetState() == State.InCriticalEncounter)
            {
                var critical = module.GetModule<CriticalEncountersModule>();

                // InCriticalEncounterHandler deliberately holds State.InCriticalEncounter
                // after the encounter ends for as long as the player is still in combat.
                // In that window every event is Inactive, so .Last(predicate) threw
                // InvalidOperationException on EVERY FRAME until combat dropped. The raw
                // indexer on the line below was the same hazard already fixed in
                // FindCriticalEncounter/FindFate and missed here.
                var active = critical.CriticalEncounters.Values
                    .Where(ev => ev.State != DynamicEventState.Inactive)
                    .ToList();

                if (active.Count == 0)
                {
                    return;
                }

                var encounter = active[^1];
                if (!EventData.CriticalEncounters.TryGetValue(encounter.DynamicEventId, out var data))
                {
                    return;
                }

                Activity = new CriticalEncounter(data, lifestream, vnav, module, critical);

                if (Activity != null)
                {
                    module.Debug($"Resuming running activity: {Activity.GetName()}");
                }

                return;
            }

            if (states.GetState() == State.InFate)
            {
                Activity ??= FindFate(module, lifestream, vnav);

                if (Activity != null)
                {
                    module.Debug($"Resuming running activity: {Activity.GetName()}");
                }

                return;
            }
        }

        if (Activity != null && !Activity.IsValid())
        {
            Plugin.Chain.Abort();
            vnav.Stop();
            Activity = null;
        }

        if (IsChainActive)
        {
            return;
        }

        if (Activity != null)
        {
            if (Activity.state == ActivityState.Done)
            {
                Activity = null;
                return;
            }

            var chain = Activity.GetChain(states);
            if (chain == null)
            {
                return;
            }

            Plugin.Chain.Submit(chain);
            return;
        }

        if (!module.Config.ShouldDoFates && !module.Config.ShouldDoCriticalEncounters)
        {
            return;
        }

        // Try and get the next activity
        Activity ??= module.Config.ShouldDoCriticalEncounters ? FindCriticalEncounter(module, lifestream, vnav) : null;
        Activity ??= module.Config.ShouldDoFates ? FindFate(module, lifestream, vnav) : null;
        if (Activity != null)
        {
            Svc.Log.Info($"Selected activity: {Activity.GetName()}");
            return;
        }

        var closest = AethernetData.GetClosestToPlayer();
        if (closest.DistanceToPlayer() <= 4.5f)
        {
            return;
        }

        idleTime += framework.UpdateDelta.Milliseconds;
        if (idleTime > 3000)
        {
            idleTime = 0;

            Plugin.Chain.Submit(ChainHelper.ReturnChain(new ReturnChainConfig { ApproachAetheryte = true }));
        }
    }

    private static CriticalEncounter? FindCriticalEncounter(AutomatorModule module, Lifestream lifestream, VNavmesh vnav)
    {
        if (!module.TryGetModule<CriticalEncountersModule>(out var source) || source == null)
        {
            return null;
        }

        foreach (var encounter in source.CriticalEncounters.Values)
        {
            if (!module.Config.CriticalEncountersMap.TryGetValue(encounter.DynamicEventId, out var enabled) || !enabled)
            {
                continue;
            }

            if (encounter.State != DynamicEventState.Register)
            {
                continue;
            }

            if (!EventData.CriticalEncounters.TryGetValue(encounter.DynamicEventId, out var data))
            {
                continue;
            }

            return new CriticalEncounter(data, lifestream, vnav, module, source);
        }

        return null;
    }

    private static FateActivity? FindFate(AutomatorModule module, Lifestream lifestream, VNavmesh vnav)
    {
        if (!module.TryGetModule<FatesModule>(out var source) || source == null)
        {
            return null;
        }

        foreach (var fate in source.fates.Values)
        {
            // Unknown fate ids must skip, not throw. Before the North Horn
            // entries existed this indexer raised KeyNotFoundException on
            // every North Horn fate and took the Automator down with it.
            if (!module.Config.FatesMap.TryGetValue(fate.Id, out var fateEnabled) || !fateEnabled)
            {
                continue;
            }

            return new FateActivity(fate.Data, lifestream, vnav, module, fate);
        }

        return null;
    }

    public void Refresh()
    {
        Activity = null;
        idleTime = 0;
    }
}
