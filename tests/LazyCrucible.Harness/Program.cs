using LazyCrucible;

namespace LazyCrucible.Harness;

/// <summary>
///     Offline proof for LazyCrucible's familiar selection (moved out of GluttonyCombo's BST harness on
///     2026-09-21 with the code it tests). Pure logic only: the live layer (PetSelect) reads the game and
///     feeds these functions.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        Formation();
        FeedScreenReplay();
        RosterOverwriteReplay();
        PolicyCases.Run();
        ReplayCases.Run();
        GuideCases.Run();

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    private static void Formation()
    {
        Console.WriteLine("-- formation logic (latch, horn delta, basis, roster plan) --");
        var allRows = Enumerable.Range(1, BST_Beasts.Count).ToList();
        var slotsFull = BST_CrucibleAdvisor.PickSlots(1, 1, allRows, new Dictionary<int, int>());
        var allCandidates = new List<int>();
        for (var r = 1; r <= BST_Beasts.Count; r++) allCandidates.Add(r);
        var cov10 = BST_CrucibleAdvisor.PickSlotsCoverage(1, allCandidates, new Dictionary<int, int>(), 10);

        // --- PlanHornChanges + HpPercentByRow (autograb assignment planning) ---
        var planWant = slotsFull.Select(p => p.Row).ToList();
        Check("PlanHornChanges: all slots already correct → no calls",
            FormationLogic.PlanHornChanges(planWant, slotsFull).Count == 0);
        if (planWant.Count >= 2)
        {
            var oneWrong = new List<int>(planWant) { [0] = planWant[1] };
            var changes = FormationLogic.PlanHornChanges(oneWrong, slotsFull);
            Check("PlanHornChanges: one slot wrong → exactly that slot changes",
                changes.Count == 1 && changes[0].Slot == 0 && changes[0].FromRow == planWant[1]
                && changes[0].ToRow == planWant[0],
                string.Join(",", changes.Select(c => $"{c.Slot}:{c.FromRow}->{c.ToRow}")));
        }
        if (planWant.Count >= 1)
        {
            var knocked = new List<int>(planWant) { [0] = 0 };
            var replace = FormationLogic.PlanHornChanges(knocked, slotsFull);
            Check("PlanHornChanges: knocked-out slot occupant is replaced",
                replace.Count == 1 && replace[0].Slot == 0 && replace[0].FromRow == 0
                && replace[0].ToRow == planWant[0],
                string.Join(",", replace.Select(c => $"{c.Slot}:{c.FromRow}->{c.ToRow}")));
        }

        // --- Round-12 .225 late-run membership delta (Abort 1 / Abort 2 shapes) ---
        // Abort 1: pre horn rows 21,20,19 (sl=8.5.7) → coverage want 27,20,21. Slot planner
        // would re-toggle 20/21; membership delta must remove only 19 and add only 27.
        {
            var abort1Current = new List<int> { 21, 20, 19 };
            var abort1Picks = new List<CrucibleBeastPick>
            {
                new(27, 0, false, "coverage board 1, battle unidentified, Quelling Wave"),
                new(20, 0, false, "coverage board 1, battle unidentified"),
                new(21, 0, false, "coverage board 1, battle unidentified"),
            };
            var (a1Rem, a1Add) = FormationLogic.PlanHornMembershipDelta(abort1Current, abort1Picks);
            Check("PlanHornMembershipDelta: .225 abort1 overlapping horn → remove 19 only, add 27 only",
                a1Rem.Count == 1 && a1Rem[0] == 19
                && a1Add.Count == 1 && a1Add[0] == 27
                && !a1Add.Contains(20) && !a1Add.Contains(21) && !a1Rem.Contains(20) && !a1Rem.Contains(21),
                $"rem={string.Join(",", a1Rem)} add={string.Join(",", a1Add)}");
            var a1Slot = FormationLogic.PlanHornChanges(abort1Current, abort1Picks);
            Check("PlanHornChanges: .225 abort1 still reports slot rewrites (telemetry need=)",
                a1Slot.Count >= 2,
                string.Join(",", a1Slot.Select(c => $"{c.Slot}:{c.FromRow}->{c.ToRow}")));
        }
        // Abort 2: pre 28,22,12 (sl=0.1.3) → want 27,21,12. Must not re-toggle already-on 12.
        {
            var abort2Current = new List<int> { 28, 22, 12 };
            var abort2Picks = new List<CrucibleBeastPick>
            {
                new(27, 0, false, "coverage board 1, battle unidentified, Quelling Wave"),
                new(21, 0, false, "coverage board 1, battle unidentified"),
                new(12, 0, false, "coverage board 1, battle unidentified, blunt x2, crowd control"),
            };
            var (a2Rem, a2Add) = FormationLogic.PlanHornMembershipDelta(abort2Current, abort2Picks);
            Check("PlanHornMembershipDelta: .225 abort2 overlapping horn → remove 28+22, add 27+21, leave 12",
                a2Rem.Count == 2 && a2Rem.Contains(28) && a2Rem.Contains(22)
                && a2Add.Count == 2 && a2Add.Contains(27) && a2Add.Contains(21)
                && !a2Rem.Contains(12) && !a2Add.Contains(12),
                $"rem={string.Join(",", a2Rem)} add={string.Join(",", a2Add)}");
        }
        // Empty horn still plans all three adds (early-fight coverage fill path).
        {
            var (emptyRem, emptyAdd) = FormationLogic.PlanHornMembershipDelta(
                Array.Empty<int>(), slotsFull);
            Check("PlanHornMembershipDelta: empty horn → no removals, all desired adds",
                emptyRem.Count == 0 && emptyAdd.Count == slotsFull.Count
                && emptyAdd.SequenceEqual(slotsFull.Select(p => p.Row)),
                $"rem={string.Join(",", emptyRem)} add={string.Join(",", emptyAdd)}");
        }
        // Identified re-arm after leave_standing: PassDone on battleKey=-1, then detail settles → re-arm.
        {
            var unidentOpen = FormationLogic.NextFormationArm(
                default, screenOpen: true, battleKey: -1, surfaceKey: 1);
            var leftStanding = FormationLogic.MarkFormationPassDone(unidentOpen);
            var afterFocus = FormationLogic.NextFormationArm(
                leftStanding, screenOpen: true, battleKey: 5, surfaceKey: 1);
            Check("FormationArm: leave_standing PassDone then detail settles → re-armed (not aborted)",
                !FormationLogic.IsFormationArmed(leftStanding)
                && FormationLogic.IsFormationArmed(afterFocus)
                && afterFocus.BattleKey == 5 && !afterFocus.Aborted,
                $"left={leftStanding} after={afterFocus}");
            var abortedUnident = FormationLogic.MarkFormationAborted(unidentOpen);
            var blockedFocus = FormationLogic.NextFormationArm(
                abortedUnident, screenOpen: true, battleKey: 5, surfaceKey: 1);
            Check("FormationArm: .225 abort path blocks re-arm when detail settles (contrast)",
                !FormationLogic.IsFormationArmed(blockedFocus) && blockedFocus.Aborted,
                $"blocked={blockedFocus}");
        }


        // --- FormationArm re-arm (Dalamud-free phase decision) ---
        // Board surface (1): battle-key re-arm is a board rule (pre-entry never re-arms on a key change).
        var closed = default(FormationLogic.FormationArmState);
        var opened = FormationLogic.NextFormationArm(closed, screenOpen: true, battleKey: 10, surfaceKey: 1);
        Check("FormationArm: screen opens → armed",
            FormationLogic.IsFormationArmed(opened) && opened.BattleKey == 10);

        var afterPass = FormationLogic.MarkFormationPassDone(opened);
        var sameBattle = FormationLogic.NextFormationArm(afterPass, screenOpen: true, battleKey: 10, surfaceKey: 1);
        Check("FormationArm: pass done, same battle → not re-armed",
            !FormationLogic.IsFormationArmed(sameBattle) && sameBattle.PassDone);

        var battleChanged = FormationLogic.NextFormationArm(afterPass, screenOpen: true, battleKey: 20, surfaceKey: 1);
        Check("FormationArm: battle changes while screen open → re-armed",
            FormationLogic.IsFormationArmed(battleChanged) && battleChanged.BattleKey == 20 && !battleChanged.PassDone);

        var afterClose = FormationLogic.NextFormationArm(afterPass, screenOpen: false, battleKey: 10);
        var reopen = FormationLogic.NextFormationArm(afterClose, screenOpen: true, battleKey: 10, surfaceKey: 1);
        Check("FormationArm: screen closes and reopens → re-armed",
            !afterClose.ScreenOpen && FormationLogic.IsFormationArmed(reopen));

        var aborted = FormationLogic.MarkFormationAborted(opened);
        var abortSame = FormationLogic.NextFormationArm(aborted, screenOpen: true, battleKey: 99);
        Check("FormationArm: aborted phase stays closed until screen drops (battle change ignored)",
            !FormationLogic.IsFormationArmed(abortSame) && abortSame.Aborted);
        var abortCleared = FormationLogic.NextFormationArm(
            FormationLogic.NextFormationArm(aborted, screenOpen: false, battleKey: 0),
            screenOpen: true, battleKey: 11);
        Check("FormationArm: aborted phase re-arms after screen drop",
            FormationLogic.IsFormationArmed(abortCleared));

        // --- Round-7 surface / pre-entry arming ---
        var preentryOpen = FormationLogic.NextFormationArm(closed, screenOpen: true, battleKey: -1, surfaceKey: 0);
        Check("FormationArm: screen opens outside any board (preentry surface) → armed",
            FormationLogic.IsFormationArmed(preentryOpen) && preentryOpen.SurfaceKey == 0);

        // "No roster" is a live-layer gate (armed requires partyCount>0); the latch itself still arms on screen open.
        // Territory change alone must not re-arm: same surface + same battle key, pass already done.
        var preentryDone = FormationLogic.MarkFormationPassDone(preentryOpen);
        var territoryOnly = FormationLogic.NextFormationArm(preentryDone, screenOpen: true, battleKey: -1, surfaceKey: 0);
        Check("FormationArm: territory change alone (same surface+battle key) → not re-armed",
            !FormationLogic.IsFormationArmed(territoryOnly) && territoryOnly.PassDone);

        var surfaceFlip = FormationLogic.NextFormationArm(preentryDone, screenOpen: true, battleKey: -1, surfaceKey: 1);
        Check("FormationArm: surface preentry→board → re-armed",
            FormationLogic.IsFormationArmed(surfaceFlip) && surfaceFlip.SurfaceKey == 1 && !surfaceFlip.PassDone);


        // --- Round-8 horn index basis + party index resolve (autograb write route) ---
        var partySample = new List<int> { 17, 28, 35, 22, 18, 6, 1, 12, 29, 27 };
        Check("IsHornIndexBasis: empty with party → horn-capable",
            FormationLogic.IsHornIndexBasis(Array.Empty<int>(), partySample.Count));
        Check("IsHornIndexBasis: 0.1.4 indices → horn",
            FormationLogic.IsHornIndexBasis(new[] { 0, 1, 4 }, partySample.Count));
        Check("IsHornIndexBasis: ten familiar ids → not horn",
            !FormationLogic.IsHornIndexBasis(partySample, partySample.Count));
        Check("IsHornIndexBasis: pet id 27 as lone value with party 10 → not horn",
            !FormationLogic.IsHornIndexBasis(new[] { 27 }, partySample.Count));
        // .222 live defect: transient horn-shaped sl=3.5.6 at Bentbranch roster-menu-open must not
        // take the horn write path (screen/shape before size). Same vector on a board/ActivePet screen stays horn.
        Check("IsHornWriteBasis: .222 transient 3.5.6 on roster surface → not horn",
            !FormationLogic.IsHornWriteBasis(new[] { 3, 5, 6 }, partySample.Count, rosterSurface: true));
        Check("IsHornWriteBasis: .222 transient 3.5.6 on horn/board surface → horn",
            FormationLogic.IsHornWriteBasis(new[] { 3, 5, 6 }, partySample.Count, rosterSurface: false));
        Check("IsHornWriteBasis: empty on roster surface → not horn (wait settle)",
            !FormationLogic.IsHornWriteBasis(Array.Empty<int>(), partySample.Count, rosterSurface: true));
        Check("IsHornWriteBasis: empty on horn surface → horn-capable",
            FormationLogic.IsHornWriteBasis(Array.Empty<int>(), partySample.Count, rosterSurface: false));
        Check("FindPartyIndex: present row → index",
            FormationLogic.FindPartyIndex(partySample, 18) == 4
            && FormationLogic.FindPartyIndex(partySample, 17) == 0);
        Check("FindPartyIndex: missing row → -1",
            FormationLogic.FindPartyIndex(partySample, 99) == -1);
        var resolved = FormationLogic.ResolveHornPetRows(new[] { 0, 1, 4 }, partySample);
        Check("ResolveHornPetRows: indices 0.1.4 → pets 17.28.18",
            resolved.Count == 3 && resolved[0] == 17 && resolved[1] == 28 && resolved[2] == 18);
        Check("SelectionEquals: match / mismatch",
            FormationLogic.SelectionEquals(new[] { 0, 1, 4 }, new[] { 0, 1, 4 })
            && !FormationLogic.SelectionEquals(new[] { 0, 1, 4 }, new[] { 0 })
            && !FormationLogic.SelectionEquals(new[] { 0, 1 }, new[] { 0, 1, 4 }));



        var covPicks = cov10;
        var desired10 = cov10.ConvertAll(p => p.Row);
        // Case 1: already matching -> 0 changes
        var changesMatch = FormationLogic.PlanRosterChanges(desired10, covPicks);
        Check("PlanRosterChanges: identical roster → 0 changes", changesMatch.Count == 0);

        // Case 2: completely disjoint roster
        var actualDisjoint = allCandidates.Where(c => !desired10.Contains(c)).Take(10).ToList();
        var changesDisjoint = FormationLogic.PlanRosterChanges(actualDisjoint, covPicks);
        Check("PlanRosterChanges: disjoint roster → 10 removes, 10 adds",
            changesDisjoint.Count == 20
            && changesDisjoint.Count(c => c.FromRow != 0 && c.ToRow == 0) == 10
            && changesDisjoint.Count(c => c.FromRow == 0 && c.ToRow != 0) == 10);

        // Case 3: 7 matching, 3 different
        var partialSample = new List<int>(desired10.Take(7));
        partialSample.AddRange(actualDisjoint.Take(3));
        var changesPartial = FormationLogic.PlanRosterChanges(partialSample, covPicks);
        Check("PlanRosterChanges: 7 matching + 3 different → 3 removes, 3 adds",
            changesPartial.Count == 6
            && changesPartial.Count(c => c.FromRow != 0 && c.ToRow == 0) == 3
            && changesPartial.Count(c => c.FromRow == 0 && c.ToRow != 0) == 3);
    }


    /// <summary>
    ///     Replay of the six 1.0.4.227-.229 `readback=fail` passes (2026-09-21 00:45:07, 00:45:15, 11:43:58,
    ///     11:44:02, 11:44:12, 12:36:01 ET). Every one ran with XBMContentsItemShop open: the shop opens
    ///     XBMPetParty as the Beast Feed target picker (AtkValues [2]=3 [3]=0), which reuses SelectedPetIds
    ///     as a one-familiar selection. The pass read that lone index as a Battlehorn (basis=horn, bt=5 = the
    ///     stage focus left on the elite fought just before the shop), fired horn toggles plus
    ///     ApplyPetSelection into the feed flow and aborted. The feed picker must never be written.
    /// </summary>
    private static void FeedScreenReplay()
    {
        Console.WriteLine("-- feed screen replay (bt=5 readback=fail, .227-.229) --");
        var party = new List<int> { 28, 22, 18, 27, 20, 5, 21, 41, 40, 39 };

        // Exact states read at the six failing passes: board surface, PetParty open, ActivePet closed,
        // one index in SelectedPetIds (the feed target the player had just picked), addon mode 3 / sub 0.
        var failing = new (string When, int Selected)[]
        {
            ("00:45:06.998 sl=9", 9), ("00:45:15.008 sl=7", 7), ("11:43:58.471 sl=8", 8),
            ("11:44:02.446 sl=2", 2), ("11:44:12.540 sl=6", 6), ("12:36:01.147 sl=1", 1),
        };
        foreach (var (when, sel) in failing)
        {
            var d = FormationLogic.DecideFormationWrite(new(
                PetPartyOpen: true, ActivePetOpen: false, PreEntrySurface: false,
                AddonMode: 3, AddonSubMode: 0, SelectedPetIds: new[] { sel }, PartyCount: party.Count));
            Check($"Feed picker {when}: no write", d.Write == FormationLogic.FormationWrite.None && d.Reason == "feed_or_camp",
                $"{d.Write}/{d.Reason}");
        }

        // First frame of the shop's feed open (00:45:01.274): only [3]=0 populated, SelectedPetIds still
        // holds the previous horn 8.7.3. SubMode 0 alone already identifies the feed flow.
        var firstFrame = FormationLogic.DecideFormationWrite(new(
            true, false, false, AddonMode: -1, AddonSubMode: 0, new[] { 8, 7, 3 }, party.Count));
        Check("Feed picker first frame (only SubMode populated, stale horn 8.7.3): no write",
            firstFrame.Write == FormationLogic.FormationWrite.None, $"{firstFrame.Write}/{firstFrame.Reason}");

        // Campsite rest picker (2026-09-21 12:56:54 ET): the same addon in agent mode 4, [2]=4 [3]=0.
        var camp = FormationLogic.DecideFormationWrite(new(
            true, false, false, AddonMode: 4, AddonSubMode: 0, new[] { 0, 4 }, party.Count));
        Check("Campsite rest picker (mode 4): no write",
            camp.Write == FormationLogic.FormationWrite.None && camp.Reason == "feed_or_camp", $"{camp.Write}/{camp.Reason}");

        // Regression guards: the Battlehorn preview must still be written, including on its first frame.
        var hornEmpty = FormationLogic.DecideFormationWrite(new(
            true, false, false, AddonMode: 2, AddonSubMode: 1, Array.Empty<int>(), party.Count));
        Check("Horn preview after the shop (00:45:44.353, empty horn, mode 2): horn write",
            hornEmpty.Write == FormationLogic.FormationWrite.Horn, $"{hornEmpty.Write}/{hornEmpty.Reason}");
        var hornFirstFrame = FormationLogic.DecideFormationWrite(new(
            true, false, false, AddonMode: -1, AddonSubMode: 1, new[] { 8, 7, 3 }, party.Count));
        Check("Horn preview first frame (only SubMode 1 populated): horn write, no added delay",
            hornFirstFrame.Write == FormationLogic.FormationWrite.Horn, $"{hornFirstFrame.Write}/{hornFirstFrame.Reason}");
        var rosterList = FormationLogic.DecideFormationWrite(new(
            true, false, true, AddonMode: 0, AddonSubMode: 1, party, party.Count));
        Check("Bentbranch roster list (mode 0, ten ids): roster write",
            rosterList.Write == FormationLogic.FormationWrite.Roster, $"{rosterList.Write}/{rosterList.Reason}");
        var rosterTransient = FormationLogic.DecideFormationWrite(new(
            true, false, true, AddonMode: 0, AddonSubMode: 1, new[] { 3, 5, 6 }, party.Count));
        Check("Bentbranch roster list transient 3.5.6 (.222): wait, never horn",
            rosterTransient.Write == FormationLogic.FormationWrite.Wait, $"{rosterTransient.Write}/{rosterTransient.Reason}");

        // Notebook "Team Composition" (XBMActivePet with no XBMPetParty, Bentbranch 00:06:01.467): the
        // overworld familiar team, not a run screen. The pass armed there on stale run data.
        var notebookTeam = FormationLogic.DecideFormationWrite(new(
            PetPartyOpen: false, ActivePetOpen: true, PreEntrySurface: true,
            AddonMode: -1, AddonSubMode: -1, new[] { 1, 7, 4 }, party.Count));
        Check("Notebook team screen (ActivePet without PetParty): no write",
            notebookTeam.Write == FormationLogic.FormationWrite.None, $"{notebookTeam.Write}/{notebookTeam.Reason}");

        // Mode values the pass does not write.
        Check("Classify: [2]=3 [3]=0 -> FeedOrCamp; [2]=4/5 -> FeedOrCamp; [2]=1 -> Other; [2]=0 -> Roster; [2]=2 -> Horn",
            FormationLogic.ClassifyPetPartyScreen(3, 0) == FormationLogic.PetPartyScreen.FeedOrCamp
            && FormationLogic.ClassifyPetPartyScreen(4, 0) == FormationLogic.PetPartyScreen.FeedOrCamp
            && FormationLogic.ClassifyPetPartyScreen(5, 1) == FormationLogic.PetPartyScreen.FeedOrCamp
            && FormationLogic.ClassifyPetPartyScreen(1, 1) == FormationLogic.PetPartyScreen.Other
            && FormationLogic.ClassifyPetPartyScreen(0, 1) == FormationLogic.PetPartyScreen.Roster
            && FormationLogic.ClassifyPetPartyScreen(2, 1) == FormationLogic.PetPartyScreen.Horn
            && FormationLogic.ClassifyPetPartyScreen(-1, 1) == FormationLogic.PetPartyScreen.RosterOrHorn
            && FormationLogic.ClassifyPetPartyScreen(-1, -1) == FormationLogic.PetPartyScreen.Settling);
    }

    /// <summary>
    ///     Replay of the 1.0.4.229 Bentbranch roster overwrite (2026-09-21 12:50:24-12:50:36 ET). The pass found
    ///     the roster already correct (12:50:24.215, pass done). AutoDuty, running board 1 with a leveling team
    ///     (its log: "Board is holding 10; clearing it before reading more ranks", 12:50:24.320 / 26.066 /
    ///     27.870), then cleared the roster (pet-party events kind 0 [2,0], kind 7, kind 8) and rebuilt it; each
    ///     time the rebuild reached four familiars GluttonyCombo's writer replaced it with its own ten (12:50:25.7,
    ///     27.5, 30.2, 36.3) until the option was switched off (12:52:25 note=off). Mechanism: while the roster
    ///     is empty the live pass fed battle key 0 to the latch, the key returned to -1 as the rebuild started,
    ///     and a key change re-armed the writer. Any edit the pass did not send (player or another tool) wins.
    /// </summary>
    private static void RosterOverwriteReplay()
    {
        Console.WriteLine("-- Bentbranch roster overwrite replay (12:50, .229) --");
        const int preentry = 0;
        var s = FormationLogic.NextFormationArm(default, screenOpen: true, battleKey: -1, surfaceKey: preentry);
        Check("Roster menu opens: armed", FormationLogic.IsFormationArmed(s));
        s = FormationLogic.MarkFormationPassDone(s); // 12:50:24.215 already_correct
        s = FormationLogic.NextFormationArm(s, true, battleKey: 0, surfaceKey: preentry); // party=0 (cleared)
        s = FormationLogic.NextFormationArm(s, true, battleKey: -1, surfaceKey: preentry); // rebuild starts
        Check("Roster cleared and rebuilt on the same open: writer stays done (no overwrite)",
            !FormationLogic.IsFormationArmed(s), s.ToString());

        // The clear events at 12:50:24.320-.537 (AutoDuty's) are selection edits; hovers and navigation are not.
        Check("IsSelectionEditEvent: pp kind 0 [2,0] roster select, kind 7, kind 8, kind 5, kind 16 → edit",
            FormationLogic.IsSelectionEditEvent("pp", 0, 2, 2)
            && FormationLogic.IsSelectionEditEvent("pp", 7, 3, 0)
            && FormationLogic.IsSelectionEditEvent("pp", 8, 1, 0)
            && FormationLogic.IsSelectionEditEvent("pp", 5, 3, 0)
            && FormationLogic.IsSelectionEditEvent("pp", 16, 1, 0));
        Check("IsSelectionEditEvent: pp kind 0 [1,idx] horn toggle → edit",
            FormationLogic.IsSelectionEditEvent("pp", 0, 2, 1));
        Check("IsSelectionEditEvent: pp single-Int navigation [5]/[0]/[-2], nb hover [5,u], nb kind 5 add",
            !FormationLogic.IsSelectionEditEvent("pp", 0, 1, 5)
            && !FormationLogic.IsSelectionEditEvent("pp", 0, 1, -2)
            && !FormationLogic.IsSelectionEditEvent("pp", 1, 1, 0)
            && !FormationLogic.IsSelectionEditEvent("nb", 0, 2, 5)
            && FormationLogic.IsSelectionEditEvent("nb", 5, 1, 0));

        // Player edit on a board: stands down for the rest of the open even when the focus settles,
        // and the next open (next formation) is automatic again.
        var h = FormationLogic.NextFormationArm(default, true, battleKey: 5, surfaceKey: 1);
        h = FormationLogic.MarkFormationPassDone(h);
        h = FormationLogic.MarkPlayerEdited(h);
        var hKey = FormationLogic.NextFormationArm(h, true, battleKey: 6, surfaceKey: 1);
        Check("Player edit on the horn preview: battle-key change does not re-arm",
            !FormationLogic.IsFormationArmed(hKey) && hKey.PlayerEdited, hKey.ToString());
        var hNext = FormationLogic.NextFormationArm(
            FormationLogic.NextFormationArm(hKey, false, 0, 1), true, battleKey: 7, surfaceKey: 1);
        Check("Player edit clears when the screen closes: next formation armed",
            FormationLogic.IsFormationArmed(hNext) && !hNext.PlayerEdited, hNext.ToString());
        Check("MarkPlayerEdited on a closed screen is a no-op",
            !FormationLogic.MarkPlayerEdited(default).PlayerEdited);

        // Roster edited by the player or another tool this Bentbranch visit: never rewritten on a later open.
        var party = new List<int> { 28, 22, 18, 27, 20, 5, 21, 41, 40, 39 };
        var owned = FormationLogic.DecideFormationWrite(new(
            true, false, true, AddonMode: 0, AddonSubMode: 1, party, party.Count, RosterPlayerOwned: true));
        Check("Roster menu reopened after a manual edit: no write",
            owned.Write == FormationLogic.FormationWrite.None && owned.Reason == "player_owned", $"{owned.Write}/{owned.Reason}");
    }

    internal static void Check(string what, bool ok, string? detail = null)
    {
        if (ok)
        {
            _pass++;
            if (detail is not null && Environment.GetEnvironmentVariable("LC_VERBOSE") == "1")
                Console.WriteLine($"  ok {what}  [{detail}]");
            return;
        }
        _fail++;
        Console.WriteLine($"FAIL {what}{(detail is null ? "" : $"  [{detail}]")}");
    }
}
