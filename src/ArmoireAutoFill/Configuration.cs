using Dalamud.Configuration;

namespace ArmoireAutoFill;

[System.Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public bool ScanOnLoad { get; set; } = true;

    // UI toggles
    public bool ShowOwnedItems { get; set; } = false;
    public bool HideCompleteDungeons { get; set; } = true;

    // Auto-store: when enabled, eligible items are automatically stored to the armoire
    // when the armoire UI is opened. A manual button is also available.
    // On by default since v0.4.2.0 (config v3) - the plugin lives up to its name.
    public bool AutoStoreOnOpen { get; set; } = true;

    // When true, storing also pulls eligible gear from the armoury chest.
    // Off by default: only the regular inventory (bags) is scanned.
    public bool AutoStoreIncludeArmory { get; set; } = false;

    // When true, any item that belongs to a saved gearset is never auto-stored.
    public bool SkipGearsetItems { get; set; } = true;

    // Cache of item IDs the player has unlocked in the armoire. Populated from
    // ItemFinderModule->CabinetItemUnlockBits at startup and on framework polls.
    public List<uint> ArmoireItemIds { get; set; } = [];

    /// <summary>Newest CHANGELOG version the in-game "What's new" popup has shown (shared LalaChangelog gate).</summary>
    public string? LastSeenChangelogVersion { get; set; }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
