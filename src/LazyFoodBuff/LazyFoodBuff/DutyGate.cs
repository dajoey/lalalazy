namespace LazyFoodBuff;

/// <summary>
///     Where "Only eat in combat duties" allows eating. PURE (no Dalamud types): compiled into
///     tests/LazyFoodBuff.TelemetryHarness. The live values come from FoodService.
/// </summary>
internal static class DutyGate
{
    // Combat duty TerritoryIntendedUse values (from ECommons TerritoryIntendedUseEnum).
    private static readonly HashSet<uint> CombatDutyIntendedUses = new()
    {
        3,    // Dungeon
        8,    // Alliance Raid
        10,   // Trial
        16,   // Raid
        17,   // Raid (alternate)
        33,   // Treasure Map Duty (has combat)
        52,   // Large Scale Raid (Bozja Dalriada etc.)
        53,   // Large Scale Savage Raid
        57,   // Criterion Duty
        58,   // Criterion Savage Duty
        31,   // Deep Dungeon (Palace of the Dead, Heaven-on-High, Eureka Orthos)
        61,   // Occult Crescent (South Horn, North Horn)
    };

    private const uint VariantDungeon = 4;

    /// <summary> Crucible of the Unbroken boards 1-5 (territories 1339-1343) are the only rows with this use. </summary>
    private const uint CrucibleOfTheUnbroken = 62;

    /// <summary> Central Shroud, where the Crucible entry NPC stands by the Bentbranch Meadows aetheryte. </summary>
    private const uint CentralShroud = 148;

    /// <summary> PlaceName 70 "Bentbranch" (area) and 94 "Bentbranch Meadows" (aetheryte sub-area). </summary>
    private static readonly HashSet<uint> BentbranchPlaceNames = new() { 70, 94 };

    /// <summary> Beastmaster, the only job that can enter the Crucible. </summary>
    private const uint Beastmaster = 43;

    /// <summary> Whether the character is somewhere "Only eat in combat duties" allows eating. </summary>
    public static bool IsCombatDuty(uint territoryId, uint intendedUse, uint areaPlaceNameId, uint subAreaPlaceNameId, uint classJobId)
    {
        if (territoryId == 0)
            return false;
        if (CombatDutyIntendedUses.Contains(intendedUse))
            return true;
        if (intendedUse == VariantDungeon)
            return true;
        // Crucible boards: meals are one of the two consumable kinds allowed inside.
        if (intendedUse == CrucibleOfTheUnbroken)
            return true;
        // Crucible entry: eat before entering (eaten inside, HP does not refill to the new maximum).
        if (territoryId == CentralShroud && classJobId == Beastmaster
            && (BentbranchPlaceNames.Contains(areaPlaceNameId) || BentbranchPlaceNames.Contains(subAreaPlaceNameId)))
            return true;
        return false;
    }
}
