#region
using Dalamud.Game.ClientState.Objects.Types;
using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS;
using GluttonyCombo.Data.Conflicts;
using GluttonyCombo.Extensions;
using GluttonyCombo.Services;
using System.Collections.Generic;

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming
#endregion

namespace GluttonyCombo.Combos.PvE;

// Mimic-aware BLU auto-rotation. Self-contained in this file (plus BLU_Config.cs) to stay friendly
// to the nightly upstream merge — BLU.cs is left untouched. The engine routes the DPS-tagged preset
// through AutomateDPS -> ExecuteST -> this combo's Invoke; heal/tank lanes live INSIDE this DPS
// Invoke because the engine's heal/tank automation is hard-gated to CombatRole.Healer/Tank and BLU
// is magical-ranged DPS (locked decision #1, 2026-06-18).
internal partial class BLU
{
    #region BLU auto-rotation action IDs (not already declared in BLU.cs)
    public const uint
        MountainBuster   = 11428,
        Quasar           = 18324,
        BothEnds         = 23287,
        Apokalypsis      = 34581,
        ConvictionMarcato= 34574,
        RubyDynamics     = 34571,
        CandyCane        = 34578,
        GoblinPunch      = 34563,
        MightyGuard      = 11417,
        PeculiarLight    = 11421,
        ChelonianGate    = 23273,
        TheLook          = 11399,
        FrogLegs         = 18307,
        StickyTongue     = 11412,
        Schiltron        = 34565,
        DragonForce      = 23280,
        Cactguard        = 18315,
        PomCure          = 18303,
        Stotram          = 23269,
        Exuviation       = 18318,
        AngelsSnack      = 23272,
        Gobskin          = 18304,
        Rehydration      = 34566,
        ColdFog          = 23267,
        ForceField       = 34575,
        Diamondback      = 11424,
        VeilOfTheWhorl   = 11431,
        Avail            = 18306,
        ToadOil          = 11410,
        CondensedLibra   = 18321;
    #endregion

    #region BLU auto-rotation status IDs (not already declared in BLU.Buffs)
    public const ushort
        HealerMimicry   = 2126,
        MightyGuardBuff = 1719,
        SurpanakhasFury = 2130,
        WingedRedemption= 3641,
        TouchOfFrost    = 2494;
    #endregion

    private enum MimicState { None, DPS, Tank, Healer }

    internal class BLU_AutoRotation_DPS : CustomCombo
    {
        protected internal override Preset Preset => Preset.BLU_AutoRotation_DPS;

        // BMR distance is only pushed when the mimic actually changes (throttle).
        private static int _lastBmrMimic = -99;
        private static bool _surpReady;

        // Spells the generic filler must NEVER auto-cast: buffs/enablers, heals/raise,
        // mitigation/defensive (Diamondback immobilises, etc.), stances/threat/draw, suicides &
        // self-damage, knockbacks/movement/channel-locks, hard CC (sleep/stun/dispel/bind),
        // instant-KO & %HP gimmicks, plus the cooldown-managed damage and DoTs the cascade above
        // already handles. EVERYTHING ELSE that is slotted, off cooldown and in range is fair game,
        // so any damage spell you slot is used automatically.
        private static readonly HashSet<uint> FillerExcluded = new()
        {
            // buffs / enablers
            11415, 18309, 11393, 23265, 11411, 11421, 18322, 18321, 11410, 23276,
            // heals / raise
            11406, 18303, 23269, 23416, 18318, 23272, 18304, 34566, 18317,
            // mitigation / defensive
            23267, 11424, 34575, 23280, 11431, 11418, 18306, 18315, 34565,
            // tank stance / threat / draw / self-heal CD
            11417, 11399, 18307, 11412, 18320,
            // suicides / self-damage
            11407, 11408, 11409, 34568,
            // knockback / draw / movement / channel-lock
            11383, 18296, 23282, 11401, 11402,
            // hard CC: sleep / stun / dispel / bind / interrupt / debuff-bomb / MP drain
            11392, 18301, 11394, 11403, 11396, 23266, 18300, 18314, 18302, 18319, 11423, 11388, 11395,
            // instant-KO / %HP gimmicks (no effect on bosses)
            11414, 18312, 11416, 23277, 11397, 11405, 11413, 18313, 34573,
            // cooldown-managed damage handled by the cascade above
            23275, 34571, 23264, 34582, 23287, 23290, 34580, 23285, 34581, 23288, 11430, 18323,
            11426, 11427, 11429, 11428, 18324, 18325, 34576, 34574, 18305, 34578,
            // DoTs (handled above / not spammable)
            11386, 34567, 34579, 23281,
            // niche conditional (Revenge Blast = 50 potency at full HP)
            18316,
        };

        protected override uint Invoke(uint actionID)
        {
            if (actionID is not SonicBoom)
                return actionID;

            var mimic = CurrentMimic();
            UpdateBmrDistance(mimic);

            // Heal lane (only if the user enabled the BLU heal preset). May return a heal action.
            if (IsEnabled(Preset.BLU_AutoRotation_Heal))
            {
                uint heal = HealLane(mimic);
                if (heal != 0)
                    return heal;
            }

            // Damage lane by mimic. Tank lane under Tank mimic; everyone else uses the DPS lane.
            uint dmg = mimic == MimicState.Tank ? TankLane() : DpsLane(mimic);
            if (dmg != 0)
                return dmg;

            // Nothing better — fall through to Sonic Boom (the replaced button).
            return actionID;
        }

        #region Mimic + BMR distance

        private static MimicState CurrentMimic()
        {
            if (HasStatusEffect(Buffs.TankMimicry)) return MimicState.Tank;
            if (HasStatusEffect(HealerMimicry))     return MimicState.Healer;
            if (HasStatusEffect(Buffs.DPSMimicry))   return MimicState.DPS;
            return MimicState.None;
        }

        private static void UpdateBmrDistance(MimicState mimic)
        {
            if ((int)mimic == _lastBmrMimic)
                return;
            _lastBmrMimic = (int)mimic;

            float d = mimic switch
            {
                MimicState.Tank   => Config.BLU_BMR_Distance_Tank,
                MimicState.Healer => Config.BLU_BMR_Distance_Healer,
                _                 => Config.BLU_BMR_Distance_DPS,
            };
            // The setter is null/throw-safe internally (reflects into BMR's live AI config).
            ConflictingPluginsChecks.BossModReborn.SetMaxDistanceToTarget(d);
        }

        #endregion

        #region Shared helpers

        private static IGameObject? EnemyTarget() =>
            SimpleTarget.HardTarget.IfHostile() ?? SimpleTarget.LastHostileHardTarget;

        // True when the next planned Moon Flute burst is close enough that we should hold charges.
        private static bool BurstSoon() =>
            IsSpellActive(MoonFlute) && Config.BLU_Use_MoonFlute &&
            GetCooldownRemainingTime(MoonFlute) <= Config.BLU_Surpanakha_HoldForBurstSec;

        // Pre-raidwide defensives that apply in any lane (Cold Fog -> White Death window).
        private static uint DefensiveStep()
        {
            if (Config.BLU_Use_ColdFog && IsSpellActive(ColdFog))
            {
                if (HasStatusEffect(TouchOfFrost))
                    return OriginalHook(ColdFog); // White Death (400, instant)
                if (IsOffCooldown(ColdFog) && GroupDamageIncoming(Config.BLU_ColdFog_LeadSeconds))
                    return ColdFog;
            }
            return 0;
        }

        #endregion

        #region Heal lane

        private static uint HealLane(MimicState mimic)
        {
            float party = GetPartyAvgHPPercent();
            float self  = PlayerHealthPercentageHp();
            int partyGate = mimic == MimicState.Healer
                ? Config.BLU_Heal_PartyHPPercent
                : Config.BLU_Heal_EmergencyHPPercent;

            // Prophylactic barrier ahead of a known raidwide.
            if (Config.BLU_Use_Gobskin && IsSpellActive(Gobskin) &&
                party <= Config.BLU_ProphylacticMit_HPPercent &&
                GroupDamageIncoming(Config.BLU_ProphylacticMit_LeadSeconds))
                return Gobskin;

            // Emergency single target -> Pom Cure, retargeted onto the hurt ally.
            if (Config.BLU_Use_PomCure && IsSpellActive(PomCure) &&
                party <= Config.BLU_Heal_PomCureHPPercent &&
                (mimic == MimicState.Healer || self <= Config.BLU_Heal_EmergencyHPPercent))
                return PomCure.Retarget(SonicBoom, SimpleTarget.Stack.AllyToHeal);

            // Party-wide top-up (self-centered PBAoE heals — no targeting needed).
            if (party <= partyGate)
            {
                if (Config.BLU_Use_AngelsSnack && IsSpellActive(AngelsSnack) &&
                    IsOffCooldown(AngelsSnack) && mimic == MimicState.Healer)
                    return AngelsSnack;
                if (Config.BLU_Use_WhiteWind && IsSpellActive(WhiteWind) && self >= 50)
                    return WhiteWind;
                if (Config.BLU_Use_Stotram && IsSpellActive(Stotram))
                    return Stotram;
            }

            // Cleanse + heal under Healer mimic when the party is hurt.
            if (mimic == MimicState.Healer && Config.BLU_Use_Exuviation && IsSpellActive(Exuviation) &&
                party <= Config.BLU_Heal_PartyHPPercent)
                return Exuviation;

            // Self emergency.
            if (self <= Config.BLU_Heal_EmergencyHPPercent &&
                Config.BLU_Use_Rehydration && IsSpellActive(Rehydration))
                return Rehydration;

            return 0;
        }

        #endregion

        #region DPS lane

        private static bool BurstWanted() =>
            HasStatusEffect(Buffs.MoonFlute) ||
            (IsSpellActive(MoonFlute) && Config.BLU_Use_MoonFlute &&
             IsOffCooldown(MoonFlute) && !HasStatusEffect(Buffs.WaningNocturne) &&
             ((IsSpellActive(BeingMortal) && IsOffCooldown(BeingMortal)) ||
              (IsSpellActive(Nightbloom) && IsOffCooldown(Nightbloom))));

        private static uint DpsLane(MimicState mimic)
        {
            // Waning Nocturne = total lockout.
            if (HasStatusEffect(Buffs.WaningNocturne))
                return 0;

            // Reactive defensives (Cold Fog / White Death).
            uint def = DefensiveStep();
            if (def != 0)
                return def;

            // Reactive: Final Sting kill-range (suicide — toggle defaults off, slider defaults 1%).
            if (Config.BLU_Use_FinalSting && IsSpellActive(FinalSting) && CurrentTarget is not null &&
                GetTargetHPPercent() <= Config.BLU_FinalSting_BossHPPercent)
            {
                if (Config.BLU_Use_Whistle && IsSpellActive(Whistle) && !HasStatusEffect(Buffs.Whistle))
                    return Whistle;
                return FinalSting;
            }

            // Moon Flute burst window (scripted sub-sequence).
            if (BurstWanted())
            {
                uint b = BurstStep();
                if (b != 0)
                    return b;
            }

            var enemy = EnemyTarget();

            // --- Filler cascade (first castable wins) ---

            // DoT refresh — snapshot Bristle first, and guard against re-applying during the
            // status-application delay (fixes double Mortal Flame).
            bool needBreath = Config.BLU_Use_BreathOfMagic && IsSpellActive(BreathOfMagic) &&
                              !HasStatusEffect(Debuffs.BreathOfMagic, CurrentTarget, true) && !JustUsed(BreathOfMagic);
            bool needMortal = Config.BLU_Use_MortalFlame && IsSpellActive(MortalFlame) &&
                              !HasStatusEffect(Debuffs.MortalFlame, CurrentTarget, true) && !JustUsed(MortalFlame);
            bool needSoT    = Config.BLU_Use_SongOfTorment && IsSpellActive(SongOfTorment) &&
                              !HasStatusEffect(Debuffs.SongOfTorment, CurrentTarget, true) && !JustUsed(SongOfTorment);
            if (needBreath || needMortal || needSoT)
            {
                if (Config.BLU_Use_Bristle && IsSpellActive(Bristle) && !HasStatusEffect(Buffs.Bristle))
                    return Bristle;
                if (needBreath) return BreathOfMagic;
                if (needMortal) return MortalFlame;
                if (needSoT)    return SongOfTorment;
            }

            // Rose of Destruction the instant it is up.
            if (Config.BLU_Use_RoseOfDestruction && IsSpellActive(RoseOfDestruction) && IsOffCooldown(RoseOfDestruction))
                return RoseOfDestruction;

            // Winged Reprobation — spend all charges; OriginalHook resolves the Conviction Marcato
            // payoff at 3 stacks so the combo keeps going instead of stalling at 2.
            if (Config.BLU_Use_WingedReprobation && IsSpellActive(WingedReprobation) && IsOffCooldown(WingedReprobation))
                return OriginalHook(WingedReprobation);

            // Conviction Marcato payoff while Winged Redemption is up.
            if (Config.BLU_Use_ConvictionMarcato && IsSpellActive(ConvictionMarcato) && HasStatusEffect(WingedRedemption))
                return ConvictionMarcato;

            // Surpanakha — once charges cap at 4, dump all 4 consecutively for the ramp.
            if (Config.BLU_Use_Surpanakha && IsSpellActive(Surpanakha))
            {
                if (GetRemainingCharges(Surpanakha) == 4) _surpReady = true;
                if (GetRemainingCharges(Surpanakha) == 0) _surpReady = false;
                if (_surpReady && GetRemainingCharges(Surpanakha) > 0 && !BurstSoon())
                    return Surpanakha;
            }

            // oGCD damage (prioritise the longer recurring ones first).
            if (Config.BLU_Use_ShockStrike && IsSpellActive(ShockStrike) && IsOffCooldown(ShockStrike))
                return ShockStrike;
            if (Config.BLU_Use_MountainBuster && IsSpellActive(MountainBuster) && IsOffCooldown(MountainBuster))
                return MountainBuster;
            if (Config.BLU_Use_FeatherRain && IsSpellActive(FeatherRain) && IsOffCooldown(FeatherRain))
                return FeatherRain.Retarget(SonicBoom, enemy);
            if (Config.BLU_Use_Eruption && IsSpellActive(Eruption) && IsOffCooldown(Eruption))
                return Eruption;
            if (Config.BLU_Use_Quasar && IsSpellActive(Quasar) && IsOffCooldown(Quasar))
                return Quasar;
            if (Config.BLU_Use_JKick && IsSpellActive(JKick) && IsOffCooldown(JKick))
                return JKick;
            if (Config.BLU_Use_SeaShanty && IsSpellActive(SeaShanty) && IsOffCooldown(SeaShanty) && !BurstSoon())
                return SeaShanty;

            // Generic filler — cast ANY slotted, off-cooldown, in-range damage spell so the
            // rotation never idles. Considers your whole loadout (not a hand-picked list); only
            // FillerExcluded is skipped. Iterates in your spellbook order.
            foreach (var fillerId in Service.Configuration.ActiveBLUSpells)
                if (!FillerExcluded.Contains(fillerId) && IsOffCooldown(fillerId) && InActionRange(fillerId))
                    return fillerId;

            return 0;
        }

        // Scripted Moon Flute burst. Ported from BLU_NewMoonFluteOpener, gated by the per-ability
        // toggles. Returns 0 when nothing in the burst is ready (caller falls to the filler cascade).
        private static uint BurstStep()
        {
            if (!HasStatusEffect(Buffs.MoonFlute))
            {
                if (Config.BLU_Use_Whistle && IsSpellActive(Whistle) && !HasStatusEffect(Buffs.Whistle) && !WasLastAction(Whistle))
                    return Whistle;
                if (Config.BLU_Use_Tingle && IsSpellActive(Tingle) && !HasStatusEffect(Buffs.Tingle))
                    return Tingle;
                if (Config.BLU_Use_RoseOfDestruction && IsSpellActive(RoseOfDestruction) && GetCooldown(RoseOfDestruction).CooldownRemaining < 1f)
                    return RoseOfDestruction;
                if (Config.BLU_Use_MoonFlute && IsSpellActive(MoonFlute) && !JustUsed(MoonFlute))
                    return MoonFlute;
            }

            if (Config.BLU_Use_JKick && IsSpellActive(JKick) && IsOffCooldown(JKick))
                return JKick;
            if (Config.BLU_Use_TripleTrident && IsSpellActive(TripleTrident) && IsOffCooldown(TripleTrident))
                return TripleTrident;
            if (Config.BLU_Use_Nightbloom && IsSpellActive(Nightbloom) && IsOffCooldown(Nightbloom))
                return Nightbloom;

            bool wantDoT =
                (Config.BLU_Use_BreathOfMagic && IsSpellActive(BreathOfMagic) && !HasStatusEffect(Debuffs.BreathOfMagic, CurrentTarget, true)) ||
                (Config.BLU_Use_MortalFlame && IsSpellActive(MortalFlame) && !HasStatusEffect(Debuffs.MortalFlame, CurrentTarget, true));

            if (wantDoT)
            {
                if (Config.BLU_Use_Bristle && IsSpellActive(Bristle) && !HasStatusEffect(Buffs.Bristle))
                    return Bristle;
                if (Config.BLU_Use_FeatherRain && IsSpellActive(FeatherRain) && IsOffCooldown(FeatherRain))
                    return FeatherRain.Retarget(SonicBoom, EnemyTarget());
                if (Config.BLU_Use_SeaShanty && IsSpellActive(SeaShanty) && IsOffCooldown(SeaShanty))
                    return SeaShanty;
                if (Config.BLU_Use_BreathOfMagic && IsSpellActive(BreathOfMagic) && !HasStatusEffect(Debuffs.BreathOfMagic, CurrentTarget, true))
                    return BreathOfMagic;
                if (Config.BLU_Use_MortalFlame && IsSpellActive(MortalFlame) && !HasStatusEffect(Debuffs.MortalFlame, CurrentTarget, true))
                    return MortalFlame;
            }
            else
            {
                if (Config.BLU_Use_WingedReprobation && IsSpellActive(WingedReprobation) && IsOffCooldown(WingedReprobation) &&
                    !WasLastSpell(WingedReprobation) && !WasLastAbility(FeatherRain) &&
                    (!HasStatusEffect(Buffs.WingedReprobation) || GetStatusEffect(Buffs.WingedReprobation)?.Param < 2))
                    return WingedReprobation;
                if (Config.BLU_Use_FeatherRain && IsSpellActive(FeatherRain) && IsOffCooldown(FeatherRain))
                    return FeatherRain.Retarget(SonicBoom, EnemyTarget());
                if (Config.BLU_Use_SeaShanty && IsSpellActive(SeaShanty) && IsOffCooldown(SeaShanty))
                    return SeaShanty;
            }

            if (Config.BLU_Use_WingedReprobation && IsSpellActive(WingedReprobation) && IsOffCooldown(WingedReprobation) &&
                !WasLastAbility(ShockStrike) && GetStatusEffect(Buffs.WingedReprobation)?.Param < 2)
                return WingedReprobation;
            if (Config.BLU_Use_ShockStrike && IsSpellActive(ShockStrike) && IsOffCooldown(ShockStrike))
                return ShockStrike;
            if (Config.BLU_Use_BeingMortal && IsSpellActive(BeingMortal) && IsOffCooldown(BeingMortal))
                return BeingMortal;
            if (Config.BLU_Use_Bristle && IsSpellActive(Bristle) && !HasStatusEffect(Buffs.Bristle) &&
                IsOffCooldown(MatraMagic) && Config.BLU_Use_MatraMagic && IsSpellActive(MatraMagic))
                return Bristle;
            if (IsOffCooldown(Role.Swiftcast) && ActionLearned(Role.Swiftcast))
                return Role.Swiftcast;
            if (Config.BLU_Use_Surpanakha && IsSpellActive(Surpanakha) && GetRemainingCharges(Surpanakha) > 0)
                return Surpanakha;
            if (Config.BLU_Use_MatraMagic && IsSpellActive(MatraMagic) && HasStatusEffect(Role.Buffs.Swiftcast))
                return MatraMagic;
            if (Config.BLU_Use_PhantomFlurry && IsSpellActive(PhantomFlurry) && IsOffCooldown(PhantomFlurry))
                return PhantomFlurry;
            if (HasStatusEffect(Buffs.PhantomFlurry) && GetStatusEffect(Buffs.PhantomFlurry)?.RemainingTime < 2)
                return OriginalHook(PhantomFlurry);

            return 0;
        }

        #endregion

        #region Tank lane

        private static uint TankLane()
        {
            // Ensure Mighty Guard is ON (auto-on, never auto-cancel — Joey 2026-06-18).
            if (Config.BLU_Tank_AutoMightyGuard && Config.BLU_Use_MightyGuard && IsSpellActive(MightyGuard) &&
                !HasStatusEffect(MightyGuardBuff) && !WasLastAction(MightyGuard))
                return MightyGuard;

            // Reactive defensives shared with the DPS lane.
            uint def = DefensiveStep();
            if (def != 0)
                return def;

            var enemy = EnemyTarget();

            // Party-wide damage debuffs (tank is the ideal applier).
            if (Config.BLU_Use_Offguard && IsSpellActive(Offguard) && IsOffCooldown(Offguard) &&
                !HasStatusEffect(Debuffs.Offguard, CurrentTarget, true))
                return Offguard;
            if (Config.BLU_Use_PeculiarLight && IsSpellActive(PeculiarLight) && IsOffCooldown(PeculiarLight))
                return PeculiarLight;

            // Recurring nuke + instant oGCDs (unaffected by the Mighty Guard GCD penalty).
            if (Config.BLU_Use_RoseOfDestruction && IsSpellActive(RoseOfDestruction) && IsOffCooldown(RoseOfDestruction))
                return RoseOfDestruction;
            if (Config.BLU_Use_ShockStrike && IsSpellActive(ShockStrike) && IsOffCooldown(ShockStrike))
                return ShockStrike;
            if (Config.BLU_Use_MountainBuster && IsSpellActive(MountainBuster) && IsOffCooldown(MountainBuster))
                return MountainBuster;
            if (Config.BLU_Use_FeatherRain && IsSpellActive(FeatherRain) && IsOffCooldown(FeatherRain))
                return FeatherRain.Retarget(SonicBoom, enemy);
            if (Config.BLU_Use_Quasar && IsSpellActive(Quasar) && IsOffCooldown(Quasar))
                return Quasar;
            if (Config.BLU_Use_JKick && IsSpellActive(JKick) && IsOffCooldown(JKick))
                return JKick;
            if (Config.BLU_Use_Surpanakha && IsSpellActive(Surpanakha) && GetRemainingCharges(Surpanakha) == 4)
                return Surpanakha;

            // Devour for the bite + survival.
            if (Config.BLU_Use_Devour && IsSpellActive(Devour) && IsOffCooldown(Devour))
                return Devour;

            // Goblin Punch terminal filler (320 from the front under Mighty Guard).
            if (Config.BLU_Use_GoblinPunch && IsSpellActive(GoblinPunch))
                return GoblinPunch;

            return 0;
        }

        #endregion
    }
}
