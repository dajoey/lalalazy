using System;

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     One owner per raidwide for the timed AoE regen (WHM Medica II/Medica III, AST Aspected
///     Helios/Helios Conjunction). Two independent casters share each raidwide preset but used
///     to share nothing else: the autorotation timed lock (<see cref="AutoRotationController"/>,
///     arms at cast-bar detect and completes ~1.2s after the bar resolves) and the job combo's
///     raidwide feature (RaidwideMedica / RaidwideAspectedHelios, firing inside the last seconds
///     of the bar), plus the generic AoE-heal priority list re-resolving the same actions. The
///     2026-10-19 report: the same AoE HoT heal went out twice for one raidwide, and a cast that
///     landed early was followed by the same HoT heal again while the first HoT was still ticking.
///
///     Whichever caster commits first owns the raidwide:
///     - while the timed lock is ARMED it holds the cast bar and the better fire time; the combo
///       feature and the generic heal path stand down so the well-timed cast is the only one;
///     - after any timed-regen cast is COMMITTED, its own HoT suppresses every other caster (and
///       a re-fire of the same lock) until the HoT would have ended - an early cast is never
///       followed by the same HoT AoE heal.
///     Pure clock state, no game reads, so the rotation harnesses compile this file unchanged.
/// </summary>
internal static class TimedRegenGate
{
    private static DateTime _armedUntil = DateTime.MinValue;
    private static DateTime _ownedUntil = DateTime.MinValue;

    /// <summary>The autorot timed lock scheduled the cast for the raidwide it just detected.</summary>
    internal static void Arm(TimeSpan window) => _armedUntil = DateTime.UtcNow + window;

    /// <summary>The lock gave the raidwide back (bar vanished before the fire time, or the party was covered elsewhere).</summary>
    internal static void Disarm() => _armedUntil = DateTime.MinValue;

    /// <summary>
    ///     A timed-regen cast was committed (the press went out - lock or generic heal path). Its
    ///     HoT now owns this raidwide for <paramref name="lifetime"/> (cast remainder + HoT duration).
    /// </summary>
    internal static void MarkCast(TimeSpan lifetime) => _ownedUntil = DateTime.UtcNow + lifetime;

    /// <summary>The timed lock holds the scheduling of the raidwide currently detected.</summary>
    internal static bool TimedByLock => DateTime.UtcNow < _armedUntil;

    /// <summary>A committed timed-regen cast's own HoT is still live.</summary>
    internal static bool LiveFromCast => DateTime.UtcNow < _ownedUntil;

    /// <summary>Someone owns this raidwide's regen: every other caster of the same AoE HoT heal stands down.</summary>
    internal static bool Owned => TimedByLock || LiveFromCast;

    /// <summary>Clears all state. Self-expiring in the plugin (windows above); the harnesses call it between cases.</summary>
    internal static void Reset()
    {
        _armedUntil = DateTime.MinValue;
        _ownedUntil = DateTime.MinValue;
    }
}
