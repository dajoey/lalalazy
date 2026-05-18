#region

using ECommons;
using ECommons.EzIpcManager;
using ECommons.Logging;
using System;

#endregion

namespace GluttonyCombo.Services.IPC_Subscriber;

/// <summary>
/// Subscriber for AutoDuty IPC. Used to detect when AutoDuty is in control
/// of the player (running a duty, navigating, or paused for mechanics such
/// as Pyretic / untarget) so Gluttony can yield autorotation and target
/// acquisition instead of fighting AutoDuty over target selection.
/// </summary>
/// <remarks>
/// AutoDuty's actual public IPC surface is PascalCase:
/// <c>AutoDuty.IsStopped</c> and <c>AutoDuty.IsNavigating</c>. The previous
/// version of this file declared lowercase fields (<c>isRunning</c>,
/// <c>isPaused</c>, <c>currentState</c>) which EzIPC bound verbatim to
/// method names that don't exist, so every call threw and the yield logic
/// was effectively dead.
/// </remarks>
internal sealed class AutoDuty()
    : ReusableIPC("AutoDuty", new Version(0, 0, 0, 0))
{
    /// <summary>True if AutoDuty is fully stopped (not running any duty).</summary>
    public bool IsStopped
    {
        get
        {
            if (!IsEnabled) return true; // not installed -> treat as stopped
            try { return _isStopped(); }
            catch (Exception e)
            {
                PluginLog.Verbose(
                    $"[ConflictingPlugins] [{PluginName}] " +
                    $"`IsStopped` failed: {e.ToStringFull()}");
                return true;
            }
        }
    }

    /// <summary>True if AutoDuty is currently navigating the player.</summary>
    public bool IsNavigating
    {
        get
        {
            if (!IsEnabled) return false;
            try { return _isNavigating(); }
            catch (Exception e)
            {
                PluginLog.Verbose(
                    $"[ConflictingPlugins] [{PluginName}] " +
                    $"`IsNavigating` failed: {e.ToStringFull()}");
                return false;
            }
        }
    }

    /// <summary>
    /// True when AutoDuty is actively controlling the player. While true,
    /// Gluttony skips its own autorotation and target acquisition so it
    /// doesn't fight AutoDuty's target/mechanic handling (Pyretic untargets,
    /// LoS movement, between-pull repositioning, etc.).
    /// </summary>
    /// <remarks>
    /// We treat any state other than <c>IsStopped</c> as "AutoDuty has the
    /// wheel." This will over-yield slightly during pure-combat phases (when
    /// AutoDuty is happy to let Gluttony attack), but that's the lesser evil
    /// compared to the original targeting-loop bug where Gluttony would
    /// re-target during Pyretic.
    /// </remarks>
    public bool ShouldYield => IsEnabled && !IsStopped;

#pragma warning disable CS0649, CS8618 // EzIPC assigns these via reflection at construction
    [EzIPC("AutoDuty.IsStopped", false)]
    private readonly Func<bool> _isStopped = null!;

    [EzIPC("AutoDuty.IsNavigating", false)]
    private readonly Func<bool> _isNavigating = null!;
#pragma warning restore CS8618, CS0649
}
