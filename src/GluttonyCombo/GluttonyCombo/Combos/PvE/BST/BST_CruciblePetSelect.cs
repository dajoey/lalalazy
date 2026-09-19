#region Dependencies

using Dalamud.Hooking;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

#endregion

namespace GluttonyCombo.Combos.PvE;

/// <summary>
///     Crucible pet-selection autograb (live). Reads AgentXBMPetParty / AgentXBMStageDetailList, ranks via
///     <see cref="BST_CrucibleAdvisor.PickSlots"/>, and drives horn assignment through the unmerged PR #1952
///     member functions (TogglePet / ApplyPetSelection) with a SelectedPetIds read-back abort. Default-off.
///     Residual assumption (G2): those member functions are the assign route; if signatures miss or read-back
///     fails, the pass degrades to doing nothing — never a retry loop, never the Int-8 start-battle confirm.
/// </summary>
internal static unsafe class BST_CruciblePetSelect
{
    // AgentXBMPetParty offsets (PR #1952 / DailyRoutines production).
    private const int PartySelectedPets = 0x78;
    private const int PartySelectedPetIds = 0x90;
    private const int PartyCandidatePets = 0xA8;
    private const int PartyCurrentHp = 0xC0;
    private const int PartyMaxHp = 0xFC;
    private const int PartySlots = 15;

    // AgentXBMStageDetailList.
    private const int StageContentId = 0x38;
    private const int StageMode = 0x3C;
    private const int StageEntries = 0x50;
    private const int StageEntrySize = 0xA0;
    private const int StageEntryType = 0x00;
    private const int StageBattleDetailId = 0x7C;
    private const int StageMaxEntries = 1000;

    // PR #1952 member-function signatures.
    private const string SigTogglePet = "40 56 57 48 83 EC ?? 83 79 ?? ?? 8B F2";
    private const string SigApplyPetSelection = "E8 ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ?? 33 FF 89 7B";

    private delegate void TogglePetDelegate(AgentInterface* agent, uint petId);
    private delegate void ApplyPetSelectionDelegate(AgentInterface* agent);
    private delegate AtkValue* ReceiveEventDelegate(
        AgentInterface* agent, AtkValue* returnValues, AtkValue* values, uint valueCount, ulong eventKind);

    private static TogglePetDelegate? _togglePet;
    private static ApplyPetSelectionDelegate? _applyPetSelection;
    private static bool _sigsResolved;
    private static bool _sigsOk;

    private static Hook<ReceiveEventDelegate>? _receiveEventHook;
    private static bool _probeWanted;

    private static bool _inFormation;
    private static bool _passDoneThisPhase;
    private static bool _abortThisPhase;
    private static bool _loggedAggroThisRun;
    private static int _lastBoard;

    /// <summary> Framework tick: probe lifecycle + one autograb pass per formation phase. </summary>
    internal static void Tick()
    {
        if (Player.Job is not Job.BST || Player.Object is null)
        {
            TeardownProbe();
            ResetRun();
            return;
        }

        var board = BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType);
        if (board == 0)
        {
            TeardownProbe();
            ResetRun();
            return;
        }

        if (board != _lastBoard)
        {
            _lastBoard = board;
            _loggedAggroThisRun = false;
        }

        EnsureProbe();
        if (!_loggedAggroThisRun)
        {
            _loggedAggroThisRun = true;
            var aggro = (CrucibleAggroMode)(int)BST.Config.BST_CrucibleAggro;
            LogPs($"PS|{UnixMs()}|aggro={(int)aggro}|aggroName={aggro}|b={board}|note=effective_default_On_stored_overrides");
        }

        try
        {
            RunFormationPass(board);
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[BST_CruciblePetSelect] formation pass failed");
        }
    }

    internal static void Dispose()
    {
        TeardownProbe();
        ResetRun();
        _sigsResolved = false;
        _sigsOk = false;
        _togglePet = null;
        _applyPetSelection = null;
    }

    private static void ResetRun()
    {
        _inFormation = false;
        _passDoneThisPhase = false;
        _abortThisPhase = false;
        _loggedAggroThisRun = false;
        _lastBoard = 0;
    }

    private static void RunFormationPass(int territoryBoard)
    {
        var stage = GetAgent(AgentId.XBMStageDetailList);
        var pet = GetAgent(AgentId.XBMPetParty);
        if (stage == 0 || pet == 0)
            return;

        var mode = *(uint*)(stage + StageMode);
        if (mode != 0)
        {
            _inFormation = false;
            _passDoneThisPhase = false;
            _abortThisPhase = false;
            return;
        }

        if (!_inFormation)
        {
            _inFormation = true;
            _passDoneThisPhase = false;
            _abortThisPhase = false;
        }

        if (_passDoneThisPhase || _abortThisPhase)
            return;

        // Pet-party agent must carry data (SelectedPets non-empty).
        if (!TryReadPetVector(pet, PartySelectedPets, out var partyRows) || partyRows.Count == 0)
            return;

        var now = UnixMs();
        var optOn = (bool)BST.Config.BST_CrucibleAutoGrab;

        if (!optOn)
        {
            LogPs($"PS|{now}|opt=0|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|calls=0|note=off");
            _passDoneThisPhase = true;
            return;
        }

        if (!TryIdentifyBattle(stage, territoryBoard, out var board, out var battle, out var detailId, out var nameIds))
        {
            LogPs($"PS|{now}|opt=1|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|abort=battle_unidentified|calls=0");
            _passDoneThisPhase = true;
            return;
        }

        var hpPets = ReadPartyHpRaw(pet, partyRows);
        var hpMap = BST_CrucibleAdvisor.HpPercentByRow(hpPets);
        var candidates = new List<int>(partyRows.Count);
        foreach (var r in partyRows)
            if (r is >= 1 and <= BST_Beasts.Count)
                candidates.Add(r);

        var picks = BST_CrucibleAdvisor.PickSlots(board, battle, candidates, hpMap, 3);
        var currentHorns = ReadPetIds(pet, PartySelectedPetIds);
        var changes = BST_CrucibleAdvisor.PlanHornChanges(currentHorns, picks);

        var inv = CultureInfo.InvariantCulture;
        var candStr = string.Join(",", hpPets.ConvertAll(p =>
            $"{p.Row}:{(p.Max == 0 || p.Current == 0 ? 0 : (int)Math.Round(100.0 * p.Current / p.Max))}"));
        var pickStr = string.Join(",", picks.ConvertAll(p => $"{p.Row}:{Clean(p.Why, 40)}"));
        var nameStr = string.Join(",", nameIds);
        LogPs($"PS|{now}|opt=1|b={board}|terr={Svc.ClientState.TerritoryType}|bt={battle}|detail={detailId}|names={nameStr}|cand={candStr}|picks={pickStr}|need={changes.Count}");

        if (changes.Count == 0)
        {
            LogPs($"PS|{now}|opt=1|b={board}|bt={battle}|calls=0|readback=ok|note=already_correct");
            _passDoneThisPhase = true;
            return;
        }

        if (!EnsureSigs())
        {
            LogPs($"PS|{now}|opt=1|b={board}|bt={battle}|abort=sigs_missing|calls=0");
            _abortThisPhase = true;
            _passDoneThisPhase = true;
            return;
        }

        // Residual G2 route: clear mismatched flute occupants, TogglePet each desired row in slot order, ApplyPetSelection.
        // Never ConfirmSelection / StartBattle / Int-8.
        var calls = new List<string>(8);
        var agent = (AgentInterface*)pet;

        foreach (var change in changes)
        {
            if (change.FromRow is >= 1 and <= BST_Beasts.Count)
            {
                calls.Add($"TogglePet-{change.FromRow}");
                _togglePet!(agent, (uint)change.FromRow);
                if (!ReadbackOk(pet, expectRemove: change.FromRow, expectAdd: 0, slot: change.Slot, afterToggleOff: true))
                {
                    AbortReadback(now, board, battle, calls, "toggle_off_mismatch");
                    return;
                }
            }
        }

        for (var slot = 0; slot < 3; slot++)
        {
            var want = slot < picks.Count ? picks[slot].Row : 0;
            if (want == 0)
                continue;
            var have = ReadPetIds(pet, PartySelectedPetIds);
            var at = slot < have.Count ? have[slot] : 0;
            if (at == want)
                continue;
            calls.Add($"TogglePet+{want}");
            _togglePet!(agent, (uint)want);
            if (!ReadbackOk(pet, expectRemove: 0, expectAdd: want, slot: slot, afterToggleOff: false))
            {
                AbortReadback(now, board, battle, calls, "toggle_on_mismatch");
                return;
            }
        }

        calls.Add("ApplyPetSelection");
        _applyPetSelection!(agent);

        var final = ReadPetIds(pet, PartySelectedPetIds);
        var desired = new List<int>(3);
        for (var i = 0; i < 3 && i < picks.Count; i++)
            desired.Add(picks[i].Row);

        if (!ListsMatchPrefix(final, desired))
        {
            AbortReadback(now, board, battle, calls, "apply_mismatch");
            return;
        }

        LogPs($"PS|{now}|opt=1|b={board}|bt={battle}|calls={string.Join(",", calls)}|readback=ok|sl={string.Join(".", final)}");
        _passDoneThisPhase = true;
    }

    private static void AbortReadback(long now, int board, int battle, List<string> calls, string reason)
    {
        LogPs($"PS|{now}|opt=1|b={board}|bt={battle}|calls={string.Join(",", calls)}|readback=fail|abort={reason}");
        _abortThisPhase = true;
        _passDoneThisPhase = true;
    }

    private static bool ReadbackOk(nint pet, int expectRemove, int expectAdd, int slot, bool afterToggleOff)
    {
        // After TogglePet(off): that row must no longer occupy the named flute slot.
        // After TogglePet(on) / Apply: final ListsMatchPrefix is the hard gate; this only
        // catches an immediate wrong write so we abort before pressing more buttons.
        var horns = ReadPetIds(pet, PartySelectedPetIds);
        if (afterToggleOff)
        {
            if (expectRemove is < 1 or > BST_Beasts.Count)
                return true;
            var at = slot < horns.Count ? horns[slot] : 0;
            return at != expectRemove;
        }
        if (expectAdd is >= 1 and <= BST_Beasts.Count)
        {
            // Desired row should appear somewhere in SelectedPetIds after a select toggle.
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

    private static bool TryIdentifyBattle(nint stage, int territoryBoard, out int board, out int battle, out uint detailId, out List<uint> nameIds)
    {
        board = 0;
        battle = -1;
        detailId = 0;
        nameIds = [];

        var contentId = *(uint*)(stage + StageContentId);
        if (contentId is >= 1 and <= 5 && (int)contentId != territoryBoard)
            return false;

        // Prefer the first EntryType==3 whose BattleDetail maps to this board. Mid-run the focused node's
        // battle-detail rows are present; when several boards' details appear, territory board filters.
        // If multiple distinct battles map on this board, require a single distinct battleDetailId among type-3
        // rows that belong to this board — otherwise abort (branch ambiguity).
        var seen = new HashSet<uint>();
        for (var i = 0; i < StageMaxEntries; i++)
        {
            var entry = stage + StageEntries + i * StageEntrySize;
            var type = *(uint*)(entry + StageEntryType);
            if (type == 0 && i > 0 && seen.Count > 0)
            {
                // Sparse trailing empties are common; keep scanning a bit, but stop after a long empty run.
            }
            if (type != 3)
                continue;

            var id = *(uint*)(entry + StageBattleDetailId);
            if (id == 0)
                continue;
            if (!BST_CrucibleData.TryBattleOfDetail(id, out var b, out var bt))
                continue;
            if (b != territoryBoard)
                continue;
            seen.Add(id);
            if (detailId == 0)
            {
                detailId = id;
                board = b;
                battle = bt;
            }
        }

        if (detailId == 0 || seen.Count != 1)
            return false;

        foreach (var e in BST_CrucibleData.Enemies)
            if (e.Board == board && e.Battle == battle)
                nameIds.Add(e.NameId);
        return nameIds.Count > 0;
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

    private static nint GetAgent(AgentId id)
    {
        var module = AgentModule.Instance();
        if (module is null)
            return 0;
        return (nint)module->GetAgentByInternalId(id);
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
            // ApplyPetSelection is an E8 call site in the PR listing — resolve the call target.
            _applyPetSelection = Marshal.GetDelegateForFunctionPointer<ApplyPetSelectionDelegate>(ResolveCallTarget(apply));
            _sigsOk = _togglePet is not null && _applyPetSelection is not null;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[BST_CruciblePetSelect] signature resolve failed");
            _sigsOk = false;
        }
        return _sigsOk;
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

    // ------------------------------------------------------------------ PSP| probe (read-only ReceiveEvent)

    private static void EnsureProbe()
    {
        _probeWanted = true;
        if (_receiveEventHook is not null)
            return;
        try
        {
            var agent = GetAgent(AgentId.XBMPetParty);
            if (agent == 0)
                return;
            var vt = *(nint*)agent;
            // AgentInterface.VirtualTable->ReceiveEvent — first function after the common AtkEventInterface layout.
            // Index matches ClientStructs AgentInterface / AtkEventInterface ReceiveEvent slot (used by DailyRoutines HookVFuncFromName).
            var receiveEvent = *(nint*)(vt + IntPtr.Size * ReceiveEventVTableIndex());
            if (receiveEvent == 0)
                return;
            _receiveEventHook = Svc.Hook.HookFromAddress<ReceiveEventDelegate>(receiveEvent, ReceiveEventDetour);
            _receiveEventHook.Enable();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[BST_CruciblePetSelect] PSP probe hook failed");
        }
    }

    private static void TeardownProbe()
    {
        _probeWanted = false;
        if (_receiveEventHook is null)
            return;
        try
        {
            _receiveEventHook.Disable();
            _receiveEventHook.Dispose();
        }
        catch { /* ignore */ }
        _receiveEventHook = null;
    }

    /// <summary>
    ///     VTable index for ReceiveEvent on AgentInterface. Verified against FFXIVClientStructs AtkEventInterface
    ///     (ReceiveEvent is the primary event entry). If the probe never fires, the live-grade round still has
    ///     manual-click absence as negative evidence — do not guess a different index without a capture.
    /// </summary>
    private static int ReceiveEventVTableIndex() => 2;

    private static AtkValue* ReceiveEventDetour(
        AgentInterface* agent, AtkValue* returnValues, AtkValue* values, uint valueCount, ulong eventKind)
    {
        try
        {
            if (_probeWanted && BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType) != 0)
            {
                var inv = CultureInfo.InvariantCulture;
                var sb = new StringBuilder(128);
                sb.Append("PSP|").Append(UnixMs().ToString(inv))
                  .Append("|kind=").Append(eventKind.ToString(inv))
                  .Append("|n=").Append(valueCount.ToString(inv));
                var n = Math.Min(valueCount, 8u);
                for (var i = 0u; i < n; i++)
                {
                    var v = values[i];
                    sb.Append('|').Append(i.ToString(inv)).Append(':');
                    switch (v.Type)
                    {
                        case AtkValueType.Int:
                            sb.Append('i').Append(v.Int.ToString(inv));
                            break;
                        case AtkValueType.UInt:
                            sb.Append('u').Append(v.UInt.ToString(inv));
                            break;
                        case AtkValueType.Bool:
                            sb.Append('b').Append(v.Byte != 0 ? '1' : '0');
                            break;
                        default:
                            sb.Append('t').Append(((int)v.Type).ToString(inv));
                            break;
                    }
                }
                Svc.Log.Information(sb.ToString());
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[BST_CruciblePetSelect] PSP log failed");
        }

        return _receiveEventHook!.Original(agent, returnValues, values, valueCount, eventKind);
    }

    private static long UnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static void LogPs(string line)
    {
        if (line.Length > 900)
            line = line[..900];
        Svc.Log.Information(line);
    }

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
