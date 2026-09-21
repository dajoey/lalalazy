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

    /// <summary> Beast Feed picker (opened by buying feed): choose which familiar eats it and confirm. </summary>
    public bool AutoFeed { get; set; } = true;

    /// <summary> Shop: buy the items, gear and feed the fights ahead call for (never sells, never closes the shop). </summary>
    public bool AutoShop { get; set; } = true;

    /// <summary> Treasure coffer: take the best choice. </summary>
    public bool AutoTreasure { get; set; } = true;

    /// <summary> Spoils after a battle: "Take all" when everything fits. </summary>
    public bool AutoSpoils { get; set; } = true;

    /// <summary> Campsite: select which familiars rest (the Rest button is left to the player). </summary>
    public bool AutoCamp { get; set; } = true;

    /// <summary> Score bonus the feed choices may chase; survival first by default. </summary>
    public ScoreGoal ScoreGoal { get; set; } = ScoreGoal.SurvivalFirst;

    /// <summary> Open the fight guide on the upcoming fight when the board layout or the Battlehorn screen shows it. </summary>
    public bool GuideAutoOpen { get; set; } = true;

    /// <summary> Newest CHANGELOG version the in-game "What's new" popup has shown (shared LalaChangelog gate). </summary>
    public string? LastSeenChangelogVersion { get; set; }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
