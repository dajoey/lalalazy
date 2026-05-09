using Dalamud.Configuration;
using System;

namespace AutoKupo;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; }

    public float MaxDistance { get; set; } = 5f;

    public int MaxIterations { get; set; } = 100;

    public int TalkClickDelayMs { get; set; } = 200;

    public bool AutoInteract { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
