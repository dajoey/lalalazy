using System;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Modules.Fates;
using Dalamud.Game.ClientState.Fates;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace LazyOccultCrescent.Modules.CriticalEncounters;

public class TowerTimer : IDisposable
{
    private readonly CriticalEncounterTracker tracker;

    private readonly FatesModule fates;

    private DateTime LastForkedTowerEnd = DateTime.Now;

    private DateTime LastForkedTowerRegister = DateTime.Now;

    private TimeSpan ForkedTowerSpawnTimer = TimeSpan.FromMinutes(5);

    private readonly TimeSpan ForkedTowerWarmUpTimer = TimeSpan.FromSeconds(330);

    public int FatesCompleted { get; private set; } = 0;

    public int CriticalEncountersCompleted { get; private set; } = 0;

    public TowerTimer(CriticalEncounterTracker tracker, FatesModule fates)
    {
        this.tracker = tracker;
        this.fates = fates;

        fates.tracker.OnFateDespawned += OnFateDespawned;
        tracker.OnInactiveState += OnCriticalEncounterDespawned;
        tracker.OnRegisterState += OnCriticalEncounterRegistered;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public TimeSpan GetTimeToForkedTowerSpawn(DynamicEventState state)
    {
        if (state != DynamicEventState.Inactive)
        {
            return TimeSpan.Zero;
        }

        var fateModifier = TimeSpan.FromMinutes(1) * FatesCompleted;
        var criticalModifier = TimeSpan.FromMinutes(5) * CriticalEncountersCompleted;

        var time = LastForkedTowerEnd + (ForkedTowerSpawnTimer - fateModifier - criticalModifier) - DateTime.Now;
        if (time < TimeSpan.Zero)
        {
            ForkedTowerSpawnTimer = TimeSpan.FromMinutes(60);
            time = LastForkedTowerEnd + (ForkedTowerSpawnTimer - fateModifier - criticalModifier) - DateTime.Now;
        }

        return time;
    }

    public TimeSpan GetTimeRemainingToRegister(DynamicEventState state)
    {
        if (state != DynamicEventState.Register && state != DynamicEventState.Warmup)
        {
            return TimeSpan.Zero;
        }

        return LastForkedTowerRegister + ForkedTowerWarmUpTimer - DateTime.Now;
    }

    private void OnFateDespawned(Fate _)
    {
        FatesCompleted++;
    }

    private void OnCriticalEncounterDespawned(DynamicEvent ev)
    {
        CriticalEncountersCompleted++;

        if (ev.EventType < 4)
        {
            return;
        }

        LastForkedTowerEnd = DateTime.Now;
        LastForkedTowerRegister = DateTime.Now;

        FatesCompleted = 0;
        CriticalEncountersCompleted = 0;
        ForkedTowerSpawnTimer = TimeSpan.FromMinutes(60);
    }

    private void OnCriticalEncounterRegistered(DynamicEvent ev)
    {
        if (ev.EventType < 4)
        {
            return;
        }

        LastForkedTowerRegister = DateTime.Now;
    }

    private void OnTerritoryChanged(uint _)
    {
        if (!ZoneData.IsInOccultCrescent())
        {
            return;
        }

        FatesCompleted = 0;
        CriticalEncountersCompleted = 0;

        LastForkedTowerEnd = DateTime.Now;
        LastForkedTowerRegister = DateTime.Now;
        ForkedTowerSpawnTimer = TimeSpan.FromMinutes(5);
    }

    public void Dispose()
    {
        fates.tracker.OnFateDespawned -= OnFateDespawned;
        tracker.OnInactiveState -= OnCriticalEncounterDespawned;
        tracker.OnRegisterState -= OnCriticalEncounterRegistered;
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
    }
}
