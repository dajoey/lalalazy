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

    // ------------------------------------------------------------------ core lane 2026-10-13

    /// <summary>
    ///     The Summon Order statuses the game puts on the FAMILIAR (not the player) while it carries out an order. The sheet calls
    ///     them four identical generic rows; each one follows exactly one order (ffxivdb, status added up to 2.5 s after the press,
    ///     09-08..10-11): 1915 after Trick 3,230 of 3,906, 1964 after Tempered Release 3,146 of 3,222, 1965 after Parting Blow
    ///     1,715 of 3,559, 1966 after Snarl 2,273 of 3,258. A Trick pressed while 1964 was on the familiar got its Heart 18.9% of
    ///     the time, during or up to 2 s after 1965 22%, every other Trick 82.5%.
    /// </summary>
    public static class PetOrderStatus
    {
        public const ushort
            Trick = 1915,
            Release = 1964,
            Recall = 1965,
            Snarl = 1966;
    }

    /// <summary>
    ///     Crucible casts that knock the character back (or slam it far enough that Vileskin's 15 s knockback immunity answers
    ///     them), with the battle they belong to. Ids are the casts stock BossMod Reborn's Crucible modules key their knockback
    ///     components on (origin/main be8ff771c, BossMod/Modules/Global/CrucibleOfTheUnbroken/; the module and line are in
    ///     <see cref="KnockbackCast.Source"/>). Most are cast by an invisible helper, so the live half scans every caster in the
    ///     arena, not just the target. Not listed: Progenitrix Meltdown (no castbar, head marker 412 only), Lauda's Unseen Force
    ///     (the push comes when a status runs out), the Ice Dragon's Touchdown (a 0.5 s cast BMR models outside its knockback
    ///     components).
    /// </summary>
    public static class CrucibleKnockbacks
    {
        public readonly record struct KnockbackCast(uint CastId, byte Board, byte Battle, string Name, string Source);

        public static readonly KnockbackCast[] Casts =
        [
            new(48168, 2, 3, "Typhoon", "02SecondBoard/WyvernPiece.cs:17,55 (WyvernPiece, 3.0 s, 10 y)"),
            new(48199, 2, 5, "Shockwave", "02SecondBoard/YoungerTablitaurPiece.cs:24,87 (helper, 7.0 s, 20 y donut)"),
            new(48202, 2, 5, "Shockwave (long)", "02SecondBoard/YoungerTablitaurPiece.cs:28,87 (helper, 10.5 s, 20 y donut)"),
            new(48607, 3, 0, "Beastly Aura", "03ThirdBoard/GuttlerTheGutter.cs:25,233-242 (helper, 7.0 s, 20 y)"),
            new(48471, 3, 1, "Crushing Blade", "03ThirdBoard/CavalierPiece.cs:23,99-116 (CavalierPiece on the player, 5.0 s, 15 y)"),
            new(48481, 3, 2, "Tsunami", "03ThirdBoard/YmirPiece.cs:17,126 (helper, 8.0 s, 35 y)"),
            new(48556, 3, 5, "Landslip", "03ThirdBoard/LakhamuPiece.cs:24,149 (helper, 8.0 s, 20 y)"),
            new(48816, 4, 0, "Touchdown", "04FirstMasterBoard/BorgnyTheVenomous.cs:21,243 (helper, 4.0 s, 30 y)"),
            new(48667, 4, 1, "Aero III", "04FirstMasterBoard/StrixPiece.cs:21,50 (Strix Plume, 12.0 s, 25 y)"),
            new(50758, 4, 2, "Tremblor", "04FirstMasterBoard/MorbolPiece.cs:27,54 (helper, 5.0 s, 10 y)"),
            new(48725, 4, 5, "Fivefold Fallout", "04FirstMasterBoard/GargoylePiece.cs:38,310-326 (helper, 3.0 s, 20 y)"),
            new(50649, 4, 7, "Obliterate", "04FirstMasterBoard/GolemPiece.cs:21,269 (golem, 5.0 s, 17 y)"),
            new(48805, 4, 9, "Fiery Fury", "04FirstMasterBoard/ProgenitrixPiece.cs:27,147 (helper, 7.9 s, 15 y)"),
            new(49453, 5, 0, "Beastly Aura", "05SecondMasterBoard/LaudaTheSpellcleaver.cs:29,161-170 (helper, 7.0 s, 20 y)"),
            new(49473, 5, 0, "Guttler Glutter", "05SecondMasterBoard/LaudaTheSpellcleaver.cs:72,645 (Lauda, 27.1 s, 20 y)"),
            new(49226, 5, 3, "Swing Round", "05SecondMasterBoard/BoogymanPiece.cs:19,62 (helper, 5.0 s, 20 y)"),
            new(49238, 5, 4, "Shockwave", "05SecondMasterBoard/AtomosPiece.cs:26,85 (Atomos, 6.0 s, 16 y)"),
            new(49242, 5, 4, "Water III", "05SecondMasterBoard/AtomosPiece.cs:40,106 (bavarois, 2.0 s, 20 y)"),
            new(49274, 5, 6, "Thermobaric Charge", "05SecondMasterBoard/DurgaPiece.cs:28,154 (helper, 2.0 s, 40 y)"),
        ];

        private static readonly System.Collections.Generic.HashSet<uint> CastIds = [.. System.Linq.Enumerable.Select(Casts, c => c.CastId)];

        /// <summary> The cast is a known Crucible knockback. </summary>
        public static bool IsKnockbackCast(uint castId) => castId != 0 && CastIds.Contains(castId);

        /// <summary> The battle (board 1-5, battle 0 = boss) has a known knockback cast. </summary>
        public static bool BattleHasKnockback(int board, int battle)
        {
            foreach (var c in Casts)
                if (c.Board == board && c.Battle == battle)
                    return true;
            return false;
        }
    }
}
