using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS;
using GluttonyCombo.Extensions;
using System;
using System.Collections.Generic;

namespace GluttonyCombo.Combos.PvE;

// BLU AutoRotation — Phase 1.5: greedy DPET priority engine over the full damaging kit.
// See CHANGELOG. Key behaviors: GCD spells are NOT gated on the global-GCD cooldown (only
// real cooldowns gate); oGCDs weave on CanWeave(); DoTs use per-action JustUsed cadence;
// Surpanakha fires on any available charge; channels (Phantom Flurry/Apokalypsis) are cast
// when stationary+in-range then held with All.SavageBlade (a no-op) so they aren't cancelled;
// Winged Reprobation runs its 4-stack -> Conviction Marcato chain via OriginalHook.
internal partial class BLU
{
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
        AetherialSpark = 23281,
        Apokalypsis    = 34581,
        RevengeBlast   = 18316;

    private const ushort WingedRedemptionStatus = 3641;
    private const ushort ApokalypsisStatus = 3644;

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
        public bool FillerOnly;
        public BluAspect Aspect = BluAspect.Magical;
        public uint[] WantsBuffs = [];
    }

    private const float OgcdCost = 0.6f;
    private const float GcdCost = 2.5f;
    private const float DotRefresh = 3f;
    private const float DotApplyGrace = 8f;
    private const int BuffWorthPotency = 400;

    internal static readonly List<BluSpell> StCatalog =
    [
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
        new() { Id = RevengeBlast,     Name = "Revenge Blast",   Potency = 50,  Aspect = BluAspect.Physical, Melee = true },
        new() { Id = MatraMagic,       Name = "Matra Magic",     Potency = 400, Aspect = BluAspect.Magical, WantsBuffs = [Bristle] },
        new() { Id = TripleTrident,    Name = "Triple Trident",  Potency = 600, Aspect = BluAspect.Physical, WantsBuffs = [Whistle, Tingle] },

        new() { Id = AquaBreath,       Name = "Aqua Breath",     Potency = 140, CastS = 2f, Aspect = BluAspect.Magical },
        new() { Id = HighVoltage,      Name = "High Voltage",    Potency = 200, Aspect = BluAspect.Magical },
        new() { Id = ThousandNeedles,  Name = "1000 Needles",    Potency = 140, CastS = 2f, Aspect = BluAspect.Physical },
        new() { Id = Plaincracker,     Name = "Plaincracker",    Potency = 220, Aspect = BluAspect.Physical },
        new() { Id = Stotram,          Name = "Stotram",         Potency = 220, Aspect = BluAspect.Magical },
        new() { Id = PeripheralSynthesis, Name = "Peripheral Synthesis", Potency = 240, Aspect = BluAspect.Magical },
        new() { Id = RamsVoice,        Name = "The Ram's Voice", Potency = 220, CastS = 2f, Aspect = BluAspect.Magical, FillerOnly = true },

        new() { Id = SongOfTorment,    Name = "Song of Torment", DotPotency = 50,  DotDurationS = 30, DotStatus = Debuffs.SongOfTorment, CastS = 2f, Aspect = BluAspect.Unaspected, WantsBuffs = [Bristle] },
        new() { Id = BreathOfMagic,    Name = "Breath of Magic", DotPotency = 120, DotDurationS = 60, DotStatus = Debuffs.BreathOfMagic, CastS = 2f, Aspect = BluAspect.Unaspected, WantsBuffs = [Bristle] },
        new() { Id = MortalFlame,      Name = "Mortal Flame",    DotPotency = 40,  DotDurationS = 90, DotStatus = Debuffs.MortalFlame,   NoTimer = true, CastS = 2f, Aspect = BluAspect.Magical, WantsBuffs = [Bristle] },
        new() { Id = AetherialSpark,   Name = "Aetherial Spark", DotPotency = 35,  DotDurationS = 15, DotStatus = 0, Aspect = BluAspect.Magical },

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

    internal class BLU_ST_AdvancedMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BLU_ST_AdvancedMode;

        internal static bool DbgCanWeave;
        internal static uint DbgWeavePick;
        internal static uint DbgGcdPick;

        internal static List<string> BluDebugRows()
        {
            var rows = new List<string>();
            foreach (var s in StCatalog)
            {
                bool active = IsSpellActive(s.Id);
                var cd = GetCooldown(s.Id);
                bool ready = s.IsOgcd ? !cd.IsCooldown : (!cd.IsCooldown || cd.CooldownTotal <= 3f);
                rows.Add($"{(active ? "*" : " ")}{(ready ? "R" : "-")}{(s.IsOgcd ? "o" : "g")} {s.Name}  rem={cd.CooldownRemaining:0.0} chg={cd.RemainingCharges}/{cd.MaxCharges}");
            }
            return rows;
        }

        protected override uint Invoke(uint actionID)
        {
            if (actionID is not SonicBoom)
                return actionID;

            // Channel hold: never cancel an active channel (any other action/movement ends it).
            if (HasStatusEffect(Buffs.PhantomFlurry) || JustUsed(PhantomFlurry, 2f))
            {
                var pf = GetStatusEffect(Buffs.PhantomFlurry);
                if (IsSpellActive(PhantomFlurry) && pf is not null && pf.RemainingTime is > 0 and <= 1.2f)
                    return OriginalHook(PhantomFlurry);
                return All.SavageBlade;
            }
            if (HasStatusEffect(ApokalypsisStatus) || JustUsed(Apokalypsis, 2f))
                return All.SavageBlade;

            // Start a channel on cooldown when stationary and in range.
            if (!IsMoving())
            {
                if (IsSpellActive(PhantomFlurry) && IsOffCooldown(PhantomFlurry) && InActionRange(PhantomFlurry))
                    return PhantomFlurry;
                if (IsSpellActive(Apokalypsis) && IsOffCooldown(Apokalypsis) && !IsSpellActive(BeingMortal) && InActionRange(Apokalypsis))
                    return Apokalypsis;
            }

            DbgCanWeave = CanWeave();
            if (DbgCanWeave)
            {
                uint og = BestWeave();
                DbgWeavePick = og;
                if (og != 0)
                    return og;
            }

            uint gcd = BestGcd();
            DbgGcdPick = gcd;
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

        // GCD spells are only "not ready" if they have a REAL cooldown (> the global GCD).
        // Gating plain GCDs on IsOffCooldown wrongly excludes them while the 2.5s GCD rolls.
        private static bool ReadyGcd(uint id)
        {
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
                    if (GetRemainingCharges(s.Id) == 0)
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

            // Winged Reprobation chain (finish once started / when Conviction Marcato is ready).
            if (targetAlive && IsSpellActive(WingedReprobation))
            {
                uint wr = OriginalHook(WingedReprobation);
                int stacks = GetStatusEffect(Buffs.WingedReprobation)?.Param ?? 0;
                if ((HasStatusEffect(WingedRedemptionStatus) || (stacks >= 1 && stacks <= 3)) && IsOffCooldown(wr))
                    return wr;
            }

            // DoT lane
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

            // Direct nuke lane (FillerOnly only if nothing else)
            uint nukeAct = 0; double nukeVal = 0; int nukePot = 0;
            uint fillAct = 0; double fillVal = 0; int fillPot = 0;
            foreach (var s in StCatalog)
            {
                if (s.IsOgcd || s.DotDurationS != 0 || s.Potency == 0 || !IsSpellActive(s.Id))
                    continue;
                if (!ReadyGcd(s.Id))
                    continue;
                if (s.Melee && !InMeleeRange())
                    continue;
                int basePot = s.Id == RevengeBlast ? (PlayerHealthPercentageHp() < 20f ? 500 : 50) : s.Potency;
                double eff = basePot * Mult(s) + (HasStatusEffect(Buffs.Tingle) ? 100 : 0);
                double val = eff / Cost(s);
                if (s.FillerOnly)
                { if (val > fillVal) { fillVal = val; fillAct = s.Id; fillPot = basePot; } }
                else
                { if (val > nukeVal) { nukeVal = val; nukeAct = s.Id; nukePot = basePot; } }
            }
            if (nukeAct == 0 && fillAct != 0)
            { nukeAct = fillAct; nukeVal = fillVal; nukePot = fillPot; }

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
                    if (status != 0 && IsSpellActive(buffSpell) && ReadyGcd(buffSpell)
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
