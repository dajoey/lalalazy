using System;
using System.Collections.Generic;
using System.Linq;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.Combos.PvE;

/// <summary>
///     Crucible of the Unbroken rules for the Beastmaster engine. PURE, like <see cref="BST_RotationLogic"/>,
///     which calls these only when <see cref="BstSettings.Crucible"/> is on and the territory is a Crucible
///     board (<see cref="BstState.CrucibleBoard"/> != 0). Outside the Crucible nothing here runs.
/// </summary>
/// <remarks>
///     What is different in there (guides, the game's own panel data, and other automation's graded runs):
///     <list type="bullet">
///         <item>Familiars have HP and no regeneration; HP carries from node to node and only a campsite rest
///               restores it. A familiar that is knocked out is gone for the run. A familiar leaves by a horn
///               blown over it (1 s cast, HP kept) before it gets low; Parting Blow is the fallback, because the
///               familiar keeps taking hits while it performs the blow (one died at 38% that way).</item>
///         <item>Horns are not blown out of combat (crash reports and blocked summons on the board).</item>
///         <item>Every enemy panel says which casts are interruptible, which buffs dispellable, which debuffs
///               cleansable; the Kinship that answers the fight is borrowed from the familiar that has it.</item>
///         <item>Some enemies counter attackers (spikes stances), are invulnerable for a phase, or must not be
///               hit at all (eggs, morphos).</item>
///         <item>Snarl / Challenge are the only enmity tools, and Snarl also stops the character's own enmity.
///               Covering a hard hit costs the familiar about three times what it saves the character, so Snarl
///               is for a low character only, when the familiar can carry the damage. The rules start in shadow
///               mode: decided and logged, not pressed, until a run has been graded.</item>
///     </list>
/// </remarks>
internal static class BST_CrucibleLogic
{
    /// <summary> All.Cease: the auto-rotation treats it as "press nothing". </summary>
    public const uint Hold = 1_000_004;

    /// <summary> Below this HP on every remaining enemy the round is ending: no normal Parting Blow exit. </summary>
    public const float RoundEndingHp = 10f;

    /// <summary> A familiar save is skipped when the last enemy is this close to death anyway. </summary>
    public const float EnemyDyingHp = 3f;

    /// <summary> XBMPet row of the vulture: Bloodcurdling Caw dispels a buff. </summary>
    public const int VultureRow = 11;

    /// <summary>
    ///     Horn picks named in the "no horns assigned" chat warning. Gluttony installs no fight guide, so the need model
    ///     is the enemy panel alone (every call Required): the picks answer what the fight calls for, then rank by points.
    /// </summary>
    public static List<CrucibleBeastPick> HornWarningPicks(int board, int battle, Func<int, bool> captured) =>
        BST_CrucibleNeedFirst.Pick(board, battle, captured);

    /// <summary>
    ///     What the rotation reads for a battle: the ability needs the picker and the fight guide read (the panel's calls plus the
    ///     guide's counters, one list per battle). Required needs always count. A Useful interrupt or cleanse counts too: the game's
    ///     own flag (the cast is interruptible, the character's debuff is cleansable) decides at run time, so a guide claim the game
    ///     contradicts never fires. A Useful dispel does NOT count: the dispellable-buff list is this plugin's own (the panel's flags
    ///     plus Might), and where the guides and the sheet disagree (Might, Grab and Grow, Impassion) a swap and a Tempered Release
    ///     spent on a buff nobody has seen come off would be a guess; a run log that shows the buff leaving after a dispel settles it.
    /// </summary>
    public static CrucibleNeeds FightNeeds(int board, int battle)
    {
        var model = CrucibleNeedModel.For(board, battle);
        return model.Required | (model.Useful & ~CrucibleNeeds.Dispel);
    }

    /// <summary> A familiar summoned this recently is not swapped out above the critical line. </summary>
    public const float SwapGraceSeconds = 8f;

    /// <summary> A horn blown this recently may still be casting: give it this long before Parting Blow. </summary>
    public const float HornRetrySeconds = 2f;

    /// <summary> The next familiar must be at least this many points healthier to be worth a swap. </summary>
    public const float SwapMinGain = 10f;

    /// <summary> The critical line is half the swap line. </summary>
    public static float CriticalHp(in BstSettings cfg) => cfg.CruciblePetSwapHp / 2f;

    // ------------------------------------------------------------------ kin

    /// <summary> The Kinship the held Beast Mode variant belongs to (None when nothing is held). </summary>
    public static BeastmasterKinType HeldKin(in BstState s)
    {
        if (!s.KinshipHeld)
            return BeastmasterKinType.None;

        return s.BeastModeResolved switch
        {
            BST.Beastskin => BeastmasterKinType.Beastkin,
            BST.Vileskin => BeastmasterKinType.Vilekin,
            BST.CloudSkim => BeastmasterKinType.Cloudkin,
            BST.Seedsower => BeastmasterKinType.Seedkin,
            BST.QuellingWave => BeastmasterKinType.Wavekin,
            BST.Scaleskin => BeastmasterKinType.Scalekin,
            BST.SoulCrush => BeastmasterKinType.Soulkin,
            BST.ScouringAsh => BeastmasterKinType.Ashkin,
            _ => BeastmasterKinType.None,
        };
    }

    /// <summary> First learned horn whose assigned beast is of <paramref name="kin"/> (or is <paramref name="row"/>); 0 = none. </summary>
    public static int SlotWithKin(in BstState s, BeastmasterKinType kin, int row = 0)
    {
        var learned = LearnedHornSlots(s.Level);
        for (var slot = 1; slot <= learned; slot++)
        {
            var beast = BST_Beasts.ByRow(SlotBeast(s, slot));
            if (beast is { } b && (row != 0 ? b.Row == row : b.Kin == kin))
                return slot;
        }
        return 0;
    }

    /// <summary>
    ///     The Kinship this battle's panel calls for, in order interrupt (Soulkin) &gt; dispel (Wavekin, unless a
    ///     vulture is on a horn) &gt; cleanse (Ashkin, unless a bat is on a horn), skipping needs no assigned beast answers.
    /// </summary>
    public static BeastmasterKinType WantedKin(in BstState s)
    {
        if ((s.CrucibleNeeds & CrucibleNeeds.Interrupt) != 0 && SlotWithKin(s, BeastmasterKinType.Soulkin) != 0)
            return BeastmasterKinType.Soulkin;
        if ((s.CrucibleNeeds & CrucibleNeeds.Dispel) != 0 && SlotWithKin(s, BeastmasterKinType.None, VultureRow) == 0
            && SlotWithKin(s, BeastmasterKinType.Wavekin) != 0)
            return BeastmasterKinType.Wavekin;
        if ((s.CrucibleNeeds & CrucibleNeeds.Cleanse) != 0 && SlotWithKin(s, BeastmasterKinType.None, BST_CrucibleData.BatRow) == 0
            && SlotWithKin(s, BeastmasterKinType.Ashkin) != 0)
            return BeastmasterKinType.Ashkin;
        return BeastmasterKinType.None;
    }

    /// <summary>
    ///     In combat: the familiar out is the one whose Kinship the fight calls for and that Kinship is not held, so
    ///     its One with Nature goes to Borrow instead of Tempered Release. The Soulkin is the exception with a clock:
    ///     Soul Crush is a 30 s kinship and the borrow spends the Soulkin's once-per-summon One with Nature, so it is
    ///     borrowed only while an interruptible cast is live (that is the re-arm; otherwise
    ///     <see cref="HoldOwnForInterrupt"/> keeps the ammo).
    /// </summary>
    public static bool ShouldBorrowForFight(in BstState s, BeastmasterBeast? beast) =>
        beast is { } b && b.Kin != BeastmasterKinType.None && WantedKin(s) == b.Kin && HeldKin(s) != b.Kin
        && s.OneWithNature && BorrowAllowed(s)
        && (b.Kin != BeastmasterKinType.Soulkin || s.TargetInterruptible);

    /// <summary>
    ///     Out of combat (only with "horns out of combat" allowed): summon the horn with the wanted kin, Borrow, and let
    ///     the between-pulls refresh swap to another horn.
    /// </summary>
    public static (uint ActionId, string Reason) PrepareKinship(in BstState s, List<string> declines)
    {
        if (s.Level < LvBorrow || s.CrucibleNeeds == CrucibleNeeds.None)
            return (0, "");

        var kin = WantedKin(s);
        if (kin == BeastmasterKinType.None || HeldKin(s) == kin)
            return (0, "");

        var slot = SlotWithKin(s, kin);
        var name = kin.ToString().ToLowerInvariant();

        if (s.ActiveSlot == slot)
        {
            // The Soulkin waits for a live cast (see ShouldBorrowForFight): a prepull borrow would arm Soul Crush's
            // 30 s window before anything can be interrupted.
            if (kin == BeastmasterKinType.Soulkin)
            {
                declines.Add("crucible:prepull-borrow-waiting-cast");
                return (0, "");
            }
            if (s.OneWithNature && s.ReadyBorrow && s.SinceHornPress > SummonSettleSeconds)
                return (BST.Borrow, $"crucible:prepull-borrow-{name}");
            declines.Add("crucible:prepull-borrow-waiting");
            return (0, "");
        }

        if (!HornReady(s, slot))
        {
            declines.Add($"crucible:prepull-{name}-horn-not-ready");
            return (0, "");
        }

        if (!FamiliarPresentOrPending(s) || (FamiliarOut(s) && s.SinceHornPress > SummonSettleSeconds + 1f))
            return (HornAction(slot), $"crucible:prepull-{name}-slot{slot}");

        return (0, "");
    }

    /// <summary>
    ///     Hold the Soulkin's One with Nature on an interrupt fight: Soul Crush is a 30 s kinship and the very borrow that
    ///     arms it spends the Soulkin's once-per-summon One with Nature, so each summon buys one window. Spending that
    ///     One with Nature on a Tempered Release while no cast is up wastes the window before the next cast starts (the
    ///     2026-10-02 evening: every one of the 12 completed heals started with Soul Crush not held). While this holds,
    ///     <see cref="ShouldBorrowForFight"/> still borrows the moment an interruptible cast is live — that is the re-arm.
    /// </summary>
    public static bool HoldOwnForInterrupt(in BstState s, in BstSettings cfg, BeastmasterBeast? beast) =>
        cfg.Crucible && s.CrucibleBoard != 0 && cfg.UseSoulCrush && (s.CrucibleNeeds & CrucibleNeeds.Interrupt) != 0
        && beast is { Kin: BeastmasterKinType.Soulkin }
        && HeldKin(s) != BeastmasterKinType.Soulkin
        && !s.TargetInterruptible;

    /// <summary>
    ///     The bat's Ultrasonics is the cleanse answer, and its One with Nature is spent by the very release that
    ///     cleanses — each summon buys one cleanse. Board 5, 2026-10-03 11:08:42: the bat was summoned on the cleanse
    ///     fight and its One with Nature went to a discretionary Tempered and borrow 1.4 s later; 11:09:31 the
    ///     Paralysis landed and stayed because Ultrasonics needs that One with Nature, and the character died behind
    ///     it. Hold the bat's One with Nature while no cleansable debuff is up and Scouring Ash is not held;
    ///     <see cref="TryCleanse"/> spends it the moment a debuff lands.
    /// </summary>
    public static bool HoldOwnForCleanse(in BstState s, in BstSettings cfg, BeastmasterBeast? beast) =>
        cfg.Crucible && s.CrucibleBoard != 0 && (s.CrucibleNeeds & CrucibleNeeds.Cleanse) != 0
        && beast is { Row: BST_CrucibleData.BatRow }
        && HeldKin(s) != BeastmasterKinType.Ashkin
        && !s.PlayerHasCleansableDebuff;

    /// <summary>
    ///     The vulture's Bloodcurdling Caw is the dispel answer on a fight the panel calls for, and its One with
    ///     Nature buys one Caw per summon (the Strix Piece's Ultimate Focus). Hold it between dispellable buffs
    ///     unless Quelling Wave is already held; <see cref="TryDispel"/> spends it the moment the buff lands.
    /// </summary>
    public static bool HoldOwnForDispel(in BstState s, in BstSettings cfg, BeastmasterBeast? beast) =>
        cfg.Crucible && s.CrucibleBoard != 0 && (s.CrucibleNeeds & CrucibleNeeds.Dispel) != 0
        && beast is { Row: VultureRow }
        && HeldKin(s) != BeastmasterKinType.Wavekin
        && !s.TargetHasDispellableBuff;

    /// <summary>
    ///     A discretionary Borrow (Tempered on recast, release blocked) would replace a held answer kinship with another
    ///     kin on a fight that still needs that answer — the 2026-10-02 evening showed the held Soul Crush dropping to
    ///     beastskin exactly this way mid-fight, and the same borrow replaces a held Scouring Ash or Quelling Wave.
    ///     Returns the decline reason naming the held answer, or null when the borrow may go through.
    /// </summary>
    public static string? HeldAnswerBorrowBlock(in BstState s, in BstSettings cfg)
    {
        if (!cfg.Crucible || s.CrucibleBoard == 0 || !s.KinshipHeld)
            return null;
        if (cfg.UseSoulCrush && (s.CrucibleNeeds & CrucibleNeeds.Interrupt) != 0 && s.BeastModeResolved == BST.SoulCrush)
            return "own:borrow-keep-soulcrush";
        if ((s.CrucibleNeeds & CrucibleNeeds.Cleanse) != 0 && s.BeastModeResolved == BST.ScouringAsh)
            return "own:borrow-keep-scouringash";
        if ((s.CrucibleNeeds & CrucibleNeeds.Dispel) != 0 && s.BeastModeResolved == BST.QuellingWave)
            return "own:borrow-keep-quellingwave";
        return null;
    }

    /// <summary>
    ///     A discretionary Borrow (Tempered on recast, release blocked) would replace the held Soul Crush with another
    ///     kin on a fight that still needs interrupts — the 2026-10-02 evening showed the held kinship dropping to
    ///     beastskin exactly this way mid-fight. While Soul Crush is held on an interrupt fight, those borrows wait.
    /// </summary>
    public static bool KeepHeldSoulCrush(in BstState s, in BstSettings cfg) =>
        cfg.Crucible && s.CrucibleBoard != 0 && cfg.UseSoulCrush && (s.CrucibleNeeds & CrucibleNeeds.Interrupt) != 0
        && s.KinshipHeld && s.BeastModeResolved == BST.SoulCrush;

    // ------------------------------------------------------------------ horns

    /// <summary> Last HP seen on a horn's familiar this run; unknown (0) counts as healthy. </summary>
    public static float SlotPetHp(in BstState s, int slot)
    {
        var hp = slot switch { 1 => s.Slot1PetHp, 2 => s.Slot2PetHp, 3 => s.Slot3PetHp, _ => 0f };
        return hp <= 0f ? 100f : hp;
    }

    private static bool IsExitBeast(in BstState s, int slot) =>
        BST_Beasts.ByRow(SlotBeast(s, slot)) is { } b && (b.Release & BeastmasterReleaseTraits.Exit) != 0;

    private static bool IsSetUpBeast(in BstState s, int slot) => BST_CrucibleData.SetUpBeasts.Contains(SlotBeast(s, slot));

    /// <summary>
    ///     The horn to summon in the Crucible: among ready, learned, assigned horns (not <paramref name="exceptSlot"/>),
    ///     familiars above the swap line first, then set-up familiars (vulnerability / resistance down) before the rest,
    ///     wespe last unless its Final Sting is due, then the healthiest, then slot order. A familiar at or below the
    ///     critical line only comes out while Parting Blow is ready to save it.
    /// </summary>
    public static int PickHorn(in BstState s, in BstSettings cfg, int exceptSlot)
    {
        var learned = LearnedHornSlots(s.Level);
        var best = 0;
        (int, int, int, int, int, float) bestKey = (0, 0, 0, 0, 0, 0f);
        var stingDue = FinalStingDue(s, cfg);
        var answers = LiveAnswerSlots(s);
        // The fight needs an interrupt and Soul Crush is not held: the Soulkin comes out first so its Kinship is borrowed before the
        // cast (an interrupt is gone in seconds; the logs show nearly every interrupt that landed began with Soul Crush already held).
        var armInterrupt = cfg.UseSoulCrush && (s.CrucibleNeeds & CrucibleNeeds.Interrupt) != 0 && HeldKin(s) != BeastmasterKinType.Soulkin;

        for (var slot = 1; slot <= learned; slot++)
        {
            if (slot == exceptSlot || !HornReady(s, slot) || (s.SlotBeastsKnown && SlotBeast(s, slot) == 0))
                continue;

            var hp = SlotPetHp(s, slot);
            var healthy = hp > cfg.CruciblePetSwapHp;
            var key = (
                healthy ? 1 : 0,
                healthy && answers.Contains(slot) ? 1 : 0,       // the familiar that answers what the fight is doing right now
                healthy && armInterrupt && BST_Beasts.ByRow(SlotBeast(s, slot)) is { Kin: BeastmasterKinType.Soulkin } ? 1 : 0,
                IsExitBeast(s, slot) ? (stingDue ? 2 : 0) : 1,   // wespe first when its Final Sting is due, else last
                healthy && IsSetUpBeast(s, slot) ? 1 : 0,        // set-up beasts first; below the line health decides
                hp);
            if (best == 0 || key.CompareTo(bestKey) > 0)
            {
                best = slot;
                bestKey = key;
            }
        }

        if (best != 0 && bestKey.Item6 <= CriticalHp(cfg) && s.InCombat && s.PartingBlowRecast > 2f)
            return 0;
        return best;
    }

    // ------------------------------------------------------------------ in combat

    /// <summary> The familiar is covering the character (Snarl in the last 45 s and the target is on the familiar). </summary>
    public static bool Covering(in BstState s) => s.SinceSnarl < 45f && s.EnemyTargetsPet;

    /// <summary>
    ///     Keep a familiar alive. Above the swap line nothing happens. Below it, a healthier familiar (above the swap
    ///     line and 10+ points healthier) is blown in over it, after an 8 s grace from its summon. At or below the
    ///     critical line (half the swap line): any healthier ready horn, else Final Sting (wespe) or Parting Blow once a
    ///     just-blown horn had 2 s to land. A covering familiar stays unless critical. Returns the action (a horn,
    ///     Parting Blow or Tempered Release) with its reason.
    /// </summary>
    public static (uint ActionId, string Reason) TryPetSave(in BstState s, in BstSettings cfg, BeastmasterBeast? beast, List<string> declines)
    {
        if (!FamiliarOut(s))
            return (0, "");

        // Curtains for Rank 5 (49428 / 49429, King Ahriman): 6.0s cast that instantly KOs the familiar regardless of HP.
        // Parting Blow recalls the familiar safely before the cast resolves.
        if (BST_CrucibleData.CurtainsCasts.Contains(s.TargetCastId) && s.TargetCastRemaining is > 0.2f and <= 2.5f
            && s.Level >= LvPartingBlow && s.ReadyParting && s.CanWeave && !s.TargetDoNotAttack && !s.ProtectedNearTarget)
            return (BST.PartingBlow, "crucible:petsave-curtains");

        // Forward Guard (46864, Bone Knight): Parting Blow recalls the familiar before the guard lands.
        // Fires deterministically across the entire cast window (> 0.2 s) regardless of GCD/weave states
        // so the ~3 s retreat animation completes safely before the guard resolves.
        // Only with a resummon available (the horn-conservation rule every other recall follows): the guard is
        // turned by a familiar's Snarl, so a recall with every horn locked leaves nothing to turn it until a horn
        // returns about 90 s later. 2026-10-05 18:47 (Second Degree, encounter 2286): all three horns spent in the 70 s
        // before the cast, a healthy Ghost recalled 0.3 s into it, the guard stood 168 s and the run ended on the
        // board's Bleeding. 2026-09-27 22:48 was the same (no horn for 59 s). In every Forward Guard on record with
        // the familiar left out (9 of 2026-09-20/21, 1 of 2026-10-05) no familiar died and the Snarl ended the guard.
        if (BST_CrucibleData.ForwardGuardCasts.Contains(s.TargetCastId) && s.TargetCastRemaining > 0.2f
            && s.Level >= LvPartingBlow && s.ReadyParting && !s.TargetDoNotAttack && !s.ProtectedNearTarget)
        {
            if (ResummonAvailable(s, cfg))
                return (BST.PartingBlow, "crucible:petsave-guard");
            declines.Add("crucible:petsave-guard-no-resummon-horn");
        }

        var hp = s.PetHpPercent;
        if (hp <= 0f || hp > cfg.CruciblePetSwapHp)
            return (0, "");

        if (s.EnemyCount <= 1 && s.HasHostileTarget && s.TargetHpPercent <= EnemyDyingHp)
        {
            declines.Add("crucible:petsave-enemy-dying");
            return (0, "");
        }

        var critical = hp <= CriticalHp(cfg);
        if (!critical && Covering(s))
        {
            declines.Add("crucible:petsave-covering");
            return (0, "");
        }

        // 1. A horn over the familiar: HP kept, 1 s cast (not while moving).
        var swap = BestSwapHorn(s, cfg, hp, critical);
        if (swap != 0)
        {
            if (!critical && s.SinceSummon < SwapGraceSeconds)
                declines.Add("crucible:petsave-swap-grace");
            else if (s.IsMoving || s.PlayerIsCasting)
                declines.Add("crucible:petsave-swap-moving");
            else if (!(s.CanWeave || !s.GcdReady))
                declines.Add("crucible:petsave-swap-waiting-gcd");
            else if (s.SinceHornPress <= SummonSettleSeconds)
                declines.Add("crucible:petsave-horn-pending");
            else
                return (HornAction(swap), critical ? "crucible:petsave-swap-critical" : "crucible:petsave-swap");
        }

        if (!critical)
        {
            if (swap == 0)
                declines.Add("crucible:petsave-no-healthier-horn");
            return (0, "");
        }

        // 2. Critical with no horn landing: the familiar leaves on its own.
        if (s.SinceHornPress < HornRetrySeconds)
        {
            declines.Add("crucible:petsave-horn-retry");
            return (0, "");
        }
        if (s.Level < LvPartingBlow || !s.CanWeave)
            return (0, "");
        if (!s.HasHostileTarget || s.TargetDistance > 25f)
        {
            declines.Add("crucible:petsave-no-target");
            return (0, "");
        }
        if (s.TargetDoNotAttack || s.ProtectedNearTarget)
        {
            declines.Add("crucible:petsave-protected");
            return (0, "");
        }

        // Horn conservation: a recall exit with no resummon actually available strands the player
        // weaponless until a horn unlocks (~90 s). Keep the familiar out instead: it keeps covering and
        // fighting, and a familiar that falls is one familiar, while a character death ends the run
        // (Third Board 2026-09-26: last recall at 11% HP, boss 21%). When the round is ending anyway the
        // recall is free — the familiar is saved for the next battle.
        if (!ResummonAvailable(s, cfg) && !(s.EnemyCount > 0 && s.HighestEnemyHpPercent <= RoundEndingHp))
        {
            declines.Add("crucible:petsave-no-resummon-horn");
            return (0, "");
        }

        if (beast is { } b && (b.Release & BeastmasterReleaseTraits.Exit) != 0 && s.OneWithNature && s.ReadyTempered)
            return (BST.TemperedRelease, "crucible:petsave-finalsting");
        if (s.ReadyParting)
            return (BST.PartingBlow, "crucible:petsave-partingblow");

        declines.Add("crucible:petsave-partingblow-recast");
        return (0, "");
    }

    /// <summary>
    ///     A recall exit (Parting Blow / Final Sting) is safe only when a resummon will actually happen: a
    ///     ready horn, not the active one, whose familiar is above the critical line (the summon path refuses
    ///     a critical familiar while Parting Blow recasts). Without one the recall strands the player with no
    ///     familiar and no axe partner until a horn unlocks (~90 s) — the horn-exhaustion death on the Third
    ///     Board (2026-09-26 Catoblepas: last recall at 11% player HP, boss 21%, weaponless from there).
    /// </summary>
    public static bool ResummonAvailable(in BstState s, in BstSettings cfg)
    {
        var learned = LearnedHornSlots(s.Level);
        for (var slot = 1; slot <= learned; slot++)
        {
            if (slot == s.ActiveSlot || !HornReady(s, slot) || (s.SlotBeastsKnown && SlotBeast(s, slot) == 0))
                continue;
            if (SlotPetHp(s, slot) > CriticalHp(cfg))
                return true;
        }
        return false;
    }

    /// <summary>
    ///     A discretionary cycle exit (cycle-for-damage, not a HP-driven save) needs a ready horn whose familiar is above the swap line to
    ///     summon now AND another in reserve for a real familiar save: every horn press locks that horn for 90 s, so an exit that leaves
    ///     no ready horn means the next crisis has nothing to swap to and <see cref="TryPetSave"/> declines the recall too
    ///     (<c>petsave-no-resummon-horn</c>). First Master's battle 3, 2026-09-26 .. 09-30: in 8 of 8 familiar deaths a cycle exit and
    ///     resummon had spent the horns 4-37 s before the familiar crossed the swap line; 09-30 14:35:48 the only ready horn held a
    ///     42% familiar, it replaced a 100% one and fell in 34 s. With fewer than three horns learned the reserve shrinks with them.
    /// </summary>
    public static bool CycleExitKeepsReserve(in BstState s, in BstSettings cfg)
    {
        var learned = LearnedHornSlots(s.Level);
        var need = Math.Min(2, learned - 1);
        var healthy = 0;
        for (var slot = 1; slot <= learned; slot++)
        {
            if (slot == s.ActiveSlot || !HornReady(s, slot) || (s.SlotBeastsKnown && SlotBeast(s, slot) == 0))
                continue;
            if (SlotPetHp(s, slot) > cfg.CruciblePetSwapHp)
                healthy++;
        }
        return healthy >= need;
    }

    /// <summary> A ready horn whose familiar is healthier than the one out: above the swap line and 10+ points up (critical: any gain). </summary>
    private static int BestSwapHorn(in BstState s, in BstSettings cfg, float hp, bool critical)
    {
        var learned = LearnedHornSlots(s.Level);
        var best = 0;
        var bestHp = 0f;
        for (var slot = 1; slot <= learned; slot++)
        {
            if (slot == s.ActiveSlot || !HornReady(s, slot) || (s.SlotBeastsKnown && SlotBeast(s, slot) == 0))
                continue;
            var other = SlotPetHp(s, slot);
            var good = critical ? other >= hp + SwapMinGain : other > cfg.CruciblePetSwapHp && other >= hp + SwapMinGain;
            if (good && other > bestHp)
            {
                best = slot;
                bestHp = other;
            }
        }
        return best;
    }

    /// <summary>
    ///     Final Sting is due but the wespe is on a ready horn, not out: blow its horn over the familiar once that
    ///     familiar has spent One with Nature (nothing of its own is lost) and has been out 8 s. Without it a wespe on a
    ///     spare horn would never come out, because in the Crucible familiars leave only when their HP calls for it.
    /// </summary>
    public static (uint ActionId, string Reason) TryFinalStingSwap(in BstState s, in BstSettings cfg, BeastmasterBeast? beast, List<string> declines)
    {
        if (!FamiliarOut(s) || !s.HasHostileTarget || beast is not { } b || (b.Release & BeastmasterReleaseTraits.Exit) != 0)
            return (0, "");
        if (s.Level < LvTemperedRelease || s.TargetDoNotAttack || s.TargetInStance || s.TargetInvulnerable || !FinalStingDue(s, cfg) || FinalStingWasted(s))
            return (0, "");

        var slot = 0;
        var learned = LearnedHornSlots(s.Level);
        for (var i = 1; i <= learned; i++)
            if (i != s.ActiveSlot && HornReady(s, i) && IsExitBeast(s, i) && SlotPetHp(s, i) > CriticalHp(cfg))
                slot = i;
        if (slot == 0)
            return (0, "");

        if (s.OneWithNature || s.SinceSummon < SwapGraceSeconds || Covering(s))
            declines.Add("crucible:finalsting-swap-waiting");
        else if (s.IsMoving || s.PlayerIsCasting || s.SinceHornPress <= SummonSettleSeconds || !(s.CanWeave || !s.GcdReady))
            declines.Add("crucible:finalsting-swap-moving");
        else
            return (HornAction(slot), "crucible:finalsting-swap");
        return (0, "");
    }

    // ------------------------------------------------------------------ answer on demand

    /// <summary> Ability kinds in the order they are answered: an interrupt is gone in seconds, a buff or debuff lasts longer. </summary>
    private static readonly CrucibleNeeds[] AnswerKinds = [CrucibleNeeds.Interrupt, CrucibleNeeds.Dispel, CrucibleNeeds.Cleanse];

    private static string AnswerName(CrucibleNeeds kind) => kind switch
    {
        CrucibleNeeds.Interrupt => "interrupt",
        CrucibleNeeds.Dispel => "dispel",
        _ => "cleanse",
    };

    /// <summary>
    ///     A horn (never the active one — the active familiar is already out) whose beast is of <paramref name="kin"/>
    /// (or is <paramref name="row"/>); 0 = none. </summary>
    public static int AnswerSlot(in BstState s, BeastmasterKinType kin, int row = 0)
    {
        var learned = LearnedHornSlots(s.Level);
        for (var slot = 1; slot <= learned; slot++)
        {
            if (slot == s.ActiveSlot)
                continue;
            var beast = BST_Beasts.ByRow(SlotBeast(s, slot));
            if (beast is { } b && (row != 0 ? b.Row == row : b.Kin == kin))
                return slot;
        }
        return 0;
    }

    /// <summary>
    ///     The horn whose familiar answers <paramref name="kind"/> while the fight is calling for it right now, and is not
    ///     already answered. 0 = not live, already answered, or no horn carries an answer. Live means the fight's panel needs
    ///     it AND the thing is happening: an interruptible cast on the target, a dispellable buff on any enemy in reach (the
    ///     aim follows it once the Kinship is held, <see cref="DispelArmed"/>), a cleansable
    ///     debuff on the character. The vulture and the bat answer with their own Tempered Release, so they come first;
    ///     otherwise the Kinship is borrowed from the familiar that has it. The familiar that is already OUT only counts as
    ///     the answer while it can still give it: its One with Nature is up or its Kinship is already held (the 2026-10-02
    ///     evening: a Wavekin/Soulkin out with One with Nature spent answered nothing, and no swap was even considered).
    /// </summary>
    public static int LiveAnswerSlot(in BstState s, CrucibleNeeds kind, BeastmasterBeast? active)
    {
        if ((s.CrucibleNeeds & kind) == 0)
            return 0;

        var outKin = active is { } a ? a.Kin : BeastmasterKinType.None;
        var outRow = active is { } b ? b.Row : 0;
        switch (kind)
        {
            case CrucibleNeeds.Interrupt:
                if (!s.HasHostileTarget || s.TargetDoNotAttack || !s.TargetInterruptible
                    || HeldKin(s) == BeastmasterKinType.Soulkin || (outKin == BeastmasterKinType.Soulkin && s.OneWithNature))
                    return 0;
                return AnswerSlot(s, BeastmasterKinType.Soulkin);
            case CrucibleNeeds.Dispel:
            {
                if (!s.HasHostileTarget || s.TargetDoNotAttack || !(s.TargetHasDispellableBuff || s.EnemyHasDispellableBuff)
                    || (outRow == VultureRow && s.OneWithNature) || HeldKin(s) == BeastmasterKinType.Wavekin
                    || (outKin == BeastmasterKinType.Wavekin && s.OneWithNature))
                    return 0;
                var vulture = AnswerSlot(s, BeastmasterKinType.None, VultureRow);
                return vulture != 0 ? vulture : AnswerSlot(s, BeastmasterKinType.Wavekin);
            }
            default:
            {
                if (!s.PlayerHasCleansableDebuff
                    || (outRow == BST_CrucibleData.BatRow && s.OneWithNature) || HeldKin(s) == BeastmasterKinType.Ashkin
                    || (outKin == BeastmasterKinType.Ashkin && s.OneWithNature))
                    return 0;
                var bat = AnswerSlot(s, BeastmasterKinType.None, BST_CrucibleData.BatRow);
                return bat != 0 ? bat : AnswerSlot(s, BeastmasterKinType.Ashkin);
            }
        }
    }

    /// <summary> Every horn that answers something live (no familiar out, so nothing counts as already answered). </summary>
    private static HashSet<int> LiveAnswerSlots(in BstState s)
    {
        var slots = new HashSet<int>();
        if (!s.InCombat)
            return slots;
        foreach (var kind in AnswerKinds)
            if (LiveAnswerSlot(s, kind, null) is var slot and not 0)
                slots.Add(slot);
        return slots;
    }

    /// <summary>
    ///     The fight is calling for an ability (interruptible cast, dispellable buff, cleansable debuff) and the familiar out
    ///     does not answer it: blow the horn of the one that does, like a familiar save (healthy, not moving, between GCDs,
    ///     the last horn settled). The ability itself then goes out through <see cref="TryDispel"/>, <see cref="TryCleanse"/>,
    ///     the Borrow rule and the interrupt rule. Without this the familiar that was picked for the fight stays on its horn
    ///     and the rotation logs "dispel-unavailable" until the buff has done its damage (board 4 Strix Piece, 2026-10-01).
    ///     When the answering familiar is already out but cannot answer (interrupt: Soulkin out with One with Nature
    ///     spent, Soul Crush not held, no other Soulkin horn ready) that limit is named in the declines
    ///     (<c>crucible:answer-interrupt-cannot-rearm</c>) instead of passing silently (12 of 41 heals on the
    ///     Natural Nurture fight finished casting that way, 2026-10-02).
    /// </summary>
    public static (uint ActionId, string Reason) TryAnswerSwap(in BstState s, in BstSettings cfg, BeastmasterBeast? beast, List<string> declines)
    {
        if (!FamiliarOut(s) || s.CrucibleNeeds == CrucibleNeeds.None)
            return (0, "");

        foreach (var kind in AnswerKinds)
        {
            var slot = LiveAnswerSlot(s, kind, beast);
            if (slot == 0 || slot == s.ActiveSlot)
                continue;

            var name = AnswerName(kind);
            if (!HornReady(s, slot))
                declines.Add($"crucible:answer-{name}-horn-not-ready");
            else if (SlotPetHp(s, slot) <= CriticalHp(cfg))
                declines.Add($"crucible:answer-{name}-familiar-hurt");
            else if (Covering(s) && s.PlayerHpPercent < 50f)
                declines.Add($"crucible:answer-{name}-covering");
            else if (s.IsMoving || s.PlayerIsCasting || !(s.CanWeave || !s.GcdReady) || s.SinceHornPress <= SummonSettleSeconds)
            {
                declines.Add($"crucible:answer-{name}-waiting");
                return (0, "");
            }
            else
                return (HornAction(slot), $"crucible:answer-{name}-slot{slot}");
        }

        // The engine limit, said out loud: an interruptible cast is live, Soul Crush is not held, the Soulkin is
        // the familiar out and its One with Nature (the only thing that can re-borrow Soul Crush) is spent, and
        // no other ready horn carries a Soulkin. Nothing can answer this cast; the log should say so.
        if ((s.CrucibleNeeds & CrucibleNeeds.Interrupt) != 0 && s.HasHostileTarget && !s.TargetDoNotAttack && s.TargetInterruptible
            && HeldKin(s) != BeastmasterKinType.Soulkin && !InterruptArmed(s, cfg)
            && beast is { Kin: BeastmasterKinType.Soulkin } && !s.OneWithNature
            && AnswerSlot(s, BeastmasterKinType.Soulkin) == 0)
            declines.Add("crucible:answer-interrupt-cannot-rearm");

        // The same limit for the other two answers: the thing is live, the answering familiar is out with its
        // One with Nature spent (its release was the answer), the kinship is not held, and no other horn carries
        // an answerer. Nothing can answer this one; the log must say so instead of passing silently (board 5,
        // 2026-10-03: the bat's release went to a discretionary borrow 1.4 s after its summon, the Paralysis ran
        // 49 s later and the character died behind it with nothing in the log).
        if ((s.CrucibleNeeds & CrucibleNeeds.Cleanse) != 0 && s.PlayerHasCleansableDebuff
            && HeldKin(s) != BeastmasterKinType.Ashkin
            && beast is { Row: BST_CrucibleData.BatRow } && !s.OneWithNature
            && AnswerSlot(s, BeastmasterKinType.None, BST_CrucibleData.BatRow) == 0
            && AnswerSlot(s, BeastmasterKinType.Ashkin) == 0)
            declines.Add("crucible:answer-cleanse-cannot-rearm");
        if ((s.CrucibleNeeds & CrucibleNeeds.Dispel) != 0 && s.HasHostileTarget && !s.TargetDoNotAttack && s.TargetHasDispellableBuff
            && HeldKin(s) != BeastmasterKinType.Wavekin
            && beast is { Row: VultureRow } && !s.OneWithNature
            && AnswerSlot(s, BeastmasterKinType.None, VultureRow) == 0
            && AnswerSlot(s, BeastmasterKinType.Wavekin) == 0)
            declines.Add("crucible:answer-dispel-cannot-rearm");

        return (0, "");
    }

    /// <summary> Dispel an enemy buff: the vulture's Bloodcurdling Caw, else Quelling Wave (a GCD spell, allowed mid-combo). </summary>
    public static (uint ActionId, string Reason) TryDispel(in BstState s, BeastmasterBeast? beast, List<string> declines)
    {
        if (!s.HasHostileTarget || !s.TargetHasDispellableBuff || s.TargetDoNotAttack)
            return (0, "");

        // A carrier in a counter stance is held on (no GCD goes out), so the GCD sits idle and a weave window never opens:
        // with the GCD ready the Caw goes out anyway (a calm carrier keeps rolling its GCD and weaves in the next window).
        if (beast is { Row: VultureRow } && FamiliarOut(s) && s.OneWithNature && s.ReadyTempered && (s.CanWeave || (s.TargetInStance && s.GcdReady))
            && s.SinceSummon >= 0.8f && s.TargetDistance <= 25f && !s.ProtectedNearTarget)
            return (BST.TemperedRelease, "crucible:dispel-caw");

        if (s.KinshipHeld && s.BeastModeResolved == BST.QuellingWave && s.ReadyBeastMode && s.GcdReady && s.TargetDistance <= 30f)
            return (BST.QuellingWave, "crucible:dispel-quellingwave");

        declines.Add("crucible:dispel-unavailable");
        return (0, "");
    }

    /// <summary> Cleanse the character: Scouring Ash (Ashkin), or the bat's Ultrasonics while its One with Nature is up. </summary>
    public static (uint ActionId, string Reason) TryCleanse(in BstState s, BeastmasterBeast? beast)
    {
        if (!s.PlayerHasCleansableDebuff || !s.CanWeave)
            return (0, "");
        if (s.KinshipHeld && s.BeastModeResolved == BST.ScouringAsh && s.ReadyBeastMode)
            return (BST.ScouringAsh, "crucible:cleanse-scouringash");
        if (beast is { Row: BST_CrucibleData.BatRow } && FamiliarOut(s) && s.OneWithNature && s.ReadyTempered && s.SinceSummon >= 0.8f)
            return (BST.TemperedRelease, "crucible:cleanse-ultrasonics");
        return (0, "");
    }

    /// <summary>
    ///     Snarl (the familiar takes the aggro and covers the character) / Challenge (the character takes it back).
    ///     Survival: Snarl on Directional Parry, or when the character is at 40% or lower while the familiar (50%+) can
    ///     carry 15 s of the character's recent damage intake and is not a wespe about to Final Sting. At 25% or lower
    ///     the character's survival is the run: the familiar is snarled in whatever its HP (a 30% familiar holding the
    ///     enemy still beats the character holding it — Third Board 2026-09-26: 11% character, covering familiar at
    ///     critical, nothing pressed). Snarl ahead of a known tankbuster (Snarl alone: the Parting Blow that used to follow it
    ///     hands the hit back to the character, measured 2026-10-06) only when the familiar survives the recorded hit twice over
    ///     (<see cref="FamiliarCanTakeTankbuster"/>). Challenge when the parry ends, or when the familiar is at 30% or lower and the character 60%+.
    ///     Frontal-cleave-auto bosses (siren, Guttler, Pas de Seul, Lauda): their cone autos hit the familiar too, so
    ///     Snarl only to cover a known tankbuster cast (pet 50%+), and Challenge the aggro back off the familiar whenever
    ///     no such cast is up and the player is 50%+ — a familiar that holds aggro through the autos is ground down fight
    ///     long (Third Board elite 2026-09-25: pet 44% → 20% under cleave autos while the player sat at 8%).
    ///     Score mode: Challenge whenever the target is on the familiar; Snarl only for the tankbuster dodge.
    /// </summary>
    public static (uint ActionId, string Reason) ChooseAggro(in BstState s, in BstSettings cfg)
    {
        if (!s.HasHostileTarget || !FamiliarOut(s) || s.TargetDoNotAttack)
            return (0, "");

        var tankbuster = s.TargetCastId != 0 && BST_CrucibleData.Tankbusters.Contains(s.TargetCastId);
        var cleave = BST_CrucibleData.CleaveAutoBosses.Contains(s.TargetNameId);
        // The familiar covers the character with Snarl alone; the Parting Blow that used to follow it is a dormant opt-in
        // (BstSettings.CrucibleTankbusterParting): measured, it hands the hit back to the character.
        var tankbusterSetUp = cfg.CrucibleSnarlParting && tankbuster && s.ReadySnarl && !s.EnemyTargetsPet
                              && (cfg.CrucibleTankbusterParting
                                  ? s.TargetCastRemaining > cfg.CrucibleSnarlPartingLead + 1f && s.ReadyParting
                                  : s.TargetCastRemaining + BST_CrucibleData.TankbusterHitDelay(s.TargetCastId) > TankbusterSnarlMinLeadSeconds
                                    && FamiliarCanTakeTankbuster(s));

        if (cfg.CrucibleScoreMode)
        {
            if (tankbusterSetUp)
                return (BST.Snarl, "aggro:snarl-tankbuster");
            if (s.ReadyChallenge && s.EnemyTargetsPet && !(tankbuster && s.SinceSnarl < 45f))
                return (BST.Challenge, "aggro:challenge-score");
            return (0, "");
        }

        if (s.ReadySnarl && !s.EnemyTargetsPet && s.EnemyTargetsPlayer && s.SinceHornPress > SummonSettleSeconds)
        {
            if (s.TargetHasParry && s.PetHpPercent >= 50f)
                return (BST.Snarl, "aggro:snarl-parry");
            if (tankbusterSetUp)
                return (BST.Snarl, "aggro:snarl-tankbuster");

            // Cleave-auto boss: the familiar covers the known hard cast (Song of Torment, Thunderbolt),
            // then Challenge below takes the aggro back once the cast resolves.
            if (cleave && tankbuster && s.PetHpPercent >= 50f)
                return (BST.Snarl, "aggro:snarl-cleave-tankbuster");

            var lastResort = s.PlayerHpPercent is > 0f and <= 25f;
            var stingDue = BST_Beasts.ByRow(SlotBeast(s, s.ActiveSlot)) is { } b
                           && (b.Release & BeastmasterReleaseTraits.Exit) != 0 && FinalStingDue(s, cfg);

            // Last resort: whatever the familiar's HP, the alternative is the character dying.
            if (lastResort && !stingDue)
                return (BST.Snarl, "aggro:snarl-last-resort");

            if (s.PlayerHpPercent is > 0f and <= 40f && s.PetHpPercent >= 50f)
            {
                var carries = s.PetHp > s.PlayerIntakePerSecond * 15f;
                if (carries && !stingDue)
                    return (BST.Snarl, "aggro:snarl-player-low");
            }
        }

        if (s.ReadyChallenge && s.EnemyTargetsPet)
        {
            if (s.ParryJustEnded && !s.TargetHasParry)
                return (BST.Challenge, "aggro:challenge-parry-ended");

            // Cleave-auto boss: between hard casts the player holds the boss so the cone autos stop
            // hitting the familiar (not while a covered tankbuster cast is still up, and only while the
            // player is healthy enough to take the autos; below 50% the low-player rules above own it).
            if (cleave && !tankbuster && s.PlayerHpPercent >= 50f && s.PetHpPercent > 0f
                && s.SinceHornPress > SummonSettleSeconds)
                return (BST.Challenge, "aggro:challenge-cleave-auto");

            if (s.PetHpPercent is > 0f and <= 30f && s.PlayerHpPercent >= 60f && s.SinceHornPress > SummonSettleSeconds)
                return (BST.Challenge, "aggro:challenge-pet-low");
        }

        return (0, "");
    }

    /// <summary>
    ///     Final Sting is due: the target is at or below the execute line (halved with 2+ enemies), or the target's
    ///     Physical Vulnerability Up is in its last 10 s.
    /// </summary>
    public static bool FinalStingDue(in BstState s, in BstSettings cfg) =>
        s.TargetHpPercent <= (s.EnemyCount >= 2 ? cfg.CrucibleFinalStingHp / 2f : cfg.CrucibleFinalStingHp)
        || s.TargetVulnerabilityRemaining is > 0f and < 10f;

    /// <summary> Final Sting would be wasted: the target dies within 3 s anyway. </summary>
    public static bool FinalStingWasted(in BstState s) => s.TargetTimeToDeath is > 0f and < 3f;

    /// <summary>
    ///     Snarl -> Parting Blow: a known tankbuster is about to land (within the lead time), the familiar took the
    ///     aggro with Snarl in the last 45 s (Cover), and Parting Blow is ready. Guides press it 1-2 s before the cast
    ///     ends: earlier and the hit lands on the character, later and the familiar eats it.
    /// </summary>
    public static bool SnarlPartingNow(in BstState s, in BstSettings cfg) =>
        SnarlPartingWindow(s, cfg) && s.SinceSnarl < 45f;

    // ------------------------------------------------------------------ survival (2026-10-03 deaths)

    /// <summary> Out of combat on a board: top the character up to this line before the next fight. Entry below 40% HP died 8.8x the 80%+ rate over 7 days. </summary>
    public const float BoardHealHpPercent = 80f;

    /// <summary> In combat with no cast to react to: the line where a heal is pressed anyway (Burns / cone attrition deaths at 3% HP). </summary>
    public const float PanicHealHpPercent = 25f;

    /// <summary> A registered tankbuster with no Snarl -> Parting cover armed gets the potion at or below this line (per-hit damage unknown for most ids). </summary>
    public const float TankbusterGuardHpPercent = 35f;

    /// <summary> A held skin (Scaleskin / Beastskin / Vileskin) is offered this close to the hit: potions heal now, skins last. </summary>
    public const float SkinGuardLeadSeconds = 8f;

    /// <summary>
    ///     The heal-potion offer, strongest grade first: the first id that is HELD (the XBMContentsMainHUD stock walk),
    ///     was not refused by an earlier dead press, and whose recast is clear. Pure over the sets the live layer
    ///     supplies; 0 when nothing qualifies. The shipped picker took the first grade whose RECAST was clear, which
    ///     says nothing about stock: with no G4 held it pressed the dead 46962 every time while a G1 sat in the bag
    ///     (Durga held G1 only, Lauda G2), and its blanket offer throttle starved a need that appeared just after an
    ///     idle offer.
    /// </summary>
    public static uint PickHealPotion(IReadOnlyList<uint> gradesStrongestFirst, IReadOnlySet<uint> held, IReadOnlySet<uint> refused, Func<uint, bool> recastClear)
    {
        foreach (var id in gradesStrongestFirst)
        {
            if (!held.Contains(id) || refused.Contains(id))
                continue;
            if (recastClear(id))
                return id;
        }
        return 0;
    }

    /// <summary> What became of a heal-potion use request. </summary>
    public enum HealPressOutcome
    {
        Pending,
        Landed,
        Dead,
    }

    /// <summary> A HP rise of at least this share of max HP after a request counts as the potion having landed. </summary>
    public const float HealLandedHpGainPercent = 3f;

    /// <summary>
    ///     Resolve a heal-potion request from evidence of the heal itself, never from the plugin's own stamps: the potion's recast is
    ///     running, or HP rose by <see cref="HealLandedHpGainPercent"/>. The shipped tracker counted any use stamp as landed, and the
    ///     stamp is written when the game CLIENT accepts a press, so 57 presses the server never honoured (the potion action pressed
    ///     as an action, not drunk through the item menu) all read as landed and the same grade was re-pressed for twelve minutes.
    ///     Nothing by the grace means dead, however recently the same id was used before.
    /// </summary>
    public static HealPressOutcome ResolveHealPress(long msSincePress, bool recastRunning, float hpGainPercent, long graceMs)
    {
        if (recastRunning || hpGainPercent >= HealLandedHpGainPercent)
            return HealPressOutcome.Landed;
        return msSincePress >= graceMs ? HealPressOutcome.Dead : HealPressOutcome.Pending;
    }

    /// <summary>
    ///     The held heal potions in the Crucible item HUD as action -> slot index (the slot the item menu opens on): a slot counts when
    ///     its held byte is set and its XBMItem row is a heal potion row; the first slot wins when a grade sits in two.
    /// </summary>
    public static Dictionary<uint, int> HeldHealSlots(IReadOnlyList<(bool Held, uint Row)> slots, IReadOnlyDictionary<uint, uint> rowToAction)
    {
        var held = new Dictionary<uint, int>(2);
        for (var slot = 0; slot < slots.Count; slot++)
        {
            var (isHeld, row) = slots[slot];
            if (isHeld && row != 0 && rowToAction.TryGetValue(row, out var action))
                held.TryAdd(action, slot);
        }
        return held;
    }

    /// <summary> The next move of the item-menu use: open the slot, wait for the context menu, pick its first entry. </summary>
    public enum ItemMenuStep
    {
        Wait,
        Choose,
        GiveUp,
    }

    /// <summary>
    ///     A context menu that is up inside the wait is the one the open asked for (take its first entry); past the wait nothing we
    ///     opened can still be showing, so give up and never touch a menu somebody else raised.
    /// </summary>
    public static ItemMenuStep NextItemMenuStep(long msSinceOpen, bool menuReady, long waitMs)
    {
        if (msSinceOpen >= waitMs)
            return ItemMenuStep.GiveUp;
        return menuReady ? ItemMenuStep.Choose : ItemMenuStep.Wait;
    }

    /// <summary>
    ///     Survival policy (task tasks-20261003-crucible-survivability-entry-hp-01, 2026-10-03): the character kept dying from HP carried
    ///     into a fight (five of the day's ten deaths entered below 60%) and from single aimed hits above remaining HP (Atomic Ray: 3,998 /
    ///     4,782 on a 30% character, nothing pressed). The only heal actor, AutoDuty's Crucible Items, under-heals on the board (60% line) and
    ///     goes silent when the HUD stock runs dry (the Durga fight: 126 s below its 40% line, zero uses). PURE, like the rest of this file.
    ///     <para>Board heal: out of combat, below <see cref="BoardHealHpPercent"/>, press the best-grade ready heal potion action directly
    ///     (no HUD menu dance; the live layer offers only grades with stock in the HUD and steps down on a dead press).</para>
    ///     <para>Guard: a measured <see cref="BST_CrucibleData.HeavyCast"/> of kind CastOnly / PartyWide — it hits the character whatever the
    ///     familiar does — with remaining HP under its <see cref="CrucibleHeavyCast.MaxOnCharacter"/> (scaled to the board's degree) gets the best available answer: the held
    ///     skin inside <see cref="SkinGuardLeadSeconds"/> of the hit (free, 90 s), else the potion at any point in the cast. A registered
    ///     tankbuster gets the potion only when no Snarl -> Parting cover is armed and HP is at or below <see cref="TankbusterGuardHpPercent"/>
    ///     (the dodge, not the stock, is the first answer there).</para>
    ///     <para>Panic: in combat at or below <see cref="PanicHealHpPercent"/> with no cast to react to.</para>
    /// </summary>
    public static (uint ActionId, string Reason) TrySurvival(in BstState s, in BstSettings cfg, List<string> declines)
    {
        if (!cfg.CrucibleSurvival)
            return (0, "");

        var potion = s.ReadyHealPotion;

        if (s.TargetCastId != 0 && s.TargetCastRemaining + BST_CrucibleData.TankbusterHitDelay(s.TargetCastId) > 0.2f)
        {
            var heavy = BST_CrucibleData.HeavyCast(s.TargetCastId);

            // A measured cast that lands on the character regardless of enmity (CastOnly: mitigation is the only answer;
            // PartyWide: it hits both) while the character's remaining HP is under the largest logged hit.
            // The recorded hit is a Standard number: at First Degree the same cast hits about 1.75x harder (CrucibleDegree.CharacterHitScale).
            if (heavy is { Kind: CrucibleHitKind.CastOnly or CrucibleHitKind.PartyWide, MaxOnCharacter: > 0 } row
                && s.PlayerHp > 0f && s.PlayerHp < row.MaxOnCharacter * CrucibleDegree.CharacterHitScale(s.CrucibleDegreeKnown, s.CrucibleDegreeLevel))
            {
                if (s.TargetCastRemaining <= SkinGuardLeadSeconds && s.KinshipHeld && s.ReadyBeastMode && s.CanWeave
                    && s.BeastModeResolved is BST.Scaleskin or BST.Beastskin or BST.Vileskin)
                    return (s.BeastModeResolved, "crucible:raidwide-guard");
                if (potion != 0)
                    return (potion, "crucible:raidwide-guard");
                declines.Add("crucible:raidwide-guard-none");
            }
            else if ((heavy is { Kind: CrucibleHitKind.Tankbuster } || BST_CrucibleData.Tankbusters.Contains(s.TargetCastId))
                     && !TankbusterCoverArmed(s, cfg)
                     && s.PlayerHpPercent is > 0f and <= TankbusterGuardHpPercent)
            {
                if (potion != 0)
                    return (potion, "crucible:raidwide-guard");
                declines.Add("crucible:raidwide-guard-none");
            }
        }

        if (s.InCombat && s.PlayerHpPercent is > 0f and <= PanicHealHpPercent && potion != 0)
            return (potion, "crucible:panic-heal");

        if (!s.InCombat && s.PlayerHpPercent is > 0f and < BoardHealHpPercent && potion != 0 && !s.PlayerIsCasting)
            return (potion, "crucible:board-heal");

        return (0, "");
    }

    /// <summary> A familiar already holding the enemy, or a cover Snarl still able to go in (or the dormant Snarl -> Parting dodge), owns a registered tankbuster. </summary>
    private static bool TankbusterCoverArmed(in BstState s, in BstSettings cfg) =>
        s.EnemyTargetsPet
        || (cfg.CrucibleSnarlParting && s.HasHostileTarget && FamiliarOut(s)
            && !s.TargetDoNotAttack && !s.ProtectedNearTarget && s.TargetDistance <= 25f
            && (cfg.CrucibleTankbusterParting
                ? s.ReadyParting && s.SinceSnarl < 45f
                : s.ReadySnarl && FamiliarCanTakeTankbuster(s)));

    /// <summary> A cover Snarl for a registered tankbuster is worth pressing while at least this much time is left before the hit lands. </summary>
    public const float TankbusterSnarlMinLeadSeconds = 0.5f;

    /// <summary>
    ///     The margin 1.0.4.287 shipped, kept where the board's degree is not measured (Second, Third) or not read: a cover Snarl needs the
    ///     familiar to hold this many times the largest hit on a familiar the logs recorded for the cast. Where the degree is known and measured the
    ///     recorded hit is scaled to that degree and the margin is <see cref="CrucibleDegree.CoverMargin"/> (1.25).
    /// </summary>
    public const float TankbusterFamiliarHitMargin = 2f;

    /// <summary>
    ///     The familiar can take this tankbuster's hit: its HP is at least <see cref="TankbusterFamiliarHitMargin"/> times the largest hit on a
    ///     familiar the logs measured for the cast (<see cref="CrucibleHeavyCast.MaxOnFamiliar"/>), scaled to the board's degree
    ///     (<see cref="CrucibleDegree.FamiliarHitScale"/>: the recorded numbers are Standard, First Degree hits a familiar about twice as hard) and
    ///     with the margin the degree allows (<see cref="CrucibleDegree.CoverMargin"/>: 1.25 where Standard / First is known, 2.0 otherwise). A cast
    ///     with no measured hit on a familiar is never covered (the character takes it, as before the Snarl step existed). First Degree Sweeping
    ///     Evisceration put 3,300-3,900 on a 3,492 HP familiar and knocked it out in 11 of 15 casts, where at Standard the same cast did 1,224-2,616
    ///     and the Snarl alone was safe. A character already under the cast's measured hit on a character (scaled the same way,
    ///     <see cref="CrucibleDegree.CharacterHitScale"/>) is covered whatever the familiar's HP (the alternative is the death).
    /// </summary>
    public static bool FamiliarCanTakeTankbuster(in BstState s)
    {
        if (s.PetHp <= 0f)
            return false;
        var heavy = BST_CrucibleData.HeavyCast(s.TargetCastId);
        if (heavy is { MaxOnCharacter: > 0 } row
            && s.PlayerHp > 0f && s.PlayerHp < row.MaxOnCharacter * CrucibleDegree.CharacterHitScale(s.CrucibleDegreeKnown, s.CrucibleDegreeLevel))
            return true;
        if (heavy is not { MaxOnFamiliar: > 0 } fam)
            return false;
        var hit = fam.MaxOnFamiliar * HitsPerCast(s.TargetCastId) * CrucibleDegree.FamiliarHitScale(s.CrucibleDegreeKnown, s.CrucibleDegreeLevel);
        var margin = CrucibleDegree.CoverMargin(s.CrucibleDegreeKnown, s.CrucibleDegreeLevel);
        return s.PetHp >= hit * margin;
    }

    /// <summary>
    ///     A discretionary recall (the cycle exit, the pack Parting Blow) would hand a registered tankbuster back to the character: the familiar
    ///     holds the enemy, the hit has not landed yet, and the familiar can take it (the logs show it surviving the hit at this degree, or show
    ///     nothing on a familiar for the cast, so nothing says it would fall). A recall takes the familiar's enmity with it: in 47 recalls with no
    ///     Snarl while the familiar held the enemy and a tankbuster landed within 6 s, 36 landed on the character (17 and 11 by a stricter count), where a familiar left alone took
    ///     76 of 94 (81%). A recall to save a low familiar, the burst and Final Sting never ask this.
    /// </summary>
    public static bool RecallWouldHandBackTankbuster(in BstState s) =>
        s.EnemyTargetsPet && FamiliarOut(s)
        && s.TargetCastId != 0 && BST_CrucibleData.Tankbusters.Contains(s.TargetCastId)
        && s.TargetCastRemaining + BST_CrucibleData.TankbusterHitDelay(s.TargetCastId) > 0.2f
        && (BST_CrucibleData.HeavyCast(s.TargetCastId) is not { MaxOnFamiliar: > 0 } || FamiliarCanTakeTankbuster(s));

    /// <summary> Grim Fate (48730) lands as a five-hit string; the recorded hit sizes are one hit. Every other registered tankbuster is one hit. </summary>
    private static float HitsPerCast(uint castId) => castId == 48730 ? 5f : 1f;

    /// <summary>
    ///     The Parting Blow window of a known tankbuster is open (the hit lands within the lead time), whether or not Snarl set it up.
    ///     Logged in the shadow field while the option is off, so a run can be graded (it had never been evaluated: 0 decisions).
    /// </summary>
    public static bool SnarlPartingWindow(in BstState s, in BstSettings cfg) =>
        s.HasHostileTarget && FamiliarOut(s) && s.ReadyParting && !s.TargetDoNotAttack && !s.ProtectedNearTarget
        && s.TargetCastId != 0 && BST_CrucibleData.Tankbusters.Contains(s.TargetCastId)
        && s.TargetCastRemaining + BST_CrucibleData.TankbusterHitDelay(s.TargetCastId) is > 0.2f and var landsIn
        && landsIn <= cfg.CrucibleSnarlPartingLead
        && s.TargetDistance <= 25f;

    // ------------------------------------------------------------------ tactics: add packs, windows, Self-destruct

    /// <summary> What a Tempered Release is held for: an add pack (AoE release) or the Ymir's shell break (stun / bind release). </summary>
    public enum WindowKind { None, Pack, Shell }

    /// <summary> The window the guide names for this battle that the active familiar's release can serve; <see cref="WindowKind.None"/> otherwise. </summary>
    public static WindowKind WindowFor(in BstState s, BeastmasterBeast? beast)
    {
        if (beast is not { } b)
            return WindowKind.None;
        if (BST_CrucibleData.IsShellBattle(s.CrucibleBoard, s.CrucibleBattle))
            return BST_CrucibleData.ReleaseBinds(b) ? WindowKind.Shell : WindowKind.None;
        if (BST_CrucibleData.PackBattles.Contains((s.CrucibleBoard, s.CrucibleBattle)))
            return (b.Release & BeastmasterReleaseTraits.AoE) != 0 ? WindowKind.Pack : WindowKind.None;
        return WindowKind.None;
    }

    /// <summary> The pack is on the field (3+ hostile enemies), or the Ymir's shell just broke with the Ymir targeted. </summary>
    public static bool WindowOpen(in BstState s, WindowKind kind) => kind switch
    {
        WindowKind.Pack => s.EnemyCount >= BST_CrucibleData.PackMinEnemies,
        WindowKind.Shell => s.ShellJustBroke && BST_CrucibleData.ShellTargets.Contains(s.TargetNameId),
        _ => false,
    };

    /// <summary>
    ///     One tactic decision: an action, why, what to log in the shadow field (the window the option would have used while it is off)
    ///     and whether the release is being held for a window (the caller then skips its own Tempered Release).
    /// </summary>
    public readonly record struct TacticResult(uint ActionId, string Reason, string Shadow, bool HoldRelease)
    {
        public static readonly TacticResult None = new(0, "", "", false);
    }

    /// <summary>
    ///     Crucible tactics that cost the familiar's Tempered Release or a Parting Blow. Priced from the recorded runs
    ///     (task tasks-20261002-crucible-add-pack-and-window-tactics-01):
    ///     <list type="bullet">
    ///         <item>Self-destruct (4.7, 48766): ON. A 20 s deadline; in 2 of 2 runs the telemetry stops 3 s before it ends with the Golem at 9% / 12%.
    ///         The release goes first; once it is spent, a Parting Blow brings the next horn's familiar and its own release when the cast has
    ///         time left, a ready horn waits, and the Golem will not die first anyway. The fight ends here, so the 90 s horn lock is no price.</item>
    ///         <item>Add packs and the Ymir's shell break: the release is held until the window opens (the option, off): in the recorded runs the release had
    ///         already gone off before 85-97% of the pack spikes, and pack clear times with and without an AoE press do not separate (3.1: 12 s with, 15 s
    ///         without; 3.6: 22 s with, 18 s without), so no run shows it pays. While off, the window is logged in the shadow field.</item>
    ///         <item>Parting Blow on the pack (the option, off): behind the same gates as a cycle exit (stay time, resummon, reserve).</item>
    ///     </list>
    /// </summary>
    public static TacticResult TryTactic(in BstState s, in BstSettings cfg, BeastmasterBeast? beast, ReleasePlan plan, List<string> declines)
    {
        var releaseSpendable = s.OneWithNature && s.ReadyTempered && plan == ReleasePlan.Use && s.SinceSummon >= 0.8f && s.TargetDistance <= 25f;

        // ---- Self-destruct burst
        if (s.TargetCastId != 0 && BST_CrucibleData.SelfDestructCasts.Contains(s.TargetCastId)
            && !s.TargetDoNotAttack && !s.TargetInvulnerable)
        {
            if (releaseSpendable)
                return new(BST.TemperedRelease, "crucible:burst-tempered", "", false);

            var releaseDone = !(s.OneWithNature && plan == ReleasePlan.Use);
            if (releaseDone && s.ReadyParting && s.SinceTempered >= 2f && s.SinceBorrow >= 1f
                && s.TargetCastRemaining >= BST_CrucibleData.BurstPartingMinCastLeft && s.TargetDistance <= 25f
                && !(s.TargetTimeToDeath > 0f && s.TargetTimeToDeath < s.TargetCastRemaining - 2f)
                && ResummonAvailable(s, cfg))
                return new(BST.PartingBlow, "crucible:burst-parting", "", false);
            return TacticResult.None;
        }

        var kind = WindowFor(s, beast);
        if (kind == WindowKind.None)
            return TacticResult.None;
        var open = WindowOpen(s, kind);

        // ---- the held release: goes on the pack / the Ymir when the window opens
        var hold = !open && s.OneWithNature && s.ReadyTempered && plan == ReleasePlan.Use && !s.TargetDoNotAttack
                   && s.SinceSummon < BST_CrucibleData.WindowHoldSeconds && s.PetHpPercent > cfg.CruciblePetSwapHp;
        var shadow = "";
        if (hold && !cfg.CruciblePackWindow)
        {
            shadow = "crucible:pack-hold-off";
            hold = false;
        }
        if (cfg.CruciblePackWindow)
        {
            if (open && releaseSpendable)
                return new(BST.TemperedRelease, kind == WindowKind.Shell ? "crucible:ymir-bind" : "crucible:pack-release", "", false);
            if (hold)
            {
                declines.Add("crucible:hold-release-window");
                return new(0, "", "", true);
            }
        }

        // ---- Parting Blow on the pack, once its release has gone off
        if (kind == WindowKind.Pack && open && !s.OneWithNature && s.ReadyParting && s.SinceSummon >= cfg.MinFamiliarStaySeconds
            && s.SinceTempered >= 2f && s.SinceBorrow >= 1f && s.TargetDistance <= 25f
            && !(s.TargetDoNotAttack || s.TargetInStance || s.TargetInvulnerable || s.ProtectedNearTarget)
            && !(s.EnemyCount > 0 && s.HighestEnemyHpPercent <= RoundEndingHp)
            && ResummonAvailable(s, cfg) && CycleExitKeepsReserve(s, cfg))
        {
            if (RecallWouldHandBackTankbuster(s))
                declines.Add("crucible:pack-tankbuster-up");
            else if (cfg.CruciblePackWindow)
                return new(BST.PartingBlow, "crucible:pack-parting", "", false);
            else if (shadow.Length == 0)
                shadow = "crucible:pack-parting-off";
        }

        return new(0, "", shadow, false);
    }

    // ------------------------------------------------------------------ auto-targeting

    /// <summary>
    ///     One enemy auto-targeting could pick. <see cref="Avoid"/> marks a counter stance that may be relaxed when
    ///     nothing else is up; <see cref="DamageImmune"/> is an absolute exclusion.
    /// </summary>
    public readonly record struct TargetCandidate(uint NameId, float HpPercent, bool Avoid, bool DamageImmune = false, bool CastInterruptible = false, uint CastId = 0, bool Dispellable = false, bool DispellableProven = false);

    /// <summary>
    ///     Which candidates may take a hit when the enemy <see cref="AllowedTargets"/> chose is out of the action's reach:
    ///     the same exclusions (never eggs / morphos or damage-immune enemies; counter stances only when nothing
    ///     else is up) without the kill-order narrowing, so a nearby ordinary enemy keeps the damage going
    ///     instead of the rotation idling for the head of the order.
    /// </summary>
    public static List<int> SafeTargets(IReadOnlyList<TargetCandidate> candidates)
    {
        var safe = new List<int>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
            if (!candidates[i].DamageImmune
                && !BST_CrucibleData.DoNotAttack.ContainsKey(candidates[i].NameId))
                safe.Add(i);

        var calm = safe.FindAll(i => !candidates[i].Avoid);
        return calm.Count > 0 ? calm : safe;
    }

    /// <summary> Paired enemies further apart than this (HP %) get balanced: the lower one is left alone. </summary>
    public const float PairHpGap = 10f;

    /// <summary>
    ///     Which candidates auto-targeting may pick on a Crucible board: never eggs / morphos or damage-immune enemies;
    ///     enemies in a counter stance only when nothing else is up; priority adds first — and of the priority adds up, only
    ///     the most dangerous documented tier (<see cref="BST_CrucibleData.PriorityAddOrder"/>) until it is dead, so a
    ///     wave is cleared in the guides' kill order instead of nearest-first (live 2026-09-26: the siren wave's
    ///     shamblings were attacked ~5 s before the crawling piece whose touch breaks Unbeastable); of a pair that must
    ///     die together, the healthier one while they are more than <see cref="PairHpGap"/> apart. With
    ///     <paramref name="interruptArmed"/> (Soul Crush held and ready) an enemy casting something interruptible comes before
    ///     all of that: the cast is gone in seconds and the order resumes when it ends; a caster of a
    ///     <see cref="BST_CrucibleData.KillTheCaster"/> cast comes next, then (with <paramref name="dispelArmed"/>) the enemies
    ///     that carry a dispellable buff.
    /// </summary>
    public static List<int> AllowedTargets(IReadOnlyList<TargetCandidate> candidates, bool interruptArmed = false, bool dispelArmed = false)
    {
        var allowed = new List<int>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
            if (!candidates[i].DamageImmune
                && !BST_CrucibleData.DoNotAttack.ContainsKey(candidates[i].NameId))
                allowed.Add(i);

        var all = allowed;
        var calm = allowed.FindAll(i => !candidates[i].Avoid);
        if (calm.Count > 0)
            allowed = calm;

        // Soul Crush is armed and an enemy is casting something interruptible: that enemy first, whatever the kill order or the
        // pair rule says (the cast is gone in seconds; the kill order resumes the moment it ends).
        if (interruptArmed)
        {
            var casters = allowed.FindAll(i => candidates[i].CastInterruptible);
            if (casters.Count > 0)
                return casters;
        }

        // A cast the guide answers with "kill the caster before it ends" (Soul Douse, Oogle): that caster before any other.
        var killers = allowed.FindAll(i => candidates[i].CastId != 0 && BST_CrucibleData.KillTheCaster.Contains(candidates[i].CastId));
        if (killers.Count > 0)
            return killers;

        // The dispel is armed (Quelling Wave held, or the vulture ready) and an enemy carries a dispellable buff: that enemy
        // before the kill order, stances included (the spikes are exactly what the dispel removes). The buff is gone in one
        // cast and the order resumes the moment it is, or when the dispel proves futile (the carrier then stops counting).
        if (dispelArmed)
        {
            var carriers = all.FindAll(i => candidates[i].Dispellable);
            // A buff the panel flags is known to come off; an unproven one is a try. The known one first: the Drake's Blaze
            // Spikes waited 5.5 s behind the Abaddon's Regen (2026-10-03 19:34:57 to 19:35:03).
            var proven = carriers.FindAll(i => candidates[i].DispellableProven);
            if (proven.Count > 0)
                return proven;
            if (carriers.Count > 0)
                return carriers;
        }

        var priority = allowed.FindAll(i => BST_CrucibleData.PriorityAdds.Contains(candidates[i].NameId));
        if (priority.Count > 0)
        {
            // Documented kill orders: of the members of one order that are up, only the first tier present stays
            // targetable. An order never constrains an add that is not one of its members.
            var dropped = new HashSet<int>();
            for (var wave = 0; wave < BST_CrucibleData.PriorityAddOrder.Length; wave++)
            {
                var best = int.MaxValue;
                foreach (var i in priority)
                    best = Math.Min(best, BST_CrucibleData.PriorityAddTier(candidates[i].NameId, wave));
                if (best == int.MaxValue)
                    continue;
                foreach (var i in priority)
                {
                    var tier = BST_CrucibleData.PriorityAddTier(candidates[i].NameId, wave);
                    if (tier != int.MaxValue && tier > best)
                        dropped.Add(i);
                }
            }

            allowed = priority.FindAll(i => !dropped.Contains(i));
        }

        foreach (var (a, b) in BST_CrucibleData.Pairs)
        {
            var ia = allowed.FindIndex(i => candidates[i].NameId == a);
            var ib = allowed.FindIndex(i => candidates[i].NameId == b);
            if (ia < 0 || ib < 0)
                continue;
            var hpA = candidates[allowed[ia]].HpPercent;
            var hpB = candidates[allowed[ib]].HpPercent;
            if (Math.Abs(hpA - hpB) > PairHpGap)
                allowed.RemoveAt(hpA < hpB ? ia : ib);
        }

        return allowed;
    }

    /// <summary>
    ///     Soul Crush is held and ready, the option is on and the fight needs an interrupt: the rotation can answer an interruptible
    ///     cast the moment one starts, so auto-targeting may aim at the caster (<see cref="AllowedTargets"/>).
    /// </summary>
    public static bool InterruptArmed(in BstState s, in BstSettings cfg) =>
        cfg.Crucible && s.CrucibleBoard != 0 && cfg.UseSoulCrush && (s.CrucibleNeeds & CrucibleNeeds.Interrupt) != 0
        && s.KinshipHeld && s.BeastModeResolved == BST.SoulCrush && s.ReadyBeastMode;

    /// <summary> Quelling Wave is held and ready: a single-target GCD spell, nothing standing beside the carrier can be hit by it. </summary>
    public static bool DispelViaWave(in BstState s) =>
        s.KinshipHeld && s.BeastModeResolved == BST.QuellingWave && s.ReadyBeastMode;

    /// <summary>
    ///     The dispel is ready to go out and the fight calls for it while an enemy carries a dispellable buff: Quelling Wave is
    ///     held and ready, or the vulture is out with its One with Nature and Tempered Release up AND some carrier is one the
    ///     Caw can be cast at (in its range, no do-not-attack enemy within its area: <see cref="BstState.DispelCawReachable"/>;
    ///     <see cref="TryDispel"/> refuses the Caw otherwise, and an aim pinned on a carrier that cannot be dispelled holds the
    ///     rotation every tick). Auto-targeting then aims at the carrier (<see cref="AllowedTargets"/>) the way it aims at an
    ///     interruptible caster when Soul Crush is armed; <see cref="TryDispel"/> casts it on the next tick, with that enemy as the target.
    /// </summary>
    public static bool DispelArmed(in BstState s, in BstSettings cfg) =>
        cfg.Crucible && s.CrucibleBoard != 0 && (s.CrucibleNeeds & CrucibleNeeds.Dispel) != 0 && s.EnemyHasDispellableBuff
        && (DispelViaWave(s)
            || (ActiveBeast(s) is { Row: VultureRow } && FamiliarOut(s) && s.OneWithNature && s.ReadyTempered && s.DispelCawReachable));

    /// <summary>
    ///     A dispel the game does not honour is not sent again and again: after this many dispels that left the same status on
    ///     the same enemy, that status on that enemy stops counting as dispellable. The research's "live" and "guide" ids
    ///     (<see cref="BST_CrucibleData.DispelRows"/>) have never been dispelled in a run; every one costs a Kinship and a
    ///     swap, so the answer to "does Quelling Wave remove it" comes from the first two tries and stops there.
    /// </summary>
    public const int DispelMaxTries = 2;

    /// <summary> A dispel was just sent at <paramref name="enemy"/> while it carried <paramref name="statusesOnEnemy"/>: count one try on each. </summary>
    public static void NoteDispelSent(Dictionary<(ulong Enemy, uint Status), int> tries, ulong enemy, IEnumerable<uint> statusesOnEnemy)
    {
        foreach (var status in statusesOnEnemy)
            tries[(enemy, status)] = tries.GetValueOrDefault((enemy, status)) + 1;
    }

    /// <summary> The enemy's statuses as read now: a status that came off starts its count over (it can be put back on). </summary>
    public static void ForgetGoneStatuses(Dictionary<(ulong Enemy, uint Status), int> tries, ulong enemy, ICollection<uint> statusesOnEnemy)
    {
        foreach (var key in tries.Keys.Where(k => k.Enemy == enemy && !statusesOnEnemy.Contains(k.Status)).ToList())
            tries.Remove(key);
    }

    /// <summary>
    ///     Which dispels actually went out, for <see cref="NoteDispelSent"/>: one try per Quelling Wave / Tempered Release use that
    ///     followed a dispel decision. PURE: the live read passes the clock and the seconds since either was last used. A decision
    ///     repeats every tick until the cast goes out and again every GCD, so the use is matched to the FIRST decision since the
    ///     last counted use, and a use is counted once however many ticks see it. (The earlier burst count took decisions less
    ///     than 2.5 s apart for one burst: 12 Quelling Waves at the Abaddon's Regen counted 2, 2026-10-03 19:34-19:35.)
    /// </summary>
    public sealed class DispelUseTracker
    {
        /// <summary> A decision stays the open one this long (ms). </summary>
        public const long DecisionWindowMs = 2500;

        /// <summary> A use this recent (s) can still belong to the dispel just decided. </summary>
        public const float UseWindow = 1.5f;

        /// <summary> Two uses closer than this (ms) are one use seen on two ticks. </summary>
        private const long SameUseMs = 400;

        private long _pending, _decided, _counted;

        public void NoteDecision(long nowMs)
        {
            if (_pending == 0 || nowMs - _decided > DecisionWindowMs)
                _pending = nowMs;
            _decided = nowMs;
        }

        /// <summary> True once for each dispel that went out since the open decision. </summary>
        public bool CountUse(long nowMs, float sinceUsedSeconds)
        {
            if (_pending == 0 || nowMs - _decided > DecisionWindowMs || sinceUsedSeconds > UseWindow)
                return false;
            var useMs = nowMs - (long)(sinceUsedSeconds * 1000f);
            if (useMs < _pending - 100 || (_counted != 0 && useMs - _counted < SameUseMs))
                return false;
            _counted = useMs;
            _pending = 0;
            return true;
        }
    }

    /// <summary> This status on this enemy took <see cref="DispelMaxTries"/> dispels and is still there. </summary>
    public static bool DispelFutile(Dictionary<(ulong Enemy, uint Status), int> tries, ulong enemy, uint status) =>
        tries.TryGetValue((enemy, status), out var n) && n >= DispelMaxTries;

    // ------------------------------------------------------------------ familiar HP memory (party agent)

    /// <summary>
    ///     How the run-scoped familiar HP dictionary accepts a sample from AgentXBMPetParty for a beast that is
    ///     not currently summoned. The party agent briefly reports 100 after Parting Blow / a horn-swap while the
    ///     familiar is still hurt (first-board run 2026-09-17: live 19% → agent 100 for ~48 s, then 19 again).
    ///     Never raise a remembered low value to a near-full party reading while that leave is still recent; lower
    ///     readings, partial heals, and camp restores after the grace window still apply.
    /// </summary>
    public const float PartyHpRaiseEpsilon = 3f;

    /// <summary> Party readings at or above this count as "full" for the lag filter. </summary>
    public const float PartyHpFull = 99f;

    /// <summary> Remembered HP below this is "low" for the lag filter. </summary>
    public const float PartyHpLow = 95f;

    /// <summary>
    ///     Apply one party-agent HP sample for a non-active beast row. Returns whether <paramref name="remembered"/>
    ///     was written.
    /// </summary>
    public static bool ApplyPartyHpSample(Dictionary<int, float> remembered, int row, float partyHp, bool recentlyLeftLow)
    {
        if (row == 0)
            return false;
        if (remembered.TryGetValue(row, out var prev)
            && partyHp > prev + PartyHpRaiseEpsilon
            && partyHp >= PartyHpFull
            && prev < PartyHpLow
            && recentlyLeftLow)
            return false;
        remembered[row] = Math.Max(partyHp, 0.5f);
        return true;
    }
}
