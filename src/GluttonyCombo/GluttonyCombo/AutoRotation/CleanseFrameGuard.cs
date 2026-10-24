using System;
using System.Threading;

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Frame-boundary guards around the automatic-cleanse scan (<c>CleanseParty</c>), so no
///     cleanse-path state can turn a rotation tick into a hang or a per-frame spin. This gate
///     exists because the testing build that first carried <see cref="CleanseGate"/> froze the
///     game's main thread inside the plugin's own managed code a few seconds after a territory
///     change, in a state no capture could pin to one looping method; the change had also been
///     hot-reloaded into a running session minutes earlier. The gate therefore fences the two
///     windows the evidence names - right after a plugin (re)load and right after a territory
///     change - makes the scan non-reentrant, and turns any scan that costs more than a frame
///     budget into a caller-logged managed stack plus a short scanning stand-down instead of an
///     every-frame repeat. Dependency-free on purpose (pure clock state plus constants) so the
///     rotation harnesses compile it unchanged, like <see cref="CleanseGate"/>.
/// </summary>
internal static class CleanseFrameGuard
{
    /// <summary>
    ///     Seconds with no cleanse scanning right after a plugin (re)load (or harness reset):
    ///     covers the assembly-swap window and first-ticks table warm-up.
    /// </summary>
    public const double InitFenceSeconds = 10.0;

    /// <summary>
    ///     Seconds with no cleanse scanning after a territory change: the captured freeze hit
    ///     four seconds into one, while the object table and status lists were still settling.
    /// </summary>
    public const double TransitionFenceSeconds = 6.0;

    /// <summary>One cleanse scan may cost at most this many milliseconds; more is an overrun.</summary>
    public const double FrameBudgetMs = 25.0;

    /// <summary>Seconds the scan stands down after a budget overrun.</summary>
    public const double OverrunCooldownSeconds = 2.0;

    private static int _inTick;
    private static bool _initSeen;
    private static DateTime _fenceUntilUtc = DateTime.MinValue;
    private static DateTime _suspendUntilUtc = DateTime.MinValue;
    private static int _overruns;

    /// <summary>Record a plugin (re)load or harness reset: the init fence re-arms.</summary>
    public static void NoteInit(DateTime nowUtc) =>
        ArmFence(nowUtc, InitFenceSeconds);

    /// <summary>Record a territory change (wired from the per-tick territory cache).</summary>
    public static void NoteTerritoryChanged(uint territoryId, DateTime nowUtc) =>
        ArmFence(nowUtc, TransitionFenceSeconds);

    /// <summary>
    ///     Whether a cleanse scan may start this tick. The first call after load (or reset)
    ///     arms the init fence, so the very first ticks of a freshly (re)loaded plugin never
    ///     scan. Returns false while any fence or overrun stand-down is active and never
    ///     allows a second scan to start inside a running one.
    /// </summary>
    public static bool EnterTick(DateTime nowUtc)
    {
        if (!_initSeen)
        {
            _initSeen = true;
            NoteInit(nowUtc);
        }

        if (Interlocked.CompareExchange(ref _inTick, 1, 0) != 0)
            return false; // the scan never re-enters itself

        if (nowUtc < _fenceUntilUtc || nowUtc < _suspendUntilUtc)
        {
            ExitTick();
            return false;
        }

        return true;
    }

    /// <summary>Release the tick slot taken by a successful <see cref="EnterTick"/>.</summary>
    public static void ExitTick() =>
        Interlocked.Exchange(ref _inTick, 0);

    /// <summary>Whether one scan cost more than the frame budget.</summary>
    public static bool IsOverBudget(double elapsedMs) =>
        elapsedMs > FrameBudgetMs;

    /// <summary>
    ///     Record a budget overrun: the scan stands down for <see cref="OverrunCooldownSeconds"/>
    ///     so a slow scan cannot repeat every frame. The caller logs elapsed time and the
    ///     managed stack of the overrun point - that log line is the evidence a hang leaves.
    /// </summary>
    public static void NoteOverrun(double elapsedMs, DateTime nowUtc)
    {
        _overruns++;
        _suspendUntilUtc = nowUtc.AddSeconds(OverrunCooldownSeconds);
    }

    /// <summary>Budget overruns seen since load (diagnostics).</summary>
    public static int OverrunCount => _overruns;

    /// <summary>Harness/test reset: fresh state, init fence re-armed, exactly like a plugin reload.</summary>
    public static void Reset()
    {
        _inTick = 0;
        _initSeen = false;
        _overruns = 0;
        _fenceUntilUtc = DateTime.MinValue;
        _suspendUntilUtc = DateTime.MinValue;
    }

    private static void ArmFence(DateTime nowUtc, double seconds)
    {
        var until = nowUtc.AddSeconds(seconds);
        // Later of the existing fence and the new one, so a territory change landing inside
        // the init window can never shorten it.
        if (until > _fenceUntilUtc)
            _fenceUntilUtc = until;
    }
}
