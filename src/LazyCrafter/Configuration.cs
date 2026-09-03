using Dalamud.Configuration;

namespace LazyCrafter;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Inventory sources AllaganTools may be asked about. Everything on except FC chest
    /// (Scope §0 "Inventory scope"). Real enum + wiring arrives in Phase 3.
    /// </summary>
    public Dictionary<string, bool> EnabledSources { get; set; } = new()
    {
        ["Bags"] = true,
        ["ArmouryChest"] = true,
        ["Saddlebag"] = true,
        ["Retainers"] = true,
        ["AltCharacters"] = true,
        ["FCChest"] = false,
        ["GlamourDresser"] = true,
    };

    /// <summary>Idempotent; called once from the Plugin constructor.</summary>
    public void MigrateIfNeeded()
    {
        if (Version >= CurrentVersion) return;
        Version = CurrentVersion;
    }
}
