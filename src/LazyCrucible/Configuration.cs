using Dalamud.Configuration;

namespace LazyCrucible;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary> Fill the ten-familiar run roster on the Bentbranch Meadows entry menu. </summary>
    public bool AutoRoster { get; set; } = true;

    /// <summary> Fill the three Battlehorn slots on the formation screen before each fight. </summary>
    public bool AutoHorns { get; set; } = true;

    /// <summary> Leave familiar screens alone while AutoDuty is running a board (it picks its own team). </summary>
    public bool YieldToAutoDuty { get; set; } = true;

    /// <summary> Print each fight's picks (and why) to chat when they are set. </summary>
    public bool AnnouncePicks { get; set; } = true;

    /// <summary> Write every Crucible screen and its button presses to the Dalamud log (read-only). </summary>
    public bool RecordScreens { get; set; } = true;

    /// <summary> Newest CHANGELOG version the in-game "What's new" popup has shown (shared LalaChangelog gate). </summary>
    public string? LastSeenChangelogVersion { get; set; }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
