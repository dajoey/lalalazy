using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GluttonyCombo.Data;

/// <summary>
///     The pure half of the fork's Beastmaster debug collector (<c>BT|</c>): the emit gate and
///     the line format, with no Dalamud/game types, so
///     <c>tests/GluttonyCombo.TelemetryHarness</c> can assert the exact shape of what ships and
///     REPLAY the rate limit rather than reasoning about it.
///     <see cref="BeastmasterTelemetry"/> is the live half that reads the game state.
/// </summary>
/// <remarks>
///     This is a COLLECTOR, not a decision tap: BST has no rotation logic yet, so there is no
///     decision to record. It samples gauge + pet + adjusted-action state from the framework
///     tick so the follow-up rotation cards can mine <c>plugin_log_lines</c> for real play data.
///     It shares the <c>XX|unixms|...</c> grammar of <c>CT|</c> / <c>PT|</c> / <c>MT|</c> /
///     <c>FT|</c> on purpose, so one SQL shape reads every plugin.
/// </remarks>
internal static class BeastmasterTelemetryFormat
{
    /// <summary> Fixed, greppable line prefix: <c>message LIKE 'BT|%'</c> in ffxivdb. </summary>
    public const string Prefix = "BT|";

    /// <summary> Hard budget for one emitted line. </summary>
    public const int MaxLineLength = 200;

    /// <summary>
    ///     Minimum gap between two emitted lines, in milliseconds - a hard cap of 4 lines/s.
    /// </summary>
    /// <remarks>
    ///     A <c>const</c>, not a config knob: a user-tunable spam limit is a user-tunable
    ///     footgun. The framework tick runs at frame rate (~100+/s at an uncapped frame rate),
    ///     so the change-gate alone is not enough - a rapidly flapping gauge byte (chain count
    ///     ticking, familiar TP arriving) would otherwise emit per frame.
    /// </remarks>
    public const int MinIntervalMs = 250;

    /// <summary> Most status ids listed before truncation. </summary>
    public const int MaxStatuses = 10;

    /// <summary>
    ///     Everything the collector samples in one framework tick. The nine gauge bytes (0x08..0x10) are
    ///     carried raw so a follow-up card can re-interpret them without a new release, and the
    ///     decoded fields the PR names are carried alongside for direct SQL.
    /// </summary>
    internal readonly record struct Snapshot(
        byte TPGauge,
        byte FamiliarTPGauge,
        byte FamiliarTPAtLastUse,
        byte ActiveBattlehorn,
        byte InstinctualComboState,
        byte CurrentAffinity,
        byte ChainCount,
        byte KinshipState,
        ulong PetObjectId,
        string? PetName,
        uint PetDataId,
        uint AdjustedBeastMode,
        uint AdjustedAvalanche,
        IReadOnlyList<ushort> Statuses,
        uint DecisionActionId = 0,
        string? DecisionReason = null,
        string? FamiliarDecline = null,
        byte InstinctStacks = 0);

    /// <summary>
    ///     The identity of a snapshot for change detection: the eight gauge bytes, the pet
    ///     buddy id and the adjusted Beast Mode action id, exactly as the card specifies.
    /// </summary>
    /// <remarks>
    ///     Statuses are deliberately NOT part of the key. They flap (a Heart status ticking in
    ///     and out mid-combo) and would defeat the change gate, and any status transition worth
    ///     seeing moves a gauge byte too.
    /// </remarks>
    internal static (ulong Gauge, ulong Pet, uint BeastMode, uint DecisionActionId, string DecisionReason, string FamiliarDecline, byte InstinctStacks) KeyOf(in Snapshot s)
    {
        ulong gauge =
            ((ulong)s.TPGauge << 56) |
            ((ulong)s.FamiliarTPGauge << 48) |
            ((ulong)s.FamiliarTPAtLastUse << 40) |
            ((ulong)s.ActiveBattlehorn << 32) |
            ((ulong)s.InstinctualComboState << 24) |
            ((ulong)s.CurrentAffinity << 16) |
            ((ulong)s.ChainCount << 8) |
            s.KinshipState;

        return (gauge, s.PetObjectId, s.AdjustedBeastMode, s.DecisionActionId, s.DecisionReason ?? "", s.FamiliarDecline ?? "", s.InstinctStacks);
    }

    /// <summary>
    ///     The emit gate: a line goes out only when the snapshot CHANGED against the last
    ///     emitted one, and never more often than <see cref="MinIntervalMs"/>.
    /// </summary>
    /// <remarks>
    ///     The rate floor is unconditional and a suppressed change is NOT recorded, so the
    ///     state stays "new" and emits on the first tick past the window - otherwise a boring
    ///     transition would permanently swallow an interesting one 200 ms later.
    /// </remarks>
    /// <param name="state"> Mutable gate state, carried by the caller. </param>
    /// <param name="nowMs"> Current time in unix milliseconds. </param>
    /// <param name="snapshot"> The state sampled this tick. </param>
    internal static bool ShouldEmit(ref GateState state, long nowMs, in Snapshot snapshot)
    {
        var key = KeyOf(snapshot);

        if (state.HasLast && state.LastKey == key)
            return false;

        if (state.HasEmitted && nowMs - state.LastEmitMs < MinIntervalMs)
            return false;

        state.HasLast = true;
        state.LastKey = key;
        state.HasEmitted = true;
        state.LastEmitMs = nowMs;
        return true;
    }

    /// <summary> Gate state for <see cref="ShouldEmit"/>; reset clears it entirely. </summary>
    internal struct GateState
    {
        public bool HasLast;
        public (ulong Gauge, ulong Pet, uint BeastMode, uint DecisionActionId, string DecisionReason, string FamiliarDecline, byte InstinctStacks) LastKey;
        public bool HasEmitted;
        public long LastEmitMs;

        public void Reset() => this = default;
    }

    /// <summary>
    ///     Builds one collector line:
    ///     <c>BT|unixms|gaugeHex|battlehorn|affinity|chain|kinship|pet|bm|av|dec|fd|statuses</c>.
    /// </summary>
    /// <remarks>
    ///     <c>gaugeHex</c> is the nine gauge bytes 0x08..0x10 in order (byte 0x10 packs the Mastered/Natural instinct-stack nibbles), lower-case hex, no
    ///     separator. <c>pet</c> is <c>&lt;GameObjectId&gt;:&lt;Name&gt;:&lt;BNpcBase&gt;</c>
    ///     or the literal <c>none</c>. <c>dec</c> is <c>&lt;actionId&gt;:&lt;reason&gt;</c> -
    ///     the rotation's own record of what it chose and why (t_02fe2681), so the next card
    ///     can grade chains straight out of <c>plugin_log_lines</c> without re-deriving intent
    ///     from the gauge bytes alone. <c>fd</c> is why the FAMILIAR LOOP declined to act on
    ///     a tick where it declined at all (t_f987910c fix 4) - empty when the loop acted or
    ///     had nothing to report, so resummon suppression (Beast Voice vs recast vs a summon
    ///     still in flight) and per-step refusals are gradable without re-deriving them from
    ///     the gauge bytes. Absent a decision (pre-rotation builds, or a tick where
    ///     nothing fired) it renders as <c>dec=0:</c>. The status list is the only field
    ///     allowed to be cut short, and truncation is marked with a trailing <c>~</c>.
    /// </remarks>
    internal static string BuildLine(long unixMs, in Snapshot s)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(MaxLineLength + 64);

        sb.Append(Prefix).Append(unixMs.ToString(inv)).Append('|');

        AppendHex(sb, s.TPGauge);
        AppendHex(sb, s.FamiliarTPGauge);
        AppendHex(sb, s.FamiliarTPAtLastUse);
        AppendHex(sb, s.ActiveBattlehorn);
        AppendHex(sb, s.InstinctualComboState);
        AppendHex(sb, s.CurrentAffinity);
        AppendHex(sb, s.ChainCount);
        AppendHex(sb, s.KinshipState);
        AppendHex(sb, s.InstinctStacks); // byte 0x10: instinct-stack nibbles (v1.0.4.189)

        sb.Append('|').Append(s.ActiveBattlehorn.ToString(inv))
          .Append('|').Append(s.CurrentAffinity.ToString(inv))
          .Append('|').Append(s.ChainCount.ToString(inv))
          .Append('|').Append(s.KinshipState.ToString(inv))
          .Append("|pet=");

        if (s.PetObjectId == 0)
        {
            sb.Append("none");
        }
        else
        {
            sb.Append(s.PetObjectId.ToString(inv)).Append(':')
              .Append(Sanitize(s.PetName)).Append(':')
              .Append(s.PetDataId.ToString(inv));
        }

        sb.Append("|bm=").Append(s.AdjustedBeastMode.ToString(inv))
          .Append("|av=").Append(s.AdjustedAvalanche.ToString(inv))
          .Append("|dec=").Append(s.DecisionActionId.ToString(inv)).Append(':')
          .Append(SanitizeReason(s.DecisionReason))
          .Append("|fd=").Append(SanitizeReason(s.FamiliarDecline))
          .Append('|');

        // Everything above is fixed-width-ish and always present; only the status list is cut.
        var written = 0;
        var truncated = false;

        for (var i = 0; i < s.Statuses.Count; i++)
        {
            if (written >= MaxStatuses)
            {
                truncated = true;
                break;
            }

            var mark = sb.Length;

            if (written > 0)
                sb.Append(',');
            sb.Append(s.Statuses[i].ToString(inv));

            // Cut on a whole entry, and leave room for the truncation marker.
            if (sb.Length > MaxLineLength - 1)
            {
                sb.Length = mark;
                truncated = true;
                break;
            }

            written++;
        }

        if (truncated)
            sb.Append('~');

        return sb.ToString();
    }

    private static void AppendHex(StringBuilder sb, byte value) =>
        sb.Append(HexDigits[value >> 4]).Append(HexDigits[value & 0x0F]);

    private const string HexDigits = "0123456789abcdef";

    /// <summary>
    ///     A translated pet name could contain the field separator and fabricate a field;
    ///     replace anything structural. Also collapses a null/blank name to a stable token.
    /// </summary>
    private static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        Span<char> buffer = stackalloc char[Math.Min(name.Length, 32)];
        for (var i = 0; i < buffer.Length; i++)
        {
            var c = name[i];
            buffer[i] = c is '|' or ',' or ':' or '\r' or '\n' ? '_' : c;
        }

        return new string(buffer);
    }

    /// <summary>
    ///     The decision reason is a short internal literal (e.g. <c>gcdchain</c>,
    ///     <c>battlehorn:slot2</c>) written by this codebase, not player-controlled text - but
    ///     it still shares the pet-name sanitiser's structural-character rule defensively, and
    ///     caps length so one reason string cannot dominate the 200-char line budget.
    /// </summary>
    private static string SanitizeReason(string? reason)
    {
        if (string.IsNullOrEmpty(reason))
            return "";

        Span<char> buffer = stackalloc char[Math.Min(reason.Length, 40)];
        for (var i = 0; i < buffer.Length; i++)
        {
            var c = reason[i];
            buffer[i] = c is '|' or ',' or '\r' or '\n' ? '_' : c;
        }

        return new string(buffer);
    }
}