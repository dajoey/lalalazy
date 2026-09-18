#region

using System;
using System.Globalization;

#endregion

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     PURE line formats + emit gate for the Smart Movement telemetry tap (v2).
///     Grammar (shared tap contract, decision-taps.md; <b>MT| is TAKEN</b> by LazyMarketCompanion):
///     <list type="bullet">
///     <item><c>MVH|unixms|build|cushion|speed|legend</c> once per plugin load.</item>
///     <item><c>MV|unixms|job|dec|tgt|dist|nz|dst|px,pz|spd|lee|maxg|plan</c> on every decision change and at least every 1.0 s while any zone is live.</item>
///     <item><c>MZ|unixms|add|zoneId|actor|action|shape|cx,cz|r|inner|hw|hang|rot|act_ms</c> / <c>MZ|unixms|del|zoneId|why</c> on every zone change.</item>
///     </list>
///     dist = distance-to-target minus desired range (1 decimal). nz = live zones.
///     dst = steering waypoint "x,z" (1 decimal) or "-". spd = assumed speed. lee = path
///     leeway seconds ("inf" when nothing threatens). maxg = seconds the current cell stays safe
///     ("inf" when safe). plan = planner steps. All InvariantCulture; 200-char budget, cut with ~.
/// </summary>
internal static class MovementTelemetryFormat
{
    internal const string Prefix = "MV";
    internal const string ZonePrefix = "MZ";
    internal const string HeaderPrefix = "MVH";

    /// <summary> Rate floor for an unchanged key. </summary>
    internal const int MinIntervalMs = 1000;

    internal const string Legend = "off=disabled nav=vnavmesh-not-ready man=manual-input cast=finishing-cast ext=external-mover ddg=dodging eng=to-band stl=settled ooc=out-of-combat hold=safe-cell-zones-live stuck=unsafe-no-exit esc=dodge-no-leeway mnt=mounted kb=knockback appr=vnavmesh-approach hook=steering-hook-unavailable";

    private static string F1(float v) => v.ToString("F1", CultureInfo.InvariantCulture);
    private static string Sec(float v) => v >= float.MaxValue / 2 ? "inf" : v.ToString("F2", CultureInfo.InvariantCulture);

    internal static string BuildHeader(long unixMs, string build, float cushionSec, float speed)
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Join('|', HeaderPrefix, unixMs.ToString(inv), build, cushionSec.ToString("F2", inv), F1(speed), Legend);
    }

    internal static string BuildLine(long unixMs, byte job, string dec, uint tgtDataId, float distPastBand, int liveZones,
        float? dstX, float? dstZ, float px, float pz, float speed, float leewaySec, float startMaxG, int planSteps)
    {
        var inv = CultureInfo.InvariantCulture;
        var dst = dstX is { } x && dstZ is { } z ? F1(x) + "," + F1(z) : "-";
        var line = string.Join('|',
            Prefix, unixMs.ToString(inv), job.ToString(inv), dec, tgtDataId.ToString(inv), F1(distPastBand),
            liveZones.ToString(inv), dst, F1(px) + "," + F1(pz), F1(speed), Sec(leewaySec), Sec(startMaxG), planSteps.ToString(inv));
        return Clip(line);
    }

    internal static string BuildZoneAdd(long unixMs, ulong zoneId, ulong actor, uint action, string shape, float cx, float cz,
        float radius, float inner, float halfWidth, float halfAngleDeg, float rotDeg, long activationMs)
    {
        var inv = CultureInfo.InvariantCulture;
        var line = string.Join('|',
            ZonePrefix, unixMs.ToString(inv), "add", zoneId.ToString("X", inv), actor.ToString("X", inv), action.ToString("X", inv), shape,
            F1(cx) + "," + F1(cz), F1(radius), F1(inner), F1(halfWidth), F1(halfAngleDeg), F1(rotDeg), activationMs.ToString(inv));
        return Clip(line);
    }

    internal static string BuildZoneDel(long unixMs, ulong zoneId, string why)
    {
        var inv = CultureInfo.InvariantCulture;
        return Clip(string.Join('|', ZonePrefix, unixMs.ToString(inv), "del", zoneId.ToString("X", inv), why));
    }

    private static string Clip(string line) => line.Length > 200 ? line[..199] + "~" : line;

    /// <summary> Gate key: the tuple that identifies the situation. </summary>
    internal readonly record struct EmitKey(byte Decision, int DstX, int DstZ, int Zones);

    internal static EmitKey KeyOf(string dec, float? dstX, float? dstZ, int liveZones) => new(
        DecisionCode(dec),
        dstX is { } x ? (int)MathF.Round(x * 2) : int.MinValue,
        dstZ is { } z ? (int)MathF.Round(z * 2) : int.MinValue,
        liveZones);

    internal static byte DecisionCode(string dec) => dec switch
    {
        "off" => 1, "nav" => 2, "man" => 3, "cast" => 4, "ext" => 5, "ddg" => 6, "eng" => 7, "stl" => 8, "ooc" => 9,
        "hold" => 10, "stuck" => 11, "esc" => 12, "mnt" => 14, "kb" => 15, "appr" => 16, "hook" => 17,
        _ => 0,
    };

    /// <summary>
    ///     Emit on a key change once the rate floor has elapsed; a dodge start
    ///     always emits; while any zone is live, the state re-emits every
    ///     <see cref="MinIntervalMs"/> even if unchanged (no more silent holds).
    /// </summary>
    internal static bool ShouldEmit(EmitKey? lastKey, long lastMs, long nowMs, EmitKey candidate, bool isDodgeStart, bool zonesLive)
    {
        if (isDodgeStart || lastKey is null)
            return true;
        if (nowMs - lastMs < MinIntervalMs)
            return false;
        return zonesLive || lastKey.Value != candidate;
    }

    internal static string[] Parse(string line) => line.Split('|');
}
