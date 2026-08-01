using Ocelot.Config.Attributes;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.WindowManager;

public class WindowManagerConfig : ModuleConfig
{
    [Checkbox] public bool OpenMainOnStartUp { get; set; } = false;

    [Checkbox] public bool OpenMainOnEnter { get; set; } = true;

    [Checkbox] public bool CloseMainOnExit { get; set; } = true;

    [Checkbox] public bool HideMainInCombat { get; set; } = false;

    [Checkbox] public bool OpenConfigOnStartUp { get; set; } = false;

    [Checkbox] public bool OpenConfigOnEnter { get; set; } = false;

    [Checkbox] public bool CloseConfigOnExit { get; set; } = true;

    [Checkbox] public bool HideConfigInCombat { get; set; } = false;
}
