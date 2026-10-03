namespace GluttonyCombo.Data;

/// <summary>
///     The pure half of the stalled-GCD collector (<c>SG|</c>): one line while the Beastmaster rotation chose an
///     attack, the global cooldown sits ready, and nothing has been sent for more than
///     <see cref="StallAfterSeconds"/> - naming WHY (no target, own cast bar, animation lock, queued action,
///     out of range, or none of those). Written by <see cref="BeastmasterTelemetry"/> only on a Crucible board,
///     so an idle stretch can be graded by reason from <c>plugin_log_lines</c> instead of guessed from the gap.
///     No Dalamud/game types; asserted by <c>tests/GluttonyCombo.TelemetryHarness</c>.
/// </summary>
internal static class CrucibleStallFormat
{
    /// <summary> Fixed, greppable line prefix: <c>message LIKE 'SG|%'</c>. </summary>
    public const string Prefix = "SG|";

    /// <summary> Hard budget for one emitted line. </summary>
    public const int MaxLineLength = 160;

    /// <summary> Same rate floor as <c>RF|</c>: a continuing stall re-logs at most every 2 s. </summary>
    public const int MinIntervalMs = BeastmasterTelemetryFormat.MinIntervalMs;

    /// <summary> Nothing fired for this long (with the GCD ready) before the first line is written. </summary>
    public const float StallAfterSeconds = 1f;

    /// <summary> Why nothing fired, most-discriminating first; the live sampler picks the first that holds. </summary>
    internal enum Reason : byte
    {
        None = 0,
        /// <summary> No hostile target. </summary>
        NoTarget,
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

    internal readonly record struct Snapshot(
        uint ActionId,
        Reason Why,
        float EdgeDistance,
        uint TargetNameId,
        float StallSeconds);

    internal static (uint, Reason, int, uint) KeyOf(in Snapshot s) =>
        (s.ActionId, s.Why, Math.Clamp((int)(s.EdgeDistance * 10f), -10, 990), s.TargetNameId);

    /// <summary> A new stall (action, reason, distance or target changed) emits at once; the same one re-logs at most every <see cref="MinIntervalMs"/>. </summary>
    internal static bool ShouldEmit(ref GateState state, long nowMs, in Snapshot snapshot)
    {
        return false; // STUB (failing-first): the real gate lands with the implementation commit
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
    ///     <c>SG|unixms|act=id|r=reason|d=edge|t=nameId|s=seconds</c>. <c>d</c> is the hitbox-edge distance to the
    ///     target (empty without one), <c>s</c> how long nothing has been sent. <c>t</c> is 0 without a target.
    /// </summary>
    internal static string BuildLine(long unixMs, in Snapshot s)
    {
        return Prefix; // STUB (failing-first): the real line lands with the implementation commit
    }
}
