using ECommons;
using ECommons.EzIpcManager;
using ECommons.Reflection;
using System;

#nullable disable

namespace GluttonyCombo.Services.IPC_Subscriber;

/// <summary>
///     Optional reader of BossMod Reborn's hint IPC (fork, t_356159a8, v1.0.4.191).
///     Mirrors RotationSolverReborn's <c>BMRInfo_IPCSubscriber</c> shape: the
///     endpoints live under the <c>BossMod.</c> prefix (both upstream BossMod
///     and BMR register there). Every member may be null when neither plugin is
///     installed - SmartMover treats that as the normal, fully-functional
///     derived-zones-only mode.
/// </summary>
/// <remarks>
///     Verified against the INSTALLED BMR 7.5.6.4 on omasky (UTF-16 strings in
///     BossModReborn.dll). Upstream awgil BossMod does NOT expose the Hints.*
///     set - the wrapper degrades to nulls there, which is correct.
/// </remarks>
internal static class BossModHintsIPC
{
    private static readonly EzIPCDisposalToken[] _disposalTokens =
        EzIPC.Init(typeof(BossModHintsIPC), "BossMod", SafeWrapper.IPCException);

    internal static bool IsEnabled => InstalledVersion >= _validVersion;
    internal static Version InstalledVersion =>
        DalamudReflector.TryGetDalamudPlugin("BossModReborn", out var p, false, true)
            ? p.GetType().Assembly.GetName().Version
            : new Version(0, 0, 0, 0);
    private static readonly Version _validVersion = new(7, 2, 5, 90);

#pragma warning disable CS0649, CS8618
    /// <summary> True while BMR's AI controller is actively steering the player. The only
    ///     BMR input SmartMover consumes; absence of BMR (or of this endpoint) is the normal,
    ///     fully-functional derived-zones-only mode. </summary>
    [EzIPC("AI.IsNavigating", true)]
    internal static readonly Func<bool>? IsNavigating;
#pragma warning restore CS0649, CS8618

    internal static void Dispose()
    {
        foreach (var t in _disposalTokens)
        {
            try { t.Dispose(); }
            catch { }
        }
    }
}
