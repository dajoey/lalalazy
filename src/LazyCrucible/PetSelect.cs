using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static ECommons.GenericHelpers;
using static Lalalazy.Crucible.BST_CrucibleAdvisor;
using static LazyCrucible.FormationLogic;

namespace LazyCrucible;

/// <summary>
///     Crucible familiar selection (live). Moved from GluttonyCombo's BST_CruciblePetSelect on 2026-09-21 with its
///     telemetry grammar unchanged (<c>PS|</c> / <c>PSP|</c>). Arms when a familiar-selection addon is open
///     (<c>XBMActivePet</c> or <c>XBMPetParty</c>) and the run roster is readable — never on territory and
///     never on StageMode. Writes only when <c>XBMPetParty</c> itself is open and its mode values name the
///     roster list (<see cref="Configuration.AutoRoster"/>) or the Battlehorn preview
///     (<see cref="Configuration.AutoHorns"/>) — <see cref="DecideFormationWrite"/>; the shop's Beast Feed
///     picker and the notebook's team screen are never written. Ranks via <see cref="PickSlots"/> or
///     <see cref="PickSlotsCoverage"/>; on the horn screen assigns through ReceiveEvent kind 0 with a
///     <em>SelectedPets index</em> (not a familiar id), with SelectedPetIds read-back and abort-restore. Any
///     selection edit it did not send stands the pass down for that screen (the player's edit wins).
/// </summary>
internal static unsafe class PetSelect
{
    // AgentXBMPetParty offsets (PR #1952 / DailyRoutines production).
    private const int PartyPetListAddonId = 0x60;
    private const int PartyMode = 0x64;    // PR #1952 Mode (telemetry only; the addon's AtkValue [2] decides)
    private const int PartySubMode = 0x68; // PR #1952 SubMode (telemetry only; the addon's AtkValue [3] decides)
    private const int PartyContentId = 0x70;
    private const int PartySelectedPets = 0x78;
    private const int PartySelectedPetIds = 0x90;
    private const int PartyCandidatePets = 0xA8;
    private const int PartyCurrentHp = 0xC0;
    private const int PartyMaxHp = 0xFC;
    private const int PartySlots = 15;

    /// <summary> Phase-key surface: 0 = pre-entry (no board territory), 1 = in-board. </summary>
    private const int SurfacePreentry = 0;
    private const int SurfaceBoard = 1;

    // AgentXBMStageDetailList.
    private const int StageContentId = 0x38;
    private const int StageMode = 0x3C;
    private const int StageEntries = 0x50;
    private const int StageEntrySize = 0xA0;
    private const int StageEntryType = 0x00;
    private const int StageEntryStageEventId = 0x04;
    private const int StageBattleDetailId = 0x7C;
    private const int StageMaxEntries = 1000;
    private const int StageEntrySelection = 0x27150;
    private const int StageEntrySelectionLen = 100;

    // PR #1952 member-function signatures.
    private const string SigTogglePet = "40 56 57 48 83 EC ?? 83 79 ?? ?? 8B F2";
    private const string SigApplyPetSelection = "E8 ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ?? 33 FF 89 7B";

    private delegate void TogglePetDelegate(AgentInterface* agent, uint petId);
    private delegate void ApplyPetSelectionDelegate(AgentInterface* agent);

    private static TogglePetDelegate? _togglePet;
    private static ApplyPetSelectionDelegate? _applyPetSelection;
    private static AgentProbe.ReceiveEventDelegate? _receiveEvent;
    private static bool _sigsResolved;
    private static bool _sigsOk;

    /// <summary>
    ///     Why the pass must not write right now, set by the plugin each tick: <c>conflict_gluttony</c> (an older
    ///     GluttonyCombo that still writes familiars is loaded) or <c>autoduty_running</c>; null = free to write.
    /// </summary>
    internal static string? YieldReason;
    /// <summary> Last thing the pass did, for the window. </summary>
    internal static string LastSummary { get; private set; } = "";
    internal static DateTime LastSummaryAt { get; private set; }
    private static string _lastAnnounced = "";
    private static bool _loggedConflictThisPhase;

    private static bool _disarmRestOfScreen;
    /// <summary> A selection edit this pass did not send, seen by the probe; consumed on the next tick. </summary>
    private static string? _pendingExternalEdit;
    /// <summary> The player edited the Bentbranch roster this visit: the roster writer stands down until a board is entered. </summary>
    private static bool _rosterPlayerOwned;
    /// <summary> Roster membership right after this open's roster pass; a later difference is a manual edit. </summary>
    private static HashSet<int>? _rosterAfterPass;
    private static string _lastBasisNote = "";
    private static FormationArmState _arm;
    private static bool _loggedOffThisPhase;
    private static bool _loggedSigsThisPhase;
    private static int _lastBoard;
    private static string _lastPhaseSig = "";

    /// <summary>
    ///     Framework tick while Beastmaster (the plugin tears the pass down otherwise): one autograb pass per
    ///     formation phase. Territory is never a gate. The agent probe (<see cref="AgentProbe"/>) runs alongside.
    /// </summary>
    internal static void Tick()
    {
        if (Player.Object is null)
            return;

        var territoryBoard = BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType);

        if (territoryBoard != _lastBoard)
        {
            _lastBoard = territoryBoard;
            if (territoryBoard != 0)
                _rosterPlayerOwned = false; // a board was entered: the next Bentbranch visit is automatic again
        }

        try
        {
            RunFormationPass(territoryBoard);
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "formation pass");
        }
    }

    /// <summary>
    ///     Agent event seen by the probe. A selection edit the pass did not send (the player's click, a preset,
    ///     another tool) is queued for the stand-down latch on the next tick.
    /// </summary>
    internal static void OnAgentEvent(string tag, ulong kind, uint valueCount, int? firstInt)
    {
        if (OwnCalls.Depth == 0 && IsSelectionEditEvent(tag, kind, valueCount, firstInt))
            _pendingExternalEdit = $"ag={tag}|kind={kind}|v0={(firstInt?.ToString(CultureInfo.InvariantCulture) ?? "")}";
    }

    internal static void Dispose()
    {
        ResetRun();
        _sigsResolved = false;
        _sigsOk = false;
        _togglePet = null;
        _applyPetSelection = null;
        _receiveEvent = null;
    }

    internal static void ResetRun()
    {
        _arm = default;
        _loggedOffThisPhase = false;
        _loggedConflictThisPhase = false;
        _loggedSigsThisPhase = false;
        _lastBoard = 0;
        _lastPhaseSig = "";
        _disarmRestOfScreen = false;
        _lastBasisNote = "";
        _pendingExternalEdit = null;
        _rosterPlayerOwned = false;
        _rosterAfterPass = null;
    }

    private static void RunFormationPass(int territoryBoard)
    {
        var stage = GetAgent(AgentId.XBMStageDetailList);
        var pet = GetAgent(AgentId.XBMPetParty);
        var surfaceKey = territoryBoard != 0 ? SurfaceBoard : SurfacePreentry;
        var surfaceName = surfaceKey == SurfaceBoard ? "board" : "preentry";
        var xbmNames = ListVisibleXbmAddons();
        var agentNonZero = pet != 0;

        var mode = stage != 0 ? *(uint*)(stage + StageMode) : uint.MaxValue;
        var activePetOpen = IsAddonVisible("XBMActivePet");
        var petPartyOpen = IsAddonVisible("XBMPetParty");
        var petListOpen = pet != 0 && IsPetListAddonVisible(pet);
        var partyAddonShown = pet != 0 && IsAgentAddonShown(pet);
        // Arming gate: a Crucible familiar-selection addon is open (territory is never decisive).
        var screenOpen = activePetOpen || petPartyOpen;

        var partyCount = 0;
        List<int> partyRows = [];
        if (pet != 0 && TryReadPetVector(pet, PartySelectedPets, out partyRows))
            partyCount = partyRows.Count;

        // Observer line above every assign gate: fires on change while BST, any territory.
        if (partyCount == 0)
        {
            MaybeLogPhase(pet, territoryBoard, mode, activePetOpen, petPartyOpen, petListOpen, partyAddonShown,
                screenOpen, armed: false, partyCount: 0, surfaceName, xbmNames, agentNonZero);
            if (!screenOpen)
            {
                _arm = NextFormationArm(_arm, false, 0, surfaceKey);
                ConsumeExternalEdit(surfaceKey, petPartyOpen);
                _rosterAfterPass = null;
                _loggedOffThisPhase = false;
                _loggedConflictThisPhase = false;
                _loggedSigsThisPhase = false;
            }
            else
            {
                // Screen open but no roster → not armed (A2). Still advance latch so reopen re-arms. The
                // battle key is kept: feeding 0 here made the key flap -1 -> 0 -> -1 while the roster was
                // cleared and rebuilt by AutoDuty (its leveling team), which re-armed the writer (live 12:50,
                // four overwrites).
                var prevOpenEmpty = _arm.ScreenOpen;
                _arm = NextFormationArm(_arm, screenOpen, prevOpenEmpty ? _arm.BattleKey : 0, surfaceKey);
                if (_rosterAfterPass is { Count: > 0 } && OwnCalls.Depth == 0)
                    _pendingExternalEdit ??= "src=state|roster=0";
                ConsumeExternalEdit(surfaceKey, petPartyOpen);
                if (!prevOpenEmpty && _arm.ScreenOpen)
                {
                    _loggedOffThisPhase = false;
                    _loggedConflictThisPhase = false;
                    _loggedSigsThisPhase = false;
                }
            }
            return;
        }

        var contentBoard = pet != 0 ? *(uint*)(pet + PartyContentId) : 0u;
        var battleKey = 0;
        if (stage != 0 && TryIdentifyBattle(stage, territoryBoard != 0 ? territoryBoard : (int)contentBoard,
                out _, out var armBattle, out var armDetailId, out _))
            battleKey = (int)armDetailId != 0 ? (int)armDetailId : armBattle + 1;
        else if (contentBoard is >= 1 and <= 5)
            battleKey = -(int)contentBoard; // coverage phase key per board
        else if (territoryBoard != 0)
            battleKey = -territoryBoard;

        var prevOpen = _arm.ScreenOpen;
        _arm = NextFormationArm(_arm, screenOpen, battleKey, surfaceKey);
        if (!_arm.ScreenOpen)
            _rosterAfterPass = null;
        else if (_rosterAfterPass is not null && surfaceKey == SurfacePreentry && OwnCalls.Depth == 0)
        {
            // Notebook adds call TogglePet from the addon callback and never reach ReceiveEvent: watch membership.
            var nowRoster = ReadPetIds(pet, PartySelectedPetIds);
            if (!_rosterAfterPass.SetEquals(nowRoster))
                _pendingExternalEdit ??= $"src=state|roster={string.Join(".", nowRoster)}";
        }
        ConsumeExternalEdit(surfaceKey, petPartyOpen);
        if (!prevOpen && _arm.ScreenOpen)
        {
            _loggedOffThisPhase = false;
            _loggedConflictThisPhase = false;
            _loggedSigsThisPhase = false;
            _disarmRestOfScreen = false;
        }
        if (!_arm.ScreenOpen)
        {
            _loggedOffThisPhase = false;
            _loggedConflictThisPhase = false;
            _loggedSigsThisPhase = false;
            _disarmRestOfScreen = false;
        }

        var armed = IsFormationArmed(in _arm) && partyCount > 0 && !_disarmRestOfScreen;
        MaybeLogPhase(pet, territoryBoard, mode, activePetOpen, petPartyOpen, petListOpen, partyAddonShown,
            screenOpen, armed, partyCount, surfaceName, xbmNames, agentNonZero);

        if (!screenOpen)
            return;

        var now = UnixMs();
        var cfg = Plugin.Config;

        // Off-state line ABOVE every suppressing condition (battle id, sigs, assign route): both automations off.
        if (!cfg.AutoRoster && !cfg.AutoHorns)
        {
            LogOffOnce(now, territoryBoard, mode, surfaceName, activePetOpen, petPartyOpen, petListOpen, partyAddonShown, partyCount, "all");
            if (armed)
                _arm = MarkFormationPassDone(in _arm);
            return;
        }

        if (YieldReason is { } yieldReason)
        {
            if (!_loggedConflictThisPhase)
            {
                _loggedConflictThisPhase = true;
                LogPs($"PS|{now}|opt=1|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|surface={surfaceName}|calls=0|note={yieldReason}");
                SetSummary(yieldReason == "autoduty_running"
                    ? "Not writing: AutoDuty is running this board and picks its own familiars."
                    : "Not writing: an older GluttonyCombo that also fills familiars is loaded.");
            }
            return;
        }

        if (!armed)
            return;

        // What the open screen is for, from the XBMPetParty addon's own mode values ([2] Mode, [3] SubMode).
        // The shop opens the same addon/agent as the Beast Feed target picker (mode 3, sub 0) and reuses
        // SelectedPetIds as a one-familiar selection; the .227-.229 bt=5 readback=fail passes were horn
        // writes into that picker. The notebook's team screen (XBMActivePet alone) is not a run screen.
        var (addonMode, addonSub) = ReadPetPartyAddonMode();
        var selectedRaw = ReadPetIds(pet, PartySelectedPetIds);
        var gate = DecideFormationWrite(new FormationGateInput(
            petPartyOpen, activePetOpen, surfaceKey == SurfacePreentry, addonMode, addonSub, selectedRaw, partyRows.Count,
            RosterPlayerOwned: _rosterPlayerOwned));
        if (gate.Write == FormationWrite.None)
        {
            var skipNote = $"skip|{gate.Reason}|{addonMode}|{addonSub}";
            if (skipNote != _lastBasisNote)
            {
                _lastBasisNote = skipNote;
                LogPs($"PS|{now}|opt=1|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|surface={surfaceName}|ppm={addonMode}|pps={addonSub}|sl={string.Join(".", selectedRaw)}|flutes={selectedRaw.Count}|note=skip_screen|why={gate.Reason}|calls=0");
            }
            return;
        }

        // Per-surface switch: the roster list and the Battlehorn preview each have their own toggle.
        var scopeOff = gate.Write == FormationWrite.Roster ? !cfg.AutoRoster
            : gate.Write == FormationWrite.Horn ? !cfg.AutoHorns
            : false;
        if (scopeOff)
        {
            LogOffOnce(now, territoryBoard, mode, surfaceName, activePetOpen, petPartyOpen, petListOpen, partyAddonShown, partyCount,
                gate.Write == FormationWrite.Roster ? "roster" : "horn");
            _arm = MarkFormationPassDone(in _arm);
            return;
        }

        // Resolve sigs unconditionally so telemetry learns whether they resolve on this game version.
        var sigsOk = EnsureSigs();
        if (!_loggedSigsThisPhase)
        {
            _loggedSigsThisPhase = true;
            LogPs($"PS|{now}|opt=1|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|surface={surfaceName}|sigs={(sigsOk ? "ok" : "miss")}|calls=0");
        }

        var eventOk = EnsureReceiveEvent(pet);
        if (!sigsOk && !eventOk)
        {
            LogPs($"PS|{now}|opt=1|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|surface={surfaceName}|abort=no_assign_route|sigs=miss|event=0|calls=0");
            _arm = MarkFormationAborted(in _arm);
            return;
        }

        // Prefer the observed event route: live PSP proved kind=0 [Int 1, Int screenIndex] toggled SelectedPetIds.
        var route = eventOk ? "event" : "sig";

        if (gate.Write == FormationWrite.Wait)
        {
            var waitNote = $"{gate.Reason}|{selectedRaw.Count}|{string.Join(".", selectedRaw)}|{addonMode}";
            if (waitNote != _lastBasisNote)
            {
                _lastBasisNote = waitNote;
                var basisName = gate.Reason == "wait_horn_basis" ? "roster" : gate.Reason == "wait_roster_settle" ? "roster" : "pending";
                var rs = gate.Reason == "wait_roster_settle" ? 1 : 0;
                var note = gate.Reason == "settling" ? "wait_settling" : gate.Reason;
                LogPs($"PS|{now}|opt=1|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|surface={surfaceName}|basis={basisName}|sl={string.Join(".", selectedRaw)}|flutes={selectedRaw.Count}|rs={rs}|ppm={addonMode}|pps={addonSub}|note={note}|route={route}|calls=0");
            }
            return;
        }

        if (gate.Write == FormationWrite.Roster)
        {
            _lastBasisNote = "";
            ExecuteRosterWrite(pet, surfaceName, route, sigsOk, now, territoryBoard, contentBoard, selectedRaw);
            return;
        }
        _lastBasisNote = "";

        int board;
        List<uint> nameIds;
        List<CrucibleBeastPick> picks;
        var coverage = false;
        int battle;
        uint detailId;

        if (stage != 0 && TryIdentifyBattle(stage, territoryBoard != 0 ? territoryBoard : (int)contentBoard,
                out board, out battle, out detailId, out nameIds))
        {
            var hpPets = ReadPartyHpRaw(pet, partyRows);
            var hpMap = HpPercentByRow(hpPets);
            var candidates = new List<int>(partyRows.Count);
            foreach (var r in partyRows)
                if (r is >= 1 and <= BST_Beasts.Count)
                    candidates.Add(r);
            picks = PickSlots(board, battle, candidates, hpMap, 3);
        }
        else
        {
            board = territoryBoard != 0 ? territoryBoard
                : contentBoard is >= 1 and <= 5 ? (int)contentBoard
                : 0;
            if (board is < 1 or > 5)
            {
                LogPs($"PS|{now}|opt=1|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|surface={surfaceName}|content={contentBoard}|abort=battle_unidentified|route={route}|calls=0");
                _arm = MarkFormationPassDone(in _arm);
                return;
            }

            coverage = true;
            battle = -1;
            detailId = 0;
            nameIds = [];
            var hpPets = ReadPartyHpRaw(pet, partyRows);
            var hpMap = HpPercentByRow(hpPets);
            var candidates = new List<int>(partyRows.Count);
            foreach (var r in partyRows)
                if (r is >= 1 and <= BST_Beasts.Count)
                    candidates.Add(r);
            picks = PickSlotsCoverage(board, candidates, hpMap, 3);
        }

        var snapshot = new List<int>(selectedRaw);
        var currentHornRows = ResolveHornPetRows(selectedRaw, partyRows);
        var changes = PlanHornChanges(currentHornRows, picks);
        var (removeRows, addRows) = PlanHornMembershipDelta(currentHornRows, picks);

        var candStr = string.Join(",", ReadPartyHpRaw(pet, partyRows).ConvertAll(p =>
            $"{p.Row}:{(p.Max == 0 || p.Current == 0 ? 0 : (int)Math.Round(100.0 * p.Current / p.Max))}"));
        var pickStr = string.Join(",", picks.ConvertAll(p =>
        {
            var idx = FindPartyIndex(partyRows, p.Row);
            return $"{p.Row}:idx{(idx < 0 ? "miss" : idx.ToString(CultureInfo.InvariantCulture))}:{Clean(p.Why, 80)}";
        }));
        var nameStr = string.Join(",", nameIds);
        LogPs($"PS|{now}|opt=1|b={board}|terr={Svc.ClientState.TerritoryType}|surface={surfaceName}|basis=horn|bt={battle}|detail={detailId}|coverage={(coverage ? 1 : 0)}|names={nameStr}|cand={candStr}|picks={pickStr}|need={changes.Count}|route={route}|sigs={(sigsOk ? "ok" : "miss")}");

        // Mid-run unidentified: never replace a non-empty horn with coverage. Focus settles ~40 ms
        // later and re-arms an opponent-fitted pass; a coverage replace that aborts emptied the horn (.225).
        if (coverage && currentHornRows.Exists(r => r is >= 1 and <= BST_Beasts.Count))
        {
            LogPs($"PS|{now}|opt=1|b={board}|bt={battle}|surface={surfaceName}|route={route}|calls=0|readback=ok|note=leave_standing_horn|apply=not_needed|sl={string.Join(".", snapshot)}");
            _arm = MarkFormationPassDone(in _arm);
            return;
        }

        if (removeRows.Count == 0 && addRows.Count == 0
            && ListsMatchPrefix(currentHornRows, picks.ConvertAll(p => p.Row)))
        {
            LogPs($"PS|{now}|opt=1|b={board}|bt={battle}|surface={surfaceName}|route={route}|calls=0|readback=ok|note=already_correct|apply=not_needed");
            _arm = MarkFormationPassDone(in _arm);
            AnnounceHorns(board, battle, coverage, picks);
            return;
        }

        var agent = (AgentInterface*)pet;
        var calls = new List<string>(8);

        foreach (var fromRow in removeRows)
        {
            if (!ToggleOne(agent, partyRows, fromRow, route, calls))
            {
                AbortWithRestore(pet, snapshot, now, board, battle, route, calls, "toggle_off_no_route");
                return;
            }
            if (!ReadbackHornOk(pet, partyRows, expectRemove: fromRow, expectAdd: 0, afterToggleOff: true))
            {
                AbortWithRestore(pet, snapshot, now, board, battle, route, calls, "toggle_off_mismatch");
                return;
            }
        }

        foreach (var want in addRows)
        {
            if (!ToggleOne(agent, partyRows, want, route, calls))
            {
                AbortWithRestore(pet, snapshot, now, board, battle, route, calls, "toggle_on_no_route");
                return;
            }
            if (!ReadbackHornOk(pet, partyRows, expectRemove: 0, expectAdd: want, afterToggleOff: false))
            {
                AbortWithRestore(pet, snapshot, now, board, battle, route, calls, "toggle_on_mismatch");
                return;
            }
        }

        var afterToggles = ResolveHornPetRows(ReadPetIds(pet, PartySelectedPetIds), partyRows);
        var desired = new List<int>(3);
        for (var i = 0; i < 3 && i < picks.Count; i++)
            desired.Add(picks[i].Row);

        var applyNeeded = !ListsMatchPrefix(afterToggles, desired);
        if (applyNeeded)
        {
            if (!sigsOk || _applyPetSelection is null)
            {
                AbortWithRestore(pet, snapshot, now, board, battle, route, calls, "apply_unavailable");
                return;
            }
            calls.Add("ApplyPetSelection");
            _applyPetSelection(agent);
        }

        var finalRows = ResolveHornPetRows(ReadPetIds(pet, PartySelectedPetIds), partyRows);
        if (!ListsMatchPrefix(finalRows, desired))
        {
            AbortWithRestore(pet, snapshot, now, board, battle, route, calls, "apply_mismatch");
            return;
        }

        var finalIdx = ReadPetIds(pet, PartySelectedPetIds);
        LogPs($"PS|{now}|opt=1|b={board}|bt={battle}|surface={surfaceName}|route={route}|calls={string.Join(",", calls)}|readback=ok|apply={(applyNeeded ? "needed" : "not_needed")}|sl={string.Join(".", finalIdx)}");
        _arm = MarkFormationPassDone(in _arm);
        AnnounceHorns(board, battle, coverage, picks);
    }

    /// <summary> The once-per-phase <c>note=off</c> line; <paramref name="scope"/> says which switch is off. </summary>
    private static void LogOffOnce(long now, int territoryBoard, uint mode, string surfaceName, bool activePetOpen, bool petPartyOpen,
        bool petListOpen, bool partyAddonShown, int partyCount, string scope)
    {
        if (_loggedOffThisPhase)
            return;
        _loggedOffThisPhase = true;
        LogPs($"PS|{now}|opt=0|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|mode={mode}|surface={surfaceName}|activePet={(activePetOpen ? 1 : 0)}|petParty={(petPartyOpen ? 1 : 0)}|petList={(petListOpen ? 1 : 0)}|partyAddon={(partyAddonShown ? 1 : 0)}|party={partyCount}|calls=0|note=off|scope={scope}");
    }

    private static void SetSummary(string text)
    {
        LastSummary = text;
        LastSummaryAt = DateTime.Now;
    }

    private static string BoardName(int board) =>
        board is >= 1 and <= 5 ? BST_CrucibleData.Boards[board - 1].Name : $"board {board}";

    /// <summary> Horn picks are announced once the fight is identified; a coverage fill only updates the window. </summary>
    private static void AnnounceHorns(int board, int battle, bool coverage, IReadOnlyList<CrucibleBeastPick> picks)
    {
        if (coverage)
        {
            SetSummary($"Battlehorns (fight not identified yet, best for {BoardName(board)}): {string.Join(", ", picks.Select(p => BeastName(p.Row)))}");
            return;
        }
        Announce($"h|{board}|{battle}", $"Battlehorns for {BST_CrucibleData.BattleLabel(board, battle)}", picks);
    }

    private static string BeastName(int row)
    {
        if (row is < 1 or > BST_Beasts.Count)
            return "?";
        var name = BST_Beasts.All[row].Name;
        return name.Length == 0 ? "?" : char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>
    ///     Chat line and window summary for picks that are now on the horns (or the roster). Once per battle key;
    ///     never for the coverage fallback (it is replaced ~40 ms later when the fight is identified).
    /// </summary>
    private static void Announce(string key, string what, IReadOnlyList<CrucibleBeastPick> picks)
    {
        var names = string.Join(", ", picks.Select(p => string.IsNullOrEmpty(p.Why) ? BeastName(p.Row) : $"{BeastName(p.Row)} ({p.Why})"));
        SetSummary($"{what}: {names}");
        if (key == _lastAnnounced)
            return;
        _lastAnnounced = key;
        if (Plugin.Config.AnnouncePicks)
            Svc.Chat.Print($"[LazyCrucible] {what}: {names}");
    }

    /// <summary>
    ///     Turn a selection edit the pass did not send into the stand-down latch: the rest of this screen
    ///     open is the player's, and on the Bentbranch roster the rest of the visit is.
    /// </summary>
    private static void ConsumeExternalEdit(int surfaceKey, bool petPartyOpen)
    {
        var edit = _pendingExternalEdit;
        if (edit is null)
            return;
        _pendingExternalEdit = null;
        if (!_arm.ScreenOpen)
            return;
        var already = _arm.PlayerEdited;
        _arm = MarkPlayerEdited(in _arm);
        if (surfaceKey == SurfacePreentry && petPartyOpen)
            _rosterPlayerOwned = true; // the run roster itself was edited, not the notebook's overworld team
        if (!already)
            LogPs($"PS|{UnixMs()}|opt={(Plugin.Config.AutoRoster || Plugin.Config.AutoHorns ? 1 : 0)}|terr={Svc.ClientState.TerritoryType}|surface={(surfaceKey == SurfaceBoard ? "board" : "preentry")}|{edit}|note=player_edit|calls=0");
    }

    private static bool ToggleOne(AgentInterface* agent, IReadOnlyList<int> partyRows, int petRow, string route, List<string> calls)
    {
        if (route == "event" && _receiveEvent is not null)
        {
            var idx = FindPartyIndex(partyRows, petRow);
            if (idx < 0)
            {
                calls.Add($"EventToggle-miss-pet{petRow}");
                return false;
            }
            calls.Add($"EventToggle-idx{idx}-pet{petRow}");
            return FireEventToggle(agent, idx);
        }
        if (_togglePet is not null)
        {
            // Sig route takes a familiar id (PR #1952), not a screen index.
            calls.Add($"TogglePet-{petRow}");
            _togglePet(agent, (uint)petRow);
            return true;
        }
        return false;
    }

    /// <summary>
    ///     Replay the live-proven pet-party toggle: ReceiveEvent(eventKind 0, [Int 1, Int screenIndex]),
    ///     where screenIndex is the index into SelectedPets on the open horn screen — never a familiar id.
    /// </summary>
    internal static bool FireEventToggle(AgentInterface* agent, int screenIndex)
    {
        if (_receiveEvent is null)
            return false;
        var values = stackalloc AtkValue[2];
        var ret = stackalloc AtkValue[1];
        values[0].Type = AtkValueType.Int;
        values[0].Int = 1;
        values[1].Type = AtkValueType.Int;
        values[1].Int = screenIndex;
        ret[0] = default;
        OwnCalls.Depth++;
        try
        {
            _receiveEvent(agent, ret, values, 2, 0);
        }
        finally
        {
            OwnCalls.Depth--;
        }
        return true;
    }

    private static void ExecuteRosterWrite(
        nint pet, string surfaceName, string route, bool sigsOk, long now, int territoryBoard, uint contentBoard, List<int> selectedRaw)
    {
        var board = territoryBoard != 0 ? territoryBoard
            : contentBoard is >= 1 and <= 5 ? (int)contentBoard
            : 1;

        // Try reading unlocked pets bitfield from AgentXBMMonsterNotebook (AgentId 500) at offset 0x70.
        var notebook = GetAgent(AgentId.XBMMonsterNotebook);
        var candidates = new List<int>(BST_Beasts.Count);
        if (notebook != 0)
        {
            try
            {
                for (var row = 1; row <= BST_Beasts.Count; row++)
                {
                    var idx = row - 1;
                    var b = *(byte*)(notebook + 0x70 + (idx >> 3));
                    if ((b & (1 << (idx & 7))) != 0)
                        candidates.Add(row);
                }
            }
            catch
            {
                candidates.Clear();
            }
        }
        if (candidates.Count == 0)
        {
            for (var row = 1; row <= BST_Beasts.Count; row++)
                candidates.Add(row);
        }

        var candidatePets = ReadPetIds(pet, PartyCandidatePets);
        var hpPets = ReadPartyHpRaw(pet, candidatePets.Count > 0 ? candidatePets : selectedRaw);
        var hpMap = HpPercentByRow(hpPets);

        var picks = BST_CrucibleAdvisor.PickSlotsCoverage(board, candidates, hpMap, 10);
        var desiredRows = picks.ConvertAll(p => p.Row);

        var snapshot = new List<int>(selectedRaw);
        var changes = PlanRosterChanges(selectedRaw, picks);

        var candStr = string.Join(",", candidates.ConvertAll(r => $"{r}:{(hpMap.TryGetValue(r, out var h) ? h : 100)}"));
        var pickStr = string.Join(",", picks.ConvertAll(p => $"{p.Row}:{Clean(p.Why, 80)}"));
        LogPs($"PS|{now}|opt=1|b={board}|terr={Svc.ClientState.TerritoryType}|surface={surfaceName}|basis=roster|bt=-1|detail=0|coverage=1|names=|cand={candStr}|picks={pickStr}|need={changes.Count}|route={route}|sigs={(sigsOk ? "ok" : "miss")}");

        if (changes.Count == 0)
        {
            LogPs($"PS|{now}|opt=1|b={board}|bt=-1|surface={surfaceName}|basis=roster|route={route}|calls=0|readback=ok|note=already_correct|apply=not_needed|sl={string.Join(".", snapshot)}");
            _arm = MarkFormationPassDone(in _arm);
            SetSummary($"Run roster for {BoardName(board)} already set: {string.Join(", ", desiredRows.ConvertAll(BeastName))}");
            _rosterAfterPass = new HashSet<int>(snapshot);
            return;
        }

        var agent = (AgentInterface*)pet;
        var calls = new List<string>(changes.Count + 2);

        // Removals first
        foreach (var c in changes)
        {
            if (c.FromRow is >= 1 and <= BST_Beasts.Count)
            {
                calls.Add($"TogglePet-rem{c.FromRow}");
                if (_togglePet is not null)
                {
                    _togglePet(agent, (uint)c.FromRow);
                }
                else
                {
                    AbortWithRestore(pet, snapshot, now, board, -1, route, calls, "toggle_unavailable", isRosterSurface: true);
                    return;
                }
            }
        }

        // Additions second
        foreach (var c in changes)
        {
            if (c.ToRow is >= 1 and <= BST_Beasts.Count)
            {
                calls.Add($"TogglePet-add{c.ToRow}");
                if (_togglePet is not null)
                {
                    _togglePet(agent, (uint)c.ToRow);
                }
                else
                {
                    AbortWithRestore(pet, snapshot, now, board, -1, route, calls, "toggle_unavailable", isRosterSurface: true);
                    return;
                }
            }
        }

        if (_applyPetSelection is not null)
        {
            calls.Add("ApplyPetSelection");
            _applyPetSelection(agent);
        }

        if (notebook != 0)
        {
            try
            {
                *(ushort*)(notebook + 0x5E) = 0x0101;
            }
            catch { }
        }

        var finalRows = ReadPetIds(pet, PartySelectedPetIds);
        var desiredSet = new HashSet<int>(desiredRows);
        var finalSet = new HashSet<int>(finalRows);

        var match = desiredSet.SetEquals(finalSet);
        if (!match)
        {
            AbortWithRestore(pet, snapshot, now, board, -1, route, calls, "roster_mismatch", isRosterSurface: true);
            return;
        }

        var gained = finalRows.FindAll(r => !snapshot.Contains(r));
        var dropped = snapshot.FindAll(r => !finalRows.Contains(r));
        LogPs($"PS|{now}|opt=1|b={board}|bt=-1|surface={surfaceName}|basis=roster|route={route}|calls={string.Join(",", calls)}|readback=ok|apply=needed|pre={string.Join(".", snapshot)}|post={string.Join(".", finalRows)}|gain={string.Join(".", gained)}|dropped={string.Join(".", dropped)}|sl={string.Join(".", finalRows)}");
        _arm = MarkFormationPassDone(in _arm);
        _rosterAfterPass = new HashSet<int>(finalRows);
        Announce($"r|{board}|{string.Join(".", desiredRows)}", $"Run roster for {BoardName(board)}",
            picks.ConvertAll(p => p with { Why = "" }));
    }

    private static void AbortWithRestore(
        nint pet, List<int> snapshot, long now, int board, int battle, string route, List<string> calls, string reason, bool isRosterSurface = false)
    {
        var after = ReadPetIds(pet, PartySelectedPetIds);
        var damaged = !SelectionEquals(snapshot, after);
        var restored = false;
        if (damaged)
            restored = TryRestoreSelection(pet, snapshot, isRosterSurface);

        var afterRestore = ReadPetIds(pet, PartySelectedPetIds);
        var match = SelectionEquals(snapshot, afterRestore);
        if (!match)
            _disarmRestOfScreen = true;

        LogPs($"PS|{now}|opt=1|b={board}|bt={battle}|route={route}|calls={string.Join(",", calls)}|readback=fail|abort={reason}"
              + $"|pre={string.Join(".", snapshot)}|post={string.Join(".", after)}"
              + $"|damaged={(damaged ? 1 : 0)}|restored={(restored && match ? 1 : 0)}"
              + $"|disarm={(_disarmRestOfScreen ? 1 : 0)}");
        _arm = MarkFormationAborted(in _arm);
    }

    /// <summary>
    ///     Best-effort restore of SelectedPetIds after a failed pass: toggle indices or familiar IDs present only on one side.
    ///     Returns true when a restore attempt ran; caller still verifies SelectionEquals.
    /// </summary>
    private static bool TryRestoreSelection(nint pet, List<int> snapshot, bool isRosterSurface)
    {
        try
        {
            var agent = (AgentInterface*)pet;
            var current = ReadPetIds(pet, PartySelectedPetIds);

            if (isRosterSurface)
            {
                if (_togglePet is null)
                    return false;
                foreach (var petId in current)
                {
                    if (!snapshot.Contains(petId) && petId is >= 1 and <= BST_Beasts.Count)
                        _togglePet(agent, (uint)petId);
                }
                current = ReadPetIds(pet, PartySelectedPetIds);
                foreach (var petId in snapshot)
                {
                    if (!current.Contains(petId) && petId is >= 1 and <= BST_Beasts.Count)
                        _togglePet(agent, (uint)petId);
                }
                if (_applyPetSelection is not null)
                    _applyPetSelection(agent);
                return true;
            }

            if (_receiveEvent is null && _togglePet is null)
                return false;
            // Horn surface
            // Toggle off extras, then toggle on missing — order matches manual horn edits (toggle membership).
            foreach (var idx in current)
            {
                if (!snapshot.Contains(idx))
                    FireEventToggle(agent, idx);
            }
            current = ReadPetIds(pet, PartySelectedPetIds);
            foreach (var idx in snapshot)
            {
                if (!current.Contains(idx))
                    FireEventToggle(agent, idx);
            }
            return true;
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "restore failed");
            return false;
        }
    }

    private static bool ReadbackHornOk(
        nint pet, IReadOnlyList<int> partyRows, int expectRemove, int expectAdd, bool afterToggleOff)
    {
        var horns = ResolveHornPetRows(ReadPetIds(pet, PartySelectedPetIds), partyRows);
        if (afterToggleOff)
        {
            if (expectRemove is < 1 or > BST_Beasts.Count)
                return true;
            for (var i = 0; i < horns.Count; i++)
                if (horns[i] == expectRemove)
                    return false;
            return true;
        }
        if (expectAdd is >= 1 and <= BST_Beasts.Count)
        {
            for (var i = 0; i < horns.Count; i++)
                if (horns[i] == expectAdd)
                    return true;
            return false;
        }
        return true;
    }

    private static bool ListsMatchPrefix(IReadOnlyList<int> actual, IReadOnlyList<int> desired)
    {
        if (desired.Count == 0)
            return true;
        if (actual.Count < desired.Count)
            return false;
        for (var i = 0; i < desired.Count; i++)
            if (actual[i] != desired[i])
                return false;
        return true;
    }

    internal static bool TryIdentifyBattle(nint stage, int territoryBoard, out int board, out int battle, out uint detailId, out List<uint> nameIds)
    {
        board = 0;
        battle = -1;
        detailId = 0;
        nameIds = [];

        var contentId = *(uint*)(stage + StageContentId);
        if (contentId is >= 1 and <= 5 && (int)contentId != territoryBoard)
            return false;

        // Prefer the focused stage entry via _entrySelection[StageEventId]; fall back to unique board match.
        uint focusedDetail = 0;
        int focusedBoard = 0;
        int focusedBattle = -1;
        var boardMatches = new List<(uint Id, int Board, int Battle)>(8);

        for (var i = 0; i < StageMaxEntries; i++)
        {
            var entry = stage + StageEntries + i * StageEntrySize;
            var type = *(uint*)(entry + StageEntryType);
            if (type != 3)
                continue;

            var id = *(uint*)(entry + StageBattleDetailId);
            if (id == 0)
                continue;
            if (!BST_CrucibleData.TryBattleOfDetail(id, out var b, out var bt))
                continue;
            if (territoryBoard != 0 && b != territoryBoard)
                continue;
            if (territoryBoard == 0 && contentId is >= 1 and <= 5 && b != (int)contentId)
                continue;

            boardMatches.Add((id, b, bt));

            var stageEventId = *(uint*)(entry + StageEntryStageEventId);
            if (stageEventId < StageEntrySelectionLen)
            {
                var selected = *(byte*)(stage + StageEntrySelection + (int)stageEventId);
                if (selected != 0 && focusedDetail == 0)
                {
                    focusedDetail = id;
                    focusedBoard = b;
                    focusedBattle = bt;
                }
            }
        }

        if (focusedDetail != 0)
        {
            detailId = focusedDetail;
            board = focusedBoard;
            battle = focusedBattle;
        }
        else if (boardMatches.Count == 1)
        {
            detailId = boardMatches[0].Id;
            board = boardMatches[0].Board;
            battle = boardMatches[0].Battle;
        }
        else
            return false;

        foreach (var e in BST_CrucibleData.Enemies)
            if (e.Board == board && e.Battle == battle)
                nameIds.Add(e.NameId);
        return nameIds.Count > 0;
    }

    /// <summary>
    ///     The run roster (AgentXBMPetParty SelectedPets) and each familiar's HP percent (0 = knocked out), for the
    ///     selection policies. False when the agent holds no party.
    /// </summary>
    internal static bool TryReadRunRoster(out List<int> rows, out Dictionary<int, int> hpPercent)
    {
        hpPercent = [];
        var pet = GetAgent(AgentId.XBMPetParty);
        if (pet == 0 || !TryReadPetVector(pet, PartySelectedPets, out rows) || rows.Count == 0)
        {
            rows = [];
            return false;
        }
        hpPercent = HpPercentByRow(ReadPartyHpRaw(pet, rows));
        return true;
    }

    private static List<(int Row, uint Current, uint Max)> ReadPartyHpRaw(nint agent, List<int> partyRows)
    {
        var list = new List<(int, uint, uint)>(partyRows.Count);
        for (var i = 0; i < partyRows.Count && i < PartySlots; i++)
        {
            var cur = *(uint*)(agent + PartyCurrentHp + i * 4);
            var max = *(uint*)(agent + PartyMaxHp + i * 4);
            list.Add((partyRows[i], cur, max));
        }
        return list;
    }

    private static List<int> ReadPetIds(nint agent, int vectorOffset)
    {
        TryReadPetVector(agent, vectorOffset, out var rows);
        return rows;
    }

    private static bool TryReadPetVector(nint agent, int offset, out List<int> rows)
    {
        rows = [];
        try
        {
            var first = *(nint*)(agent + offset);
            var last = *(nint*)(agent + offset + 8);
            if (first == 0 || last < first)
                return false;
            var count = (int)((last - first) / 8);
            if (count is <= 0 or > PartySlots)
                return false;
            for (var i = 0; i < count; i++)
                rows.Add((int)*(uint*)(first + i * 8));
            return true;
        }
        catch
        {
            rows = [];
            return false;
        }
    }

    internal static nint GetAgent(AgentId id)
    {
        var module = AgentModule.Instance();
        if (module is null)
            return 0;
        return (nint)module->GetAgentByInternalId(id);
    }

    private static bool IsAddonVisible(string name)
    {
        try
        {
            return TryGetAddonByName<AtkUnitBase>(name, out var addon)
                   && addon is not null
                   && addon->IsVisible;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPetListAddonVisible(nint pet)
    {
        try
        {
            var id = *(uint*)(pet + PartyPetListAddonId);
            if (id == 0)
                return false;
            var mgr = RaptureAtkUnitManager.Instance();
            if (mgr is null)
                return false;
            var addon = mgr->GetAddonById((ushort)id);
            return addon is not null && addon->IsVisible;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAgentAddonShown(nint pet)
    {
        try
        {
            var agent = (AgentInterface*)pet;
            return agent->IsAddonShown();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     XBMPetParty AtkValues [2] (Mode) and [3] (SubMode); -1 when the addon is not visible or the value
    ///     is not populated yet (only [3] is set on the first frame of an open). Live: roster list 0/1,
    ///     Battlehorn preview 2/1, shop feed picker 3/0.
    /// </summary>
    internal static (int Mode, int SubMode) ReadPetPartyAddonMode()
    {
        try
        {
            if (!TryGetAddonByName<AtkUnitBase>("XBMPetParty", out var addon) || addon is null || !addon->IsVisible)
                return (-1, -1);
            var values = addon->AtkValues;
            if (values is null || addon->AtkValuesCount < 4)
                return (-1, -1);
            return (AtkNumber(values[2]), AtkNumber(values[3]));
        }
        catch
        {
            return (-1, -1);
        }
    }

    private static int AtkNumber(AtkValue v) => v.Type switch
    {
        AtkValueType.UInt => (int)v.UInt,
        AtkValueType.Int => v.Int,
        _ => -1,
    };

    private static void MaybeLogPhase(
        nint pet, int territoryBoard, uint mode,
        bool activePetOpen, bool petPartyOpen, bool petListOpen, bool partyAddonShown,
        bool screenOpen, bool armed, int partyCount, string surfaceName, string xbmNames, bool agentNonZero)
    {
        try
        {
            var flutes = partyCount > 0 && pet != 0 ? ReadPetIds(pet, PartySelectedPetIds) : [];
            var optOn = Plugin.Config.AutoRoster || Plugin.Config.AutoHorns;
            var (ppm, pps) = petPartyOpen ? ReadPetPartyAddonMode() : (-1, -1);
            var agm = pet != 0 ? (int)*(uint*)(pet + PartyMode) : -1;
            var ags = pet != 0 ? (int)*(uint*)(pet + PartySubMode) : -1;
            var sig =
                $"m={mode}|ap={(activePetOpen ? 1 : 0)}|pp={(petPartyOpen ? 1 : 0)}|pl={(petListOpen ? 1 : 0)}|pa={(partyAddonShown ? 1 : 0)}"
                + $"|ppm={ppm}|pps={pps}|agm={agm}|ags={ags}"
                + $"|so={(screenOpen ? 1 : 0)}|arm={(armed ? 1 : 0)}|opt={(optOn ? 1 : 0)}|p={partyCount}|f={flutes.Count}"
                + $"|bk={_arm.BattleKey}|sf={_arm.SurfaceKey}|xb={xbmNames}|ag={(agentNonZero ? 1 : 0)}";
            if (sig == _lastPhaseSig)
                return;
            _lastPhaseSig = sig;
            LogPs($"PS|{UnixMs()}|gate=phase|mode={mode}|surface={surfaceName}|activePet={(activePetOpen ? 1 : 0)}|petParty={(petPartyOpen ? 1 : 0)}"
                + $"|petList={(petListOpen ? 1 : 0)}|partyAddon={(partyAddonShown ? 1 : 0)}|screen={(screenOpen ? 1 : 0)}|armed={(armed ? 1 : 0)}"
                + $"|opt={(optOn ? 1 : 0)}|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}"
                + $"|agent={(agentNonZero ? 1 : 0)}|xbm={xbmNames}"
                + $"|party={partyCount}|flutes={flutes.Count}|sl={string.Join(".", flutes)}|bk={_arm.BattleKey}|sk={_arm.SurfaceKey}"
                + $"|ppm={ppm}|pps={pps}|agm={agm}|ags={ags}|calls=0");
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "phase log failed");
        }
    }

    /// <summary> Visible addon names starting with XBM, comma-joined (empty if none). </summary>
    private static string ListVisibleXbmAddons()
    {
        try
        {
            var manager = RaptureAtkUnitManager.Instance();
            if (manager is null)
                return "";
            var list = manager->AtkUnitManager.AllLoadedUnitsList;
            var count = Math.Min((int)list.Count, list.Entries.Length);
            var names = new List<string>(8);
            for (var i = 0; i < count; i++)
            {
                var unit = list.Entries[i].Value;
                if (unit is null || !unit->IsVisible)
                    continue;
                var name = unit->NameString;
                if (name is null || !name.StartsWith("XBM", StringComparison.Ordinal))
                    continue;
                if (!names.Contains(name))
                    names.Add(name);
            }
            return string.Join(",", names);
        }
        catch
        {
            return "";
        }
    }

    private static bool EnsureSigs()
    {
        if (_sigsResolved)
            return _sigsOk;
        _sigsResolved = true;
        try
        {
            if (!Svc.SigScanner.TryScanText(SigTogglePet, out var toggle)
                || !Svc.SigScanner.TryScanText(SigApplyPetSelection, out var apply))
            {
                _sigsOk = false;
                return false;
            }
            _togglePet = Marshal.GetDelegateForFunctionPointer<TogglePetDelegate>(toggle);
            _applyPetSelection = Marshal.GetDelegateForFunctionPointer<ApplyPetSelectionDelegate>(ResolveCallTarget(apply));
            _sigsOk = _togglePet is not null && _applyPetSelection is not null;
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "signature resolve failed");
            _sigsOk = false;
        }
        return _sigsOk;
    }

    internal static bool EnsureReceiveEvent(nint pet)
    {
        if (_receiveEvent is not null)
            return true;
        try
        {
            var agent = (AgentInterface*)pet;
            var vt = agent->VirtualTable;
            if (vt is null)
                return false;
            var receiveEvent = (nint)vt->ReceiveEvent;
            if (receiveEvent == 0)
                return false;
            _receiveEvent = Marshal.GetDelegateForFunctionPointer<AgentProbe.ReceiveEventDelegate>(receiveEvent);
            return _receiveEvent is not null;
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "ReceiveEvent resolve failed");
            return false;
        }
    }

    /// <summary> Resolve an E8 rel32 call instruction to its absolute target. </summary>
    private static nint ResolveCallTarget(nint callSite)
    {
        var opcode = *(byte*)callSite;
        if (opcode != 0xE8)
            return callSite;
        var rel = *(int*)(callSite + 1);
        return callSite + 5 + rel;
    }

    private static long UnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static void LogPs(string line) => CrucibleLog.Line(line);

    private static string Clean(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var sb = new StringBuilder(Math.Min(text.Length, max));
        foreach (var c in text)
        {
            if (sb.Length >= max)
                break;
            sb.Append(c is '|' or ';' or '\r' or '\n' ? '_' : c);
        }
        return sb.ToString();
    }
}
