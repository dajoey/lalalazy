using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS;
using GluttonyCombo.Extensions;
using System;
using System.Collections.Generic;

namespace GluttonyCombo.Combos.PvE;

// =====================================================================================
//  BLU AutoRotation — Phase 1.3: greedy DPET priority engine over the FULL damaging kit.
//
//  Goal: take ANY set of slotted spells and cast them in the order that does the most
//  damage. Every damaging Blue Mage spell (single-target AND AoE) is in the catalog and
//  scored live by damage-per-execution-time (potency x current buff multiplier / time
//  cost; oGCD ~0.6s weave lock, GCD 2.5s). Best damage oGCD weaves on CanWeave(); AoE
//  oGCDs and AoE GCDs are included because they still damage the primary target and serve
//  as filler so the character is never idle while something is castable.
//
//  DoTs are robust against status-readback quirks: treated as up if the debuff is detected
//  with time left OR we cast it on this exact target within its duration (per-target
//  wall-clock via JustUsedOn). Permanent DoTs (Mortal Flame -> RemainingTime reads 0) are
//  handled explicitly. Bristle/Whistle/Tingle are only spent in front of a payload big
//  enough (>= BuffWorth) to beat the GCD they cost; long DoTs (Mortal Flame is permanent)
//  are prime Bristle targets and snapshot it for the whole duration.
//
//  DELIBERATELY EXCLUDED from greedy auto (each for a concrete reason, handled in later
//  phases): channels that cancel if you press anything else (Flame Thrower, Phantom Flurry,
//  Apokalypsis); Winged Reprobation (3-stack OriginalHook combo); self-KO (Final Sting,
//  Self-destruct, Transfusion); Revenge Blast (needs a Wild Rage low-HP setup). Pure
//  utility / CC / heals / mitigation / buffs are not damage and are not auto-cast here.
//
//  POTENCIES/ASPECTS = level-80 / 6.x; action IDs verified (upstream consts + Garland).
//  Potencies marked approx affect only ordering between near-equal options.
// =====================================================================================
internal partial class BLU
{
    // Verified IDs not already defined in BLU.cs constants (Garland, job 36)
    public const uint
        WaterCannon    = 11385,
        GoblinPunch    = 34563,
        MountainBuster = 11428,
        Quasar         = 18324,
        BothEnds       = 23287,
        AquaBreath     = 11390,
        HighVoltage    = 11387,
        Glower         = 11404,
        Plaincracker   = 11391,
        DrillCannons   = 11398,
        ThousandNeedles= 11397,
        Stotram        = 23269,
        AetherialSpark = 23281;

    internal enum BluAspect { Physical, Magical, Unaspected }

    internal sealed class BluSpell
    {
        public uint Id;
        public string Name = "";
        public int Potency;
        public int DotPotency;
        public int DotDurationS;
        public ushort DotStatus;
        public bool NoTimer;
        public float CastS;
        public bool IsOgcd;
        public bool IsCharge;
        public bool Melee;
        public BluAspect Aspect = BluAspect.Magical;
        public uint[] WantsBuffs = [];
    }

    private const float OgcdCost = 0.6f;
    private const float GcdCost = 2.5f;
    private const float DotRefresh = 3f;
    private const float DotApplyGrace = 8f;
    private const int BuffWorthPotency = 400;

    // ---- Full level-80 damaging catalog (verified IDs) ----
    internal static readonly List<BluSpell> StCatalog =
    [
        // --- Single-target GCD nukes / filler ---
        new() { Id = SonicBoom,        Name = "Sonic Boom",      Potency = 210, Aspect = BluAspect.Magical },
        new() { Id = WaterCannon,      Name = "Water Cannon",    Potency = 200, CastS = 2f, Aspect = BluAspect.Magical },
        new() { Id = Glower,           Name = "Glower",          Potency = 220, Aspect = BluAspect.Magical },
        new() { Id = MustardBomb,      Name = "Mustard Bomb",    Potency = 220, Aspect = BluAspect.Magical },
        new() { Id = SharpenedKnife,   Name = "Sharpened Knife", Potency = 220, Aspect = BluAspect.Physical, Melee = true },
        new() { Id = GoblinPunch,      Name = "Goblin Punch",    Potency = 220, Aspect = BluAspect.Physical, Melee = true },
        new() { Id = DrillCannons,     Name = "Drill Cannons",   Potency = 200, Aspect = BluAspect.Physical },
        new() { Id = PerpetualRay,     Name = "Perpetual Ray",   Potency = 200, Aspect = BluAspect.Physical, Melee = true },
        new() { Id = WhiteKnightsTour, Name = "White Knight's Tour", Potency = 200, Aspect = BluAspect.Magical },
        new() { Id = BlackKnightsTour, Name = "Black Knight's Tour", Potency = 200, Aspect = BluAspect.Magical },
        new() { Id = MatraMagic,       Name = "Matra Magic",     Potency = 400, Aspect = BluAspect.Magical, WantsBuffs = [Bristle] },
        new() { Id = TripleTrident,    Name = "Triple Trident",  Potency = 600, Aspect = BluAspect.Physical, WantsBuffs = [Whistle, Tingle] },

        // --- AoE GCD nukes (also hit the ST target; great filler / multi-target) ---
        new() { Id = AquaBreath,       Name = "Aqua Breath",     Potency = 140, CastS = 2f, Aspect = BluAspect.Magical },
        new() { Id = HighVoltage,      Name = "High Voltage",    Potency = 200, Aspect = BluAspect.Magical },
        new() { Id = ThousandNeedles,  Name = "1000 Needles",    Potency = 140, CastS = 2f, Aspect = BluAspect.Physical },
        new() { Id = Plaincracker,     Name = "Plaincracker",    Potency = 220, Aspect = BluAspect.Physical },
        new() { Id = Stotram,          Name = "Stotram",         Potency = 220, Aspect = BluAspect.Magical },
        new() { Id = PeripheralSynthesis, Name = "Peripheral Synthesis", Potency = 240, Aspect = BluAspect.Magical },
        new() { Id = RamsVoice,        Name = "The Ram's Voice", Potency = 220, CastS = 2f, Aspect = BluAspect.Magical },

        // --- DoTs (GCD). Unaspected/magical -> Bristle. ---
        new() { Id = SongOfTorment,    Name = "Song of Torment", DotPotency = 50,  DotDurationS = 30, DotStatus = Debuffs.SongOfTorment, CastS = 2f, Aspect = BluAspect.Unaspected, WantsBuffs = [Bristle] },
        new() { Id = BreathOfMagic,    Name = "Breath of Magic", DotPotency = 120, DotDurationS = 60, DotStatus = Debuffs.BreathOfMagic, CastS = 2f, Aspect = BluAspect.Unaspected, WantsBuffs = [Bristle] },
        new() { Id = MortalFlame,      Name = "Mortal Flame",    DotPotency = 40,  DotDurationS = 90, DotStatus = Debuffs.MortalFlame,   NoTimer = true, CastS = 2f, Aspect = BluAspect.Magical, WantsBuffs = [Bristle] },
        new() { Id = AetherialSpark,   Name = "Aetherial Spark", DotPotency = 35,  DotDurationS = 15, DotStatus = 0, Aspect = BluAspect.Magical }, // redundant w/ SoT/Nightbloom; cadence-tracked

        // --- Damage oGCDs (weave; not buffed by Bristle/Whistle). AoE ones still hit ST. ---
        new() { Id = FeatherRain,      Name = "Feather Rain",    Potency = 220, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = Eruption,         Name = "Eruption",        Potency = 290, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = ShockStrike,      Name = "Shock Strike",    Potency = 400, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = MountainBuster,   Name = "Mountain Buster", Potency = 400, IsOgcd = true, Aspect = BluAspect.Physical },
        new() { Id = RoseOfDestruction,Name = "Rose of Destr.",  Potency = 400, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = GlassDance,       Name = "Glass Dance",     Potency = 350, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = JKick,            Name = "J Kick",          Potency = 350, IsOgcd = true, Aspect = BluAspect.Physical },
        new() { Id = Quasar,           Name = "Quasar",          Potency = 300, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = Nightbloom,       Name = "Nightbloom",      Potency = 400, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = BothEnds,         Name = "Both Ends",       Potency = 400, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = SeaShanty,        Name = "Sea Shanty",      Potency = 500, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = BeingMortal,      Name = "Being Mortal",    Potency = 1000,IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = Surpanakha,       Name = "Surpanakha",      Potency = 350, IsOgcd = true, IsCharge = true, Aspect = BluAspect.Physical },
    ];

    internal static ushort BuffStatusOf(uint spellId) => spellId switch
    {
        Bristle => Buffs.Bristle,
        Whistle => Buffs.Whistle,
        Tingle  => Buffs.Tingle,
        _ => 0,
    };

    internal static bool SurpanakhaDumping;

    internal class BLU_ST_AdvancedMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BLU_ST_AdvancedMode;

        protected override uint Invoke(uint actionID)
        {
            if (actionID is not SonicBoom)
                return actionID;

            if (CanWeave())
            {
                uint og = BestWeave();
                if (og != 0)
                    return og;
            }

            uint gcd = BestGcd();
            return gcd != 0 ? gcd : actionID;
        }

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

        private static float Cost(BluSpell s) => s.IsOgcd ? OgcdCost : Math.Max(s.CastS, GcdCost);

        private bool DotIsUp(BluSpell s)
        {
            bool present = s.DotStatus != 0 && HasStatusEffect(s.DotStatus, CurrentTarget, true);
            if (present && (s.NoTimer || GetStatusEffectRemainingTime(s.DotStatus, CurrentTarget, true) > DotRefresh))
                return true;
            float window = s.NoTimer ? 3600f : Math.Max(s.DotDurationS - DotRefresh, DotApplyGrace);
            if (JustUsedOn(s.Id, CurrentTarget, window))
                return true;
            return !present && JustUsedOn(s.Id, CurrentTarget, DotApplyGrace);
        }

        private uint BestWeave()
        {
            uint best = 0;
            double bestVal = 0;
            foreach (var s in StCatalog)
            {
                if (!s.IsOgcd || !IsSpellActive(s.Id))
                    continue;
                if (s.IsCharge)
                {
                    uint charges = GetRemainingCharges(s.Id);
                    if (charges >= GetMaxCharges(s.Id) && charges > 0)
                        SurpanakhaDumping = true;
                    if (charges == 0)
                        SurpanakhaDumping = false;
                    if (!SurpanakhaDumping || charges == 0)
                        continue;
                }
                else if (!IsOffCooldown(s.Id))
                    continue;

                double val = s.Potency * Mult(s) / Cost(s);
                if (val > bestVal) { bestVal = val; best = s.Id; }
            }
            return best;
        }

        private uint BestGcd()
        {
            bool targetAlive = CurrentTarget is not null && !TargetIsDead() && GetTargetHPPercent() > 2f;

            uint dotAct = 0; double dotVal = 0; int dotPot = 0;
            if (targetAlive)
            {
                foreach (var s in StCatalog)
                {
                    if (s.DotDurationS == 0 || !IsSpellActive(s.Id))
                        continue;
                    if (DotIsUp(s))
                        continue;
                    int total = s.DotPotency * (s.DotDurationS / 3);
                    double val = total * Mult(s) / Cost(s);
                    if (val > dotVal) { dotVal = val; dotAct = s.Id; dotPot = total; }
                }
            }

            uint nukeAct = 0; double nukeVal = 0; int nukePot = 0;
            foreach (var s in StCatalog)
            {
                if (s.IsOgcd || s.DotDurationS != 0 || s.Potency == 0 || !IsSpellActive(s.Id))
                    continue;
                if (!IsOffCooldown(s.Id))
                    continue;
                if (s.Melee && !InMeleeRange())
                    continue;
                double eff = s.Potency * Mult(s) + (HasStatusEffect(Buffs.Tingle) ? 100 : 0);
                double val = eff / Cost(s);
                if (val > nukeVal) { nukeVal = val; nukeAct = s.Id; nukePot = s.Potency; }
            }

            uint payload; int payloadPot; uint[] wants;
            if (dotVal >= nukeVal && dotAct != 0)
            { payload = dotAct; payloadPot = dotPot; wants = WantsOf(dotAct); }
            else
            { payload = nukeAct; payloadPot = nukePot; wants = WantsOf(nukeAct); }

            if (payload == 0)
                return 0;

            if (payloadPot >= BuffWorthPotency)
            {
                foreach (uint buffSpell in wants)
                {
                    ushort status = BuffStatusOf(buffSpell);
                    if (status != 0 && IsSpellActive(buffSpell) && IsOffCooldown(buffSpell)
                        && !HasStatusEffect(status) && !JustUsed(buffSpell))
                        return buffSpell;
                }
            }

            return payload;
        }

        private static uint[] WantsOf(uint id)
        {
            foreach (var s in StCatalog)
                if (s.Id == id)
                    return s.WantsBuffs;
            return [];
        }
    }
}
