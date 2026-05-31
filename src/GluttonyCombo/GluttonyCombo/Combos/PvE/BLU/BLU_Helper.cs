using GluttonyCombo.Combos.PvE;
using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS;
using GluttonyCombo.Extensions;
using System;
using System.Collections.Generic;

namespace GluttonyCombo.Combos.PvE;

// =====================================================================================
//  BLU AutoRotation — Phase 1: greedy single-target DPET priority engine.
//
//  This is NOT a fixed rotation. Every slotted spell is scored live by damage-per-
//  execution-time (potency x current-buff-multiplier / time it costs), where an oGCD
//  costs only its ~0.6s weave lock and a GCD costs a full 2.5s. Buffs (Bristle/Whistle/
//  Tingle) are only applied in front of a payload big enough that the +50% beats the
//  GCD they cost ("worth-it" gate) — you do NOT Bristle before filler. DoTs are scored
//  on total-over-duration and only (re)applied when absent/about-to-fall (no clipping).
//  Surpanakha is dumped as a 4-charge bundle, never charge-by-charge.
//
//  POTENCIES ARE THE LEVEL-80 / 6.x SET, HARDCODED AS A STATIC ASSUMPTION (BLU potencies
//  are level-locked and stable — that is the point of the job). Numbers marked "approx"
//  are from established BLU theory and should get one in-game verification pass before
//  they are trusted as exact DPET inputs; the engine's behavior is correct regardless,
//  only the fine ordering between near-equal options depends on the exact values.
//
//  Phase 1 scope: single-target, greedy (fire on cooldown). Moon Flute burst window,
//  AoE, heals, tank/mitigation, and the full per-ability config UI are later phases.
// =====================================================================================
internal partial class BLU
{
    // Anchor the auto-combo replaces (Water Cannon, the lv1 always-learnable nuke).
    public const uint WaterCannon = 11385;

    internal enum BluAspect { Physical, Magical, Unaspected }

    internal sealed class BluSpell
    {
        public uint Id;
        public string Name = "";
        public int Potency;          // direct-hit potency (0 for pure buffs / pure DoTs)
        public int DotPotency;       // per-tick potency (0 if not a DoT)
        public int DotDurationS;     // DoT duration in seconds (0 if not a DoT)
        public ushort DotStatus;     // debuff id used to read uptime (0 if none)
        public float CastS;          // 0 = instant
        public bool IsOgcd;          // true = weaves; costs anim-lock not a GCD
        public bool IsCharge;        // true = charge-dump ability (Surpanakha)
        public BluAspect Aspect = BluAspect.Magical;
        public ushort GrantsBuff;    // self-buff status this spell applies (0 if none)
        public uint[] WantsBuffs = []; // setup spells (ids) to apply first, in order
    }

    private const float OgcdCost = 0.6f;   // animation lock
    private const float GcdCost = 2.5f;    // base GCD
    private const float DotRefresh = 3f;   // re-apply a DoT only when <= this remains
    private const int BuffWorthPotency = 400; // only buff payloads at/above this value

    // ---- Static level-80 catalog (single-target relevant subset for P1) ----------------
    internal static readonly List<BluSpell> StCatalog =
    [
        // Direct nukes / filler (GCD spells, buffable by Bristle/Whistle)
        new() { Id = WaterCannon,      Name = "Water Cannon",   Potency = 200, CastS = 1.5f, Aspect = BluAspect.Magical },
        new() { Id = SonicBoom,        Name = "Sonic Boom",     Potency = 210, Aspect = BluAspect.Magical },
        new() { Id = SharpenedKnife,   Name = "Sharpened Knife",Potency = 220, Aspect = BluAspect.Physical },
        new() { Id = MatraMagic,       Name = "Matra Magic",    Potency = 400, Aspect = BluAspect.Magical, WantsBuffs = [Bristle] },
        new() { Id = TripleTrident,    Name = "Triple Trident", Potency = 600, Aspect = BluAspect.Physical, WantsBuffs = [Whistle, Tingle] },

        // DoTs (GCD spells)
        new() { Id = SongOfTorment,    Name = "Song of Torment",DotPotency = 50,  DotDurationS = 30, DotStatus = Debuffs.SongOfTorment,  Aspect = BluAspect.Unaspected, WantsBuffs = [Bristle] },
        new() { Id = BreathOfMagic,    Name = "Breath of Magic",DotPotency = 120, DotDurationS = 60, DotStatus = Debuffs.BreathOfMagic,  Aspect = BluAspect.Magical, WantsBuffs = [Bristle] }, // tick potency approx
        new() { Id = MortalFlame,      Name = "Mortal Flame",   DotPotency = 40,  DotDurationS = 90, DotStatus = Debuffs.MortalFlame,    Aspect = BluAspect.Physical }, // "permanent"; modeled as long DoT

        // Damage oGCDs (weave; not buffed by Bristle/Whistle)
        new() { Id = FeatherRain,      Name = "Feather Rain",   Potency = 220, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = ShockStrike,      Name = "Shock Strike",   Potency = 400, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = RoseOfDestruction,Name = "Rose of Destr.", Potency = 400, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = GlassDance,       Name = "Glass Dance",    Potency = 350, IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = JKick,            Name = "J Kick",         Potency = 350, IsOgcd = true, Aspect = BluAspect.Physical },
        new() { Id = Nightbloom,       Name = "Nightbloom",     Potency = 400, IsOgcd = true, Aspect = BluAspect.Magical }, // initial; carries a strong DoT too
        new() { Id = BeingMortal,      Name = "Being Mortal",   Potency = 1000,IsOgcd = true, Aspect = BluAspect.Magical },
        new() { Id = Surpanakha,       Name = "Surpanakha",     Potency = 350, IsOgcd = true, IsCharge = true, Aspect = BluAspect.Physical }, // avg of 200/300/400/500 dump
    ];

    // Map a setup spell to the self-buff status it grants (for "is it already up?" checks).
    internal static ushort BuffStatusOf(uint spellId) => spellId switch
    {
        Bristle => Buffs.Bristle,
        Whistle => Buffs.Whistle,
        Tingle  => Buffs.Tingle,
        _ => 0,
    };

    // Latch so Surpanakha is dumped all-charges-at-once rather than bled one at a time.
    internal static bool SurpanakhaDumping;

    internal class BLU_ST_AdvancedMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BLU_ST_AdvancedMode;

        protected override uint Invoke(uint actionID)
        {
            if (actionID is not WaterCannon)
                return actionID;

            // Weave window: best damage oGCD (near-free in time → wins over GCD fillers).
            if (CanWeave())
            {
                uint og = BestWeave();
                if (og != 0)
                    return og;
            }

            uint gcd = BestGcd();
            return gcd != 0 ? gcd : actionID;
        }

        // --- buff multiplier on a candidate, from CURRENT status (never pre-counts a buff
        //     we have not actually applied yet) ---
        private static double Mult(BluSpell s)
        {
            double m = 1.0;
            if (HasStatusEffect(Buffs.MoonFlute))
                m *= 1.5;                                   // Waxing Nocturne: all damage
            if (HasStatusEffect(Debuffs.Offguard, CurrentTarget, true))
                m *= 1.05;                                  // target damage-taken up
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
                    // Dump the whole charge stack at once; do not start unless full.
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
            // 1) DoT lane: only (re)apply a DoT that is absent or about to fall, and only
            //    if the target will live long enough to be worth it.
            uint dotAct = 0;
            double dotVal = 0;
            int dotPayloadPot = 0;
            bool targetAlive = CurrentTarget is not null && !TargetIsDead() && GetTargetHPPercent() > 2f;
            if (targetAlive)
            {
                foreach (var s in StCatalog)
                {
                    if (s.DotDurationS == 0 || !IsSpellActive(s.Id))
                        continue;
                    float rem = HasStatusEffect(s.DotStatus, CurrentTarget, true)
                        ? GetStatusEffectRemainingTime(s.DotStatus, CurrentTarget, true) : 0f;
                    if (rem > DotRefresh)
                        continue;                           // healthy DoT: do not clip
                    int ticks = s.DotDurationS / 3;
                    int total = s.DotPotency * ticks;
                    double val = total * Mult(s) / Cost(s);
                    if (val > dotVal)
                    {
                        dotVal = val;
                        dotAct = s.Id;
                        dotPayloadPot = total;
                    }
                }
            }

            // 2) Direct-nuke lane
            uint nukeAct = 0;
            double nukeVal = 0;
            int nukePot = 0;
            foreach (var s in StCatalog)
            {
                if (s.IsOgcd || s.DotDurationS != 0 || s.Potency == 0 || !IsSpellActive(s.Id))
                    continue;
                if (!IsOffCooldown(s.Id))
                    continue;
                double eff = s.Potency * Mult(s) + (HasStatusEffect(Buffs.Tingle) ? 100 : 0);
                double val = eff / Cost(s);
                if (val > nukeVal)
                {
                    nukeVal = val;
                    nukeAct = s.Id;
                    nukePot = s.Potency;
                }
            }

            // 3) Pick the higher-value payload between DoT upkeep and direct nuke.
            uint payload;
            int payloadPot;
            uint[] wants;
            if (dotVal >= nukeVal && dotAct != 0)
            {
                payload = dotAct;
                payloadPot = dotPayloadPot;
                wants = WantsOf(dotAct);
            }
            else
            {
                payload = nukeAct;
                payloadPot = nukePot;
                wants = WantsOf(nukeAct);
            }

            if (payload == 0)
                return 0;

            // 4) Worth-it buff setup: only in front of a big-enough payload, only if the
            //    buff is slotted, ready, and not already up. Returns the setup spell first.
            if (payloadPot >= BuffWorthPotency)
            {
                foreach (uint buffSpell in wants)
                {
                    ushort status = BuffStatusOf(buffSpell);
                    if (status != 0 && IsSpellActive(buffSpell) && IsOffCooldown(buffSpell)
                        && !HasStatusEffect(status))
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
