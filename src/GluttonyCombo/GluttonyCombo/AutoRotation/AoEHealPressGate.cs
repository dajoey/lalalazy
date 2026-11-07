// Fork (1.0.4.335): pure decision core for the auto-rotation's AoE-heal press cadence.
// Deliberately free of Dalamud and ECommons types (the clock arrives as a tick count) so
// the offline harnesses (tests/GluttonyCombo.RotationHarness.AST / .WHM) assert the exact
// semantics that ship; AutoRotationHelper.ExecuteAoE's heal branch is the live caller.
//
// Reported live (2026-11-07, PotD floors 51+): once the AoE trigger armed, the press path
// re-issued the same heal every framework tick — casts that movement cancelled were
// re-pressed ~0.3s later (four Helios cast STARTS in 1.3s, then six more, then nine more),
// and the heal that had just left the gate did not stand the path down while it landed and
// party HP propagated. The one-button tail always names a heal, so the press path itself
// must own "not back to back": after an AoE heal press goes out, the next one waits out
// that cast's remaining flight plus a land grace for the server to apply it and the
// client to see it. Real damage is never starved — the window is one cast long, and a
// party that is STILL under the count gate after it heals again immediately.

using System;

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     One AoE heal press at a time in the auto-rotation: after a press, further AoE heal
///     presses stand down until the cast could land and party HP could propagate. Pure
///     clock state (tick counts in, verdicts out); the plugin passes
///     <see cref="Environment.TickCount64"/>, the harnesses pass synthetic ticks.
/// </summary>
internal static class AoEHealPressGate
{
    /// <summary>
    ///     After the cast's own flight time, this many milliseconds more for the server to
    ///     apply the heal and the client's party HP to catch up. Inside this window a
    ///     re-press would be reading the pre-heal state and firing on stale data.
    /// </summary>
    internal const int LandGraceMs = 1000;

    private static long _standsDownUntilTick;

    /// <summary>An AoE heal press went out (cast started or queued); its land window begins.</summary>
    internal static void NotePress(int castTimeMs, long nowTickMs) =>
        _standsDownUntilTick = nowTickMs + Math.Max(0, castTimeMs) + LandGraceMs;

    /// <summary>Whether AoE heal presses stand down right now (a press is still in flight).</summary>
    internal static bool StandsDown(long nowTickMs) => nowTickMs < _standsDownUntilTick;

    /// <summary>Clears the window. Self-expiring in the plugin; the harnesses call it between cases.</summary>
    internal static void Reset() => _standsDownUntilTick = 0;
}
