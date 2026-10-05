// Fork (1.0.4.283): pure decision cores for the DPS target thrash fix. Deliberately free of Dalamud
// types so the offline harness (tests/GluttonyCombo.DpsTargetStabilityHarness) asserts the exact
// semantics that ship.

using System;

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     What the plugin-wide current target (<c>OverrideTarget</c>, read by every combo and by the CR|
///     telemetry) holds once a rotation tick has finished.
/// </summary>
/// <remarks>
///     Root cause of the 2026-10-03 .. 10-05 thrash (ffxivdb CR| / RF| lines): the out-of-range fallback
///     (1.0.4.258) aimed one press at an in-range enemy by writing it into the current target and never
///     put the mode's pick back, and its verdict flaps per action, so the current target alternated
///     pick, fallback enemy, pick. 437 of 530 A, B, A flips in CR| had an RF| line naming exactly that pair.
/// </remarks>
internal static class DpsTargetExposure
{
    /// <summary>
    ///     <paramref name="chosen"/> is the enemy the targeting mode picked this tick, <paramref name="pressedAt"/> the
    ///     in-range enemy the out-of-range fallback aimed this tick's single press at (null when no fallback ran).
    ///     The fallback redirects ONE press; it is not a new pick, so the pick stays the current target.
    /// </summary>
    internal static T? AfterTick<T>(T? chosen, T? pressedAt) where T : class =>
        chosen ?? pressedAt;
}

/// <summary>
///     Cadence floor for one stream of DPS target picks: a different enemy is taken at most once per
///     <see cref="DefaultMinPickIntervalMs"/>, except the first pick, a pick whose held enemy is gone, and a
///     mechanic target, which switch at once.
/// </summary>
/// <remarks>
///     This is the backstop under the root fix (<see cref="DpsTargetExposure"/>): every pick source
///     (mode dropdown, Crucible kill order, boss-mod target, fallback enemy) is a stateless function of live
///     inputs (distance, HP, reach, line of sight) that can flip from one frame to the next, so the stream
///     is bounded here whatever flips it. Not thread-safe: the framework thread only.
/// </remarks>
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

    /// <param name="Id"> The enemy to use this tick (0 = none). </param>
    /// <param name="Changed"> The pick changed this tick. </param>
    /// <param name="HeldMs"> How long the previous pick had been held (on a change), or the current one (otherwise). </param>
    /// <param name="Suppressed"> Different picks turned down since the last change. </param>
    internal readonly record struct Result(ulong Id, Why Why, bool Changed, long HeldMs, int Suppressed);

    private readonly long _minIntervalMs;
    private ulong _held;
    private long _heldAtMs;
    private int _suppressed;

    /// <param name="minIntervalMs"> 0 switches the floor off (every tick follows the fresh pick). </param>
    public DpsTargetStability(long minIntervalMs = DefaultMinPickIntervalMs)
    {
        _minIntervalMs = minIntervalMs;
    }

    /// <summary>
    ///     The pick to use this tick. <paramref name="fresh"/> is the enemy the targeting rules prefer right now (0 = none),
    ///     <paramref name="heldStillValid"/> says whether the held enemy can still be attacked at all (alive, targetable,
    ///     reachable): reach flicker is NOT invalidity, only the floor decides about it. <paramref name="forced"/> marks a
    ///     mechanic target.
    /// </summary>
    internal Result Resolve(ulong fresh, long nowMs, Func<ulong, bool> heldStillValid, bool forced = false)
    {
        if (fresh == 0)
            return new(0, Why.None, false, 0, 0); // no pick this tick: no target, the hold is kept

        if (_held == 0)
            return Commit(fresh, Why.First, nowMs);

        if (fresh == _held)
            return new(_held, Why.None, false, nowMs - _heldAtMs, _suppressed);

        if (!heldStillValid(_held))
            return Commit(fresh, Why.HeldInvalid, nowMs);

        if (forced)
            return Commit(fresh, Why.Forced, nowMs);

        if (nowMs - _heldAtMs >= _minIntervalMs)
            return Commit(fresh, Why.Repick, nowMs);

        _suppressed++;
        return new(_held, Why.Held, false, nowMs - _heldAtMs, _suppressed);
    }

    private Result Commit(ulong fresh, Why why, long nowMs)
    {
        var heldMs = _held == 0 ? 0 : nowMs - _heldAtMs;
        var suppressed = _suppressed;
        _held = fresh;
        _heldAtMs = nowMs;
        _suppressed = 0;
        return new(fresh, why, true, heldMs, suppressed);
    }

    /// <summary> <c>TS|unixms|why=..|path=..|from=nameId|to=nameId|held=ms|sup=n</c>: one line per change of the DPS pick. </summary>
    internal static string BuildLine(long unixMs, Why why, string path, uint fromNameId, uint toNameId, long heldMs, int suppressed) =>
        $"TS|{unixMs}|why={why}|path={path}|from={fromNameId}|to={toNameId}|held={heldMs}|sup={suppressed}";
}
