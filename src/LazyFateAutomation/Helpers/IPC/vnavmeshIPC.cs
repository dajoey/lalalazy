using ECommons.EzIpcManager;
using System.Threading;
using System.Threading.Tasks;

namespace LazyFateAutomation.Helpers.IPC;

#nullable disable
[Ipc(Ipc.Navmesh)]
public class NavmeshIPC : BaseIPC {
    public override string Name => "vnavmesh";
    public override string Repo => Veyn;
    
    public NavmeshIPC() => EzIPC.Init(this, Name);

    [EzIPC("Nav.IsReady")] private readonly Func<bool> IsReadyFunc;
    public bool IsReady => IsReadyFunc != null && IsReadyFunc();

    [EzIPC("Nav.BuildProgress")] private readonly Func<float> BuildProgressFunc;
    public float BuildProgress => BuildProgressFunc != null ? BuildProgressFunc() : -1f;

    [EzIPC("Nav.%m")] public readonly Func<bool> Reload;
    [EzIPC("Nav.%m")] public readonly Func<bool> Rebuild;
    /// <summary> Vector3 from, Vector3 to, bool fly </summary>
    [EzIPC("Nav.%m")] public readonly Func<Vector3, Vector3, bool, List<Vector3>> Pathfind;
    /// <summary> Vector3 from, Vector3 to, bool fly, float range </summary>
    [EzIPC("Nav.%m")] public readonly Func<Vector3, Vector3, bool, float, List<Vector3>> PathfindWithTolerance;
    /// <summary> Vector3 from, Vector3 to, bool fly, CancellationToken cancel </summary>
    [EzIPC("Nav.%m")] public readonly Func<Vector3, Vector3, bool, CancellationToken, List<Vector3>> PathfindCancelable;
    [EzIPC("Nav.%m")] public readonly Action PathfindCancelAll;
    [EzIPC("Nav.%m")] public readonly Func<bool> NavPathfindInProgress;
    [EzIPC("Nav.%m")] public readonly Func<int> PathfindNumQueued;
    [EzIPC("Nav.%m")] public readonly Func<bool> IsAutoLoad;
    [EzIPC("Nav.%m")] public readonly Action<bool> SetAutoLoad;
    /// <summary> Vector3 startingPos, string filename, float pixelSize </summary>
    [EzIPC("Nav.%m")] public readonly Func<Vector3, string, float, bool> BuildBitmap;
    /// <summary> Vector3 startingPos, string filename, float pixelSize, Vector3 minBounds, Vector3 maxBounds </summary>
    [EzIPC("Nav.%m")] public readonly Func<Vector3, string, float, Vector3, Vector3, bool> BuildBitmapBounded;

    [EzIPC("Query.Mesh.NearestPoint")] private readonly Func<Vector3, float, float, Vector3?> NearestPointFunc;
    public Vector3? NearestPoint(Vector3 position, float halfExtentXZ = 5, float halfExtentY = 5) => NearestPointFunc != null ? NearestPointFunc(position, halfExtentXZ, halfExtentY) : null;

    [EzIPC("Query.Mesh.NearestPointReachable")] private readonly Func<Vector3, float, float, Vector3?> NearestPointReachableFunc;
    public Vector3? NearestPointReachable(Vector3 position, float halfExtentXZ = 5, float halfExtentY = 5) => NearestPointReachableFunc != null ? NearestPointReachableFunc(position, halfExtentXZ, halfExtentY) : null;

    [EzIPC("Query.Mesh.PointOnFloor")] private readonly Func<Vector3, bool, float, Vector3?> PointOnFloorFunc;
    public Vector3? PointOnFloor(Vector3 position, bool allowUnlandable = false, float halfExtentXZ = 5) => PointOnFloorFunc != null ? PointOnFloorFunc(position, allowUnlandable, halfExtentXZ) : null;

    [EzIPC("Query.Mesh.FlagToPoint")] private readonly Func<Vector3?> FlagToPointFunc;
    public Vector3? FlagToPoint() => FlagToPointFunc != null ? FlagToPointFunc() : null;

    /// <summary> List<Vector3> waypoints, bool fly </summary>
    [EzIPC("Path.%m")] public readonly Action<List<Vector3>, bool> MoveTo;
    [EzIPC("Path.%m")] public readonly Action Stop;
    [EzIPC("Path.%m")] public readonly Func<bool> IsRunning;
    [EzIPC("Path.%m")] public readonly Func<int> NumWaypoints;
    [EzIPC("Path.%m")] public readonly Func<List<Vector3>> ListWaypoints;
    [EzIPC("Path.%m")] public readonly Func<bool> GetMovementAllowed;
    [EzIPC("Path.%m")] public readonly Action<bool> SetMovementAllowed;
    [EzIPC("Path.%m")] public readonly Func<bool> GetAlignCamera;
    [EzIPC("Path.%m")] public readonly Action<bool> SetAlignCamera;

    [EzIPC("Path.GetTolerance")] private readonly Func<float> GetToleranceFunc;
    public float GetTolerance() => GetToleranceFunc != null ? GetToleranceFunc() : 0f;
    [EzIPC("Path.%m")] public readonly Action<float> SetTolerance;

    [EzIPC("SimpleMove.PathfindAndMoveTo")] private readonly Func<Vector3, bool, bool> PathfindAndMoveToFunc;
    public bool PathfindAndMoveTo(Vector3 dest, bool fly = false) => PathfindAndMoveToFunc != null && PathfindAndMoveToFunc(dest, fly);

    /// <summary> Vector3 dest, bool fly, float range </summary>
    [EzIPC("SimpleMove.%m")] public readonly Func<Vector3, bool, float, bool> PathfindAndMoveCloseTo;
    [EzIPC("SimpleMove.%m")] public readonly Func<bool> PathfindInProgress;

    public bool PathfindingInProgress => PathfindInProgress != null && PathfindInProgress();

    [EzIPC("Window.%m")] public readonly Func<bool> IsOpen;
    [EzIPC("Window.%m")] public readonly Action<bool> SetOpen;

    [EzIPC("DTR.%m")] public readonly Func<bool> IsShown;
    [EzIPC("DTR.%m")] public readonly Action<bool> SetShown;
}
