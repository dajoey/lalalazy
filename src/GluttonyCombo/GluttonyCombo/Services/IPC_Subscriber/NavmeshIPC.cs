using ECommons;
using ECommons.EzIpcManager;
using ECommons.Reflection;
using System;
using System.Collections.Generic;
using System.Numerics;
#nullable disable

namespace GluttonyCombo.Services.IPC_Subscriber;

/// <summary>
///     vnavmesh IPC surface extended for SmartMover (fork, t_356159a8, v1.0.4.191):
///     adds Nav.PathfindAvoid + Path.MoveTo (danger-aware routing) on top of the
///     existing SimpleMove set. All members may be null when vnavmesh is absent.
/// </summary>
internal static class NavmeshIPC
{
    private static EzIPCDisposalToken[] _disposalTokens = EzIPC.Init(typeof(NavmeshIPC), "vnavmesh", SafeWrapper.IPCException);

    internal static bool IsEnabled => InstalledVersion >= _validVersion;
    internal static Version InstalledVersion => DalamudReflector.TryGetDalamudPlugin("vnavmesh", out var dalamudPlugin, false, true) ? dalamudPlugin.GetType().Assembly.GetName().Version : new Version(0, 0, 0, 0);
    private static Version _validVersion = new(0, 0, 0, 0);

#pragma warning disable CS0649, CS8618
    [EzIPC("Nav.IsReady")] public static readonly Func<bool> IsReadyFunc;
    [EzIPC("SimpleMove.PathfindAndMoveTo")] public static readonly Func<Vector3, bool, bool> PathfindAndMoveToFunc;
    [EzIPC("Path.Stop")] public static readonly Action Stop;
    [EzIPC("Path.IsRunning")] public static readonly Func<bool> IsRunningFunc;
    [EzIPC("SimpleMove.PathfindInProgress")] public static readonly Func<bool> PathfindInProgressFunc;

    // --- SmartMover additions (v1.0.4.191) ---
    /// <summary> Danger-aware path query: returns waypoints, does NOT move. </summary>
    [EzIPC("Nav.PathfindAvoid", true)]
    public static readonly Func<Vector3, Vector3, bool, Vector3, float, List<Vector3>> PathfindAvoidFunc;

    /// <summary> Follow an explicit waypoint list. </summary>
    [EzIPC("Path.MoveTo", true)]
    public static readonly Action<List<Vector3>, bool> MoveToFunc;

    // --- SmartMover arena-bounds addition (v1.0.4.196, BMR-parity gap 1) ---
    /// <summary>
    ///     Real mesh-walkability query: null result = the point is not on the
    ///     navmesh (unreachable/off-arena). Args are (point, allowUnlandable,
    ///     halfExtentXZ) per upstream vnavmesh IPCProvider.cs. Used to derive
    ///     TRUE arena bounds for dodge/engage candidates instead of relying
    ///     solely on the distance-from-target heuristic.
    /// </summary>
    [EzIPC("Query.Mesh.PointOnFloor", true)]
    public static readonly Func<Vector3, bool, float, Vector3?> PointOnFloorFunc;
#pragma warning restore CS8618, CS0649

    internal static bool IsReady => IsReadyFunc != null && IsReadyFunc();
    internal static bool PathfindingInProgress => PathfindInProgressFunc != null && PathfindInProgressFunc();
    internal static bool CanPathfind => IsEnabled && IsReady;

    internal static bool PathfindAndMoveTo(Vector3 dest, bool fly = false)
    {
        return PathfindAndMoveToFunc != null && PathfindAndMoveToFunc(dest, fly);
    }

    /// <summary>
    ///     Whether <paramref name="p"/> sits on the navmesh (a legal standing
    ///     point). FAIL-OPEN on any exception or a missing/not-ready query so
    ///     an IPC hiccup degrades to the old distance-heuristic-only behaviour
    ///     instead of freezing the mover in place.
    /// </summary>
    internal static bool IsPointWalkable(Vector3 p)
    {
        if (PointOnFloorFunc is null)
            return true;
        try
        {
            return PointOnFloorFunc(p, false, 2f) is not null;
        }
        catch
        {
            return true;
        }
    }

    internal static void Dispose()
    {
        foreach (var token in _disposalTokens)
        {
            try
            {
                token.Dispose();
            }
            catch (Exception ex)
            {
                ex.Log();
            }
        }
    }
}
