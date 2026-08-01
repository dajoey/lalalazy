using Ocelot.Config.Attributes;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.CriticalEncounters;

public class CriticalEncountersConfig : ModuleConfig
{
    [Checkbox]
    [Label("generic.label.enabled")]
    public bool Enabled { get; set; } = true;

    [Checkbox] public bool TrackForkedTower { get; set; } = true;

    [Checkbox] public bool LogSpawn { get; set; } = false;

    [Checkbox] public bool AlertAll { get; set; } = false;

    [Checkbox] public bool AlertAzurite { get; set; } = false;

    [Checkbox] public bool AlertVerdigris { get; set; } = false;

    [Checkbox] public bool AlertMalachite { get; set; } = false;

    [Checkbox] public bool AlertRealgar { get; set; } = false;

    [Checkbox] public bool AlertCaputMortuum { get; set; } = false;

    [Checkbox] public bool AlertOrpiment { get; set; } = false;

    [Checkbox] public bool AlertOracle { get; set; } = false;

    [Checkbox] public bool AlertBerserker { get; set; } = false;

    [Checkbox] public bool AlertRanger { get; set; } = false;
}
