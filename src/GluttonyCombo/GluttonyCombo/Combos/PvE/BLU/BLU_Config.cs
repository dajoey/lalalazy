<<<<<<< C:/Scripts/nightly-upstream-merge/_scratch-20260830/ours.tmp
#region
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Window.Functions;
using static GluttonyCombo.Window.Functions.UserConfig;

// ReSharper disable AccessToStaticMemberViaDerivedType
// ReSharper disable GrammarMistakeInComment
// ReSharper disable SwitchStatementMissingSomeEnumCasesNoDefault
// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace
// ReSharper disable ClassNeverInstantiated.Global
#endregion

=======
using ECommons.ImGuiMethods;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Resources.Localization.JobConfigs;
using static GluttonyCombo.Window.Functions.UserConfig;

>>>>>>> C:/Scripts/nightly-upstream-merge/_scratch-20260830/theirs.tmp
namespace GluttonyCombo.Combos.PvE;

internal partial class BLU
{
    #region BLU auto-rotation action IDs
    // New action-ID constants for the auto-rotation live here (in the partial) rather than in
    // BLU.cs, to keep the merge-sensitive BLU.cs untouched for the nightly upstream merge.
    // Constants already present in BLU.cs (SonicBoom, FeatherRain, etc.) are reused, not redeclared.
    public const uint
        WhiteWind = 11406;
    #endregion

    /// <summary>
    /// BLU auto-rotation configuration. Per-ability UserBool toggles act as a global allow-list — a
    /// spell can only auto-cast if it is slotted (IsSpellActive), its toggle here is on, and its
    /// readiness predicate (Phase 3 cascade) holds. All 124 learnable spells get a toggle; the
    /// cascade only actively drives ~40 of them. Sliders tune lane behaviour. Kept self-contained in
    /// its own file to stay friendly to the nightly upstream merge.
    /// </summary>
    internal static class Config
    {
<<<<<<< C:/Scripts/nightly-upstream-merge/_scratch-20260830/ours.tmp
        #region Per-ability toggles (all 124 spells)

        // --- Buffs & Enablers ---
        public static UserBool
            BLU_Use_MoonFlute        = new("BLU_Use_MoonFlute", true),
            BLU_Use_Whistle          = new("BLU_Use_Whistle", true),
            BLU_Use_Bristle          = new("BLU_Use_Bristle", true),
            BLU_Use_Tingle           = new("BLU_Use_Tingle", true),
            BLU_Use_Offguard         = new("BLU_Use_Offguard", true),
            BLU_Use_PeculiarLight    = new("BLU_Use_PeculiarLight", true),
            BLU_Use_CondensedLibra   = new("BLU_Use_CondensedLibra", true),
            BLU_Use_AethericMimicry  = new("BLU_Use_AethericMimicry", true);

        // --- Burst Nukes ---
        public static UserBool
            BLU_Use_BeingMortal      = new("BLU_Use_BeingMortal", true),
            BLU_Use_BothEnds         = new("BLU_Use_BothEnds", true),
            BLU_Use_Nightbloom       = new("BLU_Use_Nightbloom", true),
            BLU_Use_SeaShanty        = new("BLU_Use_SeaShanty", true),
            BLU_Use_MatraMagic       = new("BLU_Use_MatraMagic", true),
            BLU_Use_TripleTrident    = new("BLU_Use_TripleTrident", true),
            BLU_Use_RoseOfDestruction= new("BLU_Use_RoseOfDestruction", true),
            BLU_Use_RubyDynamics     = new("BLU_Use_RubyDynamics", true),
            BLU_Use_GlassDance       = new("BLU_Use_GlassDance", true),
            BLU_Use_Surpanakha       = new("BLU_Use_Surpanakha", true);

        // --- oGCD Fillers ---
        public static UserBool
            BLU_Use_FeatherRain      = new("BLU_Use_FeatherRain", true),
            BLU_Use_Eruption         = new("BLU_Use_Eruption", true),
            BLU_Use_ShockStrike      = new("BLU_Use_ShockStrike", true),
            BLU_Use_MountainBuster   = new("BLU_Use_MountainBuster", true),
            BLU_Use_Quasar           = new("BLU_Use_Quasar", true),
            BLU_Use_JKick            = new("BLU_Use_JKick", true),
            BLU_Use_MagicHammer      = new("BLU_Use_MagicHammer", true),
            BLU_Use_CandyCane        = new("BLU_Use_CandyCane", true);

        // --- Channels ---
        public static UserBool
            BLU_Use_PhantomFlurry    = new("BLU_Use_PhantomFlurry", true),
            BLU_Use_Apokalypsis      = new("BLU_Use_Apokalypsis", true);

        // --- DoTs ---
        public static UserBool
            BLU_Use_BreathOfMagic    = new("BLU_Use_BreathOfMagic", true),
            BLU_Use_MortalFlame      = new("BLU_Use_MortalFlame", true),
            BLU_Use_SongOfTorment    = new("BLU_Use_SongOfTorment", true),
            BLU_Use_AetherialSpark   = new("BLU_Use_AetherialSpark", true);

        // --- Filler GCDs ---
        public static UserBool
            BLU_Use_SonicBoom        = new("BLU_Use_SonicBoom", true),
            BLU_Use_GoblinPunch      = new("BLU_Use_GoblinPunch", true),
            BLU_Use_WingedReprobation= new("BLU_Use_WingedReprobation", true),
            BLU_Use_ConvictionMarcato= new("BLU_Use_ConvictionMarcato", true),
            BLU_Use_SharpenedKnife   = new("BLU_Use_SharpenedKnife", true),
            BLU_Use_RevengeBlast     = new("BLU_Use_RevengeBlast", true);

        // --- Heals (Transfusion is a self-suicide -> default off) ---
        public static UserBool
            BLU_Use_WhiteWind        = new("BLU_Use_WhiteWind", true),
            BLU_Use_PomCure          = new("BLU_Use_PomCure", true),
            BLU_Use_Stotram          = new("BLU_Use_Stotram", true),
            BLU_Use_Exuviation       = new("BLU_Use_Exuviation", true),
            BLU_Use_AngelsSnack      = new("BLU_Use_AngelsSnack", true),
            BLU_Use_Gobskin          = new("BLU_Use_Gobskin", true),
            BLU_Use_Rehydration      = new("BLU_Use_Rehydration", true),
            BLU_Use_AngelWhisper     = new("BLU_Use_AngelWhisper", true),
            BLU_Use_Transfusion      = new("BLU_Use_Transfusion", false);

        // --- Mitigation / Defensive (Diamondback + Basic Instinct disruptive -> default off) ---
        public static UserBool
            BLU_Use_ColdFog          = new("BLU_Use_ColdFog", true),
            BLU_Use_Diamondback      = new("BLU_Use_Diamondback", false),
            BLU_Use_ForceField       = new("BLU_Use_ForceField", true),
            BLU_Use_DragonForce      = new("BLU_Use_DragonForce", true),
            BLU_Use_VeilOfTheWhorl   = new("BLU_Use_VeilOfTheWhorl", true),
            BLU_Use_IceSpikes        = new("BLU_Use_IceSpikes", true),
            BLU_Use_ToadOil          = new("BLU_Use_ToadOil", true),
            BLU_Use_BasicInstinct    = new("BLU_Use_BasicInstinct", false),
            BLU_Use_Avail            = new("BLU_Use_Avail", true),
            BLU_Use_Cactguard        = new("BLU_Use_Cactguard", true);

        // --- Tank Tools ---
        public static UserBool
            BLU_Use_MightyGuard      = new("BLU_Use_MightyGuard", true),
            BLU_Use_Devour           = new("BLU_Use_Devour", true),
            BLU_Use_ChelonianGate    = new("BLU_Use_ChelonianGate", true),
            BLU_Use_TheLook          = new("BLU_Use_TheLook", true),
            BLU_Use_FrogLegs         = new("BLU_Use_FrogLegs", true),
            BLU_Use_StickyTongue     = new("BLU_Use_StickyTongue", true),
            BLU_Use_Schiltron        = new("BLU_Use_Schiltron", true);

        // --- Execute / Suicide / %HP (suicides default off) ---
        public static UserBool
            BLU_Use_FinalSting       = new("BLU_Use_FinalSting", false),
            BLU_Use_SelfDestruct     = new("BLU_Use_SelfDestruct", false),
            BLU_Use_WildRage         = new("BLU_Use_WildRage", false),
            BLU_Use_ThousandNeedles  = new("BLU_Use_ThousandNeedles", true),
            BLU_Use_Missile          = new("BLU_Use_Missile", true),
            BLU_Use_TailScrew        = new("BLU_Use_TailScrew", true),
            BLU_Use_Launcher         = new("BLU_Use_Launcher", true),
            BLU_Use_DimensionalShift = new("BLU_Use_DimensionalShift", true),
            BLU_Use_Ultravibration   = new("BLU_Use_Ultravibration", true),
            BLU_Use_Level5Death      = new("BLU_Use_Level5Death", true),
            BLU_Use_Level5Petrify    = new("BLU_Use_Level5Petrify", true),
            BLU_Use_Doom             = new("BLU_Use_Doom", true);

        // --- CC & Utility (dormant: toggle present, no cascade predicate drives them) ---
        public static UserBool
            BLU_Use_Snort            = new("BLU_Use_Snort", true),
            BLU_Use_FourTonzeWeight  = new("BLU_Use_FourTonzeWeight", true),
            BLU_Use_WaterCannon      = new("BLU_Use_WaterCannon", true),
            BLU_Use_HighVoltage      = new("BLU_Use_HighVoltage", true),
            BLU_Use_BadBreath        = new("BLU_Use_BadBreath", true),
            BLU_Use_FlyingFrenzy     = new("BLU_Use_FlyingFrenzy", true),
            BLU_Use_AquaBreath       = new("BLU_Use_AquaBreath", true),
            BLU_Use_Plaincracker     = new("BLU_Use_Plaincracker", true),
            BLU_Use_AcornBomb        = new("BLU_Use_AcornBomb", true),
            BLU_Use_MindBlast        = new("BLU_Use_MindBlast", true),
            BLU_Use_BloodDrain       = new("BLU_Use_BloodDrain", true),
            BLU_Use_BombToss         = new("BLU_Use_BombToss", true),
            BLU_Use_DrillCannons     = new("BLU_Use_DrillCannons", true),
            BLU_Use_Loom             = new("BLU_Use_Loom", true),
            BLU_Use_FlameThrower     = new("BLU_Use_FlameThrower", true),
            BLU_Use_Faze             = new("BLU_Use_Faze", true),
            BLU_Use_Glower           = new("BLU_Use_Glower", true),
            BLU_Use_InkJet           = new("BLU_Use_InkJet", true),
            BLU_Use_FlyingSardine    = new("BLU_Use_FlyingSardine", true),
            BLU_Use_FireAngon        = new("BLU_Use_FireAngon", true),
            BLU_Use_AlpineDraft      = new("BLU_Use_AlpineDraft", true),
            BLU_Use_ProteanWave      = new("BLU_Use_ProteanWave", true),
            BLU_Use_Northerlies      = new("BLU_Use_Northerlies", true),
            BLU_Use_Electrogenesis   = new("BLU_Use_Electrogenesis", true),
            BLU_Use_Kaltstrahl       = new("BLU_Use_Kaltstrahl", true),
            BLU_Use_AbyssalTransfixion = new("BLU_Use_AbyssalTransfixion", true),
            BLU_Use_Chirp            = new("BLU_Use_Chirp", true),
            BLU_Use_EerieSoundwave   = new("BLU_Use_EerieSoundwave", true),
            BLU_Use_WhiteKnightsTour = new("BLU_Use_WhiteKnightsTour", true),
            BLU_Use_BlackKnightsTour = new("BLU_Use_BlackKnightsTour", true),
            BLU_Use_PerpetualRay     = new("BLU_Use_PerpetualRay", true),
            BLU_Use_Reflux           = new("BLU_Use_Reflux", true),
            BLU_Use_TatamiGaeshi     = new("BLU_Use_TatamiGaeshi", true),
            BLU_Use_SaintlyBeam      = new("BLU_Use_SaintlyBeam", true),
            BLU_Use_FeculentFlood    = new("BLU_Use_FeculentFlood", true),
            BLU_Use_Blaze            = new("BLU_Use_Blaze", true),
            BLU_Use_MustardBomb      = new("BLU_Use_MustardBomb", true),
            BLU_Use_HydroPull        = new("BLU_Use_HydroPull", true),
            BLU_Use_MaledictionOfWater = new("BLU_Use_MaledictionOfWater", true),
            BLU_Use_ChocoMeteor      = new("BLU_Use_ChocoMeteor", true),
            BLU_Use_PeripheralSynthesis = new("BLU_Use_PeripheralSynthesis", true),
            BLU_Use_RightRound       = new("BLU_Use_RightRound", true),
            BLU_Use_PeatPelt         = new("BLU_Use_PeatPelt", true),
            BLU_Use_DeepClean        = new("BLU_Use_DeepClean", true),
            BLU_Use_DivinationRune   = new("BLU_Use_DivinationRune", true),
            BLU_Use_LaserEye         = new("BLU_Use_LaserEye", true),
            BLU_Use_RamsVoice        = new("BLU_Use_RamsVoice", true),
            BLU_Use_DragonsVoice     = new("BLU_Use_DragonsVoice", true);

        #endregion

        #region Behaviour toggles

        // Tank lane ensures Mighty Guard is ON (auto-cast if missing). Never auto-cancels — dropping
        // the stance when leaving the tank lane is left to the player (Joey, 2026-06-18).
        public static UserBool BLU_Tank_AutoMightyGuard = new("BLU_Tank_AutoMightyGuard", true);

        #endregion

        #region Sliders / thresholds

        // DPS lane
        public static UserInt   BLU_FinalSting_BossHPPercent   = new("BLU_FinalSting_BossHPPercent", 1);
        public static UserInt   BLU_Surpanakha_HoldForBurstSec = new("BLU_Surpanakha_HoldForBurstSec", 20);
        public static UserFloat BLU_DoT_RefreshLeadSec         = new("BLU_DoT_RefreshLeadSec", 3f);

        // Defensive / predictive
        public static UserFloat BLU_ColdFog_LeadSeconds        = new("BLU_ColdFog_LeadSeconds", 3f);
        public static UserInt   BLU_ProphylacticMit_HPPercent  = new("BLU_ProphylacticMit_HPPercent", 90);
        public static UserFloat BLU_ProphylacticMit_LeadSeconds= new("BLU_ProphylacticMit_LeadSeconds", 3f);

        // Heal lane
        public static UserInt   BLU_Heal_PartyHPPercent        = new("BLU_Heal_PartyHPPercent", 85);
        public static UserInt   BLU_Heal_PomCureHPPercent      = new("BLU_Heal_PomCureHPPercent", 50);
        public static UserInt   BLU_Heal_EmergencyHPPercent    = new("BLU_Heal_EmergencyHPPercent", 40);

        // Per-mimic BossMod Reborn engagement distance (MaxDistanceToTarget)
        public static UserFloat BLU_BMR_Distance_Tank          = new("BLU_BMR_Distance_Tank", 3f);
        public static UserFloat BLU_BMR_Distance_DPS           = new("BLU_BMR_Distance_DPS", 15f);
        public static UserFloat BLU_BMR_Distance_Healer        = new("BLU_BMR_Distance_Healer", 25f);

        #endregion

        #region Draw helpers

        // Render a collapsible group of per-ability on/off toggles.
        private static void ToggleGroup(string header, params (UserBool cfg, string name, string desc)[] items)
        {
            if (!ImGui.CollapsingHeader(header))
                return;

            ImGui.Indent();
            foreach (var (cfg, name, desc) in items)
                DrawAdditionalBoolChoice(cfg, name, desc);
            ImGui.Unindent();
        }

        private static void DrawAbilityToggles()
        {
            ToggleGroup("Buffs & Enablers",
                (BLU_Use_MoonFlute, "Moon Flute", "Opens the +50% burst window."),
                (BLU_Use_Whistle, "Whistle (Harmonized)", "+80% next physical spell."),
                (BLU_Use_Bristle, "Bristle (Boost)", "+50% next magical spell."),
                (BLU_Use_Tingle, "Tingle", "Minor AoE + physical-spell potency buff."),
                (BLU_Use_Offguard, "Off-guard", "Target takes +5% damage."),
                (BLU_Use_PeculiarLight, "Peculiar Light", "Nearby enemies take +5% magic damage."),
                (BLU_Use_CondensedLibra, "Condensed Libra", "Attenuation debuff (+5% of a damage type)."),
                (BLU_Use_AethericMimicry, "Aetheric Mimicry (read-only)", "Stance is read for lane selection; never auto-cast (decision #4)."));

            ToggleGroup("Burst Nukes",
                (BLU_Use_BeingMortal, "Being Mortal", "800 PBAoE - highest single hit."),
                (BLU_Use_BothEnds, "Both Ends", "600 physical PBAoE (shares 120s w/ Nightbloom)."),
                (BLU_Use_Nightbloom, "Nightbloom", "400 + AoE DoT (shares 120s w/ Both Ends)."),
                (BLU_Use_SeaShanty, "Sea Shanty", "500 / 1000 in rain."),
                (BLU_Use_MatraMagic, "Matra Magic", "400 / 800 under DPS mimic."),
                (BLU_Use_TripleTrident, "Triple Trident", "Physical 3x150; consumes Whistle."),
                (BLU_Use_RoseOfDestruction, "The Rose of Destruction", "400, best recurring nuke (30s)."),
                (BLU_Use_RubyDynamics, "Ruby Dynamics", "Shares 30s CD w/ Rose / Chelonian Gate."),
                (BLU_Use_GlassDance, "Glass Dance", "350 arc burst (shares 90s w/ Veil)."),
                (BLU_Use_Surpanakha, "Surpanakha", "4-charge ramp; quad-weave in burst."));

            ToggleGroup("oGCD Fillers",
                (BLU_Use_FeatherRain, "Feather Rain", "220 + DoT (shares 30s w/ Eruption)."),
                (BLU_Use_Eruption, "Eruption", "300 (shares 30s w/ Feather Rain)."),
                (BLU_Use_ShockStrike, "Shock Strike", "400 magic (shares 60s w/ Mountain Buster)."),
                (BLU_Use_MountainBuster, "Mountain Buster", "400 physical (shares 60s w/ Shock Strike)."),
                (BLU_Use_Quasar, "Quasar", "300, short lock (shares 60s w/ J Kick)."),
                (BLU_Use_JKick, "J Kick", "300 (shares 60s w/ Quasar)."),
                (BLU_Use_MagicHammer, "Magic Hammer", "250 + 10% MP (shares 90s w/ Candy Cane)."),
                (BLU_Use_CandyCane, "Candy Cane", "250 + 10% MP (shares 90s w/ Magic Hammer)."));

            ToggleGroup("Channels",
                (BLU_Use_PhantomFlurry, "Phantom Flurry", "Burst finisher; snapshots, ticks into Waning."),
                (BLU_Use_Apokalypsis, "Apokalypsis", "Line channel (shares 120s w/ Being Mortal)."));

            ToggleGroup("DoTs",
                (BLU_Use_BreathOfMagic, "Breath of Magic", "Premier refreshable DoT (60s)."),
                (BLU_Use_MortalFlame, "Mortal Flame", "Permanent DoT; apply once."),
                (BLU_Use_SongOfTorment, "Song of Torment", "Bristle-snapshot DoT (shares slot w/ Nightbloom)."),
                (BLU_Use_AetherialSpark, "Aetherial Spark", "Minor line DoT."));

            ToggleGroup("Filler GCDs",
                (BLU_Use_SonicBoom, "Sonic Boom", "210, terminal DPS filler."),
                (BLU_Use_GoblinPunch, "Goblin Punch", "Tank filler; 320 front under Mighty Guard."),
                (BLU_Use_WingedReprobation, "Winged Reprobation", "Charged physical; pair w/ Whistle+Tingle."),
                (BLU_Use_ConvictionMarcato, "Conviction Marcato", "Payoff after Winged Redemption."),
                (BLU_Use_SharpenedKnife, "Sharpened Knife", "Melee-range filler."),
                (BLU_Use_RevengeBlast, "Revenge Blast", "500 when own HP < 20% (niche)."));

            ToggleGroup("Heals",
                (BLU_Use_WhiteWind, "White Wind", "Party heal = your current HP."),
                (BLU_Use_PomCure, "Pom Cure", "Single-target; 500 under Healer mimic."),
                (BLU_Use_Stotram, "Stotram", "Fast party top-up / 140 dmg otherwise."),
                (BLU_Use_Exuviation, "Exuviation", "Heal + cleanse 1 debuff."),
                (BLU_Use_AngelsSnack, "Angel's Snack", "400 + HoT (shares 120s w/ Matra/Dragon Force)."),
                (BLU_Use_Gobskin, "Gobskin", "Prophylactic barrier."),
                (BLU_Use_Rehydration, "Rehydration", "Long-cast self emergency."),
                (BLU_Use_AngelWhisper, "Angel Whisper", "Raise."),
                (BLU_Use_Transfusion, "Transfusion (suicide)", "Full HP/MP to ally, suicides self. Default OFF."));

            ToggleGroup("Mitigation / Defensive",
                (BLU_Use_ColdFog, "Cold Fog", "Pre-raidwide; converts to White Death spam."),
                (BLU_Use_Diamondback, "Diamondback", "-90% taken but forces Waning mid-burst. Default OFF."),
                (BLU_Use_ForceField, "Force Field", "-50% physical or magic."),
                (BLU_Use_DragonForce, "Dragon Force", "-20% / -40% under Tank mimic."),
                (BLU_Use_VeilOfTheWhorl, "Veil of the Whorl", "Counter (shares 90s w/ Glass Dance)."),
                (BLU_Use_IceSpikes, "Ice Spikes", "Counter + slow attacker."),
                (BLU_Use_ToadOil, "Toad Oil", "+evasion; boosts Self-destruct."),
                (BLU_Use_BasicInstinct, "Basic Instinct", "Solo-only +100% dmg/heal. Default OFF."),
                (BLU_Use_Avail, "Avail", "Ally mitigation."),
                (BLU_Use_Cactguard, "Cactguard", "Ally -5% / -15% under Tank mimic."));

            ToggleGroup("Tank Tools",
                (BLU_Use_MightyGuard, "Mighty Guard", "Tank stance; boosts Goblin Punch."),
                (BLU_Use_Devour, "Devour", "Damage + heal + max-HP buff."),
                (BLU_Use_ChelonianGate, "Chelonian Gate", "-20% then Divine Cataract payoff."),
                (BLU_Use_TheLook, "The Look", "AoE enmity."),
                (BLU_Use_FrogLegs, "Frog Legs", "Provoke."),
                (BLU_Use_StickyTongue, "Sticky Tongue", "Draw-in + stun + enmity."),
                (BLU_Use_Schiltron, "Schiltron", "Physical counter."));

            ToggleGroup("Execute / Suicide / %HP",
                (BLU_Use_FinalSting, "Final Sting (suicide)", "2000, suicides self. Behind kill-range slider. Default OFF."),
                (BLU_Use_SelfDestruct, "Self-destruct (suicide)", "1500/1800 AoE, suicides self. Default OFF."),
                (BLU_Use_WildRage, "Wild Rage (self-damage)", "Consumes 50% own HP. Default OFF."),
                (BLU_Use_ThousandNeedles, "1000 Needles", "Fixed 1000 split."),
                (BLU_Use_Missile, "Missile", "% current HP."),
                (BLU_Use_TailScrew, "Tail Screw", "% current HP."),
                (BLU_Use_Launcher, "Launcher", "% current HP."),
                (BLU_Use_DimensionalShift, "Dimensional Shift", "% current HP."),
                (BLU_Use_Ultravibration, "Ultravibration", "Instant-KO via Deep Freeze combo."),
                (BLU_Use_Level5Death, "Level 5 Death", "Level-gated instant-KO."),
                (BLU_Use_Level5Petrify, "Level 5 Petrify", "Level-gated petrify."),
                (BLU_Use_Doom, "Doom", "Doom debuff."));

            ToggleGroup("CC & Utility (dormant)",
                (BLU_Use_Snort, "Snort", "Knockback."),
                (BLU_Use_FourTonzeWeight, "4-tonze Weight", "Heavy/AoE."),
                (BLU_Use_WaterCannon, "Water Cannon", "Basic damage."),
                (BLU_Use_HighVoltage, "High Voltage", "Paralysis."),
                (BLU_Use_BadBreath, "Bad Breath", "Multi-debuff bomb."),
                (BLU_Use_FlyingFrenzy, "Flying Frenzy", "Damage."),
                (BLU_Use_AquaBreath, "Aqua Breath", "Damage."),
                (BLU_Use_Plaincracker, "Plaincracker", "Damage."),
                (BLU_Use_AcornBomb, "Acorn Bomb", "Sleep."),
                (BLU_Use_MindBlast, "Mind Blast", "Paralysis."),
                (BLU_Use_BloodDrain, "Blood Drain", "MP drain."),
                (BLU_Use_BombToss, "Bomb Toss", "Stun."),
                (BLU_Use_DrillCannons, "Drill Cannons", "Fixed damage."),
                (BLU_Use_Loom, "Loom", "Dash."),
                (BLU_Use_FlameThrower, "Flame Thrower", "Channel damage."),
                (BLU_Use_Faze, "Faze", "Stun."),
                (BLU_Use_Glower, "Glower", "Paralysis."),
                (BLU_Use_InkJet, "Ink Jet", "Damage."),
                (BLU_Use_FlyingSardine, "Flying Sardine", "Silence/interrupt."),
                (BLU_Use_FireAngon, "Fire Angon", "Damage."),
                (BLU_Use_AlpineDraft, "Alpine Draft", "Damage."),
                (BLU_Use_ProteanWave, "Protean Wave", "Knockback/damage."),
                (BLU_Use_Northerlies, "Northerlies", "Damage."),
                (BLU_Use_Electrogenesis, "Electrogenesis", "Damage."),
                (BLU_Use_Kaltstrahl, "Kaltstrahl", "Damage."),
                (BLU_Use_AbyssalTransfixion, "Abyssal Transfixion", "Stun."),
                (BLU_Use_Chirp, "Chirp", "Sleep."),
                (BLU_Use_EerieSoundwave, "Eerie Soundwave", "Dispel enemy buff."),
                (BLU_Use_WhiteKnightsTour, "White Knight's Tour", "Bind combo."),
                (BLU_Use_BlackKnightsTour, "Black Knight's Tour", "Slow combo."),
                (BLU_Use_PerpetualRay, "Perpetual Ray", "Stun combo."),
                (BLU_Use_Reflux, "Reflux", "Heavy/bind."),
                (BLU_Use_TatamiGaeshi, "Tatami-gaeshi", "Stun."),
                (BLU_Use_SaintlyBeam, "Saintly Beam", "Damage."),
                (BLU_Use_FeculentFlood, "Feculent Flood", "Damage."),
                (BLU_Use_Blaze, "Blaze", "Damage."),
                (BLU_Use_MustardBomb, "Mustard Bomb", "Lightheaded combo."),
                (BLU_Use_HydroPull, "Hydro Pull", "Draw-in."),
                (BLU_Use_MaledictionOfWater, "Malediction of Water", "Damage."),
                (BLU_Use_ChocoMeteor, "Choco Meteor", "Damage."),
                (BLU_Use_PeripheralSynthesis, "Peripheral Synthesis", "Lightheaded applier."),
                (BLU_Use_RightRound, "Right Round", "Damage."),
                (BLU_Use_PeatPelt, "Peat Pelt", "Begrimed combo."),
                (BLU_Use_DeepClean, "Deep Clean", "Begrimed payoff."),
                (BLU_Use_DivinationRune, "Divination Rune", "Damage."),
                (BLU_Use_LaserEye, "Laser Eye", "Damage."),
                (BLU_Use_RamsVoice, "The Ram's Voice", "Deep Freeze combo."),
                (BLU_Use_DragonsVoice, "The Dragon's Voice", "AoE / Ram's Voice combo."));
        }

        private static void DrawDpsTuningSliders()
        {
            if (!ImGui.CollapsingHeader("Thresholds & Tuning"))
                return;

            ImGui.Indent();

            DrawSliderInt(0, 100, BLU_FinalSting_BossHPPercent,
                "Final Sting: cast when boss HP% is at or below (suicide kill-range)");
            DrawSliderInt(0, 60, BLU_Surpanakha_HoldForBurstSec,
                "Surpanakha: hold charges when next Moon Flute is within this many seconds");
            DrawRoundedSliderFloat(0, 5, BLU_DoT_RefreshLeadSec,
                "DoT refresh: reapply this many seconds before expiry", digits: 1);

            ImGui.Separator();

            DrawRoundedSliderFloat(0, 10, BLU_ColdFog_LeadSeconds,
                "Cold Fog: pre-proc this many seconds before an incoming raidwide", digits: 1);
            DrawSliderInt(0, 100, BLU_ProphylacticMit_HPPercent,
                "Prophylactic mit (Gobskin/Dragon Force/Force Field): party HP% gate");
            DrawRoundedSliderFloat(0, 10, BLU_ProphylacticMit_LeadSeconds,
                "Prophylactic mit: lead time before incoming damage", digits: 1);

            ImGui.Separator();

            DrawRoundedSliderFloat(0, 30, BLU_BMR_Distance_Tank,
                "BossMod Reborn distance under Tank mimic", digits: 1);
            DrawRoundedSliderFloat(0, 30, BLU_BMR_Distance_DPS,
                "BossMod Reborn distance under DPS / None mimic", digits: 1);
            DrawRoundedSliderFloat(0, 30, BLU_BMR_Distance_Healer,
                "BossMod Reborn distance under Healer mimic", digits: 1);

            ImGui.Unindent();
        }

        private static void DrawHealTuningSliders()
        {
            DrawSliderInt(0, 100, BLU_Heal_PartyHPPercent,
                "Party HP% -> AoE heal (White Wind / Stotram)");
            DrawSliderInt(0, 100, BLU_Heal_PomCureHPPercent,
                "Single-target HP% -> Pom Cure");
            DrawSliderInt(0, 100, BLU_Heal_EmergencyHPPercent,
                "Emergency HP% gate under DPS / Tank mimic");
        }

        #endregion
=======
        public static UserInt
            BLU_DoTHP = new("BLU_DoTHP", 2),
            BLU_DoTTime = new("BLU_DoTTime", 3),
            BLU_Balance_Content = new("BLU_Balance_Content", 1),
            BLU_SelectedOpener = new("BLU_SelectedOpener", 0);
>>>>>>> C:/Scripts/nightly-upstream-merge/_scratch-20260830/theirs.tmp

        internal static void Draw(Preset preset)
        {
            switch (preset)
            {
<<<<<<< C:/Scripts/nightly-upstream-merge/_scratch-20260830/ours.tmp
                case Preset.BLU_AutoRotation_DPS:
                    DrawAdditionalBoolChoice(BLU_Tank_AutoMightyGuard,
                        "Auto Mighty Guard (Tank mimic)",
                        "Ensure Mighty Guard is ON while in the Tank lane. Never auto-cancels.");
                    DrawAbilityToggles();
                    DrawDpsTuningSliders();
                    break;

                case Preset.BLU_AutoRotation_Heal:
                    DrawHealTuningSliders();
=======
                case Preset.BLU_ST_DPS_Opener:
                    DrawBossOnlyChoice(BLU_Balance_Content);
                    ImGuiEx.TextUnderlined("Select Opener");
                    ImGui.Spacing();
                    DrawRadioButton(BLU_SelectedOpener,
                        "Winged Opener",
                        "Winged Reprobation opener. Standard 2.50 spell speed.", 0, descriptionAsTooltip: true);
                    DrawRadioButton(BLU_SelectedOpener,
                        "DoT Opener",
                        "Mortal Flame or Breath of Magic instead of Winged Reprobation. Requires 2.20 or faster spell speed.",
                        1, descriptionAsTooltip: true);
                    break;

                case Preset.BLU_ST_DPS_SongOfTorment:
                case Preset.BLU_ST_DPS_Breath:
                case Preset.BLU_ST_DPS_Flame:
                case Preset.BLU_ST_Tank_SongOfTorment:
                    DrawSliderInt(0, 100, BLU_DoTHP, Generics.StopEnemyHpPercent);
                    DrawSliderInt(0, 15, BLU_DoTTime, Generics.StopSeconds);
>>>>>>> C:/Scripts/nightly-upstream-merge/_scratch-20260830/theirs.tmp
                    break;
            }
        }
    }
}
