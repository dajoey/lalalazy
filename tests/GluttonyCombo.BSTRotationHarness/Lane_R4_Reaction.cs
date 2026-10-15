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

        // ---- lv-15a interrupt coverage. Log 2026-09-17..10-14: 438 interruptible cast starts, 52 with a Soul Crush effect, 274 of the other 386 completed.
        // The press itself fires (5 `soulcrush-interrupt` in the CR window, within 0.3 to 2.2 s of the cast start); the recorded misses are Natural Nurture
        // (4.8) with the horns bat / diremite / sabotender (no Soulkin anywhere: 261 of 277 interruptible ticks, Soul Crush never held), and "why" was never logged.
        // The rule is unchanged; every way the interrupt can be blocked now says so in the decline log.
        var live50 = CrucibleState(50) with { SinceSummon = 12f, ReadyParting = false, SinceHornPress = 30f, GcdReady = false, CanWeave = true };
        var rupture = live50 with
        {
            CrucibleBoard = 5, CrucibleBattle = 10, CrucibleNeeds = CrucibleNeeds.Interrupt, TargetInterruptible = true, TargetCastId = 49375, TargetCastRemaining = 5f,
            Slot1Beast = 7, Slot2Beast = 1, Slot3Beast = 26, ActiveSlot = 1, PetObjectBeast = 7, TargetDistance = 1f,
            KinshipHeld = true, BeastModeResolved = BST.SoulCrush, ReadyBeastMode = true, KinshipSlot = 1,
        };
        Check("r4-reaction: lv-15a Soul Crush held and ready, interruptible cast, in melee: Soul Crush goes out",
            Decide(rupture, cfg) is { ActionId: BST.SoulCrush, Reason: "beastmode:soulcrush-interrupt" }, $"{Decide(rupture, cfg).Reason}");
        var far = Decide(rupture with { TargetDistance = 6f }, cfg);
        Check("r4-reaction: lv-15a ... out of melee (6 y): no Soul Crush, and the log says out of range",
            far.ActionId != BST.SoulCrush && far.Declines.Contains("crucible:interrupt-out-of-range"), $"{far.ActionId}:{far.Reason} [{far.Declines}]");
        var cooling = Decide(rupture with { ReadyBeastMode = false }, cfg);
        Check("r4-reaction: lv-15a ... Soul Crush not ready: no press, and the log says not ready",
            cooling.ActionId != BST.SoulCrush && cooling.Declines.Contains("crucible:interrupt-soulcrush-not-ready"), $"{cooling.ActionId}:{cooling.Reason} [{cooling.Declines}]");
        var noWeave = Decide(rupture with { CanWeave = false, GcdReady = true }, cfg);
        Check("r4-reaction: lv-15a ... no weave window: no press, and the log says it is waiting for one",
            noWeave.ActionId != BST.SoulCrush && noWeave.Declines.Contains("crucible:interrupt-waiting-weave"), $"{noWeave.ActionId}:{noWeave.Reason} [{noWeave.Declines}]");
        var natural = live50 with
        {
            CrucibleBoard = 4, CrucibleBattle = 8, CrucibleNeeds = CrucibleNeeds.Interrupt, TargetInterruptible = true, TargetCastId = 48782, TargetCastRemaining = 4f,
            Slot1Beast = 19, Slot2Beast = 8, Slot3Beast = 22, ActiveSlot = 1, PetObjectBeast = 19, TargetDistance = 2f,
        };
        var lonely = Decide(natural, cfg);
        Check("r4-reaction: lv-15a Natural Nurture with the recorded horns (bat, diremite, sabotender): nothing can answer, and the log says there is no Soulkin on a horn",
            lonely.ActionId != BST.SoulCrush && lonely.Declines.Contains("crucible:answer-interrupt-no-horn"), $"{lonely.ActionId}:{lonely.Reason} [{lonely.Declines}]");
        var soulkinHorn = Decide(natural with { Slot3Beast = 7 }, cfg);
        Check("r4-reaction: lv-15a ... a Soulkin (coblyn) on a ready horn: its horn is blown in, no 'no horn' decline",
            soulkinHorn is { ActionId: BST.ThirdBattlehorn, Reason: "crucible:answer-interrupt-slot3" } && !soulkinHorn.Declines.Contains("no-horn"), $"{soulkinHorn.Reason} [{soulkinHorn.Declines}]");
        Check("r4-reaction: lv-15a ... outside the Crucible the decline log is unchanged",
            !Decide(natural with { CrucibleBoard = 0 }, cfg).Declines.Contains("crucible:answer-interrupt-no-horn"));

        // ---- lv-15b dispel coverage. Log: a landed Quelling Wave / Caw took the buff off 16 of 16 Damage Up (1225), 17 of 17 Ultimate Focus (5020), 7 of 7 Popoto Skin
        // (5423) and 6 of 6 Blaze Spikes (5465) holders; 199 of the 249 adds lasted over 5 s with no dispel, all in fights that Require the dispel, and in every
        // CR/BT window of 4.1 and 4.8 the horns were bat / diremite / sabotender. The rotation works when a dispeller is out; "a Useful dispel is not a need" stays
        // because no dispel was ever recorded landing on Might (1572), Growing (390) or Impassion (3129).
        var strix = live50 with
        {
            CrucibleBoard = 4, CrucibleBattle = 1, CrucibleNeeds = BST_CrucibleData.BattleNeeds(4, 1), TargetHasDispellableBuff = true, EnemyHasDispellableBuff = true,
            Slot1Beast = 19, Slot2Beast = 8, Slot3Beast = 22, ActiveSlot = 1, PetObjectBeast = 19, GcdReady = true, CanWeave = false, TargetDistance = 3f,
        };
        Check("r4-reaction: lv-15b Quelling Wave held and ready, Ultimate Focus up: it is dispelled",
            Decide(strix with { KinshipHeld = true, BeastModeResolved = BST.QuellingWave, ReadyBeastMode = true }, cfg) is { ActionId: BST.QuellingWave, Reason: "crucible:dispel-quellingwave" });
        Check("r4-reaction: lv-15b ... the vulture out with One with Nature: Bloodcurdling Caw",
            Decide(strix with { Slot1Beast = 11, PetObjectBeast = 11, OneWithNature = true, ReadyTempered = true, CanWeave = true, DispelCawReachable = true }, cfg) is { ActionId: BST.TemperedRelease, Reason: "crucible:dispel-caw" });
        var noDispeller = Decide(strix, cfg);
        Check("r4-reaction: lv-15b ... the recorded horns (bat, diremite, sabotender) carry no dispeller: nothing can dispel, and the log says there is no dispeller on a horn",
            noDispeller.Declines.Contains("crucible:answer-dispel-no-horn"), $"{noDispeller.ActionId}:{noDispeller.Reason} [{noDispeller.Declines}]");
        Check("r4-reaction: lv-15b ... a pugil (Wavekin) on a ready horn: it is blown in, no 'no horn' decline",
            Decide(strix with { Slot3Beast = 4, CanWeave = true }, cfg) is { ActionId: BST.ThirdBattlehorn, Reason: "crucible:answer-dispel-slot3" });
        BST_CrucibleGuideNeeds.Install();
        try
        {
            Check("r4-reaction: lv-15b a Useful dispel is not a need: Might (3.5) and Growing (4.8) are left to a dispeller that happens to be out (no dispel was ever recorded landing on them)",
                (BST_CrucibleLogic.FightNeeds(3, 5) & CrucibleNeeds.Dispel) == 0 && (BST_CrucibleLogic.FightNeeds(4, 8) & CrucibleNeeds.Dispel) == 0
                && CrucibleNeedModel.For(3, 5).Useful == CrucibleNeeds.Dispel);
            Check("r4-reaction: lv-15b the five panel buffs are all on the list (1225, 5020, 2074, 5423, 5465) and Regen (989) is not",
                new uint[] { 1225, 5020, 2074, 5423, 5465 }.All(BST_CrucibleData.DispellableBuffs.Contains) && !BST_CrucibleData.DispellableBuffs.Contains(989));
        }
        finally
        {
            CrucibleNeedModel.Extras = null;
        }

        // ---- 1.3-04 Arcane Blast (46899, Piscodemon): dispel Clear Mind first, else mitigate. Log: the blast hit the character for 85 to 166 (n 8) and the familiars for
        // 80 to 153, the largest 166 against about 2,400 HP. The dispel route is the Wavekin's / vulture's (a pugil's One with Nature goes to Borrow -> Quelling Wave, which
        // takes the buff off; the pugil's own Water Wall is the same One with Nature, so it can never be the answer while the dispel route is open). What the plugin
        // already does when no dispel can go out is its survival answers: the potion at the panic line.
        var blast = live50 with
        {
            CrucibleBoard = 1, CrucibleBattle = 3, CrucibleNeeds = CrucibleNeeds.Dispel, TargetCastId = 46899, TargetCastRemaining = 4f,
            TargetHasDispellableBuff = true, EnemyHasDispellableBuff = true, GcdReady = true, CanWeave = true, TargetDistance = 3f,
            Slot1Beast = 4, Slot2Beast = 1, Slot3Beast = 26, ActiveSlot = 1, PetObjectBeast = 4, PlayerHp = 2400f, PlayerHpPercent = 100f,
        };
        Check("r4-reaction: 1.3-04 Arcane Blast with Clear Mind up and Quelling Wave held: the dispel goes first (no potion, no skin)",
            Decide(blast with { KinshipHeld = true, BeastModeResolved = BST.QuellingWave, ReadyBeastMode = true, PetObjectBeast = 1, Slot1Beast = 1 }, cfg) is { ActionId: BST.QuellingWave, Reason: "crucible:dispel-quellingwave" });
        Check("r4-reaction: 1.3-04 ... a pugil out with One with Nature: its One with Nature goes to Borrow -> Quelling Wave (the dispel route), not to Water Wall",
            Decide(blast with { OneWithNature = true, ReadyTempered = true, ReadyBorrow = true, SinceSummon = 12f }, cfg) is { ActionId: BST.Borrow, Reason: "crucible:borrow-wavekin" });
        var dying = Decide(blast with { PlayerHp = 150f, PlayerHpPercent = 6f, ReadyHealPotion = 46961, Slot1Beast = 1, PetObjectBeast = 1 }, cfg);
        Check("r4-reaction: 1.3-04 ... no dispel route and the character under the blast's largest hit (150 HP): the heal potion goes out",
            dying is { ActionId: 46961 }, $"{dying.ActionId}:{dying.Reason} [{dying.Declines}]");

        // ---- 3.4-07b petrify (Catoblepas' False Demon Eye, 4891, 9 s): the generic cleanse fires on any debuff the game flags cleansable. Synthetic: the log has 12 rows of
        // 4891 on the character (every one ran its full duration) and no cleanse decision. The case: petrify lands while Scouring Ash is held.
        var petrify = live50 with
        {
            CrucibleBoard = 3, CrucibleBattle = 4, CrucibleNeeds = BST_CrucibleLogic.FightNeeds(3, 4), PlayerHasCleansableDebuff = true,
            KinshipHeld = true, BeastModeResolved = BST.ScouringAsh, ReadyBeastMode = true, KinshipSlot = 1, CanWeave = true,
        };
        Check("r4-reaction: 3.4-07b petrify lands (flagged cleansable) while Scouring Ash is held: Scouring Ash goes out",
            Decide(petrify, cfg) is { ActionId: BST.ScouringAsh, Reason: "crucible:cleanse-scouringash" }, $"{Decide(petrify, cfg).ActionId}:{Decide(petrify, cfg).Reason}");
        Check("r4-reaction: 3.4-07b ... petrify is not a poison: the poison-resistance filter keeps it (4891 counts with poison resistance up, 5140 poison does not)",
            BST_CrucibleLogic.CleanseCounts(4891, true) && BST_CrucibleLogic.CleanseCounts(4891, false) && !BST_CrucibleLogic.CleanseCounts(5140, true));
        Check("r4-reaction: 3.4-07b ... not flagged cleansable by the game: no cleanse (the plugin never guesses)",
            Decide(petrify with { PlayerHasCleansableDebuff = false }, cfg).Reason != "crucible:cleanse-scouringash");
    }
}
