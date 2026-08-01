using System.Collections.Generic;
using LazyOccultCrescent.Data;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Modules;
using Ocelot.Windows;

namespace LazyOccultCrescent.Modules.CriticalEncounters;

[OcelotModule(1002, 6)]
public class CriticalEncountersModule : Module
{
    public override CriticalEncountersConfig Config
    {
        get => PluginConfig.CriticalEncountersConfig;
    }

    public override bool IsEnabled
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    public override bool ShouldUpdate
    {
        get => true;
    }

    public readonly CriticalEncounterTracker Tracker;

    public Dictionary<uint, DynamicEvent> CriticalEncounters
    {
        get => Tracker.CriticalEncounters;
    }

    public Dictionary<uint, EventProgress> Progress
    {
        get => Tracker.Progress;
    }

    private readonly Panel panel = new();

    private readonly Alerter alerter;

    public CriticalEncountersModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        Tracker = new CriticalEncounterTracker(this);
        alerter = new Alerter(this);
    }

    public override void Update(UpdateContext context)
    {
        Tracker.Tick(context.Framework);
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        CriticalEncounters.Clear();
    }

    public override void Dispose()
    {
        base.Dispose();
        alerter.Dispose();
    }
}
