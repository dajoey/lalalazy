using LazyOccultCrescent.Data;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.Pathfinder;

[OcelotModule(3)]
public class PathfinderModule(Plugin plugin, Config config) : Module(plugin, config)
{
    public override PathfinderConfig Config
    {
        get => PluginConfig.PathfinderConfig;
    }

    public override bool ShouldUpdate
    {
        get => true;
    }

    // Push config down to the statics the movement chains read. Chains are
    // constructed all over the plugin without a module reference, so syncing
    // here once a tick is cheaper than threading config through every call site.
    public override void Update(UpdateContext context)
    {
        AggroAvoidance.Enabled = Config.AvoidAggro;
        ManualControl.Enabled = Config.YieldToManualControl;
    }
}
