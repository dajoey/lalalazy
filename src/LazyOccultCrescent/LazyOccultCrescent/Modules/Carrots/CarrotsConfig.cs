using Ocelot.Config.Attributes;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.Carrots;

public class CarrotsConfig : ModuleConfig
{
    [Checkbox]
    [Label("generic.label.enabled")]
    public bool Enabled { get; set; } = true;

    [Checkbox]
    [DependsOn(nameof(Enabled))]
    public bool DrawLineToCarrots { get; set; } = true;

    public bool ShouldDrawLineToCarrots
    {
        get => IsPropertyEnabled(nameof(DrawLineToCarrots));
    }

    [Checkbox]
    [Experimental]
    [Illegal]
    [RequiredPlugin("vnavmesh", "Lifestream")]
    [DependsOn(nameof(Enabled))]
    [Label("modules.carrots.config.show_hunt_button.label")]
    public bool EnableCarrotHunt { get; set; } = false;

    public bool ShouldEnableCarrotHunt
    {
        get => IsPropertyEnabled(nameof(EnableCarrotHunt));
    }

    [Checkbox]
    [Experimental]
    [DependsOn(nameof(Enabled), nameof(EnableCarrotHunt))]

    public bool RepeatCarrotHunt { get; set; } = false;
}
