namespace GluttonyCombo.Combos.PvE;

// Beastmaster (BST, ClassJob 43) action / status / trait IDs.
// Re-verified 2026-09-16 against the 7.56 sheets (xivapi v2, schema exdschema@2:latest) for the BST
// rebuild; see scratchpad research/bst-gamedata.md in the rebuild session and the wiki
// Projects/FFXIV Mods BST rebuild section. Pure constants (no Dalamud types): this file is also
// compiled into tests/GluttonyCombo.BSTRotationHarness.
internal partial class BST
{
    public const uint
        // Single-target weaponskill combo (1 -> 2 -> 3), shared GCD (CooldownGroup 58)
        SmashAxe = 44879,       // L1
        AxebladeBite = 44883,   // L2, +13 TP from L4 (Wild Heart)
        Shieldsplitter = 44885, // L12, +15 TP

        // Instinctual weaponskills (CooldownGroup 16, 5 s, NOT the GCD). Each spends ALL TP (min 100).
        AvalancheAxe = 44884,   // L4  Rampant
        MistralAxe = 44887,     // L8  Durant
        SpinningAxe = 44888,    // L14 Eldritch
        GaleAxe = 44889,        // L16 Volant

        // L50 finishers at 250 TP (replace the four axes via GetAdjustedActionId).
        BrutalRage = 44930,     // replaces Avalanche Axe - Sunstrider
        HawkishTalons = 44931,  // replaces Mistral Axe  - Moonstalker
        RisenFall = 44932,      // replaces Spinning Axe - Sunstrider
        Calamity = 44933,       // replaces Gale Axe     - Moonstalker

        // Battlehorns. 1 s cast. Out of combat recast 2 s; IN COMBAT a horn is locked ~90 s from the
        // moment its familiar retreats (live 2026-09-12 18:41:42 -> 18:43:12).
        FirstBattlehorn = 44881,  // L1
        SecondBattlehorn = 44892, // L10
        ThirdBattlehorn = 44894,  // L20

        // Familiar commands
        PartingBlow = 44891,     // L6, 10 s. Aetheric Burst 1,000 (1,500 with Lingering Vantage); familiar retreats.
        Trick = 47093,           // L8, 3 s. Familiar instinctual skill, familiar TP >= 100 (spends all).
        TemperedRelease = 44890, // L18, 30 s. Consumes One with Nature. Per-beast skill (BST_Beasts).
        TemperedReleaseTargeted = 47092, // hostile-targeted variant the client adjusts to for some beasts
        Borrow = 44895,          // L22, 30 s. Consumes One with Nature. Kinship by familiar kin.

        // Beast Mode placeholder (L22); GetAdjustedActionId resolves the Kinship variant.
        BeastMode = 44886,
        Beastskin = 44896,     // Beast Kinship, 90 s
        Vileskin = 44897,      // Vile Kinship, 90 s
        CloudSkim = 44898,     // Cloud Kinship, 3 s - movement dash, never automated
        Seedsower = 44899,     // Seed Kinship, 90 s, 6 y AoE
        QuellingWave = 44900,  // Wave Kinship, 2.5 s SPELL on the shared GCD, 30 y
        Scaleskin = 44901,     // Scale Kinship, 90 s
        SoulCrush = 44902,     // Soul Kinship, 30 s, interrupt
        ScouringAsh = 44903,   // Ash Kinship, 60 s, cleanse

        ShieldCharge = 44893,  // L24, 60 s; 3 charges from L36
        Rally = 44905,         // L28, 120 s (90 s from L42). Spends Mastered Instinct: +40 TP +70/stack.
        RallyingCheer = 44904, // L40, 120 s (90 s from L48). Spends Natural Instinct: +30 fam TP +70/stack.

        // Capture helpers - never automated
        Capture = 44880,
        GaugeAction = 44882, // named GaugeAction: "Gauge" collides with the BST.Gauge snapshot property

        // Pet-side Parting Blow payload (not player-castable)
        AethericBurst = 44934,

        // Crucible duty actions (identification only)
        Challenge = 46750,
        Snarl = 46751;

    public static class Buffs
    {
        public const ushort
            VolantHeart = 4595,
            RampantHeart = 4596,
            DurantHeart = 4597,
            EldritchHeart = 4598,
            Sunstrider = 4599,
            Moonstalker = 4600,
            // Permanent until spent. Borrow OR Tempered Release consumes it: one per summon
            // (88 live removals 09-04..09-16: 54 Tempered Release, 27 Borrow, 7 Parting Blow).
            OneWithNature = 4601,

            // Kinship, timed set (90 s once the source familiar is gone)
            BeastKinship = 4602,
            VileKinship = 4603,
            CloudKinship = 4604,
            SeedKinship = 4605,
            WaveKinship = 4606,
            ScaleKinship = 4607,
            SoulKinship = 4608,
            AshKinship = 4609,

            // Lingering Vantage: from Tempered Release at L44 (trait 749), from Borrow at L46 (trait 750).
            // It cannot exist below L44 - holding Parting Blow for it there stalls forever.
            LingeringVantage = 4614,

            Vileskin = 4620,
            Beastskin = 4621,
            Scaleskin = 4623,
            CapturingInterest = 4624,
            // "Otherwise engaged. Unable to perform combos with your familiar." ~7 s after a combo (gauge 0x0C == 7).
            WaveringHeart = 4643,

            // Kinship, permanent set (timer halted while the source familiar is summoned)
            BeastKinshipHeld = 4644,
            VileKinshipHeld = 4645,
            CloudKinshipHeld = 4646,
            SeedKinshipHeld = 4647,
            WaveKinshipHeld = 4648,
            ScaleKinshipHeld = 4649,
            SoulKinshipHeld = 4650,
            AshKinshipHeld = 4651;
    }

    public static class Debuffs
    {
        public const ushort
            SeedsSown = 4622,       // on enemies (Seedsower)
            InterestCaptured = 4626; // on the capture target
    }

    /// <summary> Beastmaster traits with their 7.56 sheet levels (corrected 2026-09-16). </summary>
    public static class Traits
    {
        public const uint
            WildHeart = 690,              // L4  combo TP
            WildHeartII = 691,            // L8  familiar TP from autos / Aetheric Burst
            BattlehornMastery = 692,      // L18 One with Nature on summon
            WildHeartIII = 694,           // L28 Mastered Instinct stacks
            BattlehornMasteryII = 754,    // L30 summon resets Tempered Release recast
            BattlehornMasteryIII = 755,   // L34 summon resets Borrow recast
            EnhancedShieldCharge = 751,   // L36 3 charges
            Beastmastery = 752,           // L38 Smash Axe 160, Shield Charge 300
            WildHeartIV = 693,            // L40 Natural Instinct stacks
            EnhancedRally = 756,          // L42 Rally 90 s
            TemperedReleaseMastery = 749, // L44 Lingering Vantage from Tempered Release
            EnhancedBorrow = 750,         // L46 Lingering Vantage from Borrow
            EnhancedRallyingCheer = 757,  // L48 Rallying Cheer 90 s
            InstinctualMastery = 758;     // L50 finishers at 250 TP
    }

    /// <summary> Inclusive status-id window that belongs to Beastmaster (BT| collector). </summary>
    public const ushort StatusRangeStart = 4595;

    /// <inheritdoc cref="StatusRangeStart"/>
    public const ushort StatusRangeEnd = 4651;
}
