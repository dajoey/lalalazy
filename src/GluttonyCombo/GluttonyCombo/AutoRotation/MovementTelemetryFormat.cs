#region

using System;
using System.Collections.Generic;
using System.Globalization;

#endregion

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     PURE line format + emit gate for the SmartMover movement-decision tap
///     (fork, t_356159a8, v1.0.4.191). Grammar follows the shared tap contract
///     (decision-taps.md): <c>MV|unixms|job|dec|tgt|dist|nz|dst|ovz</c>.
/// </summary>
/// <remarks>
///     <b>MT| is TAKEN</b> (LazyMarketCompanion) - the movement tap owns MV.
///     dist = distance-to-target minus desired-range, 1 decimal, InvariantCulture.
///     nz = count of live derived zones. dst = "x,z" rounded to 1y, or "-".
///     ovz (v1.0.4.195) = count of live omen-telegraph zones among those.
///     Gate: emit on change of (dec, dst) plus a 1.0s floor; a dodge START
///     (dec=ddg after a non-ddg line) always emits immediately; plugin-off
///     states (off/nav/man) are never logged as decisions. 200-char budget,
///     truncation marker ~, cut only the last field.
/// </remarks>
internal static class MovementTelemetryFormat
{
    internal const string Prefix = "MV";

    /// <summary> Rate floor: same key may not emit more often than this. A const, not config. </summary>
    internal const int MinIntervalMs = 1000;

    internal static string BuildLine(long unixMs, byte job, string dec, uint tgtDataId, float distPastBand, int liveZones, float? dstX, float? dstZ, int omenZones)
    {
        var inv = CultureInfo.InvariantCulture;
        var dist = distPastBand.ToString("F1", inv);
        var dst = dstX is { } x && dstZ is { } z
            ? MathF.Round(x).ToString(inv) + "," + MathF.Round(z).ToString(inv)
            : "-";

        var line = string.Join('|',
            Prefix, unixMs.ToString(inv), job.ToString(inv), dec, tgtDataId.ToString(inv), dist,
            liveZones.ToString(inv), dst, omenZones.ToString(inv));

        if (line.Length > 200)
            line = line[..199] + "~";
        return line;
    }

    /// <summary> Gate key: the tuple that identifies the situation. </summary>
    internal readonly record struct EmitKey(byte Decision, int DstX, int DstZ);

    internal static EmitKey KeyOf(string dec, float? dstX, float? dstZ) => new(
        DecisionCode(dec),
        dstX is { } x ? (int)MathF.Round(x) : int.MinValue,
        dstZ is { } z ? (int)MathF.Round(z) : int.MinValue);

    internal static byte DecisionCode(string dec) => dec switch
    {
        "ddg" => 6,
        "eng" => 7,
        "stl" => 8,
        "man" => 3,
        "cast" => 4,
        "bmr" => 5,
        _ => 0,
    };

    /// <summary>
    ///     Whether to emit, given the last emitted key/time and the current
    ///     candidate. Dodge-starts bypass the floor (rare events always emit);
    ///     everything else needs a key change AND the floor elapsed. Suppressed
    ///     states are not recorded, so the first tick past the window still emits.
    /// </summary>
    internal static bool ShouldEmit(EmitKey? lastKey, long lastMs, long nowMs, EmitKey candidate, bool isDodgeStart)
    {
        if (isDodgeStart)
            return true;
        if (lastKey is null)
            return true;
        if (nowMs - lastMs < MinIntervalMs)
            return false;
        return lastKey.Value != candidate;
    }

    /// <summary> Splits a line back into fields (1-based including prefix) - used by the harness and grading views. </summary>
    internal static string[] Parse(string line) => line.Split('|');
}
