using Ocelot.Config.Attributes;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.Data;

[Text("config.explanation")]
public class DataConfig : ModuleConfig
{
    [Checkbox]
    [Label("generic.label.enabled")]
    public bool Enabled { get; set; } = false;
}
