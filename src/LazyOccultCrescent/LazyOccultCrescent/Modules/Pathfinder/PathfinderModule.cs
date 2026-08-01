using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.Pathfinder;

[OcelotModule(3)]
public class PathfinderModule(Plugin plugin, Config config) : Module(plugin, config)
{
    public override PathfinderConfig Config
    {
        get => PluginConfig.PathfinderConfig;
    }
}
