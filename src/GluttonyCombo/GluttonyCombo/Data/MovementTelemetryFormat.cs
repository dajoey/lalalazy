using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GluttonyCombo.Data;

/// <summary>
///     The pure half of the fork's movement tap (2026-11-05, decision B of the Jev Fight Brain
///     plan): the line formats and emit gates for the 10 Hz movement recorder and the shadow move
///     planner, with no Dalamud or game types, so <c>tests/GluttonyCombo.MovementTelemetryHarness</c>
///     can assert the exact shape of what ships. <see cref="MovementRecorder"/> and
///     <see cref="MovementPlanner"/> are the live halves that read the game state and call in here.
/// </summary>
/// <remarks>
///     <b>Log only.</b> Nothing in this feature may press a button, write a BMR preset or transient,
///     or move the character: the recorder only observes, and the planner's only outputs are
///     <c>MP|</c> lines and relay questions. The three line kinds ride two transports:
///     <c>MP|</c> goes to the plugin log (low rate, rides the ffxivdb <c>plugin_log_lines</c>
///     harvest for humans) AND the relay ingest; <c>M|</c> and <c>H|</c> go ONLY to the relay
///     ingest (10 Hz volume never enters dalamud.log) and land in ffxivdb tables with 365-day
///     retention - kept past the 7-day INF purge on purpose, because Joey's own hand-played
///     movement is the reference for "human-like".
/// </remarks>
internal static class MovementFormat
{
    /// <summary> One sampled actor (the player or a party member) at one 10 Hz tick. </summary>
    public const string TickPrefix = "M|";

    /// <summary> One casting hostile (the telegraph signal) at one tick, change-gated. </summary>
    public const string HazardPrefix = "H|";

    /// <summary> One shadow move decision: options, code pick, Jev pick. </summary>
    public const string DecisionPrefix = "MP|";

    /// <summary> The forecast's next mechanic (Fight Brain Stage 1a join), change-gated. </summary>
    public const string ForecastPrefix = "F|";

    /// <summary> Hard cap for one forecast token. </summary>
    public const int MaxForecastToken = 48;

    /// <summary> Hard budget for one decision line (tick/hazard lines are shorter). </summary>
    public const int MaxDecisionLength = 320;

    /// <summary> Flag bits on the <c>M|</c> line's flags field. </summary>
    public const int FlagMoving = 1;
    public const int FlagKeyHeld = 2;
    public const int FlagInCombat = 4;
    public const int FlagSelfCasting = 8;
    public const int FlagBmrQueued = 16;

    /// <summary> Slot 0 is the player; 1..7 follow the party list order. </summary>
    public const int MaxPartySlots = 8;

    /// <summary> Most move options on one decision line (stay + up to three candidates). </summary>
    public const int MaxOptions = 4;

    /// <summary> Most re-asks of one cast per the Fight Brain plan (only on a consequence-class change). </summary>
    public const int MaxDecisionsPerCast = 3;

    /// <summary> Hazards re-emit only when the cast changes or the remaining time crosses a half-second step. </summary>
    public const int HazardRemainStepsPerSecond = 2;

    /// <summary>
    ///     Builds one tick line:
    ///     <c>M|unixms|pull|zone|slot|job|x|y|z|rot|flags|marker</c>.
    ///     Coordinates are 2dp (yalms), rotation 3dp (radians) - invariant culture, never a comma.
    /// </summary>
    public static string BuildTickLine(
        long unixMs, uint pullId, uint zoneId, int slot, uint jobId,
        float x, float y, float z, float rot, int flags, uint markerId)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(96);
        sb.Append(TickPrefix)
          .Append(unixMs).Append('|')
          .Append(pullId).Append('|')
          .Append(zoneId).Append('|')
          .Append(slot).Append('|')
          .Append(jobId).Append('|')
          .Append(x.ToString("F2", inv)).Append('|')
          .Append(y.ToString("F2", inv)).Append('|')
          .Append(z.ToString("F2", inv)).Append('|')
          .Append(rot.ToString("F3", inv)).Append('|')
          .Append(flags).Append('|')
          .Append(markerId);
        return sb.ToString();
    }

    /// <summary>
    ///     Builds one hazard line:
    ///     <c>H|unixms|pull|zone|srcId|castActionId|remain|total|tgtId|x|y|z|rot</c> -
    ///     the caster's own position and facing (the telegraph's anchor) plus the cast clock.
    /// </summary>
    public static string BuildHazardLine(
        long unixMs, uint pullId, uint zoneId, ulong sourceId, uint castActionId,
        float remainS, float totalS, ulong castTargetId,
        float x, float y, float z, float rot)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(128);
        sb.Append(HazardPrefix)
          .Append(unixMs).Append('|')
          .Append(pullId).Append('|')
          .Append(zoneId).Append('|')
          .Append(sourceId).Append('|')
          .Append(castActionId).Append('|')
          .Append(remainS.ToString("F2", inv)).Append('|')
          .Append(totalS.ToString("F2", inv)).Append('|')
          .Append(castTargetId).Append('|')
          .Append(x.ToString("F2", inv)).Append('|')
          .Append(y.ToString("F2", inv)).Append('|')
          .Append(z.ToString("F2", inv)).Append('|')
          .Append(rot.ToString("F3", inv));
        return sb.ToString();
    }

    /// <summary> One checked option of a shadow move decision. </summary>
    /// <param name="Id"> 0 = stay put, 1..3 = candidate spots. </param>
    /// <param name="X/Y"> The spot (stay: the player's current position). </param>
    /// <param name="LeaveInS"> Seconds from now the walk should start (0 = now). </param>
    /// <param name="ArriveInS"> Seconds from now the spot is reached. </param>
    /// <param name="GcdsLost"> Estimated GCDs the travel costs (travel seconds / 2.5). </param>
    /// <param name="PartyDist"> Yalms from the party's centre of mass at decision time. </param>
    /// <param name="BmrChecked"> True when stock BMR's <c>Hints.IsDashSafe</c> passed the path's end. </param>
    public readonly record struct MoveOption(
        int Id, float X, float Y, float LeaveInS, float ArriveInS,
        float GcdsLost, float PartyDist, bool BmrChecked);

    /// <summary>
    ///     Builds one decision line:
    ///     <c>MP|unixms|pull|zone|castActionId|hitIn|opts|code=i|jev=i|-|codeWhy|jevWhy</c>.
    ///     The option list is the only part allowed to be cut short (the stay option is
    ///     always written first, so a truncated line still carries a pickable baseline).
    /// </summary>
    public static string BuildDecisionLine(
        long unixMs, uint pullId, uint zoneId, uint castActionId, float hitInS,
        IReadOnlyList<MoveOption> options, int codePick, int jevPick,
        string codeWhy, string jevWhy)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(MaxDecisionLength + 32);

        sb.Append(DecisionPrefix)
          .Append(unixMs).Append('|')
          .Append(pullId).Append('|')
          .Append(zoneId).Append('|')
          .Append(castActionId).Append('|')
          .Append(hitInS.ToString("F2", inv)).Append('|');

        var truncated = false;
        for (var i = 0; i < options.Count; i++)
        {
            if (i > 0)
                sb.Append(';');

            var mark = sb.Length;
            var o = options[i];
            sb.Append(o.Id).Append(':')
              .Append(o.X.ToString("F1", inv)).Append(',')
              .Append(o.Y.ToString("F1", inv)).Append(',')
              .Append(o.LeaveInS.ToString("F1", inv)).Append(',')
              .Append(o.ArriveInS.ToString("F2", inv)).Append(',')
              .Append(o.GcdsLost.ToString("F1", inv)).Append(',')
              .Append(o.PartyDist.ToString("F1", inv)).Append(',')
              .Append(o.BmrChecked ? '1' : '0');

            if (sb.Length > MaxDecisionLength - 24 && i > 0)
            {
                sb.Length = mark;
                truncated = true;
                break;
            }
        }

        if (truncated)
            sb.Append('~');

        sb.Append("|code=").Append(codePick)
          .Append("|jev=").Append(jevPick >= 0 ? jevPick.ToString(inv) : "-")
          .Append('|').Append(SanitizeToken(codeWhy))
          .Append('|').Append(SanitizeToken(jevWhy));

        return sb.ToString();
    }

    /// <summary>
    ///     Builds one forecast line: <c>F|unixms|pull|zone|token</c> - the Fight Brain
    ///     forecast's next mechanic as a short token (e.g. <c>act36996in4.2s</c>), or
    ///     <c>none</c> when the forecast has nothing. Change-gated by the caller.
    /// </summary>
    public static string BuildForecastLine(long unixMs, uint pullId, uint zoneId, string? token) =>
        new StringBuilder(96).Append(ForecastPrefix)
            .Append(unixMs).Append('|')
            .Append(pullId).Append('|')
            .Append(zoneId).Append('|')
            .Append(SanitizeToken(token is null or "-" or "none" ? null : token)).ToString();

    /// <summary>
    ///     The hazard change gate: a cast emits when it is new for this source or its
    ///     remaining time crossed a half-second step - arrival-time fidelity of 0.5 s at a
    ///     fraction of the per-tick volume. Returns true (and records the step) when the
    ///     line should be emitted.
    /// </summary>
    public static bool HazardShouldEmit(
        Dictionary<(ulong Source, uint Action), int> lastSteps,
        ulong sourceId, uint castActionId, float remainS)
    {
        var step = (int)MathF.Floor(MathF.Max(remainS, 0f) * HazardRemainStepsPerSecond);
        var key = (sourceId, castActionId);
        if (lastSteps.TryGetValue(key, out var last) && last == step)
            return false;
        lastSteps[key] = step;
        return true;
    }

    /// <summary> Strips separators and caps a reason token, so a line never grows fields. </summary>
    public static string SanitizeToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return "none";
        var sb = new StringBuilder(Math.Min(token.Length, 24));
        foreach (var c in token)
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.')
                sb.Append(c);
            else if (sb.Length == 0 || sb[^1] != '_')
                sb.Append('_');
            if (sb.Length >= 24)
                break;
        }

        return sb.Length > 0 ? sb.ToString() : "none";
    }
}
