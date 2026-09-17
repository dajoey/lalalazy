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
///     What is different in there (guides + the game's own panel data, see BST_CrucibleData):
///     <list type="bullet">
///         <item>Familiars have HP and no regeneration. A familiar that is knocked out is gone for the run;
///               one that retreats (Parting Blow) comes back. So a dying familiar is sent away.</item>
///         <item>Parting Blow right as a round ends can block summoning next round: no normal exit then.</item>
///         <item>Every enemy panel says which casts are interruptible, which buffs dispellable, which debuffs
///               cleansable. The Kinship that answers the fight is borrowed before the pull.</item>
///         <item>Some enemies counter attackers (spikes stances) or must not be hit at all (eggs, morphos).</item>
///         <item>Snarl / Challenge are the only enmity tools. They start in shadow mode: decided and logged,
///               not pressed, until a run has been graded.</item>
///     </list>
/// </remarks>
internal static class BST_CrucibleLogic
{
    /// <summary> All.Cease: the auto-rotation treats it as "press nothing". </summary>
    public const uint Hold = 1_000_004;

    /// <summary> Below this HP on every remaining enemy the round is ending: no normal Parting Blow exit. </summary>
    public const float RoundEndingHp = 10f;

    /// <summary> A pet-save is skipped when the last enemy is this close to death anyway. </summary>
    public const float EnemyDyingHp = 3f;

    /// <summary> XBMPet row of the vulture: Bloodcurdling Caw dispels a buff. </summary>
    public const int VultureRow = 11;

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

    /// <summary> First learned horn whose assigned beast is of <paramref name="kin"/> (0 = none). </summary>
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
    ///     vulture is on a horn) &gt; cleanse (Ashkin), skipping needs no assigned beast can answer.
    /// </summary>
    public static BeastmasterKinType WantedKin(in BstState s)
    {
        if ((s.CrucibleNeeds & CrucibleNeeds.Interrupt) != 0 && SlotWithKin(s, BeastmasterKinType.Soulkin) != 0)
            return BeastmasterKinType.Soulkin;
        if ((s.CrucibleNeeds & CrucibleNeeds.Dispel) != 0 && SlotWithKin(s, BeastmasterKinType.None, VultureRow) == 0
            && SlotWithKin(s, BeastmasterKinType.Wavekin) != 0)
            return BeastmasterKinType.Wavekin;
        if ((s.CrucibleNeeds & CrucibleNeeds.Cleanse) != 0 && SlotWithKin(s, BeastmasterKinType.Ashkin) != 0)
            return BeastmasterKinType.Ashkin;
        return BeastmasterKinType.None;
    }

    /// <summary>
    ///     Out of combat with an enemy targeted: summon the horn with the wanted kin, Borrow, and let the
    ///     between-pulls refresh swap to another horn (swaps cost nothing out of combat).
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

    /// <summary> Last HP seen on a horn's familiar; unknown (0) counts as healthy. </summary>
    public static float SlotPetHp(in BstState s, int slot)
    {
        var hp = slot switch { 1 => s.Slot1PetHp, 2 => s.Slot2PetHp, 3 => s.Slot3PetHp, _ => 0f };
        return hp <= 0f ? 100f : hp;
    }

    /// <summary>
    ///     A ready horn like <see cref="PickReadyHorn"/>, but when its familiar was last seen near the pet-save
    ///     threshold, prefer the healthiest other ready horn: a low familiar would only be sent away again.
    ///     A familiar already at the threshold comes out only while Parting Blow is ready to save it.
    /// </summary>
    public static int PickHorn(in BstState s, in BstSettings cfg, int exceptSlot)
    {
        var first = PickReadyHorn(s, exceptSlot);

        // Wespe comes out last: its Final Sting waits for the execute, so opening with it parks the other
        // horns' Tempered Release + Parting Blow burst (guides: burst beasts first, wespe third).
        if (first != 0 && IsExitBeast(s, first) && !FinalStingExecuteHp(s, cfg))
        {
            var other = PickReadyHorn(s, exceptSlot, first);
            if (other != 0 && other != first && !IsExitBeast(s, other))
                first = other;
        }

        if (first == 0 || SlotPetHp(s, first) > cfg.CruciblePetSaveHp + 10)
            return first;

        var best = first;
        var bestHp = SlotPetHp(s, first);
        var learned = LearnedHornSlots(s.Level);
        for (var slot = 1; slot <= learned; slot++)
        {
            if (slot == exceptSlot || !HornReady(s, slot) || (s.SlotBeastsKnown && SlotBeast(s, slot) == 0))
                continue;
            var hp = SlotPetHp(s, slot);
            if (hp > bestHp)
            {
                best = slot;
                bestHp = hp;
            }
        }

        if (bestHp <= cfg.CruciblePetSaveHp && s.InCombat && s.PartingBlowRecast > 2f)
            return 0;
        return best;
    }

    private static bool IsExitBeast(in BstState s, int slot) =>
        BST_Beasts.ByRow(SlotBeast(s, slot)) is { } b && (b.Release & BeastmasterReleaseTraits.Exit) != 0;

    // ------------------------------------------------------------------ in combat

    /// <summary>
    ///     Familiar at or below the pet-save threshold: send it away before it is knocked out. Wespe leaves with
    ///     Final Sting when it can. Ignores the minimum stay, One with Nature and the other-horn rule.
    /// </summary>
    public static uint TryPetSave(in BstState s, in BstSettings cfg, BeastmasterBeast? beast, List<string> declines)
    {
        if (s.Level < LvPartingBlow || !FamiliarOut(s) || s.PetHpPercent <= 0f || s.PetHpPercent > cfg.CruciblePetSaveHp)
            return 0;

        if (!s.HasHostileTarget)
        {
            declines.Add("crucible:petsave-no-target");
            return 0;
        }
        if (s.EnemyCount <= 1 && s.TargetHpPercent <= EnemyDyingHp)
        {
            declines.Add("crucible:petsave-enemy-dying");
            return 0;
        }
        if (s.TargetDoNotAttack || s.ProtectedNearTarget)
        {
            declines.Add("crucible:petsave-protected");
            return 0;
        }
        if (s.TargetDistance > 25f)
        {
            declines.Add("crucible:petsave-out-of-range");
            return 0;
        }

        if (beast is { } b && (b.Release & BeastmasterReleaseTraits.Exit) != 0 && s.OneWithNature && s.ReadyTempered)
            return BST.TemperedRelease;
        if (s.ReadyParting)
            return BST.PartingBlow;

        declines.Add("crucible:petsave-partingblow-recast");
        return 0;
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

    /// <summary> Scouring Ash when the character carries a cleansable debuff. </summary>
    public static (uint ActionId, string Reason) TryCleanse(in BstState s)
    {
        if (s.PlayerHasCleansableDebuff && s.KinshipHeld && s.BeastModeResolved == BST.ScouringAsh && s.ReadyBeastMode && s.CanWeave)
            return (BST.ScouringAsh, "crucible:cleanse-scouringash");
        return (0, "");
    }

    /// <summary>
    ///     Snarl (familiar takes aggro and covers the character) / Challenge (character takes it back):
    ///     Snarl for Directional Parry, known single-target hits and a low character; Challenge when the parry
    ///     drops or the familiar runs low.
    /// </summary>
    public static (uint ActionId, string Reason) ChooseAggro(in BstState s, in BstSettings cfg)
    {
        if (!s.HasHostileTarget || !FamiliarOut(s) || s.TargetDoNotAttack)
            return (0, "");

        var tankbuster = s.TargetCastId != 0 && BST_CrucibleData.Tankbusters.Contains(s.TargetCastId);

        // Score mode: the character tanks so every familiar ends at full HP (Selfless). Snarl only to set up the
        // tankbuster whiff; Challenge back whenever the target turns on the familiar.
        if (cfg.CrucibleScoreMode)
        {
            if (cfg.CrucibleSnarlParting && tankbuster && s.ReadySnarl && !s.EnemyTargetsPet && s.TargetCastRemaining > cfg.CrucibleSnarlPartingLead + 1f
                && s.ReadyParting)
                return (BST.Snarl, "aggro:snarl-tankbuster");
            if (s.ReadyChallenge && s.EnemyTargetsPet && !(tankbuster && s.SinceSnarl < 45f))
                return (BST.Challenge, "aggro:challenge-score");
            return (0, "");
        }

        if (s.ReadySnarl && !s.EnemyTargetsPet)
        {
            if (s.TargetHasParry && s.EnemyTargetsPlayer)
                return (BST.Snarl, "aggro:snarl-parry");
            if (s.TargetCastId != 0 && s.TargetCastRemaining > 0.8f && (tankbuster || BST_CrucibleData.SnarlHits.Contains(s.TargetCastId))
                && s.EnemyTargetsPlayer && s.PetHpPercent >= 50f)
                return (BST.Snarl, "aggro:snarl-hardhit");
            if (s.PlayerHpPercent is > 0f and <= 40f && s.PetHpPercent >= 60f && s.EnemyTargetsPlayer)
                return (BST.Snarl, "aggro:snarl-player-low");
        }

        if (s.ReadyChallenge && s.EnemyTargetsPet)
        {
            if (s.ParryJustEnded && !s.TargetHasParry)
                return (BST.Challenge, "aggro:challenge-parry-ended");
            if (s.PetHpPercent <= 35f && s.PlayerHpPercent >= 60f)
                return (BST.Challenge, "aggro:challenge-pet-low");
        }

        return (0, "");
    }

    // ------------------------------------------------------------------ auto-targeting

    /// <summary> One enemy auto-targeting could pick. </summary>
    public readonly record struct TargetCandidate(uint NameId, float HpPercent, bool InStance);

    /// <summary> Paired enemies further apart than this (HP %) get balanced: the lower one is left alone. </summary>
    public const float PairHpGap = 10f;

    /// <summary>
    ///     Which candidates auto-targeting may pick on a Crucible board: never eggs / morphos; enemies in a
    ///     counter stance only when nothing else is up; priority adds first; of a pair that must die together,
    ///     the healthier one while they are more than <see cref="PairHpGap"/> apart.
    /// </summary>
    public static List<int> AllowedTargets(IReadOnlyList<TargetCandidate> candidates)
    {
        var allowed = new List<int>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
            if (!BST_CrucibleData.DoNotAttack.ContainsKey(candidates[i].NameId))
                allowed.Add(i);

        var calm = allowed.FindAll(i => !candidates[i].InStance);
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

    /// <summary>
    ///     Snarl -> Parting Blow: a known tankbuster is about to land (within the lead time), the familiar took the
    ///     aggro with Snarl in the last 45 s (Cover), and Parting Blow is ready. Guides press it 1-2 s before the cast
    ///     ends: earlier and the hit lands on the character, later and the familiar eats it.
    /// </summary>
    public static bool SnarlPartingNow(in BstState s, in BstSettings cfg) =>
        s.HasHostileTarget && FamiliarOut(s) && s.ReadyParting && !s.TargetDoNotAttack && !s.ProtectedNearTarget
        && s.TargetCastId != 0 && BST_CrucibleData.Tankbusters.Contains(s.TargetCastId)
        && s.TargetCastRemaining > 0.2f && s.TargetCastRemaining <= cfg.CrucibleSnarlPartingLead
        && s.SinceSnarl < 45f && s.TargetDistance <= 25f;

    /// <summary> Final Sting as an execute: target at or below the threshold (halved with 2+ enemies). </summary>
    public static bool FinalStingExecuteHp(in BstState s, in BstSettings cfg) =>
        s.TargetHpPercent <= (s.EnemyCount >= 2 ? cfg.CrucibleFinalStingHp / 2f : cfg.CrucibleFinalStingHp);
}
