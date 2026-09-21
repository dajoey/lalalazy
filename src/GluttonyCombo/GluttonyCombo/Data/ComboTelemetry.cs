using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Lalalazy.Telemetry;
using System;
using System.Collections.Generic;
using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Extensions;
using GluttonyCombo.Services;

namespace GluttonyCombo.Data;

/// <summary>
///     Optional, off-by-default combo-decision tap (fork, v1.0.4.168).<br />
///     When <see cref="Configuration.ComboTelemetry"/> is on, every time a
///     combo's settled action for a given button CHANGES, one structured line
///     is written at Information level through the normal plugin logger:
///     <c>CT|unixms|job|combo|originalActionId|chosenActionId|gcdRemaining|weaveSlot|targetHpPct|keyBuffs</c>.<br />
///     It rides the existing dalamud.log → ffxivdb <c>plugin_log_lines</c>
///     harvest (no transport of its own) and is joined to the ACT-derived
///     <c>action_events</c> table by timestamp + chosenActionId
///     (wiki Docker/ffxivdb § Action telemetry → Combo decisions).
/// </summary>
/// <remarks>
///     <b>When off (the default):</b> nothing is logged, but since 2026-09-21 the same
///     change-gate still runs and a changed decision is formatted into the in-memory
///     telemetry ring (src/Shared/LalaTelemetry) that <c>/gluttony report</c> dumps -
///     capped by a token bucket at 10 lines/s (burst 20), so a flickering decision can
///     never format on every frame. Per call this is one dictionary lookup; formatting
///     happens only on a decision change.<br />
///     <b>keyBuffs</b> is what the combos consulted THIS FRAME — the status
///     cache is cleared every framework tick and only lookups made by combo
///     code populate it — so it is per-frame, not strictly per-combo. Player
///     statuses are <c>id:remaining</c>, statuses on another object (the
///     target, mostly) are <c>t&lt;id&gt;:remaining</c>, and a consulted-but-
///     absent status is <c>id:-</c>.<br />
///     The line format and the emit gate live in
///     <see cref="ComboTelemetryFormat"/> so they can be asserted offline by
///     <c>tests/GluttonyCombo.TelemetryHarness</c>.
/// </remarks>
internal static class ComboTelemetry
{
    /// <inheritdoc cref="ComboTelemetryFormat.Prefix"/>
    public const string Prefix = ComboTelemetryFormat.Prefix;

    /// <summary> Last settled action per (preset, pressed button). </summary>
    private static readonly Dictionary<(uint Preset, uint Original), uint> LastChosen = new();

    /// <summary> Ring-only formatting budget while the log toggle is off (never per frame). </summary>
    private static TokenBucket _ringBudget = new(20, 10);

    /// <summary> Forgets every remembered decision (toggle-on, so the first decision re-emits). </summary>
    public static void Reset() => LastChosen.Clear();

    /// <summary>
    ///     Records one settled decision; emits a line only when the chosen
    ///     action for this (preset, button) pair differs from the last one.
    ///     <paramref name="log"/> = the "Combo Decision Telemetry" switch: on writes the
    ///     line to the plugin log; off keeps it only in the telemetry ring (rate-capped).
    /// </summary>
    /// <param name="preset"> The combo that made the decision. </param>
    /// <param name="original"> The button that was pressed / walked. </param>
    /// <param name="chosen"> The action that will actually go out (== original when unchanged). </param>
    /// <param name="log"> Write to the plugin log (the telemetry switch), not just the ring. </param>
    public static void Record(Preset preset, uint original, uint chosen, bool log)
    {
        if (!ComboTelemetryFormat.ShouldEmit(LastChosen, (uint)preset, original, chosen))
            return;

        if (!log && !_ringBudget.TryTake(Environment.TickCount64))
            return;

        try
        {
            var line = ComboTelemetryFormat.BuildLine(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Player.Job.ToString(),
                preset.ToString(),
                original,
                chosen,
                CustomComboFunctions.RemainingGCD,
                ActionWatching.WeaveActions.Count,
                CustomComboFunctions.CanWeave(),
                CustomComboFunctions.GetTargetHPPercent(),
                ConsultedBuffs());
            if (log)
                Svc.Log.Information(line);
            LalaTelemetry.Record(line);
        }
        catch (Exception ex)
        {
            // A telemetry tap must never be able to break a combo - but one that silently stops is how a
            // report ends up with "zero telemetry". Rate-limited WRN (fork error reporting), not Debug.
            LalaTelemetry.Swallowed("telemetry.ct", ex);
        }
    }

    /// <summary> The statuses combo code looked up this frame, as flat records. </summary>
    private static IEnumerable<ComboTelemetryFormat.Buff> ConsultedBuffs()
    {
        var playerId = Player.Object?.GameObjectId;
        foreach (var (statusId, targetId, status) in Service.ComboCache.ConsultedStatuses())
            yield return new ComboTelemetryFormat.Buff(
                statusId,
                targetId == playerId,
                status is null ? null : status.RemainingTimeOrZero(false));
    }
}
