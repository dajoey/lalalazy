using ECommons;
using ECommons.EzIpcManager;
using ECommons.Reflection;
using System;
using System.Numerics;
#nullable disable

namespace GluttonyCombo.Services.IPC_Subscriber;

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
#pragma warning restore CS8618, CS0649

    internal static bool IsReady => IsReadyFunc != null && IsReadyFunc();
    internal static bool PathfindingInProgress => PathfindInProgressFunc != null && PathfindInProgressFunc();
    internal static bool CanPathfind => IsEnabled && IsReady;

    internal static bool PathfindAndMoveTo(Vector3 dest, bool fly = false)
    {
        return PathfindAndMoveToFunc != null && PathfindAndMoveToFunc(dest, fly);
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
