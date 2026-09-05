using System;
using LazyOccultCrescent.Modules.Automator;
using LazyOccultCrescent.Modules.Buff;
using LazyOccultCrescent.Modules.Carrots;
using LazyOccultCrescent.Modules.CriticalEncounters;
using LazyOccultCrescent.Modules.Currency;
using LazyOccultCrescent.Modules.Data;
using LazyOccultCrescent.Modules.EventDrop;
using LazyOccultCrescent.Modules.Exp;
using LazyOccultCrescent.Modules.Fates;
using LazyOccultCrescent.Modules.ForkedTower;
using LazyOccultCrescent.Modules.MobFarmer;
using LazyOccultCrescent.Modules.Mount;
using LazyOccultCrescent.Modules.Pathfinder;
using LazyOccultCrescent.Modules.StateManager;
using LazyOccultCrescent.Modules.Teleporter;
using LazyOccultCrescent.Modules.Treasure;
using LazyOccultCrescent.Modules.WindowManager;
using LazyOccultCrescent.Modules.ZoneDiscovery;
using ECommons.DalamudServices;
using Ocelot;

namespace LazyOccultCrescent;

[Serializable]
public class Config : IOcelotConfig
{
    public int Version { get; set; } = 1;

    // Core
    public MountConfig MountConfig { get; set; } = new();

    public TeleporterConfig TeleporterConfig { get; set; } = new();

    public PathfinderConfig PathfinderConfig { get; set; } = new();

    public EventDropConfig EventDropConfig { get; set; } = new();

    public WindowManagerConfig WindowManagerConfig { get; set; } = new();

    public StateManagerConfig StateManagerConfig { get; set; } = new();

    public ZoneDiscoveryConfig ZoneDiscoveryConfig { get; set; } = new();

    // Functional

    public FatesConfig FatesConfig { get; set; } = new();

    public CriticalEncountersConfig CriticalEncountersConfig { get; set; } = new();

    public ForkedTowerConfig ForkedTowerConfig { get; set; } = new();

    public TreasureConfig TreasureConfig { get; set; } = new();

    public CarrotsConfig CarrotsConfig { get; set; } = new();

    public BuffConfig BuffConfig { get; set; } = new();

    // Trackers
    public CurrencyConfig CurrencyConfig { get; set; } = new();

    public ExpConfig ExpConfig { get; set; } = new();

    // Other
    public MobFarmerConfig MobFarmerConfig { get; set; } = new();

    public AutomatorConfig AutomatorConfig { get; set; } = new();

    public DataConfig DataConfig { get; set; } = new();


    /// <summary>Newest CHANGELOG version the in-game "What's new" popup has shown (shared LalaChangelog gate).</summary>
    public string? LastSeenChangelogVersion { get; set; }

    public void Save()
    {
        Svc.PluginInterface.SavePluginConfig(this);
    }
}
