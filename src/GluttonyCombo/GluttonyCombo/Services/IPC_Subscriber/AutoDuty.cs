#region

using ECommons.EzIpcManager;
using ECommons.Reflection;
using System;

#endregion

namespace GluttonyCombo.Services.IPC_Subscriber;

/// <summary>
/// Subscriber for AutoDuty IPC. Used to detect when AutoDuty is in control
/// of the player (especially during mechanics like Pyretic where it untargs),
/// so Gluttony can yield and stop re-targeting.
/// </summary>
public class AutoDuty : ReusableIPC
{
    private const string PluginInternalName = "AutoDuty";

    [EzIPC] private readonly Func<bool> isRunning;
    [EzIPC] private readonly Func<bool> isPaused;
    [EzIPC] private readonly Func<string> currentState;

    /// <summary>Whether AutoDuty is currently running a route/dungeon/etc.</summary>
    public bool IsRunning => SafeInvoke(isRunning, false);

    /// <summary>Whether AutoDuty has paused itself (e.g. for mechanics, combat, or user pause).</summary>
    public bool IsPaused => SafeInvoke(isPaused, false);

    /// <summary>
    /// Current state string: "None", "Navigating", "Stopped", "Paused",
    /// "Dead", "Revived", "Action", etc.
    /// </summary>
    public string CurrentStateStr => SafeInvoke(currentState, "None");

    /// <summary>
    /// True when AutoDuty is actively controlling the player and has paused
    /// combat autorotation (e.g. during Pyretic, Untarget mechanics, etc.).
    /// When true, Gluttony should skip its own autorotation to avoid fighting
    /// AutoDuty over target selection.
    /// </summary>
    public bool ShouldYield => IsRunning && IsPaused;

    public AutoDuty() : base(PluginInternalName, new Version(1, 0, 0, 0), reflectionNotIPC: false)
    {
    }

    private static T SafeInvoke<T>(Func<T> func, T fallback)
    {
        try { return func(); }
        catch { return fallback; }
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
