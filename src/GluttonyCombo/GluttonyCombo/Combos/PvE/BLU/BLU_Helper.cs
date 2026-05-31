using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS;
using GluttonyCombo.Extensions;
using System;
using System.Collections.Generic;

namespace GluttonyCombo.Combos.PvE;

// =====================================================================================
//  BLU AutoRotation — Phase 1.1: greedy single-target DPET priority engine.
//
//  Not a fixed rotation. Every slotted spell in the catalog is scored live by damage-
//  per-execution-time (potency x current-buff-multiplier / time it costs: oGCD ~0.6s
//  weave lock, GCD 2.5s). Best damage oGCD weaves when CanWeave(); DoTs are (re)applied
//  only when absent/about-to-fall (no clipping) and never within JustUsed() of the last
//  cast (prevents the application-delay double-cast, e.g. Mortal Flame). Surpanakha is
//  dumped as a full 4-charge bundle. Bristle/Whistle/Tingle are only spent in front of a
//  payload big enough (>= BuffWorthPotency) to be worth the GCD. Sonic Boom is the
//  guaranteed filler + the combo anchor.
//
//  POTENCIES = level-80 / 6.x set, hardcoded (BLU is level-locked). Values marked approx
//  are theory-sourced and only affect ordering between near-equal options; verify in-game
//  before trusting exact ordering. Action IDs are the verified upstream BLU constants
//  (see BLU.cs). Spells the player has slotted that are NOT in this catalog are not yet
//  driven by the engine (later phases broaden coverage).
//
//  Scope: single-target, greedy. Moon Flute burst, AoE, heals, tank/mit = later phases.
// =====================================================================================
internal partial class BLU
{
    internal enum BluAspect { Physical, Magical, Unaspected }

    internal sealed class BluSpell
    {
        public uint Id;
        public string Name = "";
        public int Potency;          // direct-hit potency (0 for pure DoTs)
        public int DotPotency;       // per-tick potency (0 if not a DoT)
        public int DotDurationS;     // DoT duration seconds (0 if not a DoT)
        public ushort DotStatus;     // debuff id for uptime check (0 if none)
        public float CastS;          // 0 = instant
        public bool IsOgcd;          // weaves; costs anim-lock not a GCD
        public bool IsCharge;        // charge-dump ability (Surpanakha)
        public bool Melee;           // requires melee range
        public BluAspect Aspect = BluAspect.Magical;
        public uint[] WantsBuffs = []; // setup spell ids to apply first, in order
    }

    private const float OgcdCost = 0.6f;
    private const float GcdCost = 2.5f;
    private const float DotRefresh = 3f;       // re-apply DoT only when <= this remains
    private const int BuffWorthPotency = 400;  // only buff payloads at/above this value

    // ---- Verified level-80 single-target catalog (IDs from upstream BLU constants) ----
    internal static readonly List<BluSpell> StCatalog =
    [
        // Direct GCD nukes / filler (buffable by Bristle/Whistle)
        new() { Id = SonicBoom,        Name = "Sonic Boom",       Potency = 210, Aspect = BluAspect.Magical },
        new() { Id = SharpenedKnife,   Name = "Sharpened Knife",  Potency = 220, Aspect = BluAspect.Physical, Melee = true },
        new() { Id = WingedReprobation, Name = "Winged Reprob.",  Potency = 300, Aspect = BluAspect.Magical },
        new() { Id = MatraMagic,       Name = "Matra Magic",      Potency = 400, Aspect = BluAspect.Magical, WantsBuffs = [Bristle] },
        new() { Id = TripleTrident,    Name = "Triple Trident",   Potency = 600, Aspect = BluAspect.Physical, WantsBuffs = [Whistle, Tingle] },

        // DoTs (GCD)
        new() { Id = SongOfTorment,    Name = "Song of Torment",  DotPotency = 50,  DotDurationS = 30, DotStatus = Debuffs.SongOfTorment, Aspect = BluAspect.Unaspected, WantsBuffs = [Bristle] },
        new() { Id = BreathOfMagic,    Name = "Breath of Magic",  DotPotency = 120, DotDurationS = 60, DotStatus = Debuffs.BreathOfMagic, Aspect = BluAspect.Magical, WantsBuffs = [Bristle] }, // tick approx
        new() { Id = MortalFlame,      Name = "Mortal Flame",     DotPotency = 40,  DotDurationS = 90, DotStatus = Debuffs.MortalFlame,   Aspect = BluAspect.Physical }, // "permanent"; modeled long

        // Damage oGCDs (weave; not buffed by Bristle/Whistle)
        new() { Id = FeatherRain,      Name = "Feather Rain",     Potency = 220, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = Eruption,         Name = "Eruption",         Potency = 290, IsOgcd = true, Aspect = BluAspect.Magical }, // shares CD w/ Feather Rain (game-enforced)
        new() { Id = ShockStrike,      Name = "Shock Strike",     Potency = 400, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = RoseOfDestruction,Name = "Rose of Destr.",   Potency = 400, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = GlassDance,       Name = "Glass Dance",      Potency = 350, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = JKick,            Name = "J Kick",           Potency = 350, IsOgcd = true, Aspect = BluAspect.Physical },
        new() { Id = Nightbloom,       Name = "Nightbloom",       Potency = 400, IsOgcd = true, Aspect = BluAspect.Magical }, // also strong DoT
        new() { Id = SeaShanty,        Name = "Sea Shanty",       Potency = 500, IsOgcd = true, Aspect = BluAspect.Magical }, // weather-boosted higher
        new() { Id = BeingMortal,      Name = "Being Mortal",     Potency = 1000,IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = Surpanakha,       Name = "Surpanakha",       Potency = 350, IsOgcd = true, IsCharge = true, Aspect = BluAspect.Physical }, // 200/300/400/500 dump avg
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
                if (s.Aspect == BluAspect.Magical && HasStatusEffect(Buffs.Bristle))
                    m *= 1.5;
                if (s.Aspect == BluAspect.Physical && HasStatusEffect(Buffs.Whistle))
                    m *= 1.5;
            }
            return m;
        }

        private static float Cost(BluSpell s) => s.IsOgcd ? OgcdCost : Math.Max(s.CastS, GcdCost);

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
                if (val > bestVal)
                {
                    bestVal = val;
                    best = s.Id;
                }
            }
            return best;
        }

        private uint BestGcd()
        {
            bool targetAlive = CurrentTarget is not null && !TargetIsDead() && GetTargetHPPercent() > 2f;

            // 1) DoT lane: (re)apply only when absent/about-to-fall, target will live, and
            //    not within JustUsed() of the last cast (kills the application-delay double).
            uint dotAct = 0; double dotVal = 0; int dotPot = 0;
            if (targetAlive)
            {
                foreach (var s in StCatalog)
                {
                    if (s.DotDurationS == 0 || !IsSpellActive(s.Id))
                        continue;
                    if (JustUsed(s.Id))
                        continue;
                    float rem = HasStatusEffect(s.DotStatus, CurrentTarget, true)
                        ? GetStatusEffectRemainingTime(s.DotStatus, CurrentTarget, true) : 0f;
                    if (rem > DotRefresh)
                        continue;
                    int total = s.DotPotency * (s.DotDurationS / 3);
                    double val = total * Mult(s) / Cost(s);
                    if (val > dotVal) { dotVal = val; dotAct = s.Id; dotPot = total; }
                }
            }

            // 2) Direct nuke lane (range-gated so a melee pick can't stall us at range)
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

            // 3) Worth-it buff setup in front of a big-enough payload only.
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
