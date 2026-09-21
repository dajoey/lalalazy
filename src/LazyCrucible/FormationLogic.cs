using System.Collections.Generic;
using Lalalazy.Crucible;

namespace LazyCrucible;

/// <summary>
///     Formation-screen decisions for the familiar autograb: when a pass may run (latch), what the open screen
///     is for (gate), what to toggle (membership delta / roster plan) and how to read the selection vectors.
///     PURE: no Dalamud or game types; compiled into tests/LazyCrucible.Harness. The live layer
///     (<see cref="PetSelect"/>) feeds it values read from AgentXBMPetParty / the XBMPetParty addon.
/// </summary>
internal static class FormationLogic
{
    /// <summary>
    ///     Latch for one Crucible formation-phase autograb pass. PURE: the live layer feeds screen-open and
    ///     identified-battle signals; this type never reads game memory. <see cref="Aborted"/> stays set until
    ///     the screen closes (re-arm on battle change does not clear an abort).
    ///     <see cref="SurfaceKey"/> distinguishes pre-entry from in-board so a Bentbranch pass and the first
    ///     in-board pass are two phases.
    /// </summary>
    public readonly record struct FormationArmState(
        bool ScreenOpen, int BattleKey, bool PassDone, bool Aborted, int SurfaceKey = 0, bool PlayerEdited = false);

    /// <summary> Surface key of the pre-entry (Bentbranch) roster menu; the board surface is 1. </summary>
    public const int PreEntrySurfaceKey = 0;

    /// <summary>
    ///     Advance the formation-phase latch. Screen close clears everything. Screen open after close or a
    ///     surface change re-arms the pass. On a board, a battle-key change while the screen stays open
    ///     re-arms too (the stage focus settles ~40 ms after the horn preview opens). On the pre-entry roster
    ///     menu a key change never re-arms: the key there is only a board guess and flapped -1 -> 0 -> -1
    ///     while the roster was cleared and rebuilt by hand (live 12:50, four overwrites). An aborted phase
    ///     or one the player edited stays done until the screen closes. Territory alone is not a re-arm
    ///     signal. PURE.
    /// </summary>
    public static FormationArmState NextFormationArm(
        FormationArmState prev, bool screenOpen, int battleKey, int surfaceKey = 0)
    {
        if (!screenOpen)
            return new(false, 0, false, false, 0);

        if (!prev.ScreenOpen)
            return new(true, battleKey, false, false, surfaceKey);

        var standDown = prev.Aborted || prev.PlayerEdited;
        var keyRearms = battleKey != prev.BattleKey && surfaceKey != PreEntrySurfaceKey;
        if ((keyRearms || surfaceKey != prev.SurfaceKey) && !standDown)
            return new(true, battleKey, false, false, surfaceKey);

        return new(true, battleKey, prev.PassDone, prev.Aborted, surfaceKey, prev.PlayerEdited);
    }

    /// <summary>
    ///     The player (or anything that is not this pass) changed the selection on the open screen: the pass
    ///     stands down until the screen closes, whatever the battle key does. PURE.
    /// </summary>
    public static FormationArmState MarkPlayerEdited(in FormationArmState s) =>
        s.ScreenOpen ? s with { PassDone = true, PlayerEdited = true } : s;

    /// <summary>
    ///     Whether a ReceiveEvent on a familiar agent that the pass did not send is a selection edit (as
    ///     opposed to navigation, hover or close). PURE. <paramref name="agent"/> is "pp" (AgentXBMPetParty)
    ///     or "nb" (AgentXBMMonsterNotebook); <paramref name="firstInt"/> is value [0] as Int (or null).
    ///     Live evidence: pp kind 0 [1, index] = Battlehorn toggle / feed target; pp kind 0 [2, index] =
    ///     roster row select; pp kind 5 = roster remove confirm; pp kind 7 then 8 = roster clear / preset;
    ///     pp kind 16 = preset apply; nb kind 5 = notebook add/remove (calls TogglePet). Single-Int kind 0
    ///     events ([5], [6], [0], [-2]) and notebook kind 0 hovers are navigation.
    /// </summary>
    public static bool IsSelectionEditEvent(string agent, ulong kind, uint valueCount, int? firstInt)
    {
        if (agent == "pp")
        {
            if (kind == 0)
                return valueCount >= 2 && firstInt is 1 or 2;
            return kind is 5 or 7 or 8 or 16;
        }
        if (agent == "nb")
            return kind == 5;
        return false;
    }

    /// <summary> Whether the assign pass may run under the current latch. PURE. </summary>
    public static bool IsFormationArmed(in FormationArmState s) => s.ScreenOpen && !s.PassDone && !s.Aborted;

    /// <summary> Mark a completed (including soft) pass for this phase. PURE. </summary>
    public static FormationArmState MarkFormationPassDone(in FormationArmState s) => s with { PassDone = true };

    /// <summary> Mark a hard abort; stays closed until the screen drops. PURE. </summary>
    public static FormationArmState MarkFormationAborted(in FormationArmState s) =>
        s with { PassDone = true, Aborted = true };

    /// <summary> One planned Battlehorn rewrite: slot index 0..2, previous row (0 = empty), desired row. </summary>
    public readonly record struct HornSlotChange(int Slot, int FromRow, int ToRow);

    /// <summary>
    ///     Which horn slots actually need a write given the current horn occupants as familiar rows and a
    ///     <see cref="BST_CrucibleAdvisor.PickSlots"/> result. PURE: no game types. Slots already matching are omitted so a
    ///     second pass does not oscillate. Empty desired slots (fewer than 3 picks) are left alone.
    ///     Callers on the live horn screen must first map <c>SelectedPetIds</c> indices through
    ///     <see cref="ResolveHornPetRows"/>.
    /// </summary>
    public static List<HornSlotChange> PlanHornChanges(
        IReadOnlyList<int> currentSelectedPetIds,
        IReadOnlyList<CrucibleBeastPick> picks)
    {
        var changes = new List<HornSlotChange>(3);
        for (var slot = 0; slot < 3; slot++)
        {
            var want = slot < picks.Count ? picks[slot].Row : 0;
            if (want == 0)
                continue;
            var have = slot < currentSelectedPetIds.Count ? currentSelectedPetIds[slot] : 0;
            if (have != want)
                changes.Add(new(slot, have, want));
        }
        return changes;
    }

    /// <summary>
    ///     Membership delta for the live Battlehorn toggle route. PURE. EventToggle flips membership, so a
    ///     slot-ordered rewrite that re-toggles an already-selected familiar deselects it (live .225 late-run
    ///     aborts). Removals are current rows not in the desired set; additions are desired rows missing from
    ///     current. Order is left to ApplyPetSelection when membership alone is insufficient.
    /// </summary>
    public static (List<int> RemoveRows, List<int> AddRows) PlanHornMembershipDelta(
        IReadOnlyList<int> currentHornRows,
        IReadOnlyList<CrucibleBeastPick> picks)
    {
        var desired = new List<int>(3);
        for (var i = 0; i < picks.Count && desired.Count < 3; i++)
        {
            var row = picks[i].Row;
            if (row is >= 1 and <= BST_Beasts.Count && !desired.Contains(row))
                desired.Add(row);
        }

        var currentSet = new HashSet<int>();
        for (var i = 0; i < currentHornRows.Count; i++)
        {
            var row = currentHornRows[i];
            if (row is >= 1 and <= BST_Beasts.Count)
                currentSet.Add(row);
        }

        var remove = new List<int>(3);
        foreach (var row in currentSet)
        {
            if (!desired.Contains(row))
                remove.Add(row);
        }

        var add = new List<int>(3);
        foreach (var row in desired)
        {
            if (!currentSet.Contains(row))
                add.Add(row);
        }

        return (remove, add);
    }

    /// <summary> One planned Crucible run roster rewrite: familiar to remove (FromRow, 0 if none), familiar to add (ToRow, 0 if none). </summary>
    public readonly record struct RosterSlotChange(int FromRow, int ToRow);

    /// <summary>
    ///     Which roster familiars actually need a write given the current roster occupants as familiar rows and a
    ///     <see cref="BST_CrucibleAdvisor.PickSlotsCoverage"/> result. PURE: no game types. Familiars already present are omitted so a
    ///     second pass does not oscillate. Any current familiar not in desired is planned for removal; any desired
    ///     familiar not in current is planned for addition.
    /// </summary>
    public static List<RosterSlotChange> PlanRosterChanges(
        IReadOnlyList<int> currentRosterPetIds,
        IReadOnlyList<CrucibleBeastPick> picks)
    {
        var desired = new List<int>(picks.Count);
        for (var i = 0; i < picks.Count; i++)
        {
            if (picks[i].Row >= 1 && picks[i].Row <= BST_Beasts.Count && !desired.Contains(picks[i].Row))
                desired.Add(picks[i].Row);
        }

        var changes = new List<RosterSlotChange>();
        // Extras in current that are not in desired -> remove (ToRow = 0)
        foreach (var cur in currentRosterPetIds)
        {
            if (cur >= 1 && cur <= BST_Beasts.Count && !desired.Contains(cur))
                changes.Add(new RosterSlotChange(cur, 0));
        }
        // Missing in current that are in desired -> add (FromRow = 0)
        foreach (var want in desired)
        {
            if (want >= 1 && want <= BST_Beasts.Count && !currentRosterPetIds.Contains(want))
                changes.Add(new RosterSlotChange(0, want));
        }
        return changes;
    }

    /// <summary>
    ///     Whether <paramref name="selectedPetIds"/> is the battlehorn index basis (each value indexes
    ///     <paramref name="partyRows"/>) rather than a roster of familiar row ids. PURE.
    ///     Empty is treated as horn-capable when <paramref name="partyCount"/> is at least 1 so a fresh
    ///     empty horn can be filled; a vector longer than 3, or any out-of-range value, is not horn basis.
    ///     Callers that know the open screen must use <see cref="IsHornWriteBasis"/> so a roster-menu
    ///     surface cannot be mis-routed by a transient ≤3 index-looking vector.
    /// </summary>
    public static bool IsHornIndexBasis(IReadOnlyList<int> selectedPetIds, int partyCount)
    {
        if (partyCount <= 0)
            return false;
        if (selectedPetIds.Count > 3)
            return false;
        for (var i = 0; i < selectedPetIds.Count; i++)
        {
            var v = selectedPetIds[i];
            if (v < 0 || v >= partyCount)
                return false;
        }
        return true;
    }

    /// <summary>
    ///     Whether the Auto-fill write path may treat <paramref name="selectedPetIds"/> as battlehorn
    ///     indices. PURE. Screen/shape is checked before the size/index test: the Bentbranch entry
    ///     roster (pre-entry familiar party without the three-slot ActivePet UI) holds familiar row
    ///     ids in SelectedPetIds once settled, and a transient ≤3 index-looking read there must not
    ///     take the horn path (live .222 defect: sl=3.5.6 at roster-menu-open → basis=horn →
    ///     readback=fail, undamaged). Board surfaces and ActivePet keep the index-size test alone.
    /// </summary>
    public static bool IsHornWriteBasis(IReadOnlyList<int> selectedPetIds, int partyCount, bool rosterSurface)
    {
        if (rosterSurface)
            return false;
        return IsHornIndexBasis(selectedPetIds, partyCount);
    }

    /// <summary> What the open familiar-party screen is for, from the addon's own mode values. </summary>
    public enum PetPartyScreen
    {
        /// <summary> Mode values not populated yet (first frame of an open). </summary>
        Settling,
        /// <summary> SubMode 1 but Mode not populated yet: roster list or horn preview, not feeding. </summary>
        RosterOrHorn,
        /// <summary> Mode 0: the ten-familiar run roster list (Bentbranch entry menu). </summary>
        Roster,
        /// <summary> Mode 2: party preview from content, where the three Battlehorn slots are assigned. </summary>
        Horn,
        /// <summary> Modes 3-5 or SubMode 0: the shop's feed flow reusing the same agent. Never written. </summary>
        Feed,
        /// <summary> Any other mode (1 = party preview without content). Never written. </summary>
        Other,
    }

    /// <summary> What the formation pass may do this tick. </summary>
    public enum FormationWrite
    {
        None,
        Wait,
        Roster,
        Horn,
    }

    /// <summary>
    ///     Inputs to <see cref="DecideFormationWrite"/>. AddonMode / AddonSubMode are XBMPetParty AtkValues
    ///     [2] / [3] (-1 = not populated), which mirror AgentXBMPetParty.Mode / SubMode.
    /// </summary>
    public readonly record struct FormationGateInput(
        bool PetPartyOpen,
        bool ActivePetOpen,
        bool PreEntrySurface,
        int AddonMode,
        int AddonSubMode,
        IReadOnlyList<int> SelectedPetIds,
        int PartyCount,
        bool RosterPlayerOwned = false);

    /// <summary>
    ///     Classify the open XBMPetParty screen from AtkValues [2] (Mode) and [3] (SubMode). PURE.
    ///     Live evidence (2026-09-20/21, every open in five runs): Bentbranch roster list [2]=0 [3]=1,
    ///     in-board Battlehorn preview [2]=2 [3]=1, shop feed picker [2]=3 [3]=0 — the same values
    ///     ClientStructs PR #1952 documents for AgentXBMPetParty.Mode (0 pet list, 1 party preview,
    ///     2 party preview from content, 3 buy feed, 4 buy feed result, 5 feed detail) and SubMode (0 for
    ///     modes 3 and 4). On the first frame of an open only [3] is populated.
    /// </summary>
    public static PetPartyScreen ClassifyPetPartyScreen(int addonMode, int addonSubMode)
    {
        // Any feed signal wins: SubMode 0 is populated on the first frame of the shop's open.
        if (addonSubMode == 0 || addonMode is 3 or 4 or 5)
            return PetPartyScreen.Feed;
        if (addonSubMode < 0)
            return PetPartyScreen.Settling;
        return addonMode switch
        {
            < 0 => PetPartyScreen.RosterOrHorn,
            0 => PetPartyScreen.Roster,
            2 => PetPartyScreen.Horn,
            _ => PetPartyScreen.Other,
        };
    }

    /// <summary>
    ///     The formation-pass gate. PURE. Returns the write the live pass may attempt and a short reason
    ///     for telemetry. Writes need the XBMPetParty addon itself open and its mode to name the roster
    ///     list (pre-entry only) or the Battlehorn preview (board only); the shop's feed picker, the
    ///     notebook's team screen (XBMActivePet alone) and unknown modes are never written. While only
    ///     SubMode 1 is populated, the previous screen/shape rules decide (no added delay on the horn).
    /// </summary>
    public static (FormationWrite Write, string Reason) DecideFormationWrite(in FormationGateInput g)
    {
        if (!g.PetPartyOpen)
            return (FormationWrite.None, g.ActivePetOpen ? "activepet_only" : "closed");
        if (g.PartyCount <= 0)
            return (FormationWrite.None, "no_party");

        var screen = ClassifyPetPartyScreen(g.AddonMode, g.AddonSubMode);
        switch (screen)
        {
            case PetPartyScreen.Feed:
                return (FormationWrite.None, "feed");
            case PetPartyScreen.Other:
                return (FormationWrite.None, "other_mode");
            case PetPartyScreen.Settling:
                return (FormationWrite.Wait, "settling");
            case PetPartyScreen.Roster when !g.PreEntrySurface:
                return (FormationWrite.None, "roster_mode_on_board");
            case PetPartyScreen.Horn when g.PreEntrySurface:
                return (FormationWrite.None, "horn_mode_preentry");
        }

        // A populated mode names the surface outright; before it populates, the screen/shape rule decides.
        var rosterSurface = screen switch
        {
            PetPartyScreen.Roster => true,
            PetPartyScreen.Horn => false,
            _ => g.PreEntrySurface && !g.ActivePetOpen,
        };
        if (IsHornWriteBasis(g.SelectedPetIds, g.PartyCount, rosterSurface))
            return (FormationWrite.Horn, "horn");
        if (!rosterSurface)
            return (FormationWrite.Wait, "wait_horn_basis");
        if (g.RosterPlayerOwned)
            return (FormationWrite.None, "player_owned");
        if (g.SelectedPetIds.Count is > 0 and <= 3)
            return (FormationWrite.Wait, "wait_roster_settle");
        return (FormationWrite.Roster, "roster");
    }

    /// <summary>
    ///     Map horn-slot indices in <paramref name="selectedPetIds"/> through the party roster to familiar
    ///     rows. Out-of-range indices become 0. PURE.
    /// </summary>
    public static List<int> ResolveHornPetRows(IReadOnlyList<int> selectedPetIds, IReadOnlyList<int> partyRows)
    {
        var rows = new List<int>(selectedPetIds.Count);
        for (var i = 0; i < selectedPetIds.Count; i++)
        {
            var idx = selectedPetIds[i];
            rows.Add(idx >= 0 && idx < partyRows.Count ? partyRows[idx] : 0);
        }
        return rows;
    }

    /// <summary>
    ///     Index of <paramref name="petRow"/> in <paramref name="partyRows"/>, or -1. PURE.
    /// </summary>
    public static int FindPartyIndex(IReadOnlyList<int> partyRows, int petRow)
    {
        if (petRow is < 1 or > BST_Beasts.Count)
            return -1;
        for (var i = 0; i < partyRows.Count; i++)
            if (partyRows[i] == petRow)
                return i;
        return -1;
    }

    /// <summary>
    ///     Whether two selection vectors match (same length and values). PURE. Used for abort-restore.
    /// </summary>
    public static bool SelectionEquals(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }
}
