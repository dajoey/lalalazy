using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4 reaction lane (task tasks-20261015-crucible-r4-fix-reaction-coverage-01). Titles start with "r4-reaction:" and name the gap id.
///     Rows: 5.7-08 (Impassion is not dispellable), 4.4-03 (Rime Wreath's Frostbite calls for a cleanse), 3.0-04b (a paralysed familiar is
///     swapped out), 1.3-04 (Arcane Blast: dispel first, else the existing survival answers), lv-15a (interrupt coverage: why no Soul Crush is
///     said in the decline log), lv-15b (dispel coverage: the five panel buffs come off whenever a dispeller is out), 3.4-07b (petrify).
/// </summary>
internal static partial class Program
{
    private static void R4ReactionLane20261015()
    {
        Console.WriteLine("-- r4-reaction: Impassion, Rime Wreath, paralysed familiar, Arcane Blast, interrupt / dispel coverage, petrify --");
        var cfg = BstSettings.Defaults();

        // ---- 5.7-08 Impassion (3129, Medusa Piece). The research row sat on the dispel list with basis "guide" (never seen on an enemy in a
        // run) and TryDispel fired at it. The enemy panel does not flag it and S4 / S7 say none of the three borrowed answers removes it.
        Check("r4-reaction: 5.7-08 Impassion (3129) is not a dispellable buff: no dispel is sent at it",
            !BST_CrucibleData.DispellableBuffs.Contains(3129), $"in list: {BST_CrucibleData.DispellableBuffs.Contains(3129)}");
        Check("r4-reaction: 5.7-08 ... its research row stays (the guide still counts it) and is not tried",
            BST_CrucibleData.DispelRows.Any(r => r.Board == 5 && r.Battle == 7 && r.StatusId == 3129) && !BST_CrucibleData.DispellableBuffs.Contains(3129));
        Check("r4-reaction: 5.7-08 the other unproven tries stay: Growing (390) and Might (1572) are still tried, Blaze Spikes (5465) stays dispellable, Regen (989) stays out",
            BST_CrucibleData.DispellableBuffs.Contains(390) && BST_CrucibleData.DispellableBuffs.Contains(1572)
            && BST_CrucibleData.DispellableBuffs.Contains(5465) && !BST_CrucibleData.DispellableBuffs.Contains(989));
        BST_CrucibleGuideNeeds.Install();
        try
        {
            Check("r4-reaction: 5.7-08 the Medusa fight (5.7) does not call for a dispel (its only dispel row is the Useful Impassion one)",
                (BST_CrucibleLogic.FightNeeds(5, 7) & CrucibleNeeds.Dispel) == 0, BST_CrucibleLogic.FightNeeds(5, 7).ToString());

            // ---- 4.4-03 Rime Wreath (48709, Ice Dragon Piece + Ice Sprite): a hit that applies Frostbite (5500, game-flagged cleansable).
            // The (4,4) panel rows carry no cleanse and the guide had no counter, so nothing brought a cleanse familiar and nothing answered it.
            var rime = CrucibleNeedModel.For(4, 4);
            Check("r4-reaction: 4.4-03 Rime Wreath: the fight (4.4) calls for a cleanse",
                (BST_CrucibleLogic.FightNeeds(4, 4) & CrucibleNeeds.Cleanse) != 0, $"req {rime.Required} useful {rime.Useful}");
            var pick = BST_CrucibleNeedFirst.SelectCaptured(4, 4, _ => true);
            var cleanseNeed = pick.Needs.FirstOrDefault(n => n.Need.Kind == CrucibleNeeds.Cleanse);
            Check("r4-reaction: 4.4-03 the need-first picks bring a cleanse familiar (Ashkin or the bat) for it",
                cleanseNeed.Covered && BST_Beasts.ByRow(cleanseNeed.Row) is { } b && (b.Kin == BeastmasterKinType.Ashkin || b.Row == BST_CrucibleData.BatRow),
                $"need {cleanseNeed.Need.What}, row {cleanseNeed.Row}");
            Check("r4-reaction: 4.4-03 the other 4.x fights keep their needs (4.3 none, 4.2 cleanse, 4.1 dispel)",
                BST_CrucibleLogic.FightNeeds(4, 3) == CrucibleNeeds.None && (BST_CrucibleLogic.FightNeeds(4, 2) & CrucibleNeeds.Cleanse) != 0
                && (BST_CrucibleLogic.FightNeeds(4, 1) & CrucibleNeeds.Dispel) != 0);

            // In the fight: Frostbite lands, the Ashkin (ghost, row 40) is on horn 2 and ready, the out familiar is the squirrel.
            var fight = CrucibleState() with
            {
                CrucibleBoard = 4, CrucibleBattle = 4, CrucibleNeeds = BST_CrucibleLogic.FightNeeds(4, 4),
                Slot1Beast = 2, Slot2Beast = 40, Slot3Beast = 26, PetObjectBeast = 2, ActiveSlot = 1,
                PlayerHasCleansableDebuff = true, SinceSummon = 20f, SinceHornPress = 30f,
            };
            var swap = Decide(fight, cfg);
            Check("r4-reaction: 4.4-03 Frostbite on the character with an Ashkin on a ready horn: the Ashkin is blown in to cleanse it",
                swap is { ActionId: BST.SecondBattlehorn, Reason: "crucible:answer-cleanse-slot2" }, $"{swap.ActionId}:{swap.Reason} [{swap.Declines}]");
        }
        finally
        {
            CrucibleNeedModel.Extras = null;
        }

        // ---- 3.0-04b Thunderbolt (48620): Paralysis (5388) on the familiar, 15 s, "sometimes preventing the execution of actions". The pet swap keyed
        // on HP only (grep 5388 at the pin: 0 hits), so a paralysed full-HP familiar stayed out for the whole 15 s. Rule: a healthy ready horn replaces it
        // while more than a few seconds are left, outside the swap grace, not while covering, and only with a reserve horn left for a real familiar save.
        var para = CrucibleState() with { PetParalysisRemaining = 12f, SinceSummon = 20f, SinceHornPress = 30f, EnemyTargetsPlayer = true };
        var swappedOut = Decide(para, cfg);
        Check("r4-reaction: 3.0-04b a paralysed full-HP familiar (12 s left), two healthy horns ready: a healthy horn is blown in over it",
            swappedOut.ActionId is BST.SecondBattlehorn or BST.ThirdBattlehorn && swappedOut.Reason == "crucible:petsave-paralysis", $"{swappedOut.ActionId}:{swappedOut.Reason} [{swappedOut.Declines}]");
        Check("r4-reaction: 3.0-04b control: not paralysed, nothing changes (no swap for a healthy familiar)",
            Decide(para with { PetParalysisRemaining = 0f }, cfg).Reason != "crucible:petsave-paralysis");
        Check("r4-reaction: 3.0-04b 4 s of Paralysis left is not worth a swap",
            Decide(para with { PetParalysisRemaining = 4f }, cfg).Reason != "crucible:petsave-paralysis");
        Check("r4-reaction: 3.0-04b a familiar summoned 3 s ago is not swapped again (swap grace)",
            Decide(para with { SinceSummon = 3f }, cfg).Reason != "crucible:petsave-paralysis");
        Check("r4-reaction: 3.0-04b a covering familiar (Snarl 10 s ago, the enemy on it) stays out",
            Decide(para with { SinceSnarl = 10f, EnemyTargetsPet = true, EnemyTargetsPlayer = false }, cfg).Reason != "crucible:petsave-paralysis");
        var moving = Decide(para with { IsMoving = true }, cfg);
        Check("r4-reaction: 3.0-04b moving: no swap, and the log says why",
            moving.Reason != "crucible:petsave-paralysis" && moving.Declines.Contains("crucible:petsave-paralysis-moving"), $"{moving.Reason} [{moving.Declines}]");
        Check("r4-reaction: 3.0-04b the other horns' familiars are hurt: no swap",
            Decide(para with { Slot2PetHp = 30f, Slot3PetHp = 30f }, cfg).Reason != "crucible:petsave-paralysis");
        Check("r4-reaction: 3.0-04b only one spare horn ready: it stays in reserve for a real familiar save",
            Decide(para with { ReadyHorn3 = false }, cfg).Reason != "crucible:petsave-paralysis");
        Check("r4-reaction: 3.0-04b outside the Crucible nothing changes",
            Decide(para with { CrucibleBoard = 0, CrucibleBattle = 0 }, cfg).Reason != "crucible:petsave-paralysis");
    }
}
