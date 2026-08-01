using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.EventDrop;

[OcelotModule(4)]
public class EventDropModule(Plugin plugin, Config config) : Module(plugin, config)
{
    public override EventDropConfig Config
    {
        get => PluginConfig.EventDropConfig;
    }
}
