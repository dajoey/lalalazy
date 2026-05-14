using Dalamud.Configuration;

namespace ArmoireAutoFill;

[System.Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public bool ScanOnLoad { get; set; } = true;

    // UI toggles
    public bool ShowOwnedItems { get; set; } = false;
    public bool HideCompleteDungeons { get; set; } = true;

    // Cache of item IDs the player has unlocked in the armoire. Populated from
    // ItemFinderModule->CabinetItemUnlockBits at startup and on framework polls.
    public List<uint> ArmoireItemIds { get; set; } = [];

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
