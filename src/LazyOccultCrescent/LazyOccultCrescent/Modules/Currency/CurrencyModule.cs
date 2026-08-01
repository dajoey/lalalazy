using Ocelot.Modules;
using Ocelot.Windows;

namespace LazyOccultCrescent.Modules.Currency;

[OcelotModule(int.MaxValue - 1001, 3)]
public class CurrencyModule(Plugin plugin, Config config) : Module(plugin, config)
{
    public override CurrencyConfig Config
    {
        get => PluginConfig.CurrencyConfig;
    }

    public override bool ShouldRender
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    public override bool ShouldUpdate
    {
        get => true;
    }

    public readonly CurrencyTracker Tracker = new();

    private readonly Panel panel = new();

    public override void Update(UpdateContext context)
    {
        Tracker.Tick(context.Framework);
    }

    public override void OnTerritoryChanged(uint _)
    {
        Tracker.Reset();
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }
}
