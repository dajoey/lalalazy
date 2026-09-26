using System;
using System.Collections.Generic;
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
    ///     its One with Nature goes to Borrow instead of Tempered Release.
    /// </summary>
    public static bool ShouldBorrowForFight(in BstState s, BeastmasterBeast? beast) =>
        beast is { } b && b.Kin != BeastmasterKinType.None && WantedKin(s) == b.Kin && HeldKin(s) != b.Kin
        && s.OneWithNature && BorrowAllowed(s);

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
        (int, int, int, float) bestKey = (0, 0, 0, 0f);
        var stingDue = FinalStingDue(s, cfg);

        for (var slot = 1; slot <= learned; slot++)
        {
            if (slot == exceptSlot || !HornReady(s, slot) || (s.SlotBeastsKnown && SlotBeast(s, slot) == 0))
                continue;

            var hp = SlotPetHp(s, slot);
            var healthy = hp > cfg.CruciblePetSwapHp;
            var key = (
                healthy ? 1 : 0,
                IsExitBeast(s, slot) ? (stingDue ? 2 : 0) : 1,   // wespe first when its Final Sting is due, else last
                healthy && IsSetUpBeast(s, slot) ? 1 : 0,        // set-up beasts first; below the line health decides
                hp);
            if (best == 0 || key.CompareTo(bestKey) > 0)
            {
                best = slot;
                bestKey = key;
            }
        }

        if (best != 0 && bestKey.Item4 <= CriticalHp(cfg) && s.InCombat && s.PartingBlowRecast > 2f)
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

        // Curtains for Rank 5 (49429, King Ahriman): 6.0s cast that instantly KOs the familiar regardless of HP.
        // Parting Blow recalls the familiar safely before the cast resolves.
        if (s.TargetCastId == 49429 && s.TargetCastRemaining is > 0.2f and <= 2.5f
            && s.Level >= LvPartingBlow && s.ReadyParting && s.CanWeave && !s.TargetDoNotAttack && !s.ProtectedNearTarget)
            return (BST.PartingBlow, "crucible:petsave-curtains");

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

        if (beast is { } b && (b.Release & BeastmasterReleaseTraits.Exit) != 0 && s.OneWithNature && s.ReadyTempered)
            return (BST.TemperedRelease, "crucible:petsave-finalsting");
        if (s.ReadyParting)
            return (BST.PartingBlow, "crucible:petsave-partingblow");

        declines.Add("crucible:petsave-partingblow-recast");
        return (0, "");
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

    /// <summary> Dispel an enemy buff: the vulture's Bloodcurdling Caw, else Quelling Wave (a GCD spell, allowed mid-combo). </summary>
    public static (uint ActionId, string Reason) TryDispel(in BstState s, BeastmasterBeast? beast, List<string> declines)
    {
        if (!s.HasHostileTarget || !s.TargetHasDispellableBuff || s.TargetDoNotAttack)
            return (0, "");

        if (beast is { Row: VultureRow } && FamiliarOut(s) && s.OneWithNature && s.ReadyTempered && s.CanWeave
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
    ///     carry 15 s of the character's recent damage intake and is not a wespe about to Final Sting (at 25% or lower
    ///     only the familiar's HP matters); Snarl ahead of a known tankbuster only with the Snarl -> Parting Blow dodge
    ///     on. Challenge when the parry ends, or when the familiar is at 30% or lower and the character 60%+.
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
        var tankbusterSetUp = cfg.CrucibleSnarlParting && tankbuster && s.ReadySnarl && !s.EnemyTargetsPet
                              && s.TargetCastRemaining > cfg.CrucibleSnarlPartingLead + 1f && s.ReadyParting;

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
            if (s.PlayerHpPercent is > 0f and <= 40f && s.PetHpPercent >= 50f)
            {
                var carries = s.PetHp > s.PlayerIntakePerSecond * 15f;
                var stingDue = BST_Beasts.ByRow(SlotBeast(s, s.ActiveSlot)) is { } b
                               && (b.Release & BeastmasterReleaseTraits.Exit) != 0 && FinalStingDue(s, cfg);
                if (lastResort || (carries && !stingDue))
                    return (BST.Snarl, lastResort ? "aggro:snarl-last-resort" : "aggro:snarl-player-low");
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
        s.HasHostileTarget && FamiliarOut(s) && s.ReadyParting && !s.TargetDoNotAttack && !s.ProtectedNearTarget
        && s.TargetCastId != 0 && BST_CrucibleData.Tankbusters.Contains(s.TargetCastId)
        && s.TargetCastRemaining + BST_CrucibleData.TankbusterHitDelay(s.TargetCastId) is > 0.2f and var landsIn
        && landsIn <= cfg.CrucibleSnarlPartingLead
        && s.SinceSnarl < 45f && s.TargetDistance <= 25f;

    // ------------------------------------------------------------------ auto-targeting

    /// <summary> One enemy auto-targeting could pick. <see cref="Avoid"/>: counter stance or invulnerable right now. </summary>
    public readonly record struct TargetCandidate(uint NameId, float HpPercent, bool Avoid);

    /// <summary> Paired enemies further apart than this (HP %) get balanced: the lower one is left alone. </summary>
    public const float PairHpGap = 10f;

    /// <summary>
    ///     Which candidates auto-targeting may pick on a Crucible board: never eggs / morphos; enemies in a counter
    ///     stance or invulnerable only when nothing else is up; priority adds first; of a pair that must die together,
    ///     the healthier one while they are more than <see cref="PairHpGap"/> apart.
    /// </summary>
    public static List<int> AllowedTargets(IReadOnlyList<TargetCandidate> candidates)
    {
        var allowed = new List<int>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
            if (!BST_CrucibleData.DoNotAttack.ContainsKey(candidates[i].NameId))
                allowed.Add(i);

        var calm = allowed.FindAll(i => !candidates[i].Avoid);
        if (calm.Count > 0)
            allowed = calm;

        var priority = allowed.FindAll(i => BST_CrucibleData.PriorityAdds.Contains(candidates[i].NameId));
        if (priority.Count > 0)
            allowed = priority;

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
