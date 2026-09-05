#region Dependencies
using Dalamud.Game.ClientState.JobGauge.Types;
using ECommons.GameHelpers;
using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using GluttonyCombo.Combos.PvE.ALL;
using GluttonyCombo.CustomComboNS;
using GluttonyCombo.CustomComboNS.Functions;
using static GluttonyCombo.Combos.PvE.RDM.Config;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;
#endregion

namespace GluttonyCombo.Combos.PvE;

internal partial class RDM
{
    #region ID's

    #region Spells
    public const uint
        Verthunder = 7505,
        Veraero = 7507,
        Veraero2 = 16525,
        Veraero3 = 25856,
        Verthunder2 = 16524,
        Verthunder3 = 25855,
        Impact = 16526,
        Redoublement = 7516,
        EnchantedRedoublement = 7529,
        EnchantedRedoublementManafication = 45962,
        Zwerchhau = 7512,
        EnchantedZwerchhau = 7528,
        EnchantedZwerchhauManafication = 45961,
        Riposte = 7504,
        EnchantedRiposte = 7527,
        EnchantedRiposteManafication = 45960,
        Scatter = 7509,
        Verstone = 7511,
        Verfire = 7510,
        Vercure = 7514,
        Jolt = 7503,
        Jolt2 = 7524,
        Jolt3 = 37004,
        Verholy = 7526,
        Verflare = 7525,
        Fleche = 7517,
        ContreSixte = 7519,
        Engagement = 16527,
        Verraise = 7523,
        Scorch = 16530,
        Resolution = 25858,
        Moulinet = 7513,
        EnchantedMoulinet = 7530,
        EnchantedMoulinetDeux = 37002,
        EnchantedMoulinetTrois = 37003,
        Corpsacorps = 7506,
        Displacement = 7515,
        EnchantedReprise = 16528,
        Reprise = 16529,
        ViceOfThorns = 37005,
        GrandImpact = 37006,
        Prefulgence = 37007,
        Acceleration = 7518,
        Manafication = 7521,
        Embolden = 7520,
        MagickBarrier = 25857;
    #endregion

    #region Buffs & Debuffs
    public static class Buffs
    {
        public const ushort
            Swiftcast = 167,
            VerfireReady = 1234,
            VerstoneReady = 1235,
            Dualcast = 1249,
            Chainspell = 2560,
            Acceleration = 1238,
            Embolden = 1239,
            EmboldenOthers = 1297,
            Manafication = 1971,
            MagickBarrier = 2707,
            MagickedSwordPlay = 3875,
            ThornedFlourish = 3876,
            GrandImpactReady = 3877,
            PrefulgenceReady = 3878;
    }
    public static class Debuffs
    {
        public const ushort
            Addle = 1203;
    }
    #endregion

    #region Traits
    public static class Traits
    {
        public const uint
            EnhancedEmbolden = 620,
            EnhancedManaficationII = 622,
            EnhancedManaficationIII = 622,
            EnhancedAccelerationII = 624;
    }
    #endregion
    #endregion

    #region Variables

    // Combo List
    internal static readonly List<uint>
    ComboActionsList =
    [
        Riposte, EnchantedRiposte, Zwerchhau, EnchantedZwerchhau, Redoublement, EnchantedRedoublement, Verholy,
        Verflare, Scorch, Moulinet, EnchantedMoulinet, EnchantedMoulinetDeux, EnchantedMoulinetTrois
    ];
    internal static bool InCombo => ComboActionsList.Contains(ComboAction);

    /// <summary>
    ///     An Enchanted Moulinet chain is already underway. The single-target combo gets this for
    ///     free - its continuation steps are separate, ungated branches - but the AoE entry is one
    ///     condition covering start AND continuation, so holding it without this exemption would
    ///     stall a chain mid-way and waste the mana already spent on it.
    /// </summary>
    internal static bool InMoulinetChain =>
        ComboAction is EnchantedMoulinet or Moulinet or EnchantedMoulinetDeux;

    /// <summary>
    ///     RDM is inside the melee combo or the finisher chain that follows it - Riposte through
    ///     Redoublement, then Verholy/Verflare, Scorch, Resolution.
    ///     <para/>
    ///     The Occult Crescent handlers read this for two different reasons, which is why it is
    ///     named for the state rather than for either one:
    ///     <list type="bullet">
    ///     <item><b>Occult Quick is held over it</b> (v1.0.4.150). The chain is several GCDs of
    ///     instant weaponskills, so a 20s spell-instant window opened across it is mostly
    ///     thrown away.</item>
    ///     <item><b>Occult Comet is held over it</b> (v1.0.4.155). Comet is a spell, and a GCD
    ///     that is not the combo's next step BREAKS the chain - so firing it mid-combo does not
    ///     merely delay the combo, it resets it and forfeits the mana already spent.</item>
    ///     </list>
    ///     <para/>
    ///     Job-guarded for the gauge read: <c>InCombo</c> is self-limiting because action ids are
    ///     unique, but <c>GetJobGauge&lt;RDMGauge&gt;()</c> off-job returns whatever is in that
    ///     memory. <c>Job</c> is qualified rather than imported to keep the using list as
    ///     upstream has it.
    /// </summary>
    internal static bool InMeleeChain =>
        Player.Job is ECommons.ExcelServices.Job.RDM && (InCombo || HasManaStacks);

    /// <summary>
    ///     The full melee combo could start on the very next GCD: the chain is already
    ///     underway, both mana pools are at the level the rotation itself requires to open
    ///     it (<see cref="HasEnoughManaToStart"/>, which carries Embolden-phase pooling via
    ///     <see cref="ManaLevel"/>), or Magicked Swordplay has made the entry free.
    ///     <para/>
    ///     The Occult Crescent Time Mage handler reads this to hold Occult Quick
    ///     (v1.0.4.170). Joey: "add a gate so that occult quick doesn't get cast when you
    ///     are able to execute the full rdm damage combo." Riposte through Resolution is
    ///     six-odd GCDs of instant weaponskills, so a 20s spell-instant window opened one
    ///     GCD before Riposte is thrown away exactly as thoroughly as one opened
    ///     mid-chain - which <see cref="InMeleeChain"/> alone caught too late. Same
    ///     predicate, one GCD earlier: "the combo is due now", derived from the
    ///     rotation's own entry conditions, so it stays true as the thresholds move.
    ///     <para/>
    ///     Job-guarded like <see cref="InMeleeChain"/>: the gauge read must not happen
    ///     off-job.
    /// </summary>
    internal static bool MeleeComboImminent =>
        Player.Job is ECommons.ExcelServices.Job.RDM &&
        (InMeleeChain || HasEnoughManaToStart || CanMagickedSwordplay);

    // Gauge Stuff
    private static RDMGauge Gauge => GetJobGauge<RDMGauge>();
    internal static bool BlackHigher => Gauge.BlackMana >= Gauge.WhiteMana;
    internal static bool WhiteHigher => Gauge.BlackMana < Gauge.WhiteMana;
    internal static int ManaDifference => Math.Abs(Gauge.BlackMana - Gauge.WhiteMana);
    // The imbalance penalty starts at a 30 gap and halves the gain of the LOWER mana, so once the
    // gap opens it is self-reinforcing and takes twice as long to close. Below this guard the
    // rotation is allowed to widen the gap on the strength of a banked proc; at or above it,
    // balance wins and the proc waits its turn. 18 keeps a +6 filler under 24 - a full GCD of
    // headroom - and mirrors the 18 already used by CanFlare/CanHoly for the +11 finishers.
    internal static bool CanWidenManaGap => ManaDifference < 18;
    internal static bool HasEnoughManaToStart => Gauge.BlackMana >= ManaLevel() && Gauge.WhiteMana >= ManaLevel();
    internal static bool HasEnoughManaToStartStandalone => Gauge.BlackMana >= ManaLevelStandalone() && Gauge.WhiteMana >= ManaLevelStandalone();
    internal static bool HasEnoughManaForCombo => Gauge is { BlackMana: >= 15, WhiteMana: >= 15 };
    internal static bool HasManaStacks => Gauge.ManaStacks == 3;
    internal static bool CanFlare => BlackHigher && Gauge.BlackMana - Gauge.WhiteMana < 18;
    internal static bool CanHoly => WhiteHigher && Gauge.WhiteMana - Gauge.BlackMana < 18;
    internal static bool RedoublementRepriseMana => Gauge is { WhiteMana: >= 20, BlackMana: >= 20 };
    internal static bool ZwerchhauRepriseMana => Gauge is { WhiteMana: >= 35, BlackMana: >= 35 };

    //Floats
    internal static float EmboldenCD => GetCooldownRemainingTime(Embolden);
    internal static float VerFireRemaining => GetStatusEffectRemainingTime(Buffs.VerfireReady);
    internal static float VerStoneRemaining => GetStatusEffectRemainingTime(Buffs.VerstoneReady);

    //Bools
    internal static bool CanVerStone => HasStatusEffect(Buffs.VerstoneReady);
    internal static bool CanVerFire => HasStatusEffect(Buffs.VerfireReady);
    internal static bool CanVerFireAndStone => HasStatusEffect(Buffs.VerstoneReady) && HasStatusEffect(Buffs.VerfireReady);
    internal static bool CanGrandImpact => HasStatusEffect(Buffs.GrandImpactReady);
    internal static bool CanMagickedSwordplay => HasStatusEffect(Buffs.MagickedSwordPlay);
    internal static bool CanPrefulgence => HasStatusEffect(Buffs.PrefulgenceReady);
    internal static bool CanViceOfThorns => HasStatusEffect(Buffs.ThornedFlourish) && !JustUsed(Embolden, 6f);
    internal static bool HasDualcast => HasStatusEffect(Buffs.Dualcast);
    internal static bool HasAccelerate => HasStatusEffect(Buffs.Acceleration);
    internal static bool HasSwiftcast => HasStatusEffect(Buffs.Swiftcast);
    internal static bool HasEmbolden => HasStatusEffect(Buffs.Embolden);
    internal static bool HasManafication => HasStatusEffect(Buffs.Manafication);
    internal static bool CanAcceleration => ActionLearned(Acceleration) && !CanVerFireAndStone && HasCharges(Acceleration) && CanInstantCD &&
                                            (EmboldenCD > 15 || ActionLearned(Embolden));
    internal static bool CanAccelerationMovement => ActionLearned(Acceleration) && IsMoving() && HasCharges(Acceleration) && CanInstantCD;
    internal static bool CanSwiftcast => Role.CanSwiftcast() && CanInstantCD && !CanVerFireAndStone && (EmboldenCD > 10 || ActionLearned(Embolden));
    internal static bool CanSwiftcastMovement => Role.CanSwiftcast() && CanInstantCD && IsMoving();
    /// <summary>
    ///     Clear to spend a cast-time cooldown - Acceleration or Swiftcast. Gate for all four of
    ///     <see cref="CanAcceleration"/>, <see cref="CanAccelerationMovement"/>,
    ///     <see cref="CanSwiftcast"/> and <see cref="CanSwiftcastMovement"/>, which is every site
    ///     in the job that presses one.
    ///     <para/>
    ///     <c>!HasFreeInstantCasts</c> restored in v1.0.4.151, at Joey's call: "occult quick
    ///     doesn't last long. I say hold acceleration until it's over." v1.0.4.144 added it,
    ///     v1.0.4.146 backed it out on the grounds that Acceleration is not purely a cast-time
    ///     cooldown - it also feeds Grand Impact and the Verfire/Verstone procs - so suppressing
    ///     it cost procs for no gain. True as far as it went, but it weighed the proc generation
    ///     against nothing: from v1.0.4.150 RDM holds its procs through a Quick window and spends
    ///     the whole of it on Verthunder III / Veraero III, so a charge spent during one buys
    ///     Grand Impact plus procs the rotation has already decided not to cast yet. The window
    ///     is short and bounded; the charge keeps.
    ///     <para/>
    ///     Occult Quick only, NOT Occult Dualcast. Different objects: Quick is a window during
    ///     which Acceleration's instant-cast half cannot be worth anything for its whole
    ///     duration, so the cost of holding is bounded by the window. A Dualcast is one charge
    ///     the next spell consumes either way, and Joey scoped this call to Quick - it stays out
    ///     until he says otherwise rather than on a guess about how the two stack.
    ///     <para/>
    ///     <c>HasFreeInstantCasts</c> already covers the press as well as the buff, so the
    ///     v1.0.4.145/.147 race - Gluttony firing Occult Quick itself and deciding the next
    ///     action before the status lands - is handled without anything extra here.
    /// </summary>
    internal static bool CanInstantCD =>
        !InCombo && !HasSwiftcast && !CanGrandImpact && !HasEmbolden && !HasDualcast &&
        !HasAccelerate && !HasFreeInstantCasts;
    internal static bool CanEngagement => InMeleeRange() && HasCharges(Engagement) && ActionLearned(Engagement);
    internal static bool PoolEngagement => !ActionLearned(Embolden) || HasEmbolden || GetRemainingCharges(Engagement) >= 1 && GetCooldownChargeRemainingTime(Engagement) < 3;
    internal static bool SaveEngagement => GetRemainingCharges(Engagement) >= 2;
    internal static bool CanCorps => ActionLearned(Corpsacorps) && GetRemainingCharges(Corpsacorps) >= 1 && GetCooldownChargeRemainingTime(Corpsacorps) < 1;
    /// <summary>
    ///     An instant-cast effect is live, so the hard-cast slot is free and
    ///     <see cref="UseInstantCastST"/> should spend it on Verthunder III / Veraero III.
    ///     <para/>
    ///     Occult Quick and Occult Dualcast added in v1.0.4.150. Joey: RDM "handles dualcast
    ///     really well. But it doesn't do well with occult quick... It'll instant cast jolt or
    ///     verfire when it should be casting one of the long-cast spells (even if there's a proc
    ///     available b/c it's still the more powerful spell)." That is precisely this flag being
    ///     false. The rotation below already prefers the long casts whenever an instant effect is
    ///     up and falls through to Grand Impact / Verstone / Verfire / Jolt when one is not - the
    ///     Occult Crescent sources simply were not in the test, so a 20s Quick window read as
    ///     "no instant effect" and RDM spent it on spells that were instant anyway.
    ///     <para/>
    ///     Strict <c>HasOccultInstantCast</c>, not the HasOrExpects form: this AFFIRMATIVELY
    ///     picks a long cast, so it must not act on a proc that has not landed. RDM's own
    ///     Dualcast is read status-only here and has always behaved well, which is the evidence
    ///     that the strict read is enough for a selection site.
    /// </summary>
    internal static bool CanInstantCast =>
        HasDualcast || HasAccelerate || HasSwiftcast || HasOccultInstantCast;
    internal static bool CanNotMagickBarrier => !ActionReady(MagickBarrier) || HasStatusEffect(Buffs.MagickBarrier, anyOwner: true);
    #endregion

    #region Functions
    internal static int ManaLevel()
    {
        if (ActionLearned(Embolden)) // Level checks for Embolden then pools certain amounts of mana throughout the cd. 
        {
            if (HasEmbolden)
                return 50;
            switch (EmboldenCD)
            {
                case > 80:
                    return 60; //Fresh out of Embolden window requiring slightly higher to keep a third melee combo from happening before a few of the procs can be used
                case > 40 and <= 80:
                    return 55; // Normal operating fire at 50
                case > 15 and <= 40:
                    return 70; // As it gets closer increases level so if we do a melee combo we still have enough for double melee burst
                case <= 15:
                    return 90; // to prevent it from firing unless it is about to cap, should only fire for manual embolden users. 
            }
        }
        if (ActionLearned(Redoublement)) // Low level stuff
            return 50;
        return ActionLearned(Zwerchhau) ? 35 : 20;
    }

    internal static int ManaLevelStandalone()
    {
        if (ActionLearned(Redoublement)) // Low level stuff
            return 50;
        return ActionLearned(Zwerchhau) ? 35 : 20;
    }
    internal static bool UseVerStone()
    {
        if (!CanVerStone || HasDualcast || HasAccelerate || HasSwiftcast || HasOccultInstantCast ||
            VerStoneRemaining < 2.5 ||
            (CanVerFire && VerFireRemaining < 10 && VerFireRemaining < VerStoneRemaining))
            return false;

        // Verstone grants White. Black higher means this closes the gap, so it is always correct.
        if (BlackHigher) return true;

        // White already higher: this widens the gap. Worth +40 potency over Jolt III only while
        // there is room to spare - otherwise hold the proc and let a Verthunder III catch up.
        return !CanVerFire && CanWidenManaGap;
    }
    internal static bool UseVerFire()
    {
        if (!CanVerFire || HasDualcast || HasAccelerate || HasSwiftcast || HasOccultInstantCast ||
            VerFireRemaining < 2.5 ||
            (CanVerStone && VerStoneRemaining < 10 && VerStoneRemaining < VerFireRemaining))
            return false;

        // Verfire grants Black. White higher means this closes the gap, so it is always correct.
        if (WhiteHigher) return true;

        // Black already higher: this widens the gap. Same trade as UseVerStone, mirrored.
        return !CanVerStone && CanWidenManaGap;
    }
    internal static uint UseInstantCastST(uint actionID)
    {
        if (!ActionLearned(Verthunder) && ActionLearned(Veraero)) // Low level Check
            return OriginalHook(Veraero);

        // Verthunder III and Veraero III are equal potency, so this pick is purely proc generation
        // versus mana balance. Casting into the higher mana is a loan against a banked proc: it is
        // only repaid if that proc actually gets cast, and procs can only go out on a hard-cast
        // GCD. Anything else that owns the hard-cast slot - Vercure, Grand Impact, a phantom
        // action - defers the repayment while this keeps borrowing, and the gap runs away into the
        // imbalance penalty. CanWidenManaGap caps the loan.
        if (BlackHigher)
            return CanVerStone && CanWidenManaGap ?
                OriginalHook(Verthunder) :
                OriginalHook(Veraero);

        if (WhiteHigher)
            return CanVerFire && CanWidenManaGap ?
                OriginalHook(Veraero) :
                OriginalHook(Verthunder);

        return actionID;
    }
    internal static uint UseHolyFlare(uint actionID)
    {
        if (!ActionLearned(Verholy))
            return Verflare;

        if (BlackHigher)
        {
            if (CanVerStone && CanFlare)
                return CanVerFire ? Verholy : Verflare;
            return Verholy;
        }
        if (WhiteHigher)
        {
            if (CanVerFire && CanHoly)
                return CanVerStone ? Verflare : Verholy;
            return Verflare;
        }
        return actionID;
    }
    internal static uint UseThunderAeroAoE(uint actionID)
    {
        if (!ActionLearned(Verthunder2))
            return OriginalHook(Jolt);
        if (BlackHigher)
            return ActionLearned(Veraero2) ? Veraero2 : Verthunder2;
        return WhiteHigher ? Verthunder2 : actionID;
    }
    #endregion

    #region Opener
    internal static Standard Opener1 = new();
    internal static GapClosing Opener2 = new();
    internal static FirstGCD Opener3 = new();
    
    internal static WrathOpener Opener()
    {
        if (RDM_Opener_Selection == 0 && Opener1.LevelChecked) return Opener1;
        if (RDM_Opener_Selection == 1 && Opener2.LevelChecked) return Opener2;
        if (RDM_Opener_Selection == 2 && Opener2.LevelChecked) return Opener3;
        
        return (Opener1.LevelChecked) ? Opener1 : WrathOpener.Dummy;
    }
    internal class Standard : WrathOpener
    {
        public override List<Func<uint>> OpenerActions { get; set; } =
        [
            () => Veraero3, // 1
            () => Verthunder3, // 2
            () => Role.Swiftcast, // 3
            () => Items.UseItem(Items.GetStrongestPotionRow(Items.PotionType.Int)), // 4
            () => Verthunder3, // 5
            () => Fleche, // 6
            () => Acceleration, // 7
            () => Verthunder3, // 8
            () => Embolden, // 9
            () => Manafication, // 10
            () => EnchantedRiposteManafication, // 11
            () => ContreSixte, // 12
            () => EnchantedZwerchhauManafication, // 13
            () => Engagement, // 14
            () => EnchantedRedoublementManafication, // 15
            () => Corpsacorps, // 16
            () => Verholy, // 17
            () => ViceOfThorns, // 18
            () => Scorch, // 19
            () => Engagement, // 20
            () => Corpsacorps, // 21
            () => Resolution, // 22
            () => Prefulgence, // 23
            () => GrandImpact, // 24
            () => Acceleration, // 25
            () => Verfire, // 26
            () => GrandImpact, // 27
            () => Verthunder3, // 28
            () => Fleche, // 29
            () => Veraero3, // 30
            () => Verfire, // 31
            () => Verthunder3, // 32
            () => Verstone, // 33
            () => Veraero3, // 34
            () => Role.Swiftcast, // 35
            () => Veraero3, // 36
            () => ContreSixte // 37
        ];
        public override int MinOpenerLevel => 100;
        public override int MaxOpenerLevel => 109;

        public override List<(int[] Steps, uint NewAction, Func<bool> Condition)> SubstitutionSteps { get; set; } =
        [
            ([1], Jolt3, () => PartyInCombat() && !Player.Object.IsCasting)
        ];

        public override List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } =
        [
            ([14, 16, 20, 21], () => !InMeleeRange()),
            ([6],() => !HasStatusEffect(Buffs.Swiftcast) && !JustUsed(Role.Swiftcast))
        ];

        internal override UserData? ContentCheckConfig => RDM_BalanceOpener_Content;
        internal override bool IncludePot => RDM_Opener_Potion;
        public override Preset Preset => Preset.RDM_Balance_Opener;
        public override bool HasCooldowns()
        {
            if (!ActionsReady([Role.Swiftcast, Fleche, Embolden, ContreSixte]) || GetRemainingCharges(Acceleration) < 2 ||
                !IsOffCooldown(Manafication) ||
                GetRemainingCharges(Engagement) < 2 ||
                GetRemainingCharges(Corpsacorps) < 2)
                return false;

            return true;
        }
    }
    internal class GapClosing : WrathOpener
    {
        public override List<Func<uint>> OpenerActions { get; set; } =
        [
            () => Veraero3, // 1
            () => Verthunder3, // 2
            () => Role.Swiftcast, // 3
            () => Items.UseItem(Items.GetStrongestPotionRow(Items.PotionType.Int)), // 4
            () => Verthunder3, // 5
            () => Fleche, // 6
            () => Acceleration, // 7
            () => Verthunder3, // 8
            () => Embolden, // 9
            () => Manafication, // 10
            () => EnchantedRiposteManafication, // 11
            () => ContreSixte, // 12
            () => EnchantedZwerchhauManafication, // 13
            () => Corpsacorps, // 14
            () => EnchantedRedoublementManafication, // 15
            () => Engagement, // 16
            () => Verholy, // 17
            () => ViceOfThorns, // 18
            () => Scorch, // 19
            () => Corpsacorps, // 20
            () => Engagement, // 21
            () => Resolution, // 22
            () => Prefulgence, // 23
            () => GrandImpact, // 24
            () => Acceleration, // 25
            () => Verfire, // 26
            () => GrandImpact, // 27
            () => Verthunder3, // 28
            () => Fleche, // 29
            () => Veraero3, // 30
            () => Verfire, // 31
            () => Verthunder3, // 32
            () => Verstone, // 33
            () => Veraero3, // 34
            () => Role.Swiftcast, // 35
            () => Veraero3, // 36
            () => ContreSixte // 37
        ];
        public override int MinOpenerLevel => 100;
        public override int MaxOpenerLevel => 109;

        public override List<(int[] Steps, uint NewAction, Func<bool> Condition)> SubstitutionSteps { get; set; } =
        [
            ([1], Jolt3, () => PartyInCombat() && !Player.Object.IsCasting)
        ];

        public override List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = 
        [
            ([16, 21], () => !InMeleeRange()),
            ([35], () => !HasStatusEffect(Buffs.Swiftcast) && !JustUsed(Role.Swiftcast))
        ];

        internal override UserData? ContentCheckConfig => RDM_BalanceOpener_Content;
        internal override bool IncludePot => RDM_Opener_Potion;
        public override Preset Preset => Preset.RDM_Balance_Opener;
        public override bool HasCooldowns()
        {
            if (!ActionsReady([Role.Swiftcast, Fleche, Embolden, ContreSixte]) || GetRemainingCharges(Acceleration) < 2 ||
                !IsOffCooldown(Manafication) ||
                GetRemainingCharges(Engagement) < 2 ||
                GetRemainingCharges(Corpsacorps) < 2)
                return false;

            return true;
        }
    }
     internal class FirstGCD : WrathOpener
    {
        public override List<Func<uint>> OpenerActions { get; set; } =
        [
            () => Acceleration, // 1
            () => Veraero3, // 2
            () => Veraero3, // 3
            () => Embolden, // 4
            () => Items.UseItem(Items.GetStrongestPotionRow(Items.PotionType.Int)), // 5
            () => GrandImpact, // 6
            () => Fleche, // 7
            () => Manafication, // 8
            () => EnchantedRiposteManafication, // 9
            () => Corpsacorps, // 10
            () => EnchantedZwerchhauManafication, // 11
            () => Engagement, // 12
            () => EnchantedRedoublementManafication, // 13
            () => ContreSixte, // 14
            () => Verflare, // 15
            () => Engagement, // 16
            () => Corpsacorps, // 17
            () => Scorch, // 18
            () => Acceleration, // 19
            () => Role.Swiftcast, // 20
            () => Resolution, // 21
            () => Veraero3, // 22
            () => ViceOfThorns, // 23
            () => Prefulgence, // 24
            () => GrandImpact, // 25
            () => Verthunder3, // 26
            () => Verfire, // 27
            () => Verthunder3, // 28
            () => Fleche // 29
        ];
        public override int MinOpenerLevel => 100;
        public override int MaxOpenerLevel => 109;

        public override List<(int[] Steps, uint NewAction, Func<bool> Condition)> SubstitutionSteps { get; set; } =
        [
            ([2], Jolt3, () => PartyInCombat() && !Player.Object.IsCasting)
        ];
        
        public override List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays
        {
            get;
            set;
        } =
        [
            ([2], () => RDMFirstGCDOpenerAccelerationTime - 6)
        ];

        public override List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } =
        [
            ([11, 15], () => !InMeleeRange())
        ];

        internal override UserData? ContentCheckConfig => RDM_BalanceOpener_Content;
        internal override bool IncludePot => RDM_Opener_Potion;
        public override Preset Preset => Preset.RDM_Balance_Opener;
        public override bool HasCooldowns()
        {
            if (!ActionsReady([Role.Swiftcast, Fleche, Embolden, ContreSixte]))
                return false;

            if (!IsOffCooldown(Manafication))
                return false;

            if (GetRemainingCharges(Corpsacorps) < 2 || GetRemainingCharges(Engagement) < 2)
                return false;
            
            if (GetRemainingCharges(Acceleration) < 2)
                return false;

            if (InCombat())
                return false;

            if (CountdownRemaining > 25)
                return false;
            
            return true;
        }
    }
    #endregion
}


