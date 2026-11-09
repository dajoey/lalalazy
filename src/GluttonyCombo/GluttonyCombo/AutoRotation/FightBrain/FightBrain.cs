// Fork (Fight Brain 1a): the Fight Brain tick — LOG-ONLY ("code owns facts, log
// first, act later"):
//   FB|forecast <add|drop>   one line when a forecast entry appears or expires
//   FB|cast-gate             one would-block line when the forecast-aware cast gate would have
//                            blocked a hard cast (shadow: the cast still starts)
// and the countdown strip (Window/FightBrainStrip.cs) draws code forecasts only.
// Nothing here presses an action, and nothing here sends a snapshot anywhere: the
// AI decision layer was removed on 2026-11-09. The forecast, the strip, the cast
// gate and the party-data shadow rules are pure code that never needed it.

using ECommons.DalamudServices;
using GluttonyCombo.Core;
using GluttonyCombo.Services;
using Lalalazy.Telemetry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GluttonyCombo.AutoRotation;

internal static class FightBrain
{
    /// <summary> Local-only Fight Brain settings (no IPC lease can drive them): the fork's rotation config owns them. </summary>
    private static AutoRotationConfig Config => Service.Configuration.RotationConfig;

    /// <summary> The forecast as of the last read, for the strip and the tick. </summary>
    private static List<FightBrainHit> _current = [];

    /// <summary> The last read that produced a change, so the strip can age entries without re-reading IPC. </summary>
    internal static long LastReadMs;

    /// <summary> True while the last forecast read saw BossMod answering. </summary>
    internal static bool ForecastSeen;

    internal static bool Enabled => Config.FightBrain.Enabled;

    // ------------------------------------------------------------------ logging

    internal static void Log(string detail)
    {
        var line = $"FB|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|{detail}";
        Svc.Log.Information(line);
        LalaTelemetry.Record(line);
    }

    // ------------------------------------------------------------------ tick

    private static long _nextReadMs;

    /// <summary> Called from the framework update, after the player checks. One cheap bool read when disabled. </summary>
    internal static void Tick()
    {
        if (!Enabled)
            return;
        var nowMs = Environment.TickCount64;
        if (nowMs < _nextReadMs)
            return;
        _nextReadMs = nowMs + FightBrainForecast.ReadEveryMs;

        var reading = FightBrainForecast.Read(nowMs);
        ForecastSeen = reading.Seen;
        var fresh = FightBrainForecastCore.Merge(_current, [], nowMs); // drop overdue entries from the kept list
        fresh = FightBrainForecastCore.Merge(fresh, reading.Hits, nowMs);
        LogForecastChange(_current, fresh, nowMs);
        _current = fresh;
        LastReadMs = nowMs;

        // The party-data shadow rules (FB|party-mit and FB|pos-zone) ride the same tick
        // and the same tick guard; log-only, pure code.
        FightBrainParty.Tick(nowMs);
    }

    /// <summary> The kept forecast entries. </summary>
    internal static IReadOnlyList<FightBrainHit> CurrentHits() => _current;

    private static void LogForecastChange(IReadOnlyList<FightBrainHit> before, IReadOnlyList<FightBrainHit> after, long nowMs)
    {
        foreach (var hit in after)
        {
            if (!before.Any(b => b.Kind == hit.Kind && Math.Abs(b.AtMs - hit.AtMs) <= FightBrainHit.MergeWindowMs))
                Log($"forecast add kind={hit.Kind} in={hit.Countdown(nowMs)} src={hit.Source}");
        }
        foreach (var hit in before)
        {
            if (!after.Any(a => a.Kind == hit.Kind && Math.Abs(a.AtMs - hit.AtMs) <= FightBrainHit.MergeWindowMs))
                Log($"forecast drop kind={hit.Kind} src={hit.Source}");
        }
    }

    /// <summary> The current forecast entries with their countdowns now, for the strip. </summary>
    internal static IReadOnlyList<(FightBrainHit Hit, double Seconds)> StripEntries()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return [.. _current.OrderBy(h => h.AtMs).Take(6).Select(h => (h, (h.AtMs - now) / 1000.0))];
    }

    // ------------------------------------------------------------------ cast gate (shadow)

    /// <summary> A cast that is still running when the player must move is a cast the zone eats: this much, plus the gate's 0.5 s, has to fit before the zone activates. </summary>
    internal const double WalkOutSeconds = 1.0;

    /// <summary> The gate writes at most one would-block line per action per this many ms. </summary>
    private const long GateLogEveryMs = 500;

    private static long _nextGateLogMs;
    private static uint _lastGateAct;

    /// <summary>
    ///     The forecast-aware cast gate, SHADOW: called wherever <see cref="AutoRotationController.MovementBlocksCastStart"/>
    ///     let a hard cast through. It never blocks; when the cast would still run at the moment the player must move
    ///     (the danger zone under the player, from BMR), it writes one FB|cast-gate line so the pass bar ("would-block
    ///     calls match real cancels at least 70% of the time") can be graded against real play.
    /// </summary>
    internal static void ShadowCastGate(float castTimeMs, uint actionId)
    {
        if (castTimeMs <= 0f || !Enabled || !ForecastSeen)
            return;
        var (zoneIn, standingInZone) = FightBrainForecast.ZoneUnderPlayer();
        if (zoneIn is not { } zone)
            return;
        var nowMs = Environment.TickCount64;
        var castEndIn = castTimeMs / 1000.0 + 0.5;
        if (!FightBrainForecastCore.WouldBlockCast(castEndIn, zone, standingInZone, WalkOutSeconds))
            return;
        if (nowMs < _nextGateLogMs && _lastGateAct == actionId)
            return;
        _nextGateLogMs = nowMs + GateLogEveryMs;
        _lastGateAct = actionId;
        Log($"cast-gate would-block act={actionId} cast_end_in={castEndIn:F1} zone_in={zone:F1} walk_out={WalkOutSeconds:F1}");
    }
}
