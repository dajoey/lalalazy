namespace GluttonyCombo.Combos.PvE;

// Beastmaster (BST, ClassJob 43) SKELETON action/status IDs (t_f719ab97).
// Sourced from skill lalalazy-ffxiv references/beastmaster-facts.md (datamined 2026-09-09,
// patch 7.56). Re-verify against the live Lumina sheets before wiring real rotation logic -
// xivapi can lag the client by a build. No rotation code consumes these yet; they exist so
// the follow-up cards do not have to re-datamine.
internal partial class BST
{
    public const uint
        // Single-target weaponskill combo (1 -> 2 -> 3)
        SmashAxe = 44879,
        AxebladeBite = 44883,
        Shieldsplitter = 44885,

        // Instinctual weaponskills, one per compass affinity
        AvalancheAxe = 44884,   // Rampant
        MistralAxe = 44887,     // Durant
        SpinningAxe = 44888,    // Eldritch
        GaleAxe = 44889,        // Volant

        // Lv50 upgrades. These REPLACE the four above via GetAdjustedActionId and are not
        // separately hotbar-able.
        BrutalRage = 44930,     // replaces Avalanche Axe (Sunstrider)
        HawkishTalons = 44931,  // replaces Mistral Axe (Moonstalker)
        RisenFall = 44932,      // replaces Spinning Axe (Sunstrider)
        Calamity = 44933,       // replaces Gale Axe (Moonstalker)

        // Battlehorns - the three familiar summon slots
        FirstBattlehorn = 44881,
        SecondBattlehorn = 44892,
        ThirdBattlehorn = 44894,

        // Familiar / pet abilities
        Trick = 47093,
        TemperedRelease = 44890,
        PartingBlow = 44891,
        Borrow = 44895,

        // Beast Mode placeholder; GetAdjustedActionId(BeastMode) resolves the live variant
        BeastMode = 44886,

        // Beast Mode variants, one per Kinship kin type (1..8 in XBMPet Classification order)
        Beastskin = 44896,
        Vileskin = 44897,
        CloudSkim = 44898,
        Seedsower = 44899,
        QuellingWave = 44900,
        Scaleskin = 44901,
        SoulCrush = 44902,
        ScouringAsh = 44903,

        // Cooldowns and resource spenders
        ShieldCharge = 44893,
        Rally = 44905,
        RallyingCheer = 44904,

        // Familiar capture
        Capture = 44880,
        // Named GaugeAction, not Gauge: the action "Gauge" (the capture-odds check) would
        // collide with the BST.Gauge gauge-snapshot property in BST_Gauge.cs.
        GaugeAction = 44882,

        // Crucible-only duty actions (not player actions; kept for identification)
        Snarl = 46751,
        Challenge = 46750;

    public static class Buffs
    {
        public const ushort
            VolantHeart = 4595,
            RampantHeart = 4596,
            DurantHeart = 4597,
            EldritchHeart = 4598,
            Sunstrider = 4599,
            Moonstalker = 4600,
            OneWithNature = 4601,

            // Kinship set. NOTE (2026-09-09): the live ffxivdb corpus shows the 4644-4651
            // set firing more often than these, so it is NOT yet settled which set the
            // PLAYER carries versus the pet-side mirror. Do not gate rotation logic on
            // these until a status_events target join says so.
            BeastKinship = 4602,
            VileKinship = 4603,
            CloudKinship = 4604,
            SeedKinship = 4605,
            WaveKinship = 4606,
            ScaleKinship = 4607,
            SoulKinship = 4608,
            AshKinship = 4609,

            LingeringVantage = 4614,
            Vileskin = 4620,
            Beastskin = 4621,
            SeedsSown = 4622,
            Scaleskin = 4623,
            InterestCaptured = 4626,
            WaveringHeart = 4643;
    }

    /// <summary> Inclusive status-id window that belongs to Beastmaster. </summary>
    /// <remarks>
    ///     Used by the <c>BT|</c> collector to list only BST statuses. Deliberately spans
    ///     4595-4651 so the unresolved 4644-4651 Kinship mirror is captured too - resolving
    ///     which of the two sets the player carries is exactly what this collector is for.
    /// </remarks>
    public const ushort StatusRangeStart = 4595;

    /// <inheritdoc cref="StatusRangeStart"/>
    public const ushort StatusRangeEnd = 4651;
}
