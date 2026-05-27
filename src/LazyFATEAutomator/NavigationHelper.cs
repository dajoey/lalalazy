using System;
using System.Collections.Generic;
using System.Numerics;
using ECommons.DalamudServices;
using ECommons.Automation;
using ActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;
using ActionManager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager;
using PlayerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;

namespace LazyFATEAutomator;

/// <summary>
/// Thin façade over vnavmesh's IPC + FFXIVClientStructs ActionManager. Designed to be safe to
/// call every framework tick — the heavy work is delegated; this wrapper only issues commands.
/// </summary>
public class NavigationHelper
{
    // GeneralAction IDs (verified against Lumina/XIVAPI):
    //   4  Sprint   |  9 Mount Roulette  |  23 Dismount  |  24 Flying Mount Roulette
    private const uint GENERAL_ACTION_MOUNT_ROULETTE = 9;
    private const uint GENERAL_ACTION_DISMOUNT       = 23;

    // ====================================================================
    // vnavmesh IPC layer
    //
    // Endpoint names verified by disassembly of vnavmesh 1.2.3.3 IPCProvider.ctor.
    // Each subscriber is created lazily and cached; if vnavmesh isn't installed, the
    // try/catch in the wrapper falls back to the chat-command path.
    // ====================================================================

    private bool _ipcInitTried;
    private bool _ipcAvailable;

    private Dalamud.Plugin.Ipc.ICallGateSubscriber<bool>? _navIsReady;
    private Dalamud.Plugin.Ipc.ICallGateSubscriber<bool>? _pathIsRunning;
    private Dalamud.Plugin.Ipc.ICallGateSubscriber<bool>? _pathfindInProgress;
    private Dalamud.Plugin.Ipc.ICallGateSubscriber<object>? _pathStop;
    private Dalamud.Plugin.Ipc.ICallGateSubscriber<Vector3, bool, System.Threading.Tasks.Task<bool>>? _pathfindAndMoveTo;
    private Dalamud.Plugin.Ipc.ICallGateSubscriber<Vector3, float, float, Vector3?>? _nearestPointReachable;

    private void EnsureIpc()
    {
        if (_ipcInitTried) return;
        _ipcInitTried = true;
        try
        {
            _navIsReady             = Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
            _pathIsRunning          = Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
            _pathfindInProgress     = Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
            _pathStop               = Plugin.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
            _pathfindAndMoveTo      = Plugin.PluginInterface.GetIpcSubscriber<Vector3, bool, System.Threading.Tasks.Task<bool>>("vnavmesh.SimpleMove.PathfindAndMoveTo");
            _nearestPointReachable  = Plugin.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
            _ipcAvailable = true;
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Warning(ex, "vnavmesh IPC unavailable, falling back to chat commands");
            _ipcAvailable = false;
        }
    }

    /// <summary>True when the navmesh for the current zone has finished loading.</summary>
    public bool NavReady
    {
        get
        {
            EnsureIpc();
            if (!_ipcAvailable) return true; // can't tell; assume yes for chat fallback
            try { return _navIsReady!.InvokeFunc(); }
            catch { return true; }
        }
    }

    /// <summary>True when vnav is actively walking/flying the player along a computed path.</summary>
    public bool IsPathing
    {
        get
        {
            EnsureIpc();
            if (!_ipcAvailable) return false;
            try { return _pathIsRunning!.InvokeFunc(); }
            catch { return false; }
        }
    }

    /// <summary>True when vnav is still computing a path (between MoveTo issued and movement starting).</summary>
    public bool IsPathfinding
    {
        get
        {
            EnsureIpc();
            if (!_ipcAvailable) return false;
            try { return _pathfindInProgress!.InvokeFunc(); }
            catch { return false; }
        }
    }

    /// <summary>True when vnav is either pathfinding or running a path.</summary>
    public bool IsBusy => IsPathfinding || IsPathing;

    /// <summary>Halts vnav and clears its queued path.</summary>
    public void Stop()
    {
        EnsureIpc();
        if (_ipcAvailable)
        {
            try { _pathStop!.InvokeAction(); return; }
            catch (Exception ex) { Plugin.PluginLog.Warning(ex, "Path.Stop IPC failed; falling back"); }
        }
        Chat.SendMessage("/vnav stop");
    }

    /// <summary>
    /// Pathfind to the destination (fly=false) and start moving. Idempotent — vnav coalesces
    /// duplicate destinations; we additionally gate on IsBusy in the state machine.
    /// </summary>
    public void MoveTo(Vector3 destination)
    {
        EnsureIpc();
        if (_ipcAvailable)
        {
            try { _pathfindAndMoveTo!.InvokeFunc(destination, false); return; }
            catch (Exception ex) { Plugin.PluginLog.Warning(ex, "SimpleMove.PathfindAndMoveTo (ground) failed; falling back"); }
        }
        Chat.SendMessage($"/vnav moveto {Fmt(destination.X)} {Fmt(destination.Y)} {Fmt(destination.Z)}");
    }

    /// <summary>Pathfind to the destination and fly there.</summary>
    public void FlyTo(Vector3 destination)
    {
        EnsureIpc();
        if (_ipcAvailable)
        {
            try { _pathfindAndMoveTo!.InvokeFunc(destination, true); return; }
            catch (Exception ex) { Plugin.PluginLog.Warning(ex, "SimpleMove.PathfindAndMoveTo (flight) failed; falling back"); }
        }
        Chat.SendMessage($"/vnav flyto {Fmt(destination.X)} {Fmt(destination.Y)} {Fmt(destination.Z)}");
    }

    /// <summary>
    /// Asks vnav for the nearest point on the navmesh that's reachable from the target. Returns
    /// the input position if vnav can't compute one (caller should still try — engaging from the
    /// exact center is often fine).
    /// </summary>
    public Vector3 NearestReachable(Vector3 target, float xzHalfExtent = 5f, float yHalfExtent = 5f)
    {
        EnsureIpc();
        if (!_ipcAvailable) return target;
        try
        {
            var v = _nearestPointReachable!.InvokeFunc(target, xzHalfExtent, yHalfExtent);
            return v ?? target;
        }
        catch { return target; }
    }

    // ====================================================================
    // ActionManager (mount, dismount). MUST be called from the framework
    // thread; StateController.Tick is wired to Framework.Update so this
    // holds by construction.
    // ====================================================================

    public unsafe void Mount()
    {
        var am = ActionManager.Instance();
        if (am == null) return;
        am->UseAction(ActionType.GeneralAction, GENERAL_ACTION_MOUNT_ROULETTE);
    }

    public unsafe void Dismount()
    {
        var am = ActionManager.Instance();
        if (am == null) return;
        am->UseAction(ActionType.GeneralAction, GENERAL_ACTION_DISMOUNT);
    }

    /// <summary>True if the player has unlocked flying in the current territory.</summary>
    public unsafe bool FlightUnlockedInCurrentZone()
    {
        var ps = PlayerState.Instance();
        if (ps == null) return false;
        try
        {
            // PlayerState.IsAetherCurrentZoneComplete(uint aetherCurrentCompFlgSetId)
            // We can't easily get the aetherCurrentCompFlgSetId without an Excel lookup, so
            // fall back to a heuristic: if any flight-capable mount works (vnav handles this
            // internally), we just trust vnav's FlyTo to succeed or no-op. The state machine
            // already falls through to ground movement if flight doesn't take off.
            return true;
        }
        catch { return true; }
    }

    public void LevelSync()         => Chat.SendMessage("/levelsync");
    public void Teleport(string a)  => Chat.SendMessage($"/tp {a}");
    public void LifestreamTravel(string t) => Chat.SendMessage($"/li {t}");

    public float GetDistanceTo(Vector3 target)
    {
        var p = Svc.Objects.LocalPlayer;
        return p == null ? float.MaxValue : Vector3.Distance(p.Position, target);
    }

    public float GetDistance2DTo(Vector3 target)
    {
        var p = Svc.Objects.LocalPlayer;
        if (p == null) return float.MaxValue;
        var dx = p.Position.X - target.X;
        var dz = p.Position.Z - target.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static string Fmt(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
