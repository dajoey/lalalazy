// Deterministic game data for the render harness: real zone, grind-mode and FATE names (game sheets, the plugin's own
// grind-mode tables), positioned around a player standing at the origin.
using FFXIVClientStructs.FFXIV.Client.Game.Fate;

namespace LazyFateAutomation;

internal static class Fixtures
{
    private static readonly (uint Id, string Name, uint Ex, string ExName, bool NoTeleport)[] ZoneRows =
    [
        (134, "Middle La Noscea", 0, "A Realm Reborn", false), (135, "Lower La Noscea", 0, "A Realm Reborn", false),
        (137, "Eastern La Noscea", 0, "A Realm Reborn", false), (138, "Western La Noscea", 0, "A Realm Reborn", false),
        (139, "Upper La Noscea", 0, "A Realm Reborn", false), (140, "Western Thanalan", 0, "A Realm Reborn", false),
        (141, "Central Thanalan", 0, "A Realm Reborn", false), (145, "Eastern Thanalan", 0, "A Realm Reborn", false),
        (146, "Southern Thanalan", 0, "A Realm Reborn", false), (147, "Northern Thanalan", 0, "A Realm Reborn", false),
        (148, "Central Shroud", 0, "A Realm Reborn", false), (152, "East Shroud", 0, "A Realm Reborn", false),
        (153, "South Shroud", 0, "A Realm Reborn", false), (154, "North Shroud", 0, "A Realm Reborn", false),
        (155, "Coerthas Central Highlands", 0, "A Realm Reborn", false), (156, "Mor Dhona", 0, "A Realm Reborn", false),
        (180, "Outer La Noscea", 0, "A Realm Reborn", false),
        (397, "Coerthas Western Highlands", 1, "Heavensward", false), (398, "The Dravanian Forelands", 1, "Heavensward", false),
        (399, "The Dravanian Hinterlands", 1, "Heavensward", true), (400, "The Churning Mists", 1, "Heavensward", false),
        (401, "The Sea of Clouds", 1, "Heavensward", false), (402, "Azys Lla", 1, "Heavensward", false),
        (612, "The Fringes", 2, "Stormblood", false), (613, "The Ruby Sea", 2, "Stormblood", false),
        (614, "Yanxia", 2, "Stormblood", false), (620, "The Peaks", 2, "Stormblood", false),
        (621, "The Lochs", 2, "Stormblood", false), (622, "The Azim Steppe", 2, "Stormblood", false),
        (813, "Lakeland", 3, "Shadowbringers", false), (814, "Kholusia", 3, "Shadowbringers", false),
        (815, "Amh Araeng", 3, "Shadowbringers", false), (816, "Il Mheg", 3, "Shadowbringers", false),
        (817, "The Rak'tika Greatwood", 3, "Shadowbringers", false), (818, "The Tempest", 3, "Shadowbringers", false),
        (956, "Labyrinthos", 4, "Endwalker", false), (957, "Thavnair", 4, "Endwalker", false),
        (958, "Garlemald", 4, "Endwalker", false), (959, "Mare Lamentorum", 4, "Endwalker", false),
        (960, "Ultima Thule", 4, "Endwalker", false), (961, "Elpis", 4, "Endwalker", false),
        (1187, "Urqopacha", 5, "Dawntrail", false), (1188, "Kozama'uka", 5, "Dawntrail", false),
        (1189, "Yak T'el", 5, "Dawntrail", false), (1190, "Shaaloani", 5, "Dawntrail", false),
        (1191, "Heritage Found", 5, "Dawntrail", false), (1192, "Living Memory", 5, "Dawntrail", false),
    ];

    public static IReadOnlyList<FateZone> Zones { get; } =
    [
        .. ZoneRows.Select(z => new FateZone(z.Id, z.Name, z.Ex, z.ExName,
            z.NoTeleport ? FateZoneLogic.ZoneClass.NoTeleportFateZone : FateZoneLogic.ZoneClass.FateZone)),
    ];

    // Zones of the Yo-kai Watch event minions (the plugin's own table, all entries).
    public static readonly uint[] YokaiZoneIds =
    [
        148, 135, 141, 152, 138, 145, 153, 139, 146, 154, 180, 134, 140,
        612, 613, 614, 620, 621, 622, 397, 398, 399, 400, 401, 402,
    ];

    /// <summary>"N left" figures the relic and gemstone modes read from the game inventory; scenarios may change them.</summary>
    public static readonly Dictionary<string, string?> ModeRemaining = new()
    {
        ["Gemstones"] = "1156 left",
        ["Yo-kai Watch (Medals)"] = null,
        ["Atma (Zodiac)"] = "126 left",
        ["Luminous Crystals (Anima)"] = "63 left",
        ["Memories (Resistance)"] = "Need relic equipped!",
        ["Law's Order (Resistance)"] = "Need Quest #69575",
        ["Demiatmas (Phantom)"] = "12 left",
        ["Paste (Phantom)"] = "1200 left",
    };

    private static HashSet<uint> Set(params uint[] ids) => [.. ids];

    public static IReadOnlyList<IFateGrindMode> GrindModes { get; } =
    [
        new HarnessGrindMode("None", null, s => s.RemainingUntilCompleted is { } r && r > 0 ? $"{r} fates" : null),
        new HarnessGrindMode("Gemstones", Zones.Where(z => z.ExVersionId >= 3).Select(z => z.TerritoryId).ToHashSet(), _ => ModeRemaining["Gemstones"]),
        new HarnessGrindMode("Yo-kai Watch (Medals)", YokaiZoneIds.ToHashSet(), s => s.RemainingUntilCompleted is { } r && r > 0 ? $"{r} fates" : ModeRemaining["Yo-kai Watch (Medals)"]),
        new HarnessGrindMode("Atma (Zodiac)", Set(148, 146, 139, 152, 145, 134, 140, 180, 135, 154, 141, 138), _ => ModeRemaining["Atma (Zodiac)"]),
        new HarnessGrindMode("Luminous Crystals (Anima)", Set(397, 401, 402, 398, 400, 399), _ => ModeRemaining["Luminous Crystals (Anima)"]),
        new HarnessGrindMode("Memories (Resistance)", Set(397, 401, 398, 400, 399, 402), _ => ModeRemaining["Memories (Resistance)"]),
        new HarnessGrindMode("Law's Order (Resistance)", Set(612, 620, 621, 613, 614, 622), _ => ModeRemaining["Law's Order (Resistance)"]),
        new HarnessGrindMode("Demiatmas (Phantom)", Set(1187, 1188, 1189, 1190, 1191, 1192), _ => ModeRemaining["Demiatmas (Phantom)"]),
        new HarnessGrindMode("Paste (Phantom)", Set(1187, 1188, 1189, 1190, 1191, 1192), _ => ModeRemaining["Paste (Phantom)"]),
    ];

    private static PublicEvent Fate(uint id, string name, byte level, int progress, float remaining, float x, float z,
        bool bonus = false, FateRule rule = FateRule.Normal, int duration = 900, FateState state = FateState.Running,
        bool onMap = true, ItemHandle? item = null)
        => new((nint)(0x1F4A0000 + (id * 0x40)), FateType.Normal, id)
        {
            Name = name,
            Level = level,
            Progress = progress,
            TimeRemaining = remaining,
            Position = new Vector3(x, 12f, z),
            HasBonus = bonus,
            Rule = rule,
            Duration = duration,
            State = state,
            IsOnMap = onMap,
            EventItem = item,
            MotivationNpcId = 1_010_000 + id,
        };

    /// <summary>Kozama'uka, nine live FATEs: eligible, over-progress, too little time left, over-long, blacklisted, pending.</summary>
    public static PublicEvent[] KozamaukaFates() =>
    [
        Fate(1886, "Young Volcanoes", 93, 34, 522, -142, 96, bonus: true),
        Fate(1888, "By Your Powers Combined", 93, 71, 310, 66, -31),
        Fate(1887, "Claws and Effect", 93, 93, 240, 211, 140),
        Fate(1894, "Lay Off the Horns", 93, 12, 678, -58, -204, rule: FateRule.Defend),
        Fate(1895, "Could've Found Something Bigger", 94, 0, 888, 305, 87, rule: FateRule.Defend),
        Fate(1889, "Gust Stop Already", 93, 48, 96, -233, -120),
        Fate(1891, "Salty Showdown", 93, 5, 1100, 120, 260, duration: 1200),
        Fate(1906, "Escape Shroom", 94, 0, -1, 410, -75, rule: FateRule.Collect, state: FateState.Preparing, onMap: false,
            item: new ItemHandle(44012, "Ik'iit Ear")),
        Fate(1905, "Porting Is Such Sweet Sorrow", 94, 57, 405, -317, 42, rule: FateRule.Defend),
    ];

    /// <summary>The FATE the populated scenarios put on the blacklist (Salty Showdown).</summary>
    public static readonly uint BlacklistedFate = 1891;
}
