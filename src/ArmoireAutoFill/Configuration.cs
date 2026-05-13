using Dalamud.Configuration;

namespace ArmoireAutoFill;

[System.Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool ScanOnLoad { get; set; } = true;

    // Cache of item IDs currently stored in the player's armoire. Populated when
    // the player opens the armoire UI at an inn; persisted so the plugin can
    // surface armoire ownership across sessions without re-prompting.
    public List<uint> ArmoireItemIds { get; set; } = [];

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
