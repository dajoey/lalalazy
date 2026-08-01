using Ocelot.Modules;
using Ocelot.Windows;

namespace LazyOccultCrescent.Modules.Buff;

[OcelotModule(1005, 2)]
public class BuffModule(Plugin plugin, Config config) : Module(plugin, config)
{
    public override BuffConfig Config
    {
        get => PluginConfig.BuffConfig;
    }

    public override bool IsEnabled
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    public override bool ShouldUpdate
    {
        get => true;
    }

    public readonly BuffManager BuffManager = new();

    private readonly Panel panel = new();

    public override void Update(UpdateContext context)
    {
        BuffManager.Update(this);
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public bool ShouldRefreshBuffs()
    {
        return BuffManager.ShouldRefresh(this);
    }
}
