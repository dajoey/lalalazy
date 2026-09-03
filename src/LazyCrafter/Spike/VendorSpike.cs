using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace LazyCrafter.Spike;

/// <summary>
/// P6 SPIKE (t_977b94b4) — throwaway. Answers one question in-game:
/// after <c>Lifestream.Teleport</c>, can <c>vnavmesh.SimpleMove.PathfindAndMoveTo</c> reach a gil-vendor NPC and
/// can we open its shop, reliably, across 5 vendors in 3 zones?
///
/// Usage: <c>/lcraft spike list</c> · <c>/lcraft spike 1..5</c> · <c>/lcraft spike all</c> · <c>/lcraft spike stop</c>.
/// Every run prints one result line to chat and to /xllog with timings and jank flags. The line is the evidence;
/// this class is not the product. Nothing here is wired into dispatch — it must be 5/5 clean before that happens.
///
/// Sequence per vendor (framework-ticked state machine, no threads):
///   Teleport → wait for the zone change (BetweenAreas seen then cleared, no loading screen, player targetable,
///   territory == expected) → wait vnavmesh Nav.IsReady (auto-reload after zone change) → dismount if mounted →
///   SimpleMove.PathfindAndMoveTo(npcPos, fly:false) → wait for Path.IsRunning to go true then false
///   (stuck watchdog 8 s of no movement; hard cap 120 s) → if still outside interact range, ONE direct nudge via
///   Path.MoveTo([npcPos]) → find the ENpc in the object table by BaseId, target it, TargetSystem.InteractWithObject
///   → wait for "Shop" (direct) or a SelectIconString/SelectString menu (multi-handler NPC; the spike selects
///   entry 0 and reports whether Shop followed) → done.
/// </summary>
public sealed unsafe class VendorSpike : IDisposable
{
    public sealed record Vendor(int N, string Zone, ushort Territory, uint AetheryteId, uint NpcId, string NpcName, Vector3 Pos, int Handlers);

    // Positions from the game's own planevent.lgb / Level sheet (spikes/006-vnav-vendor/VendorProbe), 2026-09-03.
    // Three zones, one teleport aetheryte each. Mix of short/long walks and single-/multi-handler NPCs.
    public static readonly Vendor[] Vendors =
    [
        new(1, "Limsa Lominsa Lower Decks", 129, 8, 1001787, "Bango Zango", new(-62.1f, 18.0f, 9.4f), 13),      // 24y, menu (13 handlers)
        new(2, "Limsa Lominsa Lower Decks", 129, 8, 1003253, "Gerulf",      new(-149.9f, 18.2f, 36.9f), 1),     // 76y, single GilShop
        new(3, "Ul'dah - Steps of Nald",    130, 9, 1001974, "Rianne",      new(-67.6f, 4.6f, -107.5f), 3),     // 99y, 3 handlers
        new(4, "Ul'dah - Steps of Nald",    130, 9, 1004417, "Roarich",     new(-33.6f, 9.1f, -84.3f), 12),     // 140y, menu, multi-level city
        new(5, "New Gridania",              132, 2, 1001276, "Maisenta",    new(14.0f, 0.1f, 2.1f), 18),        // 34y, menu (18 handlers)
    ];

    private const float InteractRange = 3.5f;       // vanilla NPC talk range is ~4y at the origin; NPCs are thin, 3.5 leaves margin
    private const int TeleportTimeoutMs = 45_000;
    private const int NavReadyTimeoutMs = 60_000;
    private const int WalkTimeoutMs = 120_000;
    private const int StuckMs = 8_000;
    private const int InteractTimeoutMs = 15_000;

    private enum State { Idle, Teleport, ZoneChange, NavReady, Dismount, Walk, Nudge, Interact, Menu, Done }

    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IObjectTable _objects;
    private readonly ITargetManager _targets;
    private readonly IGameGui _gameGui;
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;

    // vnavmesh (names per vnavmesh/IPCProvider.cs @ master, prefix "vnavmesh.")
    private readonly ICallGateSubscriber<bool> _navIsReady;
    private readonly ICallGateSubscriber<float> _navBuildProgress;
    private readonly ICallGateSubscriber<Vector3, bool, bool> _pathfindAndMoveTo;
    private readonly ICallGateSubscriber<bool> _simpleMoveInProgress;
    private readonly ICallGateSubscriber<bool> _pathIsRunning;
    private readonly ICallGateSubscriber<object> _pathStop;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> _pathMoveTo;
    // Lifestream (names per Lifestream/IPC/IPCProvider.cs)
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
    private readonly List<string> _jank = [];
    public readonly List<string> Results = [];

    public VendorSpike(IDalamudPluginInterface pi, IFramework framework, IClientState clientState, ICondition condition,
        IObjectTable objects, ITargetManager targets, IGameGui gameGui, IChatGui chat, IPluginLog log)
    {
        _framework = framework; _clientState = clientState; _condition = condition; _objects = objects;
        _targets = targets; _gameGui = gameGui; _chat = chat; _log = log;
        _navIsReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        _navBuildProgress = pi.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        _pathfindAndMoveTo = pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        _simpleMoveInProgress = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        _pathIsRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _pathStop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        _pathMoveTo = pi.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        _lsTeleport = pi.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
        _lsIsBusy = pi.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        _framework.Update += Tick;
    }

    public void Dispose()
    {
        _framework.Update -= Tick;
        TryStopPath();
    }

    public void Command(string arg)
    {
        arg = arg.Trim().ToLowerInvariant();
        switch (arg)
        {
            case "" or "list":
                Say("vendors: " + string.Join(" · ", Vendors.Select(v => $"{v.N}={v.NpcName} ({v.Zone}, {v.Handlers}h)")));
                Say("usage: /lcraft spike <1-5|all|stop|results>");
                return;
            case "stop":
                Abort("stopped by user");
                _queue.Clear();
                return;
            case "results":
                if (Results.Count == 0) Say("no results yet");
                foreach (var r in Results) Say(r);
                return;
            case "all":
                foreach (var v in Vendors) _queue.Enqueue(v);
                break;
            default:
                if (!int.TryParse(arg, out var n) || n < 1 || n > Vendors.Length) { Say("unknown arg; try list"); return; }
                _queue.Enqueue(Vendors[n - 1]);
                break;
        }
        if (_state == State.Idle) StartNext();
        else Say($"queued; {_queue.Count} pending");
    }

    private void StartNext()
    {
        if (!_queue.TryDequeue(out var v)) { _state = State.Idle; _cur = null; return; }
        _cur = v;
        _tpMs = _navMs = _walkMs = _interactMs = 0;
        _sawBetweenAreas = _sawRunning = _nudged = false;
        _finalDist = float.NaN; _menu = "-"; _jank.Clear();
        _total.Restart();
        Say($"{v.N}/5 {v.NpcName} @ {v.Zone}: teleporting (aetheryte {v.AetheryteId})…");
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
            Finish(false, $"exception in {_state}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Step()
    {
        var v = _cur!;
        var player = _objects.LocalPlayer;
        switch (_state)
        {
            case State.Teleport:
                if (_clientState.TerritoryType == v.Territory && !_condition[ConditionFlag.BetweenAreas])
                {
                    // Already here. Note it — a same-zone teleport is a different path than the question asks.
                    _jank.Add("already in zone (no teleport)");
                    _tpMs = 0;
                    Enter(State.NavReady);
                    return;
                }
                if (SafeCall(() => _lsIsBusy.InvokeFunc(), "Lifestream.IsBusy")) { if (_phase.ElapsedMilliseconds > 10_000) { Finish(false, "Lifestream busy for 10 s"); } return; }
                var ok = SafeCall(() => _lsTeleport.InvokeFunc(v.AetheryteId, 0), "Lifestream.Teleport");
                if (!ok) { Finish(false, "Lifestream.Teleport returned false (not attuned? in combat? IPC missing?)"); return; }
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
                    Finish(false, $"teleport did not complete in {TeleportTimeoutMs / 1000} s (sawBetweenAreas={_sawBetweenAreas}, territory={_clientState.TerritoryType})");
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
                    Finish(false, $"vnavmesh not ready after {NavReadyTimeoutMs / 1000} s (BuildProgress={SafeCall(() => _navBuildProgress.InvokeFunc(), "BuildProgress"):F2})");
                return;

            case State.Dismount:
                if (!_condition[ConditionFlag.Mounted]) { _jank.Add("had to dismount"); Enter(State.Walk); return; }
                if (_phase.ElapsedMilliseconds % 1000 < 20) ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23); // 23 = Dismount (GeneralAction sheet)
                if (_phase.ElapsedMilliseconds > 10_000) Finish(false, "could not dismount");
                return;

            case State.Walk:
                if (player is null) return;
                if (!_sawRunning && _phase.ElapsedMilliseconds < 50)
                {
                    var issued = SafeCall(() => _pathfindAndMoveTo.InvokeFunc(v.Pos, false), "vnavmesh.SimpleMove.PathfindAndMoveTo");
                    if (!issued) { Finish(false, "PathfindAndMoveTo returned false (a pathfind is already in progress)"); return; }
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
                    Finish(false, $"walk ended {dist:F1}y from NPC (need ≤{InteractRange}y)");
                    return;
                }
                if (_sawRunning && _phase.ElapsedMilliseconds - _lastMoveAt > StuckMs)
                {
                    TryStopPath();
                    _walkMs = _phase.ElapsedMilliseconds; _finalDist = dist;
                    Finish(false, $"stuck for {StuckMs / 1000} s at {dist:F1}y from NPC");
                    return;
                }
                if (!_sawRunning && !solving && _phase.ElapsedMilliseconds > 10_000)
                {
                    Finish(false, "vnavmesh never started moving (no path? check /xllog for 'Failed to find path')");
                    return;
                }
                if (_phase.ElapsedMilliseconds > WalkTimeoutMs) { TryStopPath(); Finish(false, $"walk exceeded {WalkTimeoutMs / 1000} s"); }
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
                    else Finish(false, $"nudge ended {d2:F1}y from NPC (need ≤{InteractRange}y)");
                }
                return;

            case State.Interact:
            {
                var npc = FindNpc(v.NpcId);
                if (npc is null)
                {
                    if (_phase.ElapsedMilliseconds > 5_000) Finish(false, $"NPC BaseId {v.NpcId} not in object table within 5 s (wrong id, despawned, or different instance/level)");
                    return;
                }
                if (ShopOpen()) { _interactMs = _phase.ElapsedMilliseconds; Finish(true, null); return; }
                if (MenuOpen(out var menuName, out var menuAddon))
                {
                    _menu = menuName;
                    _jank.Add($"menu {menuName} first");
                    SelectFirstEntry(menuAddon);
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
                    Finish(false, $"no Shop/menu within {InteractTimeoutMs / 1000} s of interacting (dist {Vector3.Distance(player!.Position, npc.Position):F1}y)");
                return;
            }

            case State.Menu:
                if (ShopOpen()) { _interactMs = _phase.ElapsedMilliseconds; Finish(true, null); return; }
                if (MenuOpen(out _, out var again) && _phase.ElapsedMilliseconds > 1500 && _phase.ElapsedMilliseconds % 1500 < 20) SelectFirstEntry(again);
                if (_phase.ElapsedMilliseconds > InteractTimeoutMs)
                    Finish(false, $"selected entry 0 of {_menu} but no Shop within {InteractTimeoutMs / 1000} s (entry 0 is not the shop for this NPC — needs entry text matching, not index)");
                return;
        }
    }

    private void Finish(bool ok, string? why)
    {
        var v = _cur!;
        _total.Stop();
        TryStopPath();
        var line = $"[spike] {v.N}/5 {v.NpcName} ({v.Zone}): {(ok ? "OK" : "FAIL")}"
            + $" · tp {Sec(_tpMs)} · nav +{Sec(_navMs)} · walk {Sec(_walkMs)} (final {(float.IsNaN(_finalDist) ? "?" : $"{_finalDist:F1}y")}, nudged={(_nudged ? "yes" : "no")})"
            + $" · interact {Sec(_interactMs)} · menu={_menu} · shop={(ok ? "OPEN" : "no")} · total {Sec(_total.ElapsedMilliseconds)}"
            + (_jank.Count > 0 ? $" · jank: {string.Join("; ", _jank)}" : " · jank: none")
            + (why is null ? "" : $" · why: {why}");
        Results.Add(line);
        _log.Information("{Line}", line);
        Say(line);
        _state = State.Idle;
        _cur = null;
        // Pause between vendors so Joey can read the line / the shop closes before the next teleport.
        if (_queue.Count > 0) _framework.RunOnTick(StartNext, TimeSpan.FromSeconds(4));
    }

    private void Abort(string why)
    {
        if (_cur is not null) Finish(false, why);
    }

    private static string Sec(long ms) => $"{ms / 1000.0:F1}s";

    private IGameObject? FindNpc(uint baseId)
    {
        foreach (var o in _objects)
            if (o.ObjectKind == ObjectKind.EventNpc && o.BaseId == baseId && o.IsTargetable) return o;
        return null;
    }

    private AtkUnitBase* Addon(string name)
    {
        // API 15: IGameGui.GetAddonByName returns a pointer wrapper; .Address is 0 when absent (same as ECommons TryGetAddonByName).
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
            if (a != null && a->IsVisible) { name = n; addon = a; return true; }
        }
        name = "-"; addon = null; return false;
    }

    // Same wire format ECommons AddonMaster.SelectString/SelectIconString.Entry.Select uses: Callback.Fire(addon, true, index).
    private static void SelectFirstEntry(AtkUnitBase* addon) => ECommons.Automation.Callback.Fire(addon, true, 0);

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
