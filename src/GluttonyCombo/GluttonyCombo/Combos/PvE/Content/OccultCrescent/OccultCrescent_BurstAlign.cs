#region Dependencies

using System;
using System.Collections.Generic;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using static GluttonyCombo.Combos.PvE.OccultCrescent.Config;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;
using EZ = ECommons.Throttlers.EzThrottler;
using TS = System.TimeSpan;

#endregion

namespace GluttonyCombo.Combos.PvE;

// ============================================================================================
//  PHANTOM BURST ALIGNMENT
//
//  WHAT THE GAME ACTUALLY DOES (FFXIV wiki, Phantom Job page - verified 2026-08-24):
//
//    "Phantom job actions cannot deal critical or direct hit damage and are unaffected by
//     critical or direct hit rate-increasing buffs such as Battle Litany."
//    "Percentage damage-increasing buffs such as Riddle of Fire and Searing Light" DO affect
//     phantom actions, multiplicatively with Phantom Mastery, as do traits/effects like
//     Enochian (magic actions only) and Darkside.
//    "Main stat-increasing potions or resurrection debuffs such as Weakness also do not
//     affect phantom action damage."
//
//  Three consequences drive everything in this file:
//
//  1. The existing gate reads the WRONG BUFFS. Bursting.PlayerIsDamageBuffed is a general
//     "is anyone bursting" predicate, and half of what it counts does nothing for a phantom
//     action: Battle Litany, Battle Voice, Chain Stratagem, Devilment and the Bard songs are
//     crit/direct-hit-rate buffs, and Ley Lines is a speed buff. Using it to decide whether a
//     phantom nuke is worth firing asks a question the game does not answer that way.
//
//  2. It is also TRUE ALMOST ALWAYS for some jobs. Surging Tempest, Darkside and Mage's Ballad
//     are baseline uptime effects, not windows - a Warrior or Dark Knight has one up more or
//     less permanently. A "restrict to buff windows" gate that is open 100% of the time for
//     those jobs is not a gate. PhantomWindowOpen therefore counts only transient, window-
//     shaped effects, and only ones that actually multiply phantom damage.
//
//  3. PHANTOM AIM IS NOT A PHANTOM DAMAGE BUFF. It grants +50% crit and +50% direct hit rate -
//     exactly the two things phantom actions cannot do. Its entire value is to YOUR OWN JOB's
//     actions, which makes it a 120s personal raid buff that happens to live on the phantom
//     bar. It belongs in the two-minute window for that reason, not because phantom damage
//     cares about it.
//
//  WHAT ALIGNING BUYS, AND WHY IT IS NEARLY FREE:
//
//    Every phantom recast in AlignableActions is 40s, 60s, 90s or 120s. 40, 60 and 120 all
//    divide the 120s raid-buff cycle, so once an action lands inside a window it stays inside
//    every subsequent window at no further cost - the alignment is paid for ONCE, and only up
//    to MaxDelay seconds. (90s - Megaflare - never divides either cycle, so it is offered a
//    bounded hold and otherwise drifts; that is the honest ceiling on it.)
//
//    Holding a phantom GCD does not idle the GCD. The handler simply declines and the player's
//    own job rotation takes that slot instead. The cost of a hold is the delay itself, nothing
//    more, which is why the delay is bounded and why the stall guard below exists.
//
//  WHAT THIS DELIBERATELY DOES NOT TOUCH:
//
//    Heals, mitigation, raises, interrupts, stuns, dispels, movement and debuff APPLICATION.
//    A debuff applier (Silver Cannon, Mesmerize, Blazing Spellblade, Occult Libra, Pilfer
//    Weapon, Occult Mage Masher) wants to go out EARLY so the window opens on top of it -
//    holding one is backwards. Battle Bell is excluded for the same reason: its stacks build
//    from damage taken over 60s, so it needs lead time, not timing.
//
//    The Oracle deck and the Dancer dance are excluded outright. Both are chains on expiry
//    timers, and an expired Oracle prediction inflicts False Prediction - a 50,000 potency
//    damage-over-time on yourself. Shaving seconds off a nuke is not worth introducing a
//    failure mode that kills the player.
//
//    Berserker's Rage/Deadly Blow pair is excluded: Pent-up Rage scales off damage taken
//    during a 10s window, so its timing is driven by incoming damage rather than by buffs,
//    and the existing handler already sequences the pair carefully.
//
//    Everything on a 30s or shorter recast is excluded. Those come round again inside any
//    window on their own, so a hold buys nothing and only adds jitter.
// ============================================================================================
internal partial class OccultCrescent
{
    internal static class BurstAlign
    {
        #region Alignable actions

        /// <summary>
        ///     The phantom actions a short hold is worth for. IDs are shared between the
        ///     upstream handlers in OccultCrescent.cs and the fork's 7.55 handlers in
        ///     OccultCrescent_755.cs (P755.BLM_OccultFireIII and OccultFireIII are both
        ///     49072), so one entry covers both call sites.
        /// </summary>
        private static readonly HashSet<uint> AlignableActions =
        [
            // --- 120s: one use per two-minute window, so alignment is the whole ballgame ---
            PhantomAim,         // Ranger  - +50% crit/DH for YOUR JOB, 30s. See header note 3.
            HerosRime,          // Bard    - +10% party damage, 20s
            Zeninage,           // Samurai - 1,500 potency, the single biggest phantom hit
            BladeBlitz,         // Gladiator - 600 potency AoE
            LongReach,          // Gladiator - 400 potency
            Doomsday,           // Necromancer - 350 potency line (500 under Drain Touch)

            // --- 90s: never divides 60 or 120, so it drifts; a bounded hold is all we can do -
            Megaflare,          // Summoner - 1,000 potency, 6s cast

            // --- 60s: lands in every other 60s window, every two-minute window ---
            OccultComet,        // Time Mage - 500 potency, 8s cast (handler makes it instant)
            OccultHoly,         // White Mage - 500 potency, 750 vs undead
            OccultFlare,        // Black Mage - 500 potency
            OccultJump,         // Dragoon - 500 potency
            Hellfire,           // Summoner - 600 potency, shares recast with the two below
            JudgmentBolt,
            Thunderstorm,
            Finisher,           // Gladiator - 600 potency, up to 1,000 on Finishing Fervor
            FumaShuriken,       // Ninja - 230 potency
            FlameScroll,        // Ninja - 150 potency, 195 on Fire weakness
            LightningScroll,    // Ninja - 150 potency, 195 on Lightning weakness
            OccultAquaBreath,   // Blue Mage - 300 potency

            // --- 40s: three uses per two-minute cycle, one of them free inside the window ----
            Iainuki,            // Samurai - 500 potency cone
            OccultFireIII,      // Black Mage - 400 potency, 520 on weakness; shared recast
            OccultBlizzardIII,
            OccultThunderIII,
            DeepFreeze,         // Necromancer - 300 potency line, 400 under Drain Touch
            HellWind,
            ChaosDrive,
            AetherialGain,      // Geomancer - +10% party damage, 20s (weather-gated)
        ];

        #endregion

        #region Job burst anchors

        /// <summary>
        ///     The action on the player's OWN job whose cooldown predicts the next window that
        ///     multiplies phantom damage.
        ///     <para/>
        ///     Only actions granting a percentage damage increase (or a damage-taken increase on
        ///     the target) qualify, because those are the only ones phantom actions respond to.
        ///     Crit and direct-hit-rate buffs are absent on purpose - Battle Litany, Battle
        ///     Voice, Chain Stratagem and Devilment do literally nothing for a phantom nuke, so
        ///     anchoring to them would align to a window that is not one.
        ///     <para/>
        ///     Jobs with no entry return no anchor and are never held. That is the honest
        ///     answer: Samurai, Machinist, Black Mage, Viper, White Mage, Scholar and Sage have
        ///     no personal percentage damage buff at all, and Warrior and Dark Knight have only
        ///     Surging Tempest and Darkside, which are baseline uptime rather than a window.
        ///     Their party may well be bursting, but nothing readable from the local client says
        ///     WHEN - other players' cooldowns are not visible - so those jobs keep exactly
        ///     today's behaviour rather than guessing.
        /// </summary>
        private static uint[] AnchorsFor(Job job) => job switch
        {
            Job.PLD => [PLD.FightOrFlight],           // 60s, +25%
            Job.GNB => [GNB.NoMercy],                 // 60s, +20%
            Job.MNK => [MNK.RiddleOfFire],            // 60s, +15% (Brotherhood 120s rides along)
            Job.DRG => [DRG.LanceCharge],             // 60s, +10%
            Job.NIN => [NIN.KunaisBane],              // 60s, +10% damage taken on the target
            Job.RPR => [RPR.ArcaneCircle],            // 120s, +3%
            Job.BRD => [BRD.RagingStrikes],           // 120s, +15%
            Job.DNC => [DNC.TechnicalStep],           // 120s, +5% (Devilment is crit/DH: no)
            Job.SMN => [SMN.SearingLight],            // 120s, +5%
            Job.RDM => [RDM.Embolden],                // 120s, +5%
            Job.PCT => [PCT.StarryMuse],              // 120s, +5%
            Job.AST => [AST.Divination],              // 120s, +6%
            _ => [],
        };

        #endregion

        #region Window detection

        private static bool _windowOpen;
        private static bool _anyMultiplier;

        /// <summary>
        ///     Whether ANY effect that multiplies phantom damage is live - including the
        ///     permanent ones. This is the correct predicate for "would firing this be wasted",
        ///     i.e. for <c>Phantom_RestrictToBuff</c>.
        ///     <para/>
        ///     It replaces <see cref="Bursting.PlayerIsDamageBuffed" /> in the phantom handlers
        ///     for one reason: that predicate counts Battle Litany, Battle Voice, Chain
        ///     Stratagem, Devilment, Wanderer's Minuet, Army's Paeon and Ley Lines, and not one
        ///     of those does anything for a phantom action. Battle Litany is the wiki's own
        ///     example of a buff phantom actions ignore. A gate that opens on them is opening
        ///     on nothing.
        ///     <para/>
        ///     Surging Tempest, Darkside and Mage's Ballad ARE kept here even though they are
        ///     effectively permanent, because for this question that is the right answer: a
        ///     Warrior's phantom damage really is boosted all fight long, so there is never a
        ///     moment when holding it would gain anything.
        /// </summary>
        internal static bool PhantomDamageBuffed
        {
            get
            {
                if (!EZ.Throttle("PhantomAlign_AnyBuff", TS.FromSeconds(1)))
                    return _anyMultiplier;

                _anyMultiplier =
                    PhantomWindowOpen ||
                    // Permanent or near-permanent percentage multipliers
                    HasStatusEffect(WAR.Buffs.SurgingTempest) ||
                    DRK.Gauge.DarksideTimeRemaining > 0 ||
                    HasStatusEffect(BRD.Buffs.MagesBallad, anyOwner: true) ||
                    // Phantom-side multipliers with long uptime
                    HasStatusEffect(Buffs.OffensiveAria, anyOwner: true) ||
                    HasStatusEffect(Buffs.BattlesClangor) ||
                    HasStatusEffect(Buffs.PhantomKick) ||
                    HasStatusEffect(Buffs.BlazingSpellblade);

                return _anyMultiplier;
            }
        }

        /// <summary>
        ///     Whether a WINDOW-shaped damage buff that multiplies phantom damage is live right
        ///     now. This is the predicate alignment uses, and it is narrower than
        ///     <see cref="PhantomDamageBuffed" /> in a second direction: the permanent effects
        ///     are dropped, because a predicate that is true all fight long cannot tell you a
        ///     window has opened. Those effects are real, they do boost phantom damage, and
        ///     they are simply not events.
        /// </summary>
        internal static bool PhantomWindowOpen
        {
            get
            {
                if (!EZ.Throttle("PhantomAlign_Window", TS.FromSeconds(0.5)))
                    return _windowOpen;

                _windowOpen =
                    // Party raid buffs - percentage damage only
                    HasStatusEffect(AST.Buffs.Divination, anyOwner: true) ||
                    HasStatusEffect(AST.Buffs.BalanceBuff, anyOwner: true) ||
                    HasStatusEffect(AST.Buffs.SpearBuff, anyOwner: true) ||
                    HasStatusEffect(DNC.Buffs.TechnicalFinish, anyOwner: true) ||
                    HasStatusEffect(MNK.Buffs.Brotherhood, anyOwner: true) ||
                    HasStatusEffect(PCT.Buffs.StarryMuse, anyOwner: true) ||
                    HasStatusEffect(RDM.Buffs.Embolden, anyOwner: true) ||
                    HasStatusEffect(RDM.Buffs.EmboldenOthers, anyOwner: true) ||
                    HasStatusEffect(RPR.Buffs.ArcaneCircle, anyOwner: true) ||
                    HasStatusEffect(SMN.Buffs.SearingLight, anyOwner: true) ||
                    // Personal percentage damage buffs
                    HasStatusEffect(PLD.Buffs.FightOrFlight) ||
                    HasStatusEffect(GNB.Buffs.NoMercy) ||
                    HasStatusEffect(MNK.Buffs.RiddleOfFire) ||
                    HasStatusEffect(DRG.Buffs.LanceCharge) ||
                    HasStatusEffect(BRD.Buffs.RagingStrikes) ||
                    HasStatusEffect(BRD.Buffs.RadiantFinale) ||
                    // Phantom-side percentage damage buffs that are window-shaped.
                    // Offensive Aria (70s) and Phantom Kick (40s ramp) are excluded for the
                    // same reason the permanent job buffs are.
                    HasStatusEffect(Buffs.HerosRime, anyOwner: true) ||
                    HasStatusEffect(Buffs.AetherialGain, anyOwner: true) ||
                    // Target debuffs that raise damage taken
                    HasStatusEffect(NIN.Debuffs.KunaisBane, CurrentTarget, anyOwner: true) ||
                    HasStatusEffect(NIN.Debuffs.TrickAttack, CurrentTarget, anyOwner: true) ||
                    HasStatusEffect(NIN.Debuffs.Dokumori, CurrentTarget, anyOwner: true) ||
                    HasStatusEffect(NIN.Debuffs.Mug, CurrentTarget, anyOwner: true);

                return _windowOpen;
            }
        }

        /// <summary>
        ///     Seconds until the player's own job can open a phantom-relevant damage window, or
        ///     <see cref="float.MaxValue" /> when this job has no anchor to read.
        ///     <para/>
        ///     Anchors are looked up strictly by <see cref="Player.Job" />, never by probing
        ///     cooldowns across jobs: an action the player does not have reads as ready, which
        ///     would say "burst is 0 seconds away" on every job at once and hold everything
        ///     forever.
        /// </summary>
        internal static float SecondsUntilBurst
        {
            get
            {
                var anchors = AnchorsFor(Player.Job);
                if (anchors.Length == 0)
                    return float.MaxValue;

                var soonest = float.MaxValue;
                foreach (var anchor in anchors)
                {
                    if (!LevelChecked(anchor))
                        continue;

                    var remaining = GetCooldownRemainingTime(anchor);
                    if (remaining < soonest)
                        soonest = remaining;
                }

                return soonest;
            }
        }

        #endregion

        #region Hold decision

        /// <summary>
        ///     How long a single action has been continuously held, so a hold can never become
        ///     permanent. Without this, a Gunbreaker who has No Mercy switched off in their own
        ///     job settings reads "burst 0s away" forever and every alignable phantom action
        ///     stops firing for the whole fight.
        /// </summary>
        private static readonly Dictionary<uint, long> HoldStartedAt = [];

        private const long StallGraceMs = 3000;

        /// <summary>
        ///     Whether this phantom action should sit out the current opportunity and wait for
        ///     the player's own burst window.
        /// </summary>
        internal static bool ShouldHold(uint action)
        {
            // Cheapest discriminator first: this runs from IsEnabledAndUsable, which is called
            // for every phantom action on every frame.
            if (!AlignableActions.Contains(action))
                return false;

            if (!IsEnabled(Preset.Phantom_AlignToBurst))
                return false;

            // Explicitly int, not var: UserInt carries an implicit int conversion, so `var`
            // would bind the config object itself and re-read the store on every comparison.
            int maxDelay = Phantom_AlignToBurst_MaxDelay;
            if (maxDelay <= 0)
                return false;

            // Never hold outside combat: there is no window to wait for, and an opener wants
            // everything available the moment the pull lands.
            if (!InCombat())
            {
                HoldStartedAt.Remove(action);
                return false;
            }

            // Window already open - this is the moment we were waiting for.
            if (PhantomWindowOpen)
            {
                HoldStartedAt.Remove(action);
                return false;
            }

            var wait = SecondsUntilBurst;
            if (wait > maxDelay)
            {
                // Burst is too far off to be worth waiting for. Fire now; because every recast
                // in the set except Megaflare divides the burst cycle, this use puts the action
                // back on cooldown in a phase that will reach the next window on its own.
                HoldStartedAt.Remove(action);
                return false;
            }

            var now = Environment.TickCount64;
            if (!HoldStartedAt.TryGetValue(action, out var startedAt))
            {
                HoldStartedAt[action] = now;
                return true;
            }

            // Stall guard. The anchor said the window was imminent and it never arrived - the
            // player is not pressing it, is out of range, or has it disabled. Release rather
            // than hold a damage action hostage to a buff that is not coming.
            if (now - startedAt > (long) (maxDelay * 1000) + StallGraceMs)
            {
                HoldStartedAt.Remove(action);
                return false;
            }

            return true;
        }

        #endregion

        #region Diagnostics

        /// <summary>One-line state dump for the phantom diagnostic log.</summary>
        internal static string Describe() =>
            $"align={IsEnabled(Preset.Phantom_AlignToBurst)} maxDelay={Phantom_AlignToBurst_MaxDelay}s " +
            $"job={Player.Job} anchors={AnchorsFor(Player.Job).Length} " +
            $"untilBurst={(SecondsUntilBurst >= float.MaxValue ? "n/a" : SecondsUntilBurst.ToString("F1"))} " +
            $"windowOpen={PhantomWindowOpen} holding={HoldStartedAt.Count}";

        #endregion
    }
}
