// Fork (1.0.4.283): pure decision cores for the DPS target thrash fix. Deliberately free of Dalamud
// types so the offline harness (tests/GluttonyCombo.DpsTargetStabilityHarness) asserts the exact
// semantics that ship.

using System;

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     What the plugin-wide current target (<c>OverrideTarget</c>, read by every combo and by the CR|
///     telemetry) holds once a rotation tick has finished.
/// </summary>
internal static class DpsTargetExposure
{
    /// <summary>
    ///     <paramref name="chosen"/> is the enemy the targeting mode picked this tick, <paramref name="pressedAt"/> the
    ///     in-range enemy the out-of-range fallback aimed this tick's single press at (null when no fallback ran).
    ///     The fallback redirects ONE press; it is not a new pick.
    /// </summary>
    internal static T? AfterTick<T>(T? chosen, T? pressedAt) where T : class =>
        pressedAt ?? chosen; // placeholder keeps the shipped behavior (1.0.4.258): the fallback enemy stays the current target
}

/// <summary>
///     Cadence floor and stickiness for one stream of DPS target picks.
/// </summary>
internal sealed class DpsTargetStability
{
    internal const long DefaultMinPickIntervalMs = 1000;

    internal enum Why
    {
        /// <summary> No pick this tick (nothing valid), or nothing changed. </summary>
        None,
        /// <summary> The first pick, or the first after the previous one went away: never delayed. </summary>
        First,
        /// <summary> The held pick is gone (dead, untargetable, no longer reachable): replaced at once. </summary>
        HeldInvalid,
        /// <summary> A mechanic target (interrupt, dispel carrier, boss-mod target) takes over at once. </summary>
        Forced,
        /// <summary> The held pick has been held for the minimum interval and a different one is now preferred. </summary>
        Repick,
        /// <summary> A different pick is preferred but the minimum interval has not passed: the held pick stays. </summary>
        Held,
    }

    internal readonly record struct Result(ulong Id, Why Why, bool Changed, long HeldMs, int Suppressed);

    private ulong _last;

    public DpsTargetStability(long minIntervalMs = DefaultMinPickIntervalMs)
    {
    }

    /// <summary> Placeholder keeps the shipped behavior: the fresh pick is used as it comes, every tick. </summary>
    internal Result Resolve(ulong fresh, long nowMs, Func<ulong, bool> heldStillValid, bool forced = false)
    {
        var changed = fresh != 0 && fresh != _last;
        var why = fresh == 0 ? Why.None : _last == 0 ? Why.First : changed ? Why.Repick : Why.None;
        if (fresh != 0) _last = fresh;
        return new(fresh, why, changed, 0, 0);
    }

    /// <summary> <c>TS|unixms|why=..|path=..|from=nameId|to=nameId|held=ms|sup=n</c> </summary>
    internal static string BuildLine(long unixMs, Why why, string path, uint fromNameId, uint toNameId, long heldMs, int suppressed) =>
        $"TS|{unixMs}|why={why}|path={path}|from={fromNameId}|to={toNameId}|held={heldMs}|sup={suppressed}";
}
