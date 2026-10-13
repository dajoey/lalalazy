using System;
using System.Globalization;
using System.Text;

namespace GluttonyCombo.Data;

/// <summary>
///     The pure half of the stalled-GCD collector (<c>SG|</c>): one line while the Beastmaster rotation chose an
///     attack, the global cooldown sits ready, and nothing has been sent for more than
///     <see cref="StallAfterSeconds"/> - naming WHY (no target, own cast bar, animation lock, queued action,
///     out of range, an unselectable held target, or none of those). Written by <see cref="BeastmasterTelemetry"/> only on a Crucible board,
///     so an idle stretch can be graded by reason from <c>plugin_log_lines</c> instead of guessed from the gap.
///     No Dalamud/game types; asserted by <c>tests/GluttonyCombo.TelemetryHarness</c>.
/// </summary>
internal static class CrucibleStallFormat
{
    /// <summary> Fixed, greppable line prefix: <c>message LIKE 'SG|%'</c>. </summary>
    public const string Prefix = "SG|";

    /// <summary> Hard budget for one emitted line. </summary>
    public const int MaxLineLength = 160;

    /// <summary> Same rate floor as <c>RF|</c>: a continuing stall re-logs at most every 2 s, not every change gate tick. </summary>
    public const int MinIntervalMs = 2000;

    /// <summary> Nothing fired for this long (with the GCD ready) before the first line is written. </summary>
    public const float StallAfterSeconds = 1f;

    /// <summary> Why nothing fired, most-discriminating first; the live sampler picks the first that holds. </summary>
    internal enum Reason : byte
    {
        None = 0,
        /// <summary> No hostile target. </summary>
        NoTarget,
        /// <summary> A held target the game refuses as untargetable or dead (IsTargetable false / IsDead true; invulnerability is not checked - no such predicate exists in this API set): nothing can be sent at it. </summary>
        Unselectable,
        /// <summary> The character's own cast bar is rolling. </summary>
        Cast,
        /// <summary> The character is animation-locked. </summary>
        Lock,
        /// <summary> An action is already queued. </summary>
        Queue,
        /// <summary> The chosen action cannot reach the target. </summary>
        Range,
        /// <summary> None of the above: the rotation chose, everything looked usable, nothing went out. </summary>
        Unknown,
    }

    private static readonly string[] ReasonWords = ["none", "notarget", "unselectable", "cast", "lock", "queue", "range", "unknown"];

    internal readonly record struct Snapshot(
        uint ActionId,
        Reason Why,
        float EdgeDistance,
        uint TargetNameId,
        float StallSeconds,
        string? DashHold = null);

    internal static (uint, Reason, int, uint) KeyOf(in Snapshot s) =>
        (s.ActionId, s.Why, Math.Clamp((int)(s.EdgeDistance * 10f), -10, 990), s.TargetNameId);

    /// <summary> A new stall (action, reason, distance or target changed) emits at once; the same one re-logs at most every <see cref="MinIntervalMs"/>. </summary>
    internal static bool ShouldEmit(ref GateState state, long nowMs, in Snapshot snapshot)
    {
        var key = KeyOf(snapshot);

        if (state.HasLast && state.LastKey == key && (!state.HasEmitted || nowMs - state.LastEmitMs < MinIntervalMs))
            return false;

        state.HasLast = true;
        state.LastKey = key;
        state.HasEmitted = true;
        state.LastEmitMs = nowMs;
        return true;
    }

    internal struct GateState
    {
        public bool HasLast;
        public (uint, Reason, int, uint) LastKey;
        public bool HasEmitted;
        public long LastEmitMs;

        public void Reset() => this = default;
    }

    /// <summary>
    ///     Seconds since the local player last used an action, clamped into the line budget. The caller passes
    ///     <c>ActionWatching.TimeSinceLastAction</c> - a same-kind span. Never subtract across clock kinds:
    ///     <c>DateTime.UtcNow</c> minus the local-time <c>TimeLastActionUsed</c> is always the UTC offset and
    ///     saturates every line at the clamp (found live 2026-10-03: every SG| line said s=999 while damage flowed).
    /// </summary>
    internal static float SinceFireSeconds(TimeSpan sinceLastActionUsed) =>
        MathF.Min(999f, MathF.Max(0f, (float)sinceLastActionUsed.TotalSeconds));

    /// <summary>
    ///     The live sampler's holding test: in combat, the global cooldown ready, the rotation chose a
    ///     hostile-targeted action, and nothing has fired for over <see cref="StallAfterSeconds"/> since the GCD came ready
    ///     (<paramref name="idleSeconds"/> from <see cref="IdleSeconds"/>).
    /// </summary>
    internal static bool IsStalled(bool inCombat, bool gcdReady, bool choseHostileAction, float idleSeconds) =>
        inCombat && gcdReady && choseHostileAction && idleSeconds > StallAfterSeconds;

    /// <summary>
    ///     How long nothing has been sent since the global cooldown came ready: the later of the last send and the GCD-ready moment.
    ///     Measured from the last send alone (to 2026-10-13), every GCD sent without a weave read 2.0-2.2 s "stalled" once the GCD
    ///     entered its queue window: 46,722 SG| lines on 10-06 against about 1,300 s of real idle.
    /// </summary>
    internal static float IdleSeconds(float sinceFireSeconds, float sinceGcdReadySeconds) => MathF.Min(sinceFireSeconds, sinceGcdReadySeconds);

    /// <summary> Seconds since the global cooldown came ready (its remaining recast reached 0); 0 while it rolls. One per sampler. </summary>
    internal struct GcdReadyClock
    {
        public bool Ready;
        public long ReadyAtMs;

        public float Update(long nowMs, float remainingGcdSeconds)
        {
            if (remainingGcdSeconds > 0f)
            {
                Ready = false;
                return 0f;
            }
            if (!Ready)
            {
                Ready = true;
                ReadyAtMs = nowMs;
            }
            return MathF.Max(0f, (nowMs - ReadyAtMs) / 1000f);
        }
    }

    /// <summary>
    ///     <c>SG|unixms|act=id|r=reason|d=edge|t=nameId|s=seconds</c>. <c>d</c> is the hitbox-edge distance to the
    ///     target (empty without one), <c>s</c> how long nothing has been sent since the GCD came ready. <c>t</c> is 0 without a target. On
        ///     range lines, <c>|dh=</c> names why the rotation's own gap-close (Shield Charge) did not fire.
    /// </summary>
    internal static string BuildLine(long unixMs, in Snapshot s)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(MaxLineLength + 32);

        sb.Append(Prefix).Append(unixMs.ToString(inv))
          .Append("|act=").Append(s.ActionId.ToString(inv))
          .Append("|r=").Append(ReasonWords[(int)s.Why])
          .Append("|d=");
        if (s.EdgeDistance >= 0f)
            sb.Append(Math.Min(99f, s.EdgeDistance).ToString("0.0", inv));
        sb.Append("|t=").Append(s.TargetNameId.ToString(inv))
          .Append("|s=").Append(Math.Min(999f, Math.Max(0f, s.StallSeconds)).ToString("0.0", inv));
        if (s.Why == Reason.Range && !string.IsNullOrEmpty(s.DashHold))
            sb.Append("|dh=").Append(s.DashHold);

        if (sb.Length > MaxLineLength)
            sb.Length = MaxLineLength;
        return sb.ToString();
    }
}
