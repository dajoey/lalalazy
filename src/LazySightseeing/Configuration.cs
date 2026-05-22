using Dalamud.Configuration;

namespace LazySightseeing;

[System.Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Configured default inn name (Gridania, Limsa, Ul'dah, Kugane, Crystarium, Sharlayan)
    public string DefaultInn { get; set; } = "Gridania";

    // List of Sightseeing Vista IDs that are user-selected for completion.
    // If this list contains IDs, we only attempt those. Otherwise, we attempt all uncompleted ones.
    public List<uint> SelectedSightIds { get; set; } = [];

    // Option to skip sights whose weather/time window is currently closed.
    public bool SkipIfWindowNotOpen { get; set; } = true;

    // Optional delay between actions/emotes (in milliseconds)
    public int EmoteIntervalMs { get; set; } = 4000;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
