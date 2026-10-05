using LazyFateAutomation.Helpers.Utils;

namespace LazyFateAutomation;

/// <summary>
///     Pure zone classification + currency mapping, derived from the game sheets so the pools extend
///     to future zones without per-zone code. No Dalamud references: the offline test harness
///     compiles this file and asserts it against real TerritoryType/Aetheryte fixtures.
///
///     Evidence (xivapi v2, 2026-10-05, see task tasks-20261005-lazyfate-all-zones-filters-currency-ui-01):
///     - 47 territories have FATEs: IsInUse && Mount && !IsPvpZone && TerritoryIntendedUse == 1 (Overworld),
///       grouped ARR 17 / HW 6 / SB 6 / ShB 6 / EW 6 / DT 6.
///     - 46/47 have a primary aetheryte (IsAetheryte && !Invisible); The Dravanian Hinterlands (399) is
///       aethernet-only (reachable on foot from Idyllshire), so zone swaps cannot teleport into it.
///     - Bozja (41/48), Eureka (41), Occult Crescent (61), Cosmic Exploration (60) and The Diadem have
///       NO aetheryte rows at all and are not Overworld; TeleportTo cannot route there.
///     - Bicolor Gemstones are the Shared FATE currency introduced in Shadowbringers ("Each FATE completed
///       with a Gold Medal rating will reward Bicolor Gemstones ... 16 Dawntrail / 14 Endwalker /
///       12 Shadowbringers zones", consolegameswiki Bicolor_Gemstone) => ExVersion >= 3.
///       Grand Company seals: FATEs in ARR/HW/SB zones => ExVersion <= 2.
/// </summary>
public static class FateZoneLogic {
    // TerritoryIntendedUse sheet row ids == FFXIVClientStructs FFXIV.Client.Enums.TerritoryIntendedUse values.
    public const uint UseTown = 0;
    public const uint UseOverworld = 1;
    public const uint UseEureka = 41;
    public const uint UseDiadem = 47;
    public const uint UseBozja = 48;
    public const uint UseCosmicExploration = 60;
    public const uint UseOccultCrescent = 61;

    public enum ZoneClass {
        NotAFateZone, // towns, instances, pvp, unused
        FateZone, // overworld FATE zone with a teleport aetheryte
        NoTeleportFateZone, // overworld FATE zone with no in-zone aetheryte (e.g. Dravanian Hinterlands)
        ForayZone, // Bozja/Eureka/Occult Crescent/Cosmic Exploration: no aetheryte teleport target
    }

    /// <summary>Classifies one TerritoryType row from the columns the zone rules depend on.</summary>
    public static ZoneClass ClassifyZone(bool isInUse, bool mount, bool isPvpZone, uint intendedUse, bool hasPrimaryAetheryte) {
        if (!isInUse || !mount || isPvpZone) return ZoneClass.NotAFateZone;
        return intendedUse switch {
            UseOverworld => hasPrimaryAetheryte ? ZoneClass.FateZone : ZoneClass.NoTeleportFateZone,
            UseEureka or UseDiadem or UseBozja or UseCosmicExploration or UseOccultCrescent => ZoneClass.ForayZone,
            _ => ZoneClass.NotAFateZone,
        };
    }

    /// <summary>
    ///     Currency rewarded by FATEs in a zone; null when the zone class carries no standard currency
    ///     (NoTeleportFateZone cannot be swapped into; ForayZone fates reward no standard currency).
    /// </summary>
    public static FateCurrency? ZoneCurrency(uint exVersion, ZoneClass zoneClass) {
        if (zoneClass != ZoneClass.FateZone) return null;
        return exVersion >= 3 ? FateCurrency.BicolorGemstones : FateCurrency.CompanySeals;
    }
}

/// <summary>
///     Pure FATE eligibility decision, extracted from FateToolKit.FateConditions so the FATE-type
///     filter is asserted offline against the same code the game runs. No Dalamud references.
/// </summary>
public static class FateEligibility {
    public static bool IsEligible(
        int duration, int maxDuration,
        int progress, int maxProgress,
        float timeRemaining, int minTimeRemaining,
        bool blacklisted, bool isPending,
        FateRule rule, IReadOnlySet<FateRule> excludedRules
    )
        => duration <= maxDuration
        && progress <= maxProgress
        && (timeRemaining < 0 || timeRemaining > minTimeRemaining)
        && !blacklisted
        && !isPending
        && !excludedRules.Contains(rule);

    public static void AppendFailedConditions(
        List<string> failed,
        int duration, int maxDuration,
        int progress, int maxProgress,
        float timeRemaining, int minTimeRemaining,
        bool blacklisted, bool isPending,
        FateRule rule, IReadOnlySet<FateRule> excludedRules
    ) {
        if (duration > maxDuration)
            failed.Add($"Duration {duration}s > MaxDuration {maxDuration}s");

        if (progress > maxProgress)
            failed.Add($"Progress {progress}% > MaxProgress {maxProgress}%");

        if (timeRemaining >= 0 && timeRemaining <= minTimeRemaining)
            failed.Add($"TimeRemaining {timeRemaining:F0}s <= MinTimeRemaining {minTimeRemaining}s");

        if (blacklisted)
            failed.Add("Blacklisted");

        if (isPending)
            failed.Add("Pending (not yet active / not on map)");

        if (excludedRules.Contains(rule))
            failed.Add($"{rule} FATEs are excluded in settings");
    }
}
