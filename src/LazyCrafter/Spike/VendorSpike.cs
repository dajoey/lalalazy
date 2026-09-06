using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LazyCrafter.Core;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace LazyCrafter.Spike;

/// <summary>
/// P6 SPIKE (t_977b94b4, revived as t_933683a5). Answers one question in-game: after
/// <c>Lifestream.Teleport</c>, can <c>vnavmesh.SimpleMove.PathfindAndMoveTo</c> reach a gil-vendor NPC and can we
/// open its shop, reliably, across 5 vendors in 3 zones? Joey's rule: the walk-to-vendor toggle ships only on 5/5.
///
/// <para><b>This class is INERT.</b> It is a slash command and nothing else - nothing here is wired into dispatch,
/// the cart, the Run tab or the vendor hand-off, and a normal cart run behaves identically with and without it.
/// It runs cold: no cart, no dispatch, no prior state, no particular zone or job.</para>
///
/// <para>Usage: <c>/lcraft spike list</c> · <c>/lcraft spike 1..5</c> · <c>/lcraft spike all</c> ·
/// <c>/lcraft spike stop</c> · <c>/lcraft spike results</c> (prints AND copies the paste block).</para>
///
/// <para>Sequence per vendor (framework-ticked state machine, no threads):
/// preflight (vnavmesh + Lifestream present and answering, else say so and stop) → teleport → wait for the zone
/// change (BetweenAreas seen then cleared, no loading screen, player targetable, territory == expected) → wait
/// <c>Nav.IsReady</c> (vnavmesh auto-reloads the mesh after a zone change) → dismount if mounted →
/// <c>SimpleMove.PathfindAndMoveTo(npcPos, fly:false)</c> → wait for <c>Path.IsRunning</c> to go true then false
/// (stuck watchdog 8 s of no movement; hard cap 120 s) → if still outside interact range, ONE direct nudge via
/// <c>Path.MoveTo([npcPos])</c> → find the ENpc in the object table by BaseId, target it,
/// <c>TargetSystem.InteractWithObject</c> → <c>Shop</c>, or a <c>SelectIconString</c>/<c>SelectString</c> menu
/// first, in which case the entry is chosen by <b>TEXT</b> (the NPC's own GilShop names, read from the sheets)
/// - never by index, which the first spike pass flagged as wrong for 4 of these 5 NPCs.</para>
/// </summary>
public sealed unsafe class VendorSpike : IDisposable
{
    /// <param name="ShopEntries">
    /// The exact menu entry texts that lead to this NPC's gil shop, in preference order. These are the
    /// <c>GilShop.Name</c> values of the NPC's own handlers, read offline from the game's sheets 2026-09-06
    /// (card t_933683a5); an empty array means the NPC has a single handler and opens <c>Shop</c> directly.
    /// </param>
    public sealed record Vendor(int N, string Zone, ushort Territory, uint AetheryteId, uint NpcId, string NpcName,
        Vector3 Pos, int Handlers, string[] ShopEntries);

    // Positions verified twice, offline, against the installed sqpack (card t_933683a5, 2026-09-06): the Level
    // sheet (Type 8 = ENpc) and the territory's own LGB - planevent.lgb for Ul'dah and Gridania, planner.lgb for
    // Limsa, whose two vendors are not in planevent at all. All five agreed to within 0.1y.
    // Three zones, one teleport aetheryte each. Mix of short/long walks and single-/multi-handler NPCs.
    public static readonly Vendor[] Vendors =
    [
        new(1, "Limsa Lominsa Lower Decks", 129, 8, 1001787, "Bango Zango", new(-62.1f, 18.0f, 9.4f), 13,
            ["Purchase Items", "Purchase Battle Accessories"]),                                              // 24y, menu (10 gil shops)
        new(2, "Limsa Lominsa Lower Decks", 129, 8, 1003253, "Gerulf", new(-149.9f, 18.2f, 36.9f), 1,
            []),                                                                                              // 76y, single GilShop -> Shop opens directly
        new(3, "Ul'dah - Steps of Nald", 130, 9, 1001974, "Rianne", new(-67.6f, 4.6f, -107.5f), 3,
            ["Purchase Battle Gear", "Purchase Field Gear", "Purchase Novelty Gear"]),                        // 99y, 3 handlers, NO "Purchase Items"
        new(4, "Ul'dah - Steps of Nald", 130, 9, 1004417, "Roarich", new(-33.6f, 9.1f, -84.3f), 12,
            ["Purchase Items", "Purchase Battle Accessories"]),                                              // 140y, menu, multi-level city
        new(5, "New Gridania", 132, 2, 1001276, "Maisenta", new(14.0f, 0.1f, 2.1f), 18,
            ["Purchase Items", "Purchase Battle Accessories"]),                                              // 34y, menu (18 handlers)
    ];

    private const float InteractRange = 3.5f;       // vanilla NPC talk range is ~4y at the origin; NPCs are thin, 3.5 leaves margin
    private const int TeleportTimeoutMs = 45_000;
    private const int NavReadyTimeoutMs = 60_000;
    private const int WalkTimeoutMs = 120_000;
    private const int StuckMs = 8_000;
    private const int InteractTimeoutMs = 15_000;

    private const string VnavInternalName = "vnavmesh";
    private const string LifestreamInternalName = "Lifestream";

    private enum State { Idle, Teleport, ZoneChange, NavReady, Dismount, Walk, Nudge, Interact, Menu }

    private readonly IDalamudPluginInterface _pi;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IObjectTable _objects;
    private readonly ITargetManager _targets;
    private readonly IGameGui _gameGui;
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;
    private readonly Func<string> _version;

    // vnavmesh (names per vnavmesh/IPCProvider.cs; all present in the installed 1.2.3.14, verified 2026-09-06)
    private readonly ICallGateSubscriber<bool> _navIsReady;
    private readonly ICallGateSubscriber<float> _navBuildProgress;
    private readonly ICallGateSubscriber<Vector3, bool, bool> _pathfindAndMoveTo;
    private readonly ICallGateSubscriber<bool> _simpleMoveInProgress;
    private readonly ICallGateSubscriber<bool> _pathIsRunning;
    private readonly ICallGateSubscriber<object> _pathStop;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> _pathMoveTo;
    // Lifestream (names per Lifestream/IPC/IPCProvider.cs, installed 2.5.4.16)
    private readonly ICallGateSubscriber<uint, byte, bool> _lsTeleport;
    private readonly ICallGateSubscriber<bool> _lsIsBusy;

    private readonly Queue<Vendor> _queue = new();
    private Vendor? _cur;
    private State _state = State.Idle;
    private readonly Stopwatch _total = new();
    private readonly Stopwatch _phase = new();
    private long _tpMs, _navMs, _walkMs, _interactMs;
    private bool _sawBetweenAreas, _sawRunning, _nudged;
    private float _finalDist;
    private Vector3 _lastPos;
    private long _lastMoveAt;
    private string _menu = "-";
    private string _menuPicked = "-";
    private readonly List<string> _jank = [];
    private readonly List<SpikeResult> _results = [];
    private string? _pendingClipboard;
    private bool _drawHooked;

    public IReadOnlyList<SpikeResult> Results => _results;

    public VendorSpike(IDalamudPluginInterface pi, IFramework framework, IClientState clientState, ICondition condition,
        IObjectTable objects, ITargetManager targets, IGameGui gameGui, IChatGui chat, IPluginLog log, Func<string> version)
    {
        _pi = pi; _framework = framework; _clientState = clientState; _condition = condition; _objects = objects;
        _targets = targets; _gameGui = gameGui; _chat = chat; _log = log; _version = version;
        _navIsReady = pi.GetIpcSubscriber<bool>($"{VnavInternalName}.Nav.IsReady");
        _navBuildProgress = pi.GetIpcSubscriber<float>($"{VnavInternalName}.Nav.BuildProgress");
        _pathfindAndMoveTo = pi.GetIpcSubscriber<Vector3, bool, bool>($"{VnavInternalName}.SimpleMove.PathfindAndMoveTo");
        _simpleMoveInProgress = pi.GetIpcSubscriber<bool>($"{VnavInternalName}.SimpleMove.PathfindInProgress");
        _pathIsRunning = pi.GetIpcSubscriber<bool>($"{VnavInternalName}.Path.IsRunning");
        _pathStop = pi.GetIpcSubscriber<object>($"{VnavInternalName}.Path.Stop");
        _pathMoveTo = pi.GetIpcSubscriber<List<Vector3>, bool, object>($"{VnavInternalName}.Path.MoveTo");
        _lsTeleport = pi.GetIpcSubscriber<uint, byte, bool>($"{LifestreamInternalName}.Teleport");
        _lsIsBusy = pi.GetIpcSubscriber<bool>($"{LifestreamInternalName}.IsBusy");
        _framework.Update += Tick;
    }

    public void Dispose()
    {
        _framework.Update -= Tick;
        UnhookDraw();
        TryStopPath();
    }

    // ------------------------------------------------------------------ command surface

    public void Command(string arg)
    {
        arg = arg.Trim().ToLowerInvariant();
        switch (arg)
        {
            case "" or "list":
                Say("vendors: " + string.Join(" | ", Vendors.Select(v => $"{v.N}={v.NpcName} ({v.Zone}, {v.Handlers}h)")));
                Say("usage: /lcraft spike <1-5|all|stop|results>");
                return;
            case "stop":
                Abort("stopped by user");
                _queue.Clear();
                return;
            case "results":
                PrintResults();
                return;
            case "all":
                if (!Preflight()) return;
                _results.Clear();
                foreach (var v in Vendors) _queue.Enqueue(v);
                break;
            default:
                if (!int.TryParse(arg, out var n) || n < 1 || n > Vendors.Length) { Say("unknown arg; try: /lcraft spike list"); return; }
                if (!Preflight()) return;
                _queue.Enqueue(Vendors[n - 1]);
                break;
        }
        if (_state == State.Idle) StartNext();
        else Say($"queued; {_queue.Count} pending");
    }

    /// <summary>
    /// Hard preconditions, checked LOUDLY before anything moves (card t_933683a5 decision 4): both plugins must be
    /// installed AND their IPC must actually answer. A missing plugin is never a silent no-op and never counted as
    /// a failed vendor - the run simply does not start.
    /// </summary>
    private bool Preflight()
    {
        foreach (var (name, human) in new[] { (VnavInternalName, "vnavmesh"), (LifestreamInternalName, "Lifestream") })
        {
            if (!_pi.InstalledPlugins.Any(p => p.InternalName == name && p.IsLoaded))
            {
                _chat.PrintError($"[LazyCrafter spike] {human} is not installed (or not loaded). The walk-to-vendor spike cannot run without it - install {human} and try again.");
                return false;
            }
        }
        try { _navIsReady.InvokeFunc(); }
        catch (Exception ex)
        {
            _chat.PrintError($"[LazyCrafter spike] vnavmesh is loaded but its IPC did not answer ({ex.GetType().Name}) - the spike cannot run. Update vnavmesh, or report this line.");
            return false;
        }
        try { _lsIsBusy.InvokeFunc(); }
        catch (Exception ex)
        {
            _chat.PrintError($"[LazyCrafter spike] Lifestream is loaded but its IPC did not answer ({ex.GetType().Name}) - the spike cannot run. Update Lifestream, or report this line.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// <c>/lcraft spike results</c>: the one block Joey copies back (card t_933683a5 decision 7). Printed to chat
    /// AND put on the clipboard in a single action, the same way the Run tab's Copy report does it.
    /// </summary>
    private void PrintResults()
    {
        if (_results.Count == 0) { Say("no results yet - run '/lcraft spike all' first."); return; }
        var block = SpikeReport.Render(_version(), _results);
        foreach (var line in block.Split('\n')) _chat.Print(line);
        _pendingClipboard = block;
        HookDraw();
        Say("(the block above has been copied to your clipboard - paste it back as-is)");
        _log.Information("[spike] results block:\n{Block}", block);
    }

    // The clipboard is an ImGui context call, so it must happen on the draw thread. Hooked only while there is
    // something to copy - one frame - then unhooked, so the spike costs nothing per frame when idle.
    private void HookDraw()
    {
        if (_drawHooked) return;
        _pi.UiBuilder.Draw += PumpClipboard;
        _drawHooked = true;
    }

    private void UnhookDraw()
    {
        if (!_drawHooked) return;
        _pi.UiBuilder.Draw -= PumpClipboard;
        _drawHooked = false;
    }

    private void PumpClipboard()
    {
        if (_pendingClipboard is { } text)
        {
            _pendingClipboard = null;
            try { ImGui.SetClipboardText(text); }
            catch (Exception ex) { _log.Warning("[spike] clipboard copy failed: {Msg}", ex.Message); }
        }
        UnhookDraw();
    }

    // ------------------------------------------------------------------ the state machine

    private void StartNext()
    {
        if (!_queue.TryDequeue(out var v)) { _state = State.Idle; _cur = null; return; }
        _cur = v;
        _tpMs = _navMs = _walkMs = _interactMs = 0;
        _sawBetweenAreas = _sawRunning = _nudged = false;
        _finalDist = float.NaN; _menu = "-"; _menuPicked = "-"; _jank.Clear();
        _total.Restart();
        Say($"{v.N}/5 {v.NpcName} @ {v.Zone}: teleporting (aetheryte {v.AetheryteId})...");
        Enter(State.Teleport);
    }

    private void Enter(State s) { _state = s; _phase.Restart(); }

    private void Tick(IFramework _)
    {
        if (_state is State.Idle || _cur is null) return;
        try { Step(); }
        catch (Exception ex)
        {
            _log.Error(ex, "[spike] tick failed in {State}", _state);
            Finish(false, StageOf(_state), $"exception in {_state}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string StageOf(State s) => s switch
    {
        State.Teleport => SpikeStage.Teleport,
        State.ZoneChange => SpikeStage.ZoneSettle,
        State.NavReady => SpikeStage.Navmesh,
        State.Dismount => SpikeStage.Dismount,
        State.Walk or State.Nudge => SpikeStage.Pathfind,
        State.Interact => SpikeStage.Interact,
        State.Menu => SpikeStage.Menu,
        _ => SpikeStage.Preflight,
    };

    private void Step()
    {
        var v = _cur!;
        var player = _objects.LocalPlayer;
        switch (_state)
        {
            case State.Teleport:
                if (_clientState.TerritoryType == v.Territory && !_condition[ConditionFlag.BetweenAreas])
                {
                    // Already here. Note it - a same-zone teleport is a different path than the question asks.
                    _jank.Add("already in zone (no teleport)");
                    _tpMs = 0;
                    Enter(State.NavReady);
                    return;
                }
                if (SafeCall(() => _lsIsBusy.InvokeFunc(), "Lifestream.IsBusy")) { if (_phase.ElapsedMilliseconds > 10_000) Finish(false, SpikeStage.Teleport, "Lifestream stayed busy for 10 s (another teleport or world change in progress)"); return; }
                var ok = SafeCall(() => _lsTeleport.InvokeFunc(v.AetheryteId, 0), "Lifestream.Teleport");
                if (!ok) { Finish(false, SpikeStage.Teleport, "Lifestream.Teleport returned false - aetheryte not attuned, in combat, or occupied"); return; }
                Enter(State.ZoneChange);
                return;

            case State.ZoneChange:
                if (_condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51]) _sawBetweenAreas = true;
                if (_sawBetweenAreas && !_condition[ConditionFlag.BetweenAreas] && !_condition[ConditionFlag.BetweenAreas51]
                    && !LoadingScreenVisible() && player is { IsTargetable: true } && _clientState.TerritoryType == v.Territory)
                {
                    _tpMs = _phase.ElapsedMilliseconds;
                    Enter(State.NavReady);
                    return;
                }
                if (_phase.ElapsedMilliseconds > TeleportTimeoutMs)
                    Finish(false, SpikeStage.ZoneSettle, $"the zone change did not settle in {TeleportTimeoutMs / 1000} s (sawBetweenAreas={_sawBetweenAreas}, territory={_clientState.TerritoryType}, expected {v.Territory})");
                return;

            case State.NavReady:
                if (SafeCall(() => _navIsReady.InvokeFunc(), "vnavmesh.Nav.IsReady"))
                {
                    _navMs = _phase.ElapsedMilliseconds;
                    if (_navMs > 5_000) _jank.Add($"navmesh took {_navMs / 1000.0:F1} s to be ready");
                    Enter(_condition[ConditionFlag.Mounted] ? State.Dismount : State.Walk);
                    return;
                }
                if (_phase.ElapsedMilliseconds > NavReadyTimeoutMs)
                    Finish(false, SpikeStage.Navmesh, $"vnavmesh was not ready {NavReadyTimeoutMs / 1000} s after landing (BuildProgress={SafeCall(() => _navBuildProgress.InvokeFunc(), "BuildProgress"):F2})");
                return;

            case State.Dismount:
                if (!_condition[ConditionFlag.Mounted]) { _jank.Add("had to dismount"); Enter(State.Walk); return; }
                if (_phase.ElapsedMilliseconds % 1000 < 20) ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23); // 23 = Dismount (GeneralAction sheet)
                if (_phase.ElapsedMilliseconds > 10_000) Finish(false, SpikeStage.Dismount, "could not dismount within 10 s");
                return;

            case State.Walk:
                if (player is null) return;
                if (!_sawRunning && _phase.ElapsedMilliseconds < 50)
                {
                    var issued = SafeCall(() => _pathfindAndMoveTo.InvokeFunc(v.Pos, false), "vnavmesh.SimpleMove.PathfindAndMoveTo");
                    if (!issued) { Finish(false, SpikeStage.Pathfind, "PathfindAndMoveTo returned false - a pathfind was already in progress"); return; }
                    _lastPos = player.Position; _lastMoveAt = _phase.ElapsedMilliseconds;
                    return;
                }
                var running = SafeCall(() => _pathIsRunning.InvokeFunc(), "vnavmesh.Path.IsRunning");
                var solving = SafeCall(() => _simpleMoveInProgress.InvokeFunc(), "vnavmesh.SimpleMove.PathfindInProgress");
                if (running) _sawRunning = true;
                var dist = Vector3.Distance(player.Position, v.Pos);
                if (Vector3.Distance(player.Position, _lastPos) > 0.5f) { _lastPos = player.Position; _lastMoveAt = _phase.ElapsedMilliseconds; }
                if (dist <= InteractRange)
                {
                    TryStopPath();
                    _walkMs = _phase.ElapsedMilliseconds; _finalDist = dist;
                    Enter(State.Interact);
                    return;
                }
                if (_sawRunning && !running && !solving)
                {
                    // vnavmesh finished its route and we are still outside range. Once: push straight at the NPC.
                    if (!_nudged)
                    {
                        _nudged = true;
                        _jank.Add($"vnavmesh stopped {dist:F1}y out; nudged");
                        SafeCall(() => { _pathMoveTo.InvokeAction([v.Pos], false); return true; }, "vnavmesh.Path.MoveTo");
                        _sawRunning = false;
                        Enter(State.Nudge);
                        return;
                    }
                    _walkMs = _phase.ElapsedMilliseconds; _finalDist = dist;
                    Finish(false, SpikeStage.Pathfind, $"the walk ended {dist:F1}y from the NPC (needs {InteractRange}y or closer)");
                    return;
                }
                if (_sawRunning && _phase.ElapsedMilliseconds - _lastMoveAt > StuckMs)
                {
                    TryStopPath();
                    _walkMs = _phase.ElapsedMilliseconds; _finalDist = dist;
                    Finish(false, SpikeStage.WalkTimeout, $"stopped moving for {StuckMs / 1000} s while still {dist:F1}y from the NPC");
                    return;
                }
                if (!_sawRunning && !solving && _phase.ElapsedMilliseconds > 10_000)
                {
                    Finish(false, SpikeStage.Pathfind, "vnavmesh never started moving - no path was found (check /xllog for 'Failed to find path')");
                    return;
                }
                if (_phase.ElapsedMilliseconds > WalkTimeoutMs) { TryStopPath(); Finish(false, SpikeStage.WalkTimeout, $"the walk ran past {WalkTimeoutMs / 1000} s and was given up"); }
                return;

            case State.Nudge:
                if (player is null) return;
                var d2 = Vector3.Distance(player.Position, v.Pos);
                var run2 = SafeCall(() => _pathIsRunning.InvokeFunc(), "vnavmesh.Path.IsRunning");
                if (run2) _sawRunning = true;
                if (d2 <= InteractRange || (_sawRunning && !run2) || _phase.ElapsedMilliseconds > 15_000)
                {
                    TryStopPath();
                    _walkMs += _phase.ElapsedMilliseconds; _finalDist = d2;
                    if (d2 <= InteractRange) Enter(State.Interact);
                    else Finish(false, SpikeStage.Pathfind, $"the nudge ended {d2:F1}y from the NPC (needs {InteractRange}y or closer)");
                }
                return;

            case State.Interact:
            {
                var npc = FindNpc(v.NpcId);
                if (npc is null)
                {
                    if (_phase.ElapsedMilliseconds > 5_000) Finish(false, SpikeStage.Interact, $"NPC {v.NpcName} (BaseId {v.NpcId}) was not in the object table within 5 s - despawned, or a different instance");
                    return;
                }
                if (ShopOpen()) { _interactMs = _phase.ElapsedMilliseconds; Finish(true, null, null); return; }
                if (MenuOpen(out var menuName, out var menuAddon))
                {
                    _menu = menuName;
                    _jank.Add($"{menuName} menu first");
                    if (!SelectShopEntry(menuName, menuAddon, v))
                    {
                        Finish(false, SpikeStage.Menu, $"the {menuName} menu opened but none of its entries matched this NPC's shop names [{string.Join(" | ", v.ShopEntries)}] - entries seen: [{string.Join(" | ", ReadEntries(menuName, menuAddon))}]");
                        return;
                    }
                    Enter(State.Menu);
                    return;
                }
                if (_targets.Target?.GameObjectId != npc.GameObjectId)
                {
                    if (_phase.ElapsedMilliseconds % 300 < 20) _targets.Target = npc;
                    return;
                }
                if (_phase.ElapsedMilliseconds % 1000 < 20 && !_condition[ConditionFlag.Casting] && !_condition[ConditionFlag.OccupiedInQuestEvent])
                    TargetSystem.Instance()->InteractWithObject((CSGameObject*)npc.Address, false);
                if (_phase.ElapsedMilliseconds > InteractTimeoutMs)
                    Finish(false, SpikeStage.Interact, $"nothing opened within {InteractTimeoutMs / 1000} s of interacting (standing {Vector3.Distance(player!.Position, npc.Position):F1}y away)");
                return;
            }

            case State.Menu:
                if (ShopOpen()) { _interactMs = _phase.ElapsedMilliseconds; Finish(true, null, null); return; }
                // A shop can sit behind a second menu (a topic list). Re-match by text on whatever is open now.
                if (MenuOpen(out var again, out var againAddon) && _phase.ElapsedMilliseconds > 1500 && _phase.ElapsedMilliseconds % 1500 < 20)
                    SelectShopEntry(again, againAddon, v);
                if (_phase.ElapsedMilliseconds > InteractTimeoutMs)
                    Finish(false, SpikeStage.ShopOpen, $"picked \"{_menuPicked}\" in the {_menu} menu but no Shop window opened within {InteractTimeoutMs / 1000} s");
                return;
        }
    }

    private void Finish(bool ok, string? stage, string? why)
    {
        var v = _cur!;
        _total.Stop();
        TryStopPath();
        var timings = $"tp {Sec(_tpMs)} | nav {Sec(_navMs)} | walk {Sec(_walkMs)} (final {(float.IsNaN(_finalDist) ? "?" : $"{_finalDist:F1}y")}, nudged={(_nudged ? "yes" : "no")}) | interact {Sec(_interactMs)} | menu={_menu}/{_menuPicked}";
        var result = new SpikeResult(v.N, v.NpcName, v.Zone, ok, _total.ElapsedMilliseconds / 1000.0,
            ok ? null : stage ?? SpikeStage.Preflight, ok ? null : why, timings, [.. _jank]);
        _results.RemoveAll(r => r.N == v.N);
        _results.Add(result);

        var line = $"[spike] {v.N}/5 {v.NpcName} ({v.Zone}): {(ok ? "PASS" : "FAIL")} {Sec(_total.ElapsedMilliseconds)}"
            + (ok ? "" : $" at {stage} - {why}")
            + $" | {timings}"
            + $" | notes: {(_jank.Count > 0 ? string.Join("; ", _jank) : "none")}";
        _log.Information("{Line}", line);
        Say(line);
        _state = State.Idle;
        _cur = null;
        if (_queue.Count > 0)
            // Pause between vendors so the shop closes and the line can be read before the next teleport.
            _framework.RunOnTick(StartNext, TimeSpan.FromSeconds(4));
        else
        {
            Say($"done - {_results.Count(r => r.Pass)}/{SpikeReport.Gate} passed. Run '/lcraft spike results' to copy the block to paste back.");
            if (_results.Count >= SpikeReport.Gate) PrintResults();
        }
    }

    private void Abort(string why)
    {
        if (_cur is not null) Finish(false, StageOf(_state), why);
        else Say("nothing running.");
    }

    private static string Sec(long ms) => $"{ms / 1000.0:F1}s";

    // ------------------------------------------------------------------ game reads

    private IGameObject? FindNpc(uint baseId)
    {
        foreach (var o in _objects)
            if (o.ObjectKind == ObjectKind.EventNpc && o.BaseId == baseId && o.IsTargetable) return o;
        return null;
    }

    private AtkUnitBase* Addon(string name)
    {
        // API 15: IGameGui.GetAddonByName returns a pointer wrapper; .Address is 0 when absent.
        var a = _gameGui.GetAddonByName(name, 1);
        return a == nint.Zero ? null : (AtkUnitBase*)a.Address;
    }

    private bool Visible(string name)
    {
        var a = Addon(name);
        return a != null && a->IsVisible;
    }

    private bool ShopOpen() => Visible("Shop");

    private bool MenuOpen(out string name, out AtkUnitBase* addon)
    {
        foreach (var n in new[] { "SelectIconString", "SelectString" })
        {
            var a = Addon(n);
            if (a != null && GenericHelpers.IsAddonReady(a)) { name = n; addon = a; return true; }
        }
        name = "-"; addon = null; return false;
    }

    private static List<string> ReadEntries(string addonName, AtkUnitBase* addon)
    {
        var list = new List<string>();
        try
        {
            if (addonName == "SelectIconString")
                foreach (var e in new AddonMaster.SelectIconString(addon).Entries) list.Add(e.Text);
            else
                foreach (var e in new AddonMaster.SelectString(addon).Entries) list.Add(e.Text);
        }
        catch { /* a half-built menu; the caller retries on the next tick */ }
        return list;
    }

    /// <summary>
    /// Choose the menu entry that leads to the gil shop by <b>TEXT</b>, never by index. The first spike pass picked
    /// index 0 and its own README flagged that as wrong for 4 of these 5 NPCs (Rianne has no "Purchase Items" entry
    /// at all, and Bango Zango's index 0 is a quest). Returns false when nothing matched, so the caller can report
    /// exactly which entries were on screen instead of stalling on a timeout.
    /// </summary>
    private bool SelectShopEntry(string addonName, AtkUnitBase* addon, Vendor v)
    {
        var entries = ReadEntries(addonName, addon);
        if (entries.Count == 0) return true;   // menu not readable yet; retry next tick rather than declare a miss
        foreach (var wanted in v.ShopEntries)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (!string.Equals(entries[i].Trim(), wanted, StringComparison.OrdinalIgnoreCase)) continue;
                _menuPicked = entries[i].Trim();
                ECommons.Automation.Callback.Fire(addon, true, i);
                return true;
            }
        }
        return false;
    }

    private bool LoadingScreenVisible() => Visible("NowLoading") || Visible("FadeMiddle") || Visible("FadeBack");

    private void TryStopPath() => SafeCall(() => { _pathStop.InvokeAction(); return true; }, "vnavmesh.Path.Stop");

    private T SafeCall<T>(Func<T> f, string what)
    {
        try { return f(); }
        catch (Exception ex)
        {
            if (!_jank.Contains($"IPC {what} threw")) _jank.Add($"IPC {what} threw");
            _log.Warning("[spike] IPC {What} failed: {Msg}", what, ex.Message);
            return default!;
        }
    }

    private void Say(string s) => _chat.Print($"[LazyCrafter spike] {s}");
}
