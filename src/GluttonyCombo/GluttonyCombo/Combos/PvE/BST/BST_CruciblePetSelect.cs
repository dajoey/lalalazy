#region Dependencies

using Dalamud.Hooking;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using static ECommons.GenericHelpers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using static GluttonyCombo.Combos.PvE.BST_CrucibleAdvisor;

#endregion

namespace GluttonyCombo.Combos.PvE;

/// <summary>
///     Crucible pet-selection autograb (live). Gates on Crucible territory, a non-empty run roster, and a
///     screen-open signal (XBMActivePet visible) — never on StageMode. Ranks via <see cref="PickSlots"/> and
///     assigns through the proven ReceiveEvent toggle route and/or PR #1952 TogglePet/ApplyPetSelection, with
///     SelectedPetIds read-back abort. StageMode is telemetry only. Default-off.
/// </summary>
internal static unsafe class BST_CruciblePetSelect
{
    // AgentXBMPetParty offsets (PR #1952 / DailyRoutines production).
    private const int PartyPetListAddonId = 0x60;
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
    private static ReceiveEventDelegate? _receiveEvent;
    private static bool _sigsResolved;
    private static bool _sigsOk;

    private static Hook<ReceiveEventDelegate>? _receiveEventHook;
    private static Hook<ReceiveEventDelegate>? _receiveEventWithResultHook;
    private static bool _probeWanted;

    private static FormationArmState _arm;
    private static bool _loggedAggroThisRun;
    private static bool _loggedOffThisPhase;
    private static int _lastBoard;
    private static string _lastPhaseSig = "";

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
        _receiveEvent = null;
    }

    private static void ResetRun()
    {
        _arm = default;
        _loggedAggroThisRun = false;
        _loggedOffThisPhase = false;
        _lastBoard = 0;
        _lastPhaseSig = "";
    }

    private static void RunFormationPass(int territoryBoard)
    {
        var stage = GetAgent(AgentId.XBMStageDetailList);
        var pet = GetAgent(AgentId.XBMPetParty);
        if (pet == 0)
            return;

        var mode = stage != 0 ? *(uint*)(stage + StageMode) : uint.MaxValue;
        var activePetOpen = IsAddonVisible("XBMActivePet");
        var petListOpen = IsPetListAddonVisible(pet);
        var partyAddonShown = IsAgentAddonShown(pet);
        // Chosen gate signal: XBMActivePet presence+visibility (G5). Pet-list / agent-addon are logged only.
        var screenOpen = activePetOpen;

        if (!TryReadPetVector(pet, PartySelectedPets, out var partyRows) || partyRows.Count == 0)
        {
            // No roster yet — keep StageMode telemetry out of decisions; still emit phase when signals move.
            MaybeLogPhase(pet, territoryBoard, mode, activePetOpen, petListOpen, partyAddonShown, screenOpen, armed: false, partyCount: 0);
            if (!screenOpen)
            {
                _arm = NextFormationArm(_arm, false, 0);
                _loggedOffThisPhase = false;
            }
            return;
        }

        var battleKey = 0;
        if (stage != 0 && TryIdentifyBattle(stage, territoryBoard, out _, out var battle, out var detailId, out _))
            battleKey = (int)detailId != 0 ? (int)detailId : battle + 1;

        var prevOpen = _arm.ScreenOpen;
        _arm = NextFormationArm(_arm, screenOpen, battleKey);
        if (!prevOpen && _arm.ScreenOpen)
            _loggedOffThisPhase = false;
        if (!_arm.ScreenOpen)
            _loggedOffThisPhase = false;

        var armed = IsFormationArmed(in _arm);
        MaybeLogPhase(pet, territoryBoard, mode, activePetOpen, petListOpen, partyAddonShown, screenOpen, armed, partyRows.Count);

        if (!screenOpen)
            return;

        var now = UnixMs();
        var optOn = (bool)BST.Config.BST_CrucibleAutoGrab;

        // Off-state line ABOVE every suppressing condition (battle id, sigs, assign route).
        if (!optOn)
        {
            if (!_loggedOffThisPhase)
            {
                LogPs($"PS|{now}|opt=0|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|mode={mode}|activePet={(activePetOpen ? 1 : 0)}|petList={(petListOpen ? 1 : 0)}|partyAddon={(partyAddonShown ? 1 : 0)}|party={partyRows.Count}|calls=0|note=off");
                _loggedOffThisPhase = true;
            }
            if (armed)
                _arm = MarkFormationPassDone(in _arm);
            return;
        }

        if (!armed)
            return;

        // Resolve sigs unconditionally so telemetry learns whether they resolve on this game version.
        var sigsOk = EnsureSigs();
        LogPs($"PS|{now}|opt=1|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|sigs={(sigsOk ? "ok" : "miss")}|calls=0");

        var eventOk = EnsureReceiveEvent(pet);
        if (!sigsOk && !eventOk)
        {
            LogPs($"PS|{now}|opt=1|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|abort=no_assign_route|sigs=miss|event=0|calls=0");
            _arm = MarkFormationAborted(in _arm);
            return;
        }

        // Prefer the observed event route: live PSP proved kind=0 [Int 1, Int petId] toggled SelectedPetIds.
        // Sig route has never resolved at runtime on a graded build.
        var route = eventOk ? "event" : "sig";

        if (stage == 0 || !TryIdentifyBattle(stage, territoryBoard, out var board, out battle, out detailId, out var nameIds))
        {
            LogPs($"PS|{now}|opt=1|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}|abort=battle_unidentified|route={route}|calls=0");
            _arm = MarkFormationPassDone(in _arm);
            return;
        }

        var hpPets = ReadPartyHpRaw(pet, partyRows);
        var hpMap = HpPercentByRow(hpPets);
        var candidates = new List<int>(partyRows.Count);
        foreach (var r in partyRows)
            if (r is >= 1 and <= BST_Beasts.Count)
                candidates.Add(r);

        var picks = PickSlots(board, battle, candidates, hpMap, 3);
        var currentHorns = ReadPetIds(pet, PartySelectedPetIds);
        var changes = PlanHornChanges(currentHorns, picks);

        var candStr = string.Join(",", hpPets.ConvertAll(p =>
            $"{p.Row}:{(p.Max == 0 || p.Current == 0 ? 0 : (int)Math.Round(100.0 * p.Current / p.Max))}"));
        var pickStr = string.Join(",", picks.ConvertAll(p => $"{p.Row}:{Clean(p.Why, 40)}"));
        var nameStr = string.Join(",", nameIds);
        LogPs($"PS|{now}|opt=1|b={board}|terr={Svc.ClientState.TerritoryType}|bt={battle}|detail={detailId}|names={nameStr}|cand={candStr}|picks={pickStr}|need={changes.Count}|route={route}|sigs={(sigsOk ? "ok" : "miss")}");

        if (changes.Count == 0)
        {
            LogPs($"PS|{now}|opt=1|b={board}|bt={battle}|route={route}|calls=0|readback=ok|note=already_correct|apply=not_needed");
            _arm = MarkFormationPassDone(in _arm);
            return;
        }

        var agent = (AgentInterface*)pet;
        var calls = new List<string>(8);

        foreach (var change in changes)
        {
            if (change.FromRow is >= 1 and <= BST_Beasts.Count)
            {
                if (!ToggleOne(agent, (uint)change.FromRow, route, calls))
                {
                    AbortReadback(now, board, battle, route, calls, "toggle_off_no_route");
                    return;
                }
                if (!ReadbackOk(pet, expectRemove: change.FromRow, expectAdd: 0, slot: change.Slot, afterToggleOff: true))
                {
                    AbortReadback(now, board, battle, route, calls, "toggle_off_mismatch");
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
            if (!ToggleOne(agent, (uint)want, route, calls))
            {
                AbortReadback(now, board, battle, route, calls, "toggle_on_no_route");
                return;
            }
            if (!ReadbackOk(pet, expectRemove: 0, expectAdd: want, slot: slot, afterToggleOff: false))
            {
                AbortReadback(now, board, battle, route, calls, "toggle_on_mismatch");
                return;
            }
        }

        var afterToggles = ReadPetIds(pet, PartySelectedPetIds);
        var desired = new List<int>(3);
        for (var i = 0; i < 3 && i < picks.Count; i++)
            desired.Add(picks[i].Row);

        var applyNeeded = !ListsMatchPrefix(afterToggles, desired);
        if (applyNeeded)
        {
            if (!sigsOk || _applyPetSelection is null)
            {
                AbortReadback(now, board, battle, route, calls, "apply_unavailable");
                return;
            }
            calls.Add("ApplyPetSelection");
            _applyPetSelection(agent);
        }

        var final = ReadPetIds(pet, PartySelectedPetIds);
        if (!ListsMatchPrefix(final, desired))
        {
            AbortReadback(now, board, battle, route, calls, "apply_mismatch");
            return;
        }

        LogPs($"PS|{now}|opt=1|b={board}|bt={battle}|route={route}|calls={string.Join(",", calls)}|readback=ok|apply={(applyNeeded ? "needed" : "not_needed")}|sl={string.Join(".", final)}");
        _arm = MarkFormationPassDone(in _arm);
    }

    private static bool ToggleOne(AgentInterface* agent, uint petId, string route, List<string> calls)
    {
        if (route == "event" && _receiveEvent is not null)
        {
            calls.Add($"EventToggle-{petId}");
            return FireEventToggle(agent, petId);
        }
        if (_togglePet is not null)
        {
            calls.Add($"TogglePet-{petId}");
            _togglePet(agent, petId);
            return true;
        }
        return false;
    }

    /// <summary>
    ///     Replay the live-proven pet-party toggle: ReceiveEvent(eventKind 0, [Int 1, Int petId]), resolved by
    ///     name through the generated vtable — never a literal index. Never targets the stage agent.
    /// </summary>
    private static bool FireEventToggle(AgentInterface* agent, uint petId)
    {
        if (_receiveEvent is null)
            return false;
        var values = stackalloc AtkValue[2];
        var ret = stackalloc AtkValue[1];
        values[0].Type = AtkValueType.Int;
        values[0].Int = 1;
        values[1].Type = AtkValueType.Int;
        values[1].Int = (int)petId;
        ret[0] = default;
        _receiveEvent(agent, ret, values, 2, 0);
        return true;
    }

    private static void AbortReadback(long now, int board, int battle, string route, List<string> calls, string reason)
    {
        LogPs($"PS|{now}|opt=1|b={board}|bt={battle}|route={route}|calls={string.Join(",", calls)}|readback=fail|abort={reason}");
        _arm = MarkFormationAborted(in _arm);
    }

    private static bool ReadbackOk(nint pet, int expectRemove, int expectAdd, int slot, bool afterToggleOff)
    {
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

        var seen = new HashSet<uint>();
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

    private static void MaybeLogPhase(
        nint pet, int territoryBoard, uint mode,
        bool activePetOpen, bool petListOpen, bool partyAddonShown, bool screenOpen, bool armed, int partyCount)
    {
        try
        {
            var flutes = partyCount > 0 ? ReadPetIds(pet, PartySelectedPetIds) : [];
            var optOn = (bool)BST.Config.BST_CrucibleAutoGrab;
            var sig =
                $"m={mode}|ap={(activePetOpen ? 1 : 0)}|pl={(petListOpen ? 1 : 0)}|pa={(partyAddonShown ? 1 : 0)}"
                + $"|so={(screenOpen ? 1 : 0)}|arm={(armed ? 1 : 0)}|opt={(optOn ? 1 : 0)}|p={partyCount}|f={flutes.Count}|bk={_arm.BattleKey}";
            if (sig == _lastPhaseSig)
                return;
            _lastPhaseSig = sig;
            LogPs($"PS|{UnixMs()}|gate=phase|mode={mode}|activePet={(activePetOpen ? 1 : 0)}|petList={(petListOpen ? 1 : 0)}"
                + $"|partyAddon={(partyAddonShown ? 1 : 0)}|screen={(screenOpen ? 1 : 0)}|armed={(armed ? 1 : 0)}"
                + $"|opt={(optOn ? 1 : 0)}|b={territoryBoard}|terr={Svc.ClientState.TerritoryType}"
                + $"|party={partyCount}|flutes={flutes.Count}|sl={string.Join(".", flutes)}|bk={_arm.BattleKey}|calls=0");
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[BST_CruciblePetSelect] phase log failed");
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
            Svc.Log.Debug(ex, "[BST_CruciblePetSelect] signature resolve failed");
            _sigsOk = false;
        }
        return _sigsOk;
    }

    private static bool EnsureReceiveEvent(nint pet)
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
            _receiveEvent = Marshal.GetDelegateForFunctionPointer<ReceiveEventDelegate>(receiveEvent);
            return _receiveEvent is not null;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[BST_CruciblePetSelect] ReceiveEvent resolve failed");
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

    // ------------------------------------------------------------------ PSP| probe (read-only ReceiveEvent)

    private static void EnsureProbe()
    {
        _probeWanted = true;
        if (_receiveEventHook is not null || _receiveEventWithResultHook is not null)
            return;
        try
        {
            var agent = (AgentInterface*)GetAgent(AgentId.XBMPetParty);
            if (agent is null)
                return;
            var vt = agent->VirtualTable;
            if (vt is null)
                return;

            var receiveEvent = (nint)vt->ReceiveEvent;
            if (receiveEvent != 0)
            {
                _receiveEventHook = Svc.Hook.HookFromAddress<ReceiveEventDelegate>(receiveEvent, ReceiveEventDetour);
                _receiveEventHook.Enable();
            }

            var receiveEventWithResult = (nint)vt->ReceiveEventWithResult;
            if (receiveEventWithResult != 0 && receiveEventWithResult != receiveEvent)
            {
                _receiveEventWithResultHook =
                    Svc.Hook.HookFromAddress<ReceiveEventDelegate>(receiveEventWithResult, ReceiveEventWithResultDetour);
                _receiveEventWithResultHook.Enable();
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[BST_CruciblePetSelect] PSP probe hook failed");
        }
    }

    private static void TeardownProbe()
    {
        _probeWanted = false;
        DisposeHook(ref _receiveEventHook);
        DisposeHook(ref _receiveEventWithResultHook);
    }

    private static void DisposeHook(ref Hook<ReceiveEventDelegate>? hook)
    {
        if (hook is null)
            return;
        try
        {
            hook.Disable();
            hook.Dispose();
        }
        catch { /* ignore */ }
        hook = null;
    }

    private static AtkValue* ReceiveEventDetour(
        AgentInterface* agent, AtkValue* returnValues, AtkValue* values, uint valueCount, ulong eventKind)
    {
        LogProbe("re", values, valueCount, eventKind);
        return _receiveEventHook!.Original(agent, returnValues, values, valueCount, eventKind);
    }

    private static AtkValue* ReceiveEventWithResultDetour(
        AgentInterface* agent, AtkValue* returnValues, AtkValue* values, uint valueCount, ulong eventKind)
    {
        LogProbe("rewr", values, valueCount, eventKind);
        return _receiveEventWithResultHook!.Original(agent, returnValues, values, valueCount, eventKind);
    }

    /// <summary> Read-only PSP| record of one pet-party agent event. Never alters arguments or the return value. </summary>
    private static void LogProbe(string via, AtkValue* values, uint valueCount, ulong eventKind)
    {
        try
        {
            if (!_probeWanted || BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType) == 0)
                return;

            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(128);
            sb.Append("PSP|").Append(UnixMs().ToString(inv))
              .Append("|via=").Append(via)
              .Append("|kind=").Append(eventKind.ToString(inv))
              .Append("|n=").Append(valueCount.ToString(inv));
            var n = values is null ? 0u : Math.Min(valueCount, 8u);
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
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[BST_CruciblePetSelect] PSP log failed");
        }
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
