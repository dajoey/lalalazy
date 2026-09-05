using System;
using System.Globalization;
using System.Text;

namespace AutoPotion;

/// <summary>
///     The pure half of AutoPotion's decision telemetry tap (v0.2.4.0): the line
///     format and the near-miss emit gate, with no Dalamud/game types, so
///     <c>tests/AutoPotion.TelemetryHarness</c> can assert the exact shape of what
///     ships. <see cref="PotionTelemetry"/> is the live half that reads game state
///     and calls in here.
/// </summary>
/// <remarks>
///     Mirrors <c>GluttonyCombo.Data.ComboTelemetryFormat</c> (prefix <c>CT|</c>)
///     on purpose: one query shape reads every lalalazy decision tap.
/// </remarks>
internal static class PotionTelemetryFormat
{
    /// <summary> Fixed, greppable line prefix: <c>message LIKE 'PT|%'</c> in ffxivdb. </summary>
    public const string Prefix = "PT|";

    /// <summary> Hard budget for one emitted line. </summary>
    public const int MaxLineLength = 200;

    /// <summary>
    ///     Floor on the spacing between two near-miss lines. Deliberately a const and
    ///     NOT a config knob: Tick() runs on the framework loop (throttled to one
    ///     evaluation per 150 ms), so an ungated near-miss emit would produce ~6.7
    ///     lines/second and drown the plugin log the way PvPSolver's MajorUpdater
    ///     currently does. Fires are never rate-limited.
    /// </summary>
    public const int NearMissMinIntervalMs = 5000;

    // ---- event codes (field 3) -------------------------------------------------
    /// <summary> An HP potion was used. </summary>
    public const char EvHpFired = 'h';
    /// <summary> An MP potion (Ether) was used. </summary>
    public const char EvMpFired = 'm';
    /// <summary> A deep dungeon regen potion was used. </summary>
    public const char EvRegenFired = 'r';
    /// <summary> Nothing fired; the gated near-miss line. </summary>
    public const char EvNearMiss = 'n';

    // ---- reason codes (last-but-one field) -------------------------------------
    // SHORT and STABLE. ffxivdb queries key on these strings; never rename or
    // repurpose one, only add. The human-readable prose stays in
    // PotionService._lastSkipReason for /autopotion debug, where drift is harmless.

    /// <summary> A potion fired. Carried on every h/m/r line so the field is never empty. </summary>
    public const string ReasonOk = "ok";

    /// <summary> HP threshold crossed; a usable potion existed but every one would have overshot (the waste guard declined). </summary>
    public const string ReasonHpOver = "hpover";
    /// <summary> HP threshold crossed; candidates exist in the bags but all are cooldown/duty/status blocked. </summary>
    public const string ReasonHpBlocked = "hpblocked";
    /// <summary> HP threshold crossed; no HP potion in the bags at all. </summary>
    public const string ReasonHpNoStock = "hpnostock";
    /// <summary> HP potion chosen but <c>UseAction</c> refused it. </summary>
    public const string ReasonHpUseFail = "hpusefail";

    /// <summary> MP threshold crossed; candidates blocked. </summary>
    public const string ReasonMpBlocked = "mpblocked";
    /// <summary> MP threshold crossed; no Ether in the bags. </summary>
    public const string ReasonMpNoStock = "mpnostock";
    /// <summary> MP potion chosen but <c>UseAction</c> refused it. </summary>
    public const string ReasonMpUseFail = "mpusefail";

    /// <summary> Regen threshold crossed in a deep dungeon but Rehabilitation (648) is still up. </summary>
    public const string ReasonRgRehab = "rgrehab";
    /// <summary> Regen threshold crossed; candidates blocked. </summary>
    public const string ReasonRgBlocked = "rgblocked";
    /// <summary> Regen threshold crossed; no deep dungeon medicine in the bags. </summary>
    public const string ReasonRgNoStock = "rgnostock";
    /// <summary> Regen potion chosen but <c>UseAction</c> refused it. </summary>
    public const string ReasonRgUseFail = "rgusefail";

    /// <summary>
    ///     Sentinel written into <c>mpPct</c> for a job with no MP pool at all
    ///     (warriors, most gatherers), so a query can tell "0% MP" apart from
    ///     "this job has no MP".
    /// </summary>
    public const float NoMpPool = -1f;

    /// <summary>
    ///     Mutable state for the near-miss gate: the reason currently remembered, and
    ///     when we last actually wrote a line. Lives here rather than in the live half
    ///     so the harness can drive it offline.
    /// </summary>
    internal sealed class NearMissGate
    {
        /// <summary> A reason is currently remembered, i.e. the dedupe key below is valid. </summary>
        public bool HasLast;
        public uint LastJob;
        public string LastReason = string.Empty;

        /// <summary> We have written at least one line, i.e. the rate limit is armed. </summary>
        public bool HasEmitted;
        public long LastEmitUnixMs;

        /// <summary> Full reset (telemetry toggled on): the next near-miss emits unconditionally. </summary>
        public void Reset()
        {
            HasLast = false;
            LastJob = 0;
            LastReason = string.Empty;
            HasEmitted = false;
            LastEmitUnixMs = 0;
        }

        /// <summary>
        ///     No threshold was crossed this tick (or a potion fired and resolved it).
        ///     Forget the remembered reason so a LATER dip back into the same state is
        ///     reported again — an unrepeatable one-line-per-session near-miss would be
        ///     useless for lining up against <c>deaths</c> — but keep the rate-limit
        ///     clock armed so flapping around the threshold still cannot spam.
        /// </summary>
        public void NoteResolved()
        {
            HasLast = false;
            LastJob = 0;
            LastReason = string.Empty;
        }
    }

    /// <summary>
    ///     The near-miss gate: emit only when the (job, reason-code) pair CHANGES,
    ///     and never more often than <see cref="NearMissMinIntervalMs"/>.
    /// </summary>
    /// <remarks>
    ///     The gate deliberately does NOT record a state it refused to emit. A state
    ///     suppressed purely by the rate limit therefore stays "new" and is emitted
    ///     on the first tick past the window — otherwise a boring transition two
    ///     seconds earlier would permanently swallow an interesting one, and the
    ///     interesting state would never appear in ffxivdb at all.
    /// </remarks>
    internal static bool ShouldEmitNearMiss(NearMissGate gate, uint job, string reason, long nowUnixMs)
    {
        if (gate.HasLast && gate.LastJob == job &&
            string.Equals(gate.LastReason, reason, StringComparison.Ordinal))
            return false;

        if (gate.HasEmitted && nowUnixMs - gate.LastEmitUnixMs < NearMissMinIntervalMs)
            return false;

        gate.HasLast = true;
        gate.LastJob = job;
        gate.LastReason = reason;
        gate.HasEmitted = true;
        gate.LastEmitUnixMs = nowUnixMs;
        return true;
    }

    /// <summary>
    ///     Builds one telemetry line:
    ///     <c>PT|unixms|job|ev|itemId|hpPct|hpThr|mpPct|mpThr|inCombat|inDuty|deepDungeon|reason|item</c>
    ///     (14 pipe-separated fields).
    /// </summary>
    /// <param name="job">ClassJob RowId — the same numeric id ffxivdb <c>player_samples.job</c> carries, so the join needs no name table.</param>
    /// <param name="ev">One of <see cref="EvHpFired"/> / <see cref="EvMpFired"/> / <see cref="EvRegenFired"/> / <see cref="EvNearMiss"/>.</param>
    /// <param name="itemId">Item id of the potion used; 0 on a near-miss.</param>
    /// <param name="mpPct"><see cref="NoMpPool"/> when the job has no MP pool.</param>
    /// <param name="item">Potion name; the only variable-length field, and the only one truncation may cut.</param>
    internal static string BuildLine(
        long unixMs, uint job, char ev, uint itemId,
        float hpPct, float hpThr, float mpPct, float mpThr,
        bool inCombat, bool inDuty, bool deepDungeon,
        string reason, string? item)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(MaxLineLength + 64);

        sb.Append(Prefix)
          .Append(unixMs).Append('|')
          .Append(job.ToString(inv)).Append('|')
          .Append(ev).Append('|')
          .Append(itemId.ToString(inv)).Append('|')
          .Append(hpPct.ToString("F1", inv)).Append('|')
          .Append(hpThr.ToString("F1", inv)).Append('|')
          .Append(mpPct.ToString("F1", inv)).Append('|')
          .Append(mpThr.ToString("F1", inv)).Append('|')
          .Append(inCombat ? '1' : '0').Append('|')
          .Append(inDuty ? '1' : '0').Append('|')
          .Append(deepDungeon ? '1' : '0').Append('|')
          .Append(reason).Append('|');

        // Everything above is what the ffxivdb join needs and is length-bounded by
        // construction; the item name is the only part allowed to be cut short.
        var fixedLength = sb.Length;
        if (fixedLength > MaxLineLength - 1)
        {
            // Defensive: a pathological reason code can never be allowed to blow the
            // budget either. Cut the whole line rather than emit something oversized.
            sb.Length = MaxLineLength - 1;
            return sb.Append('~').ToString();
        }

        if (!string.IsNullOrEmpty(item))
        {
            // The item name is a game string; strip the separator so a translated name
            // containing '|' can never fabricate an extra field.
            foreach (var ch in item)
                sb.Append(ch == '|' ? '/' : ch);

            if (sb.Length > MaxLineLength - 1)
            {
                sb.Length = MaxLineLength - 1;
                return sb.Append('~').ToString();
            }
        }

        return sb.ToString();
    }
}
