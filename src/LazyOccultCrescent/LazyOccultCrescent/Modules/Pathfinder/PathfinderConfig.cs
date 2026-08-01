using Ocelot.Config.Attributes;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.Pathfinder;

[Text("config.text")]
public class PathfinderConfig : ModuleConfig
{
    [FloatRange(50f, 500f)] public float ReturnCost { get; set; } = 300f;

    [FloatRange(10f, 500f)] public float TeleportCost { get; set; } = 50f;

    [FloatRange(10f, 100f)]
    [RangeIndicator(0.9f, 0.1f, 0.6f)]
    public float DetectionRange { get; set; } = 75f;

    [IntRange(1, 28)] [Experimental] public int MaxLevel { get; set; } = 23;

    // Bend automated routes around hostiles instead of walking through them.
    [Checkbox] public bool AvoidAggro { get; set; } = true;

    // Pause automation while the player is steering, then repath from wherever
    // they ended up rather than resuming the abandoned waypoint list.
    [Checkbox] public bool YieldToManualControl { get; set; } = true;
}
