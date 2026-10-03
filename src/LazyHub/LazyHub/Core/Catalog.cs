namespace LazyHub.Core;

/// <summary>One lalalazy plugin the hub knows about.</summary>
/// <param name="InternalName">Dalamud InternalName, matched against the installed plugin list.</param>
/// <param name="DisplayName">Name shown in the window (the same text as pluginmaster.json "Name").</param>
/// <param name="IconFile">File name of the 64x64 icon in the Resources folder.</param>
public sealed record CatalogEntry(string InternalName, string DisplayName, string IconFile);

/// <summary>
/// The suite, in display order. tests/LazyHub.Harness compares this list with the repo's
/// pluginmaster.json, so a plugin added to the repo without an entry here fails the harness.
/// No Dalamud types in this folder: it is compiled into the harness as plain source.
/// </summary>
public static class Catalog
{
    /// <summary>Icon of the hub itself (the bare sleeping lalafell, the repo identity mark).</summary>
    public const string HubIconFile = "lazyhub-icon.png";

    public static IReadOnlyList<CatalogEntry> Plugins { get; } =
    [
        new("GluttonyCombo",        "Gluttony Combo",         "gluttonycombo-icon.png"),
        new("PvPSolver",            "PvP Solver",             "pvpsolver-icon.png"),
        new("AutoPotion",           "AutoPotion",             "autopotion-icon.png"),
        new("LazyFoodBuff",         "LazyFoodBuff",           "lazyfoodbuff-icon.png"),
        new("LazyMarketCompanion",  "Lazy Market Companion",  "lazymarketcompanion-icon.png"),
        new("LazyFateAutomation",   "Lazy Fate Automation",   "lazyfateautomation-icon.png"),
        new("LazyCurrencySpender",  "Lazy Currency Spender",  "lazycurrencyspender-icon.png"),
        new("LazyFishSitter",       "Lazy Fish Sitter",       "lazyfishsitter-icon.png"),
        new("LazyRetainerLive",     "LazyRetainerLive",       "lazyretainerlive-icon.png"),
        new("LazyCrafter",          "LazyCrafter",            "lazycrafter-icon.png"),
        new("LazyCrucible",         "LazyCrucible",           "lazycrucible-icon.png"),
        new("LazyGearCollector",    "Lazy Gear Collector",    "lazygearcollector-icon.png"),
        new("LazySkywardTracker",   "Lazy Skyward Tracker",   "lazyskywardtracker-icon.png"),
        new("ArmoireAutoFill",      "Armoire Auto-Fill",      "armoire-icon.png"),
        new("LazyWTMath",           "Lazy WT Math",           "lazywtmath-icon.png"),
    ];

    /// <summary>Exact (case-sensitive) lookup by InternalName; null when the plugin is not part of the suite.</summary>
    public static CatalogEntry? Find(string internalName)
    {
        foreach (var p in Plugins)
            if (string.Equals(p.InternalName, internalName, StringComparison.Ordinal))
                return p;
        return null;
    }
}
