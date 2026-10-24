using System;
using System.Collections.Frozen;

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     How much a dispellable status on an ally is worth spending the global cooldown on.
///     Tiers derived from the CanDispel rows of the shipped Status sheet (244 rows dumped
///     from the installed game's sqpack): High = movement/action locks (a Heavied or Stunned
///     player cannot dodge), Trivial = damage-taken amplifiers and resist downs that fall off
///     on their own, Moderate = the default (damage over time and everything unclassified).
/// </summary>
internal enum CleanseSeverity
{
    Trivial,
    Moderate,
    High,
}

/// <summary>
///     One shared verdict for every automatic cleanse (Esuna and BLU Exuviation), so the
///     heal-combo branches and the auto-rotation's CleanseParty can never be two casters for
///     one debuff: severity x danger x one-in-flight. Dependency-free on purpose (pure clock
///     state plus static tables) so the rotation harnesses compile it unchanged, like
///     <see cref="TimedRegenGate"/>.
/// </summary>
internal static class CleanseGate
{
    /// <summary>
    ///     Seconds a freshly sent cleanse blocks further cleanses of the same target: covers
    ///     the cast itself and the local status-list lag after a successful removal (the
    ///     double-cast window).
    /// </summary>
    public const double InFlightWindowSeconds = 3.0;

    /// <summary>Own HP at or below which Moderate debuffs are left to fall off: heal first.</summary>
    public const float ModerateRequiresPlayerHpPct = 40f;

    /// <summary>Own HP at or below which only doom is worth the global cooldown.</summary>
    public const float HighRequiresPlayerHpPct = 25f;

    /// <summary>
    ///     Movement/action locks: cleansing them IS the dodge. Stun, Sleep, Amnesia,
    ///     Pacification, Silence, Slow, Bind, Heavy, Paralysis (the sheet's current and legacy
    ///     rows of those names).
    /// </summary>
    private static readonly FrozenSet<uint> HighStatuses = new uint[]
    {
        2, 3, 5, 6, 7, 9, 10, 13, 14, 17, 193, 482, 561, 564, 1107, 1344, 1345, 1346,
    }.ToFrozenSet();

    /// <summary>Damage-taken amplifiers and resist downs that fall off on their own; never worth the GCD.</summary>
    private static readonly FrozenSet<uint> TrivialStatuses = new uint[]
    {
        64, 98, 563, 638, 821, 893, 1402, 1654, 1655, 1717, 1721, 2121, 2122, 2123, 2340, 2441, 2558, 2559,
    }.ToFrozenSet();

    // One plugin-wide slot: the cleanse this plugin sent most recently. A player can only
    // have one cleanse resolving at a time, so one slot is the whole in-flight state.
    private static ulong _sentTargetId;
    private static DateTime _sentAtUtc;

    /// <summary>Record that a cleanse was just sent at this target (called at every send site).</summary>
    public static void NoteSent(ulong targetId, DateTime now)
    {
        _sentTargetId = targetId;
        _sentAtUtc = now;
    }

    /// <summary>Whether a cleanse at this target is already resolving (or its removal is still propagating).</summary>
    public static bool IsInFlight(ulong targetId, DateTime now) =>
        _sentTargetId == targetId && (now - _sentAtUtc).TotalSeconds < InFlightWindowSeconds;

    /// <summary>Harness/test reset.</summary>
    public static void Reset()
    {
        _sentTargetId = 0;
        _sentAtUtc = default;
    }

    /// <summary>The severity tier of one dispellable status row.</summary>
    public static CleanseSeverity SeverityOf(uint statusId) =>
        HighStatuses.Contains(statusId) ? CleanseSeverity.High
        : TrivialStatuses.Contains(statusId) ? CleanseSeverity.Trivial
        : CleanseSeverity.Moderate;

    /// <summary>
    ///     The full worth verdict for cleansing <paramref name="targetId"/> right now.
    ///     <paramref name="statusIds"/> are the dispellable statuses present on the candidate,
    ///     <paramref name="hasDoom"/> says the candidate carries a cleansable doom (the
    ///     existing StatusCache set), <paramref name="candidateHpPct"/> guards the dead, and
    ///     <paramref name="playerHpPct"/> is the danger term: the healer's own HP.
    /// </summary>
    public static bool MayCleanse(ulong targetId, ReadOnlySpan<uint> statusIds, bool hasDoom,
        float candidateHpPct, float playerHpPct, DateTime now)
    {
        if (candidateHpPct <= 0f)
            return false; // never cleanse the dead
        if (IsInFlight(targetId, now))
            return false; // one cleanse per debuff: a cast is resolving or its removal is still propagating
        if (hasDoom)
            return true; // doom kills outright at zero: always worth the cast

        var best = CleanseSeverity.Trivial;
        foreach (var id in statusIds)
        {
            var severity = SeverityOf(id);
            if (severity > best)
                best = severity;
            if (best == CleanseSeverity.High)
                break;
        }

        return best switch
        {
            CleanseSeverity.Trivial => false, // falls off on its own; attacking continues
            CleanseSeverity.High => playerHpPct > HighRequiresPlayerHpPct,
            _ => playerHpPct >= ModerateRequiresPlayerHpPct,
        };
    }
}
