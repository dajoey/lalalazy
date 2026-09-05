using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GluttonyCombo.Data;

/// <summary>
///     The pure half of the fork's combo-decision telemetry tap (v1.0.4.168):
///     the emit gate and the line format, with no Dalamud/game types, so
///     <c>tests/GluttonyCombo.TelemetryHarness</c> can assert the exact shape
///     of what ships. <see cref="ComboTelemetry"/> is the live half that reads
///     the game state and calls in here.
/// </summary>
internal static class ComboTelemetryFormat
{
    /// <summary> Fixed, greppable line prefix: <c>message LIKE 'CT|%'</c> in ffxivdb. </summary>
    public const string Prefix = "CT|";

    /// <summary> Hard budget for one emitted line. </summary>
    public const int MaxLineLength = 200;

    /// <summary> Most statuses listed in <c>keyBuffs</c> before truncation. </summary>
    public const int MaxBuffs = 12;

    /// <summary> One consulted status: the id, whether it sat on the player, and its remaining time (null = consulted but absent). </summary>
    internal readonly record struct Buff(uint StatusId, bool OnPlayer, float? Remaining);

    /// <summary>
    ///     The "only when the chosen action changes" gate. Returns true (and
    ///     records the new value) only when this (preset, button) pair settles
    ///     on a different action than last time — so a held button emits once,
    ///     not every frame.
    /// </summary>
    internal static bool ShouldEmit(
        Dictionary<(uint Preset, uint Original), uint> lastChosen,
        uint preset, uint original, uint chosen)
    {
        var key = (preset, original);
        if (lastChosen.TryGetValue(key, out var last) && last == chosen)
            return false;
        lastChosen[key] = chosen;
        return true;
    }

    /// <summary>
    ///     Builds one telemetry line:
    ///     <c>CT|unixms|job|combo|originalActionId|chosenActionId|gcdRemaining|weaveSlot|targetHpPct|keyBuffs</c>.
    /// </summary>
    /// <param name="weaveCount"> oGCDs already weaved in this GCD window. </param>
    /// <param name="canWeave"> Whether another weave fits right now; rendered as the <c>+</c>/<c>-</c> suffix on the weave slot. </param>
    /// <param name="buffs"> Statuses the combos consulted this frame. </param>
    internal static string BuildLine(
        long unixMs, string job, string combo,
        uint original, uint chosen,
        float gcdRemaining, int weaveCount, bool canWeave, float targetHpPct,
        IEnumerable<Buff> buffs)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(MaxLineLength + 64);

        sb.Append(Prefix)
          .Append(unixMs).Append('|')
          .Append(job).Append('|')
          .Append(combo).Append('|')
          .Append(original).Append('|')
          .Append(chosen).Append('|')
          .Append(gcdRemaining.ToString("F2", inv)).Append('|')
          .Append(weaveCount).Append(canWeave ? '+' : '-').Append('|')
          .Append(targetHpPct.ToString("F1", inv)).Append('|');

        // Everything up to here is what the ffxivdb join needs; the buff list is
        // the only part allowed to be cut short.
        var written = 0;
        var truncated = false;

        using (var e = buffs.GetEnumerator())
        {
            while (e.MoveNext())
            {
                if (written >= MaxBuffs)
                {
                    truncated = true;
                    break;
                }

                var buff = e.Current;
                var mark = sb.Length;

                if (written > 0)
                    sb.Append(';');
                if (!buff.OnPlayer)
                    sb.Append('t');
                sb.Append(buff.StatusId).Append(':');
                if (buff.Remaining is { } remaining)
                    sb.Append(remaining.ToString("F1", inv));
                else
                    sb.Append('-');

                // Cut on a whole entry, and leave room for the truncation marker.
                if (sb.Length > MaxLineLength - 1)
                {
                    sb.Length = mark;
                    truncated = true;
                    break;
                }

                written++;
            }
        }

        if (truncated)
            sb.Append('~');

        return sb.ToString();
    }
}
