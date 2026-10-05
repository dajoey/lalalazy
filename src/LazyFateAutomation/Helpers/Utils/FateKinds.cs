namespace LazyFateAutomation.Helpers.Utils;

/// <summary>
///     Config-facing FATE enums. Moved here (from PublicEvent.cs / FateToolKit.cs) so they live in a
///     file with no Dalamud references: the offline test harness compiles this file together with
///     Configuration.cs to assert old configs still load with unchanged behavior.
/// </summary>

public enum FateType {
    Normal,
    DynamicEvent, // forays
    MechaEvent, // cosmic exploration
}

/// <summary>The game's FATE rule classification (Fate sheet Rule column).</summary>
public enum FateRule : byte {
    None = 0,
    Normal = 1, // trash fates or boss fates
    Collect = 2, // pick up EventObjects or get them from killing mobs
    Escort = 3, // guide some npc to the finish line
    Defend = 4, // defend objectives like crates from being destroyed
    EventFate = 5, // used for seasonal event fates, like Little Ladies Day, Hatching Tide
    Chase = 6, // that one special fate in The Peaks
    ConcertedWorks = 7, // rebuilding the firmament fates
    Fete = 8, // firmament fates
}

/// <summary>Currencies FATEs reward, mapped from TerritoryType classes (see CHANGELOG v0.0.3.3).</summary>
public enum FateCurrency {
    None = 0,
    CompanySeals, // A Realm Reborn through Stormblood zones
    BicolorGemstones, // Shadowbringers and later zones (Shared FATE system, patch 5.0+)
    YokaiMedals, // the Yo-kai Watch event zones (watch + matching minion out)
}

/// <summary>What to do when no FATE rewarding the focused currency is up.</summary>
public enum CurrencyFocusFallback {
    NormalSelection = 0, // keep grinding normally (achievement-guided / random same-expansion swap)
    Idle, // stay put and wait for focused-currency fates
}

public enum FateSortCriteria {
    HasBonusWithTwist,
    Progress,
    HasBonus,
    TimeRemainingUrgent,
    Distance,
    TimeRemaining,
    Level,
    Name,
}

public class FateSortOrder {
    public FateSortCriteria Criteria { get; set; }
    public bool Descending { get; set; }
}
