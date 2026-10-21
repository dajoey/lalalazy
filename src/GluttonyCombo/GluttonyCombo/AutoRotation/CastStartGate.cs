// Fork (1.0.4.316): pure decision core for "movement out of danger always wins"
// (task tasks-20261021-ffxiv-forced-slidecast-roots-player-01). Deliberately free
// of Dalamud types so the offline harness (tests/GluttonyCombo.CastStartGateHarness)
// asserts the exact semantics that ship; AutoRotationController consults it at the
// spell cast-start sites.

using System;

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Decision core for the shared cast-start movement gate. With the
///     auto-rotation driving a job, a spell that has a cast bar roots the player
///     for its full duration, so a cast that begins the instant movement stops
///     forces the player to ride every dodge out to the cast's slide-cast
///     window. While the player is moving a spell must not begin (shipped
///     behavior); with a stillness window configured, the spell additionally
///     waits that many seconds after movement last stopped, so a brief stop to
///     dodge never glues a fresh cast bar onto the player. Instants (no cast
///     time) and orbwalker-driven movement are never blocked; once the window
///     has passed the rotation casts exactly as before, and slide-casting at the
///     tail of a cast keeps working. Stillness 0 (the default) preserves the
///     shipped behavior byte for byte.
/// </summary>
internal static class CastStartGate
{
    /// <summary>
    ///     Whether a spell with <paramref name="castTime"/> seconds of cast time
    ///     must not begin right now. <paramref name="timeMoving"/> is the time
    ///     the player has been moving (zero when stationary), <paramref
    ///     name="timeStill"/> the time since movement last stopped (zero while
    ///     moving or before the first stillness tick), <paramref
    ///     name="stillnessSeconds"/> the configured stillness window, and
    ///     <paramref name="orbwalking"/> whether the orbwalker integration owns
    ///     movement (it manages the slide-cast window itself).
    /// </summary>
    internal static bool Blocks(float castTime, bool orbwalking, TimeSpan timeMoving, TimeSpan timeStill, float stillnessSeconds)
    {
        // Instants are never held: they never root the player, so there is
        // nothing for the gate to protect.
        if (castTime <= 0f)
            return false;

        // The orbwalker integration owns movement and the slide-cast window
        // itself; holding spells here would only fight it (shipped behavior).
        if (orbwalking)
            return false;

        // Movement always wins: while the player is moving no spell with a cast
        // bar begins (shipped behavior).
        if (timeMoving > TimeSpan.Zero)
            return true;

        // The stillness window: after movement stops, hold the spell until the
        // player has been stationary long enough that a fresh cast bar cannot
        // glue onto a dodge. 0 (the default) means no hold - the shipped
        // behavior where the spell begins the instant movement stops. The clamp
        // keeps a negative value from becoming a permanent hold.
        return timeStill < TimeSpan.FromSeconds(Math.Max(0f, stillnessSeconds));
    }
}
