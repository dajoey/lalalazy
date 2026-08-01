using Ocelot.Modules;
using Ocelot.Windows;

namespace LazyOccultCrescent.Modules.MobFarmer;

[OcelotModule(int.MaxValue - 2)]
public class MobFarmerModule : Module
{
    public override MobFarmerConfig Config
    {
        get => PluginConfig.MobFarmerConfig;
    }

    public override bool IsEnabled
    {
        get => Config.Enabled;
    }

    private readonly Panel panel = new();

    public readonly Scanner Scanner;

    public readonly Farmer Farmer;

    public MobFarmerModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        Scanner = new Scanner(this);
        Farmer = new Farmer(this);
    }

    public override void Update(UpdateContext context)
    {
        Scanner.Tick(context.Framework);
        Farmer.Update(context.ForModule(this));
    }

    public override void Render(RenderContext context)
    {
        Farmer.Draw(context.ForModule(this));
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public override void Dispose()
    {
        Farmer.Dispose();
    }
}
