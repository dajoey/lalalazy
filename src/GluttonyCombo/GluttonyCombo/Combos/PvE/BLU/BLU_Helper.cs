using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GluttonyCombo.Combos.PvE;

// BLU AutoRotation â€” Phase 2: complete DPET engine with full spell catalog,
// AoE-aware scoring, heal preset, and conditional/gated spells.
// See CHANGELOG. Key behaviors: GCD spells are NOT gated on the global-GCD cooldown (only
// real cooldowns gate); oGCDs weave on CanWeave(); DoTs use per-action JustUsed cadence;
// Surpanakha fires on any available charge; channels (Phantom Flurry/Apokalypsis) are cast
// when stationary+in-range then held with All.SavageBlade (a no-op) so they aren't cancelled;
// Winged Reprobation runs its 4-stack -> Conviction Marcato chain via OriginalHook.
// AoE: every candidate spell's effective potency is multiplied by the number of targets it
// will hit (via NumberOfEnemiesInRange), so the engine naturally transitions from ST to AoE.
internal partial class BLU
{
    // â”€â”€ Action IDs not already defined in BLU.cs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public const uint
        WaterCannon       = 11385,
        FlyingSardine     = 11423,
        FlameThrower      = 11402,
        BloodDrain        = 11395,
        GoblinPunch       = 34563,
        MountainBuster    = 11428,
        Quasar            = 18324,
        BothEnds          = 23287,
        AquaBreath        = 11390,
        HighVoltage       = 11387,
        Glower            = 11404,
        Plaincracker      = 11391,
        DrillCannons      = 11398,
        ThousandNeedles   = 11397,
        Stotram           = 23269,
        AetherialSpark    = 23281,
        Apokalypsis       = 34581,
        RevengeBlast      = 18316,
        AbyssalTransfixion= 18300,
        Reflux            = 18319,
        FlyingFrenzy      = 11389,
        MindBlast         = 11394,
        BombToss          = 11396,
        TheLook           = 11399,
        FireAngon         = 11425,
        InkJet            = 11422,
        DragonsVoice      = 11420,
        FourTonzeWeight   = 11384,
        AlpineDraft       = 18295,
        ProteanWave       = 18296,
        Northerlies       = 18297,
        Electrogenesis    = 18298,
        Kaltstrahl        = 18299,
        TatamiGaeshi      = 23266,
        SaintlyBeam       = 23270,
        FeculentFlood     = 23271,
        Blaze             = 23278,
        MaledictionOfWater= 23283,
        ChocoMeteor       = 23284,
        ConvictionMarcato = 34574,
        RubyDynamics      = 34571,
        DivisionRune      = 34572,
        LaserEye          = 34577,
        RightRound        = 34564,
        ChelonianGate     = 34575,
        WhiteDeath        = 23268,
        DivineCataract    = 23274,
        // Self-KO / %HP (off by default)
        FinalStingId      = 11407,
        SelfDestructId    = 11408,
        WildRage          = 34568,
        Missile           = 11405,
        TailScrew         = 11413,
        DimensionalShift  = 34573,
        Launcher          = 18313,
        CandyCane         = 34578,
        // Heal spells
        WhiteWind         = 11406,
        PomCure           = 18303,
        AngelsSnack       = 23272,
        Gobskin           = 18304,
        Exuviation        = 18318,
        Rehydration       = 34566;

    private const ushort WingedRedemptionStatus = 3641;
    private const ushort ApokalypsisStatus      = 3644;
    private const ushort TouchOfFrost            = 2494;
    private const ushort AuspiciousTrance        = 2497;

    internal enum BluAspect { Physical, Magical, Unaspected }
    internal enum BluShape  { ST, Line, Cone, Circle, PBAoE, Ring, Ground, Melee }

    internal sealed class BluSpell
    {
        public uint Id;
        public string Name = "";
        public int Potency;
        public int AltPotency;           // conditional higher potency
        public int DotPotency;
        public int DotDurationS;
        public ushort DotStatus;
        public bool NoTimer;             // permanent DoT
        public float CastS;             // 0 = instant
        public bool IsOgcd;
        public bool IsCharge;
        public bool Melee;               // requires InMeleeRange()
        public bool FillerOnly;          // only use when nothing else is up
        public bool IsAoE;               // uses NumberOfEnemiesInRange for scoring
        public bool SelfKO;              // off by default
        public bool PercentHP;           // gimmick, rank last
        public float CooldownS;         // real cooldown (0 = shares GCD only)
        public BluAspect Aspect = BluAspect.Magical;
        public BluShape Shape = BluShape.ST;
        public uint[] WantsBuffs = [];
        public ushort RequiresStatus;    // gate on player having this status
        public bool IsChannel;           // channel spell
        public float ChannelDuration;    // total channel time
        public int ChannelFinisher;      // finisher potency (Phantom Flurry)
        public uint SharesRecast;        // mutually exclusive recast
    }

    private const float OgcdCost     = 0.6f;
    private const float GcdCost      = 2.5f;
    private const float DotRefresh   = 3f;
    private const float DotApplyGrace= 8f;
    private const int BuffWorthPotency = 400;

    // â”€â”€ FULL SPELL CATALOG â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    internal static readonly List<BluSpell> FullCatalog =
    [
        // â•â•â•â•â•â•â•â• ST/Filler GCDs â•â•â•â•â•â•â•â•
        new() { Id = SonicBoom,         Name = "Sonic Boom",        Potency = 210, Aspect = BluAspect.Magical },
        new() { Id = WaterCannon,       Name = "Water Cannon",      Potency = 200, Aspect = BluAspect.Magical },
        new() { Id = SharpenedKnife,    Name = "Sharpened Knife",   Potency = 220, Aspect = BluAspect.Physical, Melee = true },
        new() { Id = GoblinPunch,       Name = "Goblin Punch",      Potency = 210, Aspect = BluAspect.Physical, Melee = true },
        new() { Id = Glower,            Name = "Glower",            Potency = 220, CastS = 2f, Aspect = BluAspect.Magical },
        new() { Id = MustardBomb,       Name = "Mustard Bomb",      Potency = 220, CastS = 2f, Aspect = BluAspect.Magical },
        new() { Id = AbyssalTransfixion,Name = "Abyssal Transfixion",Potency = 220, CastS = 2f, Aspect = BluAspect.Physical },
        new() { Id = Reflux,            Name = "Reflux",            Potency = 220, CastS = 2f, Aspect = BluAspect.Magical },
        new() { Id = MatraMagic,        Name = "Matra Magic",       Potency = 400, CastS = 2f, Aspect = BluAspect.Magical, WantsBuffs = [Bristle] },
        new() { Id = TripleTrident,     Name = "Triple Trident",    Potency = 600, Aspect = BluAspect.Physical, Melee = true, CooldownS = 90f, WantsBuffs = [Whistle, Tingle] },
        new() { Id = RevengeBlast,      Name = "Revenge Blast",     Potency = 50, AltPotency = 500, CastS = 2f, Aspect = BluAspect.Physical, Melee = true },
        new() { Id = FlyingSardine,     Name = "Flying Sardine",    Potency = 10, Aspect = BluAspect.Physical },
        new() { Id = BloodDrain,        Name = "Blood Drain",       Potency = 50, Aspect = BluAspect.Unaspected },

        // â•â•â•â•â•â•â•â• AoE GCDs â•â•â•â•â•â•â•â•
        new() { Id = DrillCannons,      Name = "Drill Cannons",     Potency = 220, CastS = 1f, Aspect = BluAspect.Physical, IsAoE = true, Shape = BluShape.Line },
        new() { Id = PerpetualRay,      Name = "Perpetual Ray",     Potency = 220, CastS = 1f, Aspect = BluAspect.Physical, IsAoE = true, Melee = true },
        new() { Id = WhiteKnightsTour,  Name = "White Knight Tour", Potency = 200, CastS = 2f, Aspect = BluAspect.Physical, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = BlackKnightsTour,  Name = "Black Knight Tour", Potency = 200, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = AquaBreath,        Name = "Aqua Breath",       Potency = 140, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Cone },
        new() { Id = HighVoltage,       Name = "High Voltage",      Potency = 200, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.PBAoE },
        new() { Id = ThousandNeedles,   Name = "1000 Needles",      Potency = 220, CastS = 4f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.PBAoE },
        new() { Id = Plaincracker,      Name = "Plaincracker",      Potency = 220, CastS = 2f, Aspect = BluAspect.Physical, IsAoE = true, Shape = BluShape.PBAoE },
        new() { Id = Stotram,           Name = "Stotram",           Potency = 150, CastS = 2f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.PBAoE },
        new() { Id = PeripheralSynthesis,Name = "Periph. Synthesis",Potency = 150, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Line },
        new() { Id = RamsVoice,         Name = "Ram's Voice",       Potency = 220, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.PBAoE, FillerOnly = true },
        new() { Id = FlyingFrenzy,      Name = "Flying Frenzy",     Potency = 150, CastS = 1f, Aspect = BluAspect.Physical, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = MindBlast,         Name = "Mind Blast",        Potency = 200, CastS = 1f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.PBAoE },
        new() { Id = BombToss,          Name = "Bomb Toss",         Potency = 200, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Ground },
        new() { Id = TheLook,           Name = "The Look",          Potency = 220, CastS = 2f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.Cone },
        new() { Id = FireAngon,         Name = "Fire Angon",        Potency = 200, CastS = 1f, Aspect = BluAspect.Physical, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = InkJet,            Name = "Ink Jet",           Potency = 200, CastS = 2f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.Cone },
        new() { Id = DragonsVoice,      Name = "Dragon's Voice",    Potency = 200, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Ring },
        new() { Id = FourTonzeWeight,   Name = "4-tonze Weight",    Potency = 200, CastS = 2f, Aspect = BluAspect.Physical, IsAoE = true, Shape = BluShape.Ground },
        new() { Id = AlpineDraft,       Name = "Alpine Draft",      Potency = 220, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Line },
        new() { Id = ProteanWave,       Name = "Protean Wave",      Potency = 220, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Cone },
        new() { Id = Northerlies,       Name = "Northerlies",       Potency = 220, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Cone },
        new() { Id = Electrogenesis,    Name = "Electrogenesis",    Potency = 220, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = Kaltstrahl,        Name = "Kaltstrahl",        Potency = 220, CastS = 2f, Aspect = BluAspect.Physical, IsAoE = true, Shape = BluShape.Cone },
        new() { Id = TatamiGaeshi,      Name = "Tatami-gaeshi",     Potency = 220, CastS = 2f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.Line },
        new() { Id = SaintlyBeam,       Name = "Saintly Beam",      Potency = 100, AltPotency = 500, CastS = 2f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = FeculentFlood,     Name = "Feculent Flood",    Potency = 220, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Line },
        new() { Id = Blaze,             Name = "Blaze",             Potency = 220, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = HydroPull,         Name = "Hydro Pull",        Potency = 220, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.PBAoE },
        new() { Id = MaledictionOfWater,Name = "Maledict. of Water",Potency = 200, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Line },
        new() { Id = ChocoMeteor,       Name = "Choco Meteor",      Potency = 200, CastS = 2f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = Tingle,            Name = "Tingle (dmg)",      Potency = 100, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = PeatPelt,          Name = "Peat Pelt",         Potency = 100, CastS = 2f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = DeepClean,         Name = "Deep Clean",        Potency = 220, CastS = 2f, Aspect = BluAspect.Physical, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = LaserEye,          Name = "Laser Eye",         Potency = 220, CastS = 2f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.Circle },
        new() { Id = RightRound,        Name = "Right Round",       Potency = 110, CastS = 2f, Aspect = BluAspect.Physical, IsAoE = true, Shape = BluShape.PBAoE },
        new() { Id = DivisionRune,      Name = "Divination Rune",   Potency = 100, CastS = 2f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.Cone },
        new() { Id = BadBreath,         Name = "Bad Breath",        Potency = 20, CastS = 2f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.Cone },
        new() { Id = RubyDynamics,      Name = "Ruby Dynamics",     Potency = 220, CastS = 2f, Aspect = BluAspect.Physical, IsAoE = true, Shape = BluShape.Cone, CooldownS = 30f },
        new() { Id = Devour,            Name = "Devour",            Potency = 250, CastS = 1f, Aspect = BluAspect.Unaspected, Melee = true, CooldownS = 60f },
        new() { Id = MagicHammer,       Name = "Magic Hammer",      Potency = 250, CastS = 1f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.Circle, CooldownS = 90f },
        new() { Id = CandyCane,         Name = "Candy Cane",        Potency = 250, CastS = 1f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.Circle, CooldownS = 90f, SharesRecast = MagicHammer },

        // â•â•â•â•â•â•â•â• Conditional/Gated GCDs â•â•â•â•â•â•â•â•
        new() { Id = WhiteDeath,        Name = "White Death",       Potency = 400, Aspect = BluAspect.Magical, RequiresStatus = TouchOfFrost },
        new() { Id = DivineCataract,    Name = "Divine Cataract",   Potency = 500, Aspect = BluAspect.Magical, RequiresStatus = AuspiciousTrance },
        new() { Id = ConvictionMarcato, Name = "Conviction Marcato",Potency = 220, CastS = 2f, Aspect = BluAspect.Unaspected, IsAoE = true, Shape = BluShape.Line, RequiresStatus = WingedRedemptionStatus },

        // â•â•â•â•â•â•â•â• DoTs â•â•â•â•â•â•â•â•
        new() { Id = SongOfTorment,     Name = "Song of Torment",   DotPotency = 50,  DotDurationS = 30, DotStatus = Debuffs.SongOfTorment, CastS = 2f, Aspect = BluAspect.Unaspected, WantsBuffs = [Bristle] },
        new() { Id = BreathOfMagic,     Name = "Breath of Magic",   DotPotency = 120, DotDurationS = 60, DotStatus = Debuffs.BreathOfMagic, CastS = 2f, Aspect = BluAspect.Unaspected, WantsBuffs = [Bristle] },
        new() { Id = MortalFlame,       Name = "Mortal Flame",      DotPotency = 40,  DotDurationS = 90, DotStatus = Debuffs.MortalFlame, NoTimer = true, CastS = 2f, Aspect = BluAspect.Magical, WantsBuffs = [Bristle] },
        new() { Id = AetherialSpark,    Name = "Aetherial Spark",   Potency = 50, DotPotency = 50, DotDurationS = 15, DotStatus = 0, Aspect = BluAspect.Magical },

        // â•â•â•â•â•â•â•â• oGCDs (weave) â•â•â•â•â•â•â•â•
        new() { Id = FeatherRain,       Name = "Feather Rain",      Potency = 100, IsOgcd = true, CooldownS = 30f, Aspect = BluAspect.Physical, IsAoE = true },
        new() { Id = Eruption,          Name = "Eruption",          Potency = 200, IsOgcd = true, CooldownS = 30f, Aspect = BluAspect.Magical, IsAoE = true },
        new() { Id = ShockStrike,       Name = "Shock Strike",      Potency = 400, IsOgcd = true, CooldownS = 60f, Aspect = BluAspect.Magical },
        new() { Id = MountainBuster,    Name = "Mountain Buster",   Potency = 400, IsOgcd = true, CooldownS = 60f, Aspect = BluAspect.Physical },
        new() { Id = RoseOfDestruction, Name = "Rose of Destr.",    Potency = 400, IsOgcd = true, CooldownS = 30f, Aspect = BluAspect.Magical },
        new() { Id = GlassDance,        Name = "Glass Dance",       Potency = 250, IsOgcd = true, CooldownS = 90f, Aspect = BluAspect.Magical },
        new() { Id = JKick,             Name = "J Kick",            Potency = 350, IsOgcd = true, CooldownS = 90f, Aspect = BluAspect.Physical },
        new() { Id = Quasar,            Name = "Quasar",            Potency = 300, IsOgcd = true, CooldownS = 60f, Aspect = BluAspect.Unaspected },
        new() { Id = Nightbloom,        Name = "Nightbloom",        Potency = 400, IsOgcd = true, CooldownS = 120f, Aspect = BluAspect.Physical },
        new() { Id = BothEnds,          Name = "Both Ends",         Potency = 700, IsOgcd = true, CooldownS = 90f, Aspect = BluAspect.Unaspected },
        new() { Id = SeaShanty,         Name = "Sea Shanty",        Potency = 500, IsOgcd = true, CooldownS = 120f, Aspect = BluAspect.Magical },
        new() { Id = BeingMortal,       Name = "Being Mortal",      Potency = 800, IsOgcd = true, CooldownS = 120f, Aspect = BluAspect.Unaspected, SharesRecast = Apokalypsis },
        new() { Id = Surpanakha,        Name = "Surpanakha",        Potency = 200, IsOgcd = true, IsCharge = true, CooldownS = 30f, Aspect = BluAspect.Magical },

        // â•â•â•â•â•â•â•â• Channels â•â•â•â•â•â•â•â•
        new() { Id = PhantomFlurry,     Name = "Phantom Flurry",    Potency = 200, IsChannel = true, ChannelDuration = 5f, ChannelFinisher = 600, CooldownS = 120f, Aspect = BluAspect.Physical, IsAoE = true, Shape = BluShape.Cone },
        new() { Id = Apokalypsis,       Name = "Apokalypsis",       Potency = 140, IsChannel = true, ChannelDuration = 10f, CooldownS = 120f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Line, SharesRecast = BeingMortal },
        new() { Id = FlameThrower,     Name = "Flame Thrower",     Potency = 220, IsChannel = true, ChannelDuration = 10f, Aspect = BluAspect.Magical, IsAoE = true, Shape = BluShape.Cone },

        // â•â•â•â•â•â•â•â• Self-KO (OFF by default, never auto-cast) â•â•â•â•â•â•â•â•
        new() { Id = FinalStingId,      Name = "Final Sting",       Potency = 2000, SelfKO = true, Aspect = BluAspect.Physical },
        new() { Id = SelfDestructId,    Name = "Self-destruct",     Potency = 1500, SelfKO = true, IsAoE = true, Aspect = BluAspect.Physical },
        new() { Id = WildRage,          Name = "Wild Rage",         Potency = 500,  SelfKO = true, Aspect = BluAspect.Physical },

        // â•â•â•â•â•â•â•â• %HP Gimmick (rank last, never auto-cast) â•â•â•â•â•â•â•â•
        new() { Id = Missile,           Name = "Missile",           Potency = 0, PercentHP = true },
        new() { Id = TailScrew,         Name = "Tail Screw",        Potency = 0, PercentHP = true },
        new() { Id = DimensionalShift,  Name = "Dimensional Shift", Potency = 0, PercentHP = true },
        new() { Id = Launcher,          Name = "Launcher",          Potency = 0, PercentHP = true },
    ];

    internal static ushort BuffStatusOf(uint spellId) => spellId switch
    {
        Bristle => Buffs.Bristle,
        Whistle => Buffs.Whistle,
        Tingle  => Buffs.Tingle,
        _ => 0,
    };

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  DPS PRESET â€” BLU_ST_AdvancedMode (enum 70030)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    internal class BLU_ST_AdvancedMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BLU_ST_AdvancedMode;

        // â”€â”€ Debug state â”€â”€
        internal static bool DbgCanWeave;
        internal static uint DbgWeavePick;
        internal static uint DbgGcdPick;
        internal static string DbgNote = "";

        internal static List<string> BluDebugRows()
        {
            var rows = new List<string>
            {
                $"CanWeave={DbgCanWeave}  WeavePick={DbgWeavePick}  GcdPick={DbgGcdPick}  Note={DbgNote}",
                $"MoonFlute={HasStatusEffect(Buffs.MoonFlute)}  Bristle={HasStatusEffect(Buffs.Bristle)}  Whistle={HasStatusEffect(Buffs.Whistle)}  Tingle={HasStatusEffect(Buffs.Tingle)}",
                $"HP%={PlayerHealthPercentageHp():F0}  Moving={IsMoving()}  MeleeRange={InMeleeRange()}",
                ""
            };
            foreach (var s in FullCatalog)
            {
                if (s.SelfKO || s.PercentHP) continue;
                bool active = IsSpellActive(s.Id);
                if (!active) continue;
                var cd = GetCooldown(s.Id);
                bool ready = s.IsOgcd ? !cd.IsCooldown : (!cd.IsCooldown || cd.CooldownTotal <= 3f);
                string flags = $"{(ready ? "R" : "-")}{(s.IsOgcd ? "o" : "g")}{(s.IsAoE ? "A" : " ")}{(s.IsChannel ? "C" : " ")}";
                int enemies = s.IsAoE ? NumberOfEnemiesInRange(s.Id) : 1;
                string dotInfo = s.DotDurationS > 0 ? $" DoT:{s.DotPotency}x{s.DotDurationS}s" : "";
                rows.Add($"*{flags} {s.Name,-22} p={s.Potency,4} enem={enemies} rem={cd.CooldownRemaining:0.0} chg={cd.RemainingCharges}/{cd.MaxCharges}{dotInfo}");
            }
            return rows;
        }

        protected override uint Invoke(uint actionID)
        {
            if (actionID is not SonicBoom)
                return actionID;

            DbgNote = "";

            // â”€â”€ Channel hold: never cancel an active channel â”€â”€
            if (HasStatusEffect(Buffs.PhantomFlurry) || JustUsed(PhantomFlurry, 2f))
            {
                var pf = GetStatusEffect(Buffs.PhantomFlurry);
                if (IsSpellActive(PhantomFlurry) && pf is not null && pf.RemainingTime is > 0 and <= 1.2f)
                {
                    DbgNote = "PF finisher";
                    return OriginalHook(PhantomFlurry);
                }
                DbgNote = "PF hold";
                return All.SavageBlade;
            }
            if (HasStatusEffect(ApokalypsisStatus) || JustUsed(Apokalypsis, 2f))
            {
                DbgNote = "Apok hold";
                return All.SavageBlade;
            }

            // â”€â”€ Start a channel when stationary and in range â”€â”€
            if (!IsMoving())
            {
                if (IsSpellActive(PhantomFlurry) && IsOffCooldown(PhantomFlurry) && InActionRange(PhantomFlurry))
                {
                    DbgNote = "PF start";
                    return PhantomFlurry;
                }
                // Apokalypsis yields to Being Mortal if both share recast
                if (IsSpellActive(Apokalypsis) && IsOffCooldown(Apokalypsis)
                    && (!IsSpellActive(BeingMortal) || !IsOffCooldown(BeingMortal))
                    && InActionRange(Apokalypsis))
                {
                    DbgNote = "Apok start";
                    return Apokalypsis;
                }
            }

            // â”€â”€ Winged Reprobation chain: finish once started â”€â”€
            if (IsSpellActive(WingedReprobation))
            {
                // If we have Winged Redemption status -> fire Conviction Marcato
                if (HasStatusEffect(WingedRedemptionStatus))
                {
                    DbgNote = "WR->CM";
                    return OriginalHook(WingedReprobation);
                }
                // If chain is in progress (1-3 stacks), continue
                int stacks = GetStatusEffect(Buffs.WingedReprobation)?.Param ?? 0;
                if (stacks >= 1 && stacks <= 3 && IsOffCooldown(OriginalHook(WingedReprobation)))
                {
                    DbgNote = $"WR chain stk={stacks}";
                    return OriginalHook(WingedReprobation);
                }
            }

            // â”€â”€ Conditional spells: fire immediately if their enabler buff is up â”€â”€
            if (HasStatusEffect(TouchOfFrost) && IsSpellActive(WhiteDeath) && ReadyGcd(WhiteDeath))
            {
                DbgNote = "WhiteDeath proc";
                return WhiteDeath;
            }
            if (HasStatusEffect(AuspiciousTrance) && IsSpellActive(DivineCataract) && ReadyGcd(DivineCataract))
            {
                DbgNote = "DivineCataract proc";
                return DivineCataract;
            }

            // --- Surpanakha charge dump: keep firing until all charges spent ---
            if (IsSpellActive(Surpanakha) && JustUsed(Surpanakha, 2f) && GetRemainingCharges(Surpanakha) > 0)
            {
                DbgNote = "Surp dump";
                return Surpanakha;
            }

            // â”€â”€ oGCD weave â”€â”€
            DbgCanWeave = CanWeave();
            if (DbgCanWeave)
            {
                uint og = BestWeave();
                DbgWeavePick = og;
                if (og != 0)
                    return og;
            }

            // â”€â”€ GCD selection â”€â”€
            uint gcd = BestGcd();
            DbgGcdPick = gcd;
            return gcd != 0 ? gcd : actionID;
        }

        // â”€â”€ Buff multiplier â”€â”€
        private static double Mult(BluSpell s)
        {
            double m = 1.0;
            if (HasStatusEffect(Buffs.MoonFlute))
                m *= 1.5;
            if (HasStatusEffect(Debuffs.Offguard, CurrentTarget, true))
                m *= 1.05;
            if (!s.IsOgcd)
            {
                if (s.Aspect != BluAspect.Physical && HasStatusEffect(Buffs.Bristle))
                    m *= 1.5;
                if (s.Aspect == BluAspect.Physical && HasStatusEffect(Buffs.Whistle))
                    m *= 1.5;
            }
            return m;
        }

        private static float Cost(BluSpell s)
        {
            if (s.IsOgcd) return OgcdCost;
            if (s.IsChannel) return s.ChannelDuration;
            return Math.Max(s.CastS, GcdCost);
        }

        // GCD spells: only "not ready" if they have a REAL cooldown (> the global GCD).
        // Spells with CooldownS > 0 in the catalog use IsOffCooldown (real CD gating).
        private static bool ReadyGcd(uint id)
        {
            var entry = FullCatalog.FirstOrDefault(s => s.Id == id);
            if (entry is not null && entry.CooldownS > 0)
                return IsOffCooldown(id);
            var cd = GetCooldown(id);
            return !cd.IsCooldown || cd.CooldownTotal <= 3f;
        }

        private bool DotIsUp(BluSpell s)
        {
            float window = s.NoTimer ? 300f : Math.Max(s.DotDurationS - DotRefresh, DotApplyGrace);
            if (JustUsed(s.Id, window))
                return true;
            if (s.DotStatus != 0 && HasStatusEffect(s.DotStatus, CurrentTarget, true)
                && (s.NoTimer || GetStatusEffectRemainingTime(s.DotStatus, CurrentTarget, true) > DotRefresh))
                return true;
            return false;
        }

        private static int AoeFactor(BluSpell s)
        {
            if (!s.IsAoE) return 1;
            int count = NumberOfEnemiesInRange(s.Id);
            return count < 1 ? 1 : count;
        }

        private uint BestWeave()
        {
            // Surpanakha dump: if we just used it and charges remain, force it
            if (IsSpellActive(Surpanakha) && JustUsed(Surpanakha, 2f) && GetRemainingCharges(Surpanakha) > 0)
                return Surpanakha;

            uint best = 0;
            double bestVal = 0;
            foreach (var s in FullCatalog)
            {
                if (!s.IsOgcd || s.SelfKO || s.PercentHP || s.IsChannel)
                    continue;
                if (!IsSpellActive(s.Id))
                    continue;
                // Shared recast check
                if (s.SharesRecast != 0 && IsOnCooldown(s.Id))
                    continue;
                if (s.IsCharge)
                {
                    if (GetRemainingCharges(s.Id) == 0)
                        continue;
                }
                else if (!IsOffCooldown(s.Id))
                    continue;

                int pot = s.Potency;
                int aoeMult = AoeFactor(s);
                double val = pot * aoeMult * Mult(s) / Cost(s);
                if (val > bestVal) { bestVal = val; best = s.Id; }
            }
            return best;
        }

        private uint BestGcd()
        {
            bool targetAlive = CurrentTarget is not null && !TargetIsDead() && GetTargetHPPercent() > 2f;

            // â”€â”€ DoT lane â”€â”€
            uint dotAct = 0; double dotVal = 0; int dotPot = 0;
            if (targetAlive)
            {
                foreach (var s in FullCatalog)
                {
                    if (s.DotDurationS == 0 || s.SelfKO || s.PercentHP || !IsSpellActive(s.Id))
                        continue;
                    if (DotIsUp(s))
                        continue;
                    if (!ReadyGcd(s.Id))
                        continue;
                    if (s.CastS > 0 && IsMoving())
                        continue;
                    int total = s.DotPotency * (s.DotDurationS / 3) + s.Potency;
                    double val = total * Mult(s) / Cost(s);
                    if (val > dotVal) { dotVal = val; dotAct = s.Id; dotPot = total; }
                }
            }

            // â”€â”€ Winged Reprobation fresh chain â”€â”€ (start through normal priority)
            uint wrAct = 0; double wrVal = 0;
            if (targetAlive && IsSpellActive(WingedReprobation) && IsOffCooldown(WingedReprobation))
            {
                int stacks = GetStatusEffect(Buffs.WingedReprobation)?.Param ?? 0;
                if (stacks == 0 && !HasStatusEffect(WingedRedemptionStatus))
                {
                    wrAct = OriginalHook(WingedReprobation);
                    // Total chain: 120+220+300+400+440(CM) = 1480 potency over 5 GCDs -> 296/GCD
                    wrVal = 296 * Mult(new BluSpell { Aspect = BluAspect.Unaspected }) / GcdCost;
                }
            }

            // â”€â”€ Direct nuke lane â”€â”€
            uint nukeAct = 0; double nukeVal = 0; int nukePot = 0;
            uint fillAct = 0; double fillVal = 0; int fillPot = 0;
            foreach (var s in FullCatalog)
            {
                if (s.IsOgcd || s.IsChannel || s.DotDurationS != 0 || s.SelfKO || s.PercentHP)
                    continue;
                if (s.Potency == 0 && s.AltPotency == 0)
                    continue;
                if (!IsSpellActive(s.Id))
                    continue;
                if (!ReadyGcd(s.Id))
                    continue;
                if (s.Melee && !InMeleeRange())
                    continue;
                if (s.RequiresStatus != 0 && !HasStatusEffect(s.RequiresStatus))
                    continue;
                // Shared recast
                if (s.SharesRecast != 0 && IsOnCooldown(s.Id))
                    continue;
                // Moving check for cast-time spells
                if (s.CastS > 0 && IsMoving())
                    continue;

                int basePot = s.Potency;
                // Alt potency conditions
                if (s.Id == RevengeBlast)
                    basePot = PlayerHealthPercentageHp() < 20f ? s.AltPotency : s.Potency;
                else if (s.Id == MatraMagic && HasStatusEffect(Buffs.DPSMimicry))
                    basePot = 800;

                int aoeMult = AoeFactor(s);
                double eff = basePot * aoeMult * Mult(s) + (HasStatusEffect(Buffs.Tingle) && !s.IsOgcd ? 100 * aoeMult : 0);
                double val = eff / Cost(s);

                if (s.FillerOnly)
                { if (val > fillVal) { fillVal = val; fillAct = s.Id; fillPot = basePot; } }
                else
                { if (val > nukeVal) { nukeVal = val; nukeAct = s.Id; nukePot = basePot; } }
            }
            if (nukeAct == 0 && fillAct != 0)
            { nukeAct = fillAct; nukeVal = fillVal; nukePot = fillPot; }

            // â”€â”€ Pick best between DoT, WR chain, and nuke â”€â”€
            uint payload; int payloadPot; uint[] wants;
            if (wrVal > dotVal && wrVal > nukeVal && wrAct != 0)
            {
                DbgNote = "WR fresh chain";
                return wrAct;
            }
            if (dotVal >= nukeVal && dotAct != 0)
            { payload = dotAct; payloadPot = dotPot; wants = WantsOf(dotAct); }
            else
            { payload = nukeAct; payloadPot = nukePot; wants = WantsOf(nukeAct); }

            if (payload == 0)
                return 0;

            // â”€â”€ Buff GCDs before big payloads â”€â”€
            if (payloadPot >= BuffWorthPotency)
            {
                foreach (uint buffSpell in wants)
                {
                    ushort status = BuffStatusOf(buffSpell);
                    if (status != 0 && IsSpellActive(buffSpell) && ReadyGcd(buffSpell)
                        && !HasStatusEffect(status) && !JustUsed(buffSpell))
                    {
                        // Don't cast buff with cast time while moving
                        if (IsMoving()) continue;
                        return buffSpell;
                    }
                }
            }

            return payload;
        }

        private static uint[] WantsOf(uint id)
        {
            foreach (var s in FullCatalog)
                if (s.Id == id)
                    return s.WantsBuffs;
            return [];
        }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  HEAL PRESET â€” BLU_Heal_AdvancedMode (enum 70031)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    internal class BLU_Heal_AdvancedMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BLU_Heal_AdvancedMode;

        protected override uint Invoke(uint actionID)
        {
            if (actionID is not SonicBoom)
                return actionID;

            // â”€â”€ Raise dead party members â”€â”€
            if (IsSpellActive(AngelWhisper) && IsOffCooldown(AngelWhisper))
            {
                var dead = GetPartyMembers()
                    .Where(m => m.BattleChara is not null && m.BattleChara.IsDead)
                    .FirstOrDefault();
                if (dead.BattleChara is not null)
                    return AngelWhisper;
            }

            // â”€â”€ Emergency healing: party member below 50% â”€â”€
            bool needHeal = false;
            float lowestHp = 100f;
            foreach (var m in GetPartyMembers())
            {
                if (m.BattleChara is null || m.BattleChara.IsDead) continue;
                float hp = GetMemberHPPercent(m);
                if (hp < lowestHp) lowestHp = hp;
                if (hp < 50f) needHeal = true;
            }

            // Self-heal check
            if (PlayerHealthPercentageHp() < 50f)
                needHeal = true;

            if (needHeal)
            {
                // Gobskin (shield) if available and nobody has it
                if (IsSpellActive(Gobskin) && ReadyGcdH(Gobskin) && !JustUsed(Gobskin, 30f))
                    return Gobskin;

                // Angel's Snack (AoE heal)
                if (IsSpellActive(AngelsSnack) && ReadyGcdH(AngelsSnack))
                    return AngelsSnack;

                // Pom Cure (ST heal)
                if (IsSpellActive(PomCure) && ReadyGcdH(PomCure))
                    return PomCure;

                // White Wind (heals self+party based on own HP, use carefully)
                if (IsSpellActive(WhiteWind) && ReadyGcdH(WhiteWind) && PlayerHealthPercentageHp() > 75f)
                    return WhiteWind;

                // Rehydration
                if (IsSpellActive(Rehydration) && ReadyGcdH(Rehydration))
                    return Rehydration;
            }

            // â”€â”€ Exuviation (AoE cleanse + heal) â”€â”€
            if (lowestHp < 70f && IsSpellActive(Exuviation) && ReadyGcdH(Exuviation))
                return Exuviation;

            // â”€â”€ Nobody needs healing: fall through to DPS engine â”€â”€
            return DpsEngine(actionID);
        }

        private static bool ReadyGcdH(uint id)
        {
            var cd = GetCooldown(id);
            return !cd.IsCooldown || cd.CooldownTotal <= 3f;
        }

        private static float GetMemberHPPercent(WrathPartyMember m)
        {
            if (m.BattleChara is null) return 100f;
            if (m.BattleChara.MaxHp == 0) return 100f;
            return (float)m.BattleChara.CurrentHp / m.BattleChara.MaxHp * 100f;
        }

        // Reuse the DPS logic
        private uint DpsEngine(uint actionID)
        {
            // Channel hold
            if (HasStatusEffect(Buffs.PhantomFlurry) || JustUsed(PhantomFlurry, 2f))
            {
                var pf = GetStatusEffect(Buffs.PhantomFlurry);
                if (IsSpellActive(PhantomFlurry) && pf is not null && pf.RemainingTime is > 0 and <= 1.2f)
                    return OriginalHook(PhantomFlurry);
                return All.SavageBlade;
            }
            if (HasStatusEffect(ApokalypsisStatus) || JustUsed(Apokalypsis, 2f))
                return All.SavageBlade;

            // Conditional procs
            if (HasStatusEffect(TouchOfFrost) && IsSpellActive(WhiteDeath) && ReadyGcdH(WhiteDeath))
                return WhiteDeath;
            if (HasStatusEffect(AuspiciousTrance) && IsSpellActive(DivineCataract) && ReadyGcdH(DivineCataract))
                return DivineCataract;

            // Weave
            if (CanWeave())
            {
                uint og = HealBestWeave();
                if (og != 0) return og;
            }

            // GCD
            uint gcd = HealBestGcd();
            return gcd != 0 ? gcd : actionID;
        }

        private static double HMult(BluSpell s)
        {
            double m = 1.0;
            if (HasStatusEffect(Buffs.MoonFlute)) m *= 1.5;
            if (HasStatusEffect(Debuffs.Offguard, CurrentTarget, true)) m *= 1.05;
            if (!s.IsOgcd)
            {
                if (s.Aspect != BluAspect.Physical && HasStatusEffect(Buffs.Bristle)) m *= 1.5;
                if (s.Aspect == BluAspect.Physical && HasStatusEffect(Buffs.Whistle)) m *= 1.5;
            }
            return m;
        }

        private uint HealBestWeave()
        {
            uint best = 0; double bestVal = 0;
            foreach (var s in FullCatalog)
            {
                if (!s.IsOgcd || s.SelfKO || s.PercentHP || s.IsChannel) continue;
                if (!IsSpellActive(s.Id)) continue;
                if (s.SharesRecast != 0 && IsOnCooldown(s.Id)) continue;
                if (s.IsCharge) { if (GetRemainingCharges(s.Id) == 0) continue; }
                else if (!IsOffCooldown(s.Id)) continue;
                int aoeMult = s.IsAoE ? Math.Max(1, NumberOfEnemiesInRange(s.Id)) : 1;
                double val = s.Potency * aoeMult * HMult(s) / OgcdCost;
                if (val > bestVal) { bestVal = val; best = s.Id; }
            }
            return best;
        }

        private uint HealBestGcd()
        {
            uint best = 0; double bestVal = 0;
            foreach (var s in FullCatalog)
            {
                if (s.IsOgcd || s.IsChannel || s.SelfKO || s.PercentHP || s.DotDurationS != 0) continue;
                if (s.Potency == 0 && s.AltPotency == 0) continue;
                if (!IsSpellActive(s.Id) || !ReadyGcdH(s.Id)) continue;
                if (s.Melee && !InMeleeRange()) continue;
                if (s.RequiresStatus != 0 && !HasStatusEffect(s.RequiresStatus)) continue;
                if (s.SharesRecast != 0 && IsOnCooldown(s.Id)) continue;
                if (s.CastS > 0 && IsMoving()) continue;
                if (s.FillerOnly) continue;

                int basePot = s.Potency;
                if (s.Id == RevengeBlast)
                    basePot = PlayerHealthPercentageHp() < 20f ? s.AltPotency : s.Potency;
                else if (s.Id == MatraMagic && HasStatusEffect(Buffs.DPSMimicry))
                    basePot = 800;

                int aoeMult = s.IsAoE ? Math.Max(1, NumberOfEnemiesInRange(s.Id)) : 1;
                float cost = s.CastS > 0 ? Math.Max(s.CastS, GcdCost) : GcdCost;
                double val = basePot * aoeMult * HMult(s) / cost;
                if (val > bestVal) { bestVal = val; best = s.Id; }
            }
            return best != 0 ? best : SonicBoom;
        }
    }
}
