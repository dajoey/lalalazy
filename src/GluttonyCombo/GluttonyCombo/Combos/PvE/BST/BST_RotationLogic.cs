using System.Collections.Generic;

namespace GluttonyCombo.Combos.PvE;

/// <summary>
///     Beastmaster decision engine (BST rebuild, 2026-09-16). PURE: no Dalamud or game types.
///     <see cref="BST"/> (BST.cs) reads the game into a <see cref="BstState"/> each tick and presses
///     whatever <see cref="Decide"/> returns. The same file compiles into
///     tests/GluttonyCombo.BSTRotationHarness, whose simulator runs it at every level 1-50.
/// </summary>
/// <remarks>
///     Every rule is grounded in data, not inference (research files named in BST_Beasts.cs):
///     <list type="bullet">
///         <item>One with Nature is consumed by Borrow OR Tempered Release: one per summon.</item>
///         <item>A horn is locked ~90 s in combat after its familiar retreats, so a familiar only
///               retreats when another assigned, learned horn is ready (never petless).</item>
///         <item>Familiar TP is shared by all horns and survives retreat and resummon.</item>
///         <item>Wespe's Tempered Release (Final Sting) retreats the familiar: it is an exit, not an opener.</item>
///         <item>Lingering Vantage exists only from L44 (Tempered Release) / L46 (Borrow).</item>
///         <item>Combos are Trick + axe pairs; each axe spends all TP. The pet's Heart lands ~1 s after
///               Trick (p90 1.7 s, p99 2.5 s over 308 live Tricks), so the follow-up axe waits for it.</item>
///         <item>Axes are Weaponskills on their own recast: auto-rotation only sends them when the GCD
///               is ready, so they are offered on GCD-ready ticks, never behind a weave gate.</item>
///     </list>
/// </remarks>
internal static class BST_RotationLogic
{
    // ------------------------------------------------------------------ level gates (7.56 sheets)

    public const int
        LvAxebladeBite = 2,
        LvAvalancheAxe = 4,
        LvPartingBlow = 6,
        LvMistralAxe = 8,
        LvTrick = 8,
        LvSecondBattlehorn = 10,
        LvShieldsplitter = 12,
        LvSpinningAxe = 14,
        LvGaleAxe = 16,
        LvTemperedRelease = 18,
        LvThirdBattlehorn = 20,
        LvBorrow = 22,
        LvShieldCharge = 24,
        LvRally = 28,
        LvTemperedResetOnSummon = 30,
        LvBorrowResetOnSummon = 34,
        LvRallyingCheer = 40,
        LvVantageFromTempered = 44,
        LvVantageFromBorrow = 46,
        LvFinishers = 50;

    /// <summary> Wait this long after a Battlehorn press before believing "no familiar" again. </summary>
    public const float SummonSettleSeconds = 2.5f;

    /// <summary> Give up waiting for the pet's Heart after Trick (live p99 = 2.5 s). </summary>
    public const float PetHeartTimeoutSeconds = 3.0f;

    /// <summary> Instinctual combo window. </summary>
    public const float ComboWindowSeconds = 7.0f;

    // ------------------------------------------------------------------ compass

    public static BeastmasterAffinity Clockwise(BeastmasterAffinity a) => a switch
    {
        BeastmasterAffinity.Volant => BeastmasterAffinity.Rampant,
        BeastmasterAffinity.Rampant => BeastmasterAffinity.Durant,
        BeastmasterAffinity.Durant => BeastmasterAffinity.Eldritch,
        BeastmasterAffinity.Eldritch => BeastmasterAffinity.Volant,
        _ => BeastmasterAffinity.None,
    };

    public static BeastmasterAffinity CounterClockwise(BeastmasterAffinity a) => a switch
    {
        BeastmasterAffinity.Rampant => BeastmasterAffinity.Volant,
        BeastmasterAffinity.Durant => BeastmasterAffinity.Rampant,
        BeastmasterAffinity.Eldritch => BeastmasterAffinity.Durant,
        BeastmasterAffinity.Volant => BeastmasterAffinity.Eldritch,
        _ => BeastmasterAffinity.None,
    };

    public static bool IsCompass(BeastmasterAffinity a) =>
        a is BeastmasterAffinity.Volant or BeastmasterAffinity.Rampant or BeastmasterAffinity.Durant or BeastmasterAffinity.Eldritch;

    /// <summary> The player axe for a compass affinity. </summary>
    public static uint AxeFor(BeastmasterAffinity a) => a switch
    {
        BeastmasterAffinity.Rampant => BST.AvalancheAxe,
        BeastmasterAffinity.Durant => BST.MistralAxe,
        BeastmasterAffinity.Eldritch => BST.SpinningAxe,
        BeastmasterAffinity.Volant => BST.GaleAxe,
        _ => 0,
    };

    public static int AxeLevel(BeastmasterAffinity a) => a switch
    {
        BeastmasterAffinity.Rampant => LvAvalancheAxe,
        BeastmasterAffinity.Durant => LvMistralAxe,
        BeastmasterAffinity.Eldritch => LvSpinningAxe,
        BeastmasterAffinity.Volant => LvGaleAxe,
        _ => 99,
    };

    public static bool AxeLearned(BeastmasterAffinity a, int level) => level >= AxeLevel(a);

    /// <summary> Highest-level learned axe (any affinity), for a bare axe. 0 below L4. </summary>
    public static uint AnyLearnedAxe(int level) =>
        level >= LvGaleAxe ? BST.GaleAxe
        : level >= LvSpinningAxe ? BST.SpinningAxe
        : level >= LvMistralAxe ? BST.MistralAxe
        : level >= LvAvalancheAxe ? BST.AvalancheAxe
        : 0;

    /// <summary> Learned Battlehorn slots at a (synced) level: 1 below L10, 2 below L20, else 3. </summary>
    public static int LearnedHornSlots(int level) =>
        level >= LvThirdBattlehorn ? 3 : level >= LvSecondBattlehorn ? 2 : 1;

    public static uint HornAction(int slot) => slot switch
    {
        2 => BST.SecondBattlehorn,
        3 => BST.ThirdBattlehorn,
        _ => BST.FirstBattlehorn,
    };

    // ------------------------------------------------------------------ release policy

    public enum ReleasePlan
    {
        /// <summary> Spend One with Nature on Tempered Release now (safe beast). </summary>
        Use,
        /// <summary> Beast's release is its exit (wespe): only fire through the exit gate. </summary>
        HoldForExit,
        /// <summary> Release not allowed for this beast by config (sleep / displacement) or beast unknown. </summary>
        Blocked,
    }

    public static ReleasePlan PlanRelease(BeastmasterBeast? beast, in BstSettings cfg)
    {
        if (beast is not { } b)
            return ReleasePlan.Blocked;

        if ((b.Release & BeastmasterReleaseTraits.Exit) != 0)
            return cfg.UseFinalStingAsExit ? ReleasePlan.HoldForExit : ReleasePlan.Blocked;

        if ((b.Release & BeastmasterReleaseTraits.Sleep) != 0 && !cfg.AllowSleepRelease)
            return ReleasePlan.Blocked;

        if ((b.Release & (BeastmasterReleaseTraits.Knockback | BeastmasterReleaseTraits.DrawIn)) != 0 && !cfg.AllowDisplacingRelease)
            return ReleasePlan.Blocked;

        return ReleasePlan.Use;
    }

    // ------------------------------------------------------------------ combo ordering

    public enum ComboOrder { None, TrickFirst, AxeFirst }

    /// <summary>
    ///     How to pair Trick with an axe for a pet of <paramref name="petAffinity"/>: Trick then the
    ///     axe clockwise of the pet (intentional, Mastered Instinct from L28), or the axe counter-
    ///     clockwise of the pet then Trick (intentional, Natural Instinct from L40). Below the level
    ///     where the preferred intentional axe exists, the other order is used if its axe is learned,
    ///     else an instinctual Trick-first pair with any learned axe.
    /// </summary>
    public static ComboOrder ChooseComboOrder(int level, BeastmasterAffinity petAffinity, int masterStacks, int naturalStacks)
    {
        if (!IsCompass(petAffinity) || level < LvTrick)
            return ComboOrder.None;

        var cwLearned = AxeLearned(Clockwise(petAffinity), level);
        var ccwLearned = AxeLearned(CounterClockwise(petAffinity), level);

        // L40+: bank one Natural Instinct once Mastered is building (Rallying Cheer / L50 triple).
        if (level >= LvRallyingCheer && naturalStacks == 0 && masterStacks >= 2 && ccwLearned)
            return ComboOrder.AxeFirst;

        if (cwLearned)
            return ComboOrder.TrickFirst;

        if (ccwLearned)
            return ComboOrder.AxeFirst;

        return AnyLearnedAxe(level) != 0 ? ComboOrder.TrickFirst : ComboOrder.None;
    }

    // ------------------------------------------------------------------ decision

    /// <summary> Everything the engine needs for one tick. The live half fills it from the game. </summary>
    public struct BstState
    {
        public int Level;
        public bool InCombat;
        public bool HasHostileTarget;
        public float TargetDistance;      // yalms, float.MaxValue when none
        public float TargetHpPercent;     // 0-100
        public bool TargetInterruptible;
        public int EnemiesWithin6y;       // around the player (Seedsower)
        public bool PlayerTargetedByEnemy;
        public bool PlayerIsCasting;
        public bool IsMoving;

        // gauge
        public int PlayerTp;
        public int FamiliarTp;
        public int ActiveSlot;                        // 0 = none
        public BeastmasterAffinity ComboState;        // 0x0C: 7 = Wavering Heart
        public BeastmasterAffinity LastAffinity;      // 0x0D: cleared ~7 s after the last instinctual skill
        public int KinshipSlot;                       // horn Borrow was used from (0 none)
        public int MasterStacks;
        public int NaturalStacks;

        // familiar
        public int Slot1Beast, Slot2Beast, Slot3Beast; // XBMPet rows assigned per horn (0 = empty/unknown)
        public bool SlotBeastsKnown;                   // false when the slot table could not be read
        public bool PetObjectPresent;
        public int PetObjectBeast;                     // XBMPet row from the pet object's BNpcBase (0 unknown)

        // player statuses
        public bool OneWithNature;
        public bool LingeringVantage;
        public bool WaveringHeart;
        public bool SunOrMoonActive;
        public BeastmasterAffinity SunMoon;           // Sunstrider / Moonstalker when active
        public bool KinshipHeld;

        // timing (seconds; float.MaxValue when never)
        public bool GcdReady;       // RemainingGCD within the queue window: weaponskills/spells may be sent
        public bool CanWeave;       // enough GCD left for an ability
        public float SinceSummon;   // since the active familiar arrived (gauge slot became non-zero)
        public float SinceHornPress;
        public float SinceTrick;
        public float SinceTempered;
        public float SinceBorrow;
        public float SinceAxe;
        public float SincePetHeart; // since the gauge last showed the pet's own affinity after a Trick
        public BeastmasterAffinity LastAxeAffinity; // affinity of the player's most recent axe
        public float TemperedRecastRemaining;

        // readiness (live half: ActionReady, which also covers level sync and resources)
        public bool ReadyHorn1, ReadyHorn2, ReadyHorn3;
        public bool ReadyTrick, ReadyTempered, ReadyBorrow, ReadyParting;
        public bool ReadyAxe;       // axe recast group free
        public bool ReadyRally, ReadyCheer;
        public uint BeastModeResolved; // GetAdjustedActionId(BeastMode), 0/BeastMode when no Kinship
        public bool ReadyBeastMode;
        public bool ReadyShieldCharge;
        public int ShieldChargeCharges, ShieldChargeMax;

        // GCD combo
        public uint LastComboAction;
        public bool ComboTimerActive;
    }

    /// <summary> Config, resolved by the live half (Simple mode = defaults). </summary>
    public struct BstSettings
    {
        public bool AoE;
        public int MinFamiliarStaySeconds;
        public bool AllowPetlessCycling;
        public bool UseFinalStingAsExit;
        public bool AllowDisplacingRelease;
        public bool AllowSleepRelease;
        public bool BorrowWhileReleaseRecasts;
        public bool SummonBeforeCombat;
        public bool RefreshBetweenPulls;
        public bool UseBeastskin, UseVileskin, UseSeedsower, UseScaleskin, UseSoulCrush, UseQuellingWaveRanged;
        public bool UseShieldCharge;
        public bool UseRally;

        public static BstSettings Defaults(bool aoe = false) => new()
        {
            AoE = aoe,
            MinFamiliarStaySeconds = 10,
            AllowPetlessCycling = false,
            UseFinalStingAsExit = true,
            AllowDisplacingRelease = false,
            AllowSleepRelease = false,
            BorrowWhileReleaseRecasts = true,
            SummonBeforeCombat = true,
            RefreshBetweenPulls = true,
            UseBeastskin = true,
            UseVileskin = true,
            UseSeedsower = true,
            UseScaleskin = false,
            UseSoulCrush = true,
            UseQuellingWaveRanged = true,
            UseShieldCharge = true,
            UseRally = true,
        };
    }

    public readonly record struct BstDecision(uint ActionId, string Reason, string Declines);

    public static int SlotBeast(in BstState s, int slot) => slot switch
    {
        1 => s.Slot1Beast,
        2 => s.Slot2Beast,
        3 => s.Slot3Beast,
        _ => 0,
    };

    public static bool HornReady(in BstState s, int slot) => slot switch
    {
        1 => s.ReadyHorn1,
        2 => s.ReadyHorn2,
        3 => s.ReadyHorn3,
        _ => false,
    };

    /// <summary> A familiar is out, arriving, or leaving: never summon over it. </summary>
    public static bool FamiliarPresentOrPending(in BstState s) =>
        s.ActiveSlot != 0 || s.PetObjectPresent || s.SinceHornPress < SummonSettleSeconds;

    /// <summary> The familiar is out and commandable. </summary>
    public static bool FamiliarOut(in BstState s) => s.ActiveSlot != 0;

    /// <summary> The beast of the active familiar: slot table first, pet object second. </summary>
    public static BeastmasterBeast? ActiveBeast(in BstState s)
    {
        var row = s.ActiveSlot is >= 1 and <= 3 ? SlotBeast(s, s.ActiveSlot) : 0;
        if (row == 0)
            row = s.PetObjectBeast;
        return BST_Beasts.ByRow(row);
    }

    /// <summary>
    ///     A horn that can be summoned now: learned at this level, has a beast assigned (when the slot
    ///     table is readable), ready, and not <paramref name="exceptSlot"/>. Lowest slot first.
    /// </summary>
    public static int PickReadyHorn(in BstState s, int exceptSlot, int avoidSlot = 0)
    {
        var learned = LearnedHornSlots(s.Level);
        var fallback = 0;
        for (var slot = 1; slot <= learned; slot++)
        {
            if (slot == exceptSlot || !HornReady(s, slot))
                continue;
            if (s.SlotBeastsKnown && SlotBeast(s, slot) == 0)
                continue;
            if (slot == avoidSlot)
            {
                fallback = fallback == 0 ? slot : fallback;
                continue;
            }
            return slot;
        }
        return fallback;
    }

    /// <summary> The one decision for this tick. Never returns 0: the GCD combo is the floor. </summary>
    public static BstDecision Decide(in BstState s, in BstSettings cfg)
    {
        var declines = new List<string>(4);

        BstDecision Pick(uint id, string reason) => new(id, reason, string.Join("+", declines));

        // ---------------------------------------------------------- out of combat
        if (!s.InCombat)
        {
            if (s.HasHostileTarget && !s.PlayerIsCasting)
            {
                if (cfg.SummonBeforeCombat && !FamiliarPresentOrPending(s))
                {
                    var slot = PickReadyHorn(s, 0);
                    if (slot != 0)
                        return Pick(HornAction(slot), $"prepull:summon-slot{slot}");
                    declines.Add("prepull:no-horn-ready");
                }

                // Out of combat a horn swap costs no lockout: trade a spent summon for a fresh One with Nature.
                if (cfg.RefreshBetweenPulls && s.Level >= LvTemperedRelease && FamiliarOut(s) && !s.OneWithNature
                    && s.SinceHornPress > SummonSettleSeconds + 1f)
                {
                    // Resummoning the horn Borrow came from ends that Kinship early: avoid it.
                    var slot = PickReadyHorn(s, s.ActiveSlot, s.KinshipHeld ? s.KinshipSlot : 0);
                    if (slot != 0 && !(s.KinshipHeld && slot == s.KinshipSlot))
                        return Pick(HornAction(slot), $"prepull:refresh-slot{slot}");
                }
            }

            return Pick(GcdChain(s), "ooc:gcdchain");
        }

        var beast = ActiveBeast(s);
        var plan = s.Level >= LvTemperedRelease ? PlanRelease(beast, cfg) : ReleasePlan.Blocked;
        var familiarOut = FamiliarOut(s);

        // ---------------------------------------------------------- 1. summon
        if (!FamiliarPresentOrPending(s))
        {
            if (!s.PlayerIsCasting && (s.CanWeave || !s.GcdReady))
            {
                var slot = PickReadyHorn(s, 0);
                if (slot != 0)
                    return Pick(HornAction(slot), $"summon:slot{slot}");
                declines.Add("summon:no-horn-ready");
            }
            else
            {
                declines.Add("summon:waiting-weave");
            }
        }
        else if (!familiarOut)
        {
            declines.Add(s.PetObjectPresent ? "familiar:arriving-or-leaving" : "familiar:summon-settling");
        }

        // ---------------------------------------------------------- 2. interrupt
        if (cfg.UseSoulCrush && s.KinshipHeld && s.BeastModeResolved == BST.SoulCrush && s.ReadyBeastMode
            && s.TargetInterruptible && s.CanWeave && s.TargetDistance <= 3.5f)
            return Pick(BST.SoulCrush, "beastmode:soulcrush-interrupt");

        // ---------------------------------------------------------- 3. exit (Parting Blow / Final Sting)
        if (familiarOut && s.CanWeave)
        {
            var exit = TryExit(s, cfg, beast, plan, declines);
            if (exit != 0)
                return Pick(exit, exit == BST.TemperedRelease ? "exit:finalsting" : "exit:partingblow");
        }

        // ---------------------------------------------------------- 4. spend One with Nature
        if (familiarOut && s.OneWithNature && s.Level >= LvTemperedRelease && s.CanWeave && s.SinceSummon >= 0.8f)
        {
            switch (plan)
            {
                case ReleasePlan.Use:
                    if (s.ReadyTempered && s.TargetDistance <= 25f)
                        return Pick(BST.TemperedRelease, "own:tempered");
                    if (!s.ReadyTempered && cfg.BorrowWhileReleaseRecasts && BorrowAllowed(s) && s.TemperedRecastRemaining > 12f)
                        return Pick(BST.Borrow, "own:borrow-while-tempered-recasts");
                    declines.Add(s.ReadyTempered ? "own:tempered-out-of-range" : "own:tempered-recast");
                    break;

                case ReleasePlan.Blocked:
                    if (BorrowAllowed(s))
                        return Pick(BST.Borrow, beast is null ? "own:borrow-unknown-beast" : "own:borrow-release-blocked");
                    declines.Add(beast is null ? "own:beast-unknown" : "own:release-blocked");
                    break;

                case ReleasePlan.HoldForExit:
                    declines.Add("own:held-for-finalsting");
                    break;
            }
        }

        // ---------------------------------------------------------- 5. Rally / Rallying Cheer
        if (cfg.UseRally && s.CanWeave)
        {
            var rally = ChooseRally(s);
            if (rally != 0)
                return Pick(rally, rally == BST.Rally ? "rally:stacks" : "rallyingcheer:stacks");
        }

        // ---------------------------------------------------------- 6. instinctual combos
        var combo = ChooseInstinctual(s, beast, declines);
        if (combo.ActionId != 0)
            return Pick(combo.ActionId, combo.Reason);

        // ---------------------------------------------------------- 7. Beast Mode
        var beastMode = ChooseBeastMode(s, cfg);
        if (beastMode.ActionId != 0)
            return Pick(beastMode.ActionId, beastMode.Reason);

        // ---------------------------------------------------------- 8. Shield Charge
        if (cfg.UseShieldCharge && s.Level >= LvShieldCharge && s.ReadyShieldCharge && s.CanWeave && s.ShieldChargeCharges > 0
            && s.TargetDistance <= 20f)
        {
            if (s.TargetDistance > 3.5f)
                return Pick(BST.ShieldCharge, "shieldcharge:gapclose");
            if (s.ShieldChargeCharges >= s.ShieldChargeMax && !s.IsMoving)
                return Pick(BST.ShieldCharge, "shieldcharge:max-charges");
        }

        // ---------------------------------------------------------- 9. GCD
        return Pick(GcdChain(s), "gcdchain");
    }

    /// <summary> Smash Axe -> Axeblade Bite (L2) -> Shieldsplitter (L12). </summary>
    public static uint GcdChain(in BstState s)
    {
        if (s.ComboTimerActive && s.LastComboAction == BST.SmashAxe && s.Level >= LvAxebladeBite)
            return BST.AxebladeBite;
        if (s.ComboTimerActive && s.LastComboAction == BST.AxebladeBite && s.Level >= LvShieldsplitter)
            return BST.Shieldsplitter;
        return BST.SmashAxe;
    }

    /// <summary> Parting Blow or wespe's Final Sting when the exit gate holds; 0 otherwise (declines recorded). </summary>
    public static uint TryExit(in BstState s, in BstSettings cfg, BeastmasterBeast? beast, ReleasePlan plan, List<string> declines)
    {
        if (s.Level < LvPartingBlow)
            return 0;

        var nextHorn = PickReadyHorn(s, s.ActiveSlot);
        var petlessOk = cfg.AllowPetlessCycling;

        // Pet mid-command: let Trick / Tempered Release resolve before it leaves.
        var trickPending = s.SinceTrick < PetHeartTimeoutSeconds && s.SincePetHeart > s.SinceTrick;
        if (trickPending || s.SinceTempered < 2f || s.SinceBorrow < 1f)
        {
            declines.Add("exit:pet-busy");
            return 0;
        }

        if (s.SinceSummon < cfg.MinFamiliarStaySeconds)
        {
            declines.Add("exit:min-stay");
            return 0;
        }

        // Wespe: Final Sting IS the exit, and it needs the summon's One with Nature.
        if (plan == ReleasePlan.HoldForExit && s.OneWithNature)
        {
            if (!s.ReadyTempered)
            {
                declines.Add("exit:finalsting-recast");
                return 0;
            }
            if (nextHorn == 0 && !petlessOk)
            {
                declines.Add("exit:no-other-horn-ready");
                return 0;
            }
            return BST.TemperedRelease;
        }

        // One with Nature must be spent first (or not exist / blocked for this beast).
        var ownPending = s.Level >= LvTemperedRelease && s.OneWithNature && plan == ReleasePlan.Use;
        if (ownPending)
        {
            declines.Add("exit:one-with-nature-unspent");
            return 0;
        }
        if (s.Level >= LvTemperedRelease && s.OneWithNature && plan == ReleasePlan.Blocked && BorrowAllowed(s))
        {
            declines.Add("exit:borrow-first");
            return 0;
        }

        if (!s.ReadyParting)
        {
            declines.Add("exit:partingblow-recast");
            return 0;
        }

        if (s.TargetDistance > 25f)
        {
            declines.Add("exit:out-of-range");
            return 0;
        }

        if (nextHorn == 0 && !petlessOk)
        {
            declines.Add("exit:no-other-horn-ready");
            return 0;
        }

        // L44+: Tempered Release / Borrow grant Lingering Vantage; exit while it is up.
        var vantageExpected = (s.Level >= LvVantageFromTempered && s.SinceTempered < 60f)
                              || (s.Level >= LvVantageFromBorrow && s.SinceBorrow < 60f);
        if (vantageExpected && !s.LingeringVantage && (s.SinceTempered < 3f || s.SinceBorrow < 3f))
        {
            declines.Add("exit:vantage-arriving");
            return 0;
        }

        return BST.PartingBlow;
    }

    /// <summary>
    ///     Rally when Mastered Instinct is full (3) and TP was just spent; at L50 also mid-chain to
    ///     reach the 250-TP finisher. Rallying Cheer when Natural Instinct is banked and familiar TP is
    ///     low (at L50 kept for the Rally + Cheer triple unless full).
    /// </summary>
    public static uint ChooseRally(in BstState s)
    {
        if (s.Level >= LvRally && s.ReadyRally && s.MasterStacks >= 3 && s.PlayerTp <= 28)
            return BST.Rally;

        if (s.Level >= LvRallyingCheer && s.ReadyCheer && s.ActiveSlot != 0 && s.NaturalStacks > 0 && s.FamiliarTp < 100)
        {
            var fullOrPreFinisher = s.Level < LvFinishers ? s.NaturalStacks >= 2 : s.NaturalStacks >= 3;
            var tripleStep = s.Level >= LvFinishers && s.PlayerTp >= 250 && s.MasterStacks == 0;
            if (fullOrPreFinisher || tripleStep)
                return BST.RallyingCheer;
        }

        return 0;
    }

    /// <summary> Borrow is allowed unless the held Kinship already came from this same horn. </summary>
    public static bool BorrowAllowed(in BstState s) =>
        s.Level >= LvBorrow && s.ReadyBorrow && !(s.KinshipHeld && s.KinshipSlot == s.ActiveSlot);

    /// <summary> Trick / axe pairing, chain follow-ups, L50 finishers, bare-axe and overcap rules. </summary>
    public static (uint ActionId, string Reason) ChooseInstinctual(in BstState s, BeastmasterBeast? beast, List<string> declines)
    {
        if (s.Level < LvAvalancheAxe)
            return (0, "");

        var familiarOut = FamiliarOut(s);
        var petAffinity = beast?.TrickAffinity ?? BeastmasterAffinity.None;
        var targetInMelee = s.TargetDistance <= 3.5f;
        var locked = s.WaveringHeart || s.ComboState == BeastmasterAffinity.WaveringHeart;
        var window = ComboWindowSeconds - 0.5f;

        // L50 finisher: Sun/Moon active and TP 250 -> the axe whose finisher is the OPPOSITE type (Universality).
        if (s.Level >= LvFinishers && s.PlayerTp >= 250 && s.ReadyAxe && s.GcdReady && targetInMelee)
        {
            if (s.SunMoon == BeastmasterAffinity.Moonstalker)
                return (BST.SpinningAxe, "finisher:risenfall-universality");
            if (s.SunMoon == BeastmasterAffinity.Sunstrider)
                return (BST.MistralAxe, "finisher:hawkishtalons-universality");
        }

        // Waiting on the pet's Heart after Trick: an axe now would resolve before the pet's skill.
        if (s.SinceTrick < PetHeartTimeoutSeconds && s.SincePetHeart > s.SinceTrick)
        {
            declines.Add("combo:awaiting-pet-heart");
            return (0, "");
        }

        var petWasLast = s.SincePetHeart < window && s.SincePetHeart <= s.SinceTrick && s.SincePetHeart < s.SinceAxe;
        var axeWasLast = s.SinceAxe < window && s.SinceAxe < s.SincePetHeart;

        // Follow-up after the pet's skill: axe clockwise of the pet (intentional). At L50 with the
        // triple sequence armed (2 Mastered, 1+ Natural, Rally + Cheer ready) the COUNTER-clockwise axe
        // instead, so the next Trick after Rally + Rallying Cheer is the intentional one.
        if (petWasLast && s.PlayerTp >= 100 && s.ReadyAxe)
        {
            var tripleArmed = s.Level >= LvFinishers && s.MasterStacks == 2 && s.NaturalStacks >= 1 && s.ReadyRally && s.ReadyCheer;
            var want = tripleArmed ? CounterClockwise(petAffinity) : Clockwise(petAffinity);
            var axe = AxeLearned(want, s.Level) ? AxeFor(want) : AnyLearnedAxe(s.Level);
            if (s.GcdReady && targetInMelee)
                return (axe, tripleArmed ? "combo:axe-ccw-triple" : axe == AxeFor(want) ? "combo:axe-clockwise-after-trick" : "combo:axe-after-trick");
            declines.Add("combo:axe-waiting-gcd");
            return (0, "");
        }

        // Follow-up after the player's axe: Trick continues the chain (familiar TP permitting)...
        if (axeWasLast && familiarOut && s.FamiliarTp >= 100 && s.ReadyTrick && !locked)
        {
            if (s.CanWeave && s.TargetDistance <= 25f)
                return (BST.Trick, "combo:trick-after-axe");
            declines.Add("combo:trick-after-axe-waiting");
            return (0, "");
        }

        // ...or, after a Rally refill, the axe clockwise of the last axe.
        if (axeWasLast && s.PlayerTp >= 100 && s.ReadyAxe && IsCompass(s.LastAxeAffinity))
        {
            var want = Clockwise(s.LastAxeAffinity);
            var axe = AxeLearned(want, s.Level) ? AxeFor(want) : AnyLearnedAxe(s.Level);
            if (s.GcdReady && targetInMelee)
                return (axe, "combo:axe-after-axe");
        }

        var order = ChooseComboOrder(s.Level, petAffinity, s.MasterStacks, s.NaturalStacks);

        // Start a pair: both gauges ready, not locked, axe recast free.
        if (familiarOut && order != ComboOrder.None && !locked && s.PlayerTp >= 100 && s.FamiliarTp >= 100 && s.ReadyAxe)
        {
            if (order == ComboOrder.TrickFirst)
            {
                if (s.ReadyTrick && s.CanWeave && s.TargetDistance <= 25f)
                    return (BST.Trick, "combo:trick-first");
                declines.Add("combo:trick-first-waiting");
            }
            else
            {
                var ccw = CounterClockwise(petAffinity);
                if (s.GcdReady && targetInMelee && AxeLearned(ccw, s.Level))
                    return (AxeFor(ccw), "combo:axe-first");
                declines.Add("combo:axe-first-waiting");
            }
            return (0, "");
        }

        if (locked)
            declines.Add("combo:wavering-heart");

        // Bare axe: no familiar possible (below L8, no horn assigned or ready), or TP about to overcap.
        var familiarPossible = s.Level >= LvTrick && (familiarOut || PickReadyHorn(s, 0) != 0 || s.ActiveSlot != 0);
        if (s.PlayerTp >= 100 && s.ReadyAxe && s.GcdReady && targetInMelee
            && (!familiarPossible || s.PlayerTp >= 235))
        {
            var axe = AnyLearnedAxe(s.Level);
            if (axe != 0)
                return (axe, familiarPossible ? "axe:overcap" : "axe:no-familiar");
        }

        // Bare Trick: familiar TP about to overcap and the player cannot pair soon.
        if (familiarOut && s.Level >= LvTrick && s.FamiliarTp >= 238 && s.PlayerTp < 72 && s.ReadyTrick && s.CanWeave && s.TargetDistance <= 25f)
            return (BST.Trick, "trick:overcap");

        return (0, "");
    }

    /// <summary> Beast Mode variant for the held Kinship (Soul Crush handled as an interrupt earlier). </summary>
    public static (uint ActionId, string Reason) ChooseBeastMode(in BstState s, in BstSettings cfg)
    {
        if (!s.KinshipHeld || !s.ReadyBeastMode || s.Level < LvBorrow)
            return (0, "");

        switch (s.BeastModeResolved)
        {
            case BST.Beastskin when cfg.UseBeastskin && s.CanWeave:
                return (BST.Beastskin, "beastmode:beastskin");
            case BST.Vileskin when cfg.UseVileskin && s.CanWeave && s.PlayerTargetedByEnemy:
                return (BST.Vileskin, "beastmode:vileskin");
            case BST.Scaleskin when cfg.UseScaleskin && s.CanWeave && s.PlayerTargetedByEnemy:
                return (BST.Scaleskin, "beastmode:scaleskin");
            case BST.Seedsower when cfg.UseSeedsower && s.CanWeave && s.EnemiesWithin6y > 0:
                return (BST.Seedsower, "beastmode:seedsower");
            // Quelling Wave rolls the GCD: only out of melee, only in place of combo step 1.
            case BST.QuellingWave when cfg.UseQuellingWaveRanged && s.GcdReady && !s.ComboTimerActive
                                       && s.TargetDistance > 3.5f && s.TargetDistance <= 30f:
                return (BST.QuellingWave, "beastmode:quellingwave-ranged");
        }

        return (0, "");
    }
}
