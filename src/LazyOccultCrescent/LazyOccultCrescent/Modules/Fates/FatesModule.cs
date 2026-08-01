using System.Collections.Generic;
using Ocelot.Modules;
using Ocelot.Windows;

namespace LazyOccultCrescent.Modules.Fates;

[OcelotModule(1001, 5)]
public class FatesModule : Module
{
    public override FatesConfig Config
    {
        get => PluginConfig.FatesConfig;
    }

    public override bool IsEnabled
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    public override bool ShouldUpdate
    {
        get => true;
    }

    public readonly FateTracker tracker = new();

    public Dictionary<uint, Fate> fates
    {
        get => tracker.Fates;
    }

    private readonly Panel panel = new();

    private readonly Alerter alerter;

    public FatesModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        alerter = new Alerter(this);
    }

    public override void Update(UpdateContext context)
    {
        tracker.Update(context);
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        fates.Clear();
    }

    public override void Dispose()
    {
        base.Dispose();
        alerter.Dispose();
    }
}
