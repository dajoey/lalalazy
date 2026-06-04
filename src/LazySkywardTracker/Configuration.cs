using Dalamud.Configuration;

namespace LazySkywardTracker;

[System.Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
