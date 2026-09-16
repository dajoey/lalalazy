namespace GluttonyCombo.Combos.PvE;

// Beastmaster familiar roster (fork, BST rebuild 2026-09-16). GENERATED from the 7.56 game sheets
// (xivapi v2 schema exdschema@2:latest: XBMPet -> Pet.Abilities[0]=Trick skill, [1]=Tempered Release
// skill; BNpcName = 14406 + row; BNpcBase = 18915 + row, live-confirmed for wespe 18925, crab 18930,
// flying trap 18935). Release classes are from the verbatim Tempered Release tooltips.
// Pure data, no Dalamud types: compiled into tests/GluttonyCombo.BSTRotationHarness.
//
// Ground truth that drives the rotation (research/bst-live-evidence.md, research/bst-gamedata.md):
//  - Only wespe's Tempered Release (Final Sting, all-HP cost) makes the familiar retreat. On
//    2026-09-16 17:48 the old rotation pressed it right after summoning - the "pet sacrificed
//    moments after summoning" report.
//  - Sleep / knockback / draw-in releases are off in automation unless allowed in config.

/// <summary> What a familiar's Tempered Release skill does, for automation policy. </summary>
[System.Flags]
public enum BeastmasterReleaseTraits : ushort
{
    None = 0,
    Damage = 1 << 0,
    AoE = 1 << 1,
    /// <summary> The familiar retreats (all-HP cost): treat the release as the familiar's exit. </summary>
    Exit = 1 << 2,
    Sleep = 1 << 3,
    Knockback = 1 << 4,
    DrawIn = 1 << 5,
    CrowdControl = 1 << 6,
    TargetDebuff = 1 << 7,
    PartyBuff = 1 << 8,
    Mitigation = 1 << 9,
    PetBuff = 1 << 10,
    /// <summary> The familiar casts for 3 s. </summary>
    PetCast = 1 << 11,
}

/// <summary> One Master's Bestiary familiar. </summary>
public readonly record struct BeastmasterBeast(
    byte Row,
    string Name,
    BeastmasterKinType Kin,
    BeastmasterAffinity TrickAffinity,
    uint TrickSkillId,
    uint ReleaseSkillId,
    BeastmasterReleaseTraits Release,
    bool TrickKnocksBack,
    byte CaptureLevel);

internal static class BST_Beasts
{
    /// <summary> BNpcBase of XBMPet row 1 minus one (row N = 18915 + N). </summary>
    public const uint BNpcBaseOffset = 18915;

    public const int Count = 50;

    /// <summary> Index 0 is unused so <c>All[row]</c> works directly; rows 1-50. </summary>
    public static readonly BeastmasterBeast[] All =
    [
        default,
        new(1, "Cu Sith", BeastmasterKinType.Beastkin, BeastmasterAffinity.Rampant, 44935, 44936, BeastmasterReleaseTraits.Damage, false, 1),
        new(2, "squirrel", BeastmasterKinType.Beastkin, BeastmasterAffinity.Rampant, 44937, 44938, BeastmasterReleaseTraits.PartyBuff, false, 1),
        new(3, "lamb", BeastmasterKinType.Beastkin, BeastmasterAffinity.Rampant, 44939, 44940, BeastmasterReleaseTraits.Sleep, false, 3),
        new(4, "pugil", BeastmasterKinType.Wavekin, BeastmasterAffinity.Durant, 44941, 44942, BeastmasterReleaseTraits.Mitigation, false, 4),
        new(5, "opo-opo", BeastmasterKinType.Beastkin, BeastmasterAffinity.Rampant, 44943, 44944, BeastmasterReleaseTraits.Damage, false, 5),
        new(6, "dodo", BeastmasterKinType.Cloudkin, BeastmasterAffinity.Eldritch, 44945, 44946, BeastmasterReleaseTraits.Mitigation, false, 4),
        new(7, "coblyn", BeastmasterKinType.Soulkin, BeastmasterAffinity.Eldritch, 44947, 44948, BeastmasterReleaseTraits.Mitigation, false, 6),
        new(8, "diremite", BeastmasterKinType.Vilekin, BeastmasterAffinity.Rampant, 44949, 44950, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.Knockback, false, 10),
        new(9, "megalocrab", BeastmasterKinType.Wavekin, BeastmasterAffinity.Durant, 44951, 44952, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.TargetDebuff, false, 10),
        new(10, "wespe", BeastmasterKinType.Vilekin, BeastmasterAffinity.Volant, 44953, 44954, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.Exit, false, 1),
        new(11, "vulture", BeastmasterKinType.Cloudkin, BeastmasterAffinity.Volant, 44955, 44956, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE, false, 6),
        new(12, "mandragora", BeastmasterKinType.Seedkin, BeastmasterAffinity.Rampant, 44957, 44958, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE, false, 5),
        new(13, "geshunpest", BeastmasterKinType.Ashkin, BeastmasterAffinity.Eldritch, 44959, 44960, BeastmasterReleaseTraits.PartyBuff, false, 14),
        new(14, "puk", BeastmasterKinType.Scalekin, BeastmasterAffinity.Rampant, 44961, 44962, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.Knockback, false, 4),
        new(15, "crab", BeastmasterKinType.Wavekin, BeastmasterAffinity.Durant, 44963, 44964, BeastmasterReleaseTraits.PetBuff, false, 13),
        new(16, "mantis", BeastmasterKinType.Vilekin, BeastmasterAffinity.Durant, 44965, 44966, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.TargetDebuff, false, 16),
        new(17, "slime", BeastmasterKinType.Ashkin, BeastmasterAffinity.Eldritch, 44967, 44968, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE, false, 17),
        new(18, "dullahan", BeastmasterKinType.Soulkin, BeastmasterAffinity.Durant, 44969, 44970, BeastmasterReleaseTraits.PartyBuff, false, 20),
        new(19, "bat", BeastmasterKinType.Cloudkin, BeastmasterAffinity.Volant, 44971, 44972, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE, false, 7),
        new(20, "flying trap", BeastmasterKinType.Seedkin, BeastmasterAffinity.Volant, 44973, 44974, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.TargetDebuff, true, 10),
        new(21, "ziz", BeastmasterKinType.Scalekin, BeastmasterAffinity.Durant, 44975, 44976, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.CrowdControl, false, 16),
        new(22, "sabotender", BeastmasterKinType.Seedkin, BeastmasterAffinity.Rampant, 44977, 44978, BeastmasterReleaseTraits.PartyBuff, false, 3),
        new(23, "golem", BeastmasterKinType.Soulkin, BeastmasterAffinity.Eldritch, 44979, 44980, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.TargetDebuff, false, 29),
        new(24, "apkallu", BeastmasterKinType.Cloudkin, BeastmasterAffinity.Durant, 44981, 44982, BeastmasterReleaseTraits.Damage, false, 30),
        new(25, "adamantoise", BeastmasterKinType.Scalekin, BeastmasterAffinity.Eldritch, 44983, 44984, BeastmasterReleaseTraits.Mitigation, false, 12),
        new(26, "buffalo", BeastmasterKinType.Beastkin, BeastmasterAffinity.Rampant, 44985, 44986, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.CrowdControl, false, 8),
        new(27, "uragnite", BeastmasterKinType.Wavekin, BeastmasterAffinity.Durant, 44987, 44988, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.TargetDebuff, false, 14),
        new(28, "worm", BeastmasterKinType.Vilekin, BeastmasterAffinity.Eldritch, 44989, 44990, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.PetCast, false, 31),
        new(29, "spriggan", BeastmasterKinType.Soulkin, BeastmasterAffinity.Rampant, 44991, 44992, BeastmasterReleaseTraits.Damage, false, 7),
        new(30, "goobbue", BeastmasterKinType.Beastkin, BeastmasterAffinity.Rampant, 44993, 44994, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.TargetDebuff, false, 12),
        new(31, "gigantoad", BeastmasterKinType.Wavekin, BeastmasterAffinity.Eldritch, 44995, 44996, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.DrawIn, false, 4),
        new(32, "colibri", BeastmasterKinType.Cloudkin, BeastmasterAffinity.Volant, 44997, 44998, BeastmasterReleaseTraits.Damage, false, 33),
        new(33, "coeurl", BeastmasterKinType.Beastkin, BeastmasterAffinity.Eldritch, 44999, 45000, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.CrowdControl, false, 24),
        new(34, "raptor", BeastmasterKinType.Scalekin, BeastmasterAffinity.Durant, 45001, 45002, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.TargetDebuff, false, 9),
        new(35, "drake", BeastmasterKinType.Scalekin, BeastmasterAffinity.Rampant, 45003, 45004, BeastmasterReleaseTraits.Mitigation, false, 32),
        new(36, "treant", BeastmasterKinType.Seedkin, BeastmasterAffinity.Eldritch, 45005, 45006, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.Knockback, false, 12),
        new(37, "antling", BeastmasterKinType.Vilekin, BeastmasterAffinity.Rampant, 45007, 45008, BeastmasterReleaseTraits.Mitigation | BeastmasterReleaseTraits.CrowdControl, false, 38),
        new(38, "chimera", BeastmasterKinType.Beastkin, BeastmasterAffinity.Rampant, 45009, 45010, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.CrowdControl, false, 38),
        new(39, "morbol", BeastmasterKinType.Seedkin, BeastmasterAffinity.Rampant, 45011, 45012, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.CrowdControl, false, 31),
        new(40, "ghost", BeastmasterKinType.Ashkin, BeastmasterAffinity.Volant, 45013, 45014, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE, false, 7),
        new(41, "salamander", BeastmasterKinType.Wavekin, BeastmasterAffinity.Durant, 45015, 45016, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.TargetDebuff, false, 6),
        new(42, "cobra", BeastmasterKinType.Scalekin, BeastmasterAffinity.Durant, 45017, 45018, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.CrowdControl, false, 45),
        new(43, "hydra", BeastmasterKinType.Scalekin, BeastmasterAffinity.Durant, 45019, 45020, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.TargetDebuff, false, 50),
        new(44, "damselfly", BeastmasterKinType.Vilekin, BeastmasterAffinity.Volant, 45021, 45022, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.TargetDebuff, false, 50),
        new(45, "rotting goobbue", BeastmasterKinType.Ashkin, BeastmasterAffinity.Eldritch, 45023, 45024, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.DrawIn, false, 50),
        new(46, "zu", BeastmasterKinType.Cloudkin, BeastmasterAffinity.Volant, 45025, 45026, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE, false, 50),
        new(47, "ice golem", BeastmasterKinType.Soulkin, BeastmasterAffinity.Durant, 45027, 45028, BeastmasterReleaseTraits.PartyBuff, false, 50),
        new(48, "Karlabos", BeastmasterKinType.Wavekin, BeastmasterAffinity.Durant, 45029, 45030, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE, false, 50),
        new(49, "rafflesia", BeastmasterKinType.Seedkin, BeastmasterAffinity.Eldritch, 45031, 45032, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE, false, 50),
        new(50, "behemoth", BeastmasterKinType.Beastkin, BeastmasterAffinity.Eldritch, 45033, 45034, BeastmasterReleaseTraits.Damage | BeastmasterReleaseTraits.AoE | BeastmasterReleaseTraits.PetCast, false, 50),
    ];

    /// <summary> The beast for an XBMPet row, or null for 0 / out of range. </summary>
    public static BeastmasterBeast? ByRow(int row) => row is >= 1 and <= Count ? All[row] : null;

    /// <summary> XBMPet row from a summoned familiar's BNpcBase (DataId), or 0 when it is not a BST familiar. </summary>
    public static int RowFromBNpcBase(uint baseId) =>
        baseId is > BNpcBaseOffset and <= BNpcBaseOffset + Count ? (int)(baseId - BNpcBaseOffset) : 0;
}
