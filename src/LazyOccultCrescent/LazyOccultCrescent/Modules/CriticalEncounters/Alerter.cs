using System;
using System.Collections.Generic;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace LazyOccultCrescent.Modules.CriticalEncounters;

public class Alerter : IDisposable
{
    private readonly CriticalEncountersModule module;

    private Dictionary<Demiatma, Func<bool>> DemiatmaAlerts
    {
        get => new()
        {
            [Demiatma.Azurite] = () => module.Config.AlertAzurite,
            [Demiatma.Verdigris] = () => module.Config.AlertVerdigris,
            [Demiatma.Malachite] = () => module.Config.AlertMalachite,
            [Demiatma.Realgar] = () => module.Config.AlertRealgar,
            [Demiatma.CaputMortuum] = () => module.Config.AlertCaputMortuum,
            [Demiatma.Orpiment] = () => module.Config.AlertOrpiment,
        };
    }

    private Dictionary<SoulShard, Func<bool>> SoulShardAlerts
    {
        get => new()
        {
            [SoulShard.Oracle] = () => module.Config.AlertOracle,
            [SoulShard.Berserker] = () => module.Config.AlertBerserker,
            [SoulShard.Ranger] = () => module.Config.AlertRanger,
        };
    }

    public Alerter(CriticalEncountersModule module)
    {
        this.module = module;

        this.module.Tracker.OnRegisterState += OnCriticalEncounterSpawned;
        this.module.Tracker.OnInactiveState += OnCriticalEncounterDepawned;
    }

    private void OnCriticalEncounterSpawned(DynamicEvent ev)
    {
        if (module.Config.LogSpawn)
        {
            Svc.Chat.Print($"{ev.Name} has Spawned");
        }

        if (!ShouldAlertForCriticalEncounter(ev))
        {
            return;
        }

        unsafe { UIGlobals.PlaySoundEffect(66); }
    }

    private void OnCriticalEncounterDepawned(DynamicEvent ev)
    {
        if (module.Config.LogSpawn)
        {
            Svc.Chat.Print($"{ev.Name} has Despawned");
        }

        if (!ShouldAlertForCriticalEncounter(ev))
        {
            return;
        }

        unsafe { UIGlobals.PlaySoundEffect(68); }
    }

    private bool ShouldAlertForCriticalEncounter(DynamicEvent ev)
    {
        if (module.Config.AlertAll)
        {
            return true;
        }

        if (!EventData.CriticalEncounters.TryGetValue(ev.DynamicEventId, out var data))
        {
            return false;
        }

        if (data.Demiatma != null)
        {
            var demiatma = (Demiatma)data.Demiatma;
            if (DemiatmaAlerts.TryGetValue(demiatma, out var getter))
            {
                return getter();
            }
        }

        if (data.Soulshard != null)
        {
            var soulshard = (SoulShard)data.Soulshard;
            if (SoulShardAlerts.TryGetValue(soulshard, out var getter))
            {
                return getter();
            }
        }

        return false;
    }

    public void Dispose()
    {
        module.Tracker.OnRegisterState -= OnCriticalEncounterSpawned;
        module.Tracker.OnInactiveState -= OnCriticalEncounterDepawned;
    }
}
