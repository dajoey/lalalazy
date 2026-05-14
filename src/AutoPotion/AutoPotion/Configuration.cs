using Dalamud.Configuration;

namespace AutoPotion;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool MasterEnable { get; set; } = true;
    public bool OnlyInCombat { get; set; } = true;
    public bool OnlyInDuty { get; set; } = false;

    public bool HpPotionEnable { get; set; } = true;
    public float HpPotionThreshold { get; set; } = 60f;

    public bool RegenPotionEnable { get; set; } = true;
    public float RegenPotionThreshold { get; set; } = 80f;
}
